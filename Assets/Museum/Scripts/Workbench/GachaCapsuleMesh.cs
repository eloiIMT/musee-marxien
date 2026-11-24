using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Procedural gacha capsule, 1 unit wide (scale it by the capsule diameter), seam horizontal at y = 0.
    /// Submesh 0: the coloured bottom half, a thin shell whose lip rises a little over the seam and wraps the top.
    /// Submesh 1: the clear top half, a thin shell (outer + inner wall) that sits inside the lip.
    /// Both halves are modelled with their inner walls so the inside reads through the clear plastic.
    /// </summary>
    public static class GachaCapsuleMesh
    {
        public const float Radius = 0.5f;
        /// <summary>Half the straight part between the two domes.</summary>
        public const float HalfBand = 0.035f;
        /// <summary>Radius of the ball, around the centre, that fits inside both shells (room for the artwork).</summary>
        public const float InnerRadius = 0.44f;

        const float Shell = 0.012f;
        const float Top = HalfBand + Radius - Shell; // the clear dome sits inside the lip: one shell thinner
        const float Bottom = -HalfBand - Radius;
        public const float Height = Top - Bottom;
        /// <summary>Middle of the capsule's height (the seam is at 0, a hair above it).</summary>
        public const float CenterY = (Top + Bottom) * 0.5f;

        const float LipTop = 0.03f;
        const int Segments = 48;
        const int DomeSteps = 14;

        static Mesh mesh;

        /// <summary>Shared mesh (built once, rebuilt if Unity unloaded it).</summary>
        public static Mesh Shared
        {
            get
            {
                if (mesh == null) mesh = Build();
                return mesh;
            }
        }

        public static Mesh Build()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colored = new List<int>();
            var clear = new List<int>();

            float outer = Radius, inner = Radius - Shell; // coloured half
            float clearOuter = inner, clearInner = inner - Shell; // clear half, inside the lip

            // Coloured bottom half, cross-section walked as a closed outline (outer wall up, lip, inner wall down).
            Lathe(Dome(outer, -HalfBand, -90f, 0f, false), vertices, normals, colored);
            Lathe(Line(new Vector2(outer, -HalfBand), new Vector2(outer, LipTop), new Vector2(1f, 0f)), vertices, normals, colored);
            Lathe(Line(new Vector2(outer, LipTop), new Vector2(inner, LipTop), new Vector2(0f, 1f)), vertices, normals, colored);
            Lathe(Line(new Vector2(inner, LipTop), new Vector2(inner, -HalfBand), new Vector2(-1f, 0f)), vertices, normals, colored);
            Lathe(Dome(inner, -HalfBand, 0f, -90f, true), vertices, normals, colored);

            // Clear top half: outer wall from inside the lip up to the pole, inner wall back down, rim hidden in the lip.
            Lathe(Line(new Vector2(clearOuter, -HalfBand), new Vector2(clearOuter, HalfBand), new Vector2(1f, 0f)), vertices, normals, clear);
            Lathe(Dome(clearOuter, HalfBand, 0f, 90f, false), vertices, normals, clear);
            Lathe(Dome(clearInner, HalfBand, 90f, 0f, true), vertices, normals, clear);
            Lathe(Line(new Vector2(clearInner, HalfBand), new Vector2(clearInner, -HalfBand), new Vector2(-1f, 0f)), vertices, normals, clear);
            Lathe(Line(new Vector2(clearInner, -HalfBand), new Vector2(clearOuter, -HalfBand), new Vector2(0f, -1f)), vertices, normals, clear);

            var m = new Mesh { name = "Gacha Capsule" };
            m.SetVertices(vertices);
            m.SetNormals(normals);
            m.subMeshCount = 2;
            m.SetTriangles(colored, 0);
            m.SetTriangles(clear, 1);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Quarter-circle profile (radius, y) from <paramref name="fromDeg"/> to <paramref name="toDeg"/>.</summary>
        internal static List<(Vector2 p, Vector2 n)> Dome(float radius, float centerY, float fromDeg, float toDeg, bool inward)
        {
            var points = new List<(Vector2, Vector2)>(DomeSteps + 1);
            for (int i = 0; i <= DomeSteps; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)DomeSteps) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                if (Mathf.Abs(dir.x) < 1e-5f) dir.x = 0f; // exact pole: cos(90°) is not quite 0 in floats
                points.Add((new Vector2(radius * dir.x, centerY + radius * dir.y), inward ? -dir : dir));
            }
            return points;
        }

        internal static List<(Vector2 p, Vector2 n)> Line(Vector2 from, Vector2 to, Vector2 normal) =>
            new() { (from, normal), (to, normal) };

        /// <summary>Revolve a (radius, y) profile around Y; faces end up front-facing on the side of the given normals.</summary>
        internal static void Lathe(List<(Vector2 p, Vector2 n)> profile, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            int start = vertices.Count;
            int ring = Segments + 1;
            foreach (var (p, n) in profile)
            {
                for (int s = 0; s <= Segments; s++)
                {
                    float a = s / (float)Segments * Mathf.PI * 2f;
                    float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                    vertices.Add(new Vector3(p.x * cos, p.y, p.x * sin));
                    normals.Add(new Vector3(n.x * cos, n.y, n.x * sin).normalized);
                }
            }

            for (int i = 0; i < profile.Count - 1; i++)
            {
                for (int s = 0; s < Segments; s++)
                {
                    int a = start + i * ring + s, b = a + 1, c = a + ring, d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            FixWinding(start, profile.Count, ring, vertices, normals, triangles);
        }

        /// <summary>Flip the faces just added if they face away from their normals (keeps Lathe direction-agnostic).</summary>
        internal static void FixWinding(int start, int rows, int ring, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            int added = (rows - 1) * (ring - 1) * 6;
            int first = triangles.Count - added;
            // Judge on the biggest face: faces at a pole are (nearly) degenerate and their orientation is noise.
            float bestArea = 0f, bestDot = 0f;
            for (int t = first; t < triangles.Count; t += 3)
            {
                var a = vertices[triangles[t]];
                var face = Vector3.Cross(vertices[triangles[t + 1]] - a, vertices[triangles[t + 2]] - a);
                if (face.sqrMagnitude <= bestArea) continue;
                bestArea = face.sqrMagnitude;
                bestDot = Vector3.Dot(face, normals[triangles[t]] + normals[triangles[t + 1]] + normals[triangles[t + 2]]);
            }
            if (bestDot >= 0f) return;
            for (int u = first; u < triangles.Count; u += 3)
                (triangles[u + 1], triangles[u + 2]) = (triangles[u + 2], triangles[u + 1]);
        }
    }
}
