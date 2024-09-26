using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ImGuiNET;
using NativeFileDialogSharp;
using BBLogger;
using BBOpenGL;
using IDKEngine.Bvh;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;
using IDKEngine.Windowing;
using IDKEngine.ThirdParty;
using SysVec3 = System.Numerics.Vector3;
using SysVec2 = System.Numerics.Vector2;
using OtkVec3 = OpenTK.Mathematics.Vector3;
using OtkVec2 = OpenTK.Mathematics.Vector2;

namespace IDKEngine.Render
{
    partial class Gui : IDisposable
    {
        public enum EntityType : int
        {
            None,
            Mesh,
            Light,
            Node,
        }

        public readonly record struct SelectedEntityInfo
        {
            public readonly EntityType EntityType = EntityType.None;

            // Only used when EntityType != Node
            public readonly int EntityID;
            public readonly int InstanceID;

            // Only used when EntityType == Node
            public readonly ModelLoader.Node? Node; 

            public static readonly SelectedEntityInfo None = new SelectedEntityInfo();

            public SelectedEntityInfo(EntityType entityType, int entityId, int instanceID)
            {
                EntityType = entityType;
                EntityID = entityId;
                InstanceID = instanceID;
            }

            public SelectedEntityInfo(ModelLoader.Node node)
            {
                EntityType = EntityType.Node;
                Node = node;
            }
        }

        public SelectedEntityInfo SelectedEntity;

        private readonly ImGuiBackend guiBackend;
        private SysVec2 viewportHeaderSize;
        private float clickedEntityDistance;
        public Gui(Vector2i windowSize)
        {
            guiBackend = new ImGuiBackend(windowSize);
        }

        public void Draw(Application app, float dT)
        {
            guiBackend.BeginFrame(app, dT);
            DrawMyGui(app);
            guiBackend.EndFrame();
        }

