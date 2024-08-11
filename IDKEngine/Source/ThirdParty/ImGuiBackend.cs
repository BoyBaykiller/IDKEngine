using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ImGuiNET;
using IDKEngine.Windowing;

// Based on https://github.com/NogginBops/ImGui.NET_OpenTK_Sample/blob/5d62fc77ebacc022b2430084a87939849c119913/Dear%20ImGui%20Sample/ImGuiController.cs
namespace IDKEngine.ThirdParty
{
    unsafe class ImGuiBackend : IDisposable
    {
        private static readonly Keys[] keysEnumValues = Enum.GetValues<Keys>();

        public bool IgnoreMouseInput;

        private int glVertexArray;
        private int glVertexBuffer;
        private int glIndexBuffer;
        private int glFontTexture;
        private int glShaderProgram;
        private const int glFontTextureUnit = 0;
        private const int glProjectionUniformLocation = 0;

        private int vertexBufferSize;
        private int indexBufferSize;

        private Vector2i windowSize;
        private Vector2 scaleFactor = Vector2.One;

        private readonly List<uint> pressedKeysBuf = new List<uint>();
        public ImGuiBackend(Vector2i windowSize)
        {
            this.windowSize = windowSize;

            ImGui.SetCurrentContext(ImGui.CreateContext());
            
            ImGuiIOPtr io = ImGui.GetIO();
            io.Fonts.AddFontDefault();

            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

            CreateDeviceObjects();
            SetStyle();
        }
        
        public void BeginFrame(GameWindowBase wnd, float dtSeconds)
        {
            UpdateIO(wnd, dtSeconds);
            ImGui.NewFrame();
        }

        public void EndFrame()
        {
            ImGui.Render();
            RenderDrawData(ImGui.GetDrawData());
        }

        private void UpdateIO(GameWindowBase wnd, float dTSeconds)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            {
                Vector2 displaySize = windowSize / scaleFactor;
                io.DisplaySize = new System.Numerics.Vector2(displaySize.X, displaySize.Y);
                io.DisplayFramebufferScale = new System.Numerics.Vector2(scaleFactor.X, scaleFactor.Y);
                io.DeltaTime = dTSeconds;
            }

            Mouse mouseState = wnd.MouseState;
            Keyboard KeyboardState = wnd.KeyboardState;

            io.MouseDown[0] = mouseState[MouseButton.Left] == Keyboard.InputState.Pressed;
            io.MouseDown[1] = mouseState[MouseButton.Right] == Keyboard.InputState.Pressed;
            io.MouseDown[2] = mouseState[MouseButton.Middle] == Keyboard.InputState.Pressed;
            io.MouseDown[3] = mouseState[MouseButton.Button4] == Keyboard.InputState.Pressed;
            io.MouseDown[4] = mouseState[MouseButton.Button5] == Keyboard.InputState.Pressed;

            io.MouseWheel = (float)wnd.MouseState.ScrollX;
            io.MouseWheelH = (float)wnd.MouseState.ScrollY;

            if (IgnoreMouseInput)
            {
                io.MousePos = new System.Numerics.Vector2(-1.0f);
            }
            else
            {
                io.MousePos = new System.Numerics.Vector2(wnd.MouseState.Position.X, wnd.MouseState.Position.Y);
            }

            for (int i = 0; i < keysEnumValues.Length; i++)
            {
                Keys key = keysEnumValues[i];
                if (key == Keys.Unknown)
                {
                    continue;
                }

                bool isDown = KeyboardState[key] == Keyboard.InputState.Pressed;
                io.AddKeyEvent(TranslateKey(key), isDown);
            }

            for (int i = 0; i < pressedKeysBuf.Count; i++)
            {
                io.AddInputCharacter(pressedKeysBuf[i]);
            }
            pressedKeysBuf.Clear();

