using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace IDKEngine
{
    static class ObjectFactory
    {
        public struct Vertex
        {
            public Vector3 Position;
            public Vector2 TexCoord;
            public Vector3 Normal;
        }

        /// <summary>
        /// Source: <see href="https://gist.github.com/Pikachuxxxx/5c4c490a7d7679824e0e18af42918efc">https://gist.github.com/Pikachuxxxx/5c4c490a7d7679824e0e18af42918efc</see>
        /// </summary>
        /// <param name="radius"></param>
        /// <param name="latitudes"></param>
        /// <param name="longitudes"></param>
        public static Vertex[] GenerateSmoothSphere(float radius, int latitudes, int longitudes)
        {
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

            return vertecis.ToArray();
        }

        /// <summary>
        /// Source: <see href="https://gist.github.com/Pikachuxxxx/5c4c490a7d7679824e0e18af42918efc">https://gist.github.com/Pikachuxxxx/5c4c490a7d7679824e0e18af42918efc</see>
        /// </summary>
        /// <param name="latitudes"></param>
        /// <param name="longitudes"></param>
        /// <returns></returns>
        public static uint[] GenerateSmoothSphereIndicis(uint latitudes, uint longitudes)
        {
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

            return indicis.ToArray();
        }
    }
}