        private unsafe void DrawMyGui(Application app)
        {
            int tempInt;
            bool tempBool;
            float tempFloat;
            SysVec2 tempVec2;
            SysVec3 tempVec3;
            bool resetPathTracer = false;

            ImGui.DockSpaceOverViewport();

            bool openModelLoadPopup = false;
            if (ImGui.BeginMainMenuBar())
            {
                if (ImGui.BeginMenu("Add"))
                {
                    if (ImGui.MenuItem("glTF (drop-in supported)"))
                    {
                        openModelLoadPopup = true;
                    }

                    if (ImGui.MenuItem("Light"))
                    {
                        GpuPerFrameData perFrameData = app.PerFrameData;
                        Ray worldSpaceRay = Ray.GetWorldSpaceRay(perFrameData.CameraPos, perFrameData.InvProjection, perFrameData.InvView, new OtkVec2(0.0f));
                        OtkVec3 spawnPoint = worldSpaceRay.Origin + worldSpaceRay.Direction * 1.5f;

                        CpuLight newLight = new CpuLight(spawnPoint, RNG.RandomVec3(32.0f, 88.0f), 0.3f);
                        if (app.LightManager.AddLight(newLight))
                        {
                            int newLightIndex = app.LightManager.Count - 1;
                            CpuPointShadow pointShadow = new CpuPointShadow(128, app.RenderResolution, new OtkVec2(newLight.GpuLight.Radius, 60.0f));
                            if (!app.LightManager.CreatePointShadowForLight(pointShadow, newLightIndex))
                            {
                                pointShadow.Dispose();
                            }

                            SelectedEntity = new SelectedEntityInfo(EntityType.Light, newLightIndex, 0);
                            resetPathTracer = true;
                        }
                    }
                    ImGui.EndMenu();
                }
                ImGui.EndMainMenuBar();
            }

            // This is here because https://github.com/ocornut/imgui/issues/2200#issuecomment-440701567
            if (openModelLoadPopup)
            {
                if (app.WindowFullscreen)
                {
                    // Need to end fullscreen otherwise file explorer is not visible
                    app.WindowFullscreen = false;
                }

                DialogResult result = Dialog.FileOpen("gltf,glb");
                if (result.IsError)
                {
                    Logger.Log(Logger.LogLevel.Error, result.ErrorMessage);
                }
                else if (result.IsOk)
                {
                    AddModelDialog(result.Path);
                }
                openModelLoadPopup = false;
            }

            if (ImGui.Begin("Stats"))
            {
                {
                    float mbDrawVertices = (app.ModelManager.Vertices.SizeInBytes() + app.ModelManager.VertexPositions.SizeInBytes()) / 1000000.0f;
                    float mbDrawIndices = app.ModelManager.VertexIndices.SizeInBytes() / 1000000.0f;
                    float mbMeshInstances = app.ModelManager.MeshInstances.SizeInBytes() / 1000000.0f;
                    float totalRasterizer = mbDrawVertices + mbDrawIndices + mbMeshInstances;
                    if (ImGui.TreeNode($"Rasterizer Geometry total = {totalRasterizer}mb"))
                    {
                        ImGui.Text($"  * Vertices ({app.ModelManager.Vertices.Length}) = {mbDrawVertices}mb");
                        ImGui.Text($"  * Triangles ({app.ModelManager.VertexIndices.Length / 3}) = {mbDrawIndices}mb");
                        ImGui.Text($"  * MeshInstances ({app.ModelManager.MeshInstances.Length}) = {mbMeshInstances}mb");
                        ImGui.TreePop();
                    }
                }

                {
                    float mbBlasTrianglesIndices = app.ModelManager.BVH.BlasTriangles.SizeInBytes() / 1000000.0f;
                    float mbBlasNodes = app.ModelManager.BVH.BlasNodes.SizeInBytes() / 1000000.0f;
                    float mbBTlasNodes = app.ModelManager.BVH.TlasNodes.SizeInBytes() / 1000000.0f;
                    float totalBVH = mbBlasTrianglesIndices + mbBlasNodes + mbBTlasNodes;
                    if (ImGui.TreeNode($"BVH total = {totalBVH}mb"))
                    {
                        ImGui.Text($"  * Triangles ({app.ModelManager.BVH.BlasTriangles.Length}) = {mbBlasTrianglesIndices}mb");
                        ImGui.Text($"  * Blas Nodes ({app.ModelManager.BVH.BlasNodes.Length}) = {mbBlasNodes}mb");
                        ImGui.Text($"  * Tlas Nodes ({app.ModelManager.BVH.TlasNodes.Length}) = {mbBTlasNodes}mb");
                        ImGui.TreePop();
                    }
                }

                {
                    float mbJointMatrices = app.ModelManager.JointMatrices.SizeInBytes() / 1000000.0f;
                    float mbTotalAnimations = mbJointMatrices;
                    if (ImGui.TreeNode($"Animations total = {mbTotalAnimations}mb"))
                    {
                        ImGui.Text($"  * JointMatrices ({app.ModelManager.JointMatrices.Length}) = {mbJointMatrices}mb");
                        ImGui.TreePop();
                    }
                }
            }
            ImGui.End();

            if (ImGui.Begin("Settings"))
            {
                if (ImGui.CollapsingHeader("Camera"))
                {
                    tempVec3 = app.Camera.Position.ToNumerics();
                    if (ImGui.DragFloat3("Position", ref tempVec3))
                    {
                        app.Camera.Position = tempVec3.ToOpenTK();
                    }

                    tempVec3 = app.Camera.Velocity.ToNumerics();
                    if (ImGui.DragFloat3("Velocity", ref tempVec3))
                    {
                        app.Camera.Velocity = tempVec3.ToOpenTK();
                    }
                    ImGui.SameLine();
                    ImGui.Text($"({app.Camera.Velocity.Length})");

                    tempVec2 = new SysVec2(app.Camera.LookX, app.Camera.LookY);
                    if (ImGui.DragFloat2("LookAt", ref tempVec2))
                    {
                        app.Camera.LookX = tempVec2.X;
                        app.Camera.LookY = tempVec2.Y;
                    }

                    ImGui.SliderFloat("AccelerationSpeed", ref app.Camera.KeyboardAccelerationSpeed, 0.0f, 50.0f * Camera.MASS);
                    ImGui.SliderFloat("Sensitivity", ref app.Camera.MouseSensitivity, 0.0f, 0.2f);

                    tempFloat = MyMath.RadiansToDegrees(app.Camera.FovY);
                    if (ImGui.SliderFloat("FovY", ref tempFloat, 10.0f, 130.0f))
                    {
                        app.Camera.FovY = MyMath.DegreesToRadians(tempFloat);
                    }

                    ImGui.SliderFloat("NearPlane", ref app.Camera.NearPlane, 0.001f, 5.0f);
                    ImGui.SliderFloat("FarPlane", ref app.Camera.FarPlane, 5.0f, 2000.0f);

                    ImGui.Separator();

                    ImGui.Checkbox("Collision##Camera", ref app.SceneVsCamCollisionSettings.IsEnabled);
                    if (app.SceneVsCamCollisionSettings.IsEnabled)
                    {
                        ImGui.SliderInt("TestSteps##Camera", ref app.SceneVsCamCollisionSettings.Settings.TestSteps, 1, 20);
                        ImGui.SliderInt("RecursiveSteps##Camera", ref app.SceneVsCamCollisionSettings.Settings.RecursiveSteps, 1, 20);
                        ImGui.SliderFloat("NormalOffset##Camera", ref app.SceneVsCamCollisionSettings.Settings.EpsilonNormalOffset, 0.0f, 0.01f, "%.4g");
                    }

                    ImGui.Separator();

                    ImGui.Checkbox("HasGravity", ref app.Camera.IsGravity);
                    if (app.Camera.IsGravity)
                    {
                        ImGui.SliderFloat("Gravity", ref app.Camera.GravityDownForce, 0.0f, 100.0f);
                    }
                }


                if (ImGui.CollapsingHeader("Lights"))
                {
                    ImGui.Checkbox("SceneCollision##Lights", ref app.LightManager.SceneVsSphereCollisionSettings.IsEnabled);
                    if (app.LightManager.SceneVsSphereCollisionSettings.IsEnabled)
                    {
                        ImGui.SliderInt("TestSteps##Lights##Scene", ref app.LightManager.SceneVsSphereCollisionSettings.Settings.TestSteps, 1, 20);
                        ImGui.SliderInt("RecursiveSteps##Lights##Scene", ref app.LightManager.SceneVsSphereCollisionSettings.Settings.RecursiveSteps, 1, 20);
                        ImGui.SliderFloat("NormalOffset##Lights##Scene", ref app.LightManager.SceneVsSphereCollisionSettings.Settings.EpsilonNormalOffset, 0.0f, 0.01f, "%.4g");
                    }

                    ImGui.Checkbox("LightsCollision", ref app.LightManager.LightVsLightCollisionSetting.IsEnabled);
                    if (app.LightManager.LightVsLightCollisionSetting.IsEnabled)
                    {
                        ImGui.SliderInt("RecursiveSteps##Lights##Lights", ref app.LightManager.LightVsLightCollisionSetting.RecursiveSteps, 1, 20);
                        ImGui.SliderFloat("NormalOffset##Lights##Lights", ref app.LightManager.LightVsLightCollisionSetting.EpsilonOffset, 0.0f, 0.01f, "%.4g");
                    }
                }
            }
            ImGui.End();

            if (ImGui.Begin("Frame Recorder"))
            {
                if (app.RecorderVars.State != Application.FrameRecorderState.Replaying)
                {
                    bool isRecording = app.RecorderVars.State == Application.FrameRecorderState.Recording;
                    ImGui.Text($"Is Recording (Press {Keys.LeftControl} + {Keys.R}): {isRecording}");

                    if (ImGui.InputInt("Recording FPS", ref app.RecorderVars.FPSGoal))
                    {
                        app.RecorderVars.FPSGoal = Math.Max(5, app.RecorderVars.FPSGoal);
                    }

                    if (app.RecorderVars.State == Application.FrameRecorderState.Recording)
                    {
                        ImGui.Text($"   * Recorded frames: {app.FrameStateRecorder.Count}");
                        ImGui.Text($"   * File size: {app.FrameStateRecorder.Count * sizeof(FrameState) / 1000}kb");
                    }
                    ImGui.Separator();
                }

                bool isReplaying = app.RecorderVars.State == Application.FrameRecorderState.Replaying;
                if ((app.RecorderVars.State == Application.FrameRecorderState.None && app.FrameStateRecorder.Count > 0) || isReplaying)
                {
                    ImGui.Text($"Is Replaying (Press {Keys.LeftControl} + {Keys.Space}): {isReplaying}");
                    ImGui.Checkbox("Is Video Render", ref app.RecorderVars.IsOutputFrames);
                    ToolTipForItemAboveHovered("When enabled rendered images are saved into a folder.");

                    tempInt = app.FrameStateRecorder.ReplayStateIndex;
                    if (ImGui.SliderInt("ReplayFrame", ref tempInt, 0, Math.Max(app.FrameStateRecorder.Count - 1, 0)))
                    {
                        app.FrameStateRecorder.ReplayStateIndex = tempInt;

                        FrameState state = app.FrameStateRecorder[app.FrameStateRecorder.ReplayStateIndex];
                        app.SetFrameState(state);
                    }
                    ImGui.Separator();

                    if (app.CRenderMode == Application.RenderMode.PathTracer)
                    {
                        tempInt = app.RecorderVars.PathTracingSamplesGoal;
                        if (ImGui.InputInt("Path Tracing SPP", ref tempInt))
                        {
                            app.RecorderVars.PathTracingSamplesGoal = Math.Max(1, tempInt);
                        }
                    }
                    ImGui.Separator();
                }

                if (app.RecorderVars.State == Application.FrameRecorderState.None)
                {
                    if (ImGui.Button($"Save"))
                    {
                        app.FrameStateRecorder.SaveToFile(Application.RecordingSettings.FRAME_STATES_INPUT_FILE);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Load"))
                    {
                        app.FrameStateRecorder = StateRecorder<FrameState>.Load(Application.RecordingSettings.FRAME_STATES_INPUT_FILE);
                    }
                    ImGui.Separator();
                }
            }
            ImGui.End();

            if (ImGui.Begin("Renderer"))
            {
                ImGui.Text($"{app.MeasuredFramesPerSecond}FPS | {app.PresentationResolution.X}x{app.PresentationResolution.Y} | VSync: {app.WindowVSync.ToOnOff()} | Time: {app.TimeEnabled.ToOnOff()}");
                ImGui.Text($"{BBG.GetDeviceInfo().Name}");

                bool gpuUseTlas = app.ModelManager.BVH.GpuUseTlas;
                if (ImGui.Checkbox("GpuUseTlas", ref gpuUseTlas))
                {
                    app.ModelManager.BVH.GpuUseTlas = gpuUseTlas;
                }
                ToolTipForItemAboveHovered(
                    "This increases GPU BVH traversal performance when there exist a lot of instances.\n" +
                    $"You probably want this together with {nameof(app.ModelManager.BVH.RebuildTlas)}"
                );

                ImGui.SameLine();
                ImGui.Checkbox("CpuUseTlas", ref app.ModelManager.BVH.CpuUseTlas);
                ToolTipForItemAboveHovered(
                    "This increases CPU BVH traversal performance when there exist a lot of instances.\n" +
                    $"You probably want this together with {nameof(app.ModelManager.BVH.RebuildTlas)}"
                );
                ImGui.SameLine();
                ImGui.Checkbox("RebuildTlas", ref app.ModelManager.BVH.RebuildTlas);

                ImGui.SliderFloat("Exposure", ref app.TonemapAndGamma.Settings.Exposure, 0.0f, 8.0f);
                ImGui.SliderFloat("Saturation", ref app.TonemapAndGamma.Settings.Saturation, 0.0f, 1.5f);

                tempFloat = app.RenderResolutionScale;
                if (ImGui.SliderFloat("ResolutionScale", ref tempFloat, 0.1f, 1.0f))
                {
                    if (!MyMath.AlmostEqual(tempFloat, app.RenderResolutionScale, 0.002f))
                    {
                        app.RequestRenderResolutionScale = tempFloat;
                    }
                }

                {
                    if (ImGui.BeginCombo("Render Mode", app.CRenderMode.ToString()))
                    {
                        Application.RenderMode[] renderModes = Enum.GetValues<Application.RenderMode>();
                        for (int i = 0; i < renderModes.Length; i++)
                        {
                            bool isSelected = app.CRenderMode == renderModes[i];
                            string enumName = renderModes[i].ToString();
                            if (ImGui.Selectable(enumName, isSelected))
                            {
                                app.RequestRenderMode = (Application.RenderMode)i;
                            }

                            if (isSelected)
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                        ImGui.EndCombo();
                    }
                    ImGui.Separator();
                }

                if (app.CRenderMode == Application.RenderMode.Rasterizer)
                {
                    ImGui.Checkbox("IsWireframe", ref app.RasterizerPipeline.IsWireframe);

                    ImGui.SameLine();

                    tempBool = app.RasterizerPipeline.TakeMeshShaderPath && CpuPointShadow.TakeMeshShaderPath;
                    if (CheckBoxEnabled("UseMeshShaders", ref tempBool, BBG.GetDeviceInfo().ExtensionSupport.MeshShader))
                    {
                        app.RasterizerPipeline.TakeMeshShaderPath = tempBool;
                        CpuPointShadow.TakeMeshShaderPath = tempBool;
                    }
                    ToolTipForItemAboveHovered(
                        "If your GPU supports them this will significantly improve performance assuming a proper vertex load is given (not old sponza)."
                    );

                    ImGui.SameLine();

                    tempBool = app.RasterizerPipeline.IsHiZCulling;
                    if (ImGui.Checkbox("IsHiZCulling", ref tempBool))
                    {
                        app.RasterizerPipeline.IsHiZCulling = tempBool;
                    }
                    ToolTipForItemAboveHovered(
                        "Occlusion Culling. This is turned off because of a small edge-case issue.\n" +
                        "Significantly improves performance depending on the amount of object occlusion."
                    );

                    if (ImGui.CollapsingHeader("Voxel Global Illumination"))
                    {
                        ImGui.Checkbox("IsVXGI", ref app.RasterizerPipeline.IsVXGI);
                        if (app.RasterizerPipeline.IsVXGI)
                        {
                            ToolTipForItemAboveHovered("Controls wether the scene is re-voxelized every frame");
                            ImGui.Checkbox("GridReVoxelize", ref app.RasterizerPipeline.GridReVoxelize);
                            ImGui.Checkbox("GridFollowCamera", ref app.RasterizerPipeline.GridFollowCamera);

                            ImGui.Checkbox("IsConfigureGrid", ref app.RasterizerPipeline.IsConfigureGridMode);
                            ToolTipForItemAboveHovered(
                                "Allows to change the size of the VXGI grid.\n" +
                                "It defines the space the VXGI Lighting algorithm is applied over.\n" +
                                "This needs to be set manually. The green box marks the grid."
                            );

                            string[] resolutions = ["512", "384", "256", "128", "64"];
                            string current = app.RasterizerPipeline.Voxelizer.ResultVoxels.Width.ToString();
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

                            if (app.RasterizerPipeline.IsConfigureGridMode)
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

                                ImGui.SliderFloat("DebugStepMultiplier", ref app.RasterizerPipeline.Voxelizer.DebugStepMultiplier, 0.05f, 1.0f);
                                ImGui.SliderFloat("DebugConeAngle", ref app.RasterizerPipeline.Voxelizer.DebugConeAngle, 0, 0.5f);
                            }
                            else
                            {
                                ImGui.SliderInt("MaxSamples", ref app.RasterizerPipeline.ConeTracer.Settings.MaxSamples, 1, 24);
                                ImGui.SliderFloat("StepMultiplier", ref app.RasterizerPipeline.ConeTracer.Settings.StepMultiplier, 0.01f, 1.0f);
                                ImGui.SliderFloat("GIBoost", ref app.RasterizerPipeline.ConeTracer.Settings.GIBoost, 0.0f, 5.0f);
                                ImGui.SliderFloat("GISkyBoxBoost", ref app.RasterizerPipeline.ConeTracer.Settings.GISkyBoxBoost, 0.0f, 5.0f);
                                ImGui.SliderFloat("NormalRayOffset", ref app.RasterizerPipeline.ConeTracer.Settings.NormalRayOffset, 1.0f, 3.0f);
                                ImGui.Checkbox("IsTemporalAccumulation", ref app.RasterizerPipeline.ConeTracer.Settings.IsTemporalAccumulation);
                                ToolTipForItemAboveHovered(
                                    $"When active samples are accumulated over multiple frames.\n" +
                                    "If there is no Temporal Anti Aliasing this is treated as being disabled."
                                );
                            }

                            tempBool = app.RasterizerPipeline.Voxelizer.IsConservativeRasterization;
                            if (CheckBoxEnabled("IsConservativeRasterization", ref tempBool, Voxelizer.ALLOW_CONSERVATIVE_RASTER))
                            {
                                app.RasterizerPipeline.Voxelizer.IsConservativeRasterization = tempBool;
                            }

                            ImGui.Text($"NV_conservative_raster: {Voxelizer.ALLOW_CONSERVATIVE_RASTER}");
                            ToolTipForItemAboveHovered(
                                "Allows to make the rasterizer invoke the fragment shader even if a pixel is only partially covered.\n" +
                                "Currently there is some bug with this which causes overly bright voxels."
                            );

                            ImGui.Text($"TAKE_FAST_GEOMETRY_SHADER_PATH: {Voxelizer.TAKE_FAST_GEOMETRY_SHADER_PATH}");
                            ToolTipForItemAboveHovered(
                                "Combination of NV_geometry_shader_passthrough and NV_viewport_swizzle to take advantage of a fast \"passthrough geometry\" shader instead of having to render the scene 3 times.\n" +
                                "Regular geometry shaders were even slower which is why I decided to avoided them entirely."
                            );

                            ImGui.Text($"NV_shader_atomic_fp16_vector: {Voxelizer.TAKE_ATOMIC_FP16_PATH}");
                            ToolTipForItemAboveHovered(
                                "Allows to perform atomics on fp16 images without having to emulate such behaviour.\n" +
                                "Most noticeably without this extension voxelizing requires 2.5x times the memory."
                            );
                        }
                    }

                    if (ImGui.CollapsingHeader("Shadows"))
                    {
                        if (ImGui.BeginCombo("ShadowMode", app.RasterizerPipeline.ShadowMode.ToString()))
                        {
                            RasterPipeline.ShadowTechnique[] shadowTechniques = Enum.GetValues<RasterPipeline.ShadowTechnique>();
                            for (int i = 0; i < shadowTechniques.Length; i++)
                            {
                                bool isSelected = app.RasterizerPipeline.ShadowMode == shadowTechniques[i];
                                string enumName = shadowTechniques[i].ToString();
                                if (ImGui.Selectable(enumName, isSelected))
                                {
                                    app.RasterizerPipeline.ShadowMode = (RasterPipeline.ShadowTechnique)i;
                                }

                                if (isSelected)
                                {
                                    ImGui.SetItemDefaultFocus();
                                }
                            }
                            ImGui.EndCombo();
                        }

                        if (app.RasterizerPipeline.ShadowMode == RasterPipeline.ShadowTechnique.RayTraced)
                        {
                            ImGui.Text(
                                "This is mostly just a tech demo.\n" +
                                "For example there is no dedicated denoising.\n" +
                                "Requires abuse of TAA. FSR2 works best"
                            );

                            ImGui.SliderInt("Samples##SamplesRayTracing", ref app.RasterizerPipeline.RayTracingSamples, 1, 10);
                        }


                        ImGui.Separator();

                        ImGui.Checkbox("GenerateShadowMaps", ref app.RasterizerPipeline.GenerateShadowMaps);
                        ToolTipForItemAboveHovered("Regardless of shadow map technique used, this is still needed for effects such as volumetric lighting.\nControls wether shadow maps are regenerated every frame.");
                    }

                    if (ImGui.CollapsingHeader("Anti Aliasing"))
                    {
                        if (ImGui.BeginCombo("Mode", app.RasterizerPipeline.TAAMode.ToString()))
                        {
                            RasterPipeline.TemporalAntiAliasingMode[] options = Enum.GetValues<RasterPipeline.TemporalAntiAliasingMode>();
                            for (int i = 0; i < options.Length; i++)
                            {
                                bool isSelected = app.RasterizerPipeline.TAAMode == options[i];
                                string enumName = options[i].ToString();
                                if (ImGui.Selectable(enumName, isSelected))
                                {
                                    app.RasterizerPipeline.TAAMode = (RasterPipeline.TemporalAntiAliasingMode)i;
                                }

                                if (isSelected)
                                {
                                    ImGui.SetItemDefaultFocus();
                                }
                            }
                            ImGui.EndCombo();
                        }

                        if (app.RasterizerPipeline.TAAMode == RasterPipeline.TemporalAntiAliasingMode.TAA)
                        {
                            ImGui.Checkbox("IsNaiveTaa", ref app.RasterizerPipeline.TaaResolve.IsNaiveTaa);
                            ToolTipForItemAboveHovered(
                                "This is not a feature. It's mostly for fun and you can see the output of a naive TAA resolve pass.\n" +
                                "In static scenes this always converges to the correct result whereas with artifact mitigation valid samples might be rejected."
                            );

                            ImGui.SliderInt("Samples##SamplesTAA", ref app.RasterizerPipeline.TAASamples, 1, 36);

                            if (!app.RasterizerPipeline.TaaResolve.IsNaiveTaa)
                            {
                                ImGui.SliderFloat("PreferAliasingOverBlur", ref app.RasterizerPipeline.TaaResolve.PreferAliasingOverBlur, 0.0f, 1.0f);
                            }
                        }

                        if (app.RasterizerPipeline.TAAMode == RasterPipeline.TemporalAntiAliasingMode.FSR2)
                        {
                            ImGui.Text(
                                "FSR2 (by AMD) does Anti Aliasing but\n" +
                                "simultaneously also upscaling.\n" +
                                "Try reducing resolution scale!\n" +
                                "Note: Performance is lower than expected on NVIDIA!"
                            );

                            ImGui.Checkbox("IsSharpening", ref app.RasterizerPipeline.FSR2Wrapper.IsSharpening);

                            if (app.RasterizerPipeline.FSR2Wrapper.IsSharpening)
                            {
                                ImGui.SliderFloat("Sharpness", ref app.RasterizerPipeline.FSR2Wrapper.Sharpness, 0.0f, 1.0f);
                            }
                        }

                        if (app.RasterizerPipeline.TAAMode == RasterPipeline.TemporalAntiAliasingMode.TAA ||
                            app.RasterizerPipeline.TAAMode == RasterPipeline.TemporalAntiAliasingMode.FSR2)
                        {
                            ImGui.Checkbox("EnableMipBias", ref app.RasterizerPipeline.TAAEnableMipBias);
                            if (app.RasterizerPipeline.TAAEnableMipBias)
                            {
                                ImGui.SliderFloat("MipBias", ref app.RasterizerPipeline.TAAAdditionalMipBias, -3.0f, 3.0f);
                                ToolTipForItemAboveHovered("This bias is applied in addition to the 'optimal' computed bias\n");
                            }
                        }
                    }

                    if (ImGui.CollapsingHeader("VolumetricLighting"))
                    {
                        ImGui.Checkbox("IsVolumetricLighting", ref app.IsVolumetricLighting);
                        if (app.IsVolumetricLighting)
                        {
                            tempFloat = app.VolumetricLight.ResolutionScale;
                            if (ImGui.SliderFloat("ResolutionScale##SamplesVolumetricLight", ref tempFloat, 0.1f, 1.0f))
                            {
                                app.VolumetricLight.ResolutionScale = MathF.Max(tempFloat, 0.1f);
                            }

                            ImGui.SliderInt("Samples##SamplesVolumetricLight", ref app.VolumetricLight.Settings.SampleCount, 1, 30);
                            ImGui.SliderFloat("Scattering", ref app.VolumetricLight.Settings.Scattering, 0.0f, 1.0f);
                            ImGui.SliderFloat("Strength##StrengthVolumetricLight", ref app.VolumetricLight.Settings.Strength, 0.0f, 1.0f);

                            SysVec3 tempVec = app.VolumetricLight.Settings.Absorbance.ToNumerics();
                            if (ImGui.InputFloat3("Absorbance", ref tempVec))
                            {
                                OtkVec3 temp = tempVec.ToOpenTK();
                                temp = OtkVec3.ComponentMax(temp, OtkVec3.Zero);
                                app.VolumetricLight.Settings.Absorbance = temp;
                            }
                        }
                    }

                    if (ImGui.CollapsingHeader("Variable Rate Shading"))
                    {
                        ImGui.Text($"NV_shading_rate_image: {LightingShadingRateClassifier.IS_SUPPORTED}");
                        ToolTipForItemAboveHovered(
                            "Allows the renderer to choose a unique shading rate on each 16x16 tile\n" +
                            "as a mesaure of increasing performance by decreasing fragment shader\n" +
                            "invocations in regions where less detail may be required."
                        );

                        CheckBoxEnabled("IsVariableRateShading", ref app.RasterizerPipeline.IsVariableRateShading, LightingShadingRateClassifier.IS_SUPPORTED);

                        if (ImGui.BeginCombo("DebugMode", app.RasterizerPipeline.LightingVRS.Settings.DebugValue.ToString()))
                        {
                            LightingShadingRateClassifier.DebugMode[] debugModes = Enum.GetValues<LightingShadingRateClassifier.DebugMode>();
                            for (int i = 0; i < debugModes.Length; i++)
                            {
                                bool isSelected = app.RasterizerPipeline.LightingVRS.Settings.DebugValue == debugModes[i];
                                string enumName = debugModes[i].ToString();
                                if (ImGui.Selectable(enumName, isSelected))
                                {
                                    app.RasterizerPipeline.LightingVRS.Settings.DebugValue = (LightingShadingRateClassifier.DebugMode)i;
                                }

                                if (isSelected)
                                {
                                    ImGui.SetItemDefaultFocus();
                                }
                            }
                            ImGui.EndCombo();
                        }

                        ImGui.SliderFloat("SpeedFactor", ref app.RasterizerPipeline.LightingVRS.Settings.SpeedFactor, 0.0f, 1.0f);
                        ImGui.SliderFloat("LumVarianceFactor", ref app.RasterizerPipeline.LightingVRS.Settings.LumVarianceFactor, 0.0f, 0.3f);
                    }

                    if (ImGui.CollapsingHeader("SSAO"))
                    {
                        ImGui.Checkbox("IsSSAO", ref app.RasterizerPipeline.IsSSAO);
                        if (app.RasterizerPipeline.IsSSAO)
                        {
                            ImGui.SliderInt("Samples##SamplesSSAO", ref app.RasterizerPipeline.SSAO.Settings.SampleCount, 1, 20);
                            ImGui.SliderFloat("Radius", ref app.RasterizerPipeline.SSAO.Settings.Radius, 0.0f, 0.5f);
                            ImGui.SliderFloat("Strength##StrengthSSAO", ref app.RasterizerPipeline.SSAO.Settings.Strength, 0.0f, 10.0f);
                        }
                    }

                    if (ImGui.CollapsingHeader("SSR"))
                    {
                        ImGui.Checkbox("IsSSR", ref app.RasterizerPipeline.IsSSR);
                        if (app.RasterizerPipeline.IsSSR)
                        {
                            ImGui.SliderInt("Samples##SamplesSSR", ref app.RasterizerPipeline.SSR.Settings.SampleCount, 1, 100);
                            ImGui.SliderInt("BinarySearchSamples", ref app.RasterizerPipeline.SSR.Settings.BinarySearchCount, 0, 40);
                            ImGui.SliderFloat("MaxDist", ref app.RasterizerPipeline.SSR.Settings.MaxDist, 1, 100);
                        }
                    }
                }
                if (app.CRenderMode == Application.RenderMode.PathTracer)
                {
                    if (ImGui.CollapsingHeader("PathTracing"))
                    {
                        ImGui.Text($"Samples taken: {app.PathTracer.AccumulatedSamples}");

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

                        tempBool = app.PathTracer.TintOnTransmissiveRay;
                        if (ImGui.Checkbox("TintOnTransmissiveRay", ref tempBool))
                        {
                            app.PathTracer.TintOnTransmissiveRay = tempBool;
                        }
                        ToolTipForItemAboveHovered(
                                "This is required for gltF models to work correctly,\n" +
                                "however it's not what Path Tracers typically do, so disabled by default"
                            );

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
                        ImGui.SliderFloat("Threshold", ref app.Bloom.Settings.Threshold, 0.0f, 10.0f);
                        ImGui.SliderFloat("MaxColor", ref app.Bloom.Settings.MaxColor, 0.0f, 20.0f);

                        tempInt = app.Bloom.MinusLods;
                        if (ImGui.SliderInt("MinusLods", ref tempInt, 0, 10))
                        {
                            app.Bloom.MinusLods = tempInt;
                        }
                    }
                }

                if (ImGui.CollapsingHeader("SkyBox"))
                {
                    bool modified = false;

                    tempBool = SkyBoxManager.GetSkyBoxMode() == SkyBoxManager.SkyBoxMode.ExternalAsset;
                    if (ImGui.Checkbox("IsExternalSkyBox", ref tempBool))
                    {
                        SkyBoxManager.SetSkyBoxMode(tempBool ? SkyBoxManager.SkyBoxMode.ExternalAsset : SkyBoxManager.SkyBoxMode.InternalAtmosphericScattering);

                        resetPathTracer = true;
                    }

                    if (SkyBoxManager.GetSkyBoxMode() == SkyBoxManager.SkyBoxMode.InternalAtmosphericScattering)
                    {
                        if (ImGui.SliderFloat("Elevation", ref SkyBoxManager.AtmosphericScatterer.Settings.Elevation, -MathF.PI, MathF.PI))
                        {
                            modified = true;
                        }

                        if (ImGui.SliderFloat("Azimuth", ref SkyBoxManager.AtmosphericScatterer.Settings.Azimuth, -MathF.PI, MathF.PI))
                        {
                            modified = true;
                        }

                        if (ImGui.DragFloat("Intensity", ref SkyBoxManager.AtmosphericScatterer.Settings.LightIntensity, 0.2f))
                        {
                            modified = true;
                        }

                        if (ImGui.SliderInt("InScatteringSamples", ref SkyBoxManager.AtmosphericScatterer.Settings.ISteps, 1, 100))
                        {
                            modified = true;
                        }

                        if (ImGui.SliderInt("DensitySamples", ref SkyBoxManager.AtmosphericScatterer.Settings.JSteps, 1, 40))
                        {
                            modified = true;
                        }

                        if (modified)
                        {
                            resetPathTracer = true;
                            SkyBoxManager.AtmosphericScatterer.Compute();
                        }
                    }
                }

            }
            ImGui.End();

            if (ImGui.Begin("Entity Properties"))
            {
                if (SelectedEntity.EntityType == EntityType.Mesh)
                {
                    bool modified = false;
                    ref readonly BBG.DrawElementsIndirectCommand cmd = ref app.ModelManager.DrawCommands[SelectedEntity.EntityID];
                    ref GpuMesh mesh = ref app.ModelManager.Meshes[SelectedEntity.EntityID];
                    GpuMeshInstance meshInstance = app.ModelManager.MeshInstances[SelectedEntity.InstanceID];

                    Transformation meshInstanceTransform = Transformation.FromMatrix(meshInstance.ModelMatrix);

                    tempVec3 = meshInstanceTransform.Translation.ToNumerics();
                    if (ImGui.DragFloat3("Position", ref tempVec3, 0.1f))
                    {
                        modified = true;
                        meshInstanceTransform.Translation = tempVec3.ToOpenTK();
                    }

                    tempVec3 = meshInstanceTransform.Scale.ToNumerics();
                    if (ImGui.DragFloat3("Scale", ref tempVec3, 0.005f))
                    {
                        modified = true;
                        meshInstanceTransform.Scale = OtkVec3.ComponentMax(tempVec3.ToOpenTK(), new OtkVec3(0.001f));
                    }

                    tempVec3 = meshInstanceTransform.Rotation.ToEulerAngles().ToNumerics();
                    if (ImGui.DragFloat3("Rotation", ref tempVec3, 0.005f))
                    {
                        modified = true;
                        meshInstanceTransform.Rotation = Quaternion.FromEulerAngles(tempVec3.ToOpenTK());
                    }

                    ImGui.Separator();

                    if (ImGui.SliderFloat("NormalMapStrength", ref mesh.NormalMapStrength, 0.0f, 4.0f))
                    {
                        modified = true;
                    }
                    if (ImGui.SliderFloat("EmissiveBias", ref mesh.EmissiveBias, 0.0f, 20.0f))
                    {
                        modified = true;
                    }
                    if (ImGui.SliderFloat("SpecularBias", ref mesh.SpecularBias, -1.0f, 1.0f))
                    {
                        modified = true;
                    }
                    if (ImGui.SliderFloat("RoughnessBias", ref mesh.RoughnessBias, -1.0f, 1.0f))
                    {
                        modified = true;
                    }
                    if (ImGui.SliderFloat("TransmissionBias", ref mesh.TransmissionBias, -1.0f, 1.0f))
                    {
                        modified = true;
                    }
                    if (ImGui.SliderFloat("IORBias", ref mesh.IORBias, -2.0f, 5.0f))
                    {
                        modified = true;
                    }

                    tempVec3 = mesh.AbsorbanceBias.ToNumerics();
                    if (ImGui.DragFloat3("AbsorbanceBias", ref tempVec3, 0.01f))
                    {
                        modified = true;
                        mesh.AbsorbanceBias = tempVec3.ToOpenTK();
                    }

                    ImGui.Text($"MeshId: {SelectedEntity.EntityID}");
                    ImGui.SameLine();
                    ImGui.Text($"MaterialID: {mesh.MaterialId}");
                    ImGui.Text($"InstanceID: {SelectedEntity.InstanceID - cmd.BaseInstance}");
                    ImGui.SameLine();
                    ImGui.Text($"Triangle Count: {cmd.IndexCount / 3}");

                    if (modified)
                    {
                        meshInstance.ModelMatrix = meshInstanceTransform.GetMatrix();

                        resetPathTracer = true;
                        app.ModelManager.UploadMeshBuffer(SelectedEntity.EntityID, 1);
                        app.ModelManager.SetMeshInstance(SelectedEntity.InstanceID, meshInstance);
                    }
                }
                else if (SelectedEntity.EntityType == EntityType.Light)
                {
                    bool modified = false;

                    app.LightManager.TryGetLight(SelectedEntity.EntityID, out CpuLight cpuLight);
                    ref GpuLight gpuLight = ref cpuLight.GpuLight;

                    if (ImGui.Button("Delete"))
                    {
                        app.LightManager.DeleteLight(SelectedEntity.EntityID);
                        SelectedEntity = SelectedEntityInfo.None;
                        resetPathTracer = true;
                    }
                    else
                    {
                        if (ImGui.Button("Teleport to camera"))
                        {
                            gpuLight.Position = app.Camera.Position;
                        }

                        tempVec3 = gpuLight.Position.ToNumerics();
                        if (ImGui.DragFloat3("Position", ref tempVec3, 0.1f))
                        {
                            gpuLight.Position = tempVec3.ToOpenTK();
                        }

                        tempVec3 = cpuLight.Velocity.ToNumerics();
                        if (ImGui.DragFloat3("Velocity", ref tempVec3, 0.1f))
                        {
                            cpuLight.Velocity = tempVec3.ToOpenTK();
                        }
                        ImGui.SameLine();
                        ImGui.Text($"({cpuLight.Velocity.Length})");

                        tempVec3 = gpuLight.Color.ToNumerics();
                        if (ImGui.DragFloat3("Color", ref tempVec3, 0.1f, 0.0f))
                        {
                            modified = true;
                            gpuLight.Color = tempVec3.ToOpenTK();
                        }

                        if (ImGui.DragFloat("Radius", ref gpuLight.Radius, 0.05f, 0.01f, 30.0f))
                        {
                            modified = true;
                        }

                        ImGui.Separator();
                        if (cpuLight.HasPointShadow())
                        {
                            if (ImGui.Button("Delete PointShadow"))
                            {
                                app.LightManager.DeletePointShadowOfLight(SelectedEntity.EntityID);
                            }
                        }
                        else
                        {
                            if (ImGui.Button("Create PointShadow"))
                            {
                                CpuPointShadow newPointShadow = new CpuPointShadow(256, app.RenderResolution, new OtkVec2(gpuLight.Radius, 60.0f));
                                if (!app.LightManager.CreatePointShadowForLight(newPointShadow, SelectedEntity.EntityID))
                                {
                                    newPointShadow.Dispose();
                                }
                            }
                        }

                        if (app.LightManager.TryGetPointShadow(cpuLight.GpuLight.PointShadowIndex, out CpuPointShadow pointShadow))
                        {
                            tempInt = pointShadow.ShadowMap.Width;
                            if (ImGui.InputInt("Resolution", ref tempInt))
                            {
                                pointShadow.SetSizeShadowMap(Math.Max(tempInt, 1));
                            }

                            tempVec2 = pointShadow.ClippingPlanes.ToNumerics();
                            if (ImGui.InputFloat2("ClippingPlanes", ref tempVec2))
                            {
                                pointShadow.ClippingPlanes = tempVec2.ToOpenTK();
                            }
                        }

                        if (modified)
                        {
                            resetPathTracer = true;
                        }
                    }

                    ImGui.Text($"LightID: {SelectedEntity.EntityID}");
                }
                else if (SelectedEntity.EntityType == EntityType.Node)
                {
                    Transformation meshInstanceTransform = SelectedEntity.Node.LocalTransform;
                    bool modified = false;

                    tempVec3 = meshInstanceTransform.Translation.ToNumerics();
                    if (ImGui.DragFloat3("Position", ref tempVec3, 0.1f))
                    {
                        modified = true;
                        meshInstanceTransform.Translation = tempVec3.ToOpenTK();
                    }

                    tempVec3 = meshInstanceTransform.Scale.ToNumerics();
                    if (ImGui.DragFloat3("Scale", ref tempVec3, 0.005f))
                    {
                        modified = true;
                        meshInstanceTransform.Scale = OtkVec3.ComponentMax(tempVec3.ToOpenTK(), new OtkVec3(0.001f));
                    }

                    tempVec3 = meshInstanceTransform.Rotation.ToEulerAngles().ToNumerics();
                    if (ImGui.DragFloat3("Rotation", ref tempVec3, 0.005f))
                    {
                        modified = true;
                        meshInstanceTransform.Rotation = Quaternion.FromEulerAngles(tempVec3.ToOpenTK());
                    }

                    if (modified)
                    {
                        SelectedEntity.Node.LocalTransform = meshInstanceTransform;
                        resetPathTracer = true;
                    }

                    ImGui.Text($"Node: {SelectedEntity.Node.Name}");
                    ImGui.SameLine();
                    ImGui.Text($"NodeId: {SelectedEntity.Node.ArrayIndex}");
                    ImGui.Text($"HasSkin: {SelectedEntity.Node.HasSkin}");
                }
                else
                {
                    BothAxisCenteredText("SELECT AN ENTITY TO VIEW DETAILS");
                }

                if (SelectedEntity.EntityType == EntityType.Mesh || SelectedEntity.EntityType == EntityType.Light)
                {
                    ImGui.Text($"Distance {MathF.Round(clickedEntityDistance, 3)}");
                }
            }
            ImGui.End();

            if (ImGui.Begin("Scene Graph"))
            {
                for (int i = 0; i < app.ModelManager.CpuModels.Length; i++)
                {
                    ref readonly ModelManager.CpuModel cpuModel = ref app.ModelManager.CpuModels[i];
                    RenderNodesGraph(cpuModel.Root);

                    void RenderNodesGraph(ModelLoader.Node node)
                    {
                        ImGui.PushID(node.GetHashCode());

                        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
                        if (node.IsLeaf && node.MeshInstanceIds.Count == 0)
                        {
                            flags |= ImGuiTreeNodeFlags.Leaf;
                        }

                        bool nodeOpen = ImGui.TreeNodeEx(node.Name, flags);
                        if (ImGui.IsItemClicked())
                        {
                            SelectedEntity = new SelectedEntityInfo(node);
                        }

                        if (nodeOpen)
                        {
                            for (int i = 0; i < node.Children.Length; i++)
                            {
                                RenderNodesGraph(node.Children[i]);
                            }
                            for (int i = node.MeshInstanceIds.Start; i < node.MeshInstanceIds.End; i++)
                            {
                                if (ImGui.TreeNodeEx($"MeshInstance_{i}", ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.Leaf))
                                {
                                    ImGui.TreePop();
                                }
                                if (ImGui.IsItemClicked())
                                {
                                    SelectedEntity = new SelectedEntityInfo(EntityType.Mesh, app.ModelManager.MeshInstances[i].MeshId, i);
                                }
                            }
                            ImGui.TreePop();
                        }

                        ImGui.PopID();
                    }
                }

                if (ImGui.TreeNodeEx("Lights"))
                {
                    for (int i = 0; i < app.LightManager.Count; i++)
                    {
                        if (ImGui.TreeNodeEx($"Light_{i}", ImGuiTreeNodeFlags.Leaf))
                        {
                            ImGui.TreePop();
                        }
                        if (ImGui.IsItemClicked())
                        {
                            SelectedEntity = new SelectedEntityInfo(EntityType.Light, i, 0);
                        }

                    }
                    ImGui.TreePop();
                }
            }
            ImGui.End();

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new SysVec2(0.0f));
            if (ImGui.Begin($"Viewport"))
            {
                OtkVec2 content = ImGui.GetContentRegionAvail().ToOpenTK();

                if ((Vector2i)content != app.PresentationResolution)
                {
                    // Viewport changed, inform app of the new resolution
                    app.RequestPresentationResolution = (Vector2i)content;
                }

                SysVec2 tileBar = ImGui.GetCursorPos();
                viewportHeaderSize = ImGui.GetWindowPos() + tileBar;

                ImGui.Image((nint)app.TonemapAndGamma.Result.ID, content.ToNumerics(), new SysVec2(0.0f, 1.0f), new SysVec2(1.0f, 0.0f));
            }
            ImGui.PopStyleVar();
            ImGui.End();

            RenderLoadModelDialog(app, ref resetPathTracer);
            UpdateLoadModelDialog(app, ref resetPathTracer);

            if (resetPathTracer)
            {
                app.PathTracer?.ResetAccumulation();
            }
        }

