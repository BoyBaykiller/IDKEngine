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
                vxgiDataBuffer.SubData(0, sizeof(GLSLVXGIData), glslVxgiData);
            }
        }
        public unsafe Vector3 GridMax
        {
            get => glslVxgiData.GridMax;

            set
            {
                glslVxgiData.GridMax = value;
                glslVxgiData.OrthoProjection = Matrix4.CreateOrthographicOffCenter(glslVxgiData.GridMin.X, glslVxgiData.GridMax.X, glslVxgiData.GridMin.Y, glslVxgiData.GridMax.Y, glslVxgiData.GridMax.Z, glslVxgiData.GridMin.Z);
                vxgiDataBuffer.SubData(0, sizeof(GLSLVXGIData), glslVxgiData);
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
        private Texture fragCounterTexture;
        private readonly ShaderProgram resetTexturesProgram;
        private readonly ShaderProgram preVoxelizeProgram;
        private readonly ShaderProgram voxelizeProgram;
        private readonly ShaderProgram mipmapProgram;
        private readonly ShaderProgram visualizeDebugProgram;
        private readonly BufferObject vxgiDataBuffer;
        private GLSLVXGIData glslVxgiData;
        public unsafe Voxelizer(int width, int height, int depth, Vector3 gridMin, Vector3 gridMax, int debugLod = 0, int debugSteps = 750)
        {
            resetTexturesProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Voxelize/Clear/compute.glsl")));

            preVoxelizeProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Voxelize/PreVoxelize/vertex.glsl")),
                new Shader(ShaderType.GeometryShader, File.ReadAllText("res/shaders/Voxelize/PreVoxelize/geometry.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Voxelize/PreVoxelize/fragment.glsl")));

            voxelizeProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Voxelize/vertex.glsl")),
                new Shader(ShaderType.GeometryShader, File.ReadAllText("res/shaders/Voxelize/geometry.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Voxelize/fragment.glsl")));

            mipmapProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Voxelize/Mipmap/compute.glsl")));

            visualizeDebugProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Voxelize/Visualization/compute.glsl")));

            vxgiDataBuffer = new BufferObject();
            vxgiDataBuffer.ImmutableAllocate(sizeof(GLSLVXGIData), glslVxgiData, BufferStorageFlags.DynamicStorageBit);
            vxgiDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 5);

            SetSize(width, height, depth);
            GridMin = gridMin;
            GridMax = gridMax;
            DebugSteps = debugSteps;
            DebugLod = debugLod;
        }

        public void Render(ModelSystem modelSystem)
        {
            ResetTextures();
            Voxelize(modelSystem);
            Mipmap();
        }

        private void ResetTextures()
        {
            ResultVoxelAlbedo.BindToImageUnit(0, 0, true, 0, TextureAccess.WriteOnly, ResultVoxelAlbedo.SizedInternalFormat);
            fragCounterTexture.BindToImageUnit(1, 0, true, 0, TextureAccess.WriteOnly, fragCounterTexture.SizedInternalFormat);
            
            resetTexturesProgram.Use();
            GL.DispatchCompute((fragCounterTexture.Width + 4 - 1) / 4, (fragCounterTexture.Height + 4 - 1) / 4, (fragCounterTexture.Depth + 4 - 1) / 4);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderImageAccessBarrierBit);
        }

        TimerQuery debugTimerQuery = new TimerQuery();
        private void Voxelize(ModelSystem modelSystem)
        {
            Vector2i viewportSize = new Vector2i(ResultVoxelAlbedo.Width, ResultVoxelAlbedo.Height);

            GL.Viewport(0, 0, viewportSize.X, viewportSize.Y);
            GL.ColorMask(false, false, false, false);
            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

            ResultVoxelAlbedo.BindToImageUnit(0, 0, true, 0, TextureAccess.ReadWrite, HAS_ATOMIC_FP16_VECTOR ? SizedInternalFormat.Rgba16f : SizedInternalFormat.R32ui);
            fragCounterTexture.BindToImageUnit(1, 0, true, 0, TextureAccess.ReadWrite, fragCounterTexture.SizedInternalFormat);

            //debugTimerQuery.Begin();

            preVoxelizeProgram.Upload(0, new Vector2(1.0f) / viewportSize);
            preVoxelizeProgram.Use();
            modelSystem.Draw();
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);

            voxelizeProgram.Upload(0, new Vector2(1.0f) / viewportSize);
            voxelizeProgram.Use();
            modelSystem.Draw();
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

            //debugTimerQuery.End();
            //Console.WriteLine("Rendered into voxel grid " + timerQuery.MeasuredMilliseconds);

            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
        }

        private void Mipmap()
        {
            //debugTimerQuery.Begin();

            ResultVoxelAlbedo.BindToUnit(0);
            mipmapProgram.Use();

            int levels = Texture.GetMaxMipmapLevel(ResultVoxelAlbedo.Width, ResultVoxelAlbedo.Height, ResultVoxelAlbedo.Depth);
            for (int i = 1; i < levels; i++)
            {
                ResultVoxelAlbedo.BindToImageUnit(0, i, true, 0, TextureAccess.WriteOnly, ResultVoxelAlbedo.SizedInternalFormat);

                Vector3i size = Texture.GetMipMapLevelSize(ResultVoxelAlbedo.Width, ResultVoxelAlbedo.Height, ResultVoxelAlbedo.Depth, i);

                mipmapProgram.Upload(0, i - 1);
                GL.DispatchCompute((size.X + 4 - 1) / 4, (size.Y + 4 - 1) / 4, (size.Z + 4 - 1) / 4);
                GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
            }

            //ResultVoxelAlbedo.GenerateMipmap();
            //debugTimerQuery.End();

            //Console.WriteLine("Generated mipmap " + debugTimerQuery.MeasuredMilliseconds);
            //Console.WriteLine("====================");
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

            ResultVoxelAlbedo.ImmutableAllocate(width, height, depth, HAS_ATOMIC_FP16_VECTOR ? SizedInternalFormat.Rgba16f : SizedInternalFormat.Rgba8, Texture.GetMaxMipmapLevel(width, height, depth)); // Texture.GetMaxMipmapLevel(width, height, depth)

            if (fragCounterTexture != null) fragCounterTexture.Dispose();
            fragCounterTexture = new Texture(TextureTarget3d.Texture3D);
            fragCounterTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            fragCounterTexture.ImmutableAllocate(width, height, depth, SizedInternalFormat.R32ui);
        }

        public void Dispose()
        {
            ResultVoxelAlbedo.Dispose();
            fragCounterTexture.Dispose();

            resetTexturesProgram.Dispose();
            preVoxelizeProgram.Dispose();
            voxelizeProgram.Dispose();
            mipmapProgram.Dispose();
            visualizeDebugProgram.Dispose();

            vxgiDataBuffer.Dispose();
        }
    }
}
