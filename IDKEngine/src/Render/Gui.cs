using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ImGuiNET;
using IDKEngine.GUI;
using IDKEngine.Shapes;

namespace IDKEngine.Render
{
    class Gui : IDisposable
    {
        public enum EntityType : int
        {
            None,
            Mesh,
            Light,
        }

        public enum FrameRecorderState : int
        {
            Nothing,
            Recording,
            Replaying,
        }

        public struct SelectedEntityInfo
        {
            public EntityType Type;
            public int Index;
            public float Distance;
        }

        public ImGuiBackend Backend { get; private set; }
        public FrameRecorderState FrameRecState { get; private set; }

        public SelectedEntityInfo SelectedEntity;

        private int recordingFPS;
        private bool isInfiniteReplay;
        private bool isVideoRender;
        private int recordingRenderSampleGoal;
        public Gui(int width, int height)
        {
            Backend = new ImGuiBackend(width, height);
            FrameRecState = FrameRecorderState.Nothing;
            isInfiniteReplay = false;
            recordingRenderSampleGoal = 1;
            recordingFPS = 48;
            recordingTimer = Stopwatch.StartNew();
        }

        private System.Numerics.Vector2 viewportHeaderSize;
        public void Draw(Application app, float frameTime)
        {
            if (app.MouseState.CursorMode == CursorModeValue.CursorNormal)
            {
                Backend.IsIgnoreMouseInput = false;
            }
            else if (app.MouseState.CursorMode == CursorModeValue.CursorDisabled)
            {
                Backend.IsIgnoreMouseInput = true;
            }

            Backend.Update(app, frameTime);
            ImGui.DockSpaceOverViewport();

            int tempInt;
            bool tempBool;
            float tempFloat;
            System.Numerics.Vector2 tempVec2;
            System.Numerics.Vector3 tempVec3;

            
            if (ImGui.Begin("Camera"))
            {
                if (ImGui.CollapsingHeader("Collision Detection"))
                {
                    ImGui.Checkbox("IsEnabled", ref app.CamCollisionSettings.IsEnabled);
                    ImGui.SliderInt("TestSteps", ref app.CamCollisionSettings.TestSteps, 1, 20);
                    ImGui.SliderInt("ResponseSteps", ref app.CamCollisionSettings.ResponseSteps, 1, 20);
                    ImGui.SliderFloat("NormalOffset", ref app.CamCollisionSettings.EpsilonNormalOffset, 0.0f, 0.01f, "%.4g");
                }

                if (ImGui.CollapsingHeader("Controls"))
                {
                    tempVec3 = app.Camera.Position.ToNumerics();
                    if (ImGui.DragFloat3("Position", ref tempVec3))
                    {
                        app.Camera.Position = tempVec3.ToOpenTK();
                    }

                    ImGui.SliderFloat("Speed", ref app.Camera.KeyboardAccelerationSpeed, 0.0f, 50.0f);
                    ImGui.SliderFloat("Sensitivity", ref app.Camera.MouseSensitivity, 0.0f, 0.1f);

                    tempFloat = MathHelper.RadiansToDegrees(app.CameraFovY);
                    if (ImGui.SliderFloat("FovY", ref tempFloat, 10.0f, 130.0f))
                    {
                        app.CameraFovY = MathHelper.DegreesToRadians(tempFloat);
                    }

                    ImGui.Checkbox("HasGravity", ref app.HasGravity );
                    if (app.HasGravity)
                    {
                        ImGui.SliderFloat("Gravity", ref app.GravityDownForce, 0.0f, 100.0f);
                    }
                }

                ImGui.End();
            }


            bool shouldResetPT = false;
            ImGui.Begin("Frame Recorder");
            {
                if (FrameRecState != FrameRecorderState.Replaying)
                {
                    bool isRecording = FrameRecState == FrameRecorderState.Recording;
                    ImGui.Text($"Is Recording (Press {Keys.LeftControl} + {Keys.R}): {isRecording}");

                    if (ImGui.InputInt("Recording FPS", ref recordingFPS))
                    {
                        recordingFPS = Math.Max(5, recordingFPS);
                    }

                    if (FrameRecState == FrameRecorderState.Recording)
                    {
                        ImGui.Text($"   * Recorded frames: {app.FrameRecorder.FrameCount}");
                        unsafe
                        {
                            ImGui.Text($"   * File size: {app.FrameRecorder.FrameCount * sizeof(FrameState) / 1000}kb");
                        }
                    }
                    ImGui.Separator();
                }
                
                bool isReplaying = FrameRecState == FrameRecorderState.Replaying;
                if ((FrameRecState == FrameRecorderState.Nothing && app.FrameRecorder.IsFramesLoaded) || isReplaying)
                {
                    ImGui.Text($"Is Replaying (Press {Keys.LeftControl} + {Keys.Space}): {isReplaying}");
                    ImGui.Checkbox("Is Infite Replay", ref isInfiniteReplay);
                    
                    ImGui.Checkbox("Is Video Render", ref isVideoRender);
                    ToolTipForItemAboveHovered("When enabled rendered images are saved into a folder.");

                    tempInt = app.FrameRecorder.ReplayFrameIndex;
                    if (ImGui.SliderInt("ReplayFrame", ref tempInt, 0, app.FrameRecorder.FrameCount - 1))
                    {
                        app.FrameRecorder.ReplayFrameIndex = tempInt;

                        FrameState state = app.FrameRecorder[app.FrameRecorder.ReplayFrameIndex];
                        app.Camera.Position = state.Position;
                        app.Camera.UpVector = state.UpVector;
                        app.Camera.LookX = state.LookX;
                        app.Camera.LookY = state.LookY;
                    }
                    ImGui.Separator();

                    if (app.RenderMode == RenderMode.PathTracer)
                    {
                        tempInt = recordingRenderSampleGoal;
                        if (ImGui.InputInt("Path Tracing SPP", ref tempInt))
                        {
                            recordingRenderSampleGoal = Math.Max(1, tempInt);
                        }
                    }
                    ImGui.Separator();
                }

                if (FrameRecState == FrameRecorderState.Nothing)
                {
                    const string FRAME_RECORDER_FILE_PATH = "frameRecordData.frd";
                    if (ImGui.Button($"Save"))
                    {
                        app.FrameRecorder.SaveToFile(FRAME_RECORDER_FILE_PATH);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Load"))
                    {
                        app.FrameRecorder.Load(FRAME_RECORDER_FILE_PATH);
                    }
                    ImGui.Separator();
                }


                ImGui.End();
            }

            ImGui.Begin("Renderer");
            {
                ImGui.Text($"FPS: {app.FPS}");
                ImGui.Text($"Viewport size: {app.RenderPresentationResolution.X}x{app.RenderPresentationResolution.Y}");
                ImGui.Text($"{Helper.GPU}");
                ImGui.Text($"Triangles: {app.ModelSystem.TriangleCount}");

                if (app.RenderMode == RenderMode.PathTracer)
                {
                    ImGui.Text($"Samples taken: {app.PathTracer.AccumulatedSamples}");
                }

                tempFloat = app.TonemapAndGamma.Gamma;
                if (ImGui.SliderFloat("Gamma", ref tempFloat, 0.1f, 3.0f))
                {
                    app.TonemapAndGamma.Gamma = tempFloat;
                }

                tempFloat = app.ResolutionScale;
                if (ImGui.SliderFloat("ResolutionScale", ref tempFloat, 0.1f, 1.0f))
                {
                    app.ResolutionScale = Math.Max(tempFloat, 0.1f);
                }

                string current = app.RenderMode.ToString();
                if (ImGui.BeginCombo("Render Mode", current))
                {
                    RenderMode[] renderModes = (RenderMode[])Enum.GetValues(typeof(RenderMode));
                    for (int i = 0; i < renderModes.Length; i++)
                    {
                        string enumName = renderModes[i].ToString();
                        bool isSelected = current == enumName;
                        if (ImGui.Selectable(enumName, isSelected))
                        {
                            current = enumName;
                            app.RenderMode = (RenderMode)i;
                        }

                        if (isSelected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                    ImGui.EndCombo();
                }
                ImGui.Separator();

                if (app.RenderMode == RenderMode.Rasterizer)
                {
                    ImGui.Checkbox("IsWireframe", ref app.RasterizerPipeline.IsWireframe);

                    if (ImGui.CollapsingHeader("Voxel Global Illumination"))
                    {
                        tempBool = app.RasterizerPipeline.IsVXGI;
                        if (ImGui.Checkbox("IsVXGI", ref tempBool))
                        {
                            app.RasterizerPipeline.IsVXGI = tempBool;
                        }

                        if (app.RasterizerPipeline.IsVXGI)
                        {
                            ToolTipForItemAboveHovered("Controls wether the scene is re-voxelized every frame");
                            ImGui.Checkbox("ShouldVoxelize", ref app.RasterizerPipeline.ShouldVoxelize);

                            ImGui.Checkbox("IsConfigureGrid", ref app.RasterizerPipeline.IsConfigureGrid);
                            ToolTipForItemAboveHovered(
                                "Allows to change the size of the VXGI grid.\n" +
                                "It defines the space the VXGI Lighting algorithm is applied over.\n" +
                                "This needs to be set manually. The green box marks the grid."
                            );

                            string[] resolutions = new string[] { "512", "384", "256", "128", "64" };
                            current = app.RasterizerPipeline.Voxelizer.ResultVoxelsAlbedo.Width.ToString();
                            if (ImGui.BeginCombo("Resolution", current))
                            {
                                for (int i = 0; i < resolutions.Length; i++)
                                {
                                    bool isSelected = current == resolutions[i];
                                    if (ImGui.Selectable(resolutions[i], isSelected))
                                    {
                                        current = resolutions[i];
                                        int size = Convert.ToInt32(current);
                                        app.RasterizerPipeline.Voxelizer.SetSize(size, size, size);
                                    }

                                    if (isSelected)
                                    {
                                        ImGui.SetItemDefaultFocus();
                                    }
                                }
                                ImGui.EndCombo();
                            }

                            if (app.RasterizerPipeline.IsConfigureGrid)
                            {
                                tempVec3 = app.RasterizerPipeline.Voxelizer.GridMin.ToNumerics();
                                if (ImGui.DragFloat3("Grid Min", ref tempVec3, 0.1f))
                                {
                                    app.RasterizerPipeline.Voxelizer.GridMin = tempVec3.ToOpenTK();
                                }

                                tempVec3 = app.RasterizerPipeline.Voxelizer.GridMax.ToNumerics();
                                if (ImGui.DragFloat3("Grid Max", ref tempVec3, 0.1f))
                                {
                                    app.RasterizerPipeline.Voxelizer.GridMax = tempVec3.ToOpenTK();
                                }

                                tempFloat = app.RasterizerPipeline.Voxelizer.DebugStepMultiplier;
                                if (ImGui.SliderFloat("DebugStepMultiplier", ref tempFloat, 0.05f, 1.0f))
                                {
                                    tempFloat = MathF.Max(tempFloat, 0.05f);
                                    app.RasterizerPipeline.Voxelizer.DebugStepMultiplier = tempFloat;
                                }

                                tempFloat = app.RasterizerPipeline.Voxelizer.DebugConeAngle;
                                if (ImGui.SliderFloat("DebugConeAngle", ref tempFloat, 0, 0.5f))
                                {
                                    app.RasterizerPipeline.Voxelizer.DebugConeAngle = tempFloat;
                                }
                            }
                            else
                            {
                                tempFloat = app.RasterizerPipeline.ConeTracer.NormalRayOffset;
                                if (ImGui.SliderFloat("NormalRayOffset", ref tempFloat, 1.0f, 3.0f))
                                {
                                    app.RasterizerPipeline.ConeTracer.NormalRayOffset = tempFloat;
                                }

                                tempInt = app.RasterizerPipeline.ConeTracer.MaxSamples;
                                if (ImGui.SliderInt("MaxSamples", ref tempInt, 1, 24))
                                {
                                    app.RasterizerPipeline.ConeTracer.MaxSamples = tempInt;
                                }

                                tempFloat = app.RasterizerPipeline.ConeTracer.GIBoost;
                                if (ImGui.SliderFloat("GIBoost", ref tempFloat, 0.0f, 5.0f))
                                {
                                    app.RasterizerPipeline.ConeTracer.GIBoost = tempFloat;
                                }

                                tempFloat = app.RasterizerPipeline.ConeTracer.GISkyBoxBoost;
                                if (ImGui.SliderFloat("GISkyBoxBoost", ref tempFloat, 0.0f, 5.0f))
                                {
                                    app.RasterizerPipeline.ConeTracer.GISkyBoxBoost = tempFloat;
                                }

                                tempFloat = app.RasterizerPipeline.ConeTracer.StepMultiplier;
                                if (ImGui.SliderFloat("StepMultiplier", ref tempFloat, 0.01f, 1.0f))
                                {
                                    app.RasterizerPipeline.ConeTracer.StepMultiplier = MathF.Max(tempFloat, 0.01f);
                                }

                                tempBool = app.RasterizerPipeline.ConeTracer.IsTemporalAccumulation;
                                if (ImGui.Checkbox("IsTemporalAccumulation##0", ref tempBool))
                                {
                                    app.RasterizerPipeline.ConeTracer.IsTemporalAccumulation = tempBool;
                                }
                                ToolTipForItemAboveHovered(
                                    $"When active samples are accumulated over multiple frames.\n" +
                                    "If there is no Temporal Anti Aliasing this is treated as being disabled."
                                );
                            }

                            if (!Voxelizer.HAS_CONSERVATIVE_RASTER) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f); ImGui.BeginDisabled(); }
                            ImGui.Checkbox("IsConservativeRasterization", ref app.RasterizerPipeline.Voxelizer.IsConservativeRasterization);
                            if (!Voxelizer.HAS_CONSERVATIVE_RASTER) { ImGui.EndDisabled(); ImGui.PopStyleVar(); }

                            ImGui.Text($"NV_conservative_raster: {Voxelizer.HAS_CONSERVATIVE_RASTER}");
                            ToolTipForItemAboveHovered(
                                "Allows to make the rasterizer invoke the fragment shader even if a pixel is only partially covered.\n" +
                                "Currently there is some bug with this which causes overly bright voxels."
                            );

                            ImGui.Text($"TAKE_FAST_GEOMETRY_SHADER_PATH: {Voxelizer.TAKE_FAST_GEOMETRY_SHADER_PATH}");
                            ToolTipForItemAboveHovered(
                                "Combination of NV_geometry_shader_passthrough and NV_viewport_swizzle to take advantage of a fast \"passthrough geometry\" shader instead of having to render the scene 3 times.\n" +
                                "Regular geometry shaders were even slower which is why I decided to avoided them entirely."
                            );

                            ImGui.Text($"NV_shader_atomic_fp16_vector: {Voxelizer.HAS_ATOMIC_FP16_VECTOR}");
                            ToolTipForItemAboveHovered(
                                "Allows to perform atomics on fp16 images without having to emulate such behaviour.\n" +
                                "Most noticeably without this extension voxelizing requires 2.5x times the memory."
                            );
                        }
                    }

                    if (ImGui.CollapsingHeader("Variable Rate Shading"))
                    {
                        ImGui.Text($"NV_shading_rate_image: {VariableRateShading.HAS_VARIABLE_RATE_SHADING}");
                        ToolTipForItemAboveHovered(
                            "Allows the renderer to choose a unique shading rate on each 16x16 tile\n" +
                            "as a mesaure of increasing performance by decreasing fragment shader\n" +
                            "invocations in regions where less detail may be required."
                        );

                        if (!VariableRateShading.HAS_VARIABLE_RATE_SHADING) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f); ImGui.BeginDisabled(); }
                        ImGui.Checkbox("IsVariableRateShading", ref app.RasterizerPipeline.IsVariableRateShading);
                        if (!VariableRateShading.HAS_VARIABLE_RATE_SHADING) { ImGui.EndDisabled(); ImGui.PopStyleVar(); }

                        current = app.RasterizerPipeline.LightingVRS.DebugValue.ToString();
                        if (ImGui.BeginCombo("DebugMode", current))
                        {
                            LightingShadingRateClassifier.DebugMode[] debugModes = (LightingShadingRateClassifier.DebugMode[])Enum.GetValues(typeof(LightingShadingRateClassifier.DebugMode));
                            for (int i = 0; i < debugModes.Length; i++)
                            {
                                string enumName = debugModes[i].ToString();

                                bool isSelected = current == enumName;
                                if (ImGui.Selectable(enumName, isSelected))
                                {
                                    current = enumName;
                                    app.RasterizerPipeline.LightingVRS.DebugValue = LightingShadingRateClassifier.DebugMode.NoDebug + i;
                                }

                                if (isSelected)
                                {
                                    ImGui.SetItemDefaultFocus();
                                }
                            }
                            ImGui.EndCombo();
                        }

                        tempFloat = app.RasterizerPipeline.LightingVRS.SpeedFactor;
                        if (ImGui.SliderFloat("SpeedFactor", ref tempFloat, 0.0f, 1.0f))
                        {
                            app.RasterizerPipeline.LightingVRS.SpeedFactor = tempFloat;
                        }

                        tempFloat = app.RasterizerPipeline.LightingVRS.LumVarianceFactor;
                        if (ImGui.SliderFloat("LumVarianceFactor", ref tempFloat, 0.0f, 0.3f))
                        {
                            app.RasterizerPipeline.LightingVRS.LumVarianceFactor = tempFloat;
                        }
                    }

                    if (ImGui.CollapsingHeader("VolumetricLighting"))
                    {
                        ImGui.Checkbox("IsVolumetricLighting", ref app.RasterizerPipeline.IsVolumetricLighting);
                        if (app.RasterizerPipeline.IsVolumetricLighting)
                        {
                            tempInt = app.RasterizerPipeline.VolumetricLight.Samples;
                            if (ImGui.SliderInt("Samples##0", ref tempInt, 1, 100))
                            {
                                app.RasterizerPipeline.VolumetricLight.Samples = tempInt;
                            }

                            tempFloat = app.RasterizerPipeline.VolumetricLight.Scattering;
                            if (ImGui.SliderFloat("Scattering", ref tempFloat, 0.0f, 1.0f))
                            {
                                app.RasterizerPipeline.VolumetricLight.Scattering = tempFloat;
                            }

                            tempFloat = app.RasterizerPipeline.VolumetricLight.Strength;
                            if (ImGui.SliderFloat("Strength##0", ref tempFloat, 0.0f, 500.0f))
                            {
                                app.RasterizerPipeline.VolumetricLight.Strength = tempFloat;
                            }

                            System.Numerics.Vector3 tempVec = app.RasterizerPipeline.VolumetricLight.Absorbance.ToNumerics();
                            if (ImGui.InputFloat3("Absorbance", ref tempVec))
                            {
                                Vector3 temp = tempVec.ToOpenTK();
                                temp = Vector3.ComponentMax(temp, Vector3.Zero);
                                app.RasterizerPipeline.VolumetricLight.Absorbance = temp;
                            }

                            tempBool = app.RasterizerPipeline.VolumetricLight.IsTemporalAccumulation;
                            if (ImGui.Checkbox("IsTemporalAccumulation", ref tempBool))
                            {
                                app.RasterizerPipeline.VolumetricLight.IsTemporalAccumulation = tempBool;
                            }
                            ToolTipForItemAboveHovered(
                                $"When active samples are accumulated over multiple frames.\n" +
                                "If there is no Temporal Anti Aliasing this is treated as being disabled."
                            );
                        }
                    }

                    if (ImGui.CollapsingHeader("Anti Aliasing"))
                    {
                        current = app.TemporalAntiAliasingMode.ToString();
                        if (ImGui.BeginCombo("Mode", current))
                        {
                            TemporalAntiAliasingMode[] options = (TemporalAntiAliasingMode[])Enum.GetValues(typeof(TemporalAntiAliasingMode));
                            for (int i = 0; i < options.Length; i++)
                            {
                                string enumName = options[i].ToString();
                                bool isSelected = current == enumName;
                                if (ImGui.Selectable(enumName, isSelected))
                                {
                                    current = enumName;
                                    app.TemporalAntiAliasingMode = (TemporalAntiAliasingMode)i;
                                }

                                if (isSelected)
                                {
                                    ImGui.SetItemDefaultFocus();
                                }
                            }
                            ImGui.EndCombo();
                        }

                        if (app.TemporalAntiAliasingMode == TemporalAntiAliasingMode.TAA)
                        {
                            tempBool = app.TaaResolve.IsTaaArtifactMitigation;
                            if (ImGui.Checkbox("IsTaaArtifactMitigation", ref tempBool))
                            {
                                app.TaaResolve.IsTaaArtifactMitigation = tempBool;
                            }
                            ToolTipForItemAboveHovered(
                                "This is not a feature. It's mostly for fun and you can see the output of a naive TAA resolve pass.\n" +
                                "In static scenes this always converges to the correct result whereas with artifact mitigation valid samples might be rejected."
                            );

                            ImGui.SliderInt("Samples##1", ref app.TAASamples, 1, 36);
                        }
                        else if (app.TemporalAntiAliasingMode == TemporalAntiAliasingMode.FSR2)
                        {
                            ImGui.Text(
                                "FSR2 (by AMD) does Anti Aliasing but\n" +
                                "simultaneously also upscaling.\n" +
                                "Try reducing resolution scale!\n" +
                                "Note: Performance is lower than expected\n" +
                                "on NVIDIA!"
                            );

                            ImGui.Checkbox("IsSharpening", ref app.FSR2Wrapper.IsSharpening);


                            if (app.FSR2Wrapper.IsSharpening)
                            {
                                ImGui.SliderFloat("Sharpness", ref app.FSR2Wrapper.Sharpness, 0.0f, 1.0f);
                            }
                        }
                    }

                    if (ImGui.CollapsingHeader("SSAO"))
                    {
                        ImGui.Checkbox("IsSSAO", ref app.RasterizerPipeline.IsSSAO);
                        if (app.RasterizerPipeline.IsSSAO)
                        {
                            tempInt = app.RasterizerPipeline.SSAO.Samples;
                            if (ImGui.SliderInt("Samples##2", ref tempInt, 1, 50))
                            {
                                app.RasterizerPipeline.SSAO.Samples = tempInt;
                            }

                            tempFloat = app.RasterizerPipeline.SSAO.Radius;
                            if (ImGui.SliderFloat("Radius", ref tempFloat, 0.0f, 0.5f))
                            {
                                app.RasterizerPipeline.SSAO.Radius = tempFloat;
                            }

                            tempFloat = app.RasterizerPipeline.SSAO.Strength;
                            if (ImGui.SliderFloat("Strength##1", ref tempFloat, 0.0f, 20.0f))
                            {
                                app.RasterizerPipeline.SSAO.Strength = tempFloat;
                            }
                        }
                    }

                    if (ImGui.CollapsingHeader("SSR"))
                    {
                        ImGui.Checkbox("IsSSR", ref app.RasterizerPipeline.IsSSR);
                        if (app.RasterizerPipeline.IsSSR)
                        {
                            tempInt = app.RasterizerPipeline.SSR.Samples;
                            if (ImGui.SliderInt("Samples##3", ref tempInt, 1, 100))
                            {
                                app.RasterizerPipeline.SSR.Samples = tempInt;
                            }

                            tempInt = app.RasterizerPipeline.SSR.BinarySearchSamples;
                            if (ImGui.SliderInt("BinarySearchSamples", ref tempInt, 0, 40))
                            {
                                app.RasterizerPipeline.SSR.BinarySearchSamples = tempInt;
                            }

                            tempFloat = app.RasterizerPipeline.SSR.MaxDist;
                            if (ImGui.SliderFloat("MaxDist", ref tempFloat, 1, 100))
                            {
                                app.RasterizerPipeline.SSR.MaxDist = tempFloat;
                            }
                        }
                    }

                    if (ImGui.CollapsingHeader("Shadows"))
                    {
                        ImGui.Checkbox("IsShadows", ref app.IsShadows);
                        ToolTipForItemAboveHovered("Toggling this controls the generation of new shadow maps. It does not effect the use of existing shadow maps.");

                        ImGui.Text($"HAS_VERTEX_LAYERED_RENDERING: {PointShadowManager.TAKE_VERTEX_LAYERED_RENDERING_PATH}");
                        ToolTipForItemAboveHovered("Uses (ARB_shader_viewport_layer_array or NV_viewport_array2 or AMD_vertex_shader_layer) to generate point shadows in only 1 draw call instead of 6.");
                    }
                }
                else if (app.RenderMode == RenderMode.PathTracer)
                {
                    if (ImGui.CollapsingHeader("PathTracing"))
                    {
                        tempBool = app.PathTracer.IsDebugBVHTraversal;
                        if (ImGui.Checkbox("IsDebugBVHTraversal", ref tempBool))
                        {
                            app.PathTracer.IsDebugBVHTraversal = tempBool;
                        }

                        tempBool = app.PathTracer.IsTraceLights;
                        if (ImGui.Checkbox("IsTraceLights", ref tempBool))
                        {
                            app.PathTracer.IsTraceLights = tempBool;
                        }

                        if (!app.PathTracer.IsDebugBVHTraversal)
                        {
                            tempInt = app.PathTracer.RayDepth;
                            if (ImGui.SliderInt("MaxRayDepth", ref tempInt, 1, 50))
                            {
                                app.PathTracer.RayDepth = tempInt;
                            }

                            float floatTemp = app.PathTracer.FocalLength;
                            if (ImGui.InputFloat("FocalLength", ref floatTemp, 0.1f))
                            {
                                app.PathTracer.FocalLength = floatTemp;
                            }

                            floatTemp = app.PathTracer.LenseRadius;
                            if (ImGui.InputFloat("LenseRadius", ref floatTemp, 0.002f))
                            {
                                app.PathTracer.LenseRadius = floatTemp;
                            }
                        }
                    }
                }

                if (ImGui.CollapsingHeader("Bloom"))
                {
                    ImGui.Checkbox("IsBloom", ref app.IsBloom);
                    if (app.IsBloom)
                    {
                        tempFloat = app.Bloom.Threshold;
                        if (ImGui.SliderFloat("Threshold", ref tempFloat, 0.0f, 10.0f))
                        {
                            app.Bloom.Threshold = tempFloat;
                        }

                        tempFloat = app.Bloom.Clamp;
                        if (ImGui.SliderFloat("Clamp", ref tempFloat, 0.0f, 100.0f))
                        {
                            app.Bloom.Clamp = tempFloat;
                        }

                        tempInt = app.Bloom.MinusLods;
                        if (ImGui.SliderInt("MinusLods", ref tempInt, 0, 10))
                        {
                            app.Bloom.MinusLods = tempInt;
                        }
                    }
                }

                if (ImGui.CollapsingHeader("SkyBox"))
                {
                    bool shouldUpdateSkyBox = false;

                    tempBool = SkyBoxManager.IsExternalSkyBox;
                    if (ImGui.Checkbox("IsExternalSkyBox", ref tempBool))
                    {
                        SkyBoxManager.IsExternalSkyBox = tempBool;
                        shouldResetPT = true;
                    }

                    if (!SkyBoxManager.IsExternalSkyBox)
                    {
                        tempInt = SkyBoxManager.AtmosphericScatterer.ISteps;
                        if (ImGui.SliderInt("InScatteringSamples", ref tempInt, 1, 100))
                        {
                            SkyBoxManager.AtmosphericScatterer.ISteps = tempInt;
                            shouldUpdateSkyBox = true;
                        }

                        tempInt = SkyBoxManager.AtmosphericScatterer.JSteps;
                        if (ImGui.SliderInt("DensitySamples", ref tempInt, 1, 40))
                        {
                            SkyBoxManager.AtmosphericScatterer.JSteps = tempInt;
                            shouldUpdateSkyBox = true;
                        }

                        tempFloat = SkyBoxManager.AtmosphericScatterer.Time;
                        if (ImGui.DragFloat("Time", ref tempFloat, 0.005f))
                        {
                            SkyBoxManager.AtmosphericScatterer.Time = tempFloat;
                            shouldUpdateSkyBox = true;
                        }

                        tempFloat = SkyBoxManager.AtmosphericScatterer.LightIntensity;
                        if (ImGui.DragFloat("Intensity", ref tempFloat, 0.2f))
                        {
                            SkyBoxManager.AtmosphericScatterer.LightIntensity = tempFloat;

                            shouldUpdateSkyBox = true;
                        }
                        if (shouldUpdateSkyBox)
                        {
                            shouldResetPT = true;
                            SkyBoxManager.AtmosphericScatterer.Compute();
                        }
                    }
                }

                ImGui.End();
            }

