using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ImGuiNET;
using IDKEngine.Windowing;
using IDKEngine.OpenGL;

namespace IDKEngine.ThirdParty
{
    class ImGuiBackend : IDisposable
    {
        public bool IsIgnoreMouseInput = false;

        private bool frameBegun;

        private VAO vao;
        private ShaderProgram shaderProgram;
        private Texture fontTexture;
        private BufferObject vbo;
        private BufferObject ebo;
        
        private int Width;
        private int Height;

        private System.Numerics.Vector2 scaleFactor = System.Numerics.Vector2.One;
        public ImGuiBackend(int width, int height)
        {
            Width = width;
            Height = height;
            
            IntPtr context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);
            ImGuiIOPtr io = ImGui.GetIO();
            io.Fonts.AddFontDefault();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.ConfigDockingWithShift = true;

            CreateDeviceResources();
            SetKeyMappings();
            SetStyle();

            SetPerFrameImGuiData(1.0f / 60.0f);

            ImGui.NewFrame();
            
            frameBegun = true;
        }

        public void SetSize(Vector2i size)
        {
            Width = size.X;
            Height = size.Y;
        }

        public void Render()
        {
            if (frameBegun)
            {
                frameBegun = false;
                ImGui.Render();
                RenderImDrawData(ImGui.GetDrawData());
            }
        }
        public void Update(GameWindowBase wnd, float dT)
        {
            if (frameBegun)
            {
                ImGui.Render();
            }

            SetPerFrameImGuiData(dT);
            UpdateImGuiInput(wnd);

            frameBegun = true;
            ImGui.NewFrame();
        }

        private unsafe void CreateDeviceResources()
        {
            vbo = new BufferObject();
            vbo.MutableAllocate(10000);
            ebo = new BufferObject();
            ebo.MutableAllocate(2000);

            ImGuiIOPtr io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

            fontTexture = new Texture(Texture.Type.Texture2D);
            fontTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            fontTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            fontTexture.ImmutableAllocate(width, height, 1, Texture.InternalFormat.R8G8B8A8Unorm);
            fontTexture.Upload2D(width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

            io.Fonts.SetTexID(fontTexture.ID);
            io.Fonts.ClearTexData();

            string vertexSource = @"#version 460 core

                layout(location = 0) in vec2 Position;
                layout(location = 1) in vec2 TexCoord;
                layout(location = 2) in vec4 Color;

                layout(location = 0) uniform mat4 projection;

                out InOutVars
                {
                    vec4 Color;
                    vec2 TexCoord;
                } outData;

                void main()
                {
                    outData.Color = Color;
                    outData.TexCoord = TexCoord;
                    gl_Position = projection * vec4(Position, 0.0, 1.0);
                }";

            string fragmentSource = @"#version 460 core

                layout(location = 0) out vec4 FragColor;

                layout(binding = 0) uniform sampler2D SamplerFontTexture;

                in InOutVars
                {
                    vec4 Color;
                    vec2 TexCoord;
                } inData;

                void main()
                {
                    FragColor = inData.Color * texture(SamplerFontTexture, inData.TexCoord);
                }";

            shaderProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, vertexSource, "ImGui Backend Vertex Shader"),
                new Shader(ShaderType.FragmentShader, fragmentSource, "ImGui Backend Fragment Shader")
            );