        public void Update(Application app)
        {
            if (app.MouseState.CursorMode == CursorModeValue.CursorDisabled)
            {
                guiBackend.IgnoreMouseInput = true;
            }
            else
            {
                guiBackend.IgnoreMouseInput = false;
            }

            if (app.MouseState.CursorMode == CursorModeValue.CursorNormal && app.MouseState[MouseButton.Left] == Keyboard.InputState.Touched)
            {
                OtkVec2 clickedPixel = app.MouseState.Position;
                if (app.RenderGui)
                {
                    clickedPixel -= viewportHeaderSize.ToOpenTK();
                }
                clickedPixel.Y = app.TonemapAndGamma.Result.Height - clickedPixel.Y;

                OtkVec2 ndc = clickedPixel / (OtkVec2)app.PresentationResolution * 2.0f - new OtkVec2(1.0f);
                bool clickedInsideViewport = ndc.X < 1.0f && ndc.Y < 1.0f && ndc.X > -1.0f && ndc.Y > -1.0f;
                if (clickedInsideViewport)
                {
                    ref readonly GpuPerFrameData perFrameData = ref app.PerFrameData;
                    Ray worldSpaceRay = Ray.GetWorldSpaceRay(perFrameData.CameraPos, perFrameData.InvProjection, perFrameData.InvView, ndc);
                    SelectedEntityInfo hitEntity = RayTraceEntity(app, worldSpaceRay, out clickedEntityDistance);

                    bool entityWasAlreadySelected = hitEntity == SelectedEntity;
                    if (entityWasAlreadySelected)
                    {
                        SelectedEntity = SelectedEntityInfo.None;
                    }
                    else
                    {
                        SelectedEntity = hitEntity;
                    }
                }
            }
        }
        
