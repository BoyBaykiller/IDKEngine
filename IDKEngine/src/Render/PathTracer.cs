using System;
using System.IO;
using IDKEngine.Render.Objects;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render
{
    class PathTracer
    {
        public readonly Texture Result;
        private static readonly ShaderProgram shaderProgram = new ShaderProgram(
            new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PathTracing/compute.glsl")));

        public Texture EnvironmentMap;
        public ModelSystem ModelSystem;
        public readonly BVH BVH;
        public unsafe PathTracer(BVH bvh, ModelSystem modelSystem, Texture environmentMap, int width, int height)
        {
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba32f);

            EnvironmentMap = environmentMap;
            ModelSystem = modelSystem;
            BVH = bvh;
        }

        private int thisRenderNumFrame = 0;
        public void Render()
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba32f);
            EnvironmentMap.BindToUnit(0);

            BVH.BVHBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 1, 0, BVH.BVHBuffer.Size);
            ModelSystem.MeshBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 2, 0, ModelSystem.MeshBuffer.Size);
            BVH.VertexBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 3, 0, BVH.VertexBuffer.Size);
            ModelSystem.ElementBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 4, 0, ModelSystem.ElementBuffer.Size);
            ModelSystem.MaterialBuffer.BindBufferRange(BufferRangeTarget.UniformBuffer, 1, 0, ModelSystem.MaterialBuffer.Size);

            shaderProgram.Use();
            shaderProgram.Upload(0, thisRenderNumFrame++);
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 4 - 1) / 4, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void ResetRenderer()
        {
            thisRenderNumFrame = 0;
        }

        public void SetSize(int width, int height)
        {
            Result.MutableAllocate(width, height, 1, Result.PixelInternalFormat);
            ResetRenderer();
        }
    }
}
