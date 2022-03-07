using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class ParticleSimulator
    {
        private bool _isRunning;
        public bool IsRunning
        {
            get
            {
                return _isRunning;
            }

            set
            {
                _isRunning = value;
                shaderProgram.Upload(3, _isRunning ? 1.0f : 0.0f);
            }
        }

        public readonly int NumParticles;
        public readonly BufferObject ParticleBuffer;
        
        private static readonly ShaderProgram shaderProgram = new ShaderProgram(
            new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/particles/vertex.glsl")),
            new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/particles/fragment.glsl")));
        public readonly Texture Result;
        public unsafe ParticleSimulator(GLSLParticle[] particles)
        {
            ParticleBuffer = new BufferObject();
            ParticleBuffer.ImmutableAllocate(sizeof(GLSLParticle) * particles.Length, particles, BufferStorageFlags.DynamicStorageBit);
            ParticleBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 5, 0, ParticleBuffer.Size);

            Result = new Texture(TextureTarget2d.Texture2D);
            Result.MutableAllocate(832, 832, 1, PixelInternalFormat.R32ui, System.IntPtr.Zero, PixelFormat.RedInteger, PixelType.UnsignedInt);
            Result.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

            IsRunning = true;
            NumParticles = particles.Length;
        }

        public void Render(float dT)
        {
            shaderProgram.Use();
            shaderProgram.Upload(0, dT);

            GL.DrawArrays(PrimitiveType.Points, 0, NumParticles);
        }

        public void ProcessInputs(GameWindowBase gameWindowBase, Camera camera, in GLSLBasicData gLSLBasicData)
        {
            if (gameWindowBase.MouseState.CursorMode == CursorModeValue.CursorNormal)
            {
                if (gameWindowBase.MouseState[MouseButton.Left] == InputState.Pressed)
                {
                    Vector2 windowSpaceCoords = gameWindowBase.MouseState.Position; windowSpaceCoords.Y = gameWindowBase.Size.Y - windowSpaceCoords.Y; // [0, Width][0, Height]
                    Vector2 normalizedDeviceCoords = Vector2.Divide(windowSpaceCoords, gameWindowBase.Size) * 2 - new Vector2(1.0f); // [-1.0, 1.0][-1.0, 1.0]
                    Vector3 dir = GetWorldSpaceRay(gLSLBasicData.InvProjection, gLSLBasicData.InvView, normalizedDeviceCoords);

                    Vector3 pointOfMass = camera.Position + dir * 5.0f;
                    shaderProgram.Upload(1, pointOfMass);
                    shaderProgram.Upload(2, 1.0f);
                }
                else
                    shaderProgram.Upload(2, 0.0f);
            }

            if (gameWindowBase.KeyboardState[Keys.T] == InputState.Pressed)
                IsRunning = !IsRunning;
        }

        public static Vector3 GetWorldSpaceRay(in Matrix4 inverseProjection, in Matrix4 inverseView, in Vector2 normalizedDeviceCoords)
        {
            Vector4 rayEye = new Vector4(normalizedDeviceCoords.X, normalizedDeviceCoords.Y, -1.0f, 1.0f) * inverseProjection; rayEye.Z = -1.0f; rayEye.W = 0.0f;
            return (rayEye * inverseView).Xyz.Normalized();
        }
    }
}
