using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace IDKEngine
{
    public static class ObjectFactory
    {
        public struct Vertex
        {
            public Vector3 Position;
            public Vector2 TexCoord;
            public Vector3 Normal;
        }

        public static Span<Vertex> GenerateSmoothSphere(float radius, int latitudes, int longitudes)
        {
            // Source: https://gist.github.com/Pikachuxxxx/5c4c490a7d7679824e0e18af42918efc

            if (longitudes < 3)
                longitudes = 3;
            if (latitudes < 2)
                latitudes = 2;

            List<Vertex> vertecis = new List<Vertex>((latitudes + 1) * (longitudes + 1));

            float lengthInv = 1.0f / radius;
            float deltaLatitude = MathF.PI / latitudes;
            float deltaLongitude = 2 * MathF.PI / longitudes;
            float latitudeAngle;
            float longitudeAngle;

            Vertex vertex;

            for (int i = 0; i <= latitudes; i++)
            {
                latitudeAngle = MathF.PI / 2 - i * deltaLatitude; /* Starting -pi/2 to pi/2 */
                float xy = radius * MathF.Cos(latitudeAngle);    /* r * cos(phi) */
                float z = radius * MathF.Sin(latitudeAngle);     /* r * sin(phi )*/

                for (int j = 0; j <= longitudes; j++)
                {
                    longitudeAngle = j * deltaLongitude;

                    vertex.Position.X = xy * MathF.Cos(longitudeAngle);       /* x = r * cos(phi) * cos(theta)  */
                    vertex.Position.Y = xy * MathF.Sin(longitudeAngle);       /* y = r * cos(phi) * sin(theta)  */
                    vertex.Position.Z = z;                                    /* z = r * sin(phi) */

                    vertex.TexCoord.X = (float)j / longitudes;
                    vertex.TexCoord.Y = (float)i / latitudes;

                    vertex.Normal = vertex.Position * lengthInv;

                    vertecis.Add(vertex);
                }
            }

            return CollectionsMarshal.AsSpan(vertecis);
        }

        public static Span<uint> GenerateSmoothSphereIndicis(uint latitudes, uint longitudes)
        {
            // Source: https://gist.github.com/Pikachuxxxx/5c4c490a7d7679824e0e18af42918efc
            
            List<uint> indicis = new List<uint>((int)(latitudes * longitudes));
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
                        indicis.Add(k1);
                        indicis.Add(k2);
                        indicis.Add(k1 + 1);
                    }

                    if (i != (latitudes - 1))
                    {
                        indicis.Add(k1 + 1);
                        indicis.Add(k2);
                        indicis.Add(k2 + 1);
                    }
                }
            }

            return CollectionsMarshal.AsSpan(indicis);
        }

        public static Span<Vector3> GeneratePlane(float width, float depth, int subdivideX, int subdivideZ)
        {
            List<Vector3> positions = new List<Vector3>();
            float stepX = width / subdivideX;
            float stepZ = depth / subdivideZ;
            for (float x = -width / 2.0f; x < width / 2.0f; x += stepX)
            {
                for (float z = -depth / 2.0f; z < depth / 2.0f; z += stepZ)
                {
                    positions.AddRange(new Vector3[]
                    {
                        new Vector3( x + stepX / 2.0f, 0.0f,  z +  stepZ / 2.0f),
                        new Vector3( x + stepX / 2.0f, 0.0f,  z + -stepZ / 2.0f),
                        new Vector3( x + -stepX / 2.0f, 0.0f, z +  stepZ / 2.0f),

                        new Vector3( x + stepX / 2.0f, 0.0f,  z + -stepZ / 2.0f),
                        new Vector3( x + -stepX / 2.0f, 0.0f, z + -stepZ / 2.0f),
                        new Vector3( x + -stepX / 2.0f, 0.0f, z +  stepZ / 2.0f),
                    });
                }
            }
            return CollectionsMarshal.AsSpan(positions);
        }
    }
}
