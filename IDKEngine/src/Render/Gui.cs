using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ImGuiNET;
using IDKEngine.GUI;
using OpenTK.Graphics.OpenGL4;

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

        public ImGuiBackend Backend { get; private set; }
        private bool isInfiniteReplay;
        private bool isVideoRender;
        private int recordingRenderSampleGoal;

        public EntityType SelectedEntityType;
        public int SelectedEntityIndex;
        private float selectedEntityDist;

        private FrameRecorderState frameRecState;
        private int recordingFPS;
        public Gui(int width, int height)
        {
            Backend = new ImGuiBackend(width, height);
            frameRecState = FrameRecorderState.Nothing;
            isInfiniteReplay = false;
            recordingRenderSampleGoal = 1;
            recordingFPS = 48;
            recordingTimer = Stopwatch.StartNew();
        }

        private System.Numerics.Vector2 viewportHeaderSize;

        public void Draw(Application app, float frameTime)
        {
            Backend.Update(app, frameTime);
            ImGui.DockSpaceOverViewport();

            bool tempBool;
            int tempInt;
            System.Numerics.Vector2 tempVec2;
            System.Numerics.Vector3 tempVec3;

            bool shouldResetPT = false;
            ImGui.Begin("Frame Recorder");
            {
                if (frameRecState != FrameRecorderState.Replaying)
                {
                    bool isRecording = frameRecState == FrameRecorderState.Recording;
                    ImGui.Text($"Is Recording (Press {Keys.R}): {isRecording}");

                    if (ImGui.InputInt("Recording FPS", ref recordingFPS))
                    {
                        recordingFPS = Math.Max(5, recordingFPS);
                    }

                    if (frameRecState == FrameRecorderState.Recording)
                    {
                        ImGui.Text($"   * Recorded frames: {app.FrameRecorder.FrameCount}");
                        unsafe
                        {
                            ImGui.Text($"   * File size: {app.FrameRecorder.FrameCount * sizeof(FrameState) / 1000}kb");
                        }
                    }
                    ImGui.Separator();
                }
                
                bool isReplaying = frameRecState == FrameRecorderState.Replaying;
                if ((frameRecState == FrameRecorderState.Nothing && app.FrameRecorder.IsFramesLoaded) || isReplaying)
                {
                    ImGui.Text($"Is Replaying (Press {Keys.Space}): {isReplaying}");
                    ImGui.Checkbox("Is Infite Replay", ref isInfiniteReplay);
                    
                    ImGui.Checkbox("Is Video Render", ref isVideoRender);
                    ImGui.SameLine(); InfoMark($"When enabled rendered images are saved into a folder.");

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

                if (frameRecState == FrameRecorderState.Nothing)
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
                ImGui.Text($"Viewport size: {app.RenderResolution.X}x{app.RenderResolution.Y}");
                ImGui.Text($"{Helper.API}");
                ImGui.Text($"{Helper.GPU}");

                if (app.RenderMode == RenderMode.PathTracer)
                {
                    ImGui.Text($"Samples taken: {app.PathTracer.AccumulatedSamples}");
                }

                float tempFloat = app.PostProcessor.Gamma;
                if (ImGui.SliderFloat("Gamma", ref tempFloat, 0.1f, 3.0f))
                {
                    app.PostProcessor.Gamma = tempFloat;
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
                            ImGui.Checkbox("IsConfigureGrid", ref app.RasterizerPipeline.IsConfigureGrid);

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
                                ImGui.SameLine();
                                InfoMark(
                                    $"When active samples are accumulated over {app.TaaResolve.TaaSamples} frames (based on TAA setting)." +
                                    "If TAA is disabled this has no effect."
                                );
                            }

                            if (!Voxelizer.HAS_CONSERVATIVE_RASTER) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f); ImGui.BeginDisabled(); }
                            ImGui.Checkbox("IsConservativeRasterization", ref app.RasterizerPipeline.Voxelizer.IsConservativeRasterization);
                            if (!Voxelizer.HAS_CONSERVATIVE_RASTER) { ImGui.EndDisabled(); ImGui.PopStyleVar(); }

                            ImGui.Text($"NV_conservative_raster: {Voxelizer.HAS_CONSERVATIVE_RASTER}");
                            ImGui.SameLine();
                            InfoMark(
                                "Uses NV_conservative_raster to make the rasterizer invoke the fragment shader even if a pixel is only partially covered. " +
                                "Currently there is some bug with this which causes overly bright voxels."
                            );

                            ImGui.Text($"TAKE_FAST_GEOMETRY_SHADER_PATH: {Voxelizer.TAKE_FAST_GEOMETRY_SHADER_PATH}");
                            ImGui.SameLine();
                            InfoMark(
                                "Uses NV_geometry_shader_passthrough and NV_viewport_swizzle to take advantage of a \"passthrough geometry\" shader instead of emulating a geometry shader in the vertex shader. " +
                                "The reason this emulation is done in the first place is because actual geometry shaders turned out to be slower (without suprise)."
                            );

                            ImGui.Text($"NV_shader_atomic_fp16_vector: {Voxelizer.HAS_ATOMIC_FP16_VECTOR}");
                            ImGui.SameLine();
                            InfoMark(
                                "Uses NV_shader_atomic_fp16_vector to perform atomics on fp16 images without having to emulate such behaviour. " +
                                "Most noticeably without this extension building the voxel grid requires 2.5x times the memory"
                            );
                        }
                    }

                    if (ImGui.CollapsingHeader("Variable Rate Shading"))
                    {
                        ImGui.Text($"NV_shading_rate_image: {VariableRateShading.HAS_VARIABLE_RATE_SHADING}");
                        ImGui.SameLine();
                        InfoMark(
                            "Uses NV_shading_rate_image to choose a unique shading rate " +
                            "on each 16x16 tile as a mesaure of increasing performance by decreasing fragment " +
                            "shader invocations in regions where less detail may be required."
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
                            ImGui.SameLine();
                            InfoMark(
                                $"When active samples are accumulated over {app.TaaResolve.TaaSamples} frames (based on TAA setting)." +
                                "If TAA is disabled this has no effect."
                            );
                        }
                    }

                    if (ImGui.CollapsingHeader("TAA"))
                    {
                        tempBool = app.TaaResolve.TaaEnabled;
                        if (ImGui.Checkbox("IsTAA", ref tempBool))
                        {
                            app.TaaResolve.TaaEnabled = tempBool;
                        }

                        if (app.TaaResolve.TaaEnabled)
                        {
                            tempBool = app.TaaResolve.IsTaaArtifactMitigation;
                            if (ImGui.Checkbox("IsTaaArtifactMitigation", ref tempBool))
                            {
                                app.TaaResolve.IsTaaArtifactMitigation = tempBool;
                            }
                            ImGui.SameLine();
                            InfoMark(
                                "This is not a feature. It's mostly for fun and you can see the output of a naive TAA resolve pass. " +
                                "In static scenes this always converges to the correct result whereas with artifact mitigation valid samples might be rejected."
                            );

                            tempInt = app.TaaResolve.TaaSamples;
                            if (ImGui.SliderInt("Samples##1", ref tempInt, 1, GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT))
                            {
                                app.TaaResolve.TaaSamples = Math.Min(tempInt, GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT);
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
                        ImGui.SameLine();
                        InfoMark("Toggling this only controls the generation of updated shadow maps. It does not effect the use of existing shadow maps.");
                        
                        ImGui.Text($"HAS_VERTEX_LAYERED_RENDERING: {PointShadow.TAKE_VERTEX_LAYERED_RENDERING_PATH}");
                        ImGui.SameLine();
                        InfoMark("Uses (ARB_shader_viewport_layer_array or NV_viewport_array2 or AMD_vertex_shader_layer) to generate point shadows in only 1 draw call instead of 6.");
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
                    Ray worldSpaceRay = Ray.GetWorldSpaceRay(app.GLSLBasicData.CameraPos, app.GLSLBasicData.InvProjection, app.GLSLBasicData.InvView, new Vector2(0.0f));
                    Vector3 spawnPoint = worldSpaceRay.Origin + worldSpaceRay.Direction * 1.5f;

                    Light light = new Light(spawnPoint, new Vector3(Helper.RandomVec3(5.0f, 7.0f)), 0.3f);
                    if (app.LightManager.AddLight(light))
                    {
                        SelectedEntityType = EntityType.Light;
                        SelectedEntityIndex = app.LightManager.Count - 1;
                        shouldResetPT = true;
                    }
                }
            }

            ImGui.Begin("Entity Properties");
            {
                if (SelectedEntityType != EntityType.None)
                {
                    ImGui.Text($"{SelectedEntityType}ID: {SelectedEntityIndex}");
                    ImGui.Text($"Distance: {MathF.Round(selectedEntityDist, 3)}");
                }
                if (SelectedEntityType == EntityType.Mesh)
                {
                    bool shouldUpdateMesh = false;
                    ref readonly GLSLDrawElementsCmd cmd = ref app.ModelSystem.DrawCommands[SelectedEntityIndex];
                    ref GLSLMesh mesh = ref app.ModelSystem.Meshes[SelectedEntityIndex];
                    ref GLSLMeshInstance meshInstance = ref app.ModelSystem.MeshInstances[cmd.BaseInstance];

                    ImGui.Text($"MaterialID: {mesh.MaterialIndex}");
                    ImGui.Text($"Triangle Count: {cmd.Count / 3}");
                    ImGui.SameLine(); if (ImGui.Button("Teleport to camera"))
                    {
                        meshInstance.ModelMatrix = meshInstance.ModelMatrix.ClearTranslation() * Matrix4.CreateTranslation(app.Camera.Position);
                        shouldUpdateMesh = true;
                    }
                    
                    System.Numerics.Vector3 systemVec3 = meshInstance.ModelMatrix.ExtractTranslation().ToNumerics();
                    if (ImGui.DragFloat3("Position", ref systemVec3, 0.1f))
                    {
                        shouldUpdateMesh = true;
                        meshInstance.ModelMatrix = meshInstance.ModelMatrix.ClearTranslation() * Matrix4.CreateTranslation(systemVec3.ToOpenTK());
                    }

                    systemVec3 = meshInstance.ModelMatrix.ExtractScale().ToNumerics();
                    if (ImGui.DragFloat3("Scale", ref systemVec3, 0.005f))
                    {
                        shouldUpdateMesh = true;
                        Vector3 temp = Vector3.ComponentMax(systemVec3.ToOpenTK(), new Vector3(0.001f));
                        meshInstance.ModelMatrix = Matrix4.CreateScale(temp) * meshInstance.ModelMatrix.ClearScale();
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

                    systemVec3 = mesh.Absorbance.ToNumerics();
                    if (ImGui.InputFloat3("Absorbance", ref systemVec3))
                    {
                        Vector3 temp = systemVec3.ToOpenTK();
                        temp = Vector3.ComponentMax(temp, Vector3.Zero);

                        mesh.Absorbance = temp;
                        shouldUpdateMesh = true;
                    }

                    if (shouldUpdateMesh)
                    {
                        shouldResetPT = true;
                        app.ModelSystem.UpdateMeshBuffer(SelectedEntityIndex, 1);
                    }
                }
                else if (SelectedEntityType == EntityType.Light)
                {
                    bool shouldUpdateLight = false;
                    
                    app.LightManager.TryGetLight(SelectedEntityIndex, out Light abstractLight);
                    ref GLSLLight light = ref abstractLight.GLSLLight;

                    if (ImGui.Button("Delete"))
                    {
                        app.LightManager.RemoveLight(SelectedEntityIndex);
                        SelectedEntityType = EntityType.None;
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

                        if (ImGui.DragFloat("Radius", ref light.Radius, 0.1f))
                        {
                            light.Radius = MathF.Max(light.Radius, 0.0f);
                            shouldUpdateLight = true;
                        }

                        ImGui.Separator();
                        if (abstractLight.HasPointShadow())
                        {
                            if (ImGui.Button("Delete PointShadow"))
                            {
                                app.LightManager.DeletePointShadowOfLight(SelectedEntityIndex);
                            }
                        }
                        else
                        {
                            if (ImGui.Button("Create PointShadow"))
                            {
                                PointShadow pointShadow = new PointShadow(256, 0.5f, 60.0f);
                                app.LightManager.CreatePointShadowForLight(pointShadow, SelectedEntityIndex);
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
                            app.LightManager.UpdateLightBuffer(SelectedEntityIndex);
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
                if (content.X != app.RenderResolution.X || content.Y != app.RenderResolution.Y)
                {
                    app.RenderResolution = new Vector2i((int)content.X, (int)content.Y);
                }

                System.Numerics.Vector2 tileBar = ImGui.GetCursorPos();
                viewportHeaderSize = ImGui.GetWindowPos() + tileBar;

                ImGui.Image(app.TaaResolve.Result.ID, content, new System.Numerics.Vector2(0.0f, 1.0f), new System.Numerics.Vector2(1.0f, 0.0f));

                ImGui.End();
                ImGui.PopStyleVar();
            }

            if (shouldResetPT && app.PathTracer != null)
            {
                app.PathTracer.AccumulatedSamples = 0;
            }
            Backend.Render();
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

        private readonly Stopwatch recordingTimer;
        public void Update(Application app, float dT)
        {
            void TakeScreenshot()
            {
                int frameIndex = app.FrameRecorder.ReplayFrameIndex;
                if (frameIndex == 0) frameIndex = app.FrameRecorder.FrameCount;

                const string RECORDED_FRAME_DATA_OUT_DIR = "RecordedFrames";
                Directory.CreateDirectory(RECORDED_FRAME_DATA_OUT_DIR);

                Helper.TextureToDisk(app.PostProcessor.Result, $"{RECORDED_FRAME_DATA_OUT_DIR}/{frameIndex}");
            }

            // TODO: fix wrong logic when replaying is started in gui.Render()
            if (frameRecState == FrameRecorderState.Replaying)
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
                        app.Camera.Velocity = Vector3.Zero;
                        Backend.IsIgnoreMouseInput = false;
                    }
                    else
                    {
                        app.MouseState.CursorMode = CursorModeValue.CursorDisabled;
                        Backend.IsIgnoreMouseInput = true;
                    }
                }

                if (app.MouseState.CursorMode == CursorModeValue.CursorDisabled)
                {
                    app.Camera.ProcessInputs(app.KeyboardState, app.MouseState, dT);
                }
            }

            if (frameRecState == FrameRecorderState.Recording && recordingTimer.Elapsed.TotalMilliseconds >= (1000.0f / recordingFPS))
            {
                FrameState state = new FrameState();
                state.Position = app.Camera.Position;
                state.UpVector = app.Camera.UpVector;
                state.LookX = app.Camera.LookX;
                state.LookY = app.Camera.LookY;

                app.FrameRecorder.Record(state);
                recordingTimer.Restart();
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

            if (frameRecState != FrameRecorderState.Recording && app.FrameRecorder.IsFramesLoaded && app.KeyboardState[Keys.Space] == InputState.Touched)
            {
                frameRecState = frameRecState == FrameRecorderState.Replaying ? FrameRecorderState.Nothing : FrameRecorderState.Replaying;
            }

            if (app.MouseState.CursorMode == CursorModeValue.CursorNormal && app.MouseState[MouseButton.Left] == InputState.Touched)
            {
                Vector2i point = new Vector2i((int)app.MouseState.Position.X, (int)app.MouseState.Position.Y);
                if (app.RenderGui)
                {
                    point -= (Vector2i)viewportHeaderSize.ToOpenTK();
                }
                point.Y = app.PostProcessor.Result.Height - point.Y;

                Vector2 ndc = new Vector2((float)point.X / app.PostProcessor.Result.Width, (float)point.Y / app.PostProcessor.Result.Height) * 2.0f - new Vector2(1.0f);
                if (ndc.X > 1.0f || ndc.Y > 1.0f || ndc.X < -1.0f || ndc.Y < -1.0f)
                {
                    return;
                }

                Ray worldSpaceRay = Ray.GetWorldSpaceRay(app.GLSLBasicData.CameraPos, app.GLSLBasicData.InvProjection, app.GLSLBasicData.InvView, ndc);
                bool hitMesh = app.BVH.Intersect(worldSpaceRay, out TLAS.HitInfo meshHitInfo);
                bool hitLight = app.LightManager.Intersect(worldSpaceRay, out LightManager.HitInfo lightHitInfo);
                if (app.RenderMode == RenderMode.PathTracer && !app.PathTracer.IsTraceLights) hitLight = false;

                if (!hitMesh && !hitLight)
                {
                    SelectedEntityType = EntityType.None;
                    return;
                }

                if (!hitLight) lightHitInfo.T = float.MaxValue;
                if (!hitMesh) meshHitInfo.T = float.MaxValue;

                int hitEntityID;
                EntityType hitEntityType;
                if (meshHitInfo.T < lightHitInfo.T)
                {
                    selectedEntityDist = meshHitInfo.T;
                    hitEntityType = EntityType.Mesh;
                    hitEntityID = meshHitInfo.MeshID;
                }
                else
                {
                    selectedEntityDist = lightHitInfo.T;
                    hitEntityType = EntityType.Light;
                    hitEntityID = lightHitInfo.LightID;
                }

                if ((hitEntityID == SelectedEntityIndex) && (hitEntityType == SelectedEntityType))
                {
                    SelectedEntityType = EntityType.None;
                    SelectedEntityIndex = -1;
                }
                else
                {
                    SelectedEntityIndex = hitEntityID;
                    SelectedEntityType = hitEntityType;
                }
            }

            if (frameRecState == FrameRecorderState.Replaying)
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
