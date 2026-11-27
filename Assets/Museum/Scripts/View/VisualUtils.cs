using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>Per-renderer colour override without duplicating materials (URP _BaseColor + legacy _Color).</summary>
    public static class Tint
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Color_ = Shader.PropertyToID("_Color");
        static MaterialPropertyBlock block;

        public static void Set(Renderer r, Color c)
        {
            if (r == null) return;
            block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(BaseColor, c);
            block.SetColor(Color_, c);
            r.SetPropertyBlock(block);
        }

        /// <summary>Removes the colour override; a texture tiling set by <see cref="SetTiling"/> is kept.</summary>
        public static void Clear(Renderer r)
        {
            if (r == null) return;
            block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            bool tiled = block.HasVector(BaseMapST);
            var st = tiled ? block.GetVector(BaseMapST) : Vector4.zero;
            block.Clear();
            if (tiled) block.SetVector(BaseMapST, st);
            r.SetPropertyBlock(tiled ? block : null);
        }

        static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

        /// <summary>
        /// World-scaled texture on the top face of a scaled built-in cube (floor slab): one texture repeat every
        /// <paramref name="tile"/> metres, continuous between slabs of the same room. The built-in cube's top face maps
        /// u = 0.5 - x, v = 0.5 - z.
        /// </summary>
        public static void SetTiling(Renderer r, Vector3 centre, Vector3 size, float tile)
        {
            if (r == null || tile <= 0f) return;
            block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetVector(BaseMapST, new Vector4(size.x / tile, size.z / tile,
                                                   -(centre.x + size.x / 2f) / tile, -(centre.z + size.z / 2f) / tile));
            r.SetPropertyBlock(block);
        }
    }

    /// <summary>World-space text using the built-in font (no TextMeshPro setup needed).</summary>
    public static class Labels
    {
        static Font font;

        public static TextMesh Create(Transform parent, string text, float worldHeight, Color color, bool billboard = true)
        {
            font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            SetWorldHeight(tm, worldHeight);
            if (billboard) go.AddComponent<Billboard>();
            return tm;
        }

        /// <summary>Keep a constant size in world metres whatever the parent scale.</summary>
        public static void SetWorldHeight(TextMesh tm, float worldHeight)
        {
            var parentScale = tm.transform.parent != null ? tm.transform.parent.lossyScale.x : 1f;
            // With characterSize 1, a line is about fontSize / 10 units tall.
            tm.characterSize = 1f;
            tm.transform.localScale = Vector3.one * (worldHeight / (tm.fontSize / 10f) / Mathf.Max(parentScale, 1e-5f));
        }
    }

    /// <summary>Faces the main camera (readable labels from anywhere around the table).</summary>
    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);
        }
    }
}