            io.KeyCtrl = KeyboardState[Keys.LeftControl] == Keyboard.InputState.Pressed || KeyboardState[Keys.RightControl] == Keyboard.InputState.Pressed;
            io.KeyAlt = KeyboardState[Keys.LeftAlt] == Keyboard.InputState.Pressed || KeyboardState[Keys.RightAlt] == Keyboard.InputState.Pressed;
            io.KeyShift = KeyboardState[Keys.LeftShift] == Keyboard.InputState.Pressed || KeyboardState[Keys.RightShift] == Keyboard.InputState.Pressed;
            io.KeySuper = KeyboardState[Keys.LeftSuper] == Keyboard.InputState.Pressed || KeyboardState[Keys.RightSuper] == Keyboard.InputState.Pressed;
        }
        
        public void PressChar(uint key)
        {
            pressedKeysBuf.Add(key);
        }

        public void SetWindowSize(Vector2i size)
        {
            windowSize = size;
        }

        private void RenderDrawData(in ImDrawDataPtr drawData)
        {
            if (drawData.CmdListsCount == 0)
            {
                return;
            }

            ImGuiIOPtr io = ImGui.GetIO();
            drawData.ScaleClipRects(io.DisplayFramebufferScale);

            for (int i = 0; i < drawData.CmdListsCount; i++)
            {
                ref readonly ImDrawListPtr cmdList = ref drawData.CmdLists[i];

                int requiredVertexSize = cmdList.VtxBuffer.Size * sizeof(ImDrawVert);
                if (requiredVertexSize > vertexBufferSize)
                {
                    GL.NamedBufferData(glVertexBuffer, requiredVertexSize, null, VertexBufferObjectUsage.StaticDraw);
                    vertexBufferSize = requiredVertexSize;
                }

                int requiredIndexSize = cmdList.IdxBuffer.Size * sizeof(ushort);
                if (requiredIndexSize > indexBufferSize)
                {
                    GL.NamedBufferData(glIndexBuffer, requiredIndexSize, null, VertexBufferObjectUsage.StaticDraw);
                    indexBufferSize = requiredIndexSize;
                }
            }

            Matrix4 mvp = Matrix4.CreateOrthographicOffCenter(
                0.0f,
                io.DisplaySize.X,
                io.DisplaySize.Y,
                0.0f,
                -1.0f,
                1.0f);
            GL.ProgramUniformMatrix4f(glShaderProgram, glProjectionUniformLocation, 1, false, mvp);

            GL.Enable(EnableCap.Blend);
            GL.Enable(EnableCap.ScissorTest);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.BindVertexArray(glVertexArray);
            GL.UseProgram(glShaderProgram);
            for (int i = 0; i < drawData.CmdListsCount; i++)
            {
                ref readonly ImDrawListPtr cmdList = ref drawData.CmdLists[i];

                GL.NamedBufferSubData(glVertexBuffer, 0, cmdList.VtxBuffer.Size * sizeof(ImDrawVert), cmdList.VtxBuffer.Data);
                GL.NamedBufferSubData(glIndexBuffer, 0, cmdList.IdxBuffer.Size * sizeof(ushort), cmdList.IdxBuffer.Data);

                for (int j = 0; j < cmdList.CmdBuffer.Size; j++)
                {
                    ImDrawCmdPtr pcmd = cmdList.CmdBuffer[j];
                    if (pcmd.UserCallback != IntPtr.Zero)
                    {
                        throw new NotImplementedException("ImGui UserCallback not implemented");
                    }
                    else
                    {
                        // We do windowSize.Y - clip.W instead of clip.Y because OpenGL has flipped Y when it comes to these coordinates
                        System.Numerics.Vector4 clip = pcmd.ClipRect;
                        GL.Scissor((int)clip.X, windowSize.Y - (int)clip.W, (int)(clip.Z - clip.X), (int)(clip.W - clip.Y));

                        GL.BindTextureUnit(glFontTextureUnit, (int)pcmd.TextureId);
                        GL.DrawElementsBaseVertex(PrimitiveType.Triangles, (int)pcmd.ElemCount, DrawElementsType.UnsignedShort, (void*)(pcmd.IdxOffset * sizeof(ushort)), (int)pcmd.VtxOffset);
                    }
                }
            }

            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.ScissorTest);
        }

        private void CreateDeviceObjects()
        {
            string vertexSource = 
                $$"""
                #version 460 core
                
                layout(location = {{glProjectionUniformLocation}}) uniform mat4 Projection;
                
                layout(location = 0) in vec2 Position;
                layout(location = 1) in vec2 TexCoord;
                layout(location = 2) in vec4 Color;
                
                out InOutData
                {
                    vec4 Color;
                    vec2 TexCoord;
                } outData;

                void main()
                {
                    gl_Position = Projection * vec4(Position, 0.0, 1.0);
                    outData.Color = Color;
                    outData.TexCoord = TexCoord;
                }
                """;

            string fragmentSource = 
                $$"""
                #version 460 core

                layout(location = 0) out vec4 FragColor;
                layout(binding = {{glFontTextureUnit}}) uniform sampler2D SamplerFont;

                in InOutData
                {
                    vec4 Color;
                    vec2 TexCoord;
                } inData;

                void main()
                {
                    FragColor = texture(SamplerFont, inData.TexCoord) * inData.Color;
                }
                """;

            glShaderProgram = CreateProgram(vertexSource, fragmentSource);

            // These buffers are allocated and filled in the renderloop
            glVertexBuffer = GL.CreateBuffer();
            glIndexBuffer = GL.CreateBuffer();

            glVertexArray = GL.CreateVertexArray();
            GL.VertexArrayVertexBuffer(glVertexArray, 0, glVertexBuffer, 0, sizeof(ImDrawVert));
            GL.VertexArrayElementBuffer(glVertexArray, glIndexBuffer);

            GL.EnableVertexArrayAttrib(glVertexArray, 0);
            GL.VertexArrayAttribFormat(glVertexArray, 0, 2, VertexAttribType.Float, false, (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.pos)));
            GL.VertexArrayAttribBinding(glVertexArray, 0, 0);

            GL.EnableVertexArrayAttrib(glVertexArray, 1);
            GL.VertexArrayAttribFormat(glVertexArray, 1, 2, VertexAttribType.Float, false, (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.uv)));
            GL.VertexArrayAttribBinding(glVertexArray, 1, 0);

            GL.EnableVertexArrayAttrib(glVertexArray, 2);
            GL.VertexArrayAttribFormat(glVertexArray, 2, 4, VertexAttribType.UnsignedByte, true, (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.col)));
            GL.VertexArrayAttribBinding(glVertexArray, 2, 0);

            CreateFontsTexture();
        }

        private void CreateFontsTexture()
        {
            glFontTexture = GL.CreateTexture(TextureTarget.Texture2d);

            ImGuiIOPtr io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height);
            io.Fonts.SetTexID((nint)glFontTexture);

            GL.TextureStorage2D(glFontTexture, 1, SizedInternalFormat.Rgba8, width, height);
            GL.TextureSubImage2D(glFontTexture, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            GL.TextureParameteri(glFontTexture, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TextureParameteri(glFontTexture, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TextureParameteri(glFontTexture, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TextureParameteri(glFontTexture, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(glVertexArray);
            GL.DeleteBuffer(glVertexBuffer);
            GL.DeleteBuffer(glIndexBuffer);

            GL.DeleteTexture(glFontTexture);
            GL.DeleteProgram(glShaderProgram);
        }

        private int CreateProgram(string vertexSource, string fragmentSource)
        {
            int program = GL.CreateProgram();
            int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
            int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);

            GL.AttachShader(program, vertex);
            GL.AttachShader(program, fragment);

            GL.LinkProgram(program);

            GL.GetProgramInfoLog(program, out string info);
            if (info != string.Empty)
            {
                Console.WriteLine($"ImGui glLinkProgram had info log {info}");
            }

            GL.DetachShader(program, vertex);
            GL.DetachShader(program, fragment);

            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);

            return program;
        }

        private int CompileShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);

            GL.GetShaderInfoLog(shader, out string info);
            if (info != string.Empty)
            {
                Console.WriteLine($"ImGui glCompileShader [{type}] had info log {info}");
            }

            return shader;
        }

        private static ImGuiKey TranslateKey(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
                return key - Keys.D0 + ImGuiKey._0;

            if (key >= Keys.A && key <= Keys.Z)
                return key - Keys.A + ImGuiKey.A;

            if (key >= Keys.KeyPad0 && key <= Keys.KeyPad9)
                return key - Keys.KeyPad0 + ImGuiKey.Keypad0;

            if (key >= Keys.F1 && key <= Keys.F24)
                return key - Keys.F1 + ImGuiKey.F24;

            switch (key)
            {
                case Keys.Tab: return ImGuiKey.Tab;
                case Keys.Left: return ImGuiKey.LeftArrow;
                case Keys.Right: return ImGuiKey.RightArrow;
                case Keys.Up: return ImGuiKey.UpArrow;
                case Keys.Down: return ImGuiKey.DownArrow;
                case Keys.PageUp: return ImGuiKey.PageUp;
                case Keys.PageDown: return ImGuiKey.PageDown;
                case Keys.Home: return ImGuiKey.Home;
                case Keys.End: return ImGuiKey.End;
                case Keys.Insert: return ImGuiKey.Insert;
                case Keys.Delete: return ImGuiKey.Delete;
                case Keys.Backspace: return ImGuiKey.Backspace;
                case Keys.Space: return ImGuiKey.Space;
                case Keys.Enter: return ImGuiKey.Enter;
                case Keys.Escape: return ImGuiKey.Escape;
                case Keys.Apostrophe: return ImGuiKey.Apostrophe;
                case Keys.Comma: return ImGuiKey.Comma;
                case Keys.Minus: return ImGuiKey.Minus;
                case Keys.Period: return ImGuiKey.Period;
                case Keys.Slash: return ImGuiKey.Slash;
                case Keys.Semicolon: return ImGuiKey.Semicolon;
                case Keys.Equal: return ImGuiKey.Equal;
                case Keys.LeftBracket: return ImGuiKey.LeftBracket;
                case Keys.Backslash: return ImGuiKey.Backslash;
                case Keys.RightBracket: return ImGuiKey.RightBracket;
                case Keys.GraveAccent: return ImGuiKey.GraveAccent;
                case Keys.CapsLock: return ImGuiKey.CapsLock;
                case Keys.ScrollLock: return ImGuiKey.ScrollLock;
                case Keys.NumLock: return ImGuiKey.NumLock;
                case Keys.PrintScreen: return ImGuiKey.PrintScreen;
                case Keys.Pause: return ImGuiKey.Pause;
                case Keys.KeyPadDecimal: return ImGuiKey.KeypadDecimal;
                case Keys.KeyPadDivide: return ImGuiKey.KeypadDivide;
                case Keys.KeyPadMultiply: return ImGuiKey.KeypadMultiply;
                case Keys.KeyPadSubtract: return ImGuiKey.KeypadSubtract;
                case Keys.KeyPadAdd: return ImGuiKey.KeypadAdd;
                case Keys.KeyPadEnter: return ImGuiKey.KeypadEnter;
                case Keys.KeyPadEqual: return ImGuiKey.KeypadEqual;
                case Keys.LeftShift: return ImGuiKey.LeftShift;
                case Keys.LeftControl: return ImGuiKey.LeftCtrl;
                case Keys.LeftAlt: return ImGuiKey.LeftAlt;
                case Keys.LeftSuper: return ImGuiKey.LeftSuper;
                case Keys.RightShift: return ImGuiKey.RightShift;
                case Keys.RightControl: return ImGuiKey.RightCtrl;
                case Keys.RightAlt: return ImGuiKey.RightAlt;
                case Keys.RightSuper: return ImGuiKey.RightSuper;
                case Keys.Menu: return ImGuiKey.Menu;
                default: return ImGuiKey.None;
            }
        }

        private static void SetStyle()
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
    }
}