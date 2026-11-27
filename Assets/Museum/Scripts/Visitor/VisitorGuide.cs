using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Visitor-side state: which room the user is in (head inside a room footprint), and the artworks of the
    /// life-size museum given their floor-standing label (<see cref="ArtworkTarget"/>; the artworks stay inert).
    /// No floating text: every text the visitor reads is part of the world (labels, wall plan).
    /// Locomotion (how to move) is in <see cref="VisitorMode"/> / the XR rig.
    /// </summary>
    [RequireComponent(typeof(VisitorMode))]
    public class VisitorGuide : MonoBehaviour
    {
        public VisitorMode Mode { get; private set; }
        MuseumApi Api => Mode.Api;

        /// <summary>Cached snapshot of the museum (refreshed on every model change; never edited).</summary>
        public Museum Data { get; private set; }
        public string CurrentRoomId { get; private set; }

        /// <summary>The user entered another room.</summary>
        public event Action<string> RoomChanged;
        /// <summary>The museum changed.</summary>
        public event Action StateChanged;

        readonly Dictionary<string, ArtworkTarget> targets = new();
        string arrivalRoomId;
        Vector3 arrivalPoint; // museum coordinates

        void Awake() => Mode = GetComponent<VisitorMode>();

        void Start()
        {
            Api.Changed += OnModelChanged;
            Mode.Immersive.Refreshed += WireArtworks;
            Data = Api.Data;
            WireArtworks();
        }

        void OnDestroy()
        {
            if (Mode == null || Mode.Backend == null) return;
            Api.Changed -= OnModelChanged;
            if (Mode.Immersive != null) Mode.Immersive.Refreshed -= WireArtworks;
        }

        public Room GetRoom(string id) => id == null || Data == null ? null : Data.rooms.Find(r => r.id == id);
        public ArtworkInstance GetArtwork(string id) => id == null || Data == null ? null : Data.artworks.Find(a => a.id == id);

        void OnModelChanged(MuseumChange change)
        {
            Data = Api.Data;
            if (CurrentRoomId != null && GetRoom(CurrentRoomId) == null) CurrentRoomId = null;
            StateChanged?.Invoke();
        }

        /// <summary>After each immersive refresh: give new artworks their label and re-layout every label.</summary>
        void WireArtworks()
        {
            Data = Api.Data;
            var alive = new HashSet<string>();
            foreach (var rv in Mode.Immersive.Rooms.Values)
            {
                foreach (var pair in rv.Artworks)
                {
                    if (pair.Value == null) continue;
                    alive.Add(pair.Key);
                    var t = pair.Value.GetComponent<ArtworkTarget>();
                    if (t == null)
                    {
                        t = pair.Value.AddComponent<ArtworkTarget>();
                        t.Init(this, pair.Key);
                    }
                    targets[pair.Key] = t;
                    t.Refresh();
                }
            }
            foreach (var id in new List<string>(targets.Keys))
                if (!alive.Contains(id) || targets[id] == null) targets.Remove(id);
        }

        void Update()
        {
            if (Mode.Mode != AppMode.Visitor || Data == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            // Which room is the head in? Keep the last one when between rooms (e.g. stepping into a wall).
            var p = Mode.Immersive.transform.InverseTransformPoint(cam.transform.position);
            var id = MuseumGeometry.RoomAt(Data, p);
            if (id != null && id != CurrentRoomId) SetCurrentRoom(id);
        }

        void SetCurrentRoom(string id)
        {
            CurrentRoomId = id;
            RoomChanged?.Invoke(id);
        }

        /// <summary>
        /// Called by <see cref="VisitorMode"/> right after the rig was placed in a room by the elevator or the wall plan:
        /// the visitor stands on the room's elevator platform.
        /// </summary>
        public void OnArrived(string roomId)
        {
            var room = GetRoom(roomId);
            if (room == null) { SetCurrentRoom(roomId); return; }
            var spot = MuseumStation.Compute(room, Data).spot;
            OnArrived(roomId, MuseumSpatial.ToMuseum(room, new Vector3(spot.x, 0f, spot.y)));
        }

        /// <summary>The visitor arrived in a room at <paramref name="museumPoint"/> (e.g. in front of a door): the room's
        /// labels turn towards them.</summary>
        public void OnArrived(string roomId, Vector3 museumPoint)
        {
            arrivalRoomId = roomId;
            arrivalPoint = museumPoint;
            SetCurrentRoom(roomId);
            foreach (var t in targets.Values)
                if (t != null && GetArtwork(t.ArtworkId)?.roomId == roomId) t.Refresh();
        }

        /// <summary>
        /// Where the visitor came into <paramref name="room"/> (room-local, floor level): the point of their last arrival
        /// in it, else its elevator platform (where they arrive by default). Labels face it.
        /// </summary>
        public Vector3 ArrivalLocal(Room room)
        {
            if (room.id == arrivalRoomId) return MuseumSpatial.ToRoom(room, arrivalPoint);
            var spot = MuseumStation.Compute(room, Data).spot;
            return new Vector3(spot.x, 0f, spot.y);
        }
    }
}