            ImGui.Begin("Entity Add");
            {
                if (ImGui.Button("Add light"))
                {
                    Ray worldSpaceRay = Ray.GetWorldSpaceRay(app.GpuBasicData.CameraPos, app.GpuBasicData.InvProjection, app.GpuBasicData.InvView, new Vector2(0.0f));
                    Vector3 spawnPoint = worldSpaceRay.Origin + worldSpaceRay.Direction * 1.5f;

                    Light light = new Light(spawnPoint, new Vector3(Helper.RandomVec3(5.0f, 7.0f)), 0.3f);
                    if (app.LightManager.AddLight(light))
                    {
                        SelectedEntity.Type = EntityType.Light;
                        SelectedEntity.Index = app.LightManager.Count - 1;
                        shouldResetPT = true;
                    }
                }

                if (ImGui.Button("Load model"))
                {
                    NativeFileDialogExtendedSharp.NfdFilter[] filter = new NativeFileDialogExtendedSharp.NfdFilter[]
                    {
                        new NativeFileDialogExtendedSharp.NfdFilter() { Specification = "gltf"}
                    };
                    NativeFileDialogExtendedSharp.NfdDialogResult result = NativeFileDialogExtendedSharp.Nfd.FileOpen(filter);
                    if (result.Status == NativeFileDialogExtendedSharp.NfdStatus.Error)
                    {
                        Logger.Log(Logger.LogLevel.Error, result.Error);
                    }
                    else if (result.Status != NativeFileDialogExtendedSharp.NfdStatus.Cancel)
                    {
                        Model newModel = new Model(result.Path);
                        app.ModelSystem.Add(newModel);

                        SelectedEntity.Index = app.ModelSystem.Meshes.Length - 1;
                        SelectedEntity.Type = EntityType.Mesh; 
                        ref readonly GpuDrawElementsCmd cmd = ref app.ModelSystem.DrawCommands[SelectedEntity.Index];
                        Vector3 position = app.ModelSystem.MeshInstances[cmd.BaseInstance].ModelMatrix.ExtractTranslation();
                        SelectedEntity.Distance = Vector3.Distance(position, app.Camera.Position);

                        shouldResetPT = true;
                    }
                }
            }

