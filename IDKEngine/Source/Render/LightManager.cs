using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
using BBLogger;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class LightManager : IDisposable
    {
        // Light and PointShadow are in a 1-to-1 relationship.
        // Light is the owner.
        // Both reference each other.

        public const int GPU_MAX_UBO_LIGHT_COUNT = 256; // used in shader and client code - keep in sync!

        public struct RayHitInfo
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
                lightBufferObject.UploadData(lightBufferObject.Size - sizeof(int), sizeof(int), Count);
            }

            get => _count;
        }

        public Intersections.SceneVsMovingSphereSettings SceneVsSphereCollisionSettings = new Intersections.SceneVsMovingSphereSettings()
        {
            IsEnabled = true,
            TestSteps = 1,
            RecursiveSteps = 8,
            EpsilonNormalOffset = 0.001f,
        };

        public struct MovingLightsCollisionSettings
        {
            public bool IsEnabled;
            public int RecursiveSteps;
            public float EpsilonOffset;
        }

        public MovingLightsCollisionSettings MovingLightsCollisionSetting = new MovingLightsCollisionSettings()
        {
            IsEnabled = true,
            EpsilonOffset = 0.001f,
            RecursiveSteps = 8,
        };

        public readonly int IndicisCount;
        private readonly CpuLight[] lights;
        
        private readonly BBG.TypedBuffer<GpuLight> lightBufferObject;
        private readonly BBG.AbstractShaderProgram shaderProgram;
        private readonly PointShadowManager pointShadowManager;
        private readonly BBG.VAO vao;
        public unsafe LightManager()
        {
            lights = new CpuLight[GPU_MAX_UBO_LIGHT_COUNT];

            shaderProgram = new BBG.AbstractShaderProgram(
                new BBG.AbstractShader(BBG.ShaderType.Vertex, "Light/vertex.glsl"),
                new BBG.AbstractShader(BBG.ShaderType.Fragment, "Light/fragment.glsl"));

            lightBufferObject = new BBG.TypedBuffer<GpuLight>();
            lightBufferObject.ImmutableAllocate(BBG.BufferObject.MemLocation.DeviceLocal, BBG.BufferObject.MemAccess.Synced, lights.Length * sizeof(GpuLight) + sizeof(int));
            lightBufferObject.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 1);

            const int SphereLatitudes = 12, SphereLongitudes = 12;
            Span<ObjectFactory.Vertex> vertecis = ObjectFactory.GenerateSmoothSphere(1.0f, SphereLatitudes, SphereLongitudes);
            BBG.TypedBuffer<ObjectFactory.Vertex> vbo = new BBG.TypedBuffer<ObjectFactory.Vertex>();
            vbo.ImmutableAllocateElements(BBG.BufferObject.MemLocation.DeviceLocal, BBG.BufferObject.MemAccess.None, vertecis);

            Span<uint> indicis = ObjectFactory.GenerateSmoothSphereIndicis(SphereLatitudes, SphereLongitudes);
            BBG.TypedBuffer<uint> ebo = new BBG.TypedBuffer<uint>();
            ebo.ImmutableAllocateElements(BBG.BufferObject.MemLocation.DeviceLocal, BBG.BufferObject.MemAccess.None, indicis);

            vao = new BBG.VAO();
            vao.SetElementBuffer(ebo);
            vao.AddSourceBuffer(vbo, 0, sizeof(ObjectFactory.Vertex));
            vao.SetAttribFormat(0, 0, 3, VertexAttribType.Float, 0 * sizeof(float)); // Positions
            //vao.SetAttribFormat(0, 1, 2, VertexAttribType.Float, 3 * sizeof(float)); // TexCoord

            IndicisCount = indicis.Length;

            pointShadowManager = new PointShadowManager();
        }

        public void Draw()
        {
            BBG.Cmd.UseShaderProgram(shaderProgram);
            vao.Bind();
            BBG.Rendering.DrawIndexed(BBG.Rendering.Topology.Triangles, IndicisCount, BBG.Rendering.IndexType.Uint, Count);
        }

        public void RenderShadowMaps(ModelSystem modelSystem, Camera camera)
        {
            pointShadowManager.RenderShadowMaps(modelSystem, camera);
        }

        public void ComputeRayTracedShadows(int samples)
        {
            pointShadowManager.ComputeRayTracedShadowMaps(samples);
        }

        public bool AddLight(CpuLight light)
        {
            if (Count == GPU_MAX_UBO_LIGHT_COUNT)
            {
                Logger.Log(Logger.LogLevel.Warn, $"Cannot add {nameof(CpuLight)}. Limit of {GPU_MAX_UBO_LIGHT_COUNT} is reached");
                return false;
            }

            lights[Count++] = light;

            return true;
        }

        public void DeleteLight(int index)
        {
            if (!TryGetLight(index, out CpuLight light))
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(CpuLight)} {index} does not exist. Cannot delete it");
                return;
            }

            if (light.HasPointShadow())
            {
                DeletePointShadowOfLight(index);
            }

            if (Count > 0)
            {
                lights[index] = lights[--Count];

                // Correct PointShadow's LightIndex since the light moved
                CpuLight affectedLight = lights[index];
                if (affectedLight.HasPointShadow())
                {
                    pointShadowManager.TryGetPointShadow(affectedLight.GpuLight.PointShadowIndex, out CpuPointShadow pointShadow);
                    pointShadow.SetConnectedLight(index);
                }
            }
        }

        public bool CreatePointShadowForLight(CpuPointShadow pointShadow, int lightIndex)
        {
            if (!TryGetLight(lightIndex, out CpuLight light))
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(CpuLight)} {lightIndex} does not exist. Cannot attach {nameof(CpuPointShadow)} to it");
                return false;
            }

            if (light.HasPointShadow())
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(CpuLight)} {lightIndex} already has a {nameof(CpuPointShadow)} attached. First you must delete the old one by calling {nameof(DeletePointShadowOfLight)}");
                return false;
            }

            if (pointShadowManager.TryAddPointShadow(pointShadow, out int pointShadowIndex))
            {
                pointShadow.SetConnectedLight(lightIndex);
                lights[lightIndex].GpuLight.PointShadowIndex = pointShadowIndex;
                return true;
            }
            return false;
        }
        
        public void DeletePointShadowOfLight(int index)
        {
            if (!TryGetLight(index, out CpuLight light))
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(CpuLight)} {index} does not exist. Cannot detach {nameof(CpuPointShadow)} from it");
                return;
            }

            if (!light.HasPointShadow())
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(CpuLight)} {index} has no {nameof(CpuPointShadow)} assigned which could be detached");
                return;
            }

            int pointShadowIndex = light.GpuLight.PointShadowIndex;
            light.DisconnectPointShadow();
            pointShadowManager.DeletePointShadow(pointShadowIndex);

            // Correct light PointShadowIndex index since the PointShadow moved
            if (pointShadowManager.TryGetPointShadow(pointShadowIndex, out CpuPointShadow pointShadow))
            {
                if (!TryGetLight(pointShadow.GetGpuPointShadow().LightIndex, out CpuLight affectedLight))
                {
                    Logger.Log(Logger.LogLevel.Fatal, $"{nameof(CpuPointShadow)} {index} references {nameof(CpuLight)} {pointShadow.GetGpuPointShadow().LightIndex} which does not exist");
                    return;
                }

                affectedLight.GpuLight.PointShadowIndex = pointShadowIndex;
            }
        }

        public void AdvanceSimulation(float dT, ModelSystem modelSystem)
        {
            for (int i = 0; i < Count; i++)
            {
                CpuLight cpuLight = lights[i];
                cpuLight.AdvanceSimulation(dT);

                Sphere movingSphere = new Sphere(cpuLight.GpuLight.PrevPosition, cpuLight.GpuLight.Radius);
                Vector3 prevSpherePos = movingSphere.Center;
                Intersections.SceneVsMovingSphereCollisionRoutine(modelSystem, SceneVsSphereCollisionSettings, ref movingSphere, cpuLight.GpuLight.Position, (in Intersections.SceneHitInfo hitInfo) =>
                {
                    Vector3 deltaStep = cpuLight.GpuLight.Position - prevSpherePos;
                    Vector3 slidedDeltaStep = Plane.Reflect(deltaStep, hitInfo.SlidingPlane);
                    cpuLight.GpuLight.Position = movingSphere.Center + slidedDeltaStep;

                    cpuLight.Velocity = Plane.Reflect(cpuLight.Velocity, hitInfo.SlidingPlane);

                    prevSpherePos = movingSphere.Center;
                });
            }

            
            for (int i = 0; i < MovingLightsCollisionSetting.RecursiveSteps && MovingLightsCollisionSetting.IsEnabled; i++)
            {
                for (int j = 0; j < Count; j++)
                {
                    CpuLight light = lights[j];

                    float intersectionTime = float.MaxValue;
                    int bestOtherLightIndex = -1;

                    for (int k = j + 1; k < Count; k++)
                    {
                        CpuLight otherLight = lights[k];

                        if (Intersections.MovingSphereVsSphere(
                            Conversions.ToSphere(light.GpuLight), light.GpuLight.PrevPosition,
                            Conversions.ToSphere(otherLight.GpuLight), otherLight.GpuLight.PrevPosition,
                            out float t1, out float t2, out float tScale))
                        {
                            t1 /= tScale;
                            t2 /= tScale;

                            float thisIntersectionTime = t1;

                            bool invertBias = false;
                            if (thisIntersectionTime < 0.0f)
                            {
                                if (MathF.Abs(t1) > MathF.Abs(t2))
                                {
                                    invertBias = true;
                                    thisIntersectionTime = t2;
                                }
                            }

                            if (thisIntersectionTime > 1.0f)
                            {
                                // collision happens in the future
                                continue;
                            }

                            thisIntersectionTime -= (invertBias ? -MovingLightsCollisionSetting.EpsilonOffset : MovingLightsCollisionSetting.EpsilonOffset) / tScale;
                            if (thisIntersectionTime < intersectionTime)
                            {
                                intersectionTime = thisIntersectionTime;
                                bestOtherLightIndex = k;
                            }
                        }
                    }

                    if (bestOtherLightIndex != -1)
                    {
                        CpuLight otherLight = lights[bestOtherLightIndex];
                        CollisionResponse(light, otherLight, intersectionTime);
                    }
                }
            }
        }

        private static void CollisionResponse(CpuLight light, CpuLight otherLight, float intersectionTime)
        {
            {
                Vector3 newLightPosition = Vector3.Lerp(light.GpuLight.PrevPosition, light.GpuLight.Position, intersectionTime);
                Vector3 newOtherLightPosition = Vector3.Lerp(otherLight.GpuLight.PrevPosition, otherLight.GpuLight.Position, intersectionTime);

                Vector3 lightLostDisplacement = light.GpuLight.Position - newLightPosition;
                Vector3 otherLightLostDisplacement = otherLight.GpuLight.Position - newOtherLightPosition;

                // TOOD: Changing PrevPosition here makes gpu velocity values wrong, maybe fix?
                light.GpuLight.Position = newLightPosition;
                light.GpuLight.PrevPosition = light.GpuLight.Position;

                otherLight.GpuLight.Position = newOtherLightPosition;
                otherLight.GpuLight.PrevPosition = otherLight.GpuLight.Position;

                light.GpuLight.Position += lightLostDisplacement + otherLightLostDisplacement;
                otherLight.GpuLight.Position += otherLightLostDisplacement + lightLostDisplacement;
            }

            {
                // Source: https://physics.stackexchange.com/questions/296767/multiple-colliding-balls, https://en.wikipedia.org/wiki/Coefficient_of_restitution

                float coeffOfRestitution = 1.0f;
                float light1Mass = CpuLight.MASS;
                float light2Mass = CpuLight.MASS;
                float combinedMass = light1Mass + light2Mass;

                Vector3 otherLightNormal = Vector3.Normalize(light.GpuLight.Position - otherLight.GpuLight.Position);
                Vector3 lightNormal = -otherLightNormal;

                float ua = Vector3.Dot(-light.Velocity, lightNormal);
                float ub = Vector3.Dot(-otherLight.Velocity, lightNormal);

                float newVelA = (coeffOfRestitution * light2Mass * (ub - ua) + light1Mass * ua + light2Mass * ub) / combinedMass;
                float newVelB = (coeffOfRestitution * light1Mass * (ua - ub) + light1Mass * ua + light2Mass * ub) / combinedMass;

                light.Velocity += lightNormal * (ua - newVelA);
                otherLight.Velocity += lightNormal * (ub - newVelB);
            }
        }

        public void Update(out bool anyLightMoved)
        {
            // Update PointShadows
            for (int i = 0; i < pointShadowManager.Count; i++)
            {
                pointShadowManager.TryGetPointShadow(i, out CpuPointShadow pointShadow);
                
                CpuLight light = lights[pointShadow.GetGpuPointShadow().LightIndex];
                pointShadow.Position = light.GpuLight.Position;
            }
            pointShadowManager.Update();

            // Update lights
            anyLightMoved = false;
            for (int i = 0; i < Count; i++)
            {
                CpuLight light = lights[i];
                lightBufferObject.UploadElements(light.GpuLight, i);

                if (light.GpuLight.DidMove())
                {
                    anyLightMoved = true;
                }
                light.GpuLight.SetPrevToCurrentPosition();
            }
        }

        public bool TryGetLight(int index, out CpuLight light)
        {
            if (index >= 0 && index < Count)
            {
                light = lights[index];
                return true;
            }

            light = null;
            return false;
        }

        public bool TryGetPointShadow(int index, out CpuPointShadow pointShadow)
        {
            return pointShadowManager.TryGetPointShadow(index, out pointShadow);
        }

        public bool Intersect(in Ray ray, out RayHitInfo hitInfo)
        {
            hitInfo = new RayHitInfo();
            hitInfo.T = float.MaxValue;

            for (int i = 0; i < Count; i++)
            {
                CpuLight light = lights[i];
                if (Intersections.RayVsSphere(ray, Conversions.ToSphere(light.GpuLight), out float tMin, out float tMax) && tMax < hitInfo.T)
                {
                    hitInfo.T = tMin < 0.0f ? tMax : tMin;
                    hitInfo.LightID = i;
                }
            }

            return hitInfo.T != float.MaxValue;
        }

        public void SetSizeRayTracedShadows(Vector2i size)
        {
            pointShadowManager.SetSizeRayTracedShadows(size);
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