        public void SetSize(Vector2i size)
        {
            guiBackend.SetWindowSize(size);
        }
        
        public void PressChar(uint key)
        {
            guiBackend.PressChar(key);
        }

        public void Dispose()
        {
            guiBackend.Dispose();
        }

        private static SelectedEntityInfo RayTraceEntity(Application app, in Ray ray, out float t)
        {
            //Stopwatch sw = Stopwatch.StartNew();
            //for (int y = 0; y < app.RenderResolution.Y; y++)
            //{
            //    int localY = y;
            //    Parallel.For(0, app.RenderResolution.X, x =>
            //    {
            //        OtkVec2 ndc = new OtkVec2(x, localY) / app.PresentationResolution * 2.0f - new OtkVec2(1.0f);
            //        Ray worldSpaceRay = Ray.GetWorldSpaceRay(app.PerFrameData.CameraPos, app.PerFrameData.InvProjection, app.PerFrameData.InvView, ndc);
            //        app.ModelManager.BVH.Intersect(worldSpaceRay, out _);

            //    });
            //}
            //Console.WriteLine(sw.ElapsedMilliseconds);

            t = float.MaxValue;
            bool hitMesh = app.ModelManager.BVH.Intersect(ray, out BVH.RayHitInfo meshHitInfo);
            bool hitLight = app.LightManager.Intersect(ray, out LightManager.RayHitInfo lightHitInfo);

            if (app.CRenderMode == Application.RenderMode.PathTracer && !app.PathTracer.IsTraceLights)
            {
                hitLight = false;
            }

            SelectedEntityInfo hitEntity = SelectedEntityInfo.None;
            if (!hitMesh && !hitLight)
            {
                return hitEntity;
            }

            if (!hitLight)
            {
                lightHitInfo.T = float.MaxValue;
            }
            if (!hitMesh)
            {
                meshHitInfo.T = float.MaxValue;
            }

            if (meshHitInfo.T < lightHitInfo.T)
            {
                t = meshHitInfo.T;
                hitEntity = new SelectedEntityInfo(EntityType.Mesh, app.ModelManager.MeshInstances[meshHitInfo.InstanceID].MeshId, meshHitInfo.InstanceID);
            }
            else
            {
                t = lightHitInfo.T;
                hitEntity = new SelectedEntityInfo(EntityType.Light, lightHitInfo.LightID, 0);
            }

            return hitEntity;
        }

