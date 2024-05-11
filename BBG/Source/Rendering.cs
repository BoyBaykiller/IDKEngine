using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
using Buffer = OpenTK.Graphics.OpenGL.Buffer;

namespace BBOpenGL
{
    public static partial class BBG
    { 
        public class Rendering
        {
            public enum AttachmentLoadOp : int
            {
                // https://developer.arm.com/documentation/102479/0100/Efficient-Render-Passes/Minimizing-Start-of-Tile-Loads
                // https://community.arm.com/arm-community-blogs/b/graphics-gaming-and-vr-blog/posts/picking-the-most-efficient-load-store-operations
                // https://interactive.arm.com/story/the-arm-manga-guide-to-the-mali-gpu/

                // This is a no-op. On tiler GPUs it makes the driver do a full screen copy from image memory to tile memory which is bad.
                // On Desktop GPUs this does nothing.
                Load,

                // Clears the image to a specified clear color. On tiler GPUs this might or might not have the same cost as DontCare.
                // On Mali for example image clear works directly on tile memory and is free (as fast as DontCare).
                // On Desktop GPUs this has a minimal cost.
                Clear,

                // On tiler GPUs this tells the driver it does not have to load the image into tile memory before rendering. 
                // That means tile memory will start out undefined as it is assumed nothing reads from it.
                // This should be preferred.
                DontCare,
            }

            public enum Topology : uint
            {
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
                Blend = EnableCap.Blend,

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

            public enum ViewportSwizzleAxis : uint
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
                Point = OpenTK.Graphics.OpenGL.PolygonMode.Point,
                Line = OpenTK.Graphics.OpenGL.PolygonMode.Line,
                Fill = OpenTK.Graphics.OpenGL.PolygonMode.Fill,
            }

            public enum DepthFunction : uint
            {
                Less = OpenTK.Graphics.OpenGL.DepthFunction.Less,
                Lequal = OpenTK.Graphics.OpenGL.DepthFunction.Lequal,
                Always = OpenTK.Graphics.OpenGL.DepthFunction.Always,
            }

            public enum ShadingRate : uint
            {
                _0InvocationsPerPixel = All.ShadingRateNoInvocationsNv,
                _1InvocationPerPixelNV = All.ShadingRate1InvocationPerPixelNv,
                _1InvocationPer1x2PixelsNV = All.ShadingRate1InvocationPer1x2PixelsNv,
                _1InvocationPer2x1PixelsNV = All.ShadingRate1InvocationPer2x1PixelsNv,
                _1InvocationPer2x2PixelsNV = All.ShadingRate1InvocationPer2x2PixelsNv,
                _1InvocationPer2x4PixelsNV = All.ShadingRate1InvocationPer2x4PixelsNv,
                _1InvocationPer4x2PixelsNV = All.ShadingRate1InvocationPer4x2PixelsNv,
                _1InvocationPer4x4PixelsNV = All.ShadingRate1InvocationPer4x4PixelsNv,
                _2InvocationsPerPixelNV = All.ShadingRate2InvocationsPerPixelNv,
                _4InvocationsPerPixelNV = All.ShadingRate4InvocationsPerPixelNv,
                _8InvocationsPerPixelNV = All.ShadingRate8InvocationsPerPixelNv,
                _16InvocationsPerPixelNV = All.ShadingRate16InvocationsPerPixelNv,
            }

            public struct ColorAttachment
            {
                public Texture Texture;
                public AttachmentLoadOp AttachmentLoadOp;
                public int Level;
                public Vector4 ClearColor;
            }

            // Similar to ColorAttachment but less verbose - allows for simpler API
            public struct ColorAttachments
            {
                public Texture[] Textures;
                public AttachmentLoadOp AttachmentLoadOp;
                public Vector4 ClearColor;
            }

            public struct DepthAttachment
            {
                public Texture Texture;
                public AttachmentLoadOp AttachmentLoadOp;
                public int Level;
                public float ClearValue = 1.0f;

                public DepthAttachment()
                {
                }
            }

            public struct StencilAttachment
            {
                public Texture Texture;
                public AttachmentLoadOp AttachmentLoadOp;
                public int Level;
                public int ClearValue;
            }

            public struct VerboseRenderAttachments
            {
                public ColorAttachment[]? ColorAttachments;
                public DepthAttachment? DepthAttachment;
                public StencilAttachment? StencilAttachment;
            }