            vao = new VAO();
            vao.SetElementBuffer(ebo);
            vao.AddSourceBuffer(vbo, 0, sizeof(ImDrawVert));
            vao.SetAttribFormat(0, 0, 2, VertexAttribType.Float, 0 * sizeof(float));
            vao.SetAttribFormat(0, 1, 2, VertexAttribType.Float, 2 * sizeof(float));
            vao.SetAttribFormat(0, 2, 4, VertexAttribType.UnsignedByte, 4 * sizeof(float), true);
        }

        private void SetPerFrameImGuiData(float dT)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new System.Numerics.Vector2(Width / scaleFactor.X, Height / scaleFactor.Y);
            io.DisplayFramebufferScale = scaleFactor;
            io.DeltaTime = dT;
        }

        private readonly List<char> pressedChars = new List<char>();
        private void UpdateImGuiInput(GameWindowBase wnd)
        {
            ImGuiIOPtr io = ImGui.GetIO();

            io.MouseDown[0] = wnd.MouseState[MouseButton.Left] == Keyboard.InputState.Pressed;
            io.MouseDown[1] = wnd.MouseState[MouseButton.Right] == Keyboard.InputState.Pressed;
            io.MouseDown[2] = wnd.MouseState[MouseButton.Middle] == Keyboard.InputState.Pressed;

            if (IsIgnoreMouseInput)
                io.MousePos = new System.Numerics.Vector2(-1.0f);
            else
                io.MousePos = new System.Numerics.Vector2(wnd.MouseState.Position.X, wnd.MouseState.Position.Y);

            io.MouseWheel = (float)wnd.MouseState.ScrollX;
            io.MouseWheelH = (float)wnd.MouseState.ScrollY;

            for (int i = 0; i < Keyboard.KeyValues.Length; i++)
            {
                if (Keyboard.KeyValues[i] == Keys.Unknown)
                    continue;

                io.KeysDown[(int)Keyboard.KeyValues[i]] = wnd.KeyboardState[Keyboard.KeyValues[i]] == Keyboard.InputState.Pressed;
            }

            for (int i = 0; i < pressedChars.Count; i++)
            {
                io.AddInputCharacter(pressedChars[i]);
            }

            pressedChars.Clear();

            io.KeyCtrl = wnd.KeyboardState[Keys.LeftControl] == Keyboard.InputState.Pressed || wnd.KeyboardState[Keys.RightControl] == Keyboard.InputState.Pressed;
            io.KeyAlt = wnd.KeyboardState[Keys.LeftAlt] == Keyboard.InputState.Pressed || wnd.KeyboardState[Keys.RightAlt] == Keyboard.InputState.Pressed;
            io.KeyShift = wnd.KeyboardState[Keys.LeftShift] == Keyboard.InputState.Pressed || wnd.KeyboardState[Keys.RightShift] == Keyboard.InputState.Pressed;
            io.KeySuper = wnd.KeyboardState[Keys.LeftSuper] == Keyboard.InputState.Pressed || wnd.KeyboardState[Keys.RightSuper] == Keyboard.InputState.Pressed;
        }

        public void PressChar(char keyChar)
        {
            pressedChars.Add(keyChar);
        }

        private static void SetKeyMappings()
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.KeyMap[(int)ImGuiKey.Tab] = (int)Keys.Tab;
            io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)Keys.Left;
            io.KeyMap[(int)ImGuiKey.RightArrow] = (int)Keys.Right;
            io.KeyMap[(int)ImGuiKey.UpArrow] = (int)Keys.Up;
            io.KeyMap[(int)ImGuiKey.DownArrow] = (int)Keys.Down;
            io.KeyMap[(int)ImGuiKey.PageUp] = (int)Keys.PageUp;
            io.KeyMap[(int)ImGuiKey.PageDown] = (int)Keys.PageDown;
            io.KeyMap[(int)ImGuiKey.Home] = (int)Keys.Home;
            io.KeyMap[(int)ImGuiKey.End] = (int)Keys.End;
            io.KeyMap[(int)ImGuiKey.Delete] = (int)Keys.Delete;
            io.KeyMap[(int)ImGuiKey.Backspace] = (int)Keys.Backspace;
            io.KeyMap[(int)ImGuiKey.Enter] = (int)Keys.Enter;
            io.KeyMap[(int)ImGuiKey.Escape] = (int)Keys.Escape;
            io.KeyMap[(int)ImGuiKey.A] = (int)Keys.A;
            io.KeyMap[(int)ImGuiKey.C] = (int)Keys.C;
            io.KeyMap[(int)ImGuiKey.V] = (int)Keys.V;
            io.KeyMap[(int)ImGuiKey.X] = (int)Keys.X;
            io.KeyMap[(int)ImGuiKey.Y] = (int)Keys.Y;
            io.KeyMap[(int)ImGuiKey.Z] = (int)Keys.Z;
        }

        private unsafe void RenderImDrawData(ImDrawDataPtr drawData)
        {
            if (drawData.CmdListsCount == 0)
                return;

            for (int i = 0; i < drawData.CmdListsCount; i++)
            {
                ImDrawListPtr cmdList = drawData.CmdLists[i];
                int vertexSize = cmdList.VtxBuffer.Size * sizeof(ImDrawVert);
                if (vertexSize > vbo.Size)
                {
                    int newSize = (int)Math.Max(vbo.Size * 1.5f, vertexSize);
                    vbo.MutableAllocate(newSize);
                }

                int indexSize = cmdList.IdxBuffer.Size * sizeof(ushort);
                if (indexSize > ebo.Size)
                {
                    int newSize = (int)Math.Max(ebo.Size * 1.5f, indexSize);
                    ebo.MutableAllocate(newSize);
                }
            }

            ImGuiIOPtr io = ImGui.GetIO();
            Matrix4 projection = Matrix4.CreateOrthographicOffCenter(0.0f, io.DisplaySize.X, io.DisplaySize.Y, 0.0f, -1.0f, 1.0f);

            shaderProgram.Use();
            shaderProgram.Upload(0, projection);

            vao.Bind();

            drawData.ScaleClipRects(io.DisplayFramebufferScale);
            GL.Enable(EnableCap.ScissorTest);
            for (int i = 0; i < drawData.CmdListsCount; i++)
            {
                ImDrawListPtr cmd_list = drawData.CmdLists[i];

                vbo.UploadData(0, cmd_list.VtxBuffer.Size * sizeof(ImDrawVert), cmd_list.VtxBuffer.Data);
                ebo.UploadData(0, cmd_list.IdxBuffer.Size * sizeof(ushort), cmd_list.IdxBuffer.Data);

                int idx_offset = 0;

                for (int cmd_i = 0; cmd_i < cmd_list.CmdBuffer.Size; cmd_i++)
                {
                    ImDrawCmdPtr pcmd = cmd_list.CmdBuffer[cmd_i];
                    if (pcmd.UserCallback != IntPtr.Zero)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        GL.BindTextureUnit(0, (int)pcmd.TextureId);

                        System.Numerics.Vector4 clip = pcmd.ClipRect;
                        GL.Scissor((int)clip.X, Height - (int)clip.W, (int)(clip.Z - clip.X), (int)(clip.W - clip.Y));

                        if ((io.BackendFlags & ImGuiBackendFlags.RendererHasVtxOffset) != 0)
                            GL.DrawElementsBaseVertex(PrimitiveType.Triangles, (int)pcmd.ElemCount, DrawElementsType.UnsignedShort, (IntPtr)(idx_offset * sizeof(ushort)), 0);
                        else
                            GL.DrawElements(BeginMode.Triangles, (int)pcmd.ElemCount, DrawElementsType.UnsignedShort, (int)pcmd.IdxOffset * sizeof(ushort));
                    }

                    idx_offset += (int)pcmd.ElemCount;
                }
            }
            GL.Disable(EnableCap.ScissorTest);
        }

        private static unsafe void SetStyle()
        {
            ImGuiStylePtr style = ImGui.GetStyle();
            RangeAccessor<Vector4> colors = new RangeAccessor<Vector4>(style.Colors.Data, style.Colors.Count);

            colors[(int)ImGuiCol.Text] = new Vector4(1.000f, 1.000f, 1.000f, 1.000f);
            colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.500f, 0.500f, 0.500f, 1.000f);
            colors[(int)ImGuiCol.WindowBg] = new Vector4(0.180f, 0.180f, 0.180f, 1.000f);
            colors[(int)ImGuiCol.ChildBg] = new Vector4(0.280f, 0.280f, 0.280f, 0.000f);
            colors[(int)ImGuiCol.PopupBg] = new Vector4(0.313f, 0.313f, 0.313f, 1.000f);
            colors[(int)ImGuiCol.Border] = new Vector4(0.266f, 0.266f, 0.266f, 1.000f);
            colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.000f, 0.000f, 0.000f, 0.000f);
            colors[(int)ImGuiCol.FrameBg] = new Vector4(0.160f, 0.160f, 0.160f, 1.000f);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.200f, 0.200f, 0.200f, 1.000f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.280f, 0.280f, 0.280f, 1.000f);
            colors[(int)ImGuiCol.TitleBg] = new Vector4(0.148f, 0.148f, 0.148f, 1.000f);
            colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.148f, 0.148f, 0.148f, 1.000f);
            colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.148f, 0.148f, 0.148f, 1.000f);
            colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.195f, 0.195f, 0.195f, 1.000f);
            colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.160f, 0.160f, 0.160f, 1.000f);
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.277f, 0.277f, 0.277f, 1.000f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.300f, 0.300f, 0.300f, 1.000f);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(1.000f, 0.391f, 0.000f, 1.000f);
            colors[(int)ImGuiCol.CheckMark] = new Vector4(1.000f, 1.000f, 1.000f, 1.000f);
            colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.391f, 0.391f, 0.391f, 1.000f);
            colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(1.000f, 0.391f, 0.000f, 1.000f);
            colors[(int)ImGuiCol.Button] = new Vector4(1.000f, 1.000f, 1.000f, 0.000f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(1.000f, 1.000f, 1.000f, 0.156f);
            colors[(int)ImGuiCol.ButtonActive] = new Vector4(1.000f, 1.000f, 1.000f, 0.391f);
            colors[(int)ImGuiCol.Header] = new Vector4(0.313f, 0.313f, 0.313f, 1.000f);
            colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.469f, 0.469f, 0.469f, 1.000f);
            colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.469f, 0.469f, 0.469f, 1.000f);
            colors[(int)ImGuiCol.Separator] = colors[(int)ImGuiCol.Border];
            colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.391f, 0.391f, 0.391f, 1.000f);
            colors[(int)ImGuiCol.SeparatorActive] = new Vector4(1.000f, 0.391f, 0.000f, 1.000f);
            colors[(int)ImGuiCol.ResizeGrip] = new Vector4(1.000f, 1.000f, 1.000f, 0.250f);
            colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(1.000f, 1.000f, 1.000f, 0.670f);
            colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(1.000f, 0.391f, 0.000f, 1.000f);
            colors[(int)ImGuiCol.Tab] = new Vector4(0.098f, 0.098f, 0.098f, 1.000f);
            colors[(int)ImGuiCol.TabHovered] = new Vector4(0.352f, 0.352f, 0.352f, 1.000f);
            colors[(int)ImGuiCol.TabActive] = new Vector4(0.195f, 0.195f, 0.195f, 1.000f);
            colors[(int)ImGuiCol.TabUnfocused] = new Vector4(0.098f, 0.098f, 0.098f, 1.000f);
            colors[(int)ImGuiCol.TabUnfocusedActive] = new Vector4(0.195f, 0.195f, 0.195f, 1.000f);
            colors[(int)ImGuiCol.DockingPreview] = new Vector4(1.000f, 0.391f, 0.000f, 0.781f);
            colors[(int)ImGuiCol.DockingEmptyBg] = new Vector4(0.180f, 0.180f, 0.180f, 1.000f);
            colors[(int)ImGuiCol.PlotLines] = new Vector4(0.469f, 0.469f, 0.469f, 1.000f);
            colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(1.000f, 0.391f, 0.000f, 1.000f);
            colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.586f, 0.586f, 0.586f, 1.000f);
            colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(1.000f, 0.391f, 0.000f, 1.000f);
            colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(1.000f, 1.000f, 1.000f, 0.156f);
            colors[(int)ImGuiCol.DragDropTarget] = new Vector4(1.000f, 0.391f, 0.000f, 1.000f);
            colors[(int)ImGuiCol.NavHighlight] = new Vector4(1.000f, 0.391f, 0.000f, 1.000f);
            colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.000f, 0.391f, 0.000f, 1.000f);
            colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.000f, 0.000f, 0.000f, 0.586f);
            colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.000f, 0.000f, 0.000f, 0.586f);

            style.ChildRounding = 4.0f;
            style.FrameBorderSize = 1.0f;
            style.FrameRounding = 2.0f;
            style.GrabMinSize = 7.0f;
            style.PopupRounding = 2.0f;
            style.ScrollbarRounding = 12.0f;
            style.ScrollbarSize = 13.0f;
            style.TabBorderSize = 1.0f;
            style.TabRounding = 0.0f;
            style.WindowRounding = 4.0f;
        }

        public void Dispose()
        {
            fontTexture.Dispose();
            shaderProgram.Dispose();
            vao.Dispose();
        }
    }
}