using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Visual representation of one <see cref="Room"/>: a floor, four walls and its artworks.
    /// Built procedurally so the same code works at any scale (immersive or miniature).
    /// Never edits the model: it only mirrors the snapshot passed to <see cref="Apply"/>.
    /// </summary>
    public class RoomView : MonoBehaviour
    {
        const float FloorThickness = 0.05f;
        static readonly Color WarningColor = new Color(1f, 0.15f, 0.1f);

        public string RoomId { get; private set; }
        public Room Data { get; private set; }
        public Transform ArtworksRoot => artworksRoot;
        public GameObject Floor => floor.gameObject;
        public IReadOnlyDictionary<string, GameObject> Artworks => artworks;

        Transform floor, wallN, wallS, wallE, wallW, artworksRoot;
        TextMesh label;
        float wallThickness, lowWallHeight, labelHeight;
        bool expanded = true;
        readonly Dictionary<string, GameObject> artworks = new();
        readonly HashSet<string> warnings = new();

        /// <summary>Full-height walls with artworks visible (true) or low plan walls without artworks (false).</summary>
        public bool Expanded
        {
            get => expanded;
            set
            {
                if (expanded == value) return;
                expanded = value;
                artworksRoot.gameObject.SetActive(value);
                Layout();
            }
        }

        /// <param name="labelHeight">World height of the name label in metres; 0 = no label.</param>
        public void Init(string roomId, Material floorMaterial, Material wallMaterial, float wallThickness, float lowWallHeight,
                         bool wallColliders, float labelHeight)
        {
            RoomId = roomId;
            this.wallThickness = wallThickness;
            this.lowWallHeight = lowWallHeight;
            this.labelHeight = labelHeight;
            floor = CreatePart("Floor", floorMaterial, true);
            wallN = CreatePart("Wall N", wallMaterial, wallColliders);
            wallS = CreatePart("Wall S", wallMaterial, wallColliders);
            wallE = CreatePart("Wall E", wallMaterial, wallColliders);
            wallW = CreatePart("Wall W", wallMaterial, wallColliders);
            artworksRoot = new GameObject("Artworks").transform;
            artworksRoot.SetParent(transform, false);
            if (labelHeight > 0) label = Labels.Create(transform, "", labelHeight, Color.black);
        }

        /// <summary>Mirror a room snapshot. Position is local to the museum root, origin at floor centre.</summary>
        public void Apply(Room room)
        {
            Data = room;
            name = "Room · " + room.name;
            transform.SetLocalPositionAndRotation(room.position, room.rotation);
            if (label != null) label.text = room.name;
            Layout();
        }

        /// <summary>Reconcile artwork objects with the artworks of this room (create / update / destroy).</summary>
        public void SyncArtworks(IEnumerable<ArtworkInstance> roomArtworks, MuseumBackend backend)
        {
            var seen = new HashSet<string>();
            foreach (var a in roomArtworks)
            {
                if (!artworks.TryGetValue(a.id, out var go) || go == null)
                {
                    go = backend.InstantiateArtwork(a.id, artworksRoot);
                    artworks[a.id] = go;
                }
                go.name = a.title;
                go.transform.SetLocalPositionAndRotation(a.localPosition, a.localRotation);
                go.transform.localScale = a.localScale;
                seen.Add(a.id);
            }
            foreach (var id in new List<string>(artworks.Keys))
            {
                if (seen.Contains(id)) continue;
                if (artworks[id] != null) Destroy(artworks[id]);
                artworks.Remove(id);
            }
            ApplyWarnings();
        }

        /// <summary>Floor highlight (hover / selection / target); null restores the material colour.</summary>
        public void SetFloorTint(Color? color)
        {
            var r = floor.GetComponent<Renderer>();
            if (color.HasValue) Tint.Set(r, color.Value); else Tint.Clear(r);
        }

        /// <summary>
        /// Preview override (e.g. during a resize): exactly these artworks are drawn in red. Null returns to
        /// the default rule: artworks outside the current walls are red (the backend allows it, we show it).
        /// </summary>
        public void SetArtworkWarnings(IEnumerable<string> previewIds)
        {
            previewing = previewIds != null;
            warnings.Clear();
            if (previewIds != null) warnings.UnionWith(previewIds);
            ApplyWarnings();
        }

        bool previewing;

        /// <summary>Organizer only: highlight artworks outside the walls. Off for the visitor.</summary>
        public bool ShowWarnings { get; set; }

        void ApplyWarnings()
        {
            foreach (var pair in artworks)
            {
                if (pair.Value == null) continue;
                bool warn = previewing
                    ? warnings.Contains(pair.Key)
                    : ShowWarnings && Data != null && !MuseumGeometry.IsInside(Data.width, Data.length, pair.Value.transform.localPosition);
                foreach (var r in pair.Value.GetComponentsInChildren<Renderer>())
                    if (warn) Tint.Set(r, WarningColor); else Tint.Clear(r);
            }
        }

        void Layout()
        {
            if (Data == null) return;
            float w = Data.width, l = Data.length, e = wallThickness;
            float h = expanded ? Data.height : Mathf.Min(lowWallHeight, Data.height);

            Place(floor, new Vector3(0, -FloorThickness / 2, 0), new Vector3(w, FloorThickness, l));
            Place(wallN, new Vector3(0, h / 2, l / 2), new Vector3(w + e, h, e));
            Place(wallS, new Vector3(0, h / 2, -l / 2), new Vector3(w + e, h, e));
            Place(wallE, new Vector3(w / 2, h / 2, 0), new Vector3(e, h, l + e));
            Place(wallW, new Vector3(-w / 2, h / 2, 0), new Vector3(e, h, l + e));
            if (label != null)
            {
                label.transform.localPosition = new Vector3(0, h + 0.8f, 0);
                Labels.SetWorldHeight(label, labelHeight);
            }
        }

        static void Place(Transform t, Vector3 localPosition, Vector3 size)
        {
            t.localPosition = localPosition;
            t.localScale = size;
        }

        Transform CreatePart(string partName, Material material, bool withCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            go.transform.SetParent(transform, false);
            if (!withCollider) Destroy(go.GetComponent<Collider>());
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }
    }
}