            public struct RenderAttachments
            {
                public ColorAttachments? ColorAttachments;
                public DepthAttachment? DepthAttachment;
                public StencilAttachment? StencilAttachment;
            }

            public struct NoAttachmentsParams
            {
                public int Width;
                public int Height;
            }

            /// <summary>
            /// Requires GL_NV_viewport_swizzle
            /// </summary>
            public struct ViewportSwizzleNV
            {
                public ViewportSwizzleAxis X = ViewportSwizzleAxis.PositiveX;
                public ViewportSwizzleAxis Y = ViewportSwizzleAxis.PositiveY;
                public ViewportSwizzleAxis Z = ViewportSwizzleAxis.PositiveZ;
                public ViewportSwizzleAxis W = ViewportSwizzleAxis.PositiveW;

                public ViewportSwizzleNV()
                {
                }
            }

            /// <summary>
            /// Requires GL_NV_shading_rate_image
            /// </summary>
            public struct VariableRateShadingNV
            {
                public ShadingRate[] ShadingRatePalette;
                public Texture ShadingRateImage;
            }

            public struct Viewport
            {
                public Vector2 Size;
                public Vector2 LowerLeftCorner;
                public ViewportSwizzleNV? ViewportSwizzle;

                public static implicit operator Viewport(Vector2i size)
                {
                    return new Viewport() { Size = size };
                }
            }

            public struct GraphicsPipelineState
            {
                public Capability[] EnabledCapabilities = Array.Empty<Capability>();

                public DepthFunction DepthFunction = DepthFunction.Less;
                public TriangleFace CullFace = TriangleFace.Back;
                public DepthConvention DepthConvention = DepthConvention.ZeroToOne;
                public FillMode FillMode = FillMode.Fill;
                public VariableRateShadingNV? VariableRateShading;

                public GraphicsPipelineState()
                {
                }
            }

            private static int noAttachmentsFBO;
            private static Vector2i inferredViewportSize;
            public static void Render(string renderPassName, in RenderAttachments renderAttachments, in GraphicsPipelineState pipelineState, Action funcRender)
            {
                VerboseRenderAttachments verboseRenderAttachments = new VerboseRenderAttachments();
                if (renderAttachments.ColorAttachments.HasValue)
                {
                    verboseRenderAttachments.ColorAttachments = new ColorAttachment[renderAttachments.ColorAttachments.Value.Textures.Length];
                    for (int i = 0; i < renderAttachments.ColorAttachments.Value.Textures.Length; i++)
                    {
                        verboseRenderAttachments.ColorAttachments[i].Texture = renderAttachments.ColorAttachments.Value.Textures[i];
                        verboseRenderAttachments.ColorAttachments[i].AttachmentLoadOp = renderAttachments.ColorAttachments.Value.AttachmentLoadOp;
                        verboseRenderAttachments.ColorAttachments[i].ClearColor = renderAttachments.ColorAttachments.Value.ClearColor;
                    }
                }
                verboseRenderAttachments.DepthAttachment = renderAttachments.DepthAttachment;
                verboseRenderAttachments.StencilAttachment = renderAttachments.StencilAttachment;

                Render(renderPassName, verboseRenderAttachments, pipelineState, funcRender);
            }