        private static void ToolTipForItemAboveHovered(string text, ImGuiHoveredFlags imGuiHoveredFlags = ImGuiHoveredFlags.AllowWhenDisabled)
        {
            if (ImGui.IsItemHovered(imGuiHoveredFlags))
            {
                ImGui.SetTooltip(text);
            }
        }
        
        private static void BothAxisCenteredText(string text)
        {
            SysVec2 size = ImGui.GetWindowSize();
            SysVec2 textWidth = ImGui.CalcTextSize(text);
            ImGui.SetCursorPos((size - textWidth) * 0.5f);
            ImGui.Text(text);
        }
        
        private static void HorizontallyCenteredText(string text)
        {
            SysVec2 size = ImGui.GetWindowSize();
            SysVec2 textWidth = ImGui.CalcTextSize(text);
            ImGui.SetCursorPos(new SysVec2((size - textWidth).X * 0.5f, ImGui.GetCursorPos().Y));
            ImGui.Text(text);
        }
        
        private static bool CheckBoxEnabled(string name, ref bool value, bool enabled)
        {
            if (!enabled) 
            {
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f); 
                ImGui.BeginDisabled();
            }

            bool clicked = ImGui.Checkbox(name, ref value);

            if (!enabled)
            {
                ImGui.EndDisabled();
                ImGui.PopStyleVar();
            }

