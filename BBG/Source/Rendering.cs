using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
using FboBufferType = OpenTK.Graphics.OpenGL.Buffer;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public unsafe class Rendering
        {
            public enum AttachmentLoadOp : int
            {
                // https://developer.arm.com/documentation/102479/0100/Efficient-Render-Passes/Minimizing-Start-of-Tile-Loads
                // https://community.arm.com/arm-community-blogs/b/graphics-gaming-and-vr-blog/posts/picking-the-most-efficient-load-store-operations
                // https://interactive.arm.com/story/the-arm-manga-guide-to-the-mali-gpu/

                /// <summary>
                /// On tiler GPUs it makes the driver do a full screen copy from image memory to tile memory which is bad.
                /// On Desktop GPUs this does nothing.
                /// </summary>
                Load,

                /// <summary>
                /// Clears the image to a specified clear color. On tiler GPUs this might or might not have the same cost as DontCare.
                /// On Mali for example image clear works directly on tile memory and is free (as fast as DontCare).
                /// On Desktop GPUs this has a minimal cost.
                /// </summary>
                Clear,

                /// <summary>
                /// On tiler GPUs this tells the driver it does not have to load the image into tile memory before rendering.
                /// That means tile memory will start out undefined as it is assumed nothing reads from it.
                /// This should be preferred.
                /// </summary>
                DontCare,
            }

            public enum Topology : uint
            {
                Points = PrimitiveType.Points,
                Triangles = PrimitiveType.Triangles,
                Quads = PrimitiveType.Quads,
            }

            public enum IndexType : uint
            {
                Uint = DrawElementsType.UnsignedInt,
            }

            public enum Capability : uint
            {
                None = 0,
                DepthTest = EnableCap.DepthTest,
                CullFace = EnableCap.CullFace,

                /// <summary>
                /// Requires GL_NV_conservative_raster
                /// </summary>
                ConservativeRasterizationNV = All.ConservativeRasterizationNv,

                /// <summary>
                /// Requires GL_NV_shading_rate_image
                /// </summary>
                VariableRateShadingNV = All.ShadingRateImageNv
            }

            public enum DepthConvention : uint
            {
                ZeroToOne = ClipControlDepth.ZeroToOne,
                NegativeOneToOne = ClipControlDepth.NegativeOneToOne,
            }

            /// <summary>
            /// Requires GL_NV_viewport_swizzle
            /// </summary>
            public enum ViewportSwizzleAxisNV : uint
            {
                PositiveX = All.ViewportSwizzlePositiveXNv,
                PositiveY = All.ViewportSwizzlePositiveYNv,
                PositiveZ = All.ViewportSwizzlePositiveZNv,
                PositiveW = All.ViewportSwizzlePositiveWNv,
            }

            public enum TriangleFace : uint
            {
                Front = OpenTK.Graphics.OpenGL.TriangleFace.Front,
                Back = OpenTK.Graphics.OpenGL.TriangleFace.Back,
                FrontAndBack = OpenTK.Graphics.OpenGL.TriangleFace.FrontAndBack,
            }

            public enum FillMode : uint
            {
                Point = PolygonMode.Point,
                Line = PolygonMode.Line,
                Fill = PolygonMode.Fill,
            }

            public enum DepthFunction : uint
            {
                Less = OpenTK.Graphics.OpenGL.DepthFunction.Less,
                Lequal = OpenTK.Graphics.OpenGL.DepthFunction.Lequal,
                Always = OpenTK.Graphics.OpenGL.DepthFunction.Always,
            }

            public enum BlendOp : uint
            {
                Add = BlendEquationMode.FuncAdd,
            }

            public enum BlendFactor : uint
            {
                Zero = BlendingFactor.Zero,
                One = BlendingFactor.One,
                SrcAlpha = BlendingFactor.SrcAlpha,
                OneMinusSrcAlpha = BlendingFactor.OneMinusSrcAlpha,
            }

            /// <summary>
            /// Requires GL_NV_shading_rate_image
            /// </summary>
            public enum ShadingRateNV : uint
            {
                _0InvocationsPerPixel = All.ShadingRateNoInvocationsNv,
                _1InvocationPerPixel = All.ShadingRate1InvocationPerPixelNv,
                _1InvocationPer1x2Pixels = All.ShadingRate1InvocationPer1x2PixelsNv,
                _1InvocationPer2x1Pixels = All.ShadingRate1InvocationPer2x1PixelsNv,
                _1InvocationPer2x2Pixels = All.ShadingRate1InvocationPer2x2PixelsNv,
                _1InvocationPer2x4Pixels = All.ShadingRate1InvocationPer2x4PixelsNv,
                _1InvocationPer4x2Pixels = All.ShadingRate1InvocationPer4x2PixelsNv,
                _1InvocationPer4x4Pixels = All.ShadingRate1InvocationPer4x4PixelsNv,
                _2InvocationsPerPixel = All.ShadingRate2InvocationsPerPixelNv,
                _4InvocationsPerPixel = All.ShadingRate4InvocationsPerPixelNv,
                _8InvocationsPerPixel = All.ShadingRate8InvocationsPerPixelNv,
                _16InvocationsPerPixel = All.ShadingRate16InvocationsPerPixelNv,
            }

            public enum VertexAttributeType : uint
            {
                Float = VertexAttribType.Float,
            }

            public record struct RenderAttachments
            {
                public ColorAttachments? ColorAttachments;
                public DepthStencilAttachment? DepthStencilAttachment;
            }

            public record struct RenderAttachmentsVerbose
            {
                public ColorAttachment[]? ColorAttachments;
                public DepthStencilAttachment? DepthStencilAttachment;
            }

            /// <summary>
            /// Similar to <see cref="ColorAttachment"/> but less verbose
            /// </summary>
            public record struct ColorAttachments
            {
                public Texture[] Textures;
                public Vector4 ClearColor; // Currently assumes floating point texture format
                public AttachmentLoadOp AttachmentLoadOp;
            }

            public record struct ColorAttachment
            {
                public bool EnableWrites = true;
                public Texture Texture;
                public Vector4 ClearColor; // Currently assumes floating point texture format
                public int Level;
                public AttachmentLoadOp AttachmentLoadOp;
                public BlendState BlendState = new BlendState();

                public ColorAttachment()
                {
                }
            }

            public record struct DepthStencilAttachment
            {
                public Texture Texture;
                public float DepthClearValue = 1.0f;
                public int StencilClearValue = 0;
                public int Level;
                public AttachmentLoadOp AttachmentLoadOp;

                public DepthStencilAttachment()
                {
                }
            }

            public record struct NoRenderAttachmentsParams
            {
                public int Width;
                public int Height;
            }

            public record struct BlendState
            {
                public bool Enabled = false;
                public BlendOp BlendOp = BlendOp.Add;
                public BlendFactor SrcFactor = BlendFactor.One;
                public BlendFactor DstFactor = BlendFactor.Zero;

                public BlendState()
                {
                }
            }

            public record struct VertexInputDesc
            {
                public Buffer IndexBuffer;
                public VertexDescription? VertexDescription;
            }

            public record struct VertexDescription
            {
                public VertexBuffer[] VertexBuffers;
                public VertexAttribute[] VertexAttributes;
            }

            public record struct VertexBuffer
            {
                public Buffer Buffer;
                public int VertexSize;
                public int Offset;
            }

            public record struct VertexAttribute
            {
                public int BufferIndex;
                public nint RelativeOffset;

                public VertexAttributeType Type;
                public int NumComponents;
                public bool Normalize;
            }

            /// <summary>
            /// Requires GL_NV_viewport_swizzle
            /// </summary>
            public record struct ViewportSwizzleNV
            {
                public ViewportSwizzleAxisNV X = ViewportSwizzleAxisNV.PositiveX;
                public ViewportSwizzleAxisNV Y = ViewportSwizzleAxisNV.PositiveY;
                public ViewportSwizzleAxisNV Z = ViewportSwizzleAxisNV.PositiveZ;
                public ViewportSwizzleAxisNV W = ViewportSwizzleAxisNV.PositiveW;

                public ViewportSwizzleNV()
                {
                }
            }

            /// <summary>
            /// Requires GL_NV_shading_rate_image
            /// </summary>
            public record struct VariableRateShadingNV
            {
                public ShadingRateNV[] ShadingRatePalette;
                public Texture ShadingRateImage;
            }

            public record struct Viewport
            {
                public Vector2 Size;
                public Vector2 LowerLeftCorner;
                public ViewportSwizzleNV? ViewportSwizzle;

                public static implicit operator Viewport(Vector2i size)
                {
                    return new Viewport() { Size = size };
                }
            }

            public record struct GraphicsPipelineState
            {
                public Capability[] EnabledCapabilities = [];

                public bool EnableDepthWrites = true;
                public DepthFunction DepthFunction = DepthFunction.Less;
                public TriangleFace CullFace = TriangleFace.Back;
                public DepthConvention DepthConvention = DepthConvention.ZeroToOne;
                public FillMode FillMode = FillMode.Fill;
                public VariableRateShadingNV? VariableRateShading;

                public GraphicsPipelineState()
                {
                }
            }

            private static int fboNoAttachmentsGLHandle = 0;
            private static int vaoGLHandle = 0;

            private static Vector2i inferredViewportSize;

            public static void Render(string renderPassName, in RenderAttachments renderAttachments, in GraphicsPipelineState pipelineState, Action funcRender)
            {
                RenderAttachmentsVerbose verboseRTs = new RenderAttachmentsVerbose();
                if (renderAttachments.ColorAttachments.HasValue)
                {
                    ColorAttachments colorAttachments = renderAttachments.ColorAttachments.Value;

                    verboseRTs.ColorAttachments = new ColorAttachment[colorAttachments.Textures.Length];
                    for (int i = 0; i < renderAttachments.ColorAttachments.Value.Textures.Length; i++)
                    {
                        verboseRTs.ColorAttachments[i] = new ColorAttachment();
                        verboseRTs.ColorAttachments[i].Texture = colorAttachments.Textures[i];
                        verboseRTs.ColorAttachments[i].AttachmentLoadOp = colorAttachments.AttachmentLoadOp;
                        verboseRTs.ColorAttachments[i].ClearColor = colorAttachments.ClearColor;
                    }
                }
                verboseRTs.DepthStencilAttachment = renderAttachments.DepthStencilAttachment;

                Render(renderPassName, verboseRTs, pipelineState, funcRender);
            }

            public static void Render(string renderPassName, in RenderAttachmentsVerbose renderAttachments, in GraphicsPipelineState pipelineState, Action funcRender)
            {
                inferredViewportSize = new Vector2i();

                Debugging.PushDebugGroup(renderPassName);

                SetGraphicsPipelineState(pipelineState);

                int fbo = FramebufferCache.GetFramebuffer(RenderAttachmentsToFramebufferDesc(renderAttachments));
                
                if (renderAttachments.ColorAttachments != null)
                {
                    for (int i = 0; i < renderAttachments.ColorAttachments.Length; i++)
                    {
                        ref readonly ColorAttachment colorAttachment = ref renderAttachments.ColorAttachments[i];

                        Debug.Assert(Texture.GetFormatType(colorAttachment.Texture.Format) == Texture.InternalFormatType.Color);

                        bool enableWrites = colorAttachment.EnableWrites;
                        GL.ColorMaski((uint)i, enableWrites, enableWrites, enableWrites, enableWrites);

                        ref readonly BlendState blendState = ref colorAttachment.BlendState;
                        if (blendState.Enabled)
                        {
                            GL.Enablei(EnableCap.Blend, (uint)i);
                            GL.BlendEquationi((uint)i, (BlendEquationMode)blendState.BlendOp);
                            GL.BlendFunci((uint)i, (BlendingFactor)blendState.SrcFactor, (BlendingFactor)blendState.DstFactor);
                        }
                        else
                        {
                            GL.Disablei(EnableCap.Blend, (uint)i);
                        }

                        switch (colorAttachment.AttachmentLoadOp)
                        {
                            case AttachmentLoadOp.Load:
                                break;

                            case AttachmentLoadOp.Clear:
                                Vector4 clearColor = colorAttachment.ClearColor;
                                GL.ClearNamedFramebufferfv(fbo, FboBufferType.Color, i, &clearColor.X);
                                break;

                            case AttachmentLoadOp.DontCare:
                                FramebufferAttachment framebufferAttachment = FramebufferAttachment.ColorAttachment0 + (uint)i;
                                GL.InvalidateNamedFramebufferData(fbo, 1, ref framebufferAttachment);
                                break;
                        }

                        Vector3i textureSize = Texture.GetMipmapLevelSize(colorAttachment.Texture.Width, colorAttachment.Texture.Height, 1, colorAttachment.Level);
                        inferredViewportSize = Vector2i.ComponentMax(inferredViewportSize, textureSize.Xy);
                    }
                }
                
                if (renderAttachments.DepthStencilAttachment != null)
                {
                    DepthStencilAttachment depthStencilAttachment = renderAttachments.DepthStencilAttachment.Value;

                    Texture.InternalFormatType formatType = Texture.GetFormatType(depthStencilAttachment.Texture.Format);
                    bool hasDepthComponent = formatType.HasFlag(Texture.InternalFormatType.Depth);
                    bool hasStencilComponent = formatType.HasFlag(Texture.InternalFormatType.Stencil);

                    Debug.Assert(hasDepthComponent || hasStencilComponent);

                    switch (depthStencilAttachment.AttachmentLoadOp)
                    {
                        case AttachmentLoadOp.Load:
                            break;

                        case AttachmentLoadOp.Clear:
                            if (hasDepthComponent)
                            {
                                GL.ClearNamedFramebufferfv(fbo, FboBufferType.Depth, 0, &depthStencilAttachment.DepthClearValue);
                            }
                            if (hasStencilComponent)
                            {
                                GL.ClearNamedFramebufferiv(fbo, FboBufferType.Stencil, 0, &depthStencilAttachment.StencilClearValue);
                            }
                            break;

                        case AttachmentLoadOp.DontCare:
                            FramebufferAttachment attachment = FormatTypeToFboAttachment(formatType);
                            GL.InvalidateNamedFramebufferData(fbo, 1, ref attachment);

                            break;
                    }
                    
                    Vector3i textureSize = Texture.GetMipmapLevelSize(depthStencilAttachment.Texture.Width, depthStencilAttachment.Texture.Height, 1, depthStencilAttachment.Level);
                    inferredViewportSize = Vector2i.ComponentMax(inferredViewportSize, textureSize.Xy);
                }
                
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
                
                funcRender();

                UnsetGraphicsPipelineState(pipelineState);

                // Let's be a good citizen and also unset these
                GL.ColorMask(true, true, true, true);
                GL.DepthMask(true);

                Debugging.PopDebugGroup();
            }

            public static void Render(string renderPassName, in NoRenderAttachmentsParams fboParameters, in GraphicsPipelineState pipelineState, Action funcRender)
            {
                Debugging.PushDebugGroup(renderPassName);

                GL.DeleteFramebuffers(1, ref fboNoAttachmentsGLHandle);
                GL.CreateFramebuffers(1, ref fboNoAttachmentsGLHandle);

                GL.NamedFramebufferParameteri(fboNoAttachmentsGLHandle, FramebufferParameterName.FramebufferDefaultWidth, fboParameters.Width);
                GL.NamedFramebufferParameteri(fboNoAttachmentsGLHandle, FramebufferParameterName.FramebufferDefaultHeight, fboParameters.Height);

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboNoAttachmentsGLHandle);
                SetGraphicsPipelineState(pipelineState);

                inferredViewportSize = new Vector2i(fboParameters.Width, fboParameters.Height);
                funcRender();

                Debugging.PopDebugGroup();
            }

            public static void CopyTextureToSwapchain(Texture texture)
            {
                FramebufferCache.FramebufferDesc desc = new FramebufferCache.FramebufferDesc();
                desc.Attachments[desc.NumAttachments++] = new FramebufferCache.Attachment() { Texture = texture, AttachmentPoint = FramebufferAttachment.ColorAttachment0 };

                int fbo = FramebufferCache.GetFramebuffer(desc);

                GL.BlitNamedFramebuffer(fbo, 0, 0, 0, texture.Width, texture.Height, 0, 0, texture.Width, texture.Height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
            }

            public static void SetVertexInputAssembly(in VertexInputDesc vertexInputAssembly)
            {
                GL.DeleteVertexArrays(1, ref vaoGLHandle);
                GL.CreateVertexArrays(1, ref vaoGLHandle);

                GL.VertexArrayElementBuffer(vaoGLHandle, vertexInputAssembly.IndexBuffer.ID);

                if (vertexInputAssembly.VertexDescription.HasValue)
                {
                    VertexDescription vertexDescription = vertexInputAssembly.VertexDescription.Value;
                    for (int i = 0; i < vertexDescription.VertexBuffers.Length; i++)
                    {
                        ref readonly VertexBuffer vertexBuffer = ref vertexDescription.VertexBuffers[i];

                        GL.VertexArrayVertexBuffer(vaoGLHandle, (uint)i, vertexBuffer.Buffer.ID, vertexBuffer.Offset, vertexBuffer.VertexSize);
                    }

                    for (int i = 0; i < vertexDescription.VertexAttributes.Length; i++)
                    {
                        ref readonly VertexAttribute vertexAttribute = ref vertexDescription.VertexAttributes[i];

                        GL.EnableVertexArrayAttrib(vaoGLHandle, (uint)i);
                        GL.VertexArrayAttribFormat(vaoGLHandle, (uint)i, vertexAttribute.NumComponents, (VertexAttribType)vertexAttribute.Type, vertexAttribute.Normalize, (uint)vertexAttribute.RelativeOffset);
                        GL.VertexArrayAttribBinding(vaoGLHandle, (uint)i, (uint)vertexAttribute.BufferIndex);
                    }
                }

                GL.BindVertexArray(vaoGLHandle);
            }

            public static void DrawIndexed(Topology topology, int count, IndexType indexType, int instanceCount = 1, int baseInstance = 0, nint offset = 0)
            {
                GL.DrawElementsInstancedBaseInstance((PrimitiveType)topology, count, (DrawElementsType)indexType, offset, instanceCount, (uint)baseInstance);
            }

            public static void DrawNonIndexed(Topology topology, int first, int count, int instanceCount = 1, uint baseInstance = 0)
            {
                GL.DrawArraysInstancedBaseInstance((PrimitiveType)topology, first, count, instanceCount, baseInstance);
            }

            public static void MultiDrawNonIndexed(Buffer drawCommandBuffer, Topology topology, int drawCount, int stride, nint bufferOffset = 0)
            {
                if (drawCount == 0) return;
                GL.BindBuffer(BufferTarget.DrawIndirectBuffer, drawCommandBuffer.ID);
                GL.MultiDrawArraysIndirect((PrimitiveType)topology, bufferOffset, drawCount, stride);
            }

            public static void MultiDrawIndexed(Buffer drawCommandBuffer, Topology topology, IndexType indexType, int drawCount, int stride, nint bufferOffset = 0)
            {
                if (drawCount == 0) return;
                GL.BindBuffer(BufferTarget.DrawIndirectBuffer, drawCommandBuffer.ID);
                GL.MultiDrawElementsIndirect((PrimitiveType)topology, (DrawElementsType)indexType, bufferOffset, drawCount, stride);
            }

            /// <summary>
            /// Requires GL_NV_mesh_shader
            /// </summary>
            /// <param name="bufferObject"></param>
            /// <param name=""></param>
            public static void MultiDrawMeshletsCountNV(Buffer meshletTasksCmdsBuffer, Buffer meshletTasksCountBuffer, int maxMeshlets, int stride, nint meshletCmdOffset = 0, nint taskCountOffset = 0)
            {
                GL.BindBuffer(BufferTarget.DrawIndirectBuffer, meshletTasksCmdsBuffer.ID);
                GL.BindBuffer(BufferTarget.ParameterBuffer, meshletTasksCountBuffer.ID);
                GL.NV.MultiDrawMeshTasksIndirectCountNV(meshletCmdOffset, taskCountOffset, maxMeshlets, stride);
            }

            public static void InferViewportSize(int x = 0, int y = 0)
            {
                SetViewport(new Viewport() { Size = inferredViewportSize, LowerLeftCorner = new Vector2i(x, y) });
            }

            public static void SetViewport(Viewport viewport)
            {
                SetViewports(new ReadOnlySpan<Viewport>(in viewport));
            }

            public static void SetViewports(ReadOnlySpan<Viewport> viewports)
            {
                Span<Vector4> data = stackalloc Vector4[viewports.Length];
                for (int i = 0; i < data.Length; i++)
                {
                    ref readonly Viewport viewport = ref viewports[i];
                    data[i] = new Vector4(viewport.LowerLeftCorner, viewport.Size.X, viewport.Size.Y);

                    if (viewport.ViewportSwizzle.HasValue)
                    {
                        ViewportSwizzleNV viewportSwizzle = viewport.ViewportSwizzle.Value;
                        GL.NV.ViewportSwizzleNV((uint)i, (All)viewportSwizzle.X, (All)viewportSwizzle.Y, (All)viewportSwizzle.Z, (All)viewportSwizzle.W);
                    }
                }

                GL.ViewportArray(0, viewports.Length, ref data[0].X);
            }

            public static Capability CapIf(bool val, Capability cap)
            {
                return val ? cap : Capability.None;
            }

            internal static void SetGraphicsPipelineState(in GraphicsPipelineState pipelineState)
            {
                ref readonly ExtensionSupport extensionSupport = ref GetDeviceInfo().ExtensionSupport;

                for (int i = 0; i < pipelineState.EnabledCapabilities.Length; i++)
                {
                    Capability capability = pipelineState.EnabledCapabilities[i];
                    if (capability == Capability.None)
                    {
                        continue;
                    }

                    GL.Enable((EnableCap)capability);
                }

                GL.DepthMask(pipelineState.EnableDepthWrites);
                GL.ClipControl(ClipControlOrigin.LowerLeft, (ClipControlDepth)pipelineState.DepthConvention);
                GL.PolygonMode(OpenTK.Graphics.OpenGL.TriangleFace.FrontAndBack, (PolygonMode)pipelineState.FillMode);
                GL.DepthFunc((OpenTK.Graphics.OpenGL.DepthFunction)pipelineState.DepthFunction);
                GL.CullFace((OpenTK.Graphics.OpenGL.TriangleFace)pipelineState.CullFace);

                if (pipelineState.VariableRateShading.HasValue && extensionSupport.VariableRateShading)
                {
                    VariableRateShadingNV variableRateShading = pipelineState.VariableRateShading.Value;
                    fixed (void* ptr = variableRateShading.ShadingRatePalette)
                    {
                        GL.NV.ShadingRateImagePaletteNV(0, 0, variableRateShading.ShadingRatePalette.Length, (All*)ptr);
                    }
                    GL.NV.BindShadingRateImageNV(variableRateShading.ShadingRateImage.ID);
                    GL.NV.ShadingRateImageBarrierNV(true);
                }
            }

            private static void UnsetGraphicsPipelineState(in GraphicsPipelineState pipelineState)
            {
                ref readonly ExtensionSupport extensionSupport = ref GetDeviceInfo().ExtensionSupport;
                for (int i = 0; i < pipelineState.EnabledCapabilities.Length; i++)
                {
                    Capability capability = pipelineState.EnabledCapabilities[i];
                    if (capability == Capability.None)
                    {
                        continue;
                    }

                    GL.Disable((EnableCap)capability);
                }

                // No need to set other state as we overwrite it anyway
            }

            private static FramebufferCache.FramebufferDesc RenderAttachmentsToFramebufferDesc(in RenderAttachmentsVerbose renderAttachments)
            {
                FramebufferCache.FramebufferDesc framebufferDesc = new FramebufferCache.FramebufferDesc();
                if (renderAttachments.ColorAttachments != null)
                {
                    for (int i = 0; i < renderAttachments.ColorAttachments.Length; i++)
                    {
                        ref readonly ColorAttachment colorAttachment = ref renderAttachments.ColorAttachments[i];

                        framebufferDesc.Attachments[framebufferDesc.NumAttachments++] = new FramebufferCache.Attachment()
                        {
                            Texture = colorAttachment.Texture,
                            Level = colorAttachment.Level,
                            AttachmentPoint = FramebufferAttachment.ColorAttachment0 + (uint)i,
                        };
                    }
                }
                if (renderAttachments.DepthStencilAttachment.HasValue)
                {
                    DepthStencilAttachment depthStencilAttachment = renderAttachments.DepthStencilAttachment.Value;

                    framebufferDesc.Attachments[framebufferDesc.NumAttachments++] = new FramebufferCache.Attachment()
                    {
                        Texture = depthStencilAttachment.Texture,
                        Level = depthStencilAttachment.Level,
                        AttachmentPoint = FormatTypeToFboAttachment(Texture.GetFormatType(depthStencilAttachment.Texture.Format))
                    };
                }

                return framebufferDesc;
            }

            private static FramebufferAttachment FormatTypeToFboAttachment(Texture.InternalFormatType type)
            {
                FramebufferAttachment framebufferAttachment = type switch
                {
                    Texture.InternalFormatType.Color => FramebufferAttachment.ColorAttachment0,
                    Texture.InternalFormatType.Depth => FramebufferAttachment.DepthAttachment,
                    Texture.InternalFormatType.Stencil => FramebufferAttachment.StencilAttachment,
                    Texture.InternalFormatType.DepthStencil => FramebufferAttachment.DepthStencilAttachment,
                    _ => throw new NotSupportedException($"Can not convert {nameof(type)} = {type} to {nameof(FramebufferAttachment)}"),
                };

                return framebufferAttachment;
            }
        }
    }
}