            public static unsafe void Render(string renderPassName, in VerboseRenderAttachments renderAttachments, in GraphicsPipelineState pipelineState, Action funcRender)
            {
                inferredViewportSize = new Vector2i();

                Debugging.PushDebugGroup(renderPassName);

                int fbo = FramebufferCache.GetFramebuffer(RenderAttachmentsToFramebufferDesc(renderAttachments));

                if (renderAttachments.ColorAttachments != null)
                {
                    for (int i = 0; i < renderAttachments.ColorAttachments.Length; i++)
                    {
                        ref readonly ColorAttachment colorAttachment = ref renderAttachments.ColorAttachments[i];

                        Debug.Assert(Texture.GetFormatType(colorAttachment.Texture.Format) == Texture.InternalFormatType.Color);

                        switch (colorAttachment.AttachmentLoadOp)
                        {
                            case AttachmentLoadOp.Load:
                                break;

                            case AttachmentLoadOp.Clear:
                                Vector4 clearColor = colorAttachment.ClearColor;
                                GL.ClearNamedFramebufferfv(fbo, Buffer.Color, i, &clearColor.X);
                                break;

                            case AttachmentLoadOp.DontCare:
                                FramebufferAttachment framebufferAttachment = FramebufferAttachment.ColorAttachment0 + (uint)i;
                                GL.InvalidateNamedFramebufferData(fbo, 1, framebufferAttachment);
                                break;
                        }

                        Vector3i textureSize = Texture.GetMipMapLevelSize(colorAttachment.Texture.Width, colorAttachment.Texture.Height, colorAttachment.Texture.Depth, colorAttachment.Level);
                        inferredViewportSize = textureSize.Xy;
                    }
                }

                if (renderAttachments.DepthAttachment != null)
                {
                    DepthAttachment depthAttachment = renderAttachments.DepthAttachment.Value;

                    Debug.Assert(Texture.GetFormatType(depthAttachment.Texture.Format) == Texture.InternalFormatType.Depth);

                    switch (depthAttachment.AttachmentLoadOp)
                    {
                        case AttachmentLoadOp.Load:
                            break;

                        case AttachmentLoadOp.Clear:
                            GL.ClearNamedFramebufferfv(fbo, Buffer.Depth, 0, &depthAttachment.ClearValue);
                            break;

                        case AttachmentLoadOp.DontCare:
                            FramebufferAttachment framebufferAttachment = FramebufferAttachment.DepthAttachment;
                            GL.InvalidateNamedFramebufferData(fbo, 1, framebufferAttachment);
                            break;
                    }

                    Vector3i textureSize = Texture.GetMipMapLevelSize(depthAttachment.Texture.Width, depthAttachment.Texture.Height, depthAttachment.Texture.Depth, depthAttachment.Level);
                    inferredViewportSize = textureSize.Xy;
                }

                if (renderAttachments.StencilAttachment != null)
                {
                    StencilAttachment stencilAttachment = renderAttachments.StencilAttachment.Value;

                    Debug.Assert(Texture.GetFormatType(stencilAttachment.Texture.Format) == Texture.InternalFormatType.Depth);

                    switch (stencilAttachment.AttachmentLoadOp)
                    {
                        case AttachmentLoadOp.Load:
                            break;

                        case AttachmentLoadOp.Clear:
                            GL.ClearNamedFramebufferiv(fbo, Buffer.Stencil, 0, &stencilAttachment.ClearValue);
                            break;

                        case AttachmentLoadOp.DontCare:
                            FramebufferAttachment framebufferAttachment = FramebufferAttachment.StencilAttachment;
                            GL.InvalidateNamedFramebufferData(fbo, 1, framebufferAttachment);
                            break;
                    }

                    Vector3i textureSize = Texture.GetMipMapLevelSize(stencilAttachment.Texture.Width, stencilAttachment.Texture.Height, stencilAttachment.Texture.Depth, stencilAttachment.Level);
                    inferredViewportSize = textureSize.Xy;
                }

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
                SetGraphicsPipelineState(pipelineState);

                funcRender();

                Debugging.PopDebugGroup();
            }

            public static void Render(string renderPassName, in NoAttachmentsParams fboParameters, in GraphicsPipelineState pipelineState, Action funcRender)
            {
                inferredViewportSize = new Vector2i();

                Debugging.PushDebugGroup(renderPassName);

                if (noAttachmentsFBO == 0)
                {
                    GL.CreateFramebuffers(1, ref noAttachmentsFBO);
                }
                GL.NamedFramebufferParameteri(noAttachmentsFBO, FramebufferParameterName.FramebufferDefaultWidth, fboParameters.Width);
                GL.NamedFramebufferParameteri(noAttachmentsFBO, FramebufferParameterName.FramebufferDefaultHeight, fboParameters.Height);
                inferredViewportSize = new Vector2i(fboParameters.Width, fboParameters.Height);

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                SetGraphicsPipelineState(pipelineState);

                funcRender();

                Debugging.PopDebugGroup();
            }

