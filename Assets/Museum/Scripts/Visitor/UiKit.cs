using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Minimal world-space UI built from primitives and TextMesh (no Canvas / TextMeshPro setup needed).
    /// Everything is sized in world metres. Panels are dark plates with light text: readable on white museum walls.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.11f);
        public static readonly Color TextColor = new Color(0.95f, 0.95f, 0.93f);
        public static readonly Color MutedText = new Color(0.65f, 0.67f, 0.7f);
        public static readonly Color Ok = new Color(0.3f, 0.85f, 0.45f);
        public static readonly Color Error = new Color(1f, 0.38f, 0.32f);
        public static readonly Color Info = new Color(0.35f, 0.75f, 1f);

        static Material defaultMaterial;

        /// <summary>Unlit material used for plates and buttons (tinted per renderer with <see cref="Tint"/>).</summary>
        public static Material Material
        {
            get
            {
                if (defaultMaterial == null)
                    defaultMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
                return defaultMaterial;
            }
            set => defaultMaterial = value;
        }

        /// <summary>Museum/WorldText material (assign the asset so the shader ships in builds).</summary>
        public static Material TextMaterialTemplate { get; set; }
        static Material textMaterial;

        /// <summary>Depth-tested text material on the font atlas: text is hidden behind walls like any object.</summary>
        static Material TextMaterial(Font font)
        {
            if (textMaterial != null) return textMaterial;
            var shader = TextMaterialTemplate != null ? TextMaterialTemplate.shader : Shader.Find("Museum/WorldText");
            if (shader == null) return null;
            textMaterial = new Material(shader) { mainTexture = font.material.mainTexture, name = "World Text (runtime)" };
            return textMaterial;
        }

        /// <summary>Switch an existing TextMesh (e.g. a RoomView label) to the depth-tested text material.</summary>
        public static void UseWorldText(TextMesh tm)
        {
            var mat = TextMaterial(tm.font);
            if (mat != null) tm.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>A thin coloured box (no collider unless asked). Pivot at its centre.</summary>
        public static Transform Plate(Transform parent, string name, Vector3 localPosition, Vector3 size, Color color, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = Material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Tint.Set(r, color);
            return go.transform;
        }

        /// <summary>Text with a given line height in world metres. Faces -Z of its parent (readable from the front).</summary>
        public static TextMesh Text(Transform parent, string text, Vector3 localPosition, float lineHeight, Color color,
                                    TextAnchor anchor = TextAnchor.UpperLeft, TextAlignment alignment = TextAlignment.Left)
        {
            var tm = Labels.Create(parent, text, lineHeight, color, billboard: false);
            var mat = TextMaterial(tm.font);
            if (mat != null) tm.GetComponent<MeshRenderer>().sharedMaterial = mat;
            tm.anchor = anchor;
            tm.alignment = alignment;
            tm.transform.localPosition = localPosition;
            return tm;
        }

        /// <summary>
        /// Greedy word wrap for TextMesh (which has none). Arial averages ~0.5 line-height per character,
        /// so maxChars ≈ width / (0.5 × lineHeight). Lines beyond maxLines are cut with an ellipsis.
        /// </summary>
        public static string Wrap(string text, int maxChars, int maxLines = int.MaxValue)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r", "").Split('\n'))
            {
                var line = new StringBuilder();
                foreach (var word in paragraph.Split(' '))
                {
                    var w = word;
                    while (w.Length > maxChars) // very long word: hard cut
                    {
                        if (line.Length > 0) { lines.Add(line.ToString()); line.Clear(); }
                        lines.Add(w.Substring(0, maxChars));
                        w = w.Substring(maxChars);
                    }
                    if (line.Length > 0 && line.Length + 1 + w.Length > maxChars) { lines.Add(line.ToString()); line.Clear(); }
                    if (line.Length > 0) line.Append(' ');
                    line.Append(w);
                }
                lines.Add(line.ToString());
            }
            if (lines.Count > maxLines)
            {
                lines.RemoveRange(maxLines, lines.Count - maxLines);
                var last = lines[maxLines - 1];
                lines[maxLines - 1] = (last.Length > maxChars - 1 ? last.Substring(0, maxChars - 1) : last) + "…";
            }
            return string.Join("\n", lines);
        }

        public static int LineCount(string wrapped) => string.IsNullOrEmpty(wrapped) ? 0 : wrapped.Split('\n').Length;

        public static int CharsFor(float width, float lineHeight) => Mathf.Max(8, Mathf.FloorToInt(width / (0.5f * lineHeight)));

        /// <summary>Flat arrow mesh in the XZ plane pointing +Z, 1 unit long, centred on its middle.</summary>
        public static Mesh ArrowMesh()
        {
            var m = new Mesh { name = "Arrow" };
            m.vertices = new[]
            {
                new Vector3(-0.15f, 0, -0.5f), new Vector3(0.15f, 0, -0.5f), new Vector3(0.15f, 0, 0.05f), new Vector3(-0.15f, 0, 0.05f),
                new Vector3(-0.38f, 0, 0.05f), new Vector3(0.38f, 0, 0.05f), new Vector3(0f, 0, 0.5f),
            };
            // Both faces, so it reads from above and below.
            m.triangles = new[] { 0, 3, 2, 0, 2, 1, 4, 6, 5, 0, 2, 3, 0, 1, 2, 4, 5, 6 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>An arrow object (unlit, tinted).</summary>
        public static Transform Arrow(Transform parent, string name, float length, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * length;
            go.AddComponent<MeshFilter>().sharedMesh = ArrowMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Tint.Set(r, color);
            return go.transform;
        }
    }

    /// <summary>
    /// A 3D button: a plate with a caption, pointed with the ray and clicked with the TRIGGER (see <see cref="Clickable"/>).
    /// Lightens on hover; can be disabled (greyed, clicks ignored) and gives a short click sound.
    /// </summary>
    public class UiButton : MonoBehaviour
    {
        public event Action Clicked;
        public event Action<bool> HoverChanged;
        public TextMesh Caption { get; private set; }
        public bool IsHovered => clickable != null && clickable.IsHovered;

        Renderer plate;
        Clickable clickable;
        Color color;
        bool interactable = true, selected;

        public static UiButton Create(Transform parent, string label, Vector3 localPosition, Vector2 size, Color color, float lineHeight = 0.03f)
        {
            var root = new GameObject("Button · " + label);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            var b = root.AddComponent<UiButton>();
            var plate = UiKit.Plate(root.transform, "Plate", Vector3.zero, new Vector3(size.x, size.y, 0.012f), color, collider: true);
            b.plate = plate.GetComponent<Renderer>();
            b.color = color;
            b.Caption = UiKit.Text(root.transform, label, new Vector3(0, 0, -0.008f), lineHeight, Color.white, TextAnchor.MiddleCenter, TextAlignment.Center);
            b.clickable = plate.gameObject.AddComponent<Clickable>();
            b.clickable.Clicked += (_, _) => b.OnClick();
            b.clickable.HoverChanged += h => { b.Refresh(); b.HoverChanged?.Invoke(h); };
            return b;
        }

        public void SetColor(Color c) { color = c; Refresh(); }
        public void SetLabel(string text) { if (Caption != null) Caption.text = text; }

        /// <summary>Disabled buttons are greyed and ignore clicks (but still show their caption).</summary>
        public bool Interactable { get => interactable; set { interactable = value; Refresh(); } }

        /// <summary>Selected = outlined look (brighter, kept while not hovered), for radio-like lists.</summary>
        public bool Selected { get => selected; set { selected = value; Refresh(); } }

        void OnClick()
        {
            if (!interactable) { Sounds.Play(Sounds.Error, 0.4f); return; }
            Sounds.Play(Sounds.Click, 0.5f);
            Clicked?.Invoke();
        }

        void Refresh()
        {
            if (plate == null) return;
            var c = interactable ? color : Color.Lerp(color, new Color(0.3f, 0.3f, 0.32f), 0.75f);
            if (selected) c = Color.Lerp(c, Color.white, 0.25f);
            if (IsHovered && interactable) c = Color.Lerp(c, Color.white, 0.4f);
            Tint.Set(plate, c);
            if (Caption != null) Caption.color = interactable ? Color.white : UiKit.MutedText;
        }
    }

    /// <summary>Procedural feedback sounds (no audio assets). Played from the camera so they are always heard.</summary>
    public static class Sounds
    {
        static AudioClip click, error, whoosh;
        static AudioSource source;

        public static AudioClip Click => click ??= Tones("Click", new[] { 1400f }, 0.04f, 0.6f);
        public static AudioClip Error => error ??= Tones("Error", new[] { 220f, 180f }, 0.12f, 0.45f);
        public static AudioClip Whoosh => whoosh ??= Sweep("Whoosh", 0.6f);

        public static void Play(AudioClip clip, float volume = 1f)
        {
            if (source == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                source = cam.GetComponent<AudioSource>();
                if (source == null) source = cam.gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            source.PlayOneShot(clip, volume);
        }

        static AudioClip Tones(string name, float[] freqs, float noteLength, float volume)
        {
            const int rate = 44100;
            int perNote = (int)(rate * noteLength);
            var data = new float[perNote * freqs.Length + rate / 10];
            for (int n = 0; n < freqs.Length; n++)
                for (int i = 0; i < perNote + rate / 10 && n * perNote + i < data.Length; i++)
                {
                    float t = i / (float)rate;
                    float env = Mathf.Exp(-t * 9f) * Mathf.Clamp01(i / 200f);
                    data[n * perNote + i] += volume * env * Mathf.Sin(2 * Mathf.PI * freqs[n] * t);
                }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip Sweep(string name, float length)
        {
            const int rate = 44100;
            var data = new float[(int)(rate * length)];
            var rng = new System.Random(7);
            float lp = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)data.Length;
                float a = 0.02f + 0.25f * t; // filter opens: rising "elevator" whoosh
                lp += a * ((float)rng.NextDouble() * 2 - 1 - lp);
                data[i] = lp * 0.6f * Mathf.Sin(Mathf.PI * t);
            }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
