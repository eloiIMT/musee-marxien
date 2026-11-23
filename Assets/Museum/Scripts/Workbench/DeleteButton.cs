using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Trash icon on the gizmo (flat, always facing the camera): first trigger click asks for confirmation
    /// (shows how many artworks will be lost) and turns the icon into a white check mark on the same red disc; a second click within 3 s
    /// deletes the room. The icon grows on hover and pulses while confirmation is pending.
    /// </summary>
    public class DeleteButton : MonoBehaviour
    {
        const float ConfirmWindow = 3f;
        const float HoverScale = 1.2f, PulseAmount = 0.15f, PulseSpeed = 10f;

        static Material iconMaterial, confirmMaterial;

        Maquette maquette;
        Clickable clickable;
        Transform icon;
        Renderer iconRenderer;
        string pendingRoomId;
        float deadline;

        bool Pending => pendingRoomId != null && pendingRoomId == maquette.SelectedRoomId && Time.time <= deadline;

        /// <summary>Builds a 1×1 trash icon centred on its pivot, under <paramref name="parent"/>.</summary>
        public static DeleteButton Create(Transform parent, Maquette owner)
        {
            // Root: scaled by the gizmo, holds the collider and faces the camera.
            var root = new GameObject("Delete");
            root.transform.SetParent(parent, false);
            root.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.05f);
            root.AddComponent<Billboard>();

            // Child quad: scaled for hover feedback without fighting the gizmo's scale.
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Icon";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            quad.GetComponent<Renderer>().sharedMaterial = IconMaterial();

            var button = root.AddComponent<DeleteButton>();
            button.icon = quad.transform;
            button.iconRenderer = quad.GetComponent<Renderer>();
            button.Init(owner);
            return button;
        }

        void Init(Maquette owner)
        {
            maquette = owner;
            clickable = gameObject.AddComponent<Clickable>();
            clickable.Clicked += (_, _) => OnClick();
        }

        void Update()
        {
            // After the first click the trash turns into a red "confirm" check mark until the window closes.
            var material = Pending ? ConfirmMaterial() : IconMaterial();
            if (iconRenderer.sharedMaterial != material) iconRenderer.sharedMaterial = material;
            float s = Pending ? 1f + PulseAmount * (0.5f + 0.5f * Mathf.Sin(Time.time * PulseSpeed))
                    : clickable.IsHovered ? HoverScale : 1f;
            icon.localScale = Vector3.one * s;
        }

        void OnClick()
        {
            var id = maquette.SelectedRoomId;
            if (id == null) return;
            var room = maquette.Api.GetRoom(id);
            if (room == null) return;

            if (pendingRoomId == id && Time.time <= deadline)
            {
                pendingRoomId = null;
                maquette.SelectRoom(null); // detach the gizmo before the room view is destroyed
                maquette.Api.DeleteRoom(id);
                maquette.Toast($"« {room.name} » supprimée");
                return;
            }
            pendingRoomId = id;
            deadline = Time.time + ConfirmWindow;
            int count = maquette.Api.GetArtworksInRoom(id).Count;
            maquette.Toast($"Supprimer « {room.name} »{(count > 0 ? $" et ses {count} œuvre(s)" : "")} ?\nCliquez sur la coche pour confirmer", true);
        }

        // ---------- Icon texture (drawn in code, no asset needed) ----------

        // Unity null checks (not ??=): a material destroyed with the scene must be rebuilt, not reused.
        static Material IconMaterial()
        {
            if (iconMaterial == null) iconMaterial = CutoutMaterial(DrawIcon(128, new Color(0.9f, 0.15f, 0.15f, 1f), Sample), "Trash Icon (runtime)");
            return iconMaterial;
        }

        /// <summary>White check mark on the same red disc as the trash.</summary>
        static Material ConfirmMaterial()
        {
            if (confirmMaterial == null) confirmMaterial = CutoutMaterial(DrawIcon(128, new Color(0.9f, 0.15f, 0.15f, 1f), SampleCheck), "Confirm Icon (runtime)");
            return confirmMaterial;
        }

        static Material CutoutMaterial(Texture2D tex, string materialName)
        {
            Material material;
            var urp = Shader.Find("Universal Render Pipeline/Unlit");
            if (urp != null)
            {
                material = new Material(urp) { name = materialName };
                material.SetTexture("_BaseMap", tex);
                // Alpha clipping cuts the transparent corners around the disc.
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.5f);
                material.EnableKeyword("_ALPHATEST_ON");
            }
            else
            {
                material = new Material(Shader.Find("Unlit/Transparent Cutout")) { name = materialName };
            }
            material.mainTexture = tex;
            return material;
        }

        /// <summary>White check mark on a disc of <paramref name="bg"/>.</summary>
        static Color SampleCheck(float u, float v, Color bg, Color fg)
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            if (dx * dx + dy * dy > 0.48f * 0.48f) return Color.clear;
            var p = new Vector2(u, v);
            const float half = 0.055f;
            bool OnSegment(Vector2 a, Vector2 b)
            {
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                return (a + t * ab - p).sqrMagnitude <= half * half;
            }
            var corner = new Vector2(0.43f, 0.3f);
            return OnSegment(new Vector2(0.26f, 0.5f), corner) || OnSegment(corner, new Vector2(0.75f, 0.7f)) ? fg : bg;
        }

        /// <summary>White icon on a disc of <paramref name="bg"/>, 4× supersampled for smooth edges.</summary>
        static Texture2D DrawIcon(int size, Color bg, System.Func<float, float, Color, Color, Color> sample)
        {
            var fg = Color.white;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "GizmoIcon" };
            var pixels = new Color[size * size];
            const int ss = 4;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color sum = Color.clear;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                {
                    float u = (x + (sx + 0.5f) / ss) / size, v = (y + (sy + 0.5f) / ss) / size;
                    sum += sample(u, v, bg, fg);
                }
                pixels[y * size + x] = sum / (ss * ss);
            }
            tex.SetPixels(pixels);
            tex.Apply(true, true);
            return tex;
        }

        static Color Sample(float u, float v, Color bg, Color fg)
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            if (dx * dx + dy * dy > 0.48f * 0.48f) return Color.clear;

            bool Rect(float x0, float x1, float y0, float y1) => u >= x0 && u <= x1 && v >= y0 && v <= y1;

            if (Rect(0.42f, 0.58f, 0.70f, 0.77f) && !Rect(0.46f, 0.54f, 0.70f, 0.735f)) return fg; // handle
            if (Rect(0.26f, 0.74f, 0.63f, 0.69f)) return fg;                                      // lid
            if (v >= 0.24f && v <= 0.59f)                                                         // body (tapered)
            {
                float half = Mathf.Lerp(0.16f, 0.20f, (v - 0.24f) / 0.35f);
                if (Mathf.Abs(dx) <= half)
                {
                    bool slit = v >= 0.31f && v <= 0.52f &&
                                (Mathf.Abs(u - 0.42f) < 0.02f || Mathf.Abs(u - 0.5f) < 0.02f || Mathf.Abs(u - 0.58f) < 0.02f);
                    return slit ? bg : fg;
                }
            }
            return bg;
        }
    }
}