            public static void CopyTextureToSwapchain(Texture texture)
            {
                Debugging.PushDebugGroup("Copy texture to swapchain");

                VerboseRenderAttachments renderAttachments = new VerboseRenderAttachments();
                renderAttachments.ColorAttachments = [new ColorAttachment() { Texture = texture }];

                int fbo = FramebufferCache.GetFramebuffer(RenderAttachmentsToFramebufferDesc(renderAttachments));

                GL.BlitNamedFramebuffer(fbo, 0, 0, 0, texture.Width, texture.Height, 0, 0, texture.Width, texture.Height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

                Debugging.PopDebugGroup();
            }

            public static void DrawIndexed(Topology topology, int count, IndexType indexType, int instanceCount = 1, int baseInstance = 0, nint offset = 0)
            {
                GL.DrawElementsInstancedBaseInstance((PrimitiveType)topology, count, (DrawElementsType)indexType, offset, instanceCount, (uint)baseInstance);
            }

            public static void DrawNonIndexed(Topology topology, int first, int count, int instanceCount = 1, uint baseInstance = 0)
            {
                GL.DrawArraysInstancedBaseInstance((PrimitiveType)topology, first, count, instanceCount, baseInstance);
            }

            public static void MultiDrawIndexed(BufferObject drawCommandBuffer, Topology topology, IndexType indexType, int drawCount, int stride, nint bufferOffset = 0)
            {
                GL.BindBuffer(BufferTarget.DrawIndirectBuffer, drawCommandBuffer.ID);
                GL.MultiDrawElementsIndirect((PrimitiveType)topology, (DrawElementsType)indexType, bufferOffset, drawCount, stride);
            }

            /// <summary>
            /// Requires GL_NV_mesh_shader
            /// </summary>
            /// <param name="bufferObject"></param>
            /// <param name=""></param>
            public static void MultiDrawMeshletsCountNV(BufferObject meshletTasksCmdsBuffer, BufferObject meshletTasksCountBuffer, int maxMeshlets, int stride, nint meshletCmdOffset = 0, nint taskCountOffset = 0)
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
                SetViewport(new ReadOnlySpan<Viewport>(in viewport));
            }
            
            public static void SetViewport(ReadOnlySpan<Viewport> viewports)
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

                GL.ViewportArray(0, viewports.Length, data[0].X);
            }

            public static unsafe void SetGraphicsPipelineState(in GraphicsPipelineState pipelineState)
            {
                Capability[] capabilities = Enum.GetValues<Capability>();
                ref readonly ExtensionSupport extensionSupport = ref GetDeviceInfo().ExtensionSupport;
                for (int i = 0; i < capabilities.Length; i++)
                {
                    Capability capability = capabilities[i];
                    if (capability == Capability.None)
                    {
                        continue;
                    }
                    if (capability == Capability.ConservativeRasterizationNV && !extensionSupport.ConservativeRaster)
                    {
                        continue;
                    }
                    if (capability == Capability.VariableRateShadingNV && !extensionSupport.VariableRateShading)
                    {
                        continue;
                    }

                    GL.Disable((EnableCap)capability);
                }

                for (int i = 0; i < pipelineState.EnabledCapabilities.Length; i++)
                {
                    Capability capability = pipelineState.EnabledCapabilities[i];
                    if (capability == Capability.None)
                    {
                        continue;
                    }

                    GL.Enable((EnableCap)capability);
                }

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

            private static FramebufferCache.FramebufferDesc RenderAttachmentsToFramebufferDesc(in VerboseRenderAttachments renderAttachments)
            {
                FramebufferCache.FramebufferDesc framebufferDesc = new FramebufferCache.FramebufferDesc();
                if (renderAttachments.ColorAttachments != null)
                {
                    for (int i = 0; i < renderAttachments.ColorAttachments.Length; i++)
                    {
                        ref readonly ColorAttachment colorAttachment = ref renderAttachments.ColorAttachments[i];

                        framebufferDesc.Attachments[framebufferDesc.NumAttachments++] = new FramebufferCache.Attachment() {
                            Texture = colorAttachment.Texture,
                            Level = colorAttachment.Level,
                            AttachmentPoint = FramebufferAttachment.ColorAttachment0 + (uint)i,
                        };
                    }
                }
                if (renderAttachments.DepthAttachment.HasValue)
                {
                    DepthAttachment depthAttachment = renderAttachments.DepthAttachment.Value;

                    framebufferDesc.Attachments[framebufferDesc.NumAttachments++] = new FramebufferCache.Attachment() {
                        Texture = depthAttachment.Texture,
                        Level = depthAttachment.Level,
                        AttachmentPoint = FramebufferAttachment.DepthAttachment,
                    };
                }
                if (renderAttachments.StencilAttachment.HasValue)
                {
                    StencilAttachment stencilAttachment = renderAttachments.StencilAttachment.Value;

                    framebufferDesc.Attachments[framebufferDesc.NumAttachments++] = new FramebufferCache.Attachment() {
                        Texture = stencilAttachment.Texture,
                        Level = stencilAttachment.Level,
                        AttachmentPoint = FramebufferAttachment.StencilAttachment,
                    };
                }

                return framebufferDesc;
            }
        }
    }
}
