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
        Transform[] holeParts;   // floor around the elevator opening (immersive museum only)
        float holeHalf;
        Vector2 holeCentre;
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

        /// <summary>Height of the walls as currently drawn (full when expanded, low plan walls otherwise).</summary>
        public float DrawnWallHeight => Data == null ? 0f : expanded ? Data.height : Mathf.Min(lowWallHeight, Data.height);

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

        /// <summary>
        /// Square opening of half-size <paramref name="halfSize"/> in the floor, centred on room-local (x, z)
        /// <paramref name="centre"/> (elevator well, <see cref="ElevatorRide"/>); 0 closes it. The floor is then drawn and
        /// collided as four slabs around the opening.
        /// </summary>
        public void SetFloorHole(Vector2 centre, float halfSize)
        {
            if (Mathf.Approximately(halfSize, holeHalf) && centre == holeCentre) return;
            holeHalf = Mathf.Max(0f, halfSize);
            holeCentre = centre;
            bool open = holeHalf > 0f;
            if (open && holeParts == null)
            {
                var material = floor.GetComponent<Renderer>().sharedMaterial;
                holeParts = new Transform[4];
                for (int i = 0; i < 4; i++) holeParts[i] = CreatePart("Floor (around elevator) " + i, material, true);
            }
            floor.GetComponent<Renderer>().enabled = !open;
            floor.GetComponent<Collider>().enabled = !open;
            if (holeParts != null) foreach (var p in holeParts) p.gameObject.SetActive(open);
            Layout();
        }

        /// <summary>Replace the walls' material (e.g. translucent on the miniature); they then cast no shadow.</summary>
        public void SetWallMaterial(Material material)
        {
            foreach (var w in new[] { wallN, wallS, wallE, wallW })
            {
                var r = w.GetComponent<Renderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
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
            if (holeHalf > 0f && holeParts != null)
            {
                // opening [x0, x1] x [z0, z1], kept inside the floor
                float s = Mathf.Min(holeHalf, w / 2f - 0.05f, l / 2f - 0.05f), y = -FloorThickness / 2;
                float cx = Mathf.Clamp(holeCentre.x, -w / 2f + s + 0.05f, w / 2f - s - 0.05f);
                float cz = Mathf.Clamp(holeCentre.y, -l / 2f + s + 0.05f, l / 2f - s - 0.05f);
                float x0 = cx - s, x1 = cx + s, z0 = cz - s, z1 = cz + s;
                Place(holeParts[0], new Vector3((-w / 2f + x0) / 2f, y, 0), new Vector3(x0 + w / 2f, FloorThickness, l));
                Place(holeParts[1], new Vector3((x1 + w / 2f) / 2f, y, 0), new Vector3(w / 2f - x1, FloorThickness, l));
                Place(holeParts[2], new Vector3(cx, y, (z1 + l / 2f) / 2f), new Vector3(2f * s, FloorThickness, l / 2f - z1));
                Place(holeParts[3], new Vector3(cx, y, (-l / 2f + z0) / 2f), new Vector3(2f * s, FloorThickness, z0 + l / 2f));
            }
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
