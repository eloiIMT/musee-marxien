using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Mirrors the whole museum under this transform. Place one at scale 1 for the visitor,
    /// one scaled down on the workbench for the organizer: same code, same data.
    /// Listens to <see cref="MuseumApi.Changed"/> and reconciles views by stable id
    /// (create missing, update existing, destroy removed) so held / selected objects survive edits.
    /// </summary>
    public class MuseumView : MonoBehaviour
    {
        [SerializeField] MuseumBackend backend;

        [Header("Materials (optional, default material if empty)")]
        [SerializeField] Material floorMaterial;
        [SerializeField] Material wallMaterial;
        [SerializeField] Material cableMaterial;
        [Tooltip("Metres per repeat of the floor material's texture (0 = stretched over each room).")]
        [SerializeField] float floorTextureTile;

        [Header("Ceiling (life-size museum)")]
        [Tooltip("Moulded ceiling with a frosted skylight letting daylight in. Off on the miniature.")]
        [SerializeField] bool buildCeilings;
        [SerializeField] CeilingStyle ceiling = new();

        [Header("Doors (life-size museum)")]
        [Tooltip("Arched passages at the connections: walking into one leads to the connected room.")]
        [SerializeField] bool buildDoors;
        [SerializeField] DoorStyle doorStyle = new();

        [Header("Geometry (museum metres)")]
        [SerializeField] float wallThickness = 0.1f;
        [SerializeField] float lowWallHeight = 0.3f;
        [SerializeField] bool startExpanded = true;
        [Tooltip("Off on the miniature: the ray then reaches floors and handles behind walls.")]
        [SerializeField] bool wallColliders = true;
        [Tooltip("World height of room name labels in metres (0 = none).")]
        [SerializeField] float labelHeight;
        [Tooltip("Organizer: artworks outside their room's walls drawn in red.")]
        [SerializeField] bool showArtworkWarnings;

        [Header("Connections")]
        [Tooltip("Line width in museum metres (compensated for this object's scale).")]
        [SerializeField] float cableWidth = 0.1f;
        [SerializeField] float cableHeight = 0.2f;
        [Tooltip("Off on the life-size museum: connections are an editing aid, the visitor never sees them.")]
        [SerializeField] bool showCables = true;

        readonly Dictionary<string, RoomView> rooms = new();
        readonly Dictionary<string, LineRenderer> cables = new();
        readonly Dictionary<string, Transform> doors = new(); // "connectionKey#roomId"
        readonly Dictionary<string, DoorPortal> portals = new(); // "connectionKey#roomId"
        Transform roomsRoot, cablesRoot, portalsRoot;

        /// <summary>Raised after every refresh, once all views match the model.</summary>
        public event Action Refreshed;

        public MuseumBackend Backend => backend;
        public IReadOnlyDictionary<string, RoomView> Rooms => rooms;
        /// <summary>Connection lines by <see cref="ConnectionKey"/> (door to door); points are in museum coordinates.</summary>
        public IReadOnlyDictionary<string, LineRenderer> Cables => cables;
        public RoomView GetRoomView(string roomId) => roomId != null && rooms.TryGetValue(roomId, out var rv) ? rv : null;
        /// <summary>Life-size door of connection <paramref name="connectionKey"/> in room <paramref name="roomId"/> (null if none).</summary>
        public DoorPortal GetDoor(string connectionKey, string roomId) =>
            portals.TryGetValue(connectionKey + "#" + roomId, out var p) && p != null ? p : null;
        /// <summary>Life-size doors, by "connectionKey#roomId".</summary>
        public IReadOnlyDictionary<string, DoorPortal> Doors => portals;

        /// <summary>Stable key for an undirected connection: A-B and B-A give the same key.</summary>
        public static string ConnectionKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        public static (string a, string b) SplitKey(string key) { var p = key.Split('|'); return (p[0], p[1]); }

        void Awake()
        {
            if (backend == null) backend = FindAnyObjectByType<MuseumBackend>();
            roomsRoot = CreateChild("Rooms");
            cablesRoot = CreateChild("Connections");
            cablesRoot.gameObject.SetActive(showCables);
            if (buildDoors) portalsRoot = CreateChild("Doors");
        }

        void OnEnable()
        {
            if (backend == null) { Debug.LogError("MuseumView: no MuseumBackend in scene.", this); return; }
            backend.Api.Changed += OnChanged;
            Refresh();
        }

        void OnDisable()
        {
            if (backend != null) backend.Api.Changed -= OnChanged;
        }

        void OnChanged(MuseumChange change) => Refresh();

        /// <summary>Re-read the model and bring every view in line with it.</summary>
        public void Refresh()
        {
            var data = backend.Api.Data; // snapshot copy: read only
            SyncRooms(data);
            SyncCables(data);
            Refreshed?.Invoke();
        }

        void SyncRooms(Museum data)
        {
            var byRoom = data.artworks.ToLookup(a => a.roomId);
            var seen = new HashSet<string>();
            foreach (var room in data.rooms)
            {
                if (!rooms.TryGetValue(room.id, out var rv) || rv == null)
                {
                    rv = new GameObject().AddComponent<RoomView>();
                    rv.transform.SetParent(roomsRoot, false);
                    rv.Init(room.id, floorMaterial, wallMaterial, wallThickness, lowWallHeight, wallColliders, labelHeight);
                    rv.Expanded = startExpanded;
                    rv.SetFloorTextureTile(floorTextureTile);
                    if (buildCeilings) rv.EnableCeiling(ceiling);
                    rv.ShowWarnings = showArtworkWarnings;
                    rooms[room.id] = rv;
                }
                rv.Apply(room);
                rv.SyncArtworks(byRoom[room.id], backend);
                seen.Add(room.id);
            }
            foreach (var id in new List<string>(rooms.Keys))
            {
                if (seen.Contains(id)) continue;
                if (rooms[id] != null) Destroy(rooms[id].gameObject);
                rooms.Remove(id);
            }
        }

        /// <summary>
        /// Connections drawn as a door on the facing wall of each room (<see cref="ConnectionLayout"/>: computed,
        /// spread when several share a wall) and a thin line between the two doors, just above the floor.
        /// </summary>
        void SyncCables(Museum data)
        {
            var byId = data.rooms.ToDictionary(r => r.id);
            var layout = ConnectionLayout.Compute(data);
            var up = Vector3.up * cableHeight;
            float doorHeight = lowWallHeight + 0.15f;
            // One size for every life-size door (coherent everywhere): what the lowest room / narrowest slot allows.
            float doorScale = float.MaxValue;
            foreach (var (da0, db0) in layout.Values)
                doorScale = Mathf.Min(doorScale, DoorPortal.ScaleFor(doorStyle, byId[da0.RoomId], da0, byId[db0.RoomId], db0));
            foreach (var pair in layout)
            {
                var key = pair.Key;
                var (da, db) = pair.Value;
                var ra = byId[da.RoomId];
                var rb = byId[db.RoomId];
                if (!cables.TryGetValue(key, out var line) || line == null)
                {
                    line = CreateCable(key);
                    cables[key] = line;
                }
                // From the outer face of one door to the outer face of the other.
                var pa = MuseumSpatial.ToMuseum(ra, da.Local + da.Normal * (wallThickness / 2f));
                var pb = MuseumSpatial.ToMuseum(rb, db.Local + db.Normal * (wallThickness / 2f));
                line.SetPosition(0, pa + up);
                line.SetPosition(1, pb + up);
                line.widthMultiplier = cableWidth * transform.lossyScale.x;
                PlaceDoor(key, ra, da, doorHeight);
                PlaceDoor(key, rb, db, doorHeight);
                if (portalsRoot != null)
                {
                    PlacePortal(key, ra, da, rb, doorScale);
                    PlacePortal(key, rb, db, ra, doorScale);
                }
            }
            foreach (var key in new List<string>(cables.Keys))
            {
                if (layout.ContainsKey(key)) continue;
                if (cables[key] != null) Destroy(cables[key].gameObject);
                cables.Remove(key);
            }
            foreach (var portalKey in new List<string>(portals.Keys))
            {
                if (layout.ContainsKey(portalKey.Substring(0, portalKey.LastIndexOf('#')))) continue;
                if (portals[portalKey] != null) Destroy(portals[portalKey].gameObject);
                portals.Remove(portalKey);
            }
            foreach (var doorKey in new List<string>(doors.Keys))
            {
                var (a, b) = SplitKey(doorKey.Substring(0, doorKey.LastIndexOf('#')));
                if (layout.ContainsKey(ConnectionKey(a, b))) continue;
                if (doors[doorKey] != null) Destroy(doors[doorKey].gameObject);
                doors.Remove(doorKey);
            }
        }

        /// <summary>Door marker: a block straddling the wall at the door, a little taller than the plan's low walls.</summary>
        void PlaceDoor(string connectionKey, Room room, Door door, float height)
        {
            var doorKey = connectionKey + "#" + room.id;
            if (!doors.TryGetValue(doorKey, out var t) || t == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Door " + doorKey;
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(cablesRoot, false);
                var r = go.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (cableMaterial != null) r.sharedMaterial = cableMaterial;
                doors[doorKey] = t = go.transform;
            }
            float h = Mathf.Min(height, room.height);
            t.localPosition = MuseumSpatial.ToMuseum(room, door.Local) + Vector3.up * (h / 2f);
            t.localRotation = room.rotation * (door.AlongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f));
            t.localScale = new Vector3(door.Width, h, wallThickness * 1.8f);
        }

        /// <summary>Life-size door: against the inner face of the wall, in the middle of the layout's door slot.</summary>
        void PlacePortal(string connectionKey, Room room, Door door, Room other, float scale)
        {
            var portalKey = connectionKey + "#" + room.id;
            if (!portals.TryGetValue(portalKey, out var portal) || portal == null)
            {
                portal = new GameObject().AddComponent<DoorPortal>();
                portal.transform.SetParent(portalsRoot, false);
                portal.Init(this, doorStyle, connectionKey, room.id, other.id);
                portals[portalKey] = portal;
            }
            var innerFace = door.Local - door.Normal * (wallThickness / 2f);
            portal.Apply(MuseumSpatial.ToMuseum(room, innerFace), room.rotation * Quaternion.LookRotation(door.Normal), scale, other.name, room);
        }

        LineRenderer CreateCable(string key)
        {
            var go = new GameObject("Cable " + key);
            go.transform.SetParent(cablesRoot, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false; // points in museum coordinates, follow this transform
            line.positionCount = 2;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (cableMaterial != null) line.sharedMaterial = cableMaterial;
            return line;
        }

        Transform CreateChild(string childName)
        {
            var t = new GameObject(childName).transform;
            t.SetParent(transform, false);
            return t;
        }
    }
}
