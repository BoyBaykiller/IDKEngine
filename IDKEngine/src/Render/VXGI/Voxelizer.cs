using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class Voxelizer : IDisposable
    {
        public static readonly bool HAS_NV_CONSERVATIVE_RASTER = Helper.IsExtensionsAvailable("GL_NV_conservative_raster");
        public static readonly bool HAS_INTEL_CONSERVATIVE_RASTER = Helper.IsExtensionsAvailable("GL_INTEL_conservative_rasterization");

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


        public Texture Result;
        private readonly ShaderProgram voxelizeProgram;
        private readonly ShaderProgram debugProgram;
        private readonly BufferObject bufferObject;
        public GLSLVXGIData glslVxgiData;
        public unsafe Voxelizer(int width, int height, int depth, Vector3 gridMin, Vector3 gridMax)
        {
            string geoShaderSrc = File.ReadAllText("res/shaders/Voxelize/geometry.glsl");
            bool HAS_CONSERVATIVE_RASTER = HAS_INTEL_CONSERVATIVE_RASTER || HAS_NV_CONSERVATIVE_RASTER;
            geoShaderSrc = geoShaderSrc.Replace("__hasConservativeRaster__", $"{(HAS_CONSERVATIVE_RASTER ? 1 : 0)}");

            voxelizeProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Voxelize/vertex.glsl")),
                new Shader(ShaderType.GeometryShader, geoShaderSrc),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Voxelize/fragment.glsl")));

            debugProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Voxelize/Visualization/compute.glsl")));

            bufferObject = new BufferObject();
            bufferObject.ImmutableAllocate(sizeof(GLSLVXGIData), glslVxgiData, BufferStorageFlags.DynamicStorageBit);
            bufferObject.BindBufferBase(BufferRangeTarget.UniformBuffer, 5);

            SetSize(width, height, depth);
            GridMin = gridMin;
            GridMax = gridMax;
        }

        public void Render(ModelSystem modelSystem, Vector2i viewportSize)
        {
            if (HAS_NV_CONSERVATIVE_RASTER) GL.Enable((EnableCap)All.ConservativeRasterizationNv);
            else if (HAS_INTEL_CONSERVATIVE_RASTER) GL.Enable((EnableCap)All.ConservativeRasterizationIntel);
            // Upload texel size for Conservative Rasterization emulation
            else voxelizeProgram.Upload(0, new Vector2(1.0f) / viewportSize);

            GL.Viewport(0, 0, viewportSize.X, viewportSize.Y);
            GL.ColorMask(false, false, false, false);
            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

            Result.Clear(PixelFormat.Rgba, PixelType.Float, new Vector4(0.0f));
            Result.BindToImageUnit(0, 0, true, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba16f);

            voxelizeProgram.Use();
            modelSystem.Draw();

            Result.GenerateMipmap();

            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);

            if (HAS_NV_CONSERVATIVE_RASTER) GL.Disable((EnableCap)All.ConservativeRasterizationNv);
            else if (HAS_INTEL_CONSERVATIVE_RASTER) GL.Disable((EnableCap)All.ConservativeRasterizationIntel);
        }

        public void DebugRender(Texture debugResult)
        {
            debugResult.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);
            Result.BindToImageUnit(1, 0, true, 0, TextureAccess.ReadOnly, SizedInternalFormat.Rgba16f);
            debugProgram.Use();
            GL.DispatchCompute((debugResult.Width + 8 - 1) / 8, (debugResult.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height, int depth)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget3d.Texture3D);
            Result.SetFilter(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.SetAnisotropy(4.0f);
            Result.ImmutableAllocate(width, height, depth, SizedInternalFormat.Rgba16f);
        }

        public void Dispose()
        {
            Result.Dispose();
            voxelizeProgram.Dispose();
            debugProgram.Dispose();
            bufferObject.Dispose();
        }
    }
}
