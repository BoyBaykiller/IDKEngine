using System;
using System.IO;
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

namespace IDKEngine.Render;

partial class Gui : IDisposable
{
    public abstract record SelectedEntityInfo
    {
        public record Mesh(int MeshId, int MeshTransformId) : SelectedEntityInfo;
        public record MeshTransform(int MeshTransformId) : SelectedEntityInfo;
        public record Node(ModelLoader.Node Node_) : SelectedEntityInfo;
        public record Material(int MaterialId) : SelectedEntityInfo;
        public record Animation(int ModelId, int AnimationId) : SelectedEntityInfo;
        public record Light(int LightId) : SelectedEntityInfo;
    }

    public SelectedEntityInfo SelectedEntity;

    private readonly ImGuiBackend guiBackend;
    private readonly Queue<IDisposable> frameDeletionQueue;
    private SysVec2 viewportHeaderSize;
    private float clickedEntityDistance;
    public Gui(Vector2i windowSize)
    {
        guiBackend = new ImGuiBackend(windowSize, "Resource/imgui.ini");
        frameDeletionQueue = new Queue<IDisposable>();
    }

    public void Draw(Application app, float dT)
    {
        guiBackend.BeginFrame(app, dT);

        DrawMyGui(app);

        guiBackend.EndFrame();
        while (frameDeletionQueue.TryDequeue(out IDisposable disposable))
        {
            disposable.Dispose();
        }
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

                        SelectedEntity = new SelectedEntityInfo.Light(newLightIndex);
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
            ImGui.Text($"GC TotalPauseDuration = {GC.GetTotalPauseDuration().Milliseconds}");

            {
                float mbVertices = app.ModelManager.Vertices.SizeInBytes() / 1000000.0f;
                float mbVertexPositions = app.ModelManager.VertexPositions.SizeInBytes() / 1000000.0f;
                float mbIndices = app.ModelManager.VertexIndices.SizeInBytes() / 1000000.0f;
                float mbMeshTransforms = app.ModelManager.MeshTransforms.SizeInBytes() / 1000000.0f;
                float totalRasterizer = mbVertices + mbVertexPositions + mbIndices + mbMeshTransforms;
                if (ImGui.TreeNode($"Rasterizer Geometry total = {totalRasterizer}mb###Rasterizer Geometry total"))
                {
                    ImGui.Text($"  * Vertices ({app.ModelManager.Vertices.Length}) = {mbVertices}mb");
                    ImGui.Text($"  * VertexPositions ({app.ModelManager.Vertices.Length}) = {mbVertexPositions}mb");
                    ImGui.Text($"  * Triangles ({app.ModelManager.VertexIndices.Length / 3}) = {mbIndices}mb");
                    ImGui.Text($"  * MeshTransforms ({app.ModelManager.MeshTransforms.Length}) = {mbMeshTransforms}mb");
                    ImGui.TreePop();
                }
            }

            {
                float mbBlasTrianglesIndices = app.ModelManager.BVH.BlasTriangles.SizeInBytes() / 1000000.0f;
                float mbBlasNodes = app.ModelManager.BVH.BlasNodes.SizeInBytes() / 1000000.0f;
                float mbBTlasNodes = app.ModelManager.BVH.TlasNodes.SizeInBytes() / 1000000.0f;
                float totalBVH = mbBlasTrianglesIndices + mbBlasNodes + mbBTlasNodes;
                if (ImGui.TreeNode($"BVH total = {totalBVH}mb###BVH Total"))
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
                if (ImGui.TreeNode($"Animations total = {mbTotalAnimations}mb###Animations total"))
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

                ImGui.Checkbox("Collision##Camera", ref app.Camera.CollisionSettings.IsEnabled);
                if (app.Camera.CollisionSettings.IsEnabled)
                {
                    ImGui.SliderFloat("Radius##Camera", ref app.Camera.CollisionRadius, 0.0f, 2.0f);
                    ImGui.SliderInt("TestSteps##Camera", ref app.Camera.CollisionSettings.Settings.TestSteps, 1, 20);
                    ImGui.SliderInt("RecursiveSteps##Camera", ref app.Camera.CollisionSettings.Settings.RecursiveSteps, 1, 20);
                    ImGui.SliderFloat("NormalOffset##Camera", ref app.Camera.CollisionSettings.Settings.EpsilonNormalOffset, 0.0f, 0.01f, "%.4g");
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
                ImGui.SeparatorText($"Recording ({Keys.LeftControl} + {Keys.R}): {isRecording}");

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

            if ((app.RecorderVars.State != Application.FrameRecorderState.Recording && app.FrameStateRecorder.Count > 0))
            {
                ImGui.SeparatorText($"Replaying ({Keys.LeftControl} + {Keys.Space}): {app.RecorderVars.State == Application.FrameRecorderState.Replaying}");

                string saveFileName = string.Empty;
                for (int i = 0; i < app.ModelManager.CpuModels.Length; i++)
                {
                    saveFileName += Path.GetFileNameWithoutExtension(app.ModelManager.CpuModels[i].Root.Name);
                }
                saveFileName += DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
                saveFileName += Application.RecordingSettings.FRAME_RECORD_FILE_EXTENSION;

                if (ImGui.Button($"Save as \"{saveFileName}\""))
                {
                    app.FrameStateRecorder.SaveToFile(saveFileName);
                }

                ImGui.Checkbox("Is Video Render", ref app.RecorderVars.IsOutputFrames);
                ToolTipForItemAboveHovered("When enabled rendered images are saved into a folder.");

                tempInt = app.FrameStateRecorder.ReplayStateIndex;
                if (ImGui.SliderInt("ReplayFrame", ref tempInt, 0, Math.Max(app.FrameStateRecorder.Count - 1, 0)))
                {
                    app.FrameStateRecorder.ReplayStateIndex = tempInt;

                    FrameState state = app.FrameStateRecorder[app.FrameStateRecorder.ReplayStateIndex];
                    app.SetFrameState(state);
                }

                if (app.RenderMode_ == Application.RenderMode.PathTracer && app.RecorderVars.State == Application.FrameRecorderState.None)
                {
                    tempInt = app.RecorderVars.PathTracerSamples;
                    if (ImGui.InputInt("SamplesPerPixel", ref tempInt))
                    {
                        app.RecorderVars.PathTracerSamples = Math.Max(1, tempInt);
                    }

                    if (ImGui.Checkbox("Denoising", ref app.RecorderVars.DoDenoising))
                    {
                        if (app.RecorderVars.DoDenoising)
                        {
                            app.RecorderVars.DoDenoising = true;
                        }
                    }
                }
            }

            if (app.RecorderVars.State == Application.FrameRecorderState.None)
            {
                ImGui.SeparatorText("Saved Frame Data Files");

                string searchDir = Directory.GetCurrentDirectory();
                string[] files = Directory.GetFiles(searchDir, $"*{Application.RecordingSettings.FRAME_RECORD_FILE_EXTENSION}", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetRelativePath(searchDir, files[i]);

                    if (ImGui.Button(name))
                    {
                        app.FrameStateRecorder = StateRecorder<FrameState>.Load(name);
                    }
                }
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
            ToolTipForItemAboveHovered($"""
                This increases GPU BVH traversal performance when there exist a lot of instances.
                You probably want this together with. {nameof(app.ModelManager.BVH.RebuildTlas)}
                """
            );

            ImGui.SameLine();
            ImGui.Checkbox("CpuUseTlas", ref app.ModelManager.BVH.CpuUseTlas);
            ToolTipForItemAboveHovered($"""
                This increases CPU BVH traversal performance when there exist a lot of instances.
                You probably want this together with. {nameof(app.ModelManager.BVH.RebuildTlas)}
                """
            );
            ImGui.SameLine();
            tempBool = app.ModelManager.BVH.RebuildTlas;
            if (ImGui.Checkbox("RebuildTlas", ref tempBool))
            {
                app.ModelManager.BVH.RebuildTlas = tempBool;
            }

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

            if (ImGui.BeginCombo("Render Mode", app.RenderMode_.ToString()))
            {
                Application.RenderMode[] renderModes = Enum.GetValues<Application.RenderMode>();
                for (int i = 0; i < renderModes.Length; i++)
                {
                    Application.RenderMode mode = renderModes[i];

                    bool isSelected = mode == app.RenderMode_;
                    if (ImGui.Selectable(mode.ToString(), isSelected))
                    {
                        app.RequestRenderMode = mode;
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.Separator();

            if (app.RenderMode_ == Application.RenderMode.Rasterizer)
            {
                ImGui.Checkbox("IsWireframe", ref app.RasterizerPipeline.IsWireframe);

                ImGui.SameLine();

                tempBool = app.RasterizerPipeline.TakeMeshShaderPath && CpuPointShadow.TakeMeshShaderPath;
                if (CheckBoxEnabled("UseMeshShaders", ref tempBool, BBG.GetDeviceInfo().ExtensionSupport.MeshShader))
                {
                    app.RasterizerPipeline.TakeMeshShaderPath = tempBool;
                    CpuPointShadow.TakeMeshShaderPath = tempBool;
                }
                ToolTipForItemAboveHovered("""
                    Uses task + mesh shader from GL_NV_mesh_shader to process geometry.
                    If your GPU supports them this can significantly improve performance depending on the scene (not old sponza).
                    """
                );

                ImGui.SameLine();

                tempBool = app.RasterizerPipeline.IsHiZCulling;
                if (ImGui.Checkbox("IsHiZCulling", ref tempBool))
                {
                    app.RasterizerPipeline.IsHiZCulling = tempBool;
                }
                ToolTipForItemAboveHovered("""
                    Occlusion Culling. This is turned off because of a small edge-case issue.
                    Significantly improves performance depending on the amount of object occlusion.
                    """
                );

                if (ImGui.CollapsingHeader("Voxel Global Illumination"))
                {
                    ImGui.Checkbox("IsVXGI", ref app.RasterizerPipeline.IsVXGI);
                    if (app.RasterizerPipeline.IsVXGI)
                    {
                        ImGui.Checkbox("GridReVoxelize", ref app.RasterizerPipeline.GridReVoxelize);
                        ToolTipForItemAboveHovered("Controls wether the scene is re-voxelized every frame");

                        ImGui.Checkbox("GridFollowCamera", ref app.RasterizerPipeline.GridFollowCamera);

                        ImGui.Checkbox("IsConfigureGrid", ref app.RasterizerPipeline.IsConfigureGridMode);
                        ToolTipForItemAboveHovered("""
                            Allows to change the size of the VXGI grid.
                            It defines the space the VXGI Lighting algorithm is applied over.
                            This needs to be set manually. The green box marks the grid.
                            """
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
                            ToolTipForItemAboveHovered("""
                                When active samples are accumulated over multiple frames.
                                If there is no Temporal Anti Aliasing this is treated as being disabled.
                                """
                            );
                        }

                        tempBool = app.RasterizerPipeline.Voxelizer.IsConservativeRasterization;
                        if (CheckBoxEnabled("IsConservativeRasterization", ref tempBool, Voxelizer.ALLOW_CONSERVATIVE_RASTER))
                        {
                            app.RasterizerPipeline.Voxelizer.IsConservativeRasterization = tempBool;
                        }

                        ImGui.Text($"NV_conservative_raster: {Voxelizer.ALLOW_CONSERVATIVE_RASTER}");
                        ToolTipForItemAboveHovered("""
                            Makes the rasterizer invoke the fragment shader even if a pixel is only partially covered.
                            Currently there is some bug with this which causes overly bright voxels.
                            """
                        );

                        ImGui.Text($"TAKE_FAST_GEOMETRY_SHADER_PATH: {Voxelizer.TAKE_FAST_GEOMETRY_SHADER_PATH}");
                        ToolTipForItemAboveHovered("""
                            Combination of NV_geometry_shader_passthrough and NV_viewport_swizzle to take advantage
                            of a fast "passthrough geometry" shader instead of having to render the scene 3 times.
                            Regular geometry shaders were even slower which is why I decided to avoided them entirely.
                            """
                        );

                        ImGui.Text($"NV_shader_atomic_fp16_vector: {Voxelizer.TAKE_ATOMIC_FP16_PATH}");
                        ToolTipForItemAboveHovered("""
                            Allows to perform atomics on fp16 images without having to emulate such behaviour.
                            Most noticeably without this extension voxelizing requires 2.5x times the memory.
                            """
                        );
                    }
                }

                if (ImGui.CollapsingHeader("Shadows"))
                {
                    if (ImGui.BeginCombo("ShadowMode", app.RasterizerPipeline.ShadowMode_.ToString()))
                    {
                        RasterPipeline.ShadowMode[] shadowTechniques = Enum.GetValues<RasterPipeline.ShadowMode>();
                        for (int i = 0; i < shadowTechniques.Length; i++)
                        {
                            RasterPipeline.ShadowMode shadowMode = shadowTechniques[i];

                            bool isSelected = shadowMode == app.RasterizerPipeline.ShadowMode_;
                            if (ImGui.Selectable(shadowMode.ToString(), isSelected))
                            {
                                app.RasterizerPipeline.ShadowMode_ = shadowMode;
                            }

                            if (isSelected)
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                        ImGui.EndCombo();
                    }

                    if (app.RasterizerPipeline.ShadowMode_ == RasterPipeline.ShadowMode.RayTraced)
                    {
                        ImGui.Text("""
                            This is mostly just a tech demo.
                            There is no dedicated denoising.
                            Requires abuse of TAA. FSR2 works best.
                            """
                        );

                        ImGui.SliderInt("Samples##SamplesRayTracing", ref app.RasterizerPipeline.RayTracingSamples, 1, 10);
                    }


                    ImGui.Separator();

                    ImGui.Checkbox("GenerateShadowMaps", ref app.RasterizerPipeline.GenerateShadowMaps);
                    ToolTipForItemAboveHovered("Regardless of shadow map technique used, this is still needed for effects such as volumetric lighting.\nControls wether shadow maps are regenerated every frame.");
                }

                if (ImGui.CollapsingHeader("Anti Aliasing"))
                {
                    if (ImGui.BeginCombo("Mode", app.RasterizerPipeline.AntiAliasingMode_.ToString()))
                    {
                        RasterPipeline.AntiAliasingMode[] options = Enum.GetValues<RasterPipeline.AntiAliasingMode>();
                        for (int i = 0; i < options.Length; i++)
                        {
                            RasterPipeline.AntiAliasingMode aaMode = options[i];
                            
                            bool isSelected = aaMode == app.RasterizerPipeline.AntiAliasingMode_;
                            if (ImGui.Selectable(aaMode.ToString(), isSelected))
                            {
                                app.RasterizerPipeline.AntiAliasingMode_ = aaMode;
                            }

                            if (isSelected)
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                        ImGui.EndCombo();
                    }

                    if (app.RasterizerPipeline.AntiAliasingMode_ == RasterPipeline.AntiAliasingMode.TAA)
                    {
                        ImGui.Checkbox("IsNaiveTaa", ref app.RasterizerPipeline.TaaResolve.Settings.IsNaiveTaa);
                        ToolTipForItemAboveHovered(
                            "This is not a feature. It's mostly for fun and you can see the output of a naive TAA resolve pass.\n" +
                            "In static scenes this always converges to the correct result whereas with artifact mitigation valid samples might be rejected."
                        );

                        ImGui.SliderInt("Samples##SamplesTAA", ref app.RasterizerPipeline.TAASamples, 1, 36);

                        if (!app.RasterizerPipeline.TaaResolve.Settings.IsNaiveTaa)
                        {
                            ImGui.SliderFloat("PreferAliasingOverBlur", ref app.RasterizerPipeline.TaaResolve.Settings.PreferAliasingOverBlur, 0.0f, 1.0f);
                        }
                    }

                    if (app.RasterizerPipeline.AntiAliasingMode_ == RasterPipeline.AntiAliasingMode.FSR2)
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

                    if (app.RasterizerPipeline.AntiAliasingMode_ == RasterPipeline.AntiAliasingMode.TAA ||
                        app.RasterizerPipeline.AntiAliasingMode_ == RasterPipeline.AntiAliasingMode.FSR2)
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
                            LightingShadingRateClassifier.DebugMode mode = debugModes[i];

                            bool isSelected = mode == app.RasterizerPipeline.LightingVRS.Settings.DebugValue;
                            if (ImGui.Selectable(mode.ToString(), isSelected))
                            {
                                app.RasterizerPipeline.LightingVRS.Settings.DebugValue = mode;
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
            else if (app.RenderMode_ == Application.RenderMode.PathTracer)
            {
                if (ImGui.CollapsingHeader("PathTracing"))
                {
                    ImGui.Text($"Samples taken: {app.PathTracerPipeline.AccumulatedSamples}");

                    tempBool = app.PathTracerPipeline.DoDebugBVHTraversal;
                    if (ImGui.Checkbox("DoDebugBVHTraversal", ref tempBool))
                    {
                        app.PathTracerPipeline.DoDebugBVHTraversal = tempBool;
                    }

                    tempBool = app.PathTracerPipeline.DoTraceLights;
                    if (ImGui.Checkbox("DoTraceLights", ref tempBool))
                    {
                        app.PathTracerPipeline.DoTraceLights = tempBool;
                    }

                    if (!app.PathTracerPipeline.DoDebugBVHTraversal)
                    {
                        tempBool = app.PathTracerPipeline.DoRaySorting;
                        if (ImGui.Checkbox("Do RaySorting", ref tempBool))
                        {
                            app.PathTracerPipeline.DoRaySorting = tempBool;
                        }
                        ToolTipForItemAboveHovered("""
                            Sorts all rays by their position between each bounce to restore coherency.
                            This is only useful if many rays stay alive (e.g no Russian Roulette).
                            """
                        );

                        tempBool = app.PathTracerPipeline.DoRussianRoulette;
                        if (ImGui.Checkbox("DoRussianRoulette", ref tempBool))
                        {
                            app.PathTracerPipeline.DoRussianRoulette = tempBool;
                        }
                        ToolTipForItemAboveHovered("""
                            Probabilistically eliminates rays which carry little contribution.
                            This can significantly boost performance. How many
                            rays are eliminated depends on the albedo color.
                            Never done for the first bounce.
                            """
                        );

                        tempInt = app.PathTracerPipeline.SamplesPerPixel;
                        if (ImGui.InputInt("SPP", ref tempInt, 1))
                        {
                            app.PathTracerPipeline.SamplesPerPixel = tempInt;
                        }

                        tempInt = app.PathTracerPipeline.RayDepth;
                        if (ImGui.SliderInt("MaxRayDepth", ref tempInt, 1, 25))
                        {
                            app.PathTracerPipeline.RayDepth = tempInt;
                        }   
                    }

                    float floatTemp = app.PathTracerPipeline.FocalLength;
                    if (ImGui.InputFloat("FocalLength", ref floatTemp, 0.1f))
                    {
                        app.PathTracerPipeline.FocalLength = floatTemp;
                    }

                    floatTemp = app.PathTracerPipeline.LenseRadius;
                    if (ImGui.InputFloat("LenseRadius", ref floatTemp, 0.002f))
                    {
                        app.PathTracerPipeline.LenseRadius = floatTemp;
                    }

                    ImGui.SeparatorText("Denoising");
                    
                    if (IntelOpenImageDenoise.OIDN.LibraryFound)
                    {
                        tempBool = app.PathTracerPipeline.DenoisingEnabled;
                        if (ImGui.Checkbox("DenoisingEnabled", ref tempBool))
                        {
                            app.PathTracerPipeline.DenoisingEnabled = tempBool;
                        }
                        ToolTipForItemAboveHovered("""
                            The Intel Denoiser (OIDN 2.0) is fed certain inputs ("AOVs")
                            which the Path Tracer has to accumulate. Outputting them has
                            a small overhead which is why it needs to be manually enabled.
                            """
                        );

                        if (app.PathTracerPipeline.DenoisingEnabled)
                        {
                            if (ImGui.BeginCombo("Output", app.PathTracerPipeline.PathTracerOutput.ToString()))
                            {
                                PathTracerPipeline.OutputTexture[] outputModes = Enum.GetValues<PathTracerPipeline.OutputTexture>();
                                for (int i = 0; i < outputModes.Length; i++)
                                {
                                    PathTracerPipeline.OutputTexture mode = outputModes[i];

                                    bool isSelected = mode == app.PathTracerPipeline.PathTracerOutput;
                                    if (ImGui.Selectable(mode.ToString(), isSelected))
                                    {
                                        app.PathTracerPipeline.PathTracerOutput = mode;
                                    }
                                    if (mode == PathTracerPipeline.OutputTexture.Denoised && app.TimeEnabled)
                                    {
                                        ToolTipForItemAboveHovered("""
                                            Will revert to Noisy output when Path Tracer progress
                                            is reset. So you may want to disable time (press T).
                                            """
                                        );
                                    }

                                    if (isSelected)
                                    {
                                        ImGui.SetItemDefaultFocus();
                                    }
                                }
                                ImGui.EndCombo();
                            }

                            ImGui.Checkbox("DoPrefiltering", ref app.PathTracerPipeline.DoPrefiltering);
                            ToolTipForItemAboveHovered("""
                                If the input normal/albedo are noisy prefiltering should be applied.
                                If not, prefiltering is unnecessary and costs time and quality.    
                                """
                            );

                            ImGui.InputInt("AutoDenoisSamplesThreshold", ref app.PathTracerPipeline.AutoDenoiseSamplesThreshold);

                            if (ImGui.Button("Denoise now"))
                            {
                                app.PathTracerPipeline.Denoise();
                            }
                        }
                    }
                    else
                    {
                        ImGui.Text($"""
                            {IntelOpenImageDenoise.OIDN.LIBRARY_NAME} not found.
                            Download the DLLs from the official github
                            and place them in the working directory or
                            near the exe and restart.
                            """
                        );
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
            if (SelectedEntity is SelectedEntityInfo.Mesh meshInfo)
            {
                bool modified = false;

                ref readonly BBG.DrawElementsIndirectCommand cmd = ref app.ModelManager.DrawCommands[meshInfo.MeshId];
                ref GpuMesh mesh = ref app.ModelManager.Meshes[meshInfo.MeshId];
                ref GpuMaterial material = ref app.ModelManager.GpuMaterials[mesh.MaterialId];
                GpuMeshTransform gpuMeshTransform = app.ModelManager.MeshTransforms[meshInfo.MeshTransformId];

                Transformation meshTransform = Transformation.FromMatrix(gpuMeshTransform.ModelMatrix);

                ImGui.SeparatorText("Mesh Transform");
                if (RenderTransformPanel(ref meshTransform))
                {
                    modified = true;
                }

                ImGui.Separator();

                ImGui.SeparatorText("Mesh Material");

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
                    mesh.AbsorbanceBias = tempVec3.ToOpenTK();
                    modified = true;
                }

                if (ImGui.Checkbox("TintOnTransmissive", ref mesh.TintOnTransmissive))
                {
                    modified = true;
                }
                ToolTipForItemAboveHovered("""
                    glTF dictates we tint by the materials base color if 
                    it's volumetric or upon entering, however we may not want to do that.
                    """
                );

                if (ImGui.Checkbox("Volumetric", ref material.IsVolumetric))
                {
                    modified = true;
                }
                ToolTipForItemAboveHovered("""
                    This corresponds to KHR_materials_volume.
                    Volumetric materials are subject to refraction and absorption.
                    Thin walled materials on the other hand let light pass through undisturbed.
                    """
                );

                ImGui.Text($"Uses AlphaBlending: {material.HasAlphaBlending()}");

                ImGui.SeparatorText("Mesh Info");

                ImGui.Text($"MeshId: {meshInfo.MeshId} | MaterialId: {mesh.MaterialId}");

                if (modified)
                {
                    gpuMeshTransform.ModelMatrix = meshTransform.GetMatrix();

                    app.ModelManager.UploadMeshBuffer(meshInfo.MeshId, 1);
                    app.ModelManager.SetMeshTransform(meshInfo.MeshTransformId, gpuMeshTransform);
                    app.ModelManager.UploadMaterialBuffer(mesh.MaterialId, 1);
                    resetPathTracer = true;
                }
            }
            else if (SelectedEntity is SelectedEntityInfo.MeshTransform meshTransformInfo)
            {
                GpuMeshTransform meshTransform = app.ModelManager.MeshTransforms[meshTransformInfo.MeshTransformId];
                Transformation transform = Transformation.FromMatrix(meshTransform.ModelMatrix);

                ImGui.SeparatorText("Mesh Transform");
                if (RenderTransformPanel(ref transform))
                {
                    meshTransform.ModelMatrix = transform.GetMatrix();
                    resetPathTracer = true;

                    app.ModelManager.SetMeshTransform(meshTransformInfo.MeshTransformId, meshTransform);
                }
            }
            else if (SelectedEntity is SelectedEntityInfo.Node nodeInfo)
            {
                ModelLoader.Node node = nodeInfo.Node_;

                Transformation transform = node.LocalTransform;

                ImGui.SeparatorText("Node Transform");
                if (RenderTransformPanel(ref transform))
                {
                    node.LocalTransform = transform;
                    resetPathTracer = true;
                }

                ImGui.SeparatorText("Node Info");

                ImGui.Text($"Name: {node.Name} | ArrayIndex: {node.ArrayIndex}");
                ImGui.Text($"HasSkin: {node.HasSkin} | HasMeshes: {node.HasMeshes}");
            }
            else if (SelectedEntity is SelectedEntityInfo.Material materialInfo)
            {
                ref readonly ModelLoader.CpuMaterial cpuMaterial = ref app.ModelManager.CpuMaterials[materialInfo.MaterialId];

                for (int i = 0; i < ModelLoader.CpuMaterial.TEXTURE_COUNT; i++)
                {
                    ModelLoader.TextureType textureType = (ModelLoader.TextureType)i;
                    if (cpuMaterial.HasFallbackPixels(textureType))
                    {
                        continue;
                    }

                    (BBG.Texture texture, BBG.Sampler sampler) = cpuMaterial.SampledTextures[i];
                    BBG.TextureView textureView = new BBG.TextureView(texture, sampler.State);

                    SysVec2 content = ImGui.GetContentRegionAvail();
                    ImGui.SeparatorText($"{textureType} ({texture.Width}x{texture.Height}, {texture.Format})");
                    ImGui.Image(textureView.ID, new SysVec2(content.X), new SysVec2(0.0f, 0.0f), new SysVec2(1.0f, 1.0f));

                    frameDeletionQueue.Enqueue(textureView);
                }
            }
            else if (SelectedEntity is SelectedEntityInfo.Animation animationInfo)
            {
                ref readonly ModelManager.CpuModel model = ref app.ModelManager.CpuModels[animationInfo.ModelId];
                ref readonly ModelLoader.ModelAnimation animation = ref model.Animations[animationInfo.AnimationId];

                bool enabled = model.EnabledAnimations[animationInfo.AnimationId];
                if (ImGui.Checkbox("Apply", ref enabled))
                {
                    model.EnabledAnimations[animationInfo.AnimationId] = enabled;
                }

                ImGui.Text($"Start - End: {animation.Start} - {animation.End}sec");
                ImGui.Text($"Node Animatons: {animation.NodeAnimations.Length}");
            }
            else if (SelectedEntity is SelectedEntityInfo.Light lightInfo)
            {
                bool modified = false;

                app.LightManager.TryGetLight(lightInfo.LightId, out CpuLight cpuLight);
                ref GpuLight gpuLight = ref cpuLight.GpuLight;

                if (ImGui.Button("Delete"))
                {
                    app.LightManager.DeleteLight(lightInfo.LightId);
                    SelectedEntity = null;
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
                        gpuLight.Color = tempVec3.ToOpenTK();
                        modified = true;
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
                            app.LightManager.DeletePointShadowOfLight(lightInfo.LightId);
                        }
                    }
                    else
                    {
                        if (ImGui.Button("Create PointShadow"))
                        {
                            CpuPointShadow newPointShadow = new CpuPointShadow(256, app.RenderResolution, new OtkVec2(gpuLight.Radius, 60.0f));
                            if (!app.LightManager.CreatePointShadowForLight(newPointShadow, lightInfo.LightId))
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

                ImGui.Text($"LightID: {lightInfo.LightId}");
            }
            else
            {
                BothAxisCenteredText("SELECT AN ENTITY TO VIEW DETAILS");
            }

            if (SelectedEntity is SelectedEntityInfo.Mesh || SelectedEntity is SelectedEntityInfo.Light)
            {
                ImGui.Text($"Distance {MathF.Round(clickedEntityDistance, 3)}");
            }

            bool RenderTransformPanel(ref Transformation transform)
            {
                bool modified = false;

                tempVec3 = transform.Translation.ToNumerics();
                if (ImGui.DragFloat3("Position", ref tempVec3, 0.1f))
                {
                    transform.Translation = tempVec3.ToOpenTK();
                    modified = true;
                }

                tempVec3 = transform.Scale.ToNumerics();
                if (ImGui.DragFloat3("Scale", ref tempVec3, 0.005f))
                {
                    transform.Scale = OtkVec3.ComponentMax(tempVec3.ToOpenTK(), new OtkVec3(0.001f));
                    modified = true;
                }

                SysVec3 currentEuler = transform.Rotation.ToEulerAngles().ToNumerics() * (180f / MathF.PI);
                tempVec3 = currentEuler;
                if (ImGui.DragFloat3("Rotation", ref tempVec3, 0.5f))
                {
                    SysVec3 deltaEuler = tempVec3 - currentEuler;

                    // Convert delta to radians
                    Vector3 deltaRad = new Vector3(
                        deltaEuler.X * MathF.PI / 180f,
                        deltaEuler.Y * MathF.PI / 180f,
                        deltaEuler.Z * MathF.PI / 180f
                    );

                    // Build incremental rotation quaternion (XYZ order)
                    Quaternion qx = Quaternion.FromAxisAngle(Vector3.UnitX, deltaRad.X);
                    Quaternion qy = Quaternion.FromAxisAngle(Vector3.UnitY, deltaRad.Y);
                    Quaternion qz = Quaternion.FromAxisAngle(Vector3.UnitZ, deltaRad.Z);
                    Quaternion deltaQ = qz * qy * qx;

                    // Apply incremental rotation
                    transform.Rotation = deltaQ * transform.Rotation;

                    modified = true;
                }

                return modified;
            }
        }
        ImGui.End();

        if (ImGui.Begin("Scene Graph"))
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new SysVec2(0.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new SysVec2(0.0f));
            ImGui.BeginTable("Scene Graph", 1, ImGuiTableFlags.RowBg | ImGuiTableFlags.NoBordersInBody);
            ImGui.PopStyleVar(2);

            for (int i = 0; i < app.ModelManager.CpuModels.Length; i++)
            {
                ref readonly ModelManager.CpuModel cpuModel = ref app.ModelManager.CpuModels[i];

                ImGui.PushID(cpuModel.GetHashCode());
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.TreeNodeEx(cpuModel.Name, ImGuiTreeNodeFlags.SpanFullWidth))
                {
                    RenderNodesGraph(cpuModel.Root);

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (ImGui.TreeNodeEx("Materials"))
                    {
                        Range materialRange = app.ModelManager.GetNodeMaterialRange(cpuModel.Root);
                        for (int j = materialRange.Start; j < materialRange.End; j++)
                        {
                            ref readonly ModelLoader.CpuMaterial cpuMaterial = ref app.ModelManager.CpuMaterials[j];
                            
                            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.Leaf;
                            if (SelectedEntity is SelectedEntityInfo.Material materialInfo && materialInfo.MaterialId == j)
                            {
                                flags |= ImGuiTreeNodeFlags.Selected;
                            }

                            if (ImGui.TreeNodeEx(cpuMaterial.Name, flags))
                            {
                                ImGui.TreePop();
                            }

                            if (ImGui.IsItemClicked())
                            {
                                SelectedEntity = new SelectedEntityInfo.Material(j);
                            }
                        }

                        ImGui.TreePop();
                    }

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (ImGui.TreeNodeEx("Animations"))
                    {
                        for (int j = 0; j < cpuModel.Animations.Length; j++)
                        {
                            ref readonly ModelLoader.ModelAnimation animation = ref cpuModel.Animations[j];
                            
                            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.Leaf;
                            if (SelectedEntity is SelectedEntityInfo.Animation animationInfo && animationInfo.AnimationId == j)
                            {
                                flags |= ImGuiTreeNodeFlags.Selected;
                            }
                            if (ImGui.TreeNodeEx(animation.Name, flags))
                            {
                                ImGui.TreePop();
                            }
                            if (ImGui.IsItemClicked())
                            {
                                SelectedEntity = new SelectedEntityInfo.Animation(i, j);
                            }
                        }
                        ImGui.TreePop();
                    }

                    ImGui.TreePop();
                }

                ImGui.PopID();

                void RenderNodesGraph(ModelLoader.Node node)
                {
                    ImGui.PushID(node.GetHashCode());
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
                    if (node.IsLeaf && node.MeshTransformsRange.Count == 0)
                    {
                        flags |= ImGuiTreeNodeFlags.Leaf;
                    }

                    if (SelectedEntity is SelectedEntityInfo.Node nodeInfo && nodeInfo.Node_ == node)
                    {
                        flags |= ImGuiTreeNodeFlags.Selected;
                    }
                    bool nodeOpen = ImGui.TreeNodeEx(node.Name, flags);
                    if (ImGui.IsItemClicked())
                    {
                        SelectedEntity = new SelectedEntityInfo.Node(node);
                    }

                    if (nodeOpen)
                    {
                        for (int i = 0; i < node.Children.Length; i++)
                        {
                            RenderNodesGraph(node.Children[i]);
                        }
                        for (int i = node.MeshTransformsRange.Start; i < node.MeshTransformsRange.End; i++)
                        {
                            flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.Leaf;
                            if (SelectedEntity is SelectedEntityInfo.Mesh mesh && mesh.MeshTransformId == i ||
                                SelectedEntity is SelectedEntityInfo.MeshTransform meshTransform && meshTransform.MeshTransformId == i)
                            {
                                flags |= ImGuiTreeNodeFlags.Selected;
                            }
                            if (ImGui.TreeNodeEx($"MeshTransform_{i}", flags))
                            {
                                ImGui.TreePop();
                            }
                            if (ImGui.IsItemClicked())
                            {
                                SelectedEntity = new SelectedEntityInfo.MeshTransform(i);
                            }
                        }
                        ImGui.TreePop();
                    }

                    ImGui.PopID();
                }
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGui.TreeNodeEx("Lights"))
            {
                for (int i = 0; i < app.LightManager.Count; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.Leaf;
                    if (SelectedEntity is SelectedEntityInfo.Light lightInfo && lightInfo.LightId == i)
                    {
                        flags |= ImGuiTreeNodeFlags.Selected;
                    }
                    if (ImGui.TreeNodeEx($"Light_{i}", flags))
                    {
                        ImGui.TreePop();
                    }
                    if (ImGui.IsItemClicked())
                    {
                        SelectedEntity = new SelectedEntityInfo.Light(i);
                    }
                }
                ImGui.TreePop();
            }

            ImGui.EndTable();
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

            ImGui.Image(app.TonemapAndGamma.Result.ID, content.ToNumerics(), new SysVec2(0.0f, 1.0f), new SysVec2(1.0f, 0.0f));
        }
        ImGui.PopStyleVar();
        ImGui.End();

        RenderLoadModelDialog(app, ref resetPathTracer);
        UpdateLoadModelDialog(app, ref resetPathTracer);

        if (resetPathTracer)
        {
            app.PathTracerPipeline?.ResetAccumulation();
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
            if (app.RenderImGui)
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
                    SelectedEntity = null;
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

    private static void Test(Application app)
    {
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        Vector2i res = app.RenderResolution;

        BVH.DebugStatistics = new BVH.Statistics();
        Parallel.For(0, res.Y, y =>
        {
            for (int x = 0; x < res.X; x++)
            {
                OtkVec2 ndc = new OtkVec2(x, y) / res * 2.0f - 1.0f;
                Ray worldSpaceRay = Ray.GetWorldSpaceRay(app.PerFrameData.CameraPos, app.PerFrameData.InvProjection, app.PerFrameData.InvView, ndc);
                app.ModelManager.BVH.Intersect(worldSpaceRay, out _);
            }
        });
        //BVH.DebugStatistics.TriIntersections /= (ulong)(res.X * res.Y);
        //BVH.DebugStatistics.BoxIntersections /= (ulong)(res.X * res.Y);
        Console.WriteLine($"Statistics BoxIntersections = {BVH.DebugStatistics.BoxIntersections / (double)(res.X * res.Y)}, TriIntersections = {BVH.DebugStatistics.TriIntersections / (double)(res.X * res.Y)}");
        Console.WriteLine(sw.ElapsedMilliseconds);
    }

    private static SelectedEntityInfo RayTraceEntity(Application app, in Ray ray, out float t)
    {
        //Test(app);

        t = float.MaxValue;
        bool hitMesh = app.ModelManager.BVH.Intersect(ray, out BVH.RayHitInfo meshHitInfo);
        bool hitLight = app.LightManager.Intersect(ray, out LightManager.RayHitInfo lightHitInfo);

        if (app.RenderMode_ == Application.RenderMode.PathTracer && !app.PathTracerPipeline.DoTraceLights)
        {
            hitLight = false;
        }

        SelectedEntityInfo hitEntity = null;
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

            int meshId = app.ModelManager.BVH.BlasTriangles[meshHitInfo.TriangleId].GeometryId;
            int meshTransformId = app.ModelManager.BVH.BlasInstances[meshHitInfo.BlasInstanceId].MeshTransformId;

            hitEntity = new SelectedEntityInfo.Mesh(meshId, meshTransformId);
        }
        else
        {
            t = lightHitInfo.T;
            hitEntity = new SelectedEntityInfo.Light(lightHitInfo.LightID);
        }

        return hitEntity;
    }

    private static void ToolTipForItemAboveHovered(string text, ImGuiHoveredFlags flags = ImGuiHoveredFlags.AllowWhenDisabled)
    {
        if (ImGui.IsItemHovered(flags))
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

            public readonly string GetPopupModalName()
            {
                return $"Loading {Path.GetFileName(CompressGltfSettings.InputPath)}###{IMGUI_ID_POPUP_MODAL}";
            }
        }

        public record struct LoadParams
        {
            public bool SpawnInCamera = true;
            public bool HoistMeshes = true;
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

        public readonly void AddLoadingTask(string modelPath)
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
                        GuiLoadModel.ModelPreprocessingMode mode = preprocesModes[i];

                        bool isDisabled = mode == GuiLoadModel.ModelPreprocessingMode.gltfpack && !ModelLoader.GtlfpackWrapper.CliFound;
                        if (isDisabled)
                        {
                            ImGui.BeginDisabled();
                        }

                        bool isSelected = mode == current;
                        if (ImGui.Selectable(mode.ToString(), isSelected))
                        {
                            current = mode;
                            loadModelContext.PreprocessMode = mode;
                        }

                        if (isDisabled)
                        {
                            ImGui.EndDisabled();
                        }

                        if (mode == GuiLoadModel.ModelPreprocessingMode.gltfpack)
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
                        else if (mode == GuiLoadModel.ModelPreprocessingMode.meshoptimizer)
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
                ImGui.Checkbox("Hoist Meshes", ref loadingTask.LoadParams.HoistMeshes);
                ToolTipForItemAboveHovered("""
                    Attempts to hoist non animated/instanced mesh primitives up into a single node.
                    Meshes in a node share the same mesh transform. This will lead to the
                    generation of a larger BLASes which typically improves performance.
                    """
                );

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

                    if (loadModelContext.PreprocessMode == GuiLoadModel.ModelPreprocessingMode.meshoptimizer)
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
                                    // We override to optimizations turned off as we know gltfpack is run on the model
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
    
    private static bool LoadModel(Application app, string modelPath, in GuiLoadModel.LoadParams loadParams)
    {
        OtkVec3 modelPos = loadParams.SpawnInCamera ? app.Camera.Position : new OtkVec3(0.0f);
        Transformation transformation = new Transformation().WithScale(loadParams.Scale).WithTranslation(modelPos);
        
        if (ModelLoader.LoadGltfFromFile(modelPath, transformation.GetMatrix(), loadParams.ModelOptimizationSettings) is ModelLoader.Model model)
        {
            if (loadParams.HoistMeshes)
            {
                ModelLoader.HoistMeshPrimitives(ref model);
            }
            app.ModelManager.Add(model);
            return true;
        }

        Logger.Log(Logger.LogLevel.Error, $"Failed loading model \"{modelPath}\"");
        return false;
    }
}
