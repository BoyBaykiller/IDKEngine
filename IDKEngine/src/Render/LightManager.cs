using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class LightManager : IDisposable
    {
        public const int GLSL_MAX_UBO_LIGHT_COUNT = 256; // used in shader and client code - keep in sync!

        public struct HitInfo
        {
            public float T;
            public int LightID;
        }

        private int _count;
        public int Count
        {
            private set
            {
                _count = value;
                bufferObject.SubData(bufferObject.Size - sizeof(int), sizeof(int), Count);
            }

            get => _count;
        }

        public readonly int IndicisCount;
        public readonly GLSLLight[] Lights;
        private readonly BufferObject bufferObject;
        private readonly ShaderProgram shaderProgram;
        private readonly VAO vao;
        public unsafe LightManager(int latitudes, int longitudes)
        {
            Lights = new GLSLLight[GLSL_MAX_UBO_LIGHT_COUNT];

            shaderProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Light/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Light/fragment.glsl")));

            bufferObject = new BufferObject();
            bufferObject.ImmutableAllocate(Lights.Length * sizeof(GLSLLight) + sizeof(int), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            bufferObject.BindBufferBase(BufferRangeTarget.UniformBuffer, 2);

            Span<ObjectFactory.Vertex> vertecis = ObjectFactory.GenerateSmoothSphere(1.0f, latitudes, longitudes);
            BufferObject vbo = new BufferObject();
            fixed (ObjectFactory.Vertex* ptr = &vertecis[0])
            {
                vbo.ImmutableAllocate(vertecis.Length * sizeof(ObjectFactory.Vertex), (IntPtr)ptr, BufferStorageFlags.None);
            }

            Span<uint> indicis = ObjectFactory.GenerateSmoothSphereIndicis((uint)latitudes, (uint)longitudes);
            BufferObject ebo = new BufferObject();
            fixed (uint* ptr = &indicis[0])
            {
                ebo.ImmutableAllocate(indicis.Length * sizeof(uint), (IntPtr)ptr, BufferStorageFlags.None);
            }

            vao = new VAO();
            vao.SetElementBuffer(ebo);
            vao.AddSourceBuffer(vbo, 0, sizeof(ObjectFactory.Vertex));
            vao.SetAttribFormat(0, 0, 3, VertexAttribType.Float, 0 * sizeof(float)); // Positions
            //vao.SetAttribFormat(0, 1, 2, VertexAttribType.Float, 3 * sizeof(float)); // TexCoord

            IndicisCount = indicis.Length;
        }

        public void Draw()
        {
            shaderProgram.Use();
            vao.Bind();
            GL.DrawElementsInstanced(PrimitiveType.Triangles, IndicisCount, DrawElementsType.UnsignedInt, IntPtr.Zero, Count);
        }

        public bool Add(in GLSLLight light)
        {
            if (Count == GLSL_MAX_UBO_LIGHT_COUNT)
            {
                return false;
            }

            Lights[Count++] = light;
            UpdateLightBuffer(Count - 1, 1);

            return true;
        }

        public unsafe void RemoveAt(int index)
        {
            Debug.Assert(index >= 0 && index < Count);
            
            Count--;
            if (Count == 0)
            {
                return;
            }

            Lights[index] = Lights[Count];
            UpdateLightBuffer(index, 1);
        }

        public unsafe void UpdateLightBuffer(int start, int count)
        {
            if (count == 0) return;
            fixed (void* ptr = &Lights[start])
            {
                bufferObject.SubData(start * sizeof(GLSLLight), count * sizeof(GLSLLight), (IntPtr)ptr);
            }
        }

        public bool Intersect(Ray ray, out HitInfo hitInfo)
        {
            hitInfo = new HitInfo();
            hitInfo.T = float.MaxValue;

            for (int i = 0; i < Count; i++)
            {
                ref readonly GLSLLight light = ref Lights[i];
                if (MyMath.RaySphereIntersect(ray, light, out float min, out float max) && min > 0.0f && max < hitInfo.T)
                {
                    hitInfo.T = min;
                    hitInfo.LightID = i;
                }
            }

            return hitInfo.T != float.MaxValue;
        }

        public void Dispose()
        {
            bufferObject.Dispose();
            shaderProgram.Dispose();
            vao.Dispose();
        }
    }
}
