using System;
using System.IO;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class Voxelizer : IDisposable
    {
        public static readonly bool HAS_CONSERVATIVE_RASTER = (Helper.IsExtensionsAvailable("GL_NV_conservative_raster"));
        public static readonly bool HAS_ATOMIC_FP16_VECTOR = (Helper.IsExtensionsAvailable("GL_NV_shader_atomic_fp16_vector"));
        public static readonly bool TAKE_FAST_GEOMETRY_SHADER_PATH = (Helper.IsExtensionsAvailable("GL_NV_geometry_shader_passthrough") && Helper.IsExtensionsAvailable("GL_NV_viewport_swizzle"));

        public unsafe Vector3 GridMin
        {
            get => gpuVoxelizerData.GridMin;

            set
            {
                gpuVoxelizerData.GridMin = Vector3.ComponentMin(value, gpuVoxelizerData.GridMax - new Vector3(0.1f));
                gpuVoxelizerData.OrthoProjection = Matrix4.CreateOrthographicOffCenter(gpuVoxelizerData.GridMin.X, gpuVoxelizerData.GridMax.X, gpuVoxelizerData.GridMin.Y, gpuVoxelizerData.GridMax.Y, gpuVoxelizerData.GridMax.Z, gpuVoxelizerData.GridMin.Z);
                voxelizerDataBuffer.UploadElements(gpuVoxelizerData);
            }
        }
        public unsafe Vector3 GridMax
        {
            get => gpuVoxelizerData.GridMax;

            set
            {
                gpuVoxelizerData.GridMax = Vector3.ComponentMax(value, gpuVoxelizerData.GridMin + new Vector3(0.1f));
                gpuVoxelizerData.OrthoProjection = Matrix4.CreateOrthographicOffCenter(gpuVoxelizerData.GridMin.X, gpuVoxelizerData.GridMax.X, gpuVoxelizerData.GridMin.Y, gpuVoxelizerData.GridMax.Y, gpuVoxelizerData.GridMax.Z, gpuVoxelizerData.GridMin.Z);
                voxelizerDataBuffer.UploadElements(gpuVoxelizerData);
            }
        }

        private float _debugStepMultiplier;
        public float DebugStepMultiplier
        {
            get => _debugStepMultiplier;

            set
            {
                _debugStepMultiplier = value;
                visualizeDebugProgram.Upload(0, _debugStepMultiplier);
            }
        }

        private float _debugConeAngle;
        public float DebugConeAngle
        {
            get => _debugConeAngle;

            set
            {
                _debugConeAngle = value;
                visualizeDebugProgram.Upload(1, _debugConeAngle);
            }
        }

        /// <summary>
        /// GL_NV_conservative_raster must be available for this to have an effect
        /// </summary>
        public bool IsConservativeRasterization;

        public Texture ResultVoxelsAlbedo;
        private readonly Texture[] intermediateResultRbg; // only used if no support for GL_NV_shader_atomic_fp16_vector
        private readonly ShaderProgram mergeIntermediatesProgram; // only used if no support for GL_NV_shader_atomic_fp16_vector
        private readonly ShaderProgram clearTexturesProgram;
        private readonly ShaderProgram voxelizeProgram;
        private readonly ShaderProgram mipmapProgram;
        private readonly ShaderProgram visualizeDebugProgram;
        public readonly TypedBuffer<GpuVoxelizerData> voxelizerDataBuffer;
        private GpuVoxelizerData gpuVoxelizerData;

        private readonly Framebuffer fboNoAttachments;
        public unsafe Voxelizer(int width, int height, int depth, Vector3 gridMin, Vector3 gridMax, float debugConeAngle = 0.0f, float debugStepMultiplier = 0.4f)
        {
            
            {
                Dictionary<string, string> takeFastGeometryShaderInsertion = new Dictionary<string, string>();
                takeFastGeometryShaderInsertion.Add("TAKE_FAST_GEOMETRY_SHADER_PATH", TAKE_FAST_GEOMETRY_SHADER_PATH ? "1" : "0");
            
                Dictionary<string, string> takeAtomicFP16PathInsertion = new Dictionary<string, string>();
                takeAtomicFP16PathInsertion.Add("TAKE_ATOMIC_FP16_PATH", HAS_ATOMIC_FP16_VECTOR ? "1" : "0");

                List<Shader> voxelizeProgramShaders = new List<Shader>()
                {
                    new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/VXGI/Voxelize/Voxelize/vertex.glsl"), takeFastGeometryShaderInsertion),
                    new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/VXGI/Voxelize/Voxelize/fragment.glsl"), takeAtomicFP16PathInsertion)
                };
                if (TAKE_FAST_GEOMETRY_SHADER_PATH)
                {
                    voxelizeProgramShaders.Add(new Shader(ShaderType.GeometryShader, File.ReadAllText("res/shaders/VXGI/Voxelize/Voxelize/geometry.glsl")));
                }

                voxelizeProgram = new ShaderProgram(voxelizeProgramShaders.ToArray());
                
                clearTexturesProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/VXGI/Voxelize/Clear/compute.glsl"), takeAtomicFP16PathInsertion));
            }

            mipmapProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/VXGI/Voxelize/Mipmap/compute.glsl")));
            visualizeDebugProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/VXGI/Voxelize/DebugVisualization/compute.glsl")));
            if (!HAS_ATOMIC_FP16_VECTOR)
            {
                intermediateResultRbg = new Texture[3];
                mergeIntermediatesProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/VXGI/Voxelize/MergeIntermediates/compute.glsl")));
            }

            voxelizerDataBuffer = new TypedBuffer<GpuVoxelizerData>();
            voxelizerDataBuffer.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);
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
            ResultVoxelsAlbedo.BindToImageUnit(0, 0, true, 0, TextureAccess.ReadWrite, ResultVoxelsAlbedo.SizedInternalFormat);
            if (!HAS_ATOMIC_FP16_VECTOR)
            {
                intermediateResultRbg[0].BindToImageUnit(1, 0, true, 0, TextureAccess.ReadWrite, intermediateResultRbg[0].SizedInternalFormat);
                intermediateResultRbg[1].BindToImageUnit(2, 0, true, 0, TextureAccess.ReadWrite, intermediateResultRbg[1].SizedInternalFormat);
                intermediateResultRbg[2].BindToImageUnit(3, 0, true, 0, TextureAccess.ReadWrite, intermediateResultRbg[2].SizedInternalFormat);
            }

            clearTexturesProgram.Use();
            GL.DispatchCompute((ResultVoxelsAlbedo.Width + 4 - 1) / 4, (ResultVoxelsAlbedo.Height + 4 - 1) / 4, (ResultVoxelsAlbedo.Depth + 4 - 1) / 4);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
        }

        private void Voxelize(ModelSystem modelSystem)
        {
            fboNoAttachments.Bind();

            if (HAS_CONSERVATIVE_RASTER && IsConservativeRasterization)
            {
                GL.Enable((EnableCap)All.ConservativeRasterizationNv);
            }

            if (TAKE_FAST_GEOMETRY_SHADER_PATH)
            {
                Span<Vector4> viewports = stackalloc Vector4[3];
                viewports[0] = new Vector4(0.0f, 0.0f, ResultVoxelsAlbedo.Width, ResultVoxelsAlbedo.Height);
                viewports[1] = new Vector4(0.0f, 0.0f, ResultVoxelsAlbedo.Width, ResultVoxelsAlbedo.Height);
                viewports[2] = new Vector4(0.0f, 0.0f, ResultVoxelsAlbedo.Width, ResultVoxelsAlbedo.Height);
                GL.ViewportArray(0, viewports.Length, ref viewports[0].X);

                GL.NV.ViewportSwizzle(1, All.ViewportSwizzlePositiveXNv, All.ViewportSwizzlePositiveZNv, All.ViewportSwizzlePositiveYNv, All.ViewportSwizzlePositiveWNv); // xyzw -> xzyw
                GL.NV.ViewportSwizzle(2, All.ViewportSwizzlePositiveZNv, All.ViewportSwizzlePositiveYNv, All.ViewportSwizzlePositiveXNv, All.ViewportSwizzlePositiveWNv); // xyzw -> zyxw
            }

            Helper.SetDepthConvention(Helper.DepthConvention.NegativeOneToOne);
            GL.Viewport(0, 0, ResultVoxelsAlbedo.Width, ResultVoxelsAlbedo.Height);
            GL.Disable(EnableCap.CullFace);

            ResultVoxelsAlbedo.BindToImageUnit(0, 0, true, 0, TextureAccess.ReadWrite, ResultVoxelsAlbedo.SizedInternalFormat);
            if (!HAS_ATOMIC_FP16_VECTOR)
            {
                intermediateResultRbg[0].BindToImageUnit(1, 0, true, 0, TextureAccess.ReadWrite, SizedInternalFormat.R32ui);
                intermediateResultRbg[1].BindToImageUnit(2, 0, true, 0, TextureAccess.ReadWrite, SizedInternalFormat.R32ui);
                intermediateResultRbg[2].BindToImageUnit(3, 0, true, 0, TextureAccess.ReadWrite, SizedInternalFormat.R32ui);
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

            if (HAS_CONSERVATIVE_RASTER && IsConservativeRasterization)
            {
                GL.Disable((EnableCap)All.ConservativeRasterizationNv);
            }

            if (!HAS_ATOMIC_FP16_VECTOR)
            {
                MergeIntermediateTextures();
            }
        }

        private void MergeIntermediateTextures()
        {
            ResultVoxelsAlbedo.BindToImageUnit(0, 0, true, 0, TextureAccess.ReadWrite, ResultVoxelsAlbedo.SizedInternalFormat);

            intermediateResultRbg[0].BindToUnit(0);
            intermediateResultRbg[1].BindToUnit(1);
            intermediateResultRbg[2].BindToUnit(2);

            mergeIntermediatesProgram.Use();
            GL.DispatchCompute((ResultVoxelsAlbedo.Width + 4 - 1) / 4, (ResultVoxelsAlbedo.Height + 4 - 1) / 4, (ResultVoxelsAlbedo.Depth + 4 - 1) / 4);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        private void Mipmap()
        {
            ResultVoxelsAlbedo.BindToUnit(0);
            mipmapProgram.Use();

            int levels = Texture.GetMaxMipmapLevel(ResultVoxelsAlbedo.Width, ResultVoxelsAlbedo.Height, ResultVoxelsAlbedo.Depth);
            for (int i = 1; i < levels; i++)
            {
                ResultVoxelsAlbedo.BindToImageUnit(0, i, true, 0, TextureAccess.WriteOnly, ResultVoxelsAlbedo.SizedInternalFormat);

                Vector3i size = Texture.GetMipMapLevelSize(ResultVoxelsAlbedo.Width, ResultVoxelsAlbedo.Height, ResultVoxelsAlbedo.Depth, i);

                mipmapProgram.Upload(0, i - 1);
                GL.DispatchCompute((size.X + 4 - 1) / 4, (size.Y + 4 - 1) / 4, (size.Z + 4 - 1) / 4);
                GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
            }
        }

        public void DebugRender(Texture debugResult)
        {
            debugResult.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, debugResult.SizedInternalFormat);
            ResultVoxelsAlbedo.BindToUnit(0);
            visualizeDebugProgram.Use();
            GL.DispatchCompute((debugResult.Width + 8 - 1) / 8, (debugResult.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height, int depth)
        {
            if (ResultVoxelsAlbedo != null) ResultVoxelsAlbedo.Dispose();
            ResultVoxelsAlbedo = new Texture(TextureTarget3d.Texture3D);
            ResultVoxelsAlbedo.SetFilter(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
            ResultVoxelsAlbedo.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            ResultVoxelsAlbedo.SetAnisotropy(16.0f);
            ResultVoxelsAlbedo.ImmutableAllocate(width, height, depth, SizedInternalFormat.Rgba16f, Texture.GetMaxMipmapLevel(width, height, depth));

            if (!HAS_ATOMIC_FP16_VECTOR)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (intermediateResultRbg[i] != null) intermediateResultRbg[i].Dispose();
                    intermediateResultRbg[i] = new Texture(TextureTarget3d.Texture3D);
                    intermediateResultRbg[i].SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
                    intermediateResultRbg[i].ImmutableAllocate(width, height, depth, SizedInternalFormat.R32ui);
                }
            }

            fboNoAttachments.SetParamater(FramebufferDefaultParameter.FramebufferDefaultWidth, width);
            fboNoAttachments.SetParamater(FramebufferDefaultParameter.FramebufferDefaultHeight, height);
        }

        public void Dispose()
        {
            ResultVoxelsAlbedo.Dispose();
            if (!HAS_ATOMIC_FP16_VECTOR)
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
