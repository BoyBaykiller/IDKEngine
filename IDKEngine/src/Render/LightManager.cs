using System;
using System.IO;
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
        public readonly Light[] Lights;
        
        private readonly BufferObject bufferObject;
        private readonly ShaderProgram shaderProgram;
        private readonly VAO vao;
        private readonly PointShadowManager pointShadowManager;
        public unsafe LightManager(int latitudes, int longitudes)
        {
            Lights = new Light[GLSL_MAX_UBO_LIGHT_COUNT];

            shaderProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Light/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Light/fragment.glsl")));

            bufferObject = new BufferObject();
            bufferObject.ImmutableAllocate(Lights.Length * sizeof(GLSLLight) + sizeof(int), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            bufferObject.BindBufferBase(BufferRangeTarget.UniformBuffer, 2);

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
                Light light = Lights[i];
                if (light.HasPointShadow())
                {
                    pointShadowManager.PointShadows[light.GLSLLight.PointShadowIndex].Position = light.GLSLLight.Position;
                }
            }
            pointShadowManager.RenderShadowMaps(modelSystem);
        }

        public bool Add(Light light)
        {
            if (Count == GLSL_MAX_UBO_LIGHT_COUNT)
            {
                return false;
            }

            Lights[Count++] = light;
            UpdateLightBuffer(Count - 1);

            return true;
        }

        public void RemoveAt(int index)
        {
            Count--;
            if (Count == 0)
            {
                Logger.Log(Logger.LogLevel.Warn, $"There is no Light at index {index} to remove. Total light count is 0");
                return;
            }

            Light light = Lights[index];
            if (light.HasPointShadow())
            {
                pointShadowManager.RemoveAt(light.GLSLLight.PointShadowIndex);
            }

            Lights[index] = Lights[Count];

            UpdateLightBuffer(index);
        }

        public bool SetPointLight(PointShadow pointShadow, int lightIndex)
        {
            Light light = Lights[lightIndex];
            if (light.HasPointShadow())
            {
                Logger.Log(Logger.LogLevel.Info, $"Light at index {lightIndex} already has a PointShadow assigned. To assign a new PointShadow you must remove the old one first by calling {nameof(DeletePointLight)}");
                return false;
            }

            if (pointShadowManager.TryAdd(pointShadow, out int pointShadowIndex))
            {
                Lights[lightIndex].GLSLLight.PointShadowIndex = pointShadowIndex;
                UpdateLightBuffer(lightIndex);
                return true;
            }
            return false;
        }

        public void DeletePointLight(int lightIndex)
        {
            Light light = Lights[lightIndex];
            if (!light.HasPointShadow())
            {
                Logger.Log(Logger.LogLevel.Info, $"Can not delete PointShadow of Light as it has none assigned");
                return;
            }

            pointShadowManager.RemoveAt(light.GLSLLight.PointShadowIndex);
            light.GLSLLight.PointShadowIndex = -1;
            UpdateLightBuffer(lightIndex);
        }

        public unsafe void UpdateLightBuffer(int start)
        {
            bufferObject.SubData(start * sizeof(GLSLLight), sizeof(GLSLLight), Lights[start].GLSLLight);
        }

        public bool Intersect(in Ray ray, out HitInfo hitInfo)
        {
            hitInfo = new HitInfo();
            hitInfo.T = float.MaxValue;

            for (int i = 0; i < Count; i++)
            {
                Light light = Lights[i];
                if (MyMath.RaySphereIntersect(ray, light.GLSLLight, out float min, out float max) && min > 0.0f && max < hitInfo.T)
                {
                    hitInfo.T = min;
                    hitInfo.LightID = i;
                }
            }

            return hitInfo.T != float.MaxValue;
        }

        public void Dispose()
        {
            pointShadowManager.Dispose();
            bufferObject.Dispose();
            shaderProgram.Dispose();
            vao.Dispose();
        }
    }
}
