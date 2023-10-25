using System;
using OpenTK.Mathematics;

namespace IDKEngine.Shapes
{
    public static class Intersections
    {
        // Source: https://github.com/efryscok/OpenGL-Basic-Collision-Detection/blob/master/Project1_efryscok/TheMain.cpp#L276
        public static Vector3 TriangleClosestPoint(in Triangle triangle, in Vector3 point)
        {
            // Check if P in vertex region outside A
            Vector3 ab = triangle.P1 - triangle.P0;
            Vector3 ac = triangle.P2 - triangle.P0;
            Vector3 ap = point - triangle.P0;
            float d1 = Vector3.Dot(ab, ap);        // Vector3.Dot( ab, ap );
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0.0f && d2 <= 0.0f) return triangle.P0; // barycentric coordinates (1,0,0)

            // Check if P in vertex region outside B
            Vector3 bp = point - triangle.P1;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0.0f && d4 <= d3) return triangle.P1; // barycentric coordinates (0,1,0)

            // Check if P in edge region of AB, if so return projection of P onto AB
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0.0f && d1 >= 0.0f && d3 <= 0.0f)
            {
                float v = d1 / (d1 - d3);
                return triangle.P0 + v * ab; // barycentric coordinates (1-v,v,0)
            }

            // Check if P in vertex region outside C
            Vector3 cp = point - triangle.P2;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0.0f && d5 <= d6) return triangle.P2; // barycentric coordinates (0,0,1)

            // Check if P in edge region of AC, if so return projection of P onto AC
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0.0f && d2 >= 0.0f && d6 <= 0.0f)
            {
                float w = d2 / (d2 - d6);
                return triangle.P0 + w * ac; // barycentric coordinates (1-w,0,w)
            }

            // Check if P in edge region of BC, if so return projection of P onto BC
            float va = d3 * d6 - d5 * d4;
            if (va <= 0.0f && (d4 - d3) >= 0.0f && (d5 - d6) >= 0.0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return triangle.P1 + w * (triangle.P2 - triangle.P1); // barycentric coordinates (0,1-w,w)
            }

            // P inside face region. Compute Q through its barycentric coordinates (u,v,w)
            {
                float denom = 1.0f / (va + vb + vc);
                float v = vb * denom;
                float w = vc * denom;

                return triangle.P0 + ab * v + ac * w; // = u*a + v*b + w*c, u = va * denom = 1.0f - v - w
            }
        }

        public static bool SphereVsTriangle(in Sphere sphere, in Triangle triangle)
        {
            Vector3 triangleClosestPoint = TriangleClosestPoint(triangle, sphere.Center);
            float distSquared = Vector3.DistanceSquared(triangleClosestPoint, sphere.Center);

            return distSquared < sphere.RadiusSquared();
        }

        // Source: https://stackoverflow.com/a/4579069/12103839
        public static bool SphereVsBox(in Sphere sphere, Vector3 c1, Vector3 c2)
        {
            float distSquared = Squared(sphere.Radius);
            if (sphere.Center.X < c1.X) distSquared -= Squared(sphere.Center.X - c1.X);
            else if (sphere.Center.X > c2.X) distSquared -= Squared(sphere.Center.X - c2.X);
            if (sphere.Center.Y < c1.Y) distSquared -= Squared(sphere.Center.Y - c1.Y);
            else if (sphere.Center.Y > c2.Y) distSquared -= Squared(sphere.Center.Y - c2.Y);
            if (sphere.Center.Z < c1.Z) distSquared -= Squared(sphere.Center.Z - c1.Z);
            else if (sphere.Center.Z > c2.Z) distSquared -= Squared(sphere.Center.Z - c2.Z);
            return distSquared > 0.0f;

            static float Squared(float x)
            {
                return x * x;
            }
        }        

        public static bool BoxVsBox(in Box a, in Box b)
        {
            return a.Min.X < b.Max.X &&
                   a.Min.Y < b.Max.Y &&
                   a.Min.Z < b.Max.Z &&

                   a.Max.X > b.Min.X &&
                   a.Max.Y > b.Min.Y &&
                   a.Max.Z > b.Min.Z;
        }
        
        // Source: "Real-Time Collision Detection" by Christer Ericson, page 169
        public static bool BoxVsTriangle(in Box box, in Triangle triangle)
        {
            // Translate triangle as conceptually moving Box to origin
            var v0 = (triangle.P0 - box.Center());
            var v1 = (triangle.P1 - box.Center());
            var v2 = (triangle.P2 - box.Center());

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
            var halfSize = box.HalfSize();
            var r = halfSize.Y * MathF.Abs(f0.Z) + halfSize.Z * MathF.Abs(f0.Y);
            if (MathF.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a01
            var a01 = new Vector3(0, -f1.Z, f1.Y);
            p0 = Vector3.Dot(v0, a01);
            p1 = Vector3.Dot(v1, a01);
            p2 = Vector3.Dot(v2, a01);
            r = halfSize.Y * MathF.Abs(f1.Z) + halfSize.Z * MathF.Abs(f1.Y);
            if (MathF.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a02
            var a02 = new Vector3(0, -f2.Z, f2.Y);
            p0 = Vector3.Dot(v0, a02);
            p1 = Vector3.Dot(v1, a02);
            p2 = Vector3.Dot(v2, a02);
            r = halfSize.Y * MathF.Abs(f2.Z) + halfSize.Z * MathF.Abs(f2.Y);
            if (MathF.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a10
            var a10 = new Vector3(f0.Z, 0, -f0.X);
            p0 = Vector3.Dot(v0, a10);
            p1 = Vector3.Dot(v1, a10);
            p2 = Vector3.Dot(v2, a10);
            r = halfSize.X * MathF.Abs(f0.Z) + halfSize.Z * MathF.Abs(f0.X);
            if (MathF.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a11
            var a11 = new Vector3(f1.Z, 0, -f1.X);
            p0 = Vector3.Dot(v0, a11);
            p1 = Vector3.Dot(v1, a11);
            p2 = Vector3.Dot(v2, a11);
            r = halfSize.X * MathF.Abs(f1.Z) + halfSize.Z * MathF.Abs(f1.X);
            if (MathF.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a12
            var a12 = new Vector3(f2.Z, 0, -f2.X);
            p0 = Vector3.Dot(v0, a12);
            p1 = Vector3.Dot(v1, a12);
            p2 = Vector3.Dot(v2, a12);
            r = halfSize.X * MathF.Abs(f2.Z) + halfSize.Z * MathF.Abs(f2.X);
            if (MathF.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a20
            var a20 = new Vector3(-f0.Y, f0.X, 0);
            p0 = Vector3.Dot(v0, a20);
            p1 = Vector3.Dot(v1, a20);
            p2 = Vector3.Dot(v2, a20);
            r = halfSize.X * MathF.Abs(f0.Y) + halfSize.Y * MathF.Abs(f0.X);
            if (MathF.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a21
            var a21 = new Vector3(-f1.Y, f1.X, 0);
            p0 = Vector3.Dot(v0, a21);
            p1 = Vector3.Dot(v1, a21);
            p2 = Vector3.Dot(v2, a21);
            r = halfSize.X * MathF.Abs(f1.Y) + halfSize.Y * MathF.Abs(f1.X);
            if (MathF.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a22
            var a22 = new Vector3(-f2.Y, f2.X, 0);
            p0 = Vector3.Dot(v0, a22);
            p1 = Vector3.Dot(v1, a22);
            p2 = Vector3.Dot(v2, a22);
            r = halfSize.X * MathF.Abs(f2.Y) + halfSize.Y * MathF.Abs(f2.X);
            if (MathF.Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }

            #endregion

            #region Test the three axes corresponding to the face normals of Box b (category 1)

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
            r = halfSize.X * MathF.Abs(planeNormal.X) + halfSize.Y * MathF.Abs(planeNormal.Y) + halfSize.Z * MathF.Abs(planeNormal.Z);

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
        public static bool RayVsTriangle(in Ray ray, in Triangle triangle, out Vector3 bary, out float t)
        {
            Vector3 v1v0 = triangle.P1 - triangle.P0;
            Vector3 v2v0 = triangle.P2 - triangle.P0;
            Vector3 rov0 = ray.Origin - triangle.P0;
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
        public static bool RayVsBox(in Ray ray, in Box box, out float t1, out float t2)
        {
            t1 = float.MinValue;
            t2 = float.MaxValue;

            Vector3 t0s = (box.Min - ray.Origin) / ray.Direction;
            Vector3 t1s = (box.Max - ray.Origin) / ray.Direction;

            Vector3 tsmaller = Vector3.ComponentMin(t0s, t1s);
            Vector3 tbigger = Vector3.ComponentMax(t0s, t1s);

            t1 = MathF.Max(t1, MathF.Max(tsmaller.X, MathF.Max(tsmaller.Y, tsmaller.Z)));
            t2 = MathF.Min(t2, MathF.Min(tbigger.X, MathF.Min(tbigger.Y, tbigger.Z)));

            return t1 <= t2 && t2 > 0.0f;
        }

        // Source: https://antongerdelan.net/opengl/raycasting.html
        public static bool RayVsSphere(in Ray ray, in Sphere sphere, out float t1, out float t2)
        {
            t1 = float.MaxValue;
            t2 = float.MaxValue;

            Vector3 sphereToRay = ray.Origin - sphere.Center;
            float b = Vector3.Dot(ray.Direction, sphereToRay);
            float c = Vector3.Dot(sphereToRay, sphereToRay) - sphere.RadiusSquared();
            float discriminant = b * b - c;
            if (discriminant < 0.0f)
            {
                return false;
            }

            float squareRoot = MathF.Sqrt(discriminant);
            t1 = -b - squareRoot;
            t2 = -b + squareRoot;

            return t1 <= t2 && t2 > 0.0f;
        }


        public static Vector3 ClosestPointOnLineSegment(in Vector3 a, in Vector3 b, in Vector3 point)
        {
            Vector3 ab = b - a;
            float t = Vector3.Dot(point - a, ab) / Vector3.Dot(ab, ab);
            return a + Math.Clamp(t, 0.0f, 1.0f) * ab;
        }
    }
}
