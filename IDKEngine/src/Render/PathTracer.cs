using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class PathTracer
    {
        private int _rayDepth;
        public int RayDepth
        {
            get => _rayDepth;

            set
            {
                _rayDepth = value;
                shaderProgram.Upload("RayDepth", value);
            }
        }

        private float _focalLength;
        public float FocalLength
        {
            get => _focalLength;

            set
            {
                _focalLength = value;
                shaderProgram.Upload("FocalLength", value);
            }
        }

        private float _apertureDiameter;
        public float ApertureDiameter
        {
            get => _apertureDiameter;

            set
            {
                _apertureDiameter = value;
                shaderProgram.Upload("ApertureDiameter", value);
            }
        }

        private bool _isDebugBVHTraversal;
        public bool IsDebugBVHTraversal
        {
            get => _isDebugBVHTraversal;

            set
            {
                _isDebugBVHTraversal = value;
                shaderProgram.Upload("IsDebugBVHTraversal", _isDebugBVHTraversal);
                if (_isDebugBVHTraversal)
                {
                    shaderProgram.Upload("ApertureDiameter", 0.0f);
                    shaderProgram.Upload("RayDepth", 1);
                }
                else
                {
                    ApertureDiameter = _apertureDiameter;
                    RayDepth = _rayDepth;
                }
            }
        }

        private bool _isRNGFrameBased;
        public bool IsRNGFrameBased
        {
            get => _isRNGFrameBased;

            set
            {
                _isRNGFrameBased = value;
                shaderProgram.Upload("IsRNGFrameBased", _isRNGFrameBased);
            }
        }

        public ModelSystem ModelSystem;
        public Texture Result;
        public readonly BVH BVH;
        private readonly ShaderProgram shaderProgram;
        private readonly Texture skyBox;
        public PathTracer(BVH bvh, ModelSystem modelSystem, Texture skyBox, int width, int height)
        {
            shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PathTracing/compute.glsl")));

            SetSize(width, height);

            this.skyBox = skyBox;
            ModelSystem = modelSystem;
            BVH = bvh;

            RayDepth = 6;
            FocalLength = 10.0f;
            ApertureDiameter = 0.03f;
        }

        public void Compute()
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba32f);
            skyBox.BindToUnit(0);

            shaderProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba32f);
        }
    }
}
