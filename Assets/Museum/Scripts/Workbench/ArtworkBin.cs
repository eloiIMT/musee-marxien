using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Blue bin on the storage table: the reserve of artworks not used in the museum.
    ///  - in: an artwork held in the hand (opened gacha capsule) let go in or just above the bin; or an artwork of the
    ///    plan dragged with the ray (<see cref="ArtworkHandle"/>) and released while the ray points at the bin: it
    ///    leaves the museum (and its tours)
    ///  - out: grab a miniature in the bin and release it over a room of the plan, as with a gacha capsule
    ///    (<see cref="GachaArtworkPlacer"/>); released anywhere else it goes back to its place in the bin
    /// The contents are saved in their own file (<see cref="ArtworkBinStore"/>). Lives at Spot_Bac under the Workbench
    /// (hidden in visitor mode): origin on the bin's bottom, front (-Z) towards the user. No collider: the bin must not
    /// stop the XRI rays aimed at the miniatures inside it, aiming is tested against <see cref="AimBox"/>.
    /// </summary>
    public sealed class ArtworkBin : MonoBehaviour
    {
        // Inside of the bin, local space (Assets/3D/Source~/build.py: BW 0.92, BD 0.4, BH 0.24, front slope).
        static readonly Vector3 InnerMin = new Vector3(-0.44f, 0.012f, -0.15f);
        static readonly Vector3 InnerMax = new Vector3(0.44f, 0.24f, 0.18f);
        /// <summary>A hand let go up to this high above the rim still drops into the bin.</summary>
        const float DropAbove = 0.3f;
        static readonly Bounds AimBox = new Bounds(new Vector3(0f, 0.13f, 0.01f), new Vector3(0.98f, 0.3f, 0.44f));
        const float MaxAimDistance = 8f;
        public const int Columns = 6, Rows = 3;
        const float LayerHeight = 0.09f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Color_ = Shader.PropertyToID("_Color");
        static readonly Color HighlightColor = new Color(0.35f, 0.65f, 1f);

        [Tooltip("The bin's mesh (Atelier/Bac_Oeuvres): its first material is lit up while an artwork is aimed at it.")]
        [SerializeField] Renderer binRenderer;

        public static ArtworkBin Active { get; private set; }

        public IReadOnlyList<StoredArtwork> Items => store.Items;

        ArtworkBinStore store = new();
        MuseumBackend backend;
        Maquette maquette;
        readonly Dictionary<string, GachaArtworkPlacer> miniatures = new();
        TextMesh label;
        MaterialPropertyBlock block;
        string hint;
        bool ready;

        void Awake()
        {
            if (TriggerRouter.Instance == null && FindAnyObjectByType<TriggerRouter>() == null)
                new GameObject("Trigger Router").AddComponent<TriggerRouter>();
        }

        void OnEnable() => Active = this;

        void OnDisable()
        {
            if (Active == this) Active = null;
            ShowHint(null);
        }

        void Start()
        {
            backend = FindAnyObjectByType<MuseumBackend>();
            if (backend == null) { Debug.LogError("The artwork bin needs a MuseumBackend.", this); return; }
            store = ArtworkBinStore.Load(ArtworkBinStore.DefaultPath, id => backend.Catalog.Find(id) != null, out var error);
            if (error != null) Debug.LogWarning("Artwork bin: " + error, this);
            label = Labels.Create(transform, "", 0.03f, Color.white);
            label.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            ready = true;
            foreach (var entry in store.Items) Spawn(entry);
            RefreshLabel();
        }

        void Update()
        {
            int i = 0;
            foreach (var entry in store.Items)
            {
                if (!miniatures.TryGetValue(entry.id, out var item) || item == null) { i++; continue; }
                if (!item.IsHeld) Lay(item, i);
                i++;
            }
        }

        // ---------------------------------------------------------------- queries

        /// <summary>Is a world point inside the bin, or just above it?</summary>
        public bool Contains(Vector3 world)
        {
            var p = transform.InverseTransformPoint(world);
            return p.x >= InnerMin.x && p.x <= InnerMax.x && p.z >= InnerMin.z && p.z <= InnerMax.z
                && p.y >= 0f && p.y <= InnerMax.y + DropAbove;
        }

        /// <summary>Does this world ray point at the bin?</summary>
        public bool IsAimedBy(Ray worldRay)
        {
            var local = new Ray(transform.InverseTransformPoint(worldRay.origin), transform.InverseTransformDirection(worldRay.direction));
            return AimBox.IntersectRay(local, out float distance) && distance <= MaxAimDistance;
        }

        /// <summary>Bottom centre of a miniature's place in the bin (local space): rows from the front, then layers.</summary>
        public static Vector3 SlotPosition(int index)
        {
            int perLayer = Columns * Rows;
            int layer = index / perLayer, k = index % perLayer;
            int column = k % Columns, row = k / Columns;
            var size = InnerMax - InnerMin;
            return new Vector3(InnerMin.x + (column + 0.5f) * size.x / Columns,
                               InnerMin.y + layer * LayerHeight,
                               InnerMin.z + (row + 0.5f) * size.z / Rows);
        }

        // ---------------------------------------------------------------- in

        /// <summary>An artwork that is not in the museum (gacha capsule) goes into the bin.</summary>
        public bool Store(ArtworkDefinition definition)
        {
            if (!ready || definition == null) return false;
            var entry = store.Add(definition.id, definition.title, definition.description);
            if (!Save(out var error))
            {
                store.Remove(entry.id);
                Toast("Bac non sauvegardé : " + error, true);
                return false;
            }
            Spawn(entry);
            RefreshLabel();
            Toast($"« {entry.title} » rangée dans le bac");
            return true;
        }

        /// <summary>An artwork placed in the museum leaves it for the bin (title and description kept).</summary>
        public bool StoreFromMuseum(string artworkId)
        {
            if (!ready) return false;
            var api = backend.Api;
            var art = api.GetArtwork(artworkId);
            if (art == null) return false;

            var entry = store.Add(art.definitionId, art.title, art.description);
            if (!Save(out var error))
            {
                store.Remove(entry.id);
                Toast("Bac non sauvegardé : " + error, true);
                return false;
            }
            api.RemoveArtwork(art.id);
            if (!backend.Save(out error))
            {
                // Put it back where it was (its tour entries are lost: they were pruned by RemoveArtwork).
                var back = api.AddArtwork(art.definitionId, art.roomId, art.localPosition);
                api.SetArtworkTransform(back.id, art.localPosition, art.localRotation, art.localScale);
                api.EditArtwork(back.id, art.title, art.description);
                store.Remove(entry.id);
                Save(out _);
                Toast("Musée non sauvegardé : " + error, true);
                return false;
            }
            Spawn(entry);
            RefreshLabel();
            Toast($"« {entry.title} » rangée dans le bac");
            return true;
        }

        // ---------------------------------------------------------------- out

        /// <summary>A miniature of the bin was placed in a room as <paramref name="artworkId"/>: give it its stored texts.</summary>
        public void ApplyStoredTexts(string entryId, string artworkId)
        {
            var entry = store.Find(entryId);
            var api = backend.Api;
            var art = api.GetArtwork(artworkId);
            if (entry == null || art == null) return;
            if (art.title != entry.title || art.description != entry.description)
                api.EditArtwork(artworkId, entry.title, entry.description);
        }

        /// <summary>The miniature is now in the museum (saved): it leaves the bin.</summary>
        public void Remove(string entryId)
        {
            miniatures.Remove(entryId);
            if (!store.Remove(entryId)) return;
            if (!Save(out var error)) Toast("Bac non sauvegardé : " + error, true);
            RefreshLabel();
        }

        // ---------------------------------------------------------------- feedback

        /// <summary>Lights the bin up with a message (an artwork of the plan is aimed at it); null puts it back.</summary>
        public void ShowHint(string message)
        {
            if (hint == message) return;
            hint = message;
            if (binRenderer != null && binRenderer.sharedMaterials.Length > 0)
            {
                block ??= new MaterialPropertyBlock();
                block.Clear();
                if (message != null)
                {
                    block.SetColor(BaseColor, HighlightColor);
                    block.SetColor(Color_, HighlightColor);
                }
                binRenderer.SetPropertyBlock(block, 0);
            }
            RefreshLabel();
        }

        void RefreshLabel()
        {
            if (label == null) return;
            int n = store.Items.Count;
            label.text = hint ?? (n == 0 ? "RÉSERVE\nDéposez ici les œuvres inutilisées"
                                         : $"RÉSERVE · {n} œuvre{(n > 1 ? "s" : "")}\nSaisir une œuvre : la replacer sur le plan");
            label.color = hint != null ? new Color(0.5f, 0.8f, 1f) : Color.white;
        }

        void Toast(string message, bool error = false)
        {
            if (maquette == null) maquette = FindAnyObjectByType<Maquette>();
            if (maquette != null) maquette.Toast(message, error);
            else if (error) Debug.LogWarning("[Bac] " + message, this);
        }

        bool Save(out string error) => store.TrySave(ArtworkBinStore.DefaultPath, out error);

        // ---------------------------------------------------------------- miniatures

        /// <summary>Grabbable miniature, same size as the artworks drawn on the plan (see <see cref="GachaArtworkPlacer"/>).</summary>
        void Spawn(StoredArtwork entry)
        {
            var definition = backend.Catalog.Find(entry.definitionId);
            if (definition == null || definition.prefab == null) return;

            var item = new GameObject("Réserve · " + entry.title);
            item.transform.SetParent(transform, false);
            var visual = Instantiate(definition.prefab, item.transform, false);
            visual.name = definition.title;
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            visual.transform.localScale = Vector3.one;
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            var body = item.AddComponent<Rigidbody>();
            body.isKinematic = true; // stays put in the bin; let go elsewhere it comes back (Update)
            body.useGravity = false;
            item.AddComponent<SphereCollider>();
            item.AddComponent<XRGrabInteractable>();
            item.AddComponent<GachaCapsuleTriggerGrab>();
            var placer = item.AddComponent<GachaArtworkPlacer>();
            placer.Init(definition, backend, visual.transform);
            placer.Open();
            placer.TakeFromBin(this, entry.id);
            miniatures[entry.id] = placer;
            Lay(placer, IndexOf(entry.id));
        }

        int IndexOf(string entryId)
        {
            for (int i = 0; i < store.Items.Count; i++)
                if (store.Items[i].id == entryId) return i;
            return 0;
        }

        /// <summary>Sits the miniature on its place (it follows the maquette's scale, so this is redone every frame).</summary>
        void Lay(GachaArtworkPlacer item, int index)
        {
            var size = item.VisualWorldSize;
            float scale = Mathf.Max(transform.lossyScale.x, 1e-5f);
            var t = item.transform;
            t.SetLocalPositionAndRotation(SlotPosition(index) + Vector3.up * (size.y / 2f / scale + 0.004f), Quaternion.identity);
            var sphere = item.GetComponent<SphereCollider>();
            float radius = Mathf.Max(0.035f, size.magnitude / 2f) / Mathf.Max(t.lossyScale.x, 1e-5f);
            if (sphere != null && !Mathf.Approximately(sphere.radius, radius)) sphere.radius = radius;
        }
    }
}
