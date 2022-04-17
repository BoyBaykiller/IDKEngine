using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.GUI;
using ImGuiNET;

namespace IDKEngine.Render
{
    class Gui
    {
        private int selectedMeshIndex = -1;
        public ImGuiController ImGuiController;
        public Gui(int width, int height)
        {
            ImGuiController = new ImGuiController(width, height);
        }

        public void Render(Application window, float frameTime)
        {
            ImGuiController.Update(window, frameTime);
            ImGui.Begin("Render");
            {
                ImGui.Text($"FPS: {window.FPS}");

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
                            window.GLSLBasicData.FrameCount = 0;
                            if (current == "PathTracer")
                            {
                                window.PathTracer.SetSize(window.Size.X, window.Size.Y);
                            }
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                if (ImGui.CollapsingHeader("Bloom"))
                {
                    ImGui.Checkbox("IsBloom", ref window.IsBloom);
                    if (window.IsBloom)
                    {
                        float temp = window.Bloom.Threshold;
                        if (ImGui.SliderFloat("Threshold", ref temp, 0.0f, 10.0f))
                        {
                            window.Bloom.Threshold = temp;
                        }

                        temp = window.Bloom.Clamp;
                        if (ImGui.SliderFloat("Clamp", ref temp, 0.0f, 50.0f))
                        {
                            window.Bloom.Clamp = temp;
                        }
                    }
                }

                if (!window.IsPathTracing)
                {
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
                            if (ImGui.SliderFloat("Strength", ref tempFloat, 0.0f, 50.0f))
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
                    }

                    if (ImGui.CollapsingHeader("TAA"))
                    {
                        bool tempBool = window.ForwardRenderer.TaaEnabled;
                        if (ImGui.Checkbox("IsTAA", ref tempBool))
                        {
                            window.ForwardRenderer.TaaEnabled = tempBool;
                        }

                        if (window.ForwardRenderer.TaaEnabled)
                        {
                            int tempInt = window.ForwardRenderer.TaaSamples;
                            if (ImGui.SliderInt("Samples", ref tempInt, 1, GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT))
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
                        int tempInt = window.PathTracer.RayDepth;
                        if (ImGui.SliderInt("MaxRayDepth", ref tempInt, 1, 50))
                        {
                            window.GLSLBasicData.FrameCount = 0;
                            window.PathTracer.RayDepth = tempInt;
                        }

                        float floatTemp = window.PathTracer.FocalLength;
                        if (ImGui.InputFloat("FocalLength", ref floatTemp, 0.1f))
                        {
                            window.GLSLBasicData.FrameCount = 0;
                            window.PathTracer.FocalLength = MathF.Max(floatTemp, 0);
                        }

                        floatTemp = window.PathTracer.ApertureDiameter;
                        if (ImGui.InputFloat("ApertureDiameter", ref floatTemp, 0.002f))
                        {
                            window.GLSLBasicData.FrameCount = 0;
                            window.PathTracer.ApertureDiameter = MathF.Max(floatTemp, 0);
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
                                window.GLSLBasicData.FrameCount = 0;
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
                        window.GLSLBasicData.FrameCount = 0;
                    }

                    tempInt = window.AtmosphericScatterer.JSteps;
                    if (ImGui.SliderInt("DensitySamples", ref tempInt, 1, 40))
                    {
                        window.AtmosphericScatterer.JSteps = tempInt;
                        window.AtmosphericScatterer.Compute();
                        window.GLSLBasicData.FrameCount = 0;
                    }

                    float tempFloat = window.AtmosphericScatterer.Time;
                    if (ImGui.DragFloat("Time", ref tempFloat, 0.005f))
                    {
                        window.AtmosphericScatterer.Time = tempFloat;
                        window.AtmosphericScatterer.Compute();
                        window.GLSLBasicData.FrameCount = 0;
                    }

                    tempFloat = window.AtmosphericScatterer.LightIntensity;
                    if (ImGui.DragFloat("Intensity", ref tempFloat, 0.2f))
                    {
                        window.AtmosphericScatterer.LightIntensity = tempFloat;
                        window.AtmosphericScatterer.Compute();
                        window.GLSLBasicData.FrameCount = 0;
                    }
                }

                ImGui.End();
            }

            if (selectedMeshIndex != Forward.MESH_INDEX_CLEAR_COLOR/* && !window.IsPathTracing*/)
            {
                System.Numerics.Vector3 systemVec3;
                ImGui.Begin("GameObjectProperties", ImGuiWindowFlags.AlwaysAutoResize);
                {
                    bool hadChange = false;
                    GLSLMesh mesh = window.ModelSystem.Meshes[selectedMeshIndex];

                    ImGui.Text($"MeshID: {selectedMeshIndex}");
                    ImGui.Text($"MaterialID: {mesh.MaterialIndex}");

                    systemVec3 = OpenTKToSystem(mesh.Model.ExtractTranslation());
                    if (ImGui.DragFloat3("Position", ref systemVec3, 0.1f))
                    {
                        hadChange = true;
                        mesh.Model = mesh.Model.ClearTranslation() * Matrix4.CreateTranslation(SystemToOpenTK(systemVec3));
                    }

                    if (ImGui.SliderFloat("Emissive", ref mesh.Emissive, 0.0f, 100.0f))
                    {
                        hadChange = true;
                    }

                    if (ImGui.SliderFloat("NormalMapStrength", ref mesh.NormalMapStrength, 0.0f, 4.0f))
                    {
                        hadChange = true;
                    }

                    if (ImGui.SliderFloat("SpecularChance", ref mesh.SpecularChance, 0.0f, 1.0f))
                    {
                        hadChange = true;
                    }

                    if (ImGui.SliderFloat("Roughness", ref mesh.Roughness, 0.0f, 1.0f))
                    {
                        hadChange = true;
                    }

                    if (hadChange)
                    {
                        window.GLSLBasicData.FrameCount = 0;
                        window.ModelSystem.ForEach(selectedMeshIndex, selectedMeshIndex + 1, (ref GLSLMesh curMesh) =>
                        {
                            curMesh = mesh;
                        });
                    }
                    ImGui.End();
                }
            }

            ImGuiController.Render();
        }

        public void Update(Application window)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            if (/*!window.IsPathTracing && */window.MouseState.CursorMode == CursorModeValue.CursorNormal && window.MouseState[MouseButton.Left] == InputState.Touched && !io.WantCaptureKeyboard && !io.WantCaptureMouse)
            {
                Vector2i point = new Vector2i((int)window.MouseState.Position.X, (int)window.MouseState.Position.Y);
                point.Y = window.Size.Y - point.Y;
                window.ForwardRenderer.Framebuffer.GetPixels(point.X, point.Y, 1, 1, PixelFormat.RedInteger, PixelType.Int, ref selectedMeshIndex);
                if (window.ForwardRenderer.RenderMeshAABBIndex == selectedMeshIndex)
                    selectedMeshIndex = -1;

                window.ForwardRenderer.RenderMeshAABBIndex = selectedMeshIndex;
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
    }
}
