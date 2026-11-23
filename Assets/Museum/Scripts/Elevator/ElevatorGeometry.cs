using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Pure geometry of the elevator: shaft meshes (round tube, square-to-round collar) and the lever's angle maths.
    /// No scene access, so it is covered by EditMode tests.
    /// </summary>
    public static class ElevatorGeometry
    {
        /// <summary>Lever angle (degrees about its local X) at which the handle points at a position in pivot space (x ignored).</summary>
        public static float AngleTowards(Vector3 pivotSpace) => Mathf.Atan2(pivotSpace.z, pivotSpace.y) * Mathf.Rad2Deg;

        /// <summary>
        /// Which side of the wall plan the lever (and the platform at its foot) goes: +1 = the plan's right, -1 = its
        /// left. The side whose platform spot is farther from every footprint (room-local rects) wins; ties: right.
        /// </summary>
        public static int LeverSide(Vector2 rightSpot, Vector2 leftSpot, IList<Rect> footprints)
        {
            float Clear(Vector2 p)
            {
                float best = float.MaxValue;
                foreach (var o in footprints) best = Mathf.Min(best, DistanceToRect(p, o));
                return best;
            }
            return Clear(rightSpot) >= Clear(leftSpot) - 1e-4f ? 1 : -1;
        }

        /// <summary>Distance from a point to a rectangle (0 inside).</summary>
        public static float DistanceToRect(Vector2 p, Rect r)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
            float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Smooth start and stop (0..1 -> 0..1).</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Open vertical tube between <paramref name="y0"/> and <paramref name="y1"/>, faces turned TOWARDS the axis (seen
        /// from inside). With <paramref name="capAtY1"/> the end at y1 is closed by a disc facing back into the tube.
        /// UV: u = metres around, v = metres from <paramref name="vOrigin"/> (two tubes with the same origin relative to
        /// their closed end show the same pattern at the same distance from it).
        /// </summary>
        public static Mesh Tube(float radius, float y0, float y1, int segments, bool capAtY1, float vOrigin = 0f)
        {
            var b = new Builder();
            float circumference = 2f * Mathf.PI * radius;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * 2f * Mathf.PI / segments, a1 = (i + 1) * 2f * Mathf.PI / segments;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var inward = -(d0 + d1).normalized;
                float u0 = circumference * i / segments, u1 = circumference * (i + 1) / segments;
                float v0 = y0 - vOrigin, v1 = y1 - vOrigin;
                b.Quad(d0 * radius + Vector3.up * y0, d1 * radius + Vector3.up * y0, d1 * radius + Vector3.up * y1, d0 * radius + Vector3.up * y1,
                       inward, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
            }
            if (capAtY1) AddDisc(b, radius, y1, y1 > y0 ? Vector3.down : Vector3.up, segments);
            return b.ToMesh("Elevator Tube");
        }

        /// <summary>Flat disc at height <paramref name="y"/> facing <paramref name="facing"/> (UV 0: same look as a tube's cap).</summary>
        public static Mesh Disc(float radius, float y, Vector3 facing, int segments)
        {
            var b = new Builder();
            AddDisc(b, radius, y, facing, segments);
            return b.ToMesh("Elevator Disc");
        }

        static void AddDisc(Builder b, float radius, float y, Vector3 facing, int segments)
        {
            var centre = Vector3.up * y;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * 2f * Mathf.PI / segments, a1 = (i + 1) * 2f * Mathf.PI / segments;
                var p0 = centre + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                var p1 = centre + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                b.Triangle(centre, p0, p1, facing);
            }
        }

        /// <summary>
        /// Flat plate filling a square opening of half-size <paramref name="squareHalf"/> around a round one of radius
        /// <paramref name="radius"/> (≤ squareHalf), between two heights: top face up, bottom face down.
        /// </summary>
        public static Mesh Collar(float radius, float squareHalf, float yBottom, float yTop, int segments)
        {
            var b = new Builder();
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * 2f * Mathf.PI / segments, a1 = (i + 1) * 2f * Mathf.PI / segments;
                var in0 = Ring(a0, radius); var in1 = Ring(a1, radius);
                var out0 = SquarePoint(a0, squareHalf); var out1 = SquarePoint(a1, squareHalf);
                foreach (var (y, n) in new[] { (yTop, Vector3.up), (yBottom, Vector3.down) })
                {
                    var h = Vector3.up * y;
                    b.Quad(in0 + h, out0 + h, out1 + h, in1 + h, n,
                           new Vector2(in0.x, in0.z), new Vector2(out0.x, out0.z), new Vector2(out1.x, out1.z), new Vector2(in1.x, in1.z));
                }
            }
            return b.ToMesh("Elevator Collar");
        }

        static Vector3 Ring(float a, float r) => new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;

        /// <summary>Point of the square of half-size s in the direction of angle a (from the centre).</summary>
        public static Vector3 SquarePoint(float a, float s)
        {
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            return d * (s / Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z)));
        }

        /// <summary>Mesh builder that orders each triangle so its front face (Unity: normal = cross(b - a, c - a)) is along a wanted normal.</summary>
        class Builder
        {
            readonly List<Vector3> v = new();
            readonly List<Vector3> n = new();
            readonly List<Vector2> uv = new();
            readonly List<int> t = new();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                Triangle(a, b, c, normal, ua, ub, uc);
                Triangle(a, c, d, normal, ua, uc, ud);
            }

            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 ua = default, Vector2 ub = default, Vector2 uc = default)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0) { (b, c) = (c, b); (ub, uc) = (uc, ub); }
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                n.Add(normal); n.Add(normal); n.Add(normal);
                uv.Add(ua); uv.Add(ub); uv.Add(uc);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
