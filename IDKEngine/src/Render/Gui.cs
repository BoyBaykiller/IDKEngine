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
        public System.Numerics.Vector2 viewportPos;
        public void Draw(Application window, float frameTime)
        {
            ImGuiBackend.Update(window, frameTime);
            ImGui.DockSpaceOverViewport();

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0.0f));
            ImGui.Begin($"Viewport");
            
            System.Numerics.Vector2 content = ImGui.GetContentRegionAvail();
            
            System.Numerics.Vector2 tileBar = ImGui.GetCursorPos();
            viewportPos = ImGui.GetWindowPos() + tileBar;
            
            ImGui.Image((IntPtr)window.PostCombine.Result.ID, content, new System.Numerics.Vector2(0.0f, 1.0f), new System.Numerics.Vector2(1.0f, 0.0f));
            isHoveredViewport = ImGui.IsItemHovered();

            if (content.X != window.ViewportSize.X || content.Y != window.ViewportSize.Y)
            {
                window.SetViewportSize((int)content.X, (int)content.Y);
            }

            ImGui.End();
            ImGui.PopStyleVar();

            ImGui.Begin("Renderer");
            {
                ImGui.Text($"FPS: {window.FPS}");
                ImGui.Text($"Viewport size: {window.ViewportSize.X}x{window.ViewportSize.Y}");
                if (window.IsPathTracing)
                    ImGui.Text($"Samples taken: {window.GLSLBasicData.FreezeFramesCounter}");

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

                bool tempBool = window.PostCombine.IsDithering;
                if (ImGui.Checkbox("IsDithering", ref tempBool))
                {
                    window.PostCombine.IsDithering = tempBool;
                }

                if (!window.IsPathTracing)
                {
                    ImGui.Checkbox("DepthPrePass", ref window.ForwardRenderer.IsDepthPrePass);

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
                            "This feature when enabled allows the engine to choose a unique shading rate " +
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

                            System.Numerics.Vector3 tempVec = OpenTKToSystem(window.VolumetricLight.Absorbance);
                            if (ImGui.SliderFloat3("Absorbance", ref tempVec, 0.0f, 0.2f))
                            {
                                window.VolumetricLight.Absorbance = SystemToOpenTK(tempVec);
                            }
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
                            if (ImGui.SliderFloat("Radius", ref tempFloat, 0.0f, 2.0f))
                            {
                                window.SSAO.Radius = tempFloat;
                            }

                            tempFloat = window.SSAO.Strength;
                            if (ImGui.SliderFloat("Strength", ref tempFloat, 0.0f, 20.0f))
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
                        if (PointShadow.IS_VERTEX_LAYERED_RENDERING)
                        {
                            InfoMark(
                                "This system supports vertex layered rendering. " +
                                "Each pointshadow will be generated in only 1 draw call instead of 6."
                            );
                        }
                        else
                        {
                            InfoMark(
                                "This system does not support vertex layered rendering. " +
                                "Each pointshadow will be generated in 6 draw calls instead of 1."
                            );
                        }
                    }

                    if (ImGui.CollapsingHeader("TAA"))
                    {
                        tempBool = window.ForwardRenderer.TaaEnabled;
                        if (ImGui.Checkbox("IsTAA", ref tempBool))
                        {
                            window.ForwardRenderer.TaaEnabled = tempBool;
                        }

                        if (window.ForwardRenderer.TaaEnabled)
                        {
                            int tempInt = window.ForwardRenderer.TaaSamples;
                            if (ImGui.SliderInt("Samples   ", ref tempInt, 1, GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT))
                            {
                                window.ForwardRenderer.TaaSamples = tempInt;
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
                            window.GLSLBasicData.FreezeFramesCounter = 0;
                            window.PathTracer.IsDebugBVHTraversal = tempBool;
                        }
                        if (!window.PathTracer.IsDebugBVHTraversal)
                        {
                            int tempInt = window.PathTracer.RayDepth;
                            if (ImGui.SliderInt("MaxRayDepth", ref tempInt, 1, 50))
                            {
                                window.GLSLBasicData.FreezeFramesCounter = 0;
                                window.PathTracer.RayDepth = tempInt;
                            }

                            float floatTemp = window.PathTracer.FocalLength;
                            if (ImGui.InputFloat("FocalLength", ref floatTemp, 0.1f))
                            {
                                window.GLSLBasicData.FreezeFramesCounter = 0;
                                window.PathTracer.FocalLength = MathF.Max(floatTemp, 0);
                            }

                            floatTemp = window.PathTracer.ApertureDiameter;
                            if (ImGui.InputFloat("ApertureDiameter", ref floatTemp, 0.002f))
                            {
                                window.GLSLBasicData.FreezeFramesCounter = 0;
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

                if (ImGui.CollapsingHeader("EnvironmentMap"))
                {
                    string[] resolutions = new string[] { "2048", "1024", "512", "256", "128", "64", "32" };
                    current = window.AtmosphericScatterer.Result.Width.ToString();
                    if (ImGui.BeginCombo("Resolution", current))
                    {
                        for (int i = 0; i < resolutions.Length; i++)
                        {
                            bool isSelected = current == resolutions[i];
                            if (ImGui.Selectable(resolutions[i], isSelected))
                            {
                                current = resolutions[i];
                                window.AtmosphericScatterer.SetSize(Convert.ToInt32(current));
                                window.AtmosphericScatterer.Compute();
                                window.GLSLBasicData.FreezeFramesCounter = 0;
                            }

                            if (isSelected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }

                    int tempInt = window.AtmosphericScatterer.ISteps;
                    if (ImGui.SliderInt("InScatteringSamples", ref tempInt, 1, 100))
                    {
                        window.AtmosphericScatterer.ISteps = tempInt;
                        window.AtmosphericScatterer.Compute();
                        window.GLSLBasicData.FreezeFramesCounter = 0;
                    }

                    tempInt = window.AtmosphericScatterer.JSteps;
                    if (ImGui.SliderInt("DensitySamples", ref tempInt, 1, 40))
                    {
                        window.AtmosphericScatterer.JSteps = tempInt;
                        window.AtmosphericScatterer.Compute();
                        window.GLSLBasicData.FreezeFramesCounter = 0;
                    }

                    float tempFloat = window.AtmosphericScatterer.Time;
                    if (ImGui.DragFloat("Time", ref tempFloat, 0.005f))
                    {
                        window.AtmosphericScatterer.Time = tempFloat;
                        window.AtmosphericScatterer.Compute();
                        window.GLSLBasicData.FreezeFramesCounter = 0;
                    }

                    tempFloat = window.AtmosphericScatterer.LightIntensity;
                    if (ImGui.DragFloat("Intensity", ref tempFloat, 0.2f))
                    {
                        window.AtmosphericScatterer.LightIntensity = tempFloat;
                        window.AtmosphericScatterer.Compute();
                        window.GLSLBasicData.FreezeFramesCounter = 0;
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
                    bool hadChange = false;
                    ref GLSLMesh mesh = ref window.ModelSystem.Meshes[selectedEntityIndex];
                    GLSLDrawCommand cmd = window.ModelSystem.DrawCommands[selectedEntityIndex];

                    ImGui.Text($"MaterialID: {mesh.MaterialIndex}");
                    ImGui.Text($"Triangle Count: {cmd.Count / 3}");
                    
                    System.Numerics.Vector3 systemVec3 = OpenTKToSystem(window.ModelSystem.ModelMatrices[selectedEntityIndex][0].ExtractTranslation());
                    if (ImGui.DragFloat3("Position", ref systemVec3, 0.1f))
                    {
                        hadChange = true;
                        window.ModelSystem.ModelMatrices[selectedEntityIndex][0] = window.ModelSystem.ModelMatrices[selectedEntityIndex][0].ClearTranslation() * Matrix4.CreateTranslation(SystemToOpenTK(systemVec3));
                    }

                    if (ImGui.SliderFloat("NormalMapStrength", ref mesh.NormalMapStrength, 0.0f, 4.0f))
                    {
                        hadChange = true;
                    }

                    if (ImGui.SliderFloat("EmissiveBias", ref mesh.EmissiveBias, 0.0f, 20.0f))
                    {
                        hadChange = true;
                    }

                    if (ImGui.SliderFloat("SpecularBias", ref mesh.SpecularBias, -1.0f, 1.0f))
                    {
                        hadChange = true;
                    }

                    if (ImGui.SliderFloat("RoughnessBias", ref mesh.RoughnessBias, -1.0f, 1.0f))
                    {
                        hadChange = true;
                    }

                    if (ImGui.SliderFloat("RefractionChance", ref mesh.RefractionChance, 0.0f, 1.0f))
                    {
                        hadChange = true;
                    }

                    if (ImGui.SliderFloat("IOR", ref mesh.IOR, 1.0f, 5.0f))
                    {
                        hadChange = true;
                    }

                    systemVec3 = OpenTKToSystem(mesh.Absorbance);
                    if (ImGui.SliderFloat3("Absorbance", ref systemVec3, 0.0f, 1.0f))
                    {
                        mesh.Absorbance = SystemToOpenTK(systemVec3);
                        hadChange = true;
                    }

                    if (hadChange)
                    {
                        window.GLSLBasicData.FreezeFramesCounter = 0;
                        window.ModelSystem.UpdateMeshBuffer((int)selectedEntityIndex, (int)selectedEntityIndex + 1);
                        window.ModelSystem.UpdateModelMatricesBuffer((int)selectedEntityIndex, (int)selectedEntityIndex + 1);
                    }
                }
                else if (selectedEntityType == EntityType.Light)
                {
                    bool hadChange = false;
                    ref GLSLLight light = ref window.ForwardRenderer.LightingContext.Lights[selectedEntityIndex];

                    System.Numerics.Vector3 systemVec3 = OpenTKToSystem(light.Position);
                    if (ImGui.DragFloat3("Position", ref systemVec3, 0.1f))
                    {
                        hadChange = true;
                        light.Position = SystemToOpenTK(systemVec3);
                    }

                    systemVec3 = OpenTKToSystem(light.Color);
                    if (ImGui.DragFloat3("Color", ref systemVec3, 0.1f, 0.0f))
                    {
                        hadChange = true;
                        light.Color = SystemToOpenTK(systemVec3);
                    }

                    if (ImGui.DragFloat("Radius", ref light.Radius, 0.1f, 0.0f))
                    {
                        hadChange = true;
                    }

                    if (hadChange)
                    {
                        window.GLSLBasicData.FreezeFramesCounter = 0;
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
            if (isHoveredViewport && window.MouseState[MouseButton.Left] == InputState.Touched)
            {
                Vector2i point = new Vector2i((int)window.MouseState.Position.X, (int)window.MouseState.Position.Y);
                point -= SystemToOpenTK(viewportPos);
                point.Y = window.ViewportSize.Y - point.Y;

                Vector2 ndc = new Vector2((float)point.X / window.ForwardRenderer.Result.Width, (float)point.Y / window.ForwardRenderer.Result.Height) * 2.0f - new Vector2(1.0f);
                Ray worldSpaceRay = Ray.GetWorldSpaceRay(window.GLSLBasicData.CameraPos, window.GLSLBasicData.InvProjection, window.GLSLBasicData.InvView, ndc);
                bool hitMesh = window.BVH.Intersect(worldSpaceRay, out BVH.HitInfo meshHitInfo);
                bool hitLight = window.ForwardRenderer.LightingContext.Intersect(worldSpaceRay, out Lighter.HitInfo lightHitInfo);

                if (!hitMesh && !hitLight)
                {
                    window.ForwardRenderer.RenderMeshAABBIndex = -1;
                    selectedEntityType = EntityType.None;
                    return;
                }

                if (meshHitInfo.T < lightHitInfo.T)
                {
                    selectedEntityType = EntityType.Mesh;
                    selectedEntityDist = meshHitInfo.T;
                    if (meshHitInfo.HitID == window.ForwardRenderer.RenderMeshAABBIndex)
                    {
                        window.ForwardRenderer.RenderMeshAABBIndex = -1;
                        selectedEntityType = EntityType.None;
                    }
                    else
                    {
                        window.ForwardRenderer.RenderMeshAABBIndex = meshHitInfo.HitID;
                    }
                    selectedEntityIndex = meshHitInfo.HitID;
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

        private static System.Numerics.Vector3 OpenTKToSystem(Vector3 vector3)
        {
            return new System.Numerics.Vector3(vector3.X, vector3.Y, vector3.Z);
        }

        private static Vector3 SystemToOpenTK(System.Numerics.Vector3 vector3)
        {
            return new Vector3(vector3.X, vector3.Y, vector3.Z);
        }

        private static Vector2i SystemToOpenTK(System.Numerics.Vector2 vector2)
        {
            return new Vector2i((int)vector2.X, (int)vector2.Y);
        }
    }
}
