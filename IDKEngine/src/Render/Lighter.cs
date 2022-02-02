using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class Lighter
    {
        public const int GLSL_MAX_UBO_LIGHT_COUNT = 128;

        private static readonly ShaderProgram shaderProgram = new ShaderProgram(
            new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Fordward/Light/vertex.glsl")),
            new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Fordward/Light/fragment.glsl")));
        private readonly BufferObject bufferObject;

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

        public GLSLLight this[int index]
        {
            get
            {
                Debug.Assert(index >= 0 && index < Count);
                return lights[index];
            }
        }

        private static VAO vao;
        public readonly int IndicisCount;
        private readonly GLSLLight[] lights = new GLSLLight[GLSL_MAX_UBO_LIGHT_COUNT];
        public Lighter(int latitudes, int longitudes)
        {
            bufferObject = new BufferObject();
            bufferObject.ImmutableAllocate(lights.Length * Unsafe.SizeOf<GLSLLight>() + sizeof(int), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            bufferObject.BindBufferRange(BufferRangeTarget.UniformBuffer, 3, 0, bufferObject.Size);

            ObjectFactory.Vertex[] vertecis = ObjectFactory.GenerateSmoothSphere(1.0f, latitudes, longitudes);
            BufferObject vbo = new BufferObject();
            vbo.ImmutableAllocate(vertecis.Length * Unsafe.SizeOf<ObjectFactory.Vertex>(), vertecis, BufferStorageFlags.DynamicStorageBit);

            uint[] indicis = ObjectFactory.GenerateSmoothSphereIndicis((uint)latitudes, (uint)longitudes);
            BufferObject ebo = new BufferObject();
            ebo.ImmutableAllocate(indicis.Length * sizeof(uint), indicis, BufferStorageFlags.DynamicStorageBit);

            vao = new VAO();
            vao.SetElementBuffer(ebo);
            vao.AddSourceBuffer(vbo, 0, Unsafe.SizeOf<ObjectFactory.Vertex>());
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

        public void Add(GLSLLight[] lights)
        {
            Debug.Assert(Count + lights.Length <= GLSL_MAX_UBO_LIGHT_COUNT);

            bufferObject.SubData(Count * Unsafe.SizeOf<GLSLLight>(), lights.Length * Unsafe.SizeOf<GLSLLight>(), lights);
            Array.Copy(lights, 0, this.lights, Count, lights.Length);
            
            Count += lights.Length;
        }

        public void RemoveAt(int index)
        {
            Debug.Assert(index >= 0 && index < Count);
            Debug.Assert(Count - 1 >= 0);
            
            if (index == Count - 1)
            {
                Count--;
                return;
            }

            Array.Copy(lights, index + 1, lights, index, Count - index);
            bufferObject.SubData(index * Unsafe.SizeOf<GLSLLight>(), (Count - index) * Unsafe.SizeOf<GLSLLight>(), lights);
            Count--;
        }

        public delegate void FuncUploadLight(ref GLSLLight light);
        /// <summary>
        /// Applies a function over the specefied range on <see cref="Lights"/>
        /// </summary>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <param name="func"></param>
        public unsafe void Upload(int start, int count, FuncUploadLight func)
        {
            Debug.Assert(start >= 0 && (start + count) <= Count);

            for (int i = start; i < start + count; i++)
                func(ref lights[i]);

            fixed (void* ptr = &lights[start])
            {
                bufferObject.SubData(start * sizeof(GLSLLight), count * sizeof(GLSLLight), (IntPtr)ptr);
            }
        }

    }
}