            ImGui.Begin("Entity Properties");
            {
                if (SelectedEntity.Type != EntityType.None)
                {
                    ImGui.Text($"{SelectedEntity.Type}ID: {SelectedEntity.Index}");
                    ImGui.Text($"Distance: {MathF.Round(SelectedEntity.Distance, 3)}");
                }
                if (SelectedEntity.Type == EntityType.Mesh)
                {
                    bool shouldUpdateMesh = false;
                    ref readonly GpuDrawElementsCmd cmd = ref app.ModelSystem.DrawCommands[SelectedEntity.Index];
                    ref GpuMesh mesh = ref app.ModelSystem.Meshes[SelectedEntity.Index];
                    ref GpuMeshInstance meshInstance = ref app.ModelSystem.MeshInstances[cmd.BaseInstance];

                    ImGui.Text($"MaterialID: {mesh.MaterialIndex}");
                    ImGui.Text($"Triangle Count: {cmd.Count / 3}");
                    ImGui.SameLine(); if (ImGui.Button("Teleport to camera"))
                    {
                        meshInstance.ModelMatrix = meshInstance.ModelMatrix.ClearTranslation() * Matrix4.CreateTranslation(app.Camera.Position);
                        shouldUpdateMesh = true;
                    }

                    Vector3 beforeTranslation = meshInstance.ModelMatrix.ExtractTranslation();
                    tempVec3 = beforeTranslation.ToNumerics();
                    if (ImGui.DragFloat3("Position", ref tempVec3, 0.1f))
                    {
                        shouldUpdateMesh = true;
                        Vector3 dif = tempVec3.ToOpenTK() - beforeTranslation;
                        meshInstance.ModelMatrix *= Matrix4.CreateTranslation(dif);
                    }

                    tempVec3 = meshInstance.ModelMatrix.ExtractScale().ToNumerics();
                    if (ImGui.DragFloat3("Scale", ref tempVec3, 0.005f))
                    {
                        shouldUpdateMesh = true;
                        Vector3 temp = Vector3.ComponentMax(tempVec3.ToOpenTK(), new Vector3(0.001f));
                        meshInstance.ModelMatrix = Matrix4.CreateScale(temp) * meshInstance.ModelMatrix.ClearScale();
                    }

                    meshInstance.ModelMatrix.ExtractRotation().ToEulerAngles(out Vector3 beforeAngles);
                    tempVec3 = beforeAngles.ToNumerics();
                    if (ImGui.DragFloat3("Rotation", ref tempVec3, 0.005f))
                    {
                        shouldUpdateMesh = true;
                        Vector3 dif = tempVec3.ToOpenTK() - beforeAngles;
                        meshInstance.ModelMatrix *= Matrix4.CreateRotationZ(dif.Z) *
                                                    Matrix4.CreateRotationY(dif.Y) *
                                                    Matrix4.CreateRotationX(dif.X);
                    }

                    if (ImGui.SliderFloat("NormalMapStrength", ref mesh.NormalMapStrength, 0.0f, 4.0f))
                    {
                        shouldUpdateMesh = true;
                    }

                    if (ImGui.SliderFloat("EmissiveBias", ref mesh.EmissiveBias, 0.0f, 20.0f))
                    {
                        shouldUpdateMesh = true;
                    }

                    if (ImGui.SliderFloat("SpecularBias", ref mesh.SpecularBias, -1.0f, 1.0f))
                    {
                        shouldUpdateMesh = true;
                    }

                    if (ImGui.SliderFloat("RoughnessBias", ref mesh.RoughnessBias, -1.0f, 1.0f))
                    {
                        shouldUpdateMesh = true;
                    }

                    if (ImGui.SliderFloat("RefractionChance", ref mesh.RefractionChance, 0.0f, 1.0f))
                    {
                        shouldUpdateMesh = true;
                    }

                    if (ImGui.SliderFloat("IOR", ref mesh.IOR, 1.0f, 5.0f))
                    {
                        shouldUpdateMesh = true;
                    }

                    tempVec3 = mesh.Absorbance.ToNumerics();
                    if (ImGui.InputFloat3("Absorbance", ref tempVec3))
                    {
                        Vector3 temp = tempVec3.ToOpenTK();
                        temp = Vector3.ComponentMax(temp, Vector3.Zero);

                        mesh.Absorbance = temp;
                        shouldUpdateMesh = true;
                    }

                    if (shouldUpdateMesh)
                    {
                        shouldResetPT = true;
                        app.ModelSystem.UpdateMeshBuffer(SelectedEntity.Index, 1);
                    }
                }
                else if (SelectedEntity.Type == EntityType.Light)
                {
                    bool shouldUpdateLight = false;
                    
                    app.LightManager.TryGetLight(SelectedEntity.Index, out Light abstractLight);
                    ref GpuLight light = ref abstractLight.GpuLight;

                    if (ImGui.Button("Delete"))
                    {
                        app.LightManager.RemoveLight(SelectedEntity.Index);
                        SelectedEntity.Type = EntityType.None;
                        shouldResetPT = true;
                    }
                    else
                    {
                        if (ImGui.Button("Teleport to camera"))
                        {
                            light.Position = app.Camera.Position;
                            shouldUpdateLight = true;
                        }

                        tempVec3 = light.Position.ToNumerics();
                        if (ImGui.DragFloat3("Position", ref tempVec3, 0.1f))
                        {
                            shouldUpdateLight = true;
                            light.Position = tempVec3.ToOpenTK();
                        }

                        tempVec3 = light.Color.ToNumerics();
                        if (ImGui.DragFloat3("Color", ref tempVec3, 0.1f, 0.0f))
                        {
                            shouldUpdateLight = true;
                            light.Color = tempVec3.ToOpenTK();
                        }

                        if (ImGui.DragFloat("Radius", ref light.Radius, 0.1f, 0.25f, 10.0f))
                        {
                            light.Radius = MathF.Max(light.Radius, 0.25f);
                            shouldUpdateLight = true;
                        }

                        ImGui.Separator();
                        if (abstractLight.HasPointShadow())
                        {
                            if (ImGui.Button("Delete PointShadow"))
                            {
                                app.LightManager.DeletePointShadowOfLight(SelectedEntity.Index);
                            }
                        }
                        else
                        {
                            if (ImGui.Button("Create PointShadow"))
                            {
                                PointShadow pointShadow = new PointShadow(256, 0.5f, 60.0f);
                                app.LightManager.CreatePointShadowForLight(pointShadow, SelectedEntity.Index);
                            }
                        }

                        if (abstractLight.HasPointShadow())
                        {
                            PointShadow pointShadow = app.LightManager.GetPointShadow(light.PointShadowIndex);

                            tempInt = pointShadow.Result.Width;
                            if (ImGui.InputInt("Resolution", ref tempInt))
                            {
                                pointShadow.SetSize(tempInt);
                            }

                            tempVec2 = pointShadow.ClippingPlanes.ToNumerics();
                            if (ImGui.InputFloat2("ClippingPlanes", ref tempVec2))
                            {
                                pointShadow.ClippingPlanes = tempVec2.ToOpenTK();
                            }
                        }

                        if (shouldUpdateLight)
                        {
                            shouldResetPT = true;
                        }
                    }
                }
                else
                {
                    BothAxisCenteredText("PRESS E TO TOGGLE FREE CAMERA AND SELECT AN ENTITY");
                }

                ImGui.End();
            }

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0.0f));
            ImGui.Begin($"Viewport");
            {
                System.Numerics.Vector2 content = ImGui.GetContentRegionAvail();
                if (content.X != app.RenderPresentationResolution.X || content.Y != app.RenderPresentationResolution.Y)
                {
                    app.RenderPresentationResolution = new Vector2i((int)content.X, (int)content.Y);
                }

                System.Numerics.Vector2 tileBar = ImGui.GetCursorPos();
                viewportHeaderSize = ImGui.GetWindowPos() + tileBar;

                ImGui.Image(app.TonemapAndGamma.Result.ID, content, new System.Numerics.Vector2(0.0f, 1.0f), new System.Numerics.Vector2(1.0f, 0.0f));

                ImGui.End();
                ImGui.PopStyleVar();
            }

            if (shouldResetPT && app.PathTracer != null)
            {
                app.PathTracer.ResetRenderProcess();
            }
            Backend.Render();
        }

        private static void ToolTipForItemAboveHovered(string text)
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(text);
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

        private readonly Stopwatch recordingTimer;
        public void Update(Application app)
        {
            void TakeScreenshot()
            {
                int frameIndex = app.FrameRecorder.ReplayFrameIndex;
                if (frameIndex == 0) frameIndex = app.FrameRecorder.FrameCount;

                const string RECORDED_FRAME_DATA_OUT_DIR = "RecordedFrames";
                Directory.CreateDirectory(RECORDED_FRAME_DATA_OUT_DIR);

                Helper.TextureToDisk(app.TonemapAndGamma.Result, $"{RECORDED_FRAME_DATA_OUT_DIR}/{frameIndex}");
            }

            if (FrameRecState == FrameRecorderState.Replaying)
            {
                if (app.RenderMode == RenderMode.Rasterizer || (app.RenderMode == RenderMode.PathTracer && app.PathTracer.AccumulatedSamples >= recordingRenderSampleGoal))
                {
                    app.FrameRecorder.ReplayFrameIndex++;
                    if (isVideoRender)
                    {
                        TakeScreenshot();
                    }
                }
                
                if (!isInfiniteReplay && app.FrameRecorder.ReplayFrameIndex == 0)
                {
                    FrameRecState = FrameRecorderState.Nothing;
                }
            }

            if (FrameRecState == FrameRecorderState.Recording && recordingTimer.Elapsed.TotalMilliseconds >= (1000.0f / recordingFPS))
            {
                FrameState state = new FrameState();
                state.Position = app.Camera.Position;
                state.UpVector = app.Camera.UpVector;
                state.LookX = app.Camera.LookX;
                state.LookY = app.Camera.LookY;

                app.FrameRecorder.Record(state);
                recordingTimer.Restart();
            }

            if (FrameRecState != FrameRecorderState.Replaying &&
                app.KeyboardState[Keys.R] == InputState.Touched &&
                app.KeyboardState[Keys.LeftControl] == InputState.Pressed)
            {
                if (FrameRecState == FrameRecorderState.Recording)
                {
                    FrameRecState = FrameRecorderState.Nothing;
                }
                else
                {
                    FrameRecState = FrameRecorderState.Recording;
                    app.FrameRecorder.Clear();
                }
            }

            if (FrameRecState != FrameRecorderState.Recording && app.FrameRecorder.IsFramesLoaded &&
                app.KeyboardState[Keys.Space] == InputState.Touched &&
                app.KeyboardState[Keys.LeftControl] == InputState.Pressed)
            {
                FrameRecState = FrameRecState == FrameRecorderState.Replaying ? FrameRecorderState.Nothing : FrameRecorderState.Replaying;
                if (FrameRecState == FrameRecorderState.Replaying)
                {
                    app.CamCollisionSettings.IsEnabled = false;
                    app.MouseState.CursorMode = CursorModeValue.CursorNormal;
                }
            }

            if (app.MouseState.CursorMode == CursorModeValue.CursorNormal && app.MouseState[MouseButton.Left] == InputState.Touched)
            {
                Vector2i point = new Vector2i((int)app.MouseState.Position.X, (int)app.MouseState.Position.Y);
                if (app.RenderGui)
                {
                    point -= (Vector2i)viewportHeaderSize.ToOpenTK();
                }
                point.Y = app.TonemapAndGamma.Result.Height - point.Y;

                Vector2 ndc = new Vector2((float)point.X / app.TonemapAndGamma.Result.Width, (float)point.Y / app.TonemapAndGamma.Result.Height) * 2.0f - new Vector2(1.0f);
                if (ndc.X > 1.0f || ndc.Y > 1.0f || ndc.X < -1.0f || ndc.Y < -1.0f)
                {
                    return;
                }

                Ray worldSpaceRay;
                //TLAS.debugMaxStack = 0;
                //Stopwatch timer = Stopwatch.StartNew();
                //System.Threading.Tasks.Parallel.For(0, app.RenderResolution.X * app.RenderResolution.Y, i =>
                //{
                //    int y = i / app.RenderResolution.X;
                //    int x = i % app.RenderResolution.X;

                //    Vector2 ndcDebug = new Vector2((float)x / app.RenderResolution.X, (float)y / app.RenderResolution.Y) * 2.0f - new Vector2(1.0f);
                //    worldSpaceRay = Ray.GetWorldSpaceRay(app.GpuBasicData.CameraPos, app.GpuBasicData.InvProjection, app.GpuBasicData.InvView, ndcDebug);

                //    app.ModelSystem.BVH.Intersect(worldSpaceRay, out BVH.RayHitInfo test);
                //});
                //timer.Stop();
                //Console.WriteLine(timer.Elapsed.TotalMilliseconds);
                //Console.WriteLine($"stack size required: {TLAS.debugMaxStack}");
                //Console.WriteLine($"actual stack size: {app.ModelSystem.BVH.Tlas.TreeDepth}");
                worldSpaceRay = Ray.GetWorldSpaceRay(app.GpuBasicData.CameraPos, app.GpuBasicData.InvProjection, app.GpuBasicData.InvView, ndc);

                bool hitMesh = app.ModelSystem.BVH.Intersect(worldSpaceRay, out BVH.RayHitInfo meshHitInfo);
                bool hitLight = app.LightManager.Intersect(worldSpaceRay, out LightManager.HitInfo lightHitInfo);
                if (app.RenderMode == RenderMode.PathTracer && !app.PathTracer.IsTraceLights) hitLight = false;

                if (!hitMesh && !hitLight)
                {
                    SelectedEntity.Type = EntityType.None;
                    return;
                }

                if (!hitLight) lightHitInfo.T = float.MaxValue;
                if (!hitMesh) meshHitInfo.T = float.MaxValue;

                int hitEntityID;
                EntityType hitEntityType;
                if (meshHitInfo.T < lightHitInfo.T)
                {
                    SelectedEntity.Distance = meshHitInfo.T;
                    hitEntityType = EntityType.Mesh;
                    hitEntityID = meshHitInfo.MeshID;
                }
                else
                {
                    SelectedEntity.Distance = lightHitInfo.T;
                    hitEntityType = EntityType.Light;
                    hitEntityID = lightHitInfo.LightID;
                }

                if ((hitEntityID == SelectedEntity.Index) && (hitEntityType == SelectedEntity.Type))
                {
                    SelectedEntity.Type = EntityType.None;
                    SelectedEntity.Index = -1;
                }
                else
                {
                    SelectedEntity.Index = hitEntityID;
                    SelectedEntity.Type = hitEntityType;
                }
            }

            if (FrameRecState == FrameRecorderState.Replaying)
            {
                FrameState state = app.FrameRecorder[app.FrameRecorder.ReplayFrameIndex];
                app.Camera.Position = state.Position;
                app.Camera.UpVector = state.UpVector;
                app.Camera.LookX = state.LookX;
                app.Camera.LookY = state.LookY;
            }
        }

        public void Dispose()
        {
            Backend.Dispose();
        }
    }
}
