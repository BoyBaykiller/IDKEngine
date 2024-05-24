using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace IDKEngine.Utils
{
    public static class ObjectFactory
    {
        public static class Sphere
        {
            public struct Vertex
            {
                public Vector3 Position;
                public Vector2 TexCoord;
            }

            public static Vertex[] GenerateVertices(float radius, int latitudes, int longitudes)
            {
                // Source: https://gist.github.com/Pikachuxxxx/5c4c490a7d7679824e0e18af42918efc

                longitudes = Math.Max(longitudes, 3);
                latitudes = Math.Max(latitudes, 2);

                List<Vertex> vertices = new List<Vertex>((latitudes + 1) * (longitudes + 1));

                float deltaLatitude = MathF.PI / latitudes;
                float deltaLongitude = 2 * MathF.PI / longitudes;
                float latitudeAngle;
                float longitudeAngle;

                for (int i = 0; i <= latitudes; i++)
                {
                    latitudeAngle = MathF.PI / 2 - i * deltaLatitude;
                    float xy = radius * MathF.Cos(latitudeAngle);
                    float z = radius * MathF.Sin(latitudeAngle);

                    for (int j = 0; j <= longitudes; j++)
                    {
                        longitudeAngle = j * deltaLongitude;

                        Vertex vertex;
                        vertex.Position.X = xy * MathF.Cos(longitudeAngle);
                        vertex.Position.Y = xy * MathF.Sin(longitudeAngle);
                        vertex.Position.Z = z;

                        vertex.TexCoord.X = (float)j / longitudes;
                        vertex.TexCoord.Y = (float)i / latitudes;

                        vertices.Add(vertex);
                    }
                }

                return vertices.ToArray();
            }

            public static uint[] GenerateIndices(uint latitudes, uint longitudes)
            {
                // Source: https://gist.github.com/Pikachuxxxx/5c4c490a7d7679824e0e18af42918efc

                List<uint> indices = new List<uint>((int)(latitudes * longitudes));
                uint k1, k2;
                for (uint i = 0; i < latitudes; i++)
                {
                    k1 = i * (longitudes + 1);
                    k2 = k1 + longitudes + 1;
                    // 2 Triangles per latitude block excluding the first and last longitudes blocks
                    for (int j = 0; j < longitudes; j++, k1++, k2++)
                    {
                        if (i != 0)
                        {
                            indices.Add(k1);
                            indices.Add(k2);
                            indices.Add(k1 + 1);
                        }

                        if (i != latitudes - 1)
                        {
                            indices.Add(k1 + 1);
                            indices.Add(k2);
                            indices.Add(k2 + 1);
                        }
                    }
                }

                return indices.ToArray();
            }
        }

        public static class Plane
        {
            public struct Vertex
            {
                public Vector3 Position;
            }

            public static Vertex[] GeneratePlane(float width, float depth, int subdivisonX, int subdivisionZ)
            {
                List<Vertex> positions = new List<Vertex>();
                float stepX = width / subdivisonX;
                float stepZ = depth / subdivisionZ;
                
                for (float x = -width / 2.0f; x < width / 2.0f; x += stepX)
                {
                    for (float z = -depth / 2.0f; z < depth / 2.0f; z += stepZ)
                    {
                        positions.AddRange([
                            new Vertex() { Position = new Vector3(x + stepX / 2.0f, 0.0f,  z + stepZ / 2.0f) },
                            new Vertex() { Position = new Vector3(x + stepX / 2.0f, 0.0f,  z - stepZ / 2.0f) },
                            new Vertex() { Position = new Vector3(x - stepX / 2.0f, 0.0f,  z + stepZ / 2.0f) },

                            new Vertex() { Position = new Vector3(x + stepX / 2.0f, 0.0f, z - stepZ / 2.0f) },
                            new Vertex() { Position = new Vector3(x - stepX / 2.0f, 0.0f, z - stepZ / 2.0f) },
                            new Vertex() { Position = new Vector3(x - stepX / 2.0f, 0.0f, z + stepZ / 2.0f) },
                        ]);
                    }
                }

                return positions.ToArray();
            }
        }
    }
}
