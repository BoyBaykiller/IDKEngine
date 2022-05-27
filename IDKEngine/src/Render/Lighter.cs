using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class Lighter
    {
        public const int GLSL_MAX_UBO_LIGHT_COUNT = 64; // used in shader and client code - keep in sync!

        private static readonly ShaderProgram shaderProgram = new ShaderProgram(
            new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Fordward/Light/vertex.glsl")),
            new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Fordward/Light/fragment.glsl")));


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
        private static VAO vao;
        public unsafe Lighter(int latitudes, int longitudes)
        {
            Lights = new GLSLLight[GLSL_MAX_UBO_LIGHT_COUNT];

            bufferObject = new BufferObject();
            bufferObject.ImmutableAllocate(Lights.Length * sizeof(GLSLLight) + sizeof(int), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            bufferObject.BindBufferRange(BufferRangeTarget.UniformBuffer, 3, 0, bufferObject.Size);

            ObjectFactory.Vertex[] vertecis = ObjectFactory.GenerateSmoothSphere(1.0f, latitudes, longitudes);
            BufferObject vbo = new BufferObject();
            vbo.ImmutableAllocate(vertecis.Length * sizeof(ObjectFactory.Vertex), vertecis, BufferStorageFlags.DynamicStorageBit);

            uint[] indicis = ObjectFactory.GenerateSmoothSphereIndicis((uint)latitudes, (uint)longitudes);
            BufferObject ebo = new BufferObject();
            ebo.ImmutableAllocate(indicis.Length * sizeof(uint), indicis, BufferStorageFlags.DynamicStorageBit);

            vao = new VAO();
            vao.SetElementBuffer(ebo);
            vao.AddSourceBuffer(vbo, 0, sizeof(ObjectFactory.Vertex));
            vao.SetAttribFormat(0, 0, 3, VertexAttribType.Float, 0 * sizeof(float)); // Positions
            //vao.SetAttribFormat(0, 1, 2, VertexAttribType.Float, 3 * sizeof(float)); // TexCoord
            //vao.SetAttribFormat(0, 2, 3, VertexAttribType.Float, 5 * sizeof(float)); // Normals

            IndicisCount = indicis.Length;
        }

        public void Draw()
        {
            shaderProgram.Use();
            vao.Bind();
            GL.DrawElementsInstanced(PrimitiveType.Triangles, IndicisCount, DrawElementsType.UnsignedInt, IntPtr.Zero, Count);
        }

        public unsafe void Add(GLSLLight[] lights)
        {
            Debug.Assert(Count + lights.Length <= GLSL_MAX_UBO_LIGHT_COUNT);

            bufferObject.SubData(Count * sizeof(GLSLLight), lights.Length * sizeof(GLSLLight), lights);
            Array.Copy(lights, 0, Lights, Count, lights.Length);
            
            Count += lights.Length;
        }

        public unsafe void RemoveAt(int index)
        {
            Debug.Assert(index >= 0 && index < Count);
            Debug.Assert(Count - 1 >= 0);
            
            if (index == Count - 1)
            {
                Count--;
                return;
            }

            Array.Copy(Lights, index + 1, Lights, index, Count - index);
            bufferObject.SubData(index * sizeof(GLSLLight), (Count - index) * sizeof(GLSLLight), Lights);
            Count--;
        }

        public delegate void FuncUploadLight(ref GLSLLight light);
        /// <summary>
        /// Synchronizes buffer with local <see cref="Lights"/> and conditionally applies function over all elements
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="func"></param>
        public unsafe void UpdateLightBuffer(int start, int end, FuncUploadLight func = null)
        {
            Debug.Assert(start >= 0 && end <= Count);

            if (func != null)
            {
                for (int i = start; i < end; i++)
                    func(ref Lights[i]);
            }

            fixed (void* ptr = &Lights[start])
            {
                bufferObject.SubData(start * sizeof(GLSLLight), (end - start) * sizeof(GLSLLight), (IntPtr)ptr);
            }
        }
    }
}