            return clicked;
        }
    }

    partial class Gui
    {
        private record struct GuiLoadModel
        {
            public const string IMGUI_ID_POPUP_MODAL = "ModelLoadDialog";

            public enum ModelPreprocessingMode : int
            {
                gltfpack,
                meshoptimizer,
            }

            public record struct LoadingTask
            {
                public LoadParams LoadParams;
                public ModelLoader.GtlfpackWrapper.GltfpackSettings CompressGltfSettings;

                public LoadingTask(string modelPath)
                {
                    CompressGltfSettings = new ModelLoader.GtlfpackWrapper.GltfpackSettings();
                    CompressGltfSettings.ThreadsUsed = Math.Max(Environment.ProcessorCount, 1);
                    CompressGltfSettings.UseInstancing = true;
                    CompressGltfSettings.InputPath = modelPath;

                    LoadParams = new LoadParams();
                }

                public string GetPopupModalName()
                {
                    return $"Loading {Path.GetFileName(CompressGltfSettings.InputPath)}###{IMGUI_ID_POPUP_MODAL}";
                }
            }

            public record struct LoadParams
            {
                public bool SpawnInCamera = true;
                public OtkVec3 Scale = new OtkVec3(1.0f);
                public ModelLoader.OptimizationSettings ModelOptimizationSettings = ModelLoader.OptimizationSettings.Recommended;

                public LoadParams()
                {
                }
            }

            public Tuple<Task, LoadingTask>?[] CompressionsTasks = new Tuple<Task, LoadingTask>[10];
            public LoadingTask CurrentGuiDialogLoadingTask;

            public bool IsLoadModelDialog;
            public ModelPreprocessingMode PreprocessMode;

            private readonly Queue<LoadingTask> queuedLoadingTasks = new Queue<LoadingTask>();
            public GuiLoadModel()
            {
                PreprocessMode = ModelPreprocessingMode.meshoptimizer;
            }

            public bool HandleNextLoadingTask()
            {
                return queuedLoadingTasks.TryDequeue(out CurrentGuiDialogLoadingTask);
            }

            public void AddLoadingTask(string modelPath)
            {
                queuedLoadingTasks.Enqueue(new LoadingTask(modelPath));
            }
        }
        
        private GuiLoadModel loadModelContext = new GuiLoadModel();

        public void AddModelDialog(string path)
        {
            loadModelContext.AddLoadingTask(path);
        }

        public void RenderLoadModelDialog(Application app, ref bool resetPathTracer)
        {
            SysVec3 tempVec3;

            if (!loadModelContext.IsLoadModelDialog)
            {
                if (loadModelContext.HandleNextLoadingTask())
                {
                    loadModelContext.IsLoadModelDialog = true;
                    app.MouseState.CursorMode = CursorModeValue.CursorNormal;
                    ImGui.OpenPopup(loadModelContext.CurrentGuiDialogLoadingTask.GetPopupModalName());
                }
            }

            if (loadModelContext.IsLoadModelDialog)
            {
                ref GuiLoadModel.LoadingTask loadingTask = ref loadModelContext.CurrentGuiDialogLoadingTask;

                if (ImGui.BeginPopupModal(loadingTask.GetPopupModalName(), ref loadModelContext.IsLoadModelDialog, ImGuiWindowFlags.NoNavInputs))
                {
                    GuiLoadModel.ModelPreprocessingMode current = loadModelContext.PreprocessMode;
                    if (ImGui.BeginCombo("Preprocessing", current.ToString()))
                    {
                        GuiLoadModel.ModelPreprocessingMode[] preprocesModes = Enum.GetValues<GuiLoadModel.ModelPreprocessingMode>();
                        for (int i = 0; i < preprocesModes.Length; i++)
                        {
                            GuiLoadModel.ModelPreprocessingMode it = preprocesModes[i];

                            bool isDisabled = it == GuiLoadModel.ModelPreprocessingMode.gltfpack && !ModelLoader.GtlfpackWrapper.CliFound;
                            if (isDisabled)
                            {
                                ImGui.BeginDisabled();
                            }
                            bool isSelected = current == it;
                            if (ImGui.Selectable(it.ToString(), isSelected))
                            {
                                current = it;
                                loadModelContext.PreprocessMode = it;
                            }
                            if (isDisabled)
                            {
                                ImGui.EndDisabled();
                            }

                            if (it == GuiLoadModel.ModelPreprocessingMode.gltfpack)
                            {
                                if (isDisabled)
                                {
                                    ToolTipForItemAboveHovered("gltfpack CLI was not found");
                                }
                                else
                                {
                                    ToolTipForItemAboveHovered("Does optimization + compression + more");
                                }
                            }
                            if (it == GuiLoadModel.ModelPreprocessingMode.meshoptimizer)
                            {
                                ToolTipForItemAboveHovered("Subset of gltfpack. Does not require external CLI");
                            }

                            if (isSelected)
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                        ImGui.EndCombo();
                    }

                    if (loadModelContext.PreprocessMode == GuiLoadModel.ModelPreprocessingMode.gltfpack)
                    {
                        ImGui.Checkbox("UseInstancing", ref loadingTask.CompressGltfSettings.UseInstancing);
                        ImGui.Checkbox("KeepMeshPrimitives (requires gltfpack fork)", ref loadingTask.CompressGltfSettings.KeepMeshPrimitives);
                        ImGui.SliderInt("Threads", ref loadingTask.CompressGltfSettings.ThreadsUsed, 1, Environment.ProcessorCount);
                    }
                    if (loadModelContext.PreprocessMode == GuiLoadModel.ModelPreprocessingMode.meshoptimizer)
                    {
                        ImGui.Checkbox("VertexRemapOptimization", ref loadingTask.LoadParams.ModelOptimizationSettings.VertexRemapOptimization);
                        ImGui.Checkbox("VertexCacheOptimization", ref loadingTask.LoadParams.ModelOptimizationSettings.VertexCacheOptimization);
                        ImGui.Checkbox("VertexFetchOptimization", ref loadingTask.LoadParams.ModelOptimizationSettings.VertexFetchOptimization);
                    }
                    ImGui.Separator();

                    ImGui.Checkbox("SpawnInCamera", ref loadingTask.LoadParams.SpawnInCamera);

                    tempVec3 = loadingTask.LoadParams.Scale.ToNumerics();
                    if (ImGui.InputFloat3("Scale", ref tempVec3))
                    {
                        loadingTask.LoadParams.Scale = tempVec3.ToOpenTK();
                    }

                    float availX = ImGui.GetContentRegionAvail().X;
                    float availY = ImGui.GetContentRegionAvail().Y;
                    ImGui.SetCursorPosX(availX * 0.7f);
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + availY * 0.4f);
                    if (ImGui.Button("Load", new SysVec2(availX * 0.3f, availY * 0.5f)))
                    {
                        string gltfInputPath = loadingTask.CompressGltfSettings.InputPath;

                        if (loadModelContext.PreprocessMode != GuiLoadModel.ModelPreprocessingMode.gltfpack)
                        {
                            if (LoadModel(app, gltfInputPath, loadingTask.LoadParams))
                            {
                                resetPathTracer = true;
                            }
                        }
                        if (loadModelContext.PreprocessMode == GuiLoadModel.ModelPreprocessingMode.gltfpack)
                        {
                            string fileName = Path.GetFileName(gltfInputPath);
                            string compressedGltfDir = Path.Combine(Path.GetDirectoryName(gltfInputPath), $"{Path.GetFileNameWithoutExtension(gltfInputPath)}Compressed");
                            string compressedGtlfPath = Path.Combine(compressedGltfDir, fileName);
                            Directory.CreateDirectory(compressedGltfDir);

                            loadingTask.CompressGltfSettings.OutputPath = compressedGtlfPath;
                            loadingTask.CompressGltfSettings.ProcessError = (string message) =>
                            {
                                Logger.Log(Logger.LogLevel.Error, message);
                                Logger.Log(Logger.LogLevel.Error, $"An error occured while running gltfpack on \"{fileName}\"");
                            };
                            loadingTask.CompressGltfSettings.ProcessOutput = (string message) =>
                            {
                                Logger.Log(Logger.LogLevel.Info, message);
                            };

                            Task? task = ModelLoader.GtlfpackWrapper.Run(loadingTask.CompressGltfSettings);
                            if (task == null)
                            {
                                Logger.Log(Logger.LogLevel.Error, "Failed to start gltfpack. Falling back to normal model");
                                LoadModel(app, gltfInputPath, loadingTask.LoadParams);
                            }
                            else
                            {
                                bool found = false;
                                for (int i = 0; i < loadModelContext.CompressionsTasks.Length; i++)
                                {
                                    if (loadModelContext.CompressionsTasks[i] == null)
                                    {
                                        // We override with optimizations turned off, as we know gltfpack is run on the model
                                        // which already applies all optimizations
                                        loadingTask.LoadParams.ModelOptimizationSettings = ModelLoader.OptimizationSettings.AllTurnedOff;

                                        loadModelContext.CompressionsTasks[i] = new Tuple<Task, GuiLoadModel.LoadingTask>(task, loadingTask);
                                        found = true;
                                        break;
                                    }
                                }
                                if (!found)
                                {
                                    Logger.Log(Logger.LogLevel.Error, "Too many gltfpack instances running at once. Falling back to normal model");
                                    if (LoadModel(app, gltfInputPath, loadingTask.LoadParams))
                                    {
                                        resetPathTracer = true;
                                    }
                                }
                            }
                        }

                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.EndPopup();
                }
            }

            for (int i = 0; i < loadModelContext.CompressionsTasks.Length; i++)
            {
                if (loadModelContext.CompressionsTasks[i] == null)
                {
                    continue;
                }

                (Task task, GuiLoadModel.LoadingTask loadingTask) = loadModelContext.CompressionsTasks[i];
                if (!task.IsCompleted)
                {
                    ImGui.Text($"Compressing {Path.GetFileName(loadingTask.CompressGltfSettings.InputPath)}...\n");
                }
            }
        }
        
        public void UpdateLoadModelDialog(Application app, ref bool resetPathTracer)
        {
            for (int i = 0; i < loadModelContext.CompressionsTasks.Length; i++)
            {
                if (loadModelContext.CompressionsTasks[i] == null)
                {
                    continue;
                }

                (Task task, GuiLoadModel.LoadingTask loadingTask) = loadModelContext.CompressionsTasks[i];
                if (task.IsCompletedSuccessfully)
                {
                    if (LoadModel(app, loadingTask.CompressGltfSettings.OutputPath, loadingTask.LoadParams))
                    {
                        resetPathTracer = true;
                    }

                    loadModelContext.CompressionsTasks[i] = null;
                }
            }
        }
        
        private bool LoadModel(Application app, string modelPath, in GuiLoadModel.LoadParams loadParams)
        {
            OtkVec3 modelPos = loadParams.SpawnInCamera ? app.Camera.Position : new OtkVec3(0.0f);
            Transformation transformation = new Transformation().WithScale(loadParams.Scale).WithTranslation(modelPos);
            ModelLoader.Model? newModel = ModelLoader.LoadGltfFromFile(modelPath, transformation.GetMatrix(), loadParams.ModelOptimizationSettings);
            if (!newModel.HasValue)
            {
                Logger.Log(Logger.LogLevel.Error, $"Failed loading model \"{modelPath}\"");
                return false;
            }

            app.ModelManager.Add(newModel.Value);

            int newMeshIndex = app.ModelManager.Meshes.Length - 1;
            ref readonly BBG.DrawElementsIndirectCommand cmd = ref app.ModelManager.DrawCommands[newMeshIndex];
            SelectedEntity = new SelectedEntityInfo(EntityType.Mesh, newMeshIndex, (int)cmd.BaseInstance);

            return true;
        }
    }
}
