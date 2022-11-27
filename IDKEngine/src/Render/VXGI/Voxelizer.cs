using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class Voxelizer : IDisposable
    {
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
                debugProgram.Upload(0, _debugSteps);
            }
        }

        private int _debugLod;
        public int DebugLod
        {
            get => _debugLod;

            set
            {
                _debugLod = value;
                debugProgram.Upload(1, _debugLod);
            }
        }


        public Texture ResultVoxelAlbedo;
        private readonly ShaderProgram voxelizeProgram;
        private readonly ShaderProgram debugProgram;
        private readonly ShaderProgram clearFragCounterProgram;
        private readonly BufferObject bufferObject;
        private Texture fragCounterTexture;
        private GLSLVXGIData glslVxgiData;
        public unsafe Voxelizer(int width, int height, int depth, Vector3 gridMin, Vector3 gridMax, int debugLod = 0, int debugSteps = 1000)
        {
            string geoShaderSrc = File.ReadAllText("res/shaders/Voxelize/geometry.glsl");

            voxelizeProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Voxelize/vertex.glsl")),
                new Shader(ShaderType.GeometryShader, geoShaderSrc),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Voxelize/fragment.glsl")));

            debugProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Voxelize/Visualization/compute.glsl")));
            clearFragCounterProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Voxelize/Clear/compute.glsl")));

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

            ClearTextures();

            GL.Viewport(0, 0, viewportSize.X, viewportSize.Y);
            GL.ColorMask(false, false, false, false);
            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

            ResultVoxelAlbedo.BindToImageUnit(0, 0, true, 0, TextureAccess.ReadWrite, ResultVoxelAlbedo.SizedInternalFormat);
            fragCounterTexture.BindToImageUnit(1, 0, true, 0, TextureAccess.ReadWrite, fragCounterTexture.SizedInternalFormat);

            voxelizeProgram.Upload(0, new Vector2(1.0f) / viewportSize);
            voxelizeProgram.Use();
            //timerQuery.Begin();
            modelSystem.Draw();
            //timerQuery.End();
            //Console.WriteLine(timerQuery.MeasuredMilliseconds);

            //Result.GenerateMipmap(); // 3d mipmap genertion is done on cpu on nvidia. add compute bases mipmap

            //unsafe
            //{
            //    uint* pixels = Helper.Malloc<uint>(ResultVoxelAlbedo.Width * ResultVoxelAlbedo.Height * ResultVoxelAlbedo.Depth);
            //    fragCounterTexture.GetImageData(PixelFormat.RedInteger, PixelType.UnsignedInt, (nint)pixels, ResultVoxelAlbedo.Width * ResultVoxelAlbedo.Height * ResultVoxelAlbedo.Depth * sizeof(uint));
            //    uint max = 0u;
            //    for (int i = 0; i < ResultVoxelAlbedo.Width * ResultVoxelAlbedo.Height * ResultVoxelAlbedo.Depth; i++)
            //    {
            //        max = Math.Max(max, pixels[i] & ((1 << 16) - 1));
            //    }
            //    Helper.Free(pixels);
            //    Console.WriteLine(max);
            //}

            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
        }
 
        private void ClearTextures()
        {
            ResultVoxelAlbedo.BindToImageUnit(0, 0, true, 0, TextureAccess.WriteOnly, ResultVoxelAlbedo.SizedInternalFormat);
            fragCounterTexture.BindToImageUnit(1, 0, true, 0, TextureAccess.ReadWrite, fragCounterTexture.SizedInternalFormat);

            clearFragCounterProgram.Use();
            GL.DispatchCompute((fragCounterTexture.Width + 4 - 1) / 4, (fragCounterTexture.Height + 4 - 1) / 4, (fragCounterTexture.Depth + 4 - 1) / 4);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void DebugRender(Texture debugResult)
        {
            debugResult.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, debugResult.SizedInternalFormat);
            ResultVoxelAlbedo.BindToUnit(0);
            debugProgram.Use();
            GL.DispatchCompute((debugResult.Width + 8 - 1) / 8, (debugResult.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height, int depth)
        {
            if (ResultVoxelAlbedo != null) ResultVoxelAlbedo.Dispose();
            ResultVoxelAlbedo = new Texture(TextureTarget3d.Texture3D);
            ResultVoxelAlbedo.SetFilter(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
            ResultVoxelAlbedo.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            ResultVoxelAlbedo.SetAnisotropy(4.0f);
            ResultVoxelAlbedo.ImmutableAllocate(width, height, depth, SizedInternalFormat.R32ui, 1); // Texture.GetMaxMipmapLevel(width, height, depth)

            if (fragCounterTexture != null) fragCounterTexture.Dispose();
            fragCounterTexture = new Texture(TextureTarget3d.Texture3D);
            fragCounterTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            fragCounterTexture.ImmutableAllocate(width, height, depth, SizedInternalFormat.R32ui);
        }   

        public void Dispose()
        {
            ResultVoxelAlbedo.Dispose();
            voxelizeProgram.Dispose();
            debugProgram.Dispose();
            bufferObject.Dispose();

            fragCounterTexture.Dispose();
        }
    }
}
