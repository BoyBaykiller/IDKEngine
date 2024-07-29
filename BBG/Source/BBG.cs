using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public enum GpuVendor : int
        {
            Unknown,
            NVIDIA,
            AMD,
            INTEL,
        }

        public record struct ContextInfo
        {
            public string Name;
            public double GLVersion;
            public DeviceInfo DeviceInfo;
        }

        public record struct DeviceInfo
        {
            public string Name;
            public GpuVendor Vendor;
            public ExtensionSupport ExtensionSupport;
        }

        public record struct ExtensionSupport
        {
            /// <summary>
            /// GL_ARB_bindless_texture
            /// </summary>
            public bool BindlessTextures;

            /// <summary>
            /// GL_EXT_shader_image_load_formatted
            /// This extension is not advertised by older AMD drivers even though it is supported, see
            /// <see href="https://community.amd.com/t5/opengl-vulkan/opengl-bug-gl-ext-shader-image-load-formatted-not-reported-even/m-p/676326#M5140">this</see>
            /// </summary>
            public bool ImageLoadFormatted;

            /// <summary>
            /// GL_ARB_seamless_cubemap_per_texture or GL_AMD_seamless_cubemap_per_texture
            /// </summary>
            public bool SeamlessCubemapPerTexture;

            /// <summary>
            /// GL_NV_shading_rate_image
            /// </summary>
            public bool VariableRateShading;

            /// <summary>
            /// GL_NV_mesh_shader
            /// </summary>
            public bool MeshShader;

            /// <summary>
            /// GL_NV_conservative_raster
            /// </summary>
            public bool ConservativeRaster;

            /// <summary>
            /// GL_NV_shader_atomic_fp16_vector
            /// </summary>
            public bool AtomicFp16Vector;

            /// <summary>
            /// GL_NV_geometry_shader_passthrough
            /// </summary>
            public bool GeometryShaderPassthrough;

            /// <summary>
            /// GL_NV_viewport_swizzle
            /// </summary>
            public bool ViewportSwizzle;

            /// <summary>
            /// GL_ARB_shading_language_include
            /// </summary>
            public bool ShadingLanguageInclude;
        }

        public record struct DrawMeshTasksIndirectCommandNV
        {
            public int Count;
            public int First;
        }

        public record struct DrawElementsIndirectCommand
        {
            public int IndexCount;
            public int InstanceCount;
            public int FirstIndex;
            public int BaseVertex;
            public int BaseInstance;
        }

        public record struct DispatchIndirectCommand
        {
            public uint NumGroupsX;
            public uint NumGroupsY;
            public uint NumGroupsZ;
        }

        private static ContextInfo contextInfo;

        public static void Initialize(Debugging.FuncOpenGLDebugCallback openglDebugCallback = null)
        {
            if (openglDebugCallback != null)
            {
                Debugging.EnableDebugCallback = true; 
                Debugging.OpenGLDebugCallback += openglDebugCallback;
            }

            // bind dummy VAO as drawing without one is not allowed but possible
            int dummyVao = 0;
            GL.CreateVertexArrays(1, ref dummyVao);
            GL.BindVertexArray(dummyVao);

            // fix default settings
            GL.Disable(EnableCap.Multisample);
            GL.Disable(EnableCap.Dither);

            // set default graphics pipeline state
            Rendering.SetGraphicsPipelineState(new Rendering.GraphicsPipelineState());

            // ideally should be a paramater in pixel pack/unpack functions
            GL.PixelStorei(PixelStoreParameter.PackAlignment, 1);
            GL.PixelStorei(PixelStoreParameter.UnpackAlignment, 1);

            contextInfo.Name = GL.GetString(StringName.Version);
            contextInfo.GLVersion = Convert.ToDouble($"{GL.GetInteger(GetPName.MajorVersion)}.{GL.GetInteger(GetPName.MinorVersion)}");

            ref DeviceInfo deviceInfo = ref contextInfo.DeviceInfo;
            deviceInfo.Name = GL.GetString(StringName.Renderer);
            deviceInfo.Vendor = GetGpuVendor();
            deviceInfo.ExtensionSupport = GetSupportedExtensions();
        }

        private static GpuVendor GetGpuVendor()
        {
            string vendorName = GL.GetString(StringName.Vendor);
            
            if (vendorName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                return GpuVendor.NVIDIA;
            }

            if (vendorName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || 
                vendorName.Contains("ATI", StringComparison.OrdinalIgnoreCase))
            {
                return GpuVendor.AMD;
            }

            if (vendorName.Contains("INTEL", StringComparison.OrdinalIgnoreCase))
            {
                return GpuVendor.INTEL;
            }

            return GpuVendor.Unknown;
        }

        private static ExtensionSupport GetSupportedExtensions()
        {
            ExtensionSupport extensionSupport = new ExtensionSupport();

            for (uint i = 0; i < GL.GetInteger(GetPName.NumExtensions); i++)
            {
                string extension = GL.GetStringi(StringName.Extensions, i);
                if (extension == "GL_ARB_bindless_texture")
                {
                    extensionSupport.BindlessTextures = true;
                }
                else if (extension == "GL_EXT_shader_image_load_formatted")
                {
                    extensionSupport.ImageLoadFormatted = true;
                }
                else if (extension == "GL_AMD_seamless_cubemap_per_texture" ||
                         extension == "GL_ARB_seamless_cubemap_per_texture")
                {
                    extensionSupport.SeamlessCubemapPerTexture = true;
                }
                else if (extension == "GL_NV_shading_rate_image")
                {
                    extensionSupport.VariableRateShading = true;
                }
                else if (extension == "GL_NV_mesh_shader")
                {
                    extensionSupport.MeshShader = true;
                }
                else if (extension == "GL_NV_conservative_raster")
                {
                    extensionSupport.ConservativeRaster = true;
                }
                else if (extension == "GL_NV_shader_atomic_fp16_vector")
                {
                    extensionSupport.AtomicFp16Vector = true;
                }
                else if (extension == "GL_NV_geometry_shader_passthrough")
                {
                    extensionSupport.GeometryShaderPassthrough = true;
                }
                else if (extension == "GL_NV_viewport_swizzle")
                {
                    extensionSupport.ViewportSwizzle = true;
                }
                else if (extension == "GL_ARB_shading_language_include")
                {
                    extensionSupport.ShadingLanguageInclude = true;
                }
            }

            return extensionSupport;
        }

        public static ref readonly DeviceInfo GetDeviceInfo()
        {
            return ref GetContextInfo().DeviceInfo;
        }

        public static ref readonly ContextInfo GetContextInfo()
        {
            return ref contextInfo;
        }
    }
}
