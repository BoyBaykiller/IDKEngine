using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class Voxelizer : IDisposable
    {
        public static readonly bool HAS_ATOMIC_FP16_VECTOR = Helper.IsExtensionsAvailable("GL_NV_shader_atomic_fp16_vector");

        public unsafe Vector3 GridMin
        {
            get => glslVxgiData.GridMin;

            set
            {
                glslVxgiData.GridMin = value;
                glslVxgiData.OrthoProjection = Matrix4.CreateOrthographicOffCenter(glslVxgiData.GridMin.X, glslVxgiData.GridMax.X, glslVxgiData.GridMin.Y, glslVxgiData.GridMax.Y, glslVxgiData.GridMax.Z, glslVxgiData.GridMin.Z);
                bufferObject.SubData(0, sizeof(GLSLVXGIData), glslVxgiData);
            }
        }
        public unsafe Vector3 GridMax
        {
            get => glslVxgiData.GridMax;

            set
            {
                glslVxgiData.GridMax = value;
                glslVxgiData.OrthoProjection = Matrix4.CreateOrthographicOffCenter(glslVxgiData.GridMin.X, glslVxgiData.GridMax.X, glslVxgiData.GridMin.Y, glslVxgiData.GridMax.Y, glslVxgiData.GridMax.Z, glslVxgiData.GridMin.Z);
                bufferObject.SubData(0, sizeof(GLSLVXGIData), glslVxgiData);
            }
        }

        private int _debugSteps;
        public int DebugSteps
        {
            get => _debugSteps;

            set
            {
                _debugSteps = value;
                visualizeDebugProgram.Upload(0, _debugSteps);
            }
        }

        private int _debugLod;
        public int DebugLod
        {
            get => _debugLod;

            set
            {
                _debugLod = value;
                visualizeDebugProgram.Upload(1, _debugLod);
            }
        }

        public Texture ResultVoxelAlbedo;
        private readonly ShaderProgram voxelizeProgram;
        private readonly ShaderProgram visualizeDebugProgram;
        private readonly ShaderProgram resetTexturesProgram;
        private readonly BufferObject bufferObject;
        private Texture fragCounterTexture;
        private GLSLVXGIData glslVxgiData;
        public unsafe Voxelizer(int width, int height, int depth, Vector3 gridMin, Vector3 gridMax, int debugLod = 0, int debugSteps = 1000)
        {
            voxelizeProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Voxelize/vertex.glsl")),
                new Shader(ShaderType.GeometryShader, File.ReadAllText("res/shaders/Voxelize/geometry.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Voxelize/fragment.glsl")));

            visualizeDebugProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Voxelize/Visualization/compute.glsl")));
            resetTexturesProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Voxelize/Clear/compute.glsl")));

            bufferObject = new BufferObject();
            bufferObject.ImmutableAllocate(sizeof(GLSLVXGIData), glslVxgiData, BufferStorageFlags.DynamicStorageBit);
            bufferObject.BindBufferBase(BufferRangeTarget.UniformBuffer, 5);

            SetSize(width, height, depth);
            GridMin = gridMin;
            GridMax = gridMax;
            DebugSteps = debugSteps;
            DebugLod = debugLod;
        }

        TimerQuery timerQuery = new TimerQuery();
        public void Render(ModelSystem modelSystem)
        {
            Vector2i viewportSize = new Vector2i(ResultVoxelAlbedo.Width, ResultVoxelAlbedo.Height);

            ResetTextures();

            GL.Viewport(0, 0, viewportSize.X, viewportSize.Y);
            GL.ColorMask(false, false, false, false);
            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

            ResultVoxelAlbedo.BindToImageUnit(0, 0, true, 0, TextureAccess.ReadWrite, HAS_ATOMIC_FP16_VECTOR ? SizedInternalFormat.Rgba16f : SizedInternalFormat.R32ui);
            fragCounterTexture.BindToImageUnit(1, 0, true, 0, TextureAccess.ReadWrite, fragCounterTexture.SizedInternalFormat);

            voxelizeProgram.Upload(0, new Vector2(1.0f) / viewportSize);
            voxelizeProgram.Use();

            //timerQuery.Begin();
            modelSystem.Draw();
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
            //timerQuery.End();
            //Console.WriteLine("Rendered into voxel grid " + timerQuery.MeasuredMilliseconds);

            //timerQuery.Begin();
            //ResultVoxelAlbedo.GenerateMipmap();
            //timerQuery.End();
            //Console.WriteLine("Generated mipmap " + timerQuery.MeasuredMilliseconds);
            //Console.WriteLine("====================");

            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
        }
 
        private void ResetTextures()
        {
            ResultVoxelAlbedo.BindToImageUnit(0, 0, true, 0, TextureAccess.WriteOnly, HAS_ATOMIC_FP16_VECTOR ? SizedInternalFormat.Rgba16f : SizedInternalFormat.R32ui);
            fragCounterTexture.BindToImageUnit(1, 0, true, 0, TextureAccess.ReadWrite, fragCounterTexture.SizedInternalFormat);

            resetTexturesProgram.Use();
            GL.DispatchCompute((fragCounterTexture.Width + 4 - 1) / 4, (fragCounterTexture.Height + 4 - 1) / 4, (fragCounterTexture.Depth + 4 - 1) / 4);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderImageAccessBarrierBit);
        }

        public void DebugRender(Texture debugResult)
        {
            debugResult.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, debugResult.SizedInternalFormat);
            ResultVoxelAlbedo.BindToUnit(0);
            visualizeDebugProgram.Use();
            GL.DispatchCompute((debugResult.Width + 8 - 1) / 8, (debugResult.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
        }

        public void SetSize(int width, int height, int depth)
        {
            if (ResultVoxelAlbedo != null) ResultVoxelAlbedo.Dispose();
            ResultVoxelAlbedo = new Texture(TextureTarget3d.Texture3D);
            ResultVoxelAlbedo.SetFilter(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
            ResultVoxelAlbedo.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            ResultVoxelAlbedo.SetAnisotropy(4.0f);
            if (HAS_ATOMIC_FP16_VECTOR)
            {
                ResultVoxelAlbedo.ImmutableAllocate(width, height, depth, SizedInternalFormat.Rgba16f, 1); // Texture.GetMaxMipmapLevel(width, height, depth)
            }
            else
            {
                ResultVoxelAlbedo.ImmutableAllocate(width, height, depth, SizedInternalFormat.Rgba8, 1); // Texture.GetMaxMipmapLevel(width, height, depth)
            }

            if (fragCounterTexture != null) fragCounterTexture.Dispose();
            fragCounterTexture = new Texture(TextureTarget3d.Texture3D);
            fragCounterTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            fragCounterTexture.ImmutableAllocate(width, height, depth, SizedInternalFormat.R32ui);
        }   

        public void Dispose()
        {
            ResultVoxelAlbedo.Dispose();
            fragCounterTexture.Dispose();

            voxelizeProgram.Dispose();
            visualizeDebugProgram.Dispose();
            resetTexturesProgram.Dispose();

            bufferObject.Dispose();
        }
    }
}
