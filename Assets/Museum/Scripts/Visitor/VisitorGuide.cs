using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Visitor-side state: which room the user is in (head inside a room footprint), and the artworks of the
    /// life-size museum made pointable with their floor-standing label (<see cref="ArtworkTarget"/>).
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

        /// <summary>After each immersive refresh: make new artworks pointable and re-layout every label.</summary>
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

        /// <summary>Called by <see cref="VisitorMode"/> right after the rig was placed in a room.</summary>
        public void OnArrived(string roomId) => SetCurrentRoom(roomId);
    }
}
