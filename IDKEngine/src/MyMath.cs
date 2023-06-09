using System;
using OpenTK.Mathematics;

namespace IDKEngine
{
    public static class MyMath
    {
        // Source: https://github.com/leesg213/TemporalAA/blob/main/Renderer/AAPLRenderer.mm#L152
        public static void GetHaltonSequence_2_3(Span<Vector2> buffer)
        {
            int n2 = 0, d2 = 1, n3 = 0, d3 = 1;
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i].X = GetHalton(2, ref n2, ref d2);
                buffer[i].Y = GetHalton(3, ref n3, ref d3);
            }
        }

        // Source: https://github.com/leesg213/TemporalAA/blob/main/Renderer/AAPLRenderer.mm#L124
        public static float GetHalton(int baseHalton, ref int n, ref int d)
        {
            int x = d - n;
            if (x == 1)
            {
                n = 1;
                d *= baseHalton;
            }
            else
            {
                int y = d / baseHalton;
                while (x <= y)
                {
                    y /= baseHalton;
                }

                n = (baseHalton + 1) * y - x;
            }

            float result = n / (float)d;
            return result;
        }

        public static void MapHaltonSequence(Span<Vector2> halton, float width, float height)
        {
            for (int i = 0; i < halton.Length; i++)
            {
                halton[i].X = (halton[i].X * 2.0f - 1.0f) / width;
                halton[i].Y = (halton[i].Y * 2.0f - 1.0f) / height;
            }
        }

        public static void BitsInsert(ref uint mem, uint data, int offset)
        {
            mem |= data << offset;
        }

        public static uint GetBits(uint data, int offset, int bits)
        {
            uint mask = (1u << bits) - 1u;
            return (data >> offset) & mask;
        }

        public static float HalfArea(Vector3 size)
        {
            return size.X * size.Y + size.X * size.Z + size.Z * size.Y;
        }

        public static bool AabbAabbIntersect(in AABB first, in Vector3 min, Vector3 max)
        {
            return  first.Min.X < max.X &&
                    first.Min.Y < max.Y &&
                    first.Min.Z < max.Z &&

                    first.Max.X > min.X &&
                    first.Max.Y > min.Y &&
                    first.Max.Z > min.Z;
        }

        // Source: "Real-Time Collision Detection" by Christer Ericson, page 169
        // See also the published Errata at http://realtimecollisiondetection.net/books/rtcd/errata/
        public static bool TriangleBoxIntersect(in Vector3 a, in Vector3 b, in Vector3 c, in Vector3 boxCenter, in Vector3 halfSize)
        {
            // Translate triangle as conceptually moving AABB to origin
            var v0 = (a - boxCenter);
            var v1 = (b - boxCenter);
            var v2 = (c - boxCenter);

            // Compute edge vectors for triangle
            var f0 = (v1 - v0);
            var f1 = (v2 - v1);
            var f2 = (v0 - v2);

            #region Test axes a00..a22 (category 3)

            // Test axis a00
            var a00 = new Vector3(0, -f0.Z, f0.Y);
            var p0 = Vector3.Dot(v0, a00);
            var p1 = Vector3.Dot(v1, a00);
            var p2 = Vector3.Dot(v2, a00);
            var r = halfSize.Y * Math.Abs(f0.Z) + halfSize.Z * Math.Abs(f0.Y);
            if (Math.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a01
            var a01 = new Vector3(0, -f1.Z, f1.Y);
            p0 = Vector3.Dot(v0, a01);
            p1 = Vector3.Dot(v1, a01);
            p2 = Vector3.Dot(v2, a01);
            r = halfSize.Y * Math.Abs(f1.Z) + halfSize.Z * Math.Abs(f1.Y);
            if (Math.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a02
            var a02 = new Vector3(0, -f2.Z, f2.Y);
            p0 = Vector3.Dot(v0, a02);
            p1 = Vector3.Dot(v1, a02);
            p2 = Vector3.Dot(v2, a02);
            r = halfSize.Y * Math.Abs(f2.Z) + halfSize.Z * Math.Abs(f2.Y);
            if (Math.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a10
            var a10 = new Vector3(f0.Z, 0, -f0.X);
            p0 = Vector3.Dot(v0, a10);
            p1 = Vector3.Dot(v1, a10);
            p2 = Vector3.Dot(v2, a10);
            r = halfSize.X * Math.Abs(f0.Z) + halfSize.Z * Math.Abs(f0.X);
            if (Math.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a11
            var a11 = new Vector3(f1.Z, 0, -f1.X);
            p0 = Vector3.Dot(v0, a11);
            p1 = Vector3.Dot(v1, a11);
            p2 = Vector3.Dot(v2, a11);
            r = halfSize.X * Math.Abs(f1.Z) + halfSize.Z * Math.Abs(f1.X);
            if (Math.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a12
            var a12 = new Vector3(f2.Z, 0, -f2.X);
            p0 = Vector3.Dot(v0, a12);
            p1 = Vector3.Dot(v1, a12);
            p2 = Vector3.Dot(v2, a12);
            r = halfSize.X * Math.Abs(f2.Z) + halfSize.Z * Math.Abs(f2.X);
            if (Math.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a20
            var a20 = new Vector3(-f0.Y, f0.X, 0);
            p0 = Vector3.Dot(v0, a20);
            p1 = Vector3.Dot(v1, a20);
            p2 = Vector3.Dot(v2, a20);
            r = halfSize.X * Math.Abs(f0.Y) + halfSize.Y * Math.Abs(f0.X);
            if (Math.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a21
            var a21 = new Vector3(-f1.Y, f1.X, 0);
            p0 = Vector3.Dot(v0, a21);
            p1 = Vector3.Dot(v1, a21);
            p2 = Vector3.Dot(v2, a21);
            r = halfSize.X * Math.Abs(f1.Y) + halfSize.Y * Math.Abs(f1.X);
            if (Math.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a22
            var a22 = new Vector3(-f2.Y, f2.X, 0);
            p0 = Vector3.Dot(v0, a22);
            p1 = Vector3.Dot(v1, a22);
            p2 = Vector3.Dot(v2, a22);
            r = halfSize.X * Math.Abs(f2.Y) + halfSize.Y * Math.Abs(f2.X);
            if (Math.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            #endregion

            #region Test the three axes corresponding to the face normals of AABB b (category 1)

            // Exit if...
            // ... [-extents.x, extents.x] and [min(v0.x,v1.x,v2.x), max(v0.x,v1.x,v2.x)] do not overlap
            if (Max3(v0.X, v1.X, v2.X) < -halfSize.X || Min3(v0.X, v1.X, v2.X) > halfSize.X)
            {
                return false;
            }

            // ... [-extents.y, extents.y] and [min(v0.y,v1.y,v2.y), max(v0.y,v1.y,v2.y)] do not overlap
            if (Max3(v0.Y, v1.Y, v2.Y) < -halfSize.Y || Min3(v0.Y, v1.Y, v2.Y) > halfSize.Y)
            {
                return false;
            }

            // ... [-extents.z, extents.z] and [min(v0.z,v1.z,v2.z), max(v0.z,v1.z,v2.z)] do not overlap
            if (Max3(v0.Z, v1.Z, v2.Z) < -halfSize.Z || Min3(v0.Z, v1.Z, v2.Z) > halfSize.Z)
            {
                return false;
            }

            #endregion

            #region Test separating axis corresponding to triangle face normal (category 2)

            var planeNormal = Vector3.Cross(f0, f1);
            var planeDistance = Vector3.Dot(planeNormal, v0);

            // Compute the projection interval radius of b onto L(t) = b.c + t * p.n
            r = halfSize.X * Math.Abs(planeNormal.X) + halfSize.Y * Math.Abs(planeNormal.Y) + halfSize.Z * Math.Abs(planeNormal.Z);

            // Intersection occurs when plane distance falls within [-r,+r] interval
            if (planeDistance > r)
            {
                return false;
            }

            #endregion

            return true;

            static float Min3(float a, float b, float c)
            {
                return MathF.Min(a, MathF.Min(b, c));
            }
            static float Max3(float a, float b, float c)
            {
                return MathF.Max(a, MathF.Max(b, c));
            }
        }

        // Source: https://www.iquilezles.org/www/articles/intersectors/intersectors.htm
        public static bool RayTriangleIntersect(in Ray ray, in Vector3 v0, in Vector3 v1, in Vector3 v2, out Vector3 bary, out float t)
        {
            Vector3 v1v0 = v1 - v0;
            Vector3 v2v0 = v2 - v0;
            Vector3 rov0 = ray.Origin - v0;
            Vector3 normal = Vector3.Cross(v1v0, v2v0);
            Vector3 q = Vector3.Cross(rov0, ray.Direction);

            float x = Vector3.Dot(ray.Direction, normal);
            bary = new Vector3();
            bary.Yz = new Vector2(Vector3.Dot(-q, v2v0), Vector3.Dot(q, v1v0)) / x;
            bary.X = 1.0f - bary.Y - bary.Z;

            t = Vector3.Dot(-normal, rov0) / x;

            return bary.X >= 0.0f && bary.Y >= 0.0f && bary.Z >= 0.0f && t > 0.0f;
        }

        // Source: https://medium.com/@bromanz/another-view-on-the-classic-ray-aabb-intersection-algorithm-for-bvh-traversal-41125138b525
        public static bool RayCuboidIntersect(in Ray ray, in Vector3 min, in Vector3 max, out float t1, out float t2)
        {
            t1 = float.MinValue;
            t2 = float.MaxValue;

            Vector3 t0s = (min - ray.Origin) / ray.Direction;
            Vector3 t1s = (max - ray.Origin) / ray.Direction;

            Vector3 tsmaller = Vector3.ComponentMin(t0s, t1s);
            Vector3 tbigger = Vector3.ComponentMax(t0s, t1s);


            t1 = MathF.Max(t1, MathF.Max(tsmaller.X, MathF.Max(tsmaller.Y, tsmaller.Z)));
            t2 = MathF.Min(t2, MathF.Min(tbigger.X, MathF.Min(tbigger.Y, tbigger.Z)));

            return t1 <= t2 && t2 > 0.0f;
        }

        // Source: https://antongerdelan.net/opengl/raycasting.html
        public static bool RaySphereIntersect(in Ray ray, in GLSLLight light, out float t1, out float t2)
        {
            t1 = t2 = float.MaxValue;

            Vector3 sphereToRay = ray.Origin - light.Position;
            float b = Vector3.Dot(ray.Direction, sphereToRay);
            float c = Vector3.Dot(sphereToRay, sphereToRay) - light.Radius * light.Radius;
            float discriminant = b * b - c;
            if (discriminant < 0.0f)
                return false;

            float squareRoot = MathF.Sqrt(discriminant);
            t1 = -b - squareRoot;
            t2 = -b + squareRoot;

            return t1 <= t2 && t2 > 0.0f;
        }
    }
}
