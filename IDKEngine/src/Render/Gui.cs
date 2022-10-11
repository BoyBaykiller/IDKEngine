using System;
using ImGuiNET;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.GUI;

namespace IDKEngine.Render
{
    class Gui
    {
        enum EntityType
        {
            None = 0,
            Mesh = 1,
            Light = 2,
        }

        public enum FrameRecorderState : uint
        {
            Nothing = 0,
            Recording = 1,
            Replaying = 2,
        }

        public ImGuiBackend ImGuiBackend { get; private set; }
        private bool isInfiniteReplay;
        private bool isVideoRender;
        private int pathTracerRenderSampleGoal;

        private FrameRecorderState frameRecState;
        private EntityType selectedEntityType;
        private int selectedEntityIndex;
        private float selectedEntityDist;
        public Gui(int width, int height)
        {
            ImGuiBackend = new ImGuiBackend(width, height);
            frameRecState = FrameRecorderState.Nothing;
            isInfiniteReplay = false;
            pathTracerRenderSampleGoal = 1;
        }

        private bool isHoveredViewport = true;
        private System.Numerics.Vector2 viewportHeaderSize;
        public void Draw(Application app, float frameTime)
        {
            ImGuiBackend.Update(app, frameTime);
            ImGui.DockSpaceOverViewport();

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0.0f));
            ImGui.Begin($"Viewport");
            
            System.Numerics.Vector2 content = ImGui.GetContentRegionAvail();
            
            System.Numerics.Vector2 tileBar = ImGui.GetCursorPos();
            viewportHeaderSize = ImGui.GetWindowPos() + tileBar;

            ImGui.Image((IntPtr)app.PostProcessor.Result.ID, content, new System.Numerics.Vector2(0.0f, 1.0f), new System.Numerics.Vector2(1.0f, 0.0f));
            isHoveredViewport = ImGui.IsItemHovered();

            if (content.X != app.ViewportResolution.X || content.Y != app.ViewportResolution.Y)
            {
                app.SetViewportResolution((int)content.X, (int)content.Y);
            }

            ImGui.End();
            ImGui.PopStyleVar();

