using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ImGuiNET;
using NativeFileDialogSharp;
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
        }

        public enum FrameRecorderState : int
        {
            Nothing,
            Recording,
            Replaying,
        }

        public struct SelectedEntityInfo
        {
            public EntityType EntityType { get; private set; }
            public int EntityID { get; private set; }
            public int InstanceID { get; private set; }
            public float Distance { get; private set; }

            public SelectedEntityInfo(EntityType entityType, int entityID, int instanceID, float distance)
            {
                EntityType = entityType;
                EntityID = entityID;
                InstanceID = instanceID;
                Distance = distance;
            }

            public static bool operator ==(SelectedEntityInfo first, SelectedEntityInfo other)
            {
                return (first.EntityType == other.EntityType) &&
                       (first.EntityID == other.EntityID) &&
                       (first.InstanceID == other.InstanceID);
            }

            public static bool operator !=(SelectedEntityInfo first, SelectedEntityInfo other)
            {
                return !(first == other);
            }

            public static readonly SelectedEntityInfo None = new SelectedEntityInfo()
            {
                EntityType = EntityType.None
            };
        }

        public FrameRecorderState FrameRecState { get; private set; }

        public SelectedEntityInfo SelectedEntity;

        private readonly ImGuiBackend backend;
        public Gui(int width, int height)
        {
            backend = new ImGuiBackend(width, height);
            FrameRecState = FrameRecorderState.Nothing;
            recordingRenderSampleGoal = 1;
            recordingFPS = 48;
            recordingTimer = Stopwatch.StartNew();
        }

        private readonly Stopwatch recordingTimer;
        private int recordingFPS;
        private int recordingRenderSampleGoal;
        private bool isInfiniteReplay;
        private bool isVideoRender;
        private bool useMeshShaders;
        private SysVec2 viewportHeaderSize;
        private bool shouldResetPT;
        public unsafe void Draw(Application app, float frameTime)
        {
            backend.Update(app, frameTime);
            ImGui.DockSpaceOverViewport();

            int tempInt;
            bool tempBool;
            float tempFloat;
            shouldResetPT = false;
            SysVec2 tempVec2;
            SysVec3 tempVec3;

            if (ImGui.Begin("Stats"))
            {
                float mbDrawVertices = (app.ModelSystem.Vertices.Length * ((nint)sizeof(GpuVertex) + sizeof(OtkVec3))) / 1000000.0f;
                float mbDrawIndices = (app.ModelSystem.VertexIndices.Length * (nint)sizeof(uint)) / 1000000.0f;
                float mbMeshlets = (app.ModelSystem.Meshlets.Length * (nint)sizeof(GpuMeshlet)) / 1000000.0f;
                float mbMeshletsVertexIndices = (app.ModelSystem.MeshletsVertexIndices.Length * (nint)sizeof(uint)) / 1000000.0f;
                float mbMeshletsLocalIndices = (app.ModelSystem.MeshletsLocalIndices.Length * (nint)sizeof(byte)) / 1000000.0f;
                float totalRasterizer = mbDrawVertices + mbDrawIndices + mbMeshlets + mbMeshletsVertexIndices + mbMeshletsLocalIndices;
                if (ImGui.TreeNode($"Rasterizer Geometry total = {totalRasterizer}mb"))
                {
                    ImGui.Text($"  * Vertices ({app.ModelSystem.Vertices.Length}) = {mbDrawVertices}mb");
                    ImGui.Text($"  * TriangleIndices ({app.ModelSystem.VertexIndices.Length / 3}) = {mbDrawIndices}mb");
                    ImGui.Text($"  * Meshlets ({app.ModelSystem.Meshlets.Length}) = {mbMeshlets}mb");
                    ImGui.Text($"  * MeshletsVertexIndices ({app.ModelSystem.MeshletsVertexIndices.Length}) = {mbMeshletsVertexIndices}mb");
                    ImGui.Text($"  * MeshletsPrimitiveIndices ({app.ModelSystem.MeshletsLocalIndices.Length}) = {mbMeshletsLocalIndices}mb");
                    ImGui.TreePop();
                }

                float mbBlasTrianglesIndices = (app.ModelSystem.BVH.GetBlasesTriangleIndicesCount() * (nint)sizeof(BLAS.IndicesTriplet)) / 1000000.0f;
                float mbBlasNodes = (app.ModelSystem.BVH.GetBlasesNodeCount() * (nint)sizeof(GpuBlasNode)) / 1000000.0f;
                float mbBTlasNodes = (app.ModelSystem.BVH.Tlas.Blases.Length * (nint)sizeof(GpuTlasNode)) / 1000000.0f;
                float totalBVH = mbBlasTrianglesIndices + mbBlasNodes + mbBTlasNodes;
                if (ImGui.TreeNode($"BVH total = {totalBVH}mb"))
                {
                    ImGui.Text($"  * Vertex Indices ({app.ModelSystem.BVH.GetBlasesTriangleIndicesCount() * 3}) = {mbBlasTrianglesIndices}mb");
                    ImGui.Text($"  * Blas Nodes ({app.ModelSystem.BVH.GetBlasesNodeCount()}) = {mbBlasNodes}mb");
                    ImGui.Text($"  * Tlas Nodes ({app.ModelSystem.BVH.Tlas.Nodes.Length}) = {mbBTlasNodes}mb");

                    ImGui.TreePop();
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

                    tempFloat = MathHelper.RadiansToDegrees(app.Camera.FovY);
                    if (ImGui.SliderFloat("FovY", ref tempFloat, 10.0f, 130.0f))
                    {
                        app.Camera.FovY = MathHelper.DegreesToRadians(tempFloat);
                    }

                    ImGui.SliderFloat("NearPlane", ref app.Camera.NearPlane, 0.001f, 5.0f);
                    ImGui.SliderFloat("FarPlane", ref app.Camera.FarPlane, 5.0f, 1000.0f);

                    ImGui.Separator();

                    ImGui.Checkbox("Collision##Camera", ref app.SceneVsCamCollisionSettings.IsEnabled);
                    if (app.SceneVsCamCollisionSettings.IsEnabled)
                    {
                        ImGui.SliderInt("TestSteps##Camera", ref app.SceneVsCamCollisionSettings.TestSteps, 1, 20);
                        ImGui.SliderInt("RecursiveSteps##Camera", ref app.SceneVsCamCollisionSettings.RecursiveSteps, 1, 20);
                        ImGui.SliderFloat("NormalOffset##Camera", ref app.SceneVsCamCollisionSettings.EpsilonNormalOffset, 0.0f, 0.01f, "%.4g");
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
                        ImGui.SliderInt("TestSteps##Lights##Scene", ref app.LightManager.SceneVsSphereCollisionSettings.TestSteps, 1, 20);
                        ImGui.SliderInt("RecursiveSteps##Lights##Scene", ref app.LightManager.SceneVsSphereCollisionSettings.RecursiveSteps, 1, 20);
                        ImGui.SliderFloat("NormalOffset##Lights##Scene", ref app.LightManager.SceneVsSphereCollisionSettings.EpsilonNormalOffset, 0.0f, 0.01f, "%.4g");
                    }

                    ImGui.Checkbox("LightsCollision", ref app.LightManager.MovingLightsCollisionSetting.IsEnabled);
                    if (app.LightManager.MovingLightsCollisionSetting.IsEnabled)
                    {
                        ImGui.SliderInt("RecursiveSteps##Lights##Lights", ref app.LightManager.MovingLightsCollisionSetting.RecursiveSteps, 1, 20);
                        ImGui.SliderFloat("NormalOffset##Lights##Lights", ref app.LightManager.MovingLightsCollisionSetting.EpsilonOffset, 0.0f, 0.01f, "%.4g");
                    }
                }
            }
            ImGui.End();

            if (ImGui.Begin("Frame Recorder"))
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
                        ImGui.Text($"   * Recorded frames: {app.FrameStateRecorder.StatesCount}");
                        unsafe
                        {
                            ImGui.Text($"   * File size: {app.FrameStateRecorder.StatesCount * sizeof(FrameState) / 1000}kb");
                        }
                    }
                    ImGui.Separator();
                }
                
                bool isReplaying = FrameRecState == FrameRecorderState.Replaying;
                if ((FrameRecState == FrameRecorderState.Nothing && app.FrameStateRecorder.AreStatesLoaded) || isReplaying)
                {
                    ImGui.Text($"Is Replaying (Press {Keys.LeftControl} + {Keys.Space}): {isReplaying}");
                    ImGui.Checkbox("Is Infite Replay", ref isInfiniteReplay);
                    
                    ImGui.Checkbox("Is Video Render", ref isVideoRender);
                    ToolTipForItemAboveHovered("When enabled rendered images are saved into a folder.");

                    tempInt = app.FrameStateRecorder.ReplayStateIndex;
                    if (ImGui.SliderInt("ReplayFrame", ref tempInt, 0, app.FrameStateRecorder.StatesCount - 1))
                    {
                        app.FrameStateRecorder.ReplayStateIndex = tempInt;

                        FrameState state = app.FrameStateRecorder[app.FrameStateRecorder.ReplayStateIndex];
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
                        app.FrameStateRecorder.SaveToFile(FRAME_RECORDER_FILE_PATH);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Load"))
                    {
                        app.FrameStateRecorder.Load(FRAME_RECORDER_FILE_PATH);
                    }
                    ImGui.Separator();
                }
            }
            ImGui.End();

            if (ImGui.Begin("Renderer"))
            {
                ImGui.Text($"FPS: {app.FPS}");
                ImGui.Text($"Viewport size: {app.PresentationResolution.X}x{app.PresentationResolution.Y}");
                ImGui.Text($"{Helper.GPU}");

                bool gpuUseTlas = app.ModelSystem.BVH.GpuUseTlas;
                if (ImGui.Checkbox("UseGpuTlas", ref gpuUseTlas))
                {
                    app.ModelSystem.BVH.GpuUseTlas = gpuUseTlas;
                }
                ToolTipForItemAboveHovered("This increases GPU BVH traversal performance when there exist a lot of instances.\nNote that the TLAS does not get rebuild automatically.");

                ImGui.SameLine();
                if (ImGui.Button("RebuildTlas"))
                {
                    app.ModelSystem.BVH.TlasBuild();
                }

                ImGui.SliderFloat("Exposure", ref app.TonemapAndGamma.Settings.Exposure, 0.01f, 4.0f);
                ImGui.SliderFloat("Saturation", ref app.TonemapAndGamma.Settings.Saturation, 0.0f, 1.5f);

                tempFloat = app.ResolutionScale;
                if (ImGui.SliderFloat("ResolutionScale", ref tempFloat, 0.1f, 1.0f))
                {
                    app.ResolutionScale = Math.Max(tempFloat, 0.1f);
                }

                {
                    string current = app.RenderMode.ToString();
                    if (ImGui.BeginCombo("Render Mode", current))
                    {
                        RenderMode[] renderModes = Enum.GetValues<RenderMode>();
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
                }
                
                if (app.RenderMode == RenderMode.Rasterizer)
                {
                    ImGui.Checkbox("IsWireframe", ref app.RasterizerPipeline.IsWireframe);

                    ImGui.SameLine();

                    useMeshShaders = app.RasterizerPipeline.TakeMeshShaderPath && PointShadow.TakeMeshShaderPath;
                    if (ImGui.Checkbox("UseMeshShaders", ref useMeshShaders))
                    {
                        app.RasterizerPipeline.TakeMeshShaderPath = useMeshShaders;
                        PointShadow.TakeMeshShaderPath = useMeshShaders;
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
                            ImGui.Checkbox("ShouldReVoxelize", ref app.RasterizerPipeline.ShouldReVoxelize);

                            ImGui.Checkbox("IsConfigureGrid", ref app.RasterizerPipeline.IsConfigureGridMode);
                            ToolTipForItemAboveHovered(
                                "Allows to change the size of the VXGI grid.\n" +
                                "It defines the space the VXGI Lighting algorithm is applied over.\n" +
                                "This needs to be set manually. The green box marks the grid."
                            );

                            string[] resolutions = new string[] { "512", "384", "256", "128", "64" };
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

                            if (!Voxelizer.TAKE_CONSERVATIVE_RASTER_PATH) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f); ImGui.BeginDisabled(); }
                            ImGui.Checkbox("IsConservativeRasterization", ref app.RasterizerPipeline.Voxelizer.IsConservativeRasterization);
                            if (!Voxelizer.TAKE_CONSERVATIVE_RASTER_PATH) { ImGui.EndDisabled(); ImGui.PopStyleVar(); }

                            ImGui.Text($"NV_conservative_raster: {Voxelizer.TAKE_CONSERVATIVE_RASTER_PATH}");
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
                        string current = app.RasterizerPipeline.ShadowMode.ToString();
                        if (ImGui.BeginCombo("ShadowMode", current))
                        {
                            RasterPipeline.ShadowTechnique[] shadowTechniques = Enum.GetValues<RasterPipeline.ShadowTechnique>();
                            for (int i = 0; i < shadowTechniques.Length; i++)
                            {
                                string enumName = shadowTechniques[i].ToString();

                                bool isSelected = current == enumName;
                                if (ImGui.Selectable(enumName, isSelected))
                                {
                                    current = enumName;
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
                        ToolTipForItemAboveHovered("Regardless of shadow map technique used, this is still needed for effects such as volumetric lighting. Controls wether shadow maps are regenerated every frame.");
                    }

                    if (ImGui.CollapsingHeader("Anti Aliasing"))
                    {
                        string current = app.RasterizerPipeline.TemporalAntiAliasing.ToString();
                        if (ImGui.BeginCombo("Mode", current))
                        {
                            RasterPipeline.TemporalAntiAliasingMode[] options = Enum.GetValues<RasterPipeline.TemporalAntiAliasingMode>();
                            for (int i = 0; i < options.Length; i++)
                            {
                                string enumName = options[i].ToString();
                                bool isSelected = current == enumName;
                                if (ImGui.Selectable(enumName, isSelected))
                                {
                                    current = enumName;
                                    app.RasterizerPipeline.TemporalAntiAliasing = (RasterPipeline.TemporalAntiAliasingMode)i;
                                }

                                if (isSelected)
                                {
                                    ImGui.SetItemDefaultFocus();
                                }
                            }
                            ImGui.EndCombo();
                        }

                        if (app.RasterizerPipeline.TemporalAntiAliasing == RasterPipeline.TemporalAntiAliasingMode.TAA)
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
                        else if (app.RasterizerPipeline.TemporalAntiAliasing == RasterPipeline.TemporalAntiAliasingMode.FSR2)
                        {
                            ImGui.Text(
                                "FSR2 (by AMD) does Anti Aliasing but\n" +
                                "simultaneously also upscaling.\n" +
                                "Try reducing resolution scale!\n" +
                                "Note: Performance is lower than expected\n" +
                                "on NVIDIA!"
                            );

                            ImGui.Checkbox("IsSharpening", ref app.RasterizerPipeline.FSR2Wrapper.IsSharpening);

                            if (app.RasterizerPipeline.FSR2Wrapper.IsSharpening)
                            {
                                ImGui.SliderFloat("Sharpness", ref app.RasterizerPipeline.FSR2Wrapper.Sharpness, 0.0f, 1.0f);
                            }

                            ImGui.SliderFloat("AddMipBias", ref app.RasterizerPipeline.FSR2AddMipBias, -3.0f, 3.0f);
                            ToolTipForItemAboveHovered("This bias is applied in addition to the 'optimal' FSR2 computed bias\n");
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
                        ImGui.Text($"NV_shading_rate_image: {VariableRateShading.HAS_VARIABLE_RATE_SHADING}");
                        ToolTipForItemAboveHovered(
                            "Allows the renderer to choose a unique shading rate on each 16x16 tile\n" +
                            "as a mesaure of increasing performance by decreasing fragment shader\n" +
                            "invocations in regions where less detail may be required."
                        );

                        if (!VariableRateShading.HAS_VARIABLE_RATE_SHADING) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f); ImGui.BeginDisabled(); }
                        ImGui.Checkbox("IsVariableRateShading", ref app.RasterizerPipeline.IsVariableRateShading);
                        if (!VariableRateShading.HAS_VARIABLE_RATE_SHADING) { ImGui.EndDisabled(); ImGui.PopStyleVar(); }

                        string current = app.RasterizerPipeline.LightingVRS.Settings.DebugValue.ToString();
                        if (ImGui.BeginCombo("DebugMode", current))
                        {
                            LightingShadingRateClassifier.DebugMode[] debugModes = Enum.GetValues<LightingShadingRateClassifier.DebugMode>();
                            for (int i = 0; i < debugModes.Length; i++)
                            {
                                string enumName = debugModes[i].ToString();

                                bool isSelected = current == enumName;
                                if (ImGui.Selectable(enumName, isSelected))
                                {
                                    current = enumName;
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
                else if (app.RenderMode == RenderMode.PathTracer)
                {
                    if (ImGui.CollapsingHeader("PathTracing"))
                    {
                        if (app.RenderMode == RenderMode.PathTracer)
                        {
                            ImGui.Text($"Samples taken: {app.PathTracer.AccumulatedSamples}");
                        }

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

                        tempBool = app.PathTracer.IsAlwaysTintWithAlbedo;
                        if (ImGui.Checkbox("IsAlwaysTintWithAlbedo", ref tempBool))
                        {
                            app.PathTracer.IsAlwaysTintWithAlbedo = tempBool;
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
                    bool shouldUpdateSkyBox = false;

                    tempBool = SkyBoxManager.GetSkyBoxMode() == SkyBoxManager.SkyBoxMode.ExternalAsset;
                    if (ImGui.Checkbox("IsExternalSkyBox", ref tempBool))
                    {
                        SkyBoxManager.SetSkyBoxMode(tempBool ? SkyBoxManager.SkyBoxMode.ExternalAsset : SkyBoxManager.SkyBoxMode.InternalAtmosphericScattering);

                        shouldResetPT = true;
                    }

                    if (SkyBoxManager.GetSkyBoxMode() == SkyBoxManager.SkyBoxMode.InternalAtmosphericScattering)
                    {
                        tempFloat = SkyBoxManager.AtmosphericScatterer.Elevation;
                        if (ImGui.SliderFloat("Elevation", ref tempFloat, -MathF.PI, MathF.PI))
                        {
                            SkyBoxManager.AtmosphericScatterer.Elevation = tempFloat;
                            shouldUpdateSkyBox = true;
                        }

                        tempFloat = SkyBoxManager.AtmosphericScatterer.Azimuth;
                        if (ImGui.SliderFloat("Azimuth", ref tempFloat, -MathF.PI, MathF.PI))
                        {
                            SkyBoxManager.AtmosphericScatterer.Azimuth = tempFloat;
                            shouldUpdateSkyBox = true;
                        }

                        tempFloat = SkyBoxManager.AtmosphericScatterer.LightIntensity;
                        if (ImGui.DragFloat("Intensity", ref tempFloat, 0.2f))
                        {
                            SkyBoxManager.AtmosphericScatterer.LightIntensity = tempFloat;

                            shouldUpdateSkyBox = true;
                        }

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

                        if (shouldUpdateSkyBox)
                        {
                            shouldResetPT = true;
                            SkyBoxManager.AtmosphericScatterer.Compute();
                        }
                    }
                }

            }
            ImGui.End();

            if (ImGui.Begin("Entity Add"))
            {
                if (ImGui.Button("Add light"))
                {
                    Ray worldSpaceRay = Ray.GetWorldSpaceRay(app.GpuBasicData.CameraPos, app.GpuBasicData.InvProjection, app.GpuBasicData.InvView, new OtkVec2(0.0f));
                    OtkVec3 spawnPoint = worldSpaceRay.Origin + worldSpaceRay.Direction * 1.5f;

                    CpuLight newLight = new CpuLight(spawnPoint, new OtkVec3(Helper.RandomVec3(32.0f, 88.0f)), 0.3f);
                    if (app.LightManager.AddLight(newLight))
                    {
                        int newLightIndex = app.LightManager.Count - 1;
                        PointShadow pointShadow = new PointShadow(256, app.RenderResolution, new OtkVec2(newLight.GpuLight.Radius, 60.0f));
                        if (!app.LightManager.CreatePointShadowForLight(pointShadow, newLightIndex))
                        {
                            pointShadow.Dispose();
                        }

                        float distance = OtkVec3.Distance(app.Camera.Position, newLight.GpuLight.Position);
                        SelectedEntity = new SelectedEntityInfo(EntityType.Light, newLightIndex, 0, distance);

                        shouldResetPT = true;
                    }
                }
                ModuleLoadSceneRender(app);
            }
            ImGui.End();

            if (ImGui.Begin("Entity Properties"))
            {
                if (SelectedEntity.EntityType != EntityType.None)
                {
                    ImGui.Text($"{SelectedEntity.EntityType}ID: {SelectedEntity.EntityID}");
                    ImGui.Text($"Distance: {MathF.Round(SelectedEntity.Distance, 3)}");
                }
                if (SelectedEntity.EntityType == EntityType.Mesh)
                {
                    bool shouldUpdateMesh = false;
                    ref readonly GpuDrawElementsCmd cmd = ref app.ModelSystem.DrawCommands[SelectedEntity.EntityID];
                    ref GpuMesh mesh = ref app.ModelSystem.Meshes[SelectedEntity.EntityID];
                    ref GpuMeshInstance meshInstance = ref app.ModelSystem.MeshInstances[SelectedEntity.InstanceID];

                    ImGui.Text($"MaterialID: {mesh.MaterialIndex}");
                    ImGui.Text($"InstanceID: {SelectedEntity.InstanceID - cmd.BaseInstance}");
                    ImGui.Text($"Triangle Count: {cmd.IndexCount / 3}");

                    OtkVec3 beforeTranslation = meshInstance.ModelMatrix.ExtractTranslation();
                    tempVec3 = beforeTranslation.ToNumerics();
                    if (ImGui.DragFloat3("Position", ref tempVec3, 0.1f))
                    {
                        shouldUpdateMesh = true;
                        OtkVec3 dif = tempVec3.ToOpenTK() - beforeTranslation;
                        meshInstance.ModelMatrix = meshInstance.ModelMatrix * Matrix4.CreateTranslation(dif);
                    }

                    tempVec3 = meshInstance.ModelMatrix.ExtractScale().ToNumerics();
                    if (ImGui.DragFloat3("Scale", ref tempVec3, 0.005f))
                    {
                        shouldUpdateMesh = true;
                        OtkVec3 temp = OtkVec3.ComponentMax(tempVec3.ToOpenTK(), new OtkVec3(0.001f));
                        meshInstance.ModelMatrix = Matrix4.CreateScale(temp) * meshInstance.ModelMatrix.ClearScale();
                    }

                    meshInstance.ModelMatrix.ExtractRotation().ToEulerAngles(out OtkVec3 beforeAngles);
                    tempVec3 = beforeAngles.ToNumerics();
                    if (ImGui.DragFloat3("Rotation", ref tempVec3, 0.005f))
                    {
                        shouldUpdateMesh = true;
                        OtkVec3 dif = tempVec3.ToOpenTK() - beforeAngles;

                        meshInstance.ModelMatrix = Matrix4.CreateRotationZ(dif.Z) *
                                                    Matrix4.CreateRotationY(dif.Y) *
                                                    Matrix4.CreateRotationX(dif.X) *
                                                    meshInstance.ModelMatrix;
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

                    if (ImGui.SliderFloat("TransmissionBias", ref mesh.TransmissionBias, -1.0f, 1.0f))
                    {
                        shouldUpdateMesh = true;
                    }

                    if (ImGui.SliderFloat("IORBias", ref mesh.IORBias, -2.0f, 5.0f))
                    {
                        shouldUpdateMesh = true;
                    }

                    tempVec3 = mesh.AbsorbanceBias.ToNumerics();
                    if (ImGui.DragFloat3("AbsorbanceBias", ref tempVec3))
                    {
                        mesh.AbsorbanceBias = tempVec3.ToOpenTK();
                        shouldUpdateMesh = true;
                    }

                    if (shouldUpdateMesh)
                    {
                        shouldResetPT = true;
                        app.ModelSystem.UpdateMeshBuffer(SelectedEntity.EntityID, 1);
                    }
                }
                else if (SelectedEntity.EntityType == EntityType.Light)
                {
                    bool shouldUpdateLight = false;
                    
                    app.LightManager.TryGetLight(SelectedEntity.EntityID, out CpuLight cpuLight);
                    ref GpuLight gpuLight = ref cpuLight.GpuLight;

                    if (ImGui.Button("Delete"))
                    {
                        app.LightManager.DeleteLight(SelectedEntity.EntityID);
                        SelectedEntity = SelectedEntityInfo.None;
                        shouldResetPT = true;
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
                            shouldUpdateLight = true;
                            gpuLight.Color = tempVec3.ToOpenTK();
                        }

                        if (ImGui.DragFloat("Radius", ref gpuLight.Radius, 0.05f, 0.01f, 30.0f))
                        {
                            shouldUpdateLight = true;
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
                                PointShadow newPointShadow = new PointShadow(256, app.RenderResolution, new OtkVec2(gpuLight.Radius, 60.0f));
                                if (!app.LightManager.CreatePointShadowForLight(newPointShadow, SelectedEntity.EntityID))
                                {
                                    newPointShadow.Dispose();
                                }
                            }
                        }

                        if (app.LightManager.TryGetPointShadow(cpuLight.GpuLight.PointShadowIndex, out PointShadow pointShadow))
                        {
                            tempInt = pointShadow.ShadowMap.Width;
                            if (ImGui.InputInt("Resolution", ref tempInt))
                            {
                                pointShadow.SetSizeShadowMap(tempInt);
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
            }
            ImGui.End();

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new SysVec2(0.0f));
            if (ImGui.Begin($"Viewport"))
            {
                SysVec2 content = ImGui.GetContentRegionAvail();
                if (content.X != app.PresentationResolution.X || content.Y != app.PresentationResolution.Y)
                {
                    // Viewport changed, inform app of the new resolution
                    app.PresentationResolution = new Vector2i((int)content.X, (int)content.Y);
                }

                SysVec2 tileBar = ImGui.GetCursorPos();
                viewportHeaderSize = ImGui.GetWindowPos() + tileBar;

                ImGui.Image(app.TonemapAndGamma.Result.ID, content, new SysVec2(0.0f, 1.0f), new SysVec2(1.0f, 0.0f));
            }
            ImGui.PopStyleVar();
            ImGui.End();

            if (shouldResetPT && app.PathTracer != null)
            {
                app.PathTracer.ResetRenderProcess();
            }
            backend.Render();
        }

        public void Update(Application app)
        {
            if (app.MouseState.CursorMode == CursorModeValue.CursorNormal)
            {
                backend.IsIgnoreMouseInput = false;
            }
            else if (app.MouseState.CursorMode == CursorModeValue.CursorDisabled)
            {
                backend.IsIgnoreMouseInput = true;
            }

            void TakeScreenshot()
            {
                int frameIndex = app.FrameStateRecorder.ReplayStateIndex;
                if (frameIndex == 0) frameIndex = app.FrameStateRecorder.StatesCount;

                const string RECORDED_FRAME_DATA_OUT_DIR = "RecordedFrames";
                Directory.CreateDirectory(RECORDED_FRAME_DATA_OUT_DIR);

                Helper.TextureToDiskJpg(app.TonemapAndGamma.Result, $"{RECORDED_FRAME_DATA_OUT_DIR}/{frameIndex}");
            }

            ModuleLoadSceneUpdate(app);

            if (FrameRecState == FrameRecorderState.Replaying)
            {
                if (app.RenderMode == RenderMode.Rasterizer || (app.RenderMode == RenderMode.PathTracer && app.PathTracer.AccumulatedSamples >= recordingRenderSampleGoal))
                {
                    app.FrameStateRecorder.ReplayStateIndex++;
                    if (isVideoRender)
                    {
                        TakeScreenshot();
                    }
                }
                
                if (!isInfiniteReplay && app.FrameStateRecorder.ReplayStateIndex == 0)
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

                app.FrameStateRecorder.Record(state);
                recordingTimer.Restart();
            }

            if (FrameRecState != FrameRecorderState.Replaying &&
                app.KeyboardState[Keys.R] == Keyboard.InputState.Touched &&
                app.KeyboardState[Keys.LeftControl] == Keyboard.InputState.Pressed)
            {
                if (FrameRecState == FrameRecorderState.Recording)
                {
                    FrameRecState = FrameRecorderState.Nothing;
                }
                else
                {
                    FrameRecState = FrameRecorderState.Recording;
                    app.FrameStateRecorder.Clear();
                }
            }

            if (FrameRecState != FrameRecorderState.Recording && app.FrameStateRecorder.AreStatesLoaded &&
                app.KeyboardState[Keys.Space] == Keyboard.InputState.Touched &&
                app.KeyboardState[Keys.LeftControl] == Keyboard.InputState.Pressed)
            {
                FrameRecState = FrameRecState == FrameRecorderState.Replaying ? FrameRecorderState.Nothing : FrameRecorderState.Replaying;
                if (FrameRecState == FrameRecorderState.Replaying)
                {
                    app.SceneVsCamCollisionSettings.IsEnabled = false;
                    app.MouseState.CursorMode = CursorModeValue.CursorNormal;
                }
            }

            if (FrameRecState == FrameRecorderState.Replaying)
            {
                FrameState state = app.FrameStateRecorder[app.FrameStateRecorder.ReplayStateIndex];
                app.Camera.Position = state.Position;
                app.Camera.UpVector = state.UpVector;
                app.Camera.LookX = state.LookX;
                app.Camera.LookY = state.LookY;
            }

            if (app.MouseState.CursorMode == CursorModeValue.CursorNormal && app.MouseState[MouseButton.Left] == Keyboard.InputState.Touched)
            {
                OtkVec2 clickedPixel = app.MouseState.Position;
                if (app.RenderGui)
                {
                    clickedPixel -= viewportHeaderSize.ToOpenTK();
                }
                clickedPixel.Y = app.TonemapAndGamma.Result.Height - clickedPixel.Y;

                OtkVec2 ndc = clickedPixel / app.PresentationResolution * 2.0f - new OtkVec2(1.0f);
                bool clickedInsideViewport = ndc.X < 1.0f && ndc.Y < 1.0f && ndc.X > -1.0f && ndc.Y > -1.0f;
                if (clickedInsideViewport)
                {
                    Ray worldSpaceRay = Ray.GetWorldSpaceRay(app.GpuBasicData.CameraPos, app.GpuBasicData.InvProjection, app.GpuBasicData.InvView, ndc);
                    SelectedEntityInfo hitEntity = RayTraceEntity(app, worldSpaceRay);

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
        private static SelectedEntityInfo RayTraceEntity(Application app, in Ray ray)
        {
            bool hitMesh = app.ModelSystem.BVH.Intersect(ray, out BVH.RayHitInfo meshHitInfo);
            bool hitLight = app.LightManager.Intersect(ray, out LightManager.RayHitInfo lightHitInfo);
            if (app.RenderMode == RenderMode.PathTracer && !app.PathTracer.IsTraceLights) hitLight = false;

            SelectedEntityInfo hitEntity = SelectedEntityInfo.None;
            if (!hitMesh && !hitLight)
            {
                return hitEntity;
            }

            if (!hitLight) lightHitInfo.T = float.MaxValue;
            if (!hitMesh) meshHitInfo.T = float.MaxValue;

            if (meshHitInfo.T < lightHitInfo.T)
            {
                hitEntity = new SelectedEntityInfo(EntityType.Mesh, meshHitInfo.MeshID, meshHitInfo.InstanceID, meshHitInfo.T);
            }
            else
            {
                hitEntity = new SelectedEntityInfo(EntityType.Light, lightHitInfo.LightID, 0, lightHitInfo.T);
            }

            return hitEntity;
        }

        public void SetSize(Vector2i size)
        {
            backend.WindowResized(size.X, size.Y);
        }
        public void PressChar(char key)
        {
            backend.PressChar(key);
        }

        public void Dispose()
        {
            backend.Dispose();
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
    }

    partial class Gui
    {
        private struct ModuleLoadSceneVars
        {
            public bool DialogAskForCompression;
            public string GuiGltfSourcePath;

            public Tuple<Task, string>?[] GltfCompressionsTasks = new Tuple<Task, string>[10];

            public ModuleLoadSceneVars()
            {
            }
        }

        private ModuleLoadSceneVars moduleLoadSceneVars = new ModuleLoadSceneVars();
        public void ModuleLoadSceneRender(Application app)
        {
            const string POPUP_ASK_FOR_COMPRESSION = "Compress glTF?";
            if (ImGui.Button("Load glTF"))
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
                    if (ModelLoader.CompressionUtils.IsCompressedGltf(result.Path))
                    {
                        LoadModel(app, result.Path);
                    }
                    if (!ModelLoader.CompressionUtils.IsCompressedGltf(result.Path) && !ModelLoader.CompressionUtils.CompressionToolAvailable())
                    {
                        LoadModel(app, result.Path);
                        Logger.Log(Logger.LogLevel.Warn, $"The loaded model \"{Path.GetFileName(result.Path)}\" is uncompressed and the compresion tool \"{ModelLoader.CompressionUtils.COMPRESSION_TOOL_NAME}\" was not found.");
                    }

                    if (!ModelLoader.CompressionUtils.IsCompressedGltf(result.Path) && ModelLoader.CompressionUtils.CompressionToolAvailable())
                    {
                        moduleLoadSceneVars.DialogAskForCompression = true;
                        ImGui.OpenPopup(POPUP_ASK_FOR_COMPRESSION);
                    }

                    moduleLoadSceneVars.GuiGltfSourcePath = result.Path;
                }
            }

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new SysVec2(0.0f));
            if (ImGui.BeginPopupModal(POPUP_ASK_FOR_COMPRESSION, ref moduleLoadSceneVars.DialogAskForCompression, ImGuiWindowFlags.NoNavInputs))
            {
                HorizontallyCenteredText("The glTF model doesn't have compression.\nWould you like to compress to disk via gltfpack and load that?");
                ImGui.Separator();

                float availX = ImGui.GetContentRegionAvail().X;
                float availY = ImGui.GetContentRegionAvail().Y;
                ImGui.SetCursorPosX(availX * 0.1f);
                if (ImGui.Button("Yes", new SysVec2(ImGui.GetWindowWidth() * 0.35f, availY * 0.6f)))
                {
                    ImGui.CloseCurrentPopup();

                    int workerIndex = -1;
                    for (int i = 0; i < moduleLoadSceneVars.GltfCompressionsTasks.Length; i++)
                    {
                        if (moduleLoadSceneVars.GltfCompressionsTasks[i] == null)
                        {
                            workerIndex = i;
                            break;
                        }
                    }
                    if (workerIndex == -1)
                    {
                        Logger.Log(Logger.LogLevel.Error, "Too many glTF file compressions happening at once. Falling back to uncompressed model");
                        LoadModel(app, moduleLoadSceneVars.GuiGltfSourcePath);
                        return;
                    }

                    string fileName = Path.GetFileName(moduleLoadSceneVars.GuiGltfSourcePath);
                    string compressedGltfDir = Path.Combine(Path.GetDirectoryName(moduleLoadSceneVars.GuiGltfSourcePath), "Compressed");
                    string compressedGtlfPath = Path.Combine(compressedGltfDir, fileName);
                    Directory.CreateDirectory(compressedGltfDir);

                    Task? task = ModelLoader.CompressionUtils.CompressGltf(new ModelLoader.CompressionUtils.CompressGltfSettings()
                    {
                        InputPath = moduleLoadSceneVars.GuiGltfSourcePath,
                        OutputPath = compressedGtlfPath,
                        ProcessError = (string message) =>
                        {
                            Logger.Log(Logger.LogLevel.Error, message);
                            Logger.Log(Logger.LogLevel.Error, $"An error occured while compressing \"{fileName}\". Falling back to uncompressed model");
                            moduleLoadSceneVars.GltfCompressionsTasks[workerIndex] = new Tuple<Task, string>(Task.CompletedTask, moduleLoadSceneVars.GuiGltfSourcePath);
                        },
                        ProcessOutput = (string message) =>
                        {
                            Logger.Log(Logger.LogLevel.Info, message);
                        }
                    });

                    if (task == null)
                    {
                        Logger.Log(Logger.LogLevel.Error, $"Failed to create compression task. Falling back to uncompressed model");
                        moduleLoadSceneVars.GltfCompressionsTasks[workerIndex] = new Tuple<Task, string>(Task.CompletedTask, moduleLoadSceneVars.GuiGltfSourcePath);
                    }
                    else
                    {
                        moduleLoadSceneVars.GltfCompressionsTasks[workerIndex] = new Tuple<Task, string>(task, compressedGtlfPath);
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button("No", new SysVec2(ImGui.GetWindowWidth() * 0.35f, availY * 0.6f)))
                {
                    ImGui.CloseCurrentPopup();

                    LoadModel(app, moduleLoadSceneVars.GuiGltfSourcePath);
                }

                ImGui.EndPopup();
            }
            ImGui.PopStyleVar();

            for (int i = 0; i < moduleLoadSceneVars.GltfCompressionsTasks.Length; i++)
            {
                if (moduleLoadSceneVars.GltfCompressionsTasks[i] == null)
                {
                    continue;
                }

                (Task task, string gltfPath) = moduleLoadSceneVars.GltfCompressionsTasks[i];
                if (!task.IsCompleted)
                {
                    ImGui.Text($"Compressing {Path.GetFileName(gltfPath)}...\n");
                }
            }
        }
        public void ModuleLoadSceneUpdate(Application app)
        {
            for (int i = 0; i < moduleLoadSceneVars.GltfCompressionsTasks.Length; i++)
            {
                if (moduleLoadSceneVars.GltfCompressionsTasks[i] == null)
                {
                    continue;
                }

                (Task task, string gltfPath) = moduleLoadSceneVars.GltfCompressionsTasks[i];
                if (task.IsCompletedSuccessfully)
                {
                    LoadModel(app, gltfPath);
                    moduleLoadSceneVars.GltfCompressionsTasks[i] = null;
                }
            }
        }
        private void LoadModel(Application app, string path)
        {
            ModelLoader.Model newScene = ModelLoader.LoadGltfFromFile(path, Matrix4.CreateTranslation(app.Camera.Position));
            app.ModelSystem.Add(newScene);

            int newMeshIndex = app.ModelSystem.Meshes.Length - 1;

            ref readonly GpuDrawElementsCmd cmd = ref app.ModelSystem.DrawCommands[newMeshIndex];
            OtkVec3 position = app.ModelSystem.MeshInstances[cmd.BaseInstance].ModelMatrix.ExtractTranslation();
            float distance = OtkVec3.Distance(app.Camera.Position, position);

            SelectedEntity = new SelectedEntityInfo(EntityType.Mesh, newMeshIndex, cmd.BaseInstance, distance);
            shouldResetPT = true;
        }
    }
}
