using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public static class FramebufferCache
        {
            public const int MAX_COLOR_ATTACHMENTS = 8;
            public const int MAX_FRAMEBUFFER_ATTACHMENTS = 8 + 2; // 8color + 1depth + 1stencil

            public struct Attachment
            {
                public Texture Texture;
                public int Level;
                public FramebufferAttachment AttachmentPoint;

                public static bool operator==(in Attachment lhs, in Attachment rhs)
                {
                    return lhs.Texture == rhs.Texture && 
                           lhs.Level == rhs.Level &&
                           lhs.AttachmentPoint == rhs.AttachmentPoint;
                }

                public static bool operator !=(in Attachment lhs, in Attachment rhs)
                {
                    return !(lhs == rhs);
                }
            }

            public struct FramebufferDesc
            {
                public AttachmentArray Attachments;
                public int NumAttachments;

                public static bool operator ==(in FramebufferDesc lhs, in FramebufferDesc rhs)
                {
                    if (lhs.NumAttachments != rhs.NumAttachments)
                    {
                        return false;
                    }

                    for (int i = 0; i < lhs.NumAttachments; i++)
                    {
                        if (lhs.Attachments[i] != rhs.Attachments[i])
                        {
                            return false;
                        }
                    }

                    return true;
                }

                public static bool operator !=(in FramebufferDesc lhs, in FramebufferDesc rhs)
                {
                    return !(lhs == rhs);
                }
            }

            private struct FramebufferRessorce
            {
                public FramebufferDesc FramebufferDesc;
                public int GLRessource;
            }

            private static FramebufferRessorce[] framebuffers = Array.Empty<FramebufferRessorce>();

            public static int GetFramebuffer(in FramebufferDesc framebufferDesc)
            {
                for (int i = 0; i < framebuffers.Length; i++)
                {
                    ref readonly FramebufferRessorce framebuffer = ref framebuffers[i];
                    if (framebuffer.FramebufferDesc == framebufferDesc)
                    {
                        return framebuffer.GLRessource;
                    }
                }

                FramebufferRessorce newFramebuffer = CreateFramebuffer(framebufferDesc);
                framebuffers = framebuffers.Concat([newFramebuffer]).ToArray();

                return newFramebuffer.GLRessource;
            }

            public static void DeleteFramebuffersWithTexture(Texture texture)
            {
                int count = framebuffers.Length;
                int i = 0;
                while (i < count)
                {
                    bool deletedFramebuffer = false;

                    ref readonly FramebufferRessorce framebuffer = ref framebuffers[i];
                    for (int j = 0; j < framebuffer.FramebufferDesc.NumAttachments; j++)
                    {
                        ref readonly Attachment attachment = ref framebuffer.FramebufferDesc.Attachments[j];
                        if (attachment.Texture == texture)
                        {
                            GL.DeleteFramebuffer(framebuffer.GLRessource);
                            
                            if (count > 0)
                            {
                                // move deleted framebuffer to end of array
                                MathHelper.Swap(ref framebuffers[i], ref framebuffers[--count]);
                            }

                            deletedFramebuffer = true;
                            break;
                        }
                    }

                    if (!deletedFramebuffer)
                    {
                        i++;
                    }
                }
                Array.Resize(ref framebuffers, count);
            }

            private static FramebufferRessorce CreateFramebuffer(in FramebufferDesc framebufferDesc)
            {
                FramebufferRessorce newFramebuffer = new FramebufferRessorce();
                newFramebuffer.FramebufferDesc = framebufferDesc;
                GL.CreateFramebuffers(1, ref newFramebuffer.GLRessource);

                Span<ColorBuffer> drawBuffers = stackalloc ColorBuffer[MAX_COLOR_ATTACHMENTS];
                int numColorAttachments = 0;
                for (int i = 0; i < framebufferDesc.NumAttachments; i++)
                {
                    ref readonly Attachment attachment = ref framebufferDesc.Attachments[i];

                    GL.NamedFramebufferTexture(newFramebuffer.GLRessource, attachment.AttachmentPoint, attachment.Texture.ID, attachment.Level);

                    if (IsColorAttachment(attachment.AttachmentPoint))
                    {
                        drawBuffers[numColorAttachments] = ColorBuffer.ColorAttachment0 + (uint)numColorAttachments;
                        numColorAttachments++;
                    }
                }
                GL.NamedFramebufferDrawBuffers(newFramebuffer.GLRessource, numColorAttachments, drawBuffers[0]);

                return newFramebuffer;
            }

            private static bool IsColorAttachment(FramebufferAttachment framebufferAttachment)
            {
                return framebufferAttachment >= FramebufferAttachment.ColorAttachment0 &&
                       framebufferAttachment <= FramebufferAttachment.ColorAttachment16;
            }

            [InlineArray(MAX_FRAMEBUFFER_ATTACHMENTS)] // Assumed max color attachments
            public struct AttachmentArray
            {
                private Attachment _framebufferAttachment;
            }
        }
    }
}
