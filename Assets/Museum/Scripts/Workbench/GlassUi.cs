using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Shared look of the "glass" workbench controls (height slider, mode selector): palette, design-pixel units and
    /// procedural shapes (rounded rectangles, stroke icons). Matches the "Slider Hauteur Glass" design canvas.
    /// Everything faces -Z of its parent (the user), sizes are design px converted with <see cref="PxToWorld"/>.
    /// </summary>
    public static class GlassUi
    {
        public const float PxToWorld = 0.0006f; // 360 px wide = 21.6 cm

        // Opaque approximation of the glass look over the light board (no blur in the miniature).
        public static readonly Color Rim = new Color(0.56f, 0.56f, 0.58f);
        public static readonly Color Panel = new Color(0.376f, 0.376f, 0.4f);
        public static readonly Color PanelHover = new Color(0.43f, 0.43f, 0.455f);
        public static readonly Color Track = new Color(0.29f, 0.29f, 0.31f);
        public static readonly Color Fill = new Color(0.94f, 0.94f, 0.94f);
        public static readonly Color Grip = new Color(0.74f, 0.74f, 0.75f);
        public static readonly Color TickOnTrack = new Color(0.54f, 0.54f, 0.56f);
        public static readonly Color TickOnFill = new Color(0.79f, 0.79f, 0.79f);
        public static readonly Color TextMain = new Color(0.96f, 0.96f, 0.96f);
        public static readonly Color TextMuted = new Color(0.88f, 0.88f, 0.89f);
        public static readonly Color TextOnFill = new Color(0.11f, 0.11f, 0.12f);

        public static Vector3 Px(float x, float y, float zMetres) => new Vector3(x * PxToWorld, y * PxToWorld, zMetres);

        /// <summary>
        /// Colour for a TextMesh: its vertex colour reaches the WorldText shader without the sRGB → linear conversion
        /// materials get, so in a linear project dark text would come out grey.
        /// </summary>
        public static Color TextColor(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        /// <summary>Line height in world metres for a CSS font size in design px.</summary>
        public static float LineHeight(float fontPx) => fontPx * PxToWorld * 1.25f;

        /// <summary>A flat rounded rectangle (sizes in design px, depth offset in metres), unlit and tinted.</summary>
        public static Transform Part(Transform parent, string name, float w, float h, float r, float x, float y, float z, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Px(x, y, z);
            go.AddComponent<MeshFilter>().sharedMesh = RoundedRect(w * PxToWorld, h * PxToWorld, r * PxToWorld, null);
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = UiKit.Material;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            Tint.Set(rend, color);
            return go.transform;
        }

        /// <summary>Triangle fan of a rounded rectangle centred on the origin in the XY plane, front face towards -Z.</summary>
        public static Mesh RoundedRect(float w, float h, float r, Mesh reuse)
        {
            const int Segments = 8;
            r = Mathf.Min(r, w / 2, h / 2);
            var verts = new List<Vector3> { Vector3.zero };
            var corners = new[]
            {
                new Vector2(w / 2 - r, h / 2 - r), new Vector2(-w / 2 + r, h / 2 - r),
                new Vector2(-w / 2 + r, -h / 2 + r), new Vector2(w / 2 - r, -h / 2 + r),
            };
            for (int c = 0; c < 4; c++)
                for (int s = 0; s <= Segments; s++)
                {
                    float a = (c * 90f + s * 90f / Segments) * Mathf.Deg2Rad; // counter-clockwise from +X
                    verts.Add(new Vector3(corners[c].x + Mathf.Cos(a) * r, corners[c].y + Mathf.Sin(a) * r, 0));
                }
            int n = verts.Count - 1;
            var tris = new int[n * 3];
            for (int i = 0; i < n; i++)
            {
                // Clockwise as seen from -Z = front face.
                tris[i * 3] = 0;
                tris[i * 3 + 1] = 1 + (i + 1) % n;
                tris[i * 3 + 2] = 1 + i;
            }
            var mesh = reuse != null ? reuse : new Mesh { name = "Rounded Rect" };
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Width of a TextMesh line in its parent's units (built-in font metrics, ~0.1 unit per font pixel).</summary>
        public static float TextWidth(TextMesh tm)
        {
            var font = tm.font;
            font.RequestCharactersInTexture(tm.text, tm.fontSize, tm.fontStyle);
            float px = 0;
            foreach (char c in tm.text)
                if (font.GetCharacterInfo(c, out var info, tm.fontSize, tm.fontStyle)) px += info.advance;
            return px * 0.1f * tm.characterSize * tm.transform.localScale.x;
        }

        // ---------------------------------------------------------------- icons

        public enum IconKind { Height, Move, Link, Joystick, Resize }

        static readonly Dictionary<IconKind, Material> iconMaterials = new();

        /// <summary>A square icon quad (size in design px), white strokes tinted with <paramref name="color"/>.</summary>
        public static Renderer Icon(Transform parent, IconKind kind, float sizePx, float x, float y, float z, Color color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Icon " + kind;
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = Px(x, y, z);
            quad.transform.localScale = Vector3.one * (sizePx * PxToWorld);
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = IconMaterial(kind);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Tint.Set(r, color);
            return r;
        }

        static Material IconMaterial(IconKind kind)
        {
            if (iconMaterials.TryGetValue(kind, out var cached) && cached != null) return cached;
            var tex = DrawIcon(kind, 64);
            Material mat;
            var urp = Shader.Find("Universal Render Pipeline/Unlit");
            if (urp != null)
            {
                mat = new Material(urp);
                mat.SetTexture("_BaseMap", tex);
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
            }
            else
            {
                mat = new Material(Shader.Find("Unlit/Transparent Cutout"));
            }
            mat.name = $"Icon {kind} (runtime)";
            mat.mainTexture = tex;
            iconMaterials[kind] = mat;
            return mat;
        }

        /// <summary>One stroke: the segment a→b offset by <c>ring</c> (0 = plain line; &gt;0 = capsule / circle outline).</summary>
        readonly struct Stroke
        {
            public readonly Vector2 A, B;
            public readonly float Ring;
            public Stroke(float ax, float ay, float bx, float by, float ring = 0) { A = new Vector2(ax, ay); B = new Vector2(bx, by); Ring = ring; }
        }

        /// <summary>Same drawings as the design's inline SVGs (18-unit viewBox, 16 for the joystick, y down).</summary>
        static (Stroke[] strokes, float viewBox, float width) Shape(IconKind kind) => kind switch
        {
            IconKind.Height => (new[]
            {
                new Stroke(9, 3, 9, 15), new Stroke(6, 6, 9, 3), new Stroke(9, 3, 12, 6),
                new Stroke(6, 12, 9, 15), new Stroke(9, 15, 12, 12),
            }, 18f, 1.8f),
            IconKind.Move => (new[]
            {
                new Stroke(9, 2, 9, 16), new Stroke(2, 9, 16, 9),
                new Stroke(7, 4, 9, 2), new Stroke(9, 2, 11, 4), new Stroke(7, 14, 9, 16), new Stroke(9, 16, 11, 14),
                new Stroke(4, 7, 2, 9), new Stroke(2, 9, 4, 11), new Stroke(14, 7, 16, 9), new Stroke(16, 9, 14, 11),
            }, 18f, 1.8f),
            // Resize: corner-to-corner double arrow (expand), arrowheads as right-angle corners.
            IconKind.Resize => (new[]
            {
                new Stroke(4.5f, 13.5f, 13.5f, 4.5f),
                new Stroke(9, 3.5f, 14.5f, 3.5f), new Stroke(14.5f, 3.5f, 14.5f, 9),
                new Stroke(3.5f, 9, 3.5f, 14.5f), new Stroke(3.5f, 14.5f, 9, 14.5f),
            }, 18f, 1.8f),
            IconKind.Link => (new[]
            {
                new Stroke(10.6f, 7.4f, 12.4f, 5.6f, 2.6f), new Stroke(5.6f, 12.4f, 7.4f, 10.6f, 2.6f),
                new Stroke(7.6f, 10.4f, 10.4f, 7.6f),
            }, 18f, 1.8f),
            _ => (new[]
            {
                new Stroke(8, 8, 8, 8, 2.6f),
                new Stroke(3.5f, 5.8f, 1.5f, 8), new Stroke(1.5f, 8, 3.5f, 10.2f),
                new Stroke(12.5f, 5.8f, 14.5f, 8), new Stroke(14.5f, 8, 12.5f, 10.2f),
            }, 16f, 1.6f),
        };

        static Texture2D DrawIcon(IconKind kind, int size)
        {
            var (strokes, viewBox, width) = Shape(kind);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Icon " + kind };
            var pixels = new Color[size * size];
            const int ss = 4;
            float half = width / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                {
                    // Texture v goes up, the SVG y goes down.
                    var p = new Vector2((x + (sx + 0.5f) / ss) / size * viewBox, (1f - (y + (sy + 0.5f) / ss) / size) * viewBox);
                    foreach (var s in strokes)
                        if (Mathf.Abs(DistanceToSegment(p, s.A, s.B) - s.Ring) <= half) { hits++; break; }
                }
                pixels[y * size + x] = new Color(1f, 1f, 1f, hits / (float)(ss * ss));
            }
            tex.SetPixels(pixels);
            tex.Apply(true, true);
            return tex;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
