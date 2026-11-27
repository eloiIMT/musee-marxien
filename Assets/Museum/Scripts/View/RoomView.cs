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
        float floorTile;         // metres per repeat of the floor texture; 0 = material's own tiling
        RoomCeiling ceiling;     // life-size museum only
        float holeHalf;
        Vector2 holeCentre;
        TextMesh label;
        float wallThickness, lowWallHeight, labelHeight;
        bool expanded = true;
        readonly Dictionary<string, GameObject> artworks = new();
        // Plan (2D) view: flat footprint of each artwork on the floor, shown while the room is collapsed.
        static readonly Color MarkerColor = new Color(1f, 0.72f, 0.2f);
        const float MarkerMinSize = 0.3f, MarkerThickness = 0.04f;
        Transform markersRoot;
        Material markerMaterial;
        readonly Dictionary<string, Transform> markers = new();
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
                if (markersRoot != null) markersRoot.gameObject.SetActive(!value);
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
            SyncMarkers();
            ApplyWarnings();
        }

        /// <summary>
        /// Organizer plan: while collapsed, draw each artwork as a flat footprint on the floor (top view of its meshes).
        /// Off by default (immersive museum).
        /// </summary>
        public void EnablePlanMarkers(Material material)
        {
            if (markersRoot != null || material == null) return;
            markerMaterial = material;
            markersRoot = new GameObject("Artwork Markers (plan)").transform;
            markersRoot.SetParent(transform, false);
            markersRoot.gameObject.SetActive(!expanded);
            SyncMarkers();
            ApplyWarnings();
        }

        void SyncMarkers()
        {
            if (markersRoot == null) return;
            foreach (var pair in artworks)
            {
                if (pair.Value == null) continue;
                if (!markers.TryGetValue(pair.Key, out var marker) || marker == null)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Marker";
                    Destroy(go.GetComponent<Collider>()); // never takes the ray from the floor
                    var r = go.GetComponent<Renderer>();
                    r.sharedMaterial = markerMaterial;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    marker = go.transform;
                    marker.SetParent(markersRoot, false);
                    markers[pair.Key] = marker;
                }
                marker.name = "Marker · " + pair.Value.name;
                PlaceMarker(marker, pair.Value.transform);
            }
            foreach (var id in new List<string>(markers.Keys))
            {
                if (artworks.ContainsKey(id)) continue;
                if (markers[id] != null) Destroy(markers[id].gameObject);
                markers.Remove(id);
            }
        }

        /// <summary>Footprint of the artwork's meshes, in its own frame (works while hidden), laid flat on the floor.</summary>
        static void PlaceMarker(Transform marker, Transform art)
        {
            var toArt = art.worldToLocalMatrix;
            bool any = false;
            var bounds = new Bounds();
            foreach (var mf in art.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = toArt * mf.transform.localToWorldMatrix;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { bounds = new Bounds(corner, Vector3.zero); any = true; }
                    else bounds.Encapsulate(corner);
                }
            }
            var scale = art.localScale;
            var size = Vector3.Scale(bounds.size, scale);
            var centre = art.localPosition + art.localRotation * Vector3.Scale(bounds.center, scale);
            float yaw = MuseumGeometry.Yaw(art.localRotation);
            marker.SetLocalPositionAndRotation(new Vector3(centre.x, MarkerThickness / 2f + 0.005f, centre.z),
                                               Quaternion.Euler(0f, yaw, 0f));
            // Footprint measured in the artwork's frame: its yaw only (artworks stand upright).
            marker.localScale = new Vector3(Mathf.Max(Mathf.Abs(size.x), MarkerMinSize), MarkerThickness,
                                            Mathf.Max(Mathf.Abs(size.z), MarkerMinSize));
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

        /// <summary>
        /// Repeat the floor material's texture every <paramref name="metres"/> metres, whatever the room's size (the
        /// floor is a scaled cube); 0 keeps the material's tiling.
        /// </summary>
        public void SetFloorTextureTile(float metres)
        {
            floorTile = Mathf.Max(0f, metres);
            Layout();
        }

        /// <summary>Give the room a moulded ceiling with a skylight (life-size museum); shown while expanded.</summary>
        public void EnableCeiling(CeilingStyle style)
        {
            if (ceiling != null || style == null) return;
            ceiling = new GameObject("Plafond").AddComponent<RoomCeiling>();
            ceiling.transform.SetParent(transform, false);
            ceiling.Init(style);
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
                if (markers.TryGetValue(pair.Key, out var marker) && marker != null)
                    Tint.Set(marker.GetComponent<Renderer>(), warn ? WarningColor : MarkerColor);
            }
        }

        void Layout()
        {
            if (Data == null) return;
            float w = Data.width, l = Data.length, e = wallThickness;
            float h = expanded ? Data.height : Mathf.Min(lowWallHeight, Data.height);

            PlaceFloor(floor, new Vector3(0, -FloorThickness / 2, 0), new Vector3(w, FloorThickness, l));
            if (holeHalf > 0f && holeParts != null)
            {
                // opening [x0, x1] x [z0, z1], kept inside the floor
                float s = Mathf.Min(holeHalf, w / 2f - 0.05f, l / 2f - 0.05f), y = -FloorThickness / 2;
                float cx = Mathf.Clamp(holeCentre.x, -w / 2f + s + 0.05f, w / 2f - s - 0.05f);
                float cz = Mathf.Clamp(holeCentre.y, -l / 2f + s + 0.05f, l / 2f - s - 0.05f);
                float x0 = cx - s, x1 = cx + s, z0 = cz - s, z1 = cz + s;
                PlaceFloor(holeParts[0], new Vector3((-w / 2f + x0) / 2f, y, 0), new Vector3(x0 + w / 2f, FloorThickness, l));
                PlaceFloor(holeParts[1], new Vector3((x1 + w / 2f) / 2f, y, 0), new Vector3(w / 2f - x1, FloorThickness, l));
                PlaceFloor(holeParts[2], new Vector3(cx, y, (z1 + l / 2f) / 2f), new Vector3(2f * s, FloorThickness, l / 2f - z1));
                PlaceFloor(holeParts[3], new Vector3(cx, y, (-l / 2f + z0) / 2f), new Vector3(2f * s, FloorThickness, z0 + l / 2f));
            }
            Place(wallN, new Vector3(0, h / 2, l / 2), new Vector3(w + e, h, e));
            Place(wallS, new Vector3(0, h / 2, -l / 2), new Vector3(w + e, h, e));
            Place(wallE, new Vector3(w / 2, h / 2, 0), new Vector3(e, h, l + e));
            Place(wallW, new Vector3(-w / 2, h / 2, 0), new Vector3(e, h, l + e));
            if (ceiling != null)
            {
                ceiling.gameObject.SetActive(expanded);
                if (expanded) ceiling.Build(w - e, l - e, h);
            }
            if (label != null)
            {
                label.transform.localPosition = new Vector3(0, h + 0.8f, 0);
                Labels.SetWorldHeight(label, labelHeight);
            }
        }

        void PlaceFloor(Transform t, Vector3 localPosition, Vector3 size)
        {
            Place(t, localPosition, size);
            if (floorTile > 0f) Tint.SetTiling(t.GetComponent<Renderer>(), localPosition, size, floorTile);
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
