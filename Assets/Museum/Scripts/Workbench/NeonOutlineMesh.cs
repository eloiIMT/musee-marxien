using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Neon sign meshes: a round tube that follows the closed outline of a flat shape, like bent neon glass.
    /// Shapes are drawn in the XY plane, as seen from +Z (x right, y up); the tube is swept around that outline.
    /// </summary>
    public static class NeonOutlineMesh
    {
        const int Sides = 10;
        const int CornerSteps = 5;

        /// <summary>Tube of radius <paramref name="tubeRadius"/> around the closed <paramref name="outline"/>, corners rounded.</summary>
        public static Mesh Build(IReadOnlyList<Vector2> outline, float tubeRadius, float cornerRadius, string name)
        {
            var path = RoundCorners(outline, cornerRadius);
            path.Add(path[0]); // closed: repeat the first ring
            int rows = path.Count, ring = Sides + 1;

            var vertices = new List<Vector3>(rows * ring);
            var normals = new List<Vector3>(rows * ring);
            for (int i = 0; i < rows; i++)
            {
                var prev = path[(i - 1 + path.Count - 1) % (path.Count - 1)];
                var next = path[(i + 1) % (path.Count - 1)];
                var tangent = (next - prev).normalized;
                var side = new Vector3(tangent.y, -tangent.x, 0f); // in the plane, across the tube
                for (int s = 0; s <= Sides; s++)
                {
                    float a = s / (float)Sides * Mathf.PI * 2f;
                    var n = side * Mathf.Cos(a) + Vector3.forward * Mathf.Sin(a);
                    vertices.Add((Vector3)path[i] + n * tubeRadius);
                    normals.Add(n);
                }
            }

            var triangles = new List<int>((rows - 1) * Sides * 6);
            for (int i = 0; i < rows - 1; i++)
            {
                for (int s = 0; s < Sides; s++)
                {
                    int a = i * ring + s, b = a + 1, c = a + ring, d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }
            GachaCapsuleMesh.FixWinding(0, rows, ring, vertices, normals, triangles);

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Straight arrow pointing +Y, tip at the origin.</summary>
        public static List<Vector2> StraightArrow(float length, float headLength, float headHalfWidth, float shaftHalfWidth) => new()
        {
            new Vector2(0f, 0f),
            new Vector2(headHalfWidth, -headLength),
            new Vector2(shaftHalfWidth, -headLength),
            new Vector2(shaftHalfWidth, -length),
            new Vector2(-shaftHalfWidth, -length),
            new Vector2(-shaftHalfWidth, -headLength),
            new Vector2(-headHalfWidth, -headLength),
        };

        /// <summary>
        /// Curved arrow around the origin: a band between <paramref name="innerRadius"/> and <paramref name="outerRadius"/>
        /// from <paramref name="startDeg"/> going clockwise for <paramref name="sweepDeg"/>, then a head of
        /// <paramref name="headDeg"/> pointing on clockwise. Angles: 0 = right, 90 = up.
        /// </summary>
        public static List<Vector2> CircularArrow(float innerRadius, float outerRadius, float startDeg, float sweepDeg,
                                                  float headDeg, float headFlare)
        {
            var points = new List<Vector2>();
            float end = startDeg - sweepDeg;
            int steps = Mathf.Max(2, Mathf.CeilToInt(sweepDeg / 6f));
            for (int i = 0; i <= steps; i++) points.Add(Polar(outerRadius, Mathf.Lerp(startDeg, end, i / (float)steps)));
            points.Add(Polar(outerRadius + headFlare, end));
            points.Add(Polar((innerRadius + outerRadius) * 0.5f, end - headDeg));
            points.Add(Polar(innerRadius - headFlare, end));
            for (int i = steps; i >= 0; i--) points.Add(Polar(innerRadius, Mathf.Lerp(startDeg, end, i / (float)steps)));
            return points;
        }

        static Vector2 Polar(float radius, float deg) =>
            new(radius * Mathf.Cos(deg * Mathf.Deg2Rad), radius * Mathf.Sin(deg * Mathf.Deg2Rad));

        /// <summary>Replace every corner by a short curve (quadratic Bézier), as neon glass is bent, not folded.</summary>
        static List<Vector2> RoundCorners(IReadOnlyList<Vector2> outline, float radius)
        {
            var path = new List<Vector2>();
            int n = outline.Count;
            for (int i = 0; i < n; i++)
            {
                var prev = outline[(i - 1 + n) % n];
                var p = outline[i];
                var next = outline[(i + 1) % n];
                float cut = Mathf.Min(radius, 0.45f * Vector2.Distance(prev, p), 0.45f * Vector2.Distance(p, next));
                var from = p + (prev - p).normalized * cut;
                var to = p + (next - p).normalized * cut;
                for (int k = 0; k <= CornerSteps; k++)
                {
                    float t = k / (float)CornerSteps;
                    path.Add((1 - t) * (1 - t) * from + 2f * (1 - t) * t * p + t * t * to);
                }
            }
            return path;
        }
    }
}
