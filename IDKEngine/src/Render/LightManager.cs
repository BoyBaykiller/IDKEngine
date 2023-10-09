using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;
using IDKEngine.Shapes;

namespace IDKEngine.Render
{
    class LightManager : IDisposable
    {
        public const int GPU_MAX_UBO_LIGHT_COUNT = 256; // used in shader and client code - keep in sync!

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
                lightBufferObject.SubData(lightBufferObject.Size - sizeof(int), sizeof(int), Count);
            }

            get => _count;
        }

        public readonly int IndicisCount;
        private readonly Light[] lights;
        
        private readonly BufferObject lightBufferObject;
        private readonly ShaderProgram shaderProgram;
        private readonly PointShadowManager pointShadowManager;
        private readonly VAO vao;
        public unsafe LightManager(int latitudes, int longitudes)
        {
            lights = new Light[GPU_MAX_UBO_LIGHT_COUNT];

            shaderProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Light/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Light/fragment.glsl")));

            lightBufferObject = new BufferObject();
            lightBufferObject.ImmutableAllocate(lights.Length * sizeof(GpuLight) + sizeof(int), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            lightBufferObject.BindBufferBase(BufferRangeTarget.UniformBuffer, 2);

            Span<ObjectFactory.Vertex> vertecis = ObjectFactory.GenerateSmoothSphere(1.0f, latitudes, longitudes);
            BufferObject vbo = new BufferObject();
            vbo.ImmutableAllocate(vertecis.Length * sizeof(ObjectFactory.Vertex), vertecis[0], BufferStorageFlags.None);

            Span<uint> indicis = ObjectFactory.GenerateSmoothSphereIndicis((uint)latitudes, (uint)longitudes);
            BufferObject ebo = new BufferObject();
            ebo.ImmutableAllocate(indicis.Length * sizeof(uint), indicis[0], BufferStorageFlags.None);

            vao = new VAO();
            vao.SetElementBuffer(ebo);
            vao.AddSourceBuffer(vbo, 0, sizeof(ObjectFactory.Vertex));
            vao.SetAttribFormat(0, 0, 3, VertexAttribType.Float, 0 * sizeof(float)); // Positions
            //vao.SetAttribFormat(0, 1, 2, VertexAttribType.Float, 3 * sizeof(float)); // TexCoord

            IndicisCount = indicis.Length;

            pointShadowManager = new PointShadowManager();
        }

        public void Draw()
        {
            shaderProgram.Use();
            vao.Bind();
            GL.DrawElementsInstanced(PrimitiveType.Triangles, IndicisCount, DrawElementsType.UnsignedInt, IntPtr.Zero, Count);
        }

        public void RenderShadowMaps(ModelSystem modelSystem)
        {
            for (int i = 0; i < Count; i++)
            {
                Light light = lights[i];
                if (light.HasPointShadow())
                {
                    pointShadowManager.TryGetPointShadow(light.GpuLight.PointShadowIndex, out PointShadow pointShadow);
                    pointShadow.Position = light.GpuLight.Position;
                }
            }
            pointShadowManager.RenderShadowMaps(modelSystem);
        }

        public bool AddLight(Light light)
        {
            if (Count == GPU_MAX_UBO_LIGHT_COUNT)
            {
                Logger.Log(Logger.LogLevel.Warn, $"Cannot add {nameof(Light)}. Limit of {GPU_MAX_UBO_LIGHT_COUNT} is reached");
                return false;
            }

            lights[Count++] = light;

            return true;
        }

        public void RemoveLight(int index)
        {
            if (!TryGetLight(index, out Light light))
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(Light)} {index} does not exist. Cannot remove it");
                return;
            }

            if (light.HasPointShadow())
            {
                pointShadowManager.RemovePointShadow(light.GpuLight.PointShadowIndex);
            }

            if (Count - 1 >= 0)
            {
                lights[index] = lights[Count - 1];
                Count--;
            }
        }

        public bool CreatePointShadowForLight(PointShadow pointShadow, int index)
        {
            if (!TryGetLight(index, out Light light))
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(Light)} {index} does not exist. Cannot attach {nameof(PointShadow)} to it");
                return false;
            }

            if (light.HasPointShadow())
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(Light)} {index} already has a {nameof(PointShadow)} attached. First you must remove the old one by calling {nameof(DeletePointShadowOfLight)}");
                return false;
            }

            if (pointShadowManager.TryAddPointShadow(pointShadow, out int pointShadowIndex))
            {
                lights[index].GpuLight.PointShadowIndex = pointShadowIndex;
                return true;
            }
            return false;
        }
        
        public void DeletePointShadowOfLight(int index)
        {
            if (!TryGetLight(index, out Light light))
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(Light)} {index} does not exist. Cannot detach {nameof(PointShadow)} from it");
                return;
            }

            if (!light.HasPointShadow())
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(Light)} {index} has no {nameof(PointShadow)} assigned which could be detached");
                return;
            }

            pointShadowManager.RemovePointShadow(light.GpuLight.PointShadowIndex);
            light.GpuLight.PointShadowIndex = -1;
        }

        public unsafe void UpdateBufferData()
        {
            for (int i = 0; i < Count; i++)
            {
                Light light = lights[i];
                lightBufferObject.SubData(i * sizeof(GpuLight), sizeof(GpuLight), light.GpuLight);
            }
        }

        public bool TryGetLight(int index, out Light light)
        {
            light = null;
            if (index < 0 || index >= Count) return false;

            light = lights[index];
            return true;
        }

        public PointShadow GetPointShadow(int index)
        {
            pointShadowManager.TryGetPointShadow(index, out PointShadow pointShadow);
            return pointShadow;
        }

        public bool Intersect(in Ray ray, out HitInfo hitInfo)
        {
            hitInfo = new HitInfo();
            hitInfo.T = float.MaxValue;

            for (int i = 0; i < Count; i++)
            {
                Light light = lights[i];
                if (Intersections.RayVsSphere(ray, GpuTypes.Conversions.ToSphere(light.GpuLight), out float min, out float max) && max < hitInfo.T)
                {
                    hitInfo.T = min;
                    hitInfo.LightID = i;
                }
            }

            return hitInfo.T != float.MaxValue;
        }

        public void Dispose()
        {
            vao.Dispose();
            pointShadowManager.Dispose();
            shaderProgram.Dispose();
            lightBufferObject.Dispose();
        }
    }
}
