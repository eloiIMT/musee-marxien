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
        [Tooltip("Cable width in museum metres (compensated for this object's scale).")]
        [SerializeField] float cableWidth = 0.15f;
        [SerializeField] float cableHeight = 0.2f;

        readonly Dictionary<string, RoomView> rooms = new();
        readonly Dictionary<string, LineRenderer> cables = new();
        Transform roomsRoot, cablesRoot;

        /// <summary>Raised after every refresh, once all views match the model.</summary>
        public event Action Refreshed;

        public MuseumBackend Backend => backend;
        public IReadOnlyDictionary<string, RoomView> Rooms => rooms;
        /// <summary>Connection cables by <see cref="ConnectionKey"/>; points are in museum coordinates.</summary>
        public IReadOnlyDictionary<string, LineRenderer> Cables => cables;
        public RoomView GetRoomView(string roomId) => roomId != null && rooms.TryGetValue(roomId, out var rv) ? rv : null;

        /// <summary>Stable key for an undirected connection: A-B and B-A give the same key.</summary>
        public static string ConnectionKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        public static (string a, string b) SplitKey(string key) { var p = key.Split('|'); return (p[0], p[1]); }

        void Awake()
        {
            if (backend == null) backend = FindAnyObjectByType<MuseumBackend>();
            roomsRoot = CreateChild("Rooms");
            cablesRoot = CreateChild("Connections");
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

        void OnChanged(MuseumChange change)
        {
            // Whole museum replaced (Load / Reset): session-only attachment points no longer apply.
            if (change.Entity == MuseumEntity.Museum && change.Operation == MuseumOperation.Reset) ConnectionAnchors.Clear();
            Refresh();
        }

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

        void SyncCables(Museum data)
        {
            var byId = data.rooms.ToDictionary(r => r.id);
            var seen = new HashSet<string>();
            var up = Vector3.up * cableHeight;
            foreach (var room in data.rooms)
            {
                foreach (var otherId in room.connections)
                {
                    if (!byId.TryGetValue(otherId, out var other)) continue;
                    var key = ConnectionKey(room.id, otherId);
                    if (!seen.Add(key)) continue; // second direction of the same link
                    if (!cables.TryGetValue(key, out var line) || line == null)
                    {
                        line = CreateCable(key);
                        cables[key] = line;
                    }
                    // Session attachment points if the organizer chose them, else the closest wall midpoints (after Load).
                    var (pa, pb) = ConnectionAnchors.Endpoints(room, other);
                    line.SetPosition(0, pa + up);
                    line.SetPosition(1, pb + up);
                    line.widthMultiplier = cableWidth * transform.lossyScale.x;
                }
            }
            foreach (var key in new List<string>(cables.Keys))
            {
                if (seen.Contains(key)) continue;
                if (cables[key] != null) Destroy(cables[key].gameObject);
                cables.Remove(key);
            }
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
