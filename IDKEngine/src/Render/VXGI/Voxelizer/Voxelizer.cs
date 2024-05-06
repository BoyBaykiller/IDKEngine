using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Utils;
using IDKEngine.OpenGL;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class Voxelizer : IDisposable
    {
        public static readonly bool TAKE_CONSERVATIVE_RASTER_PATH = (Helper.IsExtensionsAvailable("GL_NV_conservative_raster"));
        public static readonly bool TAKE_ATOMIC_FP16_PATH = (Helper.IsExtensionsAvailable("GL_NV_shader_atomic_fp16_vector"));
        public static readonly bool TAKE_FAST_GEOMETRY_SHADER_PATH = (Helper.IsExtensionsAvailable("GL_NV_geometry_shader_passthrough") && Helper.IsExtensionsAvailable("GL_NV_viewport_swizzle"));

        public Vector3 GridMin
        {
            get => gpuVoxelizerData.GridMin;

            set
            {
                gpuVoxelizerData.GridMin = Vector3.ComponentMin(value, gpuVoxelizerData.GridMax - new Vector3(0.1f));
            }
        }
        public Vector3 GridMax
        {
            get => gpuVoxelizerData.GridMax;

            set
            {
                gpuVoxelizerData.GridMax = Vector3.ComponentMax(value, gpuVoxelizerData.GridMin + new Vector3(0.1f));
            }
        }

        public float DebugStepMultiplier;
        public float DebugConeAngle;

        /// <summary>
        /// GL_NV_conservative_raster must be available for this to have an effect
        /// </summary>
        public bool IsConservativeRasterization;

        public Texture ResultVoxels;
        private readonly Texture[] intermediateResultRbg; // only used if no support for GL_NV_shader_atomic_fp16_vector
        private readonly AbstractShaderProgram mergeIntermediatesProgram; // only used if no support for GL_NV_shader_atomic_fp16_vector
        private readonly AbstractShaderProgram clearTexturesProgram;
        private readonly AbstractShaderProgram voxelizeProgram;
        private readonly AbstractShaderProgram mipmapProgram;
        private readonly AbstractShaderProgram visualizeDebugProgram;
        public readonly TypedBuffer<GpuVoxelizerData> voxelizerDataBuffer;
        private GpuVoxelizerData gpuVoxelizerData;

        private readonly Framebuffer fboNoAttachments;
        public Voxelizer(int width, int height, int depth, Vector3 gridMin, Vector3 gridMax, float debugConeAngle = 0.0f, float debugStepMultiplier = 0.4f)
        {
            AbstractShaderProgram.ShaderInsertions[nameof(TAKE_ATOMIC_FP16_PATH)] = TAKE_ATOMIC_FP16_PATH ? "1" : "0";
            AbstractShaderProgram.ShaderInsertions[nameof(TAKE_FAST_GEOMETRY_SHADER_PATH)] = TAKE_FAST_GEOMETRY_SHADER_PATH ? "1" : "0";
            
            {
                List<AbstractShader> voxelizeProgramShaders = new List<AbstractShader>()
                {
                    new AbstractShader(ShaderType.VertexShader, "VXGI/Voxelize/Voxelize/vertex.glsl"),
                    new AbstractShader(ShaderType.FragmentShader, "VXGI/Voxelize/Voxelize/fragment.glsl")
                };
                if (TAKE_FAST_GEOMETRY_SHADER_PATH)
                {
                    voxelizeProgramShaders.Add(new AbstractShader(ShaderType.GeometryShader, "VXGI/Voxelize/Voxelize/geometry.glsl"));
                }

                voxelizeProgram = new AbstractShaderProgram(voxelizeProgramShaders.ToArray());
                
                clearTexturesProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "VXGI/Voxelize/Clear/compute.glsl"));
            }

            mipmapProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "VXGI/Voxelize/Mipmap/compute.glsl"));
            visualizeDebugProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "VXGI/Voxelize/DebugVisualization/compute.glsl"));
            if (!TAKE_ATOMIC_FP16_PATH)
            {
                intermediateResultRbg = new Texture[3];
                mergeIntermediatesProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "VXGI/Voxelize/MergeIntermediates/compute.glsl"));
            }

            voxelizerDataBuffer = new TypedBuffer<GpuVoxelizerData>();
            voxelizerDataBuffer.ImmutableAllocateElements(BufferObject.MemLocation.DeviceLocal, BufferObject.MemAccess.Synced, 1);
            voxelizerDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 5);

            fboNoAttachments = new Framebuffer();

            gpuVoxelizerData.GridMax = new Vector3(float.MaxValue);
            gpuVoxelizerData.GridMin = new Vector3(float.MinValue);

            SetSize(width, height, depth);
            GridMin = gridMin;
            GridMax = gridMax;
            DebugConeAngle = debugConeAngle;
            DebugStepMultiplier = debugStepMultiplier;
        }

        public void Render(ModelSystem modelSystem)
        {
            ClearTextures();
            Voxelize(modelSystem);
            Mipmap();
        }

        private void ClearTextures()
        {
            ResultVoxels.BindToImageUnit(0, ResultVoxels.TextureFormat, 0, true);
            if (!TAKE_ATOMIC_FP16_PATH)
            {
                intermediateResultRbg[0].BindToImageUnit(1, intermediateResultRbg[0].TextureFormat, 0, true);
                intermediateResultRbg[1].BindToImageUnit(2, intermediateResultRbg[1].TextureFormat, 0, true);
                intermediateResultRbg[2].BindToImageUnit(3, intermediateResultRbg[2].TextureFormat, 0, true);
            }

            clearTexturesProgram.Use();
            GL.DispatchCompute((ResultVoxels.Width + 4 - 1) / 4, (ResultVoxels.Height + 4 - 1) / 4, (ResultVoxels.Depth + 4 - 1) / 4);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
        }

        private void Voxelize(ModelSystem modelSystem)
        {
            voxelizerDataBuffer.UploadElements(gpuVoxelizerData);

            fboNoAttachments.Bind();

            if (TAKE_CONSERVATIVE_RASTER_PATH && IsConservativeRasterization)
            {
                GL.Enable((EnableCap)All.ConservativeRasterizationNv);
            }

            if (TAKE_FAST_GEOMETRY_SHADER_PATH)
            {
                Span<Vector4> viewports = stackalloc Vector4[3];
                viewports[0] = new Vector4(0.0f, 0.0f, ResultVoxels.Width, ResultVoxels.Height);
                viewports[1] = new Vector4(0.0f, 0.0f, ResultVoxels.Width, ResultVoxels.Height);
                viewports[2] = new Vector4(0.0f, 0.0f, ResultVoxels.Width, ResultVoxels.Height);
                GL.ViewportArray(0, viewports.Length, ref viewports[0].X);

                GL.NV.ViewportSwizzle(1, All.ViewportSwizzlePositiveXNv, All.ViewportSwizzlePositiveZNv, All.ViewportSwizzlePositiveYNv, All.ViewportSwizzlePositiveWNv); // xyzw -> xzyw
                GL.NV.ViewportSwizzle(2, All.ViewportSwizzlePositiveZNv, All.ViewportSwizzlePositiveYNv, All.ViewportSwizzlePositiveXNv, All.ViewportSwizzlePositiveWNv); // xyzw -> zyxw
            }

            Helper.SetDepthConvention(Helper.DepthConvention.NegativeOneToOne);
            GL.Viewport(0, 0, ResultVoxels.Width, ResultVoxels.Height);
            GL.Disable(EnableCap.CullFace);

            ResultVoxels.BindToImageUnit(0, ResultVoxels.TextureFormat, 0, true);
            if (!TAKE_ATOMIC_FP16_PATH)
            {
                intermediateResultRbg[0].BindToImageUnit(1, Texture.InternalFormat.R32Uint, 0, true);
                intermediateResultRbg[1].BindToImageUnit(2, Texture.InternalFormat.R32Uint, 0, true);
                intermediateResultRbg[2].BindToImageUnit(3, Texture.InternalFormat.R32Uint, 0, true);
            }

            voxelizeProgram.Use();
            if (TAKE_FAST_GEOMETRY_SHADER_PATH)
            {
                modelSystem.Draw();
            }
            else
            {
                // Instead of doing a single draw call with a standard geometry shader to select the swizzle
                // we render the scene 3 times, each time with a different swizzle. I have observed this to be slightly faster
                for (int i = 0; i < 3; i++)
                {
                    voxelizeProgram.Upload(0, i);
                    modelSystem.Draw();
                }
            }
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
            
            GL.Enable(EnableCap.CullFace);
            Helper.SetDepthConvention(Helper.DepthConvention.ZeroToOne);

            if (TAKE_CONSERVATIVE_RASTER_PATH && IsConservativeRasterization)
            {
                GL.Disable((EnableCap)All.ConservativeRasterizationNv);
            }

            if (!TAKE_ATOMIC_FP16_PATH)
            {
                MergeIntermediateTextures();
            }
        }

        private void MergeIntermediateTextures()
        {
            ResultVoxels.BindToImageUnit(0, ResultVoxels.TextureFormat, 0, true);

            intermediateResultRbg[0].BindToUnit(0);
            intermediateResultRbg[1].BindToUnit(1);
            intermediateResultRbg[2].BindToUnit(2);

            mergeIntermediatesProgram.Use();
            GL.DispatchCompute((ResultVoxels.Width + 4 - 1) / 4, (ResultVoxels.Height + 4 - 1) / 4, (ResultVoxels.Depth + 4 - 1) / 4);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        private void Mipmap()
        {
            ResultVoxels.BindToUnit(0);
            mipmapProgram.Use();

            int levels = Texture.GetMaxMipmapLevel(ResultVoxels.Width, ResultVoxels.Height, ResultVoxels.Depth);
            for (int i = 1; i < levels; i++)
            {
                ResultVoxels.BindToImageUnit(0, ResultVoxels.TextureFormat, 0, true, i);

                Vector3i size = Texture.GetMipMapLevelSize(ResultVoxels.Width, ResultVoxels.Height, ResultVoxels.Depth, i);

                mipmapProgram.Upload(0, i - 1);
                GL.DispatchCompute((size.X + 4 - 1) / 4, (size.Y + 4 - 1) / 4, (size.Z + 4 - 1) / 4);
                GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
            }
        }

        public void DebugRender(Texture debugResult)
        {
            visualizeDebugProgram.Upload(0, DebugStepMultiplier);
            visualizeDebugProgram.Upload(1, DebugConeAngle);

            debugResult.BindToImageUnit(0, debugResult.TextureFormat);
            ResultVoxels.BindToUnit(0);
            visualizeDebugProgram.Use();
            GL.DispatchCompute((debugResult.Width + 8 - 1) / 8, (debugResult.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height, int depth)
        {
            if (ResultVoxels != null) ResultVoxels.Dispose();
            ResultVoxels = new Texture(Texture.Type.Texture3D);
            ResultVoxels.SetFilter(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
            ResultVoxels.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            ResultVoxels.SetAnisotropy(16.0f);
            ResultVoxels.ImmutableAllocate(width, height, depth, Texture.InternalFormat.R16G16B16A16Float, Texture.GetMaxMipmapLevel(width, height, depth));

            if (!TAKE_ATOMIC_FP16_PATH)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (intermediateResultRbg[i] != null) intermediateResultRbg[i].Dispose();
                    intermediateResultRbg[i] = new Texture(Texture.Type.Texture3D);
                    intermediateResultRbg[i].SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
                    intermediateResultRbg[i].ImmutableAllocate(width, height, depth, Texture.InternalFormat.R32Float);
                }
            }

            fboNoAttachments.SetParamater(FramebufferDefaultParameter.FramebufferDefaultWidth, width);
            fboNoAttachments.SetParamater(FramebufferDefaultParameter.FramebufferDefaultHeight, height);
        }

        public void Dispose()
        {
            ResultVoxels.Dispose();
            if (!TAKE_ATOMIC_FP16_PATH)
            {
                for (int i = 0; i < 3; i++)
                {
                    intermediateResultRbg[i].Dispose();
                }
                mergeIntermediatesProgram.Dispose();
            }

            clearTexturesProgram.Dispose();
            voxelizeProgram.Dispose();
            mipmapProgram.Dispose();
            visualizeDebugProgram.Dispose();

            voxelizerDataBuffer.Dispose();

            fboNoAttachments.Dispose();
        }
    }
}
