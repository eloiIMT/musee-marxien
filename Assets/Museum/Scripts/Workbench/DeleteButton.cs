using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Trash icon on the gizmo (flat, always facing the camera): first trigger click asks for confirmation
    /// (shows how many artworks will be lost), a second click within 3 s deletes the room.
    /// The icon grows on hover and pulses while confirmation is pending.
    /// </summary>
    public class DeleteButton : MonoBehaviour
    {
        const float ConfirmWindow = 3f;
        const float HoverScale = 1.2f, PulseAmount = 0.15f, PulseSpeed = 10f;

        static Material iconMaterial;

        Maquette maquette;
        Clickable clickable;
        Transform icon;
        string pendingRoomId;
        float deadline;

        bool Pending => pendingRoomId != null && Time.time <= deadline;

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
            maquette.Toast($"Supprimer « {room.name} »{(count > 0 ? $" et ses {count} œuvre(s)" : "")} ?\nCliquez à nouveau pour confirmer", true);
        }

        // ---------- Icon texture (drawn in code, no asset needed) ----------

        static Material IconMaterial()
        {
            if (iconMaterial != null) return iconMaterial;
            var tex = DrawTrashIcon(128);
            var urp = Shader.Find("Universal Render Pipeline/Unlit");
            if (urp != null)
            {
                iconMaterial = new Material(urp) { name = "Trash Icon (runtime)" };
                iconMaterial.SetTexture("_BaseMap", tex);
                // Alpha clipping cuts the transparent corners around the disc.
                iconMaterial.SetFloat("_AlphaClip", 1f);
                iconMaterial.SetFloat("_Cutoff", 0.5f);
                iconMaterial.EnableKeyword("_ALPHATEST_ON");
            }
            else
            {
                iconMaterial = new Material(Shader.Find("Unlit/Transparent Cutout")) { name = "Trash Icon (runtime)" };
            }
            iconMaterial.mainTexture = tex;
            return iconMaterial;
        }

        /// <summary>White trash can on a red disc, 4× supersampled for smooth edges.</summary>
        static Texture2D DrawTrashIcon(int size)
        {
            var red = new Color(0.9f, 0.15f, 0.15f, 1f);
            var white = Color.white;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "TrashIcon" };
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
                    sum += Sample(u, v, red, white);
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
