using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ImGuiNET;
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

        public ImGuiBackend ImGuiBackend;
        private EntityType selectedEntityType;
        private int selectedEntityIndex;
        private float selectedEntityDist;

        public Gui(int width, int height)
        {
            ImGuiBackend = new ImGuiBackend(width, height);
        }

        public bool isHoveredViewport = true;
        public System.Numerics.Vector2 viewportHeaderSize;
        public void Draw(Application window, float frameTime)
        {
            ImGuiBackend.Update(window, frameTime);
            ImGui.DockSpaceOverViewport();

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0.0f));
            ImGui.Begin($"Viewport");
            
            System.Numerics.Vector2 content = ImGui.GetContentRegionAvail();
            
            System.Numerics.Vector2 tileBar = ImGui.GetCursorPos();
            viewportHeaderSize = ImGui.GetWindowPos() + tileBar;
            
            ImGui.Image((IntPtr)window.PostProcessor.Result.ID, content, new System.Numerics.Vector2(0.0f, 1.0f), new System.Numerics.Vector2(1.0f, 0.0f));
            isHoveredViewport = ImGui.IsItemHovered();

            if (content.X != window.ViewportResolution.X || content.Y != window.ViewportResolution.Y)
            {
                window.SetViewportResolution((int)content.X, (int)content.Y);
            }

            ImGui.End();
            ImGui.PopStyleVar();

            ImGui.Begin("Renderer");
            {
                ImGui.Text($"FPS: {window.FPS}");
                ImGui.Text($"Viewport size: {window.ViewportResolution.X}x{window.ViewportResolution.Y}");

                if (window.IsPathTracing)
                    ImGui.Text($"Samples taken: {window.GLSLBasicData.FreezeFrameCounter}");

                string[] renderModes = new string[] { "Rasterizer", "PathTracer" };
                string current = window.IsPathTracing ? renderModes[1] : renderModes[0];
                if (ImGui.BeginCombo("Render Path", current))
                {
                    for (int i = 0; i < renderModes.Length; i++)
                    {
                        bool isSelected = current == renderModes[i];
                        if (ImGui.Selectable(renderModes[i], isSelected))
                        {
                            current = renderModes[i];
                            window.IsPathTracing = current == renderModes[1];
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                bool tempBool = window.PostProcessor.IsDithering;
                if (ImGui.Checkbox("IsDithering", ref tempBool))
                {
                    window.PostProcessor.IsDithering = tempBool;
                }

                if (!window.IsPathTracing)
                {
                    ImGui.Checkbox("IsWireframe", ref window.IsWireframe);

                    if (ImGui.CollapsingHeader("Variable Rate Shading"))
                    {
                        if (!VariableRateShading.NV_SHADING_RATE_IMAGE)
                        {
                            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f);
                            ImGui.BeginDisabled();
                        }
                        ImGui.Checkbox("IsVRS", ref window.IsVRSForwardRender);
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

                        current = window.ForwardPassVRS.DebugValue.ToString();
                        if (ImGui.BeginCombo("DebugMode", current))
                        {
                            for (int i = 0; i < debugModes.Length; i++)
                            {
                                bool isSelected = current == debugModes[i];
                                if (ImGui.Selectable(debugModes[i], isSelected))
                                {
                                    current = debugModes[i];
                                    window.ForwardPassVRS.DebugValue = VariableRateShading.DebugMode.NoDebug + i;
                                }

                                if (isSelected)
                                    ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }

                        float tempFloat = window.ForwardPassVRS.SpeedFactor;
                        if (ImGui.SliderFloat("SpeedFactor", ref tempFloat, 0.0f, 1.0f))
                        {
                            window.ForwardPassVRS.SpeedFactor = tempFloat;
                        }

                        tempFloat = window.ForwardPassVRS.LumVarianceFactor;
                        if (ImGui.SliderFloat("LumVarianceFactor", ref tempFloat, 0.0f, 0.3f))
                        {
                            window.ForwardPassVRS.LumVarianceFactor = tempFloat;
                        }
                    }

                    if (ImGui.CollapsingHeader("VolumetricLighting"))
                    {
                        ImGui.Checkbox("IsVolumetricLighting", ref window.IsVolumetricLighting);
                        if (window.IsVolumetricLighting)
                        {
                            int tempInt = window.VolumetricLight.Samples;
                            if (ImGui.SliderInt("Samples", ref tempInt, 1, 100))
                            {
                                window.VolumetricLight.Samples = tempInt;
                            }


                            float tempFloat = window.VolumetricLight.Scattering;
                            if (ImGui.SliderFloat("Scattering", ref tempFloat, 0.0f, 1.0f))
                            {
                                window.VolumetricLight.Scattering = tempFloat;
                            }

                            tempFloat = window.VolumetricLight.Strength;
                            if (ImGui.SliderFloat("Strength", ref tempFloat, 0.0f, 500.0f))
                            {
                                window.VolumetricLight.Strength = tempFloat;
                            }

                            System.Numerics.Vector3 tempVec = window.VolumetricLight.Absorbance.ToSystemVec();
                            if (ImGui.InputFloat3("Absorbance", ref tempVec))
                            {
                                Vector3 temp = tempVec.ToOpenTKVec();
                                temp = Vector3.ComponentMax(temp, Vector3.Zero);
                                window.VolumetricLight.Absorbance = temp;
                            }

                            tempBool = window.VolumetricLight.IsTemporalAccumulation;
                            if (!window.PostProcessor.TaaEnabled) ImGui.BeginDisabled();
                            if (ImGui.Checkbox("IsTemporalAccumulation", ref tempBool))
                            {
                                window.VolumetricLight.IsTemporalAccumulation = tempBool;
                            }
                            ImGui.SameLine();
                            InfoMark(
                                "Requires TAA to be enabled. " +
                                $"When active samples are accumulated over {window.PostProcessor.TaaSamples} frames (based on TAA setting)."
                            );
                            if (!window.PostProcessor.TaaEnabled) ImGui.EndDisabled();
                        }
                    }

                    if (ImGui.CollapsingHeader("SSAO"))
                    {
                        ImGui.Checkbox("IsSSAO", ref window.IsSSAO);
                        if (window.IsSSAO)
                        {
                            int tempInt = window.SSAO.Samples;
                            if (ImGui.SliderInt("Samples  ", ref tempInt, 1, 50))
                            {
                                window.SSAO.Samples = tempInt;
                            }

                            float tempFloat = window.SSAO.Radius;
                            if (ImGui.SliderFloat("Radius", ref tempFloat, 0.0f, 0.5f))
                            {
                                window.SSAO.Radius = tempFloat;
                            }

                            tempFloat = window.SSAO.Strength;
                            if (ImGui.SliderFloat("Strength ", ref tempFloat, 0.0f, 20.0f))
                            {
                                window.SSAO.Strength = tempFloat;
                            }
                        }
                    }

                    if (ImGui.CollapsingHeader("SSR"))
                    {
                        ImGui.Checkbox("IsSSR", ref window.IsSSR);
                        if (window.IsSSR)
                        {
                            int tempInt = window.SSR.Samples;
                            if (ImGui.SliderInt("Samples ", ref tempInt, 1, 100))
                            {
                                window.SSR.Samples = tempInt;
                            }

                            tempInt = window.SSR.BinarySearchSamples;
                            if (ImGui.SliderInt("BinarySearchSamples", ref tempInt, 0, 40))
                            {
                                window.SSR.BinarySearchSamples = tempInt;
                            }

                            float tempFloat = window.SSR.MaxDist;
                            if (ImGui.SliderFloat("MaxDist", ref tempFloat, 1, 100))
                            {
                                window.SSR.MaxDist = tempFloat;
                            }
                        }
                    }

                    if (ImGui.CollapsingHeader("Shadows"))
                    {
                        ImGui.Checkbox("IsShadows", ref window.IsShadows);
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
                        tempBool = window.PostProcessor.TaaEnabled;
                        if (ImGui.Checkbox("IsTAA", ref tempBool))
                        {
                            window.PostProcessor.TaaEnabled = tempBool;
                        }

                        if (window.PostProcessor.TaaEnabled)
                        {
                            tempBool = window.PostProcessor.IsTaaArtifactMitigation;
                            if (ImGui.Checkbox("IsTaaArtifactMitigation", ref tempBool))
                            {
                                window.PostProcessor.IsTaaArtifactMitigation = tempBool;
                            }
                            ImGui.SameLine();
                            InfoMark(
                                "This is not a feature. It's mostly for fun and you can see the output of a naive TAA resolve pass. " +
                                "In static scenes this always converges to the correct result whereas with artifact mitigation valid samples might be rejected."
                            );

                            int tempInt = window.PostProcessor.TaaSamples;
                            if (ImGui.SliderInt("Samples   ", ref tempInt, 1, GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT))
                            {
                                window.PostProcessor.TaaSamples = tempInt;
                            }
                        }
                    }
                }
                else
                {
                    if (ImGui.CollapsingHeader("PathTracing"))
                    {
                        tempBool = window.PathTracer.IsDebugBVHTraversal;
                        if (ImGui.Checkbox("IsDebugBVHTraversal", ref tempBool))
                        {
                            window.GLSLBasicData.FreezeFrameCounter = 0;
                            window.PathTracer.IsDebugBVHTraversal = tempBool;
                        }

                        float tempFloat = window.PathTracer.RayCoherency;
                        if (ImGui.SliderFloat("RayCoherency", ref tempFloat, 0.0f, 1.0f))
                        {
                            window.PathTracer.RayCoherency = tempFloat;
                        }

                        if (!window.PathTracer.IsDebugBVHTraversal)
                        {
                            int tempInt = window.PathTracer.RayDepth;
                            if (ImGui.SliderInt("MaxRayDepth", ref tempInt, 1, 50))
                            {
                                window.GLSLBasicData.FreezeFrameCounter = 0;
                                window.PathTracer.RayDepth = tempInt;
                            }

                            float floatTemp = window.PathTracer.FocalLength;
                            if (ImGui.InputFloat("FocalLength", ref floatTemp, 0.1f))
                            {
                                window.GLSLBasicData.FreezeFrameCounter = 0;
                                window.PathTracer.FocalLength = MathF.Max(floatTemp, 0);
                            }

                            floatTemp = window.PathTracer.ApertureDiameter;
                            if (ImGui.InputFloat("ApertureDiameter", ref floatTemp, 0.002f))
                            {
                                window.GLSLBasicData.FreezeFrameCounter = 0;
                                window.PathTracer.ApertureDiameter = MathF.Max(floatTemp, 0);
                            }
                        }
                    }
                }

                if (ImGui.CollapsingHeader("Bloom"))
                {
                    ImGui.Checkbox("IsBloom", ref window.IsBloom);
                    if (window.IsBloom)
                    {
                        float tempFloat = window.Bloom.Threshold;
                        if (ImGui.SliderFloat("Threshold", ref tempFloat, 0.0f, 10.0f))
                        {
                            window.Bloom.Threshold = tempFloat;
                        }

                        tempFloat = window.Bloom.Clamp;
                        if (ImGui.SliderFloat("Clamp", ref tempFloat, 0.0f, 100.0f))
                        {
                            window.Bloom.Clamp = tempFloat;
                        }

                        int tempInt = window.Bloom.MinusLods;
                        if (ImGui.SliderInt("MinusLods", ref tempInt, 0, 10))
                        {
                            window.Bloom.MinusLods = tempInt;
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
                        window.GLSLBasicData.FreezeFrameCounter = 0;
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
                    ref GLSLMesh mesh = ref window.ModelSystem.Meshes[selectedEntityIndex];
                    ref readonly GLSLDrawCommand cmd = ref window.ModelSystem.DrawCommands[selectedEntityIndex];

                    ImGui.Text($"MaterialID: {mesh.MaterialIndex}");
                    ImGui.Text($"Triangle Count: {cmd.Count / 3}");
                    ImGui.SameLine(); if (ImGui.Button("Teleport to camera"))
                    {
                        window.ModelSystem.ModelMatrices[selectedEntityIndex][0] = window.ModelSystem.ModelMatrices[selectedEntityIndex][0].ClearTranslation() * Matrix4.CreateTranslation(window.Camera.Position);
                        shouldResetPT = true;
                    }
                    
                    System.Numerics.Vector3 systemVec3 = window.ModelSystem.ModelMatrices[selectedEntityIndex][0].ExtractTranslation().ToSystemVec();
                    if (ImGui.DragFloat3("Position", ref systemVec3, 0.1f))
                    {
                        shouldResetPT = true;
                        window.ModelSystem.ModelMatrices[selectedEntityIndex][0] = window.ModelSystem.ModelMatrices[selectedEntityIndex][0].ClearTranslation() * Matrix4.CreateTranslation(systemVec3.ToOpenTKVec());
                    }

                    systemVec3 = window.ModelSystem.ModelMatrices[selectedEntityIndex][0].ExtractScale().ToSystemVec();
                    if (ImGui.DragFloat3("Scale", ref systemVec3, 0.005f))
                    {
                        shouldResetPT = true;
                        Vector3 temp = Vector3.ComponentMax(systemVec3.ToOpenTKVec(), new Vector3(0.001f));
                        window.ModelSystem.ModelMatrices[selectedEntityIndex][0] = Matrix4.CreateScale(temp) * window.ModelSystem.ModelMatrices[selectedEntityIndex][0].ClearScale();
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
                        window.GLSLBasicData.FreezeFrameCounter = 0;
                        window.ModelSystem.UpdateMeshBuffer(selectedEntityIndex, selectedEntityIndex + 1);
                        window.ModelSystem.UpdateModelMatricesBuffer(selectedEntityIndex, selectedEntityIndex + 1);
                    }
                }
                else if (selectedEntityType == EntityType.Light)
                {
                    bool shouldResetPT = false;
                    ref GLSLLight light = ref window.ForwardRenderer.LightingContext.Lights[selectedEntityIndex];

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
                        window.GLSLBasicData.FreezeFrameCounter = 0;
                        window.ForwardRenderer.LightingContext.UpdateLightBuffer((int)selectedEntityIndex, (int)selectedEntityIndex + 1);
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

        public void Update(Application window)
        {
            if (window.RenderGui && isHoveredViewport && window.MouseState[MouseButton.Left] == InputState.Touched)
            {
                Vector2i point = new Vector2i((int)window.MouseState.Position.X, (int)window.MouseState.Position.Y);
                if (window.RenderGui)
                {
                    point -= (Vector2i)viewportHeaderSize.ToOpenTKVec();
                }
                point.Y = window.ForwardRenderer.Result.Height - point.Y;

                Vector2 ndc = new Vector2((float)point.X / window.ForwardRenderer.Result.Width, (float)point.Y / window.ForwardRenderer.Result.Height) * 2.0f - new Vector2(1.0f);
                Ray worldSpaceRay = Ray.GetWorldSpaceRay(window.GLSLBasicData.CameraPos, window.GLSLBasicData.InvProjection, window.GLSLBasicData.InvView, ndc);
                bool hitMesh = window.BVH.Intersect(worldSpaceRay, out BVH.RayHitInfo meshHitInfo);
                bool hitLight = window.ForwardRenderer.LightingContext.Intersect(worldSpaceRay, out Lighter.HitInfo lightHitInfo);
                if (window.IsPathTracing) hitLight = false;

                if (!hitMesh && !hitLight)
                {
                    window.ForwardRenderer.RenderMeshAABBIndex = -1;
                    selectedEntityType = EntityType.None;
                    return;
                }

                if (!hitLight) lightHitInfo.T = float.MaxValue;
                if (!hitMesh) meshHitInfo.T = float.MaxValue;

                if (meshHitInfo.T < lightHitInfo.T)
                {
                    selectedEntityType = EntityType.Mesh;
                    selectedEntityDist = meshHitInfo.T;
                    if (meshHitInfo.MeshIndex == window.ForwardRenderer.RenderMeshAABBIndex)
                    {
                        window.ForwardRenderer.RenderMeshAABBIndex = -1;
                        selectedEntityType = EntityType.None;
                    }
                    else
                    {
                        window.ForwardRenderer.RenderMeshAABBIndex = meshHitInfo.MeshIndex;
                    }
                    selectedEntityIndex = meshHitInfo.MeshIndex;
                }
                else
                {
                    selectedEntityType = EntityType.Light;
                    selectedEntityDist = lightHitInfo.T;
                    selectedEntityIndex = lightHitInfo.HitID;
                    window.ForwardRenderer.RenderMeshAABBIndex = -1;
                }
            }
        }
    }
}