            const string FRAME_RECORDER_SECTION_NAME = "Frame Recorder";
            ImGui.Begin(FRAME_RECORDER_SECTION_NAME);
            {
                bool isRecording = frameRecState == FrameRecorderState.Recording;
                if (frameRecState == FrameRecorderState.Nothing || isRecording)
                {
                    if (ImGui.Checkbox("IsRecording (R)", ref isRecording))
                    {
                        if (isRecording)
                        {
                            app.FrameRecorder.Clear();
                            frameRecState = FrameRecorderState.Recording;
                        }
                        else
                        {
                            frameRecState = FrameRecorderState.Nothing;
                        }
                    }

                    if (frameRecState == FrameRecorderState.Recording)
                    {
                        ImGui.Text($"   * Recorded frames: {app.FrameRecorder.FrameCount}");
                        unsafe
                        {
                            ImGui.Text($"   * File size: {app.FrameRecorder.FrameCount * sizeof(RecordableState) / 1000}kb");
                        }
                    }
                    ImGui.Separator();
                }

                // TODO: Make more dynamic maybe
                const string FRAME_RECORD_DATA_PATH = "frameRecordData.frd";

                bool isReplaying = frameRecState == FrameRecorderState.Replaying;
                if ((frameRecState == FrameRecorderState.Nothing && app.FrameRecorder.FramesLoaded) || isReplaying)
                {
                    ImGui.Checkbox("IsInfite Replay", ref isInfiniteReplay);
                    if (ImGui.Checkbox("IsReplaying (Space)", ref isReplaying))
                    {
                        frameRecState = isReplaying ? FrameRecorderState.Replaying : FrameRecorderState.Nothing;
                        if (isVideoRender) isVideoRender = false;
                    }
                    ImGui.Text($"    * Replayed frames: {app.FrameRecorder.ReplayFrame} / {app.FrameRecorder.FrameCount}");
                    ImGui.Separator();
                }

                if (frameRecState == FrameRecorderState.Nothing)
                {
                    if (ImGui.Button($"Save"))
                    {
                        app.FrameRecorder.SaveToFile(FRAME_RECORD_DATA_PATH);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Load"))
                    {
                        app.FrameRecorder.Load(FRAME_RECORD_DATA_PATH);
                    }
                    ImGui.Separator();
                }

                if (frameRecState == FrameRecorderState.Nothing)
                {
                    if (!app.FrameRecorder.FramesLoaded) ImGui.BeginDisabled();
                    if (ImGui.Button($"Start Video Render##VideoRender"))
                    {
                        app.FrameRecorder.ReplayFrame = 0;
                        frameRecState = FrameRecorderState.Replaying;
                        isVideoRender = true;
                    }

                    ImGui.SameLine();
                    if (!app.FrameRecorder.FramesLoaded)
                    {
                        ImGui.EndDisabled();
                        InfoMark($"Can't start render. No Frame Record Data loaded.");
                        ImGui.BeginDisabled();
                    }
                    else
                    {
                        InfoMark($"This just replays the Frame Record Data from the first frame but outputs rendered images into a folder.");
                    }

                    if (app.IsPathTracing)
                    {
                        int tempInt = pathTracerRenderSampleGoal;
                        if (ImGui.InputInt("Path Tracing SPP", ref tempInt))
                        {
                            pathTracerRenderSampleGoal = Math.Max(1, tempInt);
                        }
                    }

                    if (!app.FrameRecorder.FramesLoaded) ImGui.EndDisabled();
                }


                ImGui.End();
            }

            ImGui.Begin("Renderer");
            {
                ImGui.Text($"FPS: {app.FPS}");
                ImGui.Text($"Viewport size: {app.ViewportResolution.X}x{app.ViewportResolution.Y}");

                if (app.IsPathTracing)
                {
                    ImGui.Text($"Samples taken: {app.PathTracer.AccumulatedSamples}");
                }

                string[] renderModes = new string[] { "Rasterizer", "PathTracer" };
                string current = app.IsPathTracing ? renderModes[1] : renderModes[0];
                if (ImGui.BeginCombo("Render Path", current))
                {
                    for (int i = 0; i < renderModes.Length; i++)
                    {
                        bool isSelected = current == renderModes[i];
                        if (ImGui.Selectable(renderModes[i], isSelected))
                        {
                            current = renderModes[i];
                            app.IsPathTracing = current == renderModes[1];
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                bool tempBool = app.PostProcessor.IsDithering;
                if (ImGui.Checkbox("IsDithering", ref tempBool))
                {
                    app.PostProcessor.IsDithering = tempBool;
                }

                if (!app.IsPathTracing)
                {
                    ImGui.Checkbox("IsWireframe", ref app.IsWireframe);

                    if (ImGui.CollapsingHeader("Variable Rate Shading"))
                    {
                        if (!VariableRateShading.NV_SHADING_RATE_IMAGE)
                        {
                            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f);
                            ImGui.BeginDisabled();
                        }
                        ImGui.Checkbox("IsVRS", ref app.IsVRSForwardRender);
                        if (!VariableRateShading.NV_SHADING_RATE_IMAGE)
                        {
                            ImGui.EndDisabled();
                            ImGui.PopStyleVar();
                        }
                        ImGui.SameLine();
                        InfoMark(
                            "Requires support for NV_shading_rate_image. " +
                            "This feature allows the engine to choose a unique shading rate " +
                            "on each 16x16 tile as a mesaure of increasing performance by decreasing fragment " +
                            "shader invocations in regions where less detail may be required."
                        );


                        string[] debugModes = new string[]
                        {
                            nameof(VariableRateShading.DebugMode.NoDebug),
                            nameof(VariableRateShading.DebugMode.ShadingRate),
                            nameof(VariableRateShading.DebugMode.Speed),
                            nameof(VariableRateShading.DebugMode.Luminance),
                            nameof(VariableRateShading.DebugMode.LuminanceVariance),
                        };

                        current = app.ForwardPassVRS.DebugValue.ToString();
                        if (ImGui.BeginCombo("DebugMode", current))
                        {
                            for (int i = 0; i < debugModes.Length; i++)
                            {
                                bool isSelected = current == debugModes[i];
                                if (ImGui.Selectable(debugModes[i], isSelected))
                                {
                                    current = debugModes[i];
                                    app.ForwardPassVRS.DebugValue = VariableRateShading.DebugMode.NoDebug + i;
                                }

                                if (isSelected)
                                    ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }

                        float tempFloat = app.ForwardPassVRS.SpeedFactor;
                        if (ImGui.SliderFloat("SpeedFactor", ref tempFloat, 0.0f, 1.0f))
                        {
                            app.ForwardPassVRS.SpeedFactor = tempFloat;
                        }

                        tempFloat = app.ForwardPassVRS.LumVarianceFactor;
                        if (ImGui.SliderFloat("LumVarianceFactor", ref tempFloat, 0.0f, 0.3f))
                        {
                            app.ForwardPassVRS.LumVarianceFactor = tempFloat;
                        }
                    }

                    if (ImGui.CollapsingHeader("VolumetricLighting"))
                    {
                        ImGui.Checkbox("IsVolumetricLighting", ref app.IsVolumetricLighting);
                        if (app.IsVolumetricLighting)
                        {
                            int tempInt = app.VolumetricLight.Samples;
                            if (ImGui.SliderInt("Samples", ref tempInt, 1, 100))
                            {
                                app.VolumetricLight.Samples = tempInt;
                            }

                            float tempFloat = app.VolumetricLight.Scattering;
                            if (ImGui.SliderFloat("Scattering", ref tempFloat, 0.0f, 1.0f))
                            {
                                app.VolumetricLight.Scattering = tempFloat;
                            }

                            tempFloat = app.VolumetricLight.Strength;
                            if (ImGui.SliderFloat("Strength", ref tempFloat, 0.0f, 500.0f))
                            {
                                app.VolumetricLight.Strength = tempFloat;
                            }

                            System.Numerics.Vector3 tempVec = app.VolumetricLight.Absorbance.ToSystemVec();
                            if (ImGui.InputFloat3("Absorbance", ref tempVec))
                            {
                                Vector3 temp = tempVec.ToOpenTKVec();
                                temp = Vector3.ComponentMax(temp, Vector3.Zero);
                                app.VolumetricLight.Absorbance = temp;
                            }

                            tempBool = app.VolumetricLight.IsTemporalAccumulation;
                            if (!app.PostProcessor.TaaEnabled) ImGui.BeginDisabled();
                            if (ImGui.Checkbox("IsTemporalAccumulation", ref tempBool))
                            {
                                app.VolumetricLight.IsTemporalAccumulation = tempBool;
                            }
                            ImGui.SameLine();
                            InfoMark(
                                "Requires TAA to be enabled. " +
                                $"When active samples are accumulated over {app.PostProcessor.TaaSamples} frames (based on TAA setting)."
                            );
                            if (!app.PostProcessor.TaaEnabled) ImGui.EndDisabled();
                        }
                    }

                    if (ImGui.CollapsingHeader("SSAO"))
                    {
                        ImGui.Checkbox("IsSSAO", ref app.IsSSAO);
                        if (app.IsSSAO)
                        {
                            int tempInt = app.SSAO.Samples;
                            if (ImGui.SliderInt("Samples  ", ref tempInt, 1, 50))
                            {
                                app.SSAO.Samples = tempInt;
                            }

                            float tempFloat = app.SSAO.Radius;
                            if (ImGui.SliderFloat("Radius", ref tempFloat, 0.0f, 0.5f))
                            {
                                app.SSAO.Radius = tempFloat;
                            }

                            tempFloat = app.SSAO.Strength;
                            if (ImGui.SliderFloat("Strength ", ref tempFloat, 0.0f, 20.0f))
                            {
                                app.SSAO.Strength = tempFloat;
                            }
                        }
                    }

                    if (ImGui.CollapsingHeader("SSR"))
                    {
                        ImGui.Checkbox("IsSSR", ref app.IsSSR);
                        if (app.IsSSR)
                        {
                            int tempInt = app.SSR.Samples;
                            if (ImGui.SliderInt("Samples ", ref tempInt, 1, 100))
                            {
                                app.SSR.Samples = tempInt;
                            }

                            tempInt = app.SSR.BinarySearchSamples;
                            if (ImGui.SliderInt("BinarySearchSamples", ref tempInt, 0, 40))
                            {
                                app.SSR.BinarySearchSamples = tempInt;
                            }

                            float tempFloat = app.SSR.MaxDist;
                            if (ImGui.SliderFloat("MaxDist", ref tempFloat, 1, 100))
                            {
                                app.SSR.MaxDist = tempFloat;
                            }
                        }
                    }

                    if (ImGui.CollapsingHeader("Shadows"))
                    {
                        ImGui.Checkbox("IsShadows", ref app.IsShadows);
                        ImGui.SameLine();
                        string appendText;
                        if (PointShadow.IS_VERTEX_LAYERED_RENDERING)
                        {
                            appendText =
                                "This system supports vertex layered rendering. " +
                                "Each pointshadow will be generated in only 1 draw call instead of 6.";
                        }
                        else
                        {
                            appendText =
                                "This system does not support vertex layered rendering. " +
                                "Each pointshadow will be generated in 6 draw calls instead of 1.";
                        }
                        InfoMark(
                            "Toggling this only controls the generation of updated shadow maps. It does not effect the use of existing shadow maps. " +
                            appendText
                        );
                    }

                    if (ImGui.CollapsingHeader("TAA"))
                    {
                        tempBool = app.PostProcessor.TaaEnabled;
                        if (ImGui.Checkbox("IsTAA", ref tempBool))
                        {
                            app.PostProcessor.TaaEnabled = tempBool;
                        }

                        if (app.PostProcessor.TaaEnabled)
                        {
                            tempBool = app.PostProcessor.IsTaaArtifactMitigation;
                            if (ImGui.Checkbox("IsTaaArtifactMitigation", ref tempBool))
                            {
                                app.PostProcessor.IsTaaArtifactMitigation = tempBool;
                            }
                            ImGui.SameLine();
                            InfoMark(
                                "This is not a feature. It's mostly for fun and you can see the output of a naive TAA resolve pass. " +
                                "In static scenes this always converges to the correct result whereas with artifact mitigation valid samples might be rejected."
                            );

                            int tempInt = app.PostProcessor.TaaSamples;
                            if (ImGui.SliderInt("Samples   ", ref tempInt, 1, GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT))
                            {
                                app.PostProcessor.TaaSamples = tempInt;
                            }
                        }
                    }
                }
                else
                {
                    if (ImGui.CollapsingHeader("PathTracing"))
                    {
                        tempBool = app.PathTracer.IsDebugBVHTraversal;
                        if (ImGui.Checkbox("IsDebugBVHTraversal", ref tempBool))
                        {
                            app.PathTracer.ResetRender();
                            app.PathTracer.IsDebugBVHTraversal = tempBool;
                        }

                        tempBool = app.PathTracer.IsTraceLights;
                        if (ImGui.Checkbox("IsTraceLights", ref tempBool))
                        {
                            app.PathTracer.ResetRender();
                            app.PathTracer.IsTraceLights = tempBool;
                        }

                        float tempFloat = app.PathTracer.RayCoherency;
                        if (ImGui.SliderFloat("RayCoherency", ref tempFloat, 0.0f, 1.0f))
                        {
                            app.PathTracer.RayCoherency = tempFloat;
                        }

                        if (!app.PathTracer.IsDebugBVHTraversal)
                        {
                            int tempInt = app.PathTracer.RayDepth;
                            if (ImGui.SliderInt("MaxRayDepth", ref tempInt, 1, 50))
                            {
                                app.PathTracer.ResetRender();
                                app.PathTracer.RayDepth = tempInt;
                            }

                            float floatTemp = app.PathTracer.FocalLength;
                            if (ImGui.InputFloat("FocalLength", ref floatTemp, 0.1f))
                            {
                                app.PathTracer.ResetRender();
                                app.PathTracer.FocalLength = MathF.Max(floatTemp, 0);
                            }

                            floatTemp = app.PathTracer.ApertureDiameter;
                            if (ImGui.InputFloat("ApertureDiameter", ref floatTemp, 0.002f))
                            {
                                app.PathTracer.ResetRender();
                                app.PathTracer.ApertureDiameter = MathF.Max(floatTemp, 0);
                            }
                        }
                    }
                }

                if (ImGui.CollapsingHeader("Bloom"))
                {
                    ImGui.Checkbox("IsBloom", ref app.IsBloom);
                    if (app.IsBloom)
                    {
                        float tempFloat = app.Bloom.Threshold;
                        if (ImGui.SliderFloat("Threshold", ref tempFloat, 0.0f, 10.0f))
                        {
                            app.Bloom.Threshold = tempFloat;
                        }

                        tempFloat = app.Bloom.Clamp;
                        if (ImGui.SliderFloat("Clamp", ref tempFloat, 0.0f, 100.0f))
                        {
                            app.Bloom.Clamp = tempFloat;
                        }

                        int tempInt = app.Bloom.MinusLods;
                        if (ImGui.SliderInt("MinusLods", ref tempInt, 0, 10))
                        {
                            app.Bloom.MinusLods = tempInt;
                        }
                    }
                }

                if (ImGui.CollapsingHeader("SkyBox"))
                {
                    bool shouldResetPT = false;

                    tempBool = SkyBoxManager.IsExternalSkyBox;
                    if (ImGui.Checkbox("IsExternalSkyBox", ref tempBool))
                    {
                        SkyBoxManager.IsExternalSkyBox = tempBool;
                        shouldResetPT = true;
                    }

                    if (!SkyBoxManager.IsExternalSkyBox)
                    {
                        int tempInt = SkyBoxManager.AtmosphericScatterer.ISteps;
                        if (ImGui.SliderInt("InScatteringSamples", ref tempInt, 1, 100))
                        {
                            SkyBoxManager.AtmosphericScatterer.ISteps = tempInt;
                            shouldResetPT = true;
                        }

                        tempInt = SkyBoxManager.AtmosphericScatterer.JSteps;
                        if (ImGui.SliderInt("DensitySamples", ref tempInt, 1, 40))
                        {
                            SkyBoxManager.AtmosphericScatterer.JSteps = tempInt;
                            shouldResetPT = true;
                        }

                        float tempFloat = SkyBoxManager.AtmosphericScatterer.Time;
                        if (ImGui.DragFloat("Time", ref tempFloat, 0.005f))
                        {
                            SkyBoxManager.AtmosphericScatterer.Time = tempFloat;
                            shouldResetPT = true;
                        }

                        tempFloat = SkyBoxManager.AtmosphericScatterer.LightIntensity;
                        if (ImGui.DragFloat("Intensity", ref tempFloat, 0.2f))
                        {
                            SkyBoxManager.AtmosphericScatterer.LightIntensity = tempFloat;

                            shouldResetPT = true;
                        }

                        if (shouldResetPT)
                        {
                            SkyBoxManager.AtmosphericScatterer.Compute();
                        }
                    }
                    if (shouldResetPT)
                    {
                        app.PathTracer.ResetRender();
                    }
                }

                ImGui.End();
            }

            ImGui.Begin("Entity properties");
            {
                if (selectedEntityType != EntityType.None)
                {
                    ImGui.Text($"{selectedEntityType}ID: {selectedEntityIndex}");
                    ImGui.Text($"Distance: {MathF.Round(selectedEntityDist, 3)}");
                }
                if (selectedEntityType == EntityType.Mesh)
                {
                    bool shouldResetPT = false;
                    ref GLSLMesh mesh = ref app.ModelSystem.Meshes[selectedEntityIndex];
                    ref readonly GLSLDrawCommand cmd = ref app.ModelSystem.DrawCommands[selectedEntityIndex];

                    ImGui.Text($"MaterialID: {mesh.MaterialIndex}");
                    ImGui.Text($"Triangle Count: {cmd.Count / 3}");
                    ImGui.SameLine(); if (ImGui.Button("Teleport to camera"))
                    {
                        app.ModelSystem.ModelMatrices[selectedEntityIndex][0] = app.ModelSystem.ModelMatrices[selectedEntityIndex][0].ClearTranslation() * Matrix4.CreateTranslation(app.Camera.Position);
                        shouldResetPT = true;
                    }
                    
                    System.Numerics.Vector3 systemVec3 = app.ModelSystem.ModelMatrices[selectedEntityIndex][0].ExtractTranslation().ToSystemVec();
                    if (ImGui.DragFloat3("Position", ref systemVec3, 0.1f))
                    {
                        shouldResetPT = true;
                        app.ModelSystem.ModelMatrices[selectedEntityIndex][0] = app.ModelSystem.ModelMatrices[selectedEntityIndex][0].ClearTranslation() * Matrix4.CreateTranslation(systemVec3.ToOpenTKVec());
                    }

                    systemVec3 = app.ModelSystem.ModelMatrices[selectedEntityIndex][0].ExtractScale().ToSystemVec();
                    if (ImGui.DragFloat3("Scale", ref systemVec3, 0.005f))
                    {
                        shouldResetPT = true;
                        Vector3 temp = Vector3.ComponentMax(systemVec3.ToOpenTKVec(), new Vector3(0.001f));
                        app.ModelSystem.ModelMatrices[selectedEntityIndex][0] = Matrix4.CreateScale(temp) * app.ModelSystem.ModelMatrices[selectedEntityIndex][0].ClearScale();
                    }

                    if (ImGui.SliderFloat("NormalMapStrength", ref mesh.NormalMapStrength, 0.0f, 4.0f))
                    {
                        shouldResetPT = true;
                    }

                    if (ImGui.SliderFloat("EmissiveBias", ref mesh.EmissiveBias, 0.0f, 20.0f))
                    {
                        shouldResetPT = true;
                    }

                    if (ImGui.SliderFloat("SpecularBias", ref mesh.SpecularBias, -1.0f, 1.0f))
                    {
                        shouldResetPT = true;
                    }

                    if (ImGui.SliderFloat("RoughnessBias", ref mesh.RoughnessBias, -1.0f, 1.0f))
                    {
                        shouldResetPT = true;
                    }

                    if (ImGui.SliderFloat("RefractionChance", ref mesh.RefractionChance, 0.0f, 1.0f))
                    {
                        shouldResetPT = true;
                    }

                    if (ImGui.SliderFloat("IOR", ref mesh.IOR, 1.0f, 5.0f))
                    {
                        shouldResetPT = true;
                    }

                    systemVec3 = mesh.Absorbance.ToSystemVec();
                    if (ImGui.InputFloat3("Absorbance", ref systemVec3))
                    {
                        Vector3 temp = systemVec3.ToOpenTKVec();
                        temp = Vector3.ComponentMax(temp, Vector3.Zero);

                        mesh.Absorbance = temp;
                        shouldResetPT = true;
                    }

                    if (shouldResetPT)
                    {
                        app.PathTracer.ResetRender();
                        app.ModelSystem.UpdateMeshBuffer(selectedEntityIndex, selectedEntityIndex + 1);
                        app.ModelSystem.UpdateModelMatricesBuffer(selectedEntityIndex, selectedEntityIndex + 1);
                    }
                }
                else if (selectedEntityType == EntityType.Light)
                {
                    bool shouldResetPT = false;
                    ref GLSLLight light = ref app.ForwardRenderer.LightingContext.Lights[selectedEntityIndex];

                    System.Numerics.Vector3 systemVec3 = light.Position.ToSystemVec();
                    if (ImGui.DragFloat3("Position", ref systemVec3, 0.1f))
                    {
                        shouldResetPT = true;
                        light.Position = systemVec3.ToOpenTKVec();
                    }

                    systemVec3 = light.Color.ToSystemVec();
                    if (ImGui.DragFloat3("Color", ref systemVec3, 0.1f, 0.0f))
                    {
                        shouldResetPT = true;
                        light.Color = systemVec3.ToOpenTKVec();
                    }

                    if (ImGui.DragFloat("Radius", ref light.Radius, 0.1f))
                    {
                        light.Radius = MathF.Max(light.Radius, 0.0f);
                        shouldResetPT = true;
                    }

                    if (shouldResetPT)
                    {
                        app.PathTracer.ResetRender();
                        app.ForwardRenderer.LightingContext.UpdateLightBuffer(selectedEntityIndex, selectedEntityIndex + 1);
                    }
                }
                else
                {
                    BothAxisCenteredText("PRESS E TO TOGGLE FREE CAMERA AND SELECT AN ENTITY");
                }

                ImGui.End();
            }

            ImGuiBackend.Render();
        }

        private static void InfoMark(string text)
        {
            ImGui.TextDisabled("(?)");

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
                ImGui.TextUnformatted(text);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        private static void BothAxisCenteredText(string text)
        {
            System.Numerics.Vector2 size = ImGui.GetWindowSize();
            System.Numerics.Vector2 textWidth = ImGui.CalcTextSize(text);
            ImGui.SetCursorPos((size - textWidth) * 0.5f);
            ImGui.Text(text);
        }

        private static void HorizontallyCenteredText(string text)
        {
            System.Numerics.Vector2 size = ImGui.GetWindowSize();
            System.Numerics.Vector2 textWidth = ImGui.CalcTextSize(text);
            ImGui.SetCursorPos(new System.Numerics.Vector2((size - textWidth).X * 0.5f, ImGui.GetCursorPos().Y));
            ImGui.Text(text);
        }

        public unsafe void Update(Application app, float dT)
        {
            if (frameRecState == FrameRecorderState.Replaying)
            {
                if (app.IsPathTracing)
                {
                    if (app.PathTracer.AccumulatedSamples >= pathTracerRenderSampleGoal)
                    {
                        RecordableState state = app.FrameRecorder.Replay();
                        app.Camera.SetState(state);
                    }
                }
                else
                {
                    RecordableState state = app.FrameRecorder.Replay();
                    app.Camera.SetState(state);
                }
                //Vector3* pixels = Helper.Malloc<Vector3>(app.PostProcessor.Result.Width * app.PostProcessor.Result.Height);
                //int bufSize = sizeof(Vector3) * app.PostProcessor.Result.Width * app.PostProcessor.Result.Height;
                //app.PostProcessor.Result.GetTextureImage(PixelFormat.Rgb, PixelType.Float, bufSize, (IntPtr)pixels);
                //Helper.Free(pixels);

                if (!isInfiniteReplay && app.FrameRecorder.ReplayFrame == 0)
                {
                    frameRecState = FrameRecorderState.Nothing;
                }
            }
            else
            {
                if (app.KeyboardState[Keys.E] == InputState.Touched && !ImGui.GetIO().WantCaptureKeyboard)
                {
                    if (app.MouseState.CursorMode == CursorModeValue.CursorDisabled)
                    {
                        app.MouseState.CursorMode = CursorModeValue.CursorNormal;
                        ImGuiBackend.IsIgnoreMouseInput = false;
                        app.Camera.Velocity = Vector3.Zero;
                    }
                    else
                    {
                        app.MouseState.CursorMode = CursorModeValue.CursorDisabled;
                        ImGuiBackend.IsIgnoreMouseInput = true;
                    }
                }

                if (app.MouseState.CursorMode == CursorModeValue.CursorDisabled)
                {
                    app.Camera.ProcessInputs(app.KeyboardState, app.MouseState, dT);
                }
            }

            if (frameRecState == FrameRecorderState.Recording)
            {
                RecordableState recordableState;
                recordableState.CamPosition = app.Camera.Position;
                recordableState.CamUp = app.Camera.Up;
                recordableState.LookX = app.Camera.LookX;
                recordableState.LookY = app.Camera.LookY;
                app.FrameRecorder.Record(recordableState);
            }

            if (frameRecState != FrameRecorderState.Replaying && app.KeyboardState[Keys.R] == InputState.Touched)
            {
                if (frameRecState == FrameRecorderState.Recording)
                {
                    frameRecState = FrameRecorderState.Nothing;
                }
                else
                {
                    frameRecState = FrameRecorderState.Recording;
                    app.FrameRecorder.Clear();
                }
            }

            if (frameRecState != FrameRecorderState.Recording && app.FrameRecorder.FramesLoaded && app.KeyboardState[Keys.Space] == InputState.Touched)
            {
                if (frameRecState == FrameRecorderState.Replaying)
                    frameRecState = FrameRecorderState.Nothing;
                else
                    frameRecState = FrameRecorderState.Replaying;

                if (frameRecState == FrameRecorderState.Replaying)
                {
                    app.MouseState.CursorMode = CursorModeValue.CursorNormal;
                    ImGuiBackend.IsIgnoreMouseInput = false;
                }
            }

            if (app.RenderGui && isHoveredViewport && app.MouseState[MouseButton.Left] == InputState.Touched)
            {
                Vector2i point = new Vector2i((int)app.MouseState.Position.X, (int)app.MouseState.Position.Y);
                if (app.RenderGui)
                {
                    point -= (Vector2i)viewportHeaderSize.ToOpenTKVec();
                }
                point.Y = app.ForwardRenderer.Result.Height - point.Y;

                Vector2 ndc = new Vector2((float)point.X / app.ForwardRenderer.Result.Width, (float)point.Y / app.ForwardRenderer.Result.Height) * 2.0f - new Vector2(1.0f);
                Ray worldSpaceRay = Ray.GetWorldSpaceRay(app.GLSLBasicData.CameraPos, app.GLSLBasicData.InvProjection, app.GLSLBasicData.InvView, ndc);
                bool hitMesh = app.BVH.Intersect(worldSpaceRay, out BVH.RayHitInfo meshHitInfo);
                bool hitLight = app.ForwardRenderer.LightingContext.Intersect(worldSpaceRay, out Lighter.HitInfo lightHitInfo);
                if (app.IsPathTracing && !app.PathTracer.IsTraceLights) hitLight = false;

                if (!hitMesh && !hitLight)
                {
                    app.ForwardRenderer.RenderMeshAABBIndex = -1;
                    selectedEntityType = EntityType.None;
                    return;
                }

                if (!hitLight) lightHitInfo.T = float.MaxValue;
                if (!hitMesh) meshHitInfo.T = float.MaxValue;

                if (meshHitInfo.T < lightHitInfo.T)
                {
                    selectedEntityType = EntityType.Mesh;
                    selectedEntityDist = meshHitInfo.T;
                    if (meshHitInfo.MeshIndex == app.ForwardRenderer.RenderMeshAABBIndex)
                    {
                        app.ForwardRenderer.RenderMeshAABBIndex = -1;
                        selectedEntityType = EntityType.None;
                    }
                    else
                    {
                        app.ForwardRenderer.RenderMeshAABBIndex = meshHitInfo.MeshIndex;
                    }
                    selectedEntityIndex = meshHitInfo.MeshIndex;
                }
                else
                {
                    selectedEntityType = EntityType.Light;
                    selectedEntityDist = lightHitInfo.T;
                    selectedEntityIndex = lightHitInfo.HitID;
                    app.ForwardRenderer.RenderMeshAABBIndex = -1;
                }
            }
        }
    }
}
