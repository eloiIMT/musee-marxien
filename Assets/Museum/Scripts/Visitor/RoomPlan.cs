using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Wall plan of the room the visitor is in ("you are here" map). One instance, moved to the current room's wall
    /// on each room change, so every room has its plan. It shows the SAME miniature as the organizer's board
    /// (a <see cref="MuseumView"/> on the same data), which links the two representations, laid out the same way:
    /// north-up, same scale and centre in every room, so the plan never changes from one room to the next.
    ///  - red arrow = user position and heading, blue room = here;
    ///  - green rooms = connected: TRIGGER on one = go there (short fade, arrival facing the room we come from);
    ///    grey rooms = not connected (refused);
    ///  - lever on the wall below = back to the workshop (<see cref="ElevatorRide"/>).
    /// </summary>
    public class RoomPlan : MonoBehaviour
    {
        // Extent of the station (plan, lever, elevator) along its wall, from the plan centre (see MuseumStation).
        const float StationLeft = 0.8f, StationRight = 1.75f;
        const float BoardWidth = 0.95f, BoardHeight = 0.72f, BoardCenterY = 1.5f, WallHalfThickness = 0.05f;
        static readonly Color HereColor = new Color(0.5f, 0.8f, 1f);
        static readonly Color ConnectedColor = new Color(0.62f, 1f, 0.66f);
        static readonly Color HoverColor = new Color(0.8f, 1f, 0.82f);
        static readonly Color FarColor = new Color(0.32f, 0.33f, 0.35f);

        [SerializeField] VisitorMode mode;
        [Tooltip("Miniature MuseumView child (plan walls, no wall colliders).")]
        [SerializeField] MuseumView miniature;

        VisitorGuide Guide => mode.Guide;
        public Vector3 BoardWorldPosition => transform.position;

        Transform board, marker, markerDot;
        TextMesh title, status;
        string roomId, hoveredId;
        float scale = 0.03f;
        readonly HashSet<RoomView> wired = new();

        void Awake()
        {
            if (mode == null) mode = FindAnyObjectByType<VisitorMode>();
            board = UiKit.Plate(transform, "Board", new Vector3(0, 0, 0.012f), new Vector3(BoardWidth, BoardHeight, 0.02f), new Color(0.16f, 0.17f, 0.2f));
            title = UiKit.Text(transform, "", new Vector3(0, BoardHeight / 2f + 0.075f, -0.01f), 0.045f, Color.black, TextAnchor.MiddleCenter, TextAlignment.Center);
            UiKit.Plate(transform, "Title Plate", new Vector3(0, BoardHeight / 2f + 0.075f, 0.002f), new Vector3(BoardWidth, 0.09f, 0.01f), UiKit.PanelColor);
            title.color = Color.white;
            status = UiKit.Text(transform, "", new Vector3(0, -BoardHeight / 2f - 0.03f, -0.01f), 0.026f, Color.white, TextAnchor.UpperCenter, TextAlignment.Center);
            UiKit.Plate(transform, "Status Plate", new Vector3(0, -BoardHeight / 2f - 0.075f, 0.002f), new Vector3(BoardWidth, 0.11f, 0.01f), UiKit.PanelColor);
            // Back to the workshop: the elevator lever on the wall under the plan (ElevatorRide).

            if (miniature == null) miniature = GetComponentInChildren<MuseumView>(true);
            marker = new GameObject("You Are Here").transform;
            marker.SetParent(miniature.transform, false);
            UiKit.Arrow(marker, "Heading", 1f, new Color(1f, 0.2f, 0.2f)).localPosition = new Vector3(0, 0, 0.35f);
            markerDot = UiKit.Plate(marker, "Dot", Vector3.zero, new Vector3(0.45f, 0.05f, 0.45f), new Color(1f, 0.2f, 0.2f));
        }

        void OnEnable()
        {
            if (mode == null || mode.Guide == null) return;
            Guide.RoomChanged += OnRoomChanged;
            Guide.StateChanged += OnStateChanged;
            miniature.Refreshed += OnMiniatureRefreshed;
            OnMiniatureRefreshed();
            Place(Guide.CurrentRoomId);
        }

        void OnDisable()
        {
            if (mode == null || mode.Guide == null) return;
            Guide.RoomChanged -= OnRoomChanged;
            Guide.StateChanged -= OnStateChanged;
            if (miniature != null) miniature.Refreshed -= OnMiniatureRefreshed;
            hoveredId = null;
        }

        void OnRoomChanged(string id) => Place(id);
        void OnStateChanged() { if (roomId != Guide.CurrentRoomId || roomId == null) Place(Guide.CurrentRoomId); else { Fit(); UpdateVisuals(); } }

        // ---------------------------------------------------------------- placement on a wall

        /// <summary>
        /// Wall of a room that holds the plan: the one with fewest artworks around its middle, preferring long walls.
        /// Returns the museum-space point at the middle of the wall's inner face (floor level), its outward normal and length.
        /// </summary>
        public static (Vector3 centre, Vector3 normal, float length) ChooseWall(Room room, Museum museum)
        {
            var (local, n, len) = ChooseWallLocal(room, museum);
            return (MuseumSpatial.ToMuseum(room, local), room.rotation * n, len);
        }

        /// <summary>Same as <see cref="ChooseWall"/>, in room-local coordinates; <paramref name="ignoreArtworkId"/> is left out.</summary>
        public static (Vector3 centre, Vector3 normal, float length) ChooseWallLocal(Room room, Museum museum, string ignoreArtworkId = null)
        {
            var walls = new[]
            {
                (n: Vector3.back, half: room.length / 2f, len: room.width),
                (n: Vector3.forward, half: room.length / 2f, len: room.width),
                (n: Vector3.left, half: room.width / 2f, len: room.length),
                (n: Vector3.right, half: room.width / 2f, len: room.length),
            };
            // Doors of this room (ConnectionLayout): the plan, its lever and the elevator must not stand in front of one.
            var doors = new List<Door>();
            foreach (var pair in ConnectionLayout.Compute(museum).Values)
            {
                if (pair.a.RoomId == room.id) doors.Add(pair.a);
                if (pair.b.RoomId == room.id) doors.Add(pair.b);
            }
            int best = 0;
            float bestScore = float.MaxValue;
            for (int i = 0; i < walls.Length; i++)
            {
                var (n, half, len) = walls[i];
                float score = len < 1.6f ? 100f : 0f;
                var alongAxis = Vector3.Cross(Vector3.up, n);
                foreach (var d in doors)
                {
                    if (Vector3.Dot(d.Normal, n) < 0.5f) continue;
                    // station along this wall, from the plan centre: plan on the left, lever + elevator on the right
                    float along = Vector3.Dot(d.Local, alongAxis), half2 = d.Width / 2f + 0.2f;
                    if (along + half2 > -StationLeft && along - half2 < StationRight) score += 50f;
                }
                foreach (var a in museum.artworks)
                {
                    if (a.roomId != room.id || a.id == ignoreArtworkId) continue;
                    float toWall = half - Vector3.Dot(a.localPosition, n);              // distance from the wall plane
                    float along = Vector3.Dot(a.localPosition, Vector3.Cross(Vector3.up, n)); // offset along the wall
                    if (toWall < 1.2f && Mathf.Abs(along) < 0.9f) score += 1f;
                }
                score -= len * 0.01f; // tie-break: the longest wall
                if (score < bestScore) { bestScore = score; best = i; }
            }
            var w = walls[best];
            return (w.n * (w.half - WallHalfThickness), w.n, w.len);
        }

        void Place(string id)
        {
            roomId = id;
            var room = Guide.GetRoom(id);
            if (room == null) { board.gameObject.SetActive(false); return; }
            board.gameObject.SetActive(true);

            var root = mode.Immersive.transform;
            var (centre, normal, length) = ChooseWall(room, Guide.Data);
            var outward = root.TransformDirection(normal);
            outward.y = 0;
            var rot = Quaternion.LookRotation(outward.normalized, Vector3.up);
            transform.SetPositionAndRotation(root.TransformPoint(centre) + Vector3.up * BoardCenterY - outward.normalized * 0.03f, rot);
            Fit();
            UpdateVisuals();
        }

        /// <summary>
        /// Scale and centre the miniature so the whole museum fits on the board, always north-up: the same plan in every
        /// room (rooms never move on it from one room to the next), like the organizer's board.
        /// </summary>
        void Fit()
        {
            var data = Guide.Data;
            if (data == null || data.rooms.Count == 0) return;

            bool any = false;
            Vector2 min = default, max = default;
            foreach (var r in data.rooms)
                foreach (var c in MuseumGeometry.Corners(r.position, MuseumGeometry.Yaw(r.rotation), r.width, r.length))
                {
                    if (!any) { min = max = c; any = true; }
                    else { min = Vector2.Min(min, c); max = Vector2.Max(max, c); }
                }
            var size = max - min;
            scale = Mathf.Clamp(Mathf.Min((BoardWidth - 0.1f) / Mathf.Max(size.x, 0.1f), (BoardHeight - 0.1f) / Mathf.Max(size.y, 0.1f)), 0.004f, 0.08f);
            var centre = (min + max) / 2f;
            var centreMuseum = new Vector3(centre.x, 0, centre.y);

            // Museum up (Y) -> towards the user (-Z of the plan); museum north (+Z) -> plan up.
            var localRot = Quaternion.LookRotation(Vector3.up, Vector3.back);
            var t = miniature.transform;
            t.localScale = Vector3.one * scale;
            t.localRotation = localRot;
            t.localPosition = new Vector3(0, 0, -0.003f) - localRot * (centreMuseum * scale);
            miniature.Refresh(); // cable widths and labels depend on the scale
        }

        // ---------------------------------------------------------------- interaction

        void OnMiniatureRefreshed()
        {
            wired.RemoveWhere(r => r == null);
            foreach (var tm in miniature.GetComponentsInChildren<TextMesh>(true)) UiKit.UseWorldText(tm); // room names: hidden behind objects
            foreach (var rv in miniature.Rooms.Values)
            {
                if (!wired.Add(rv)) continue;
                var id = rv.RoomId;
                var c = rv.Floor.GetComponent<Clickable>();
                if (c == null) c = rv.Floor.AddComponent<Clickable>();
                c.Clicked += (_, _) => OnRoomClicked(id);
                c.HoverChanged += h =>
                {
                    if (h) hoveredId = id; else if (hoveredId == id) hoveredId = null;
                    UpdateVisuals();
                };
            }
            UpdateVisuals();
        }

        void OnRoomClicked(string id)
        {
            var data = Guide.Data;
            var target = Guide.GetRoom(id);
            if (target == null) return;
            if (id == roomId) { Sounds.Play(Sounds.Error, 0.4f); Flash($"Vous êtes déjà dans « {target.name} »."); return; }
            if (!MuseumPaths.AreConnected(data, roomId, id))
            {
                Sounds.Play(Sounds.Error, 0.5f);
                var path = MuseumPaths.ShortestPath(data, roomId, id);
                Flash(path != null && path.Count > 2
                    ? $"« {target.name} » n'est pas reliée à cette salle : passez d'abord par « {Guide.GetRoom(path[1])?.name} »."
                    : $"« {target.name} » n'est reliée à cette salle par aucun passage.");
                return;
            }
            Sounds.Play(Sounds.Click, 0.6f);
            mode.TeleportToRoom(id, roomId);
        }

        float flashUntil;
        void Flash(string message)
        {
            status.text = UiKit.Wrap(message, UiKit.CharsFor(BoardWidth - 0.06f, 0.026f), 3);
            status.color = UiKit.Error;
            flashUntil = Time.time + 3f;
        }

        void UpdateVisuals()
        {
            var data = Guide.Data;
            if (data == null) return;
            var here = Guide.GetRoom(roomId);
            title.text = here != null ? $"Vous êtes ici : {here.name}" : "Plan du musée";

            foreach (var rv in miniature.Rooms.Values)
            {
                var id = rv.RoomId;
                bool connected = MuseumPaths.AreConnected(data, roomId, id);
                Color c;
                if (id == roomId) c = HereColor;
                else c = connected ? ConnectedColor : FarColor;
                if (id == hoveredId && connected) c = Color.Lerp(c, HoverColor, 0.6f);
                rv.SetFloorTint(c);
            }

            if (Time.time >= flashUntil)
            {
                string line;
                var hovered = Guide.GetRoom(hoveredId);
                if (hovered != null && hoveredId != roomId)
                    line = MuseumPaths.AreConnected(data, roomId, hoveredId) ? $"Gâchette : aller dans « {hovered.name} »" : $"« {hovered.name} » : non reliée à cette salle";
                else line = "Salles vertes = reliées : gâchette pour y aller";
                status.text = UiKit.Wrap(line, UiKit.CharsFor(BoardWidth - 0.06f, 0.026f), 3);
                status.color = Color.white;
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || Guide == null) return;
            var root = mode.Immersive.transform;
            // Constant world size (metres) whatever the miniature scale.
            float inv = 1f / Mathf.Max(scale, 1e-4f);

            var p = root.InverseTransformPoint(cam.transform.position);
            var f = root.InverseTransformDirection(cam.transform.forward);
            marker.localPosition = new Vector3(p.x, 1.2f, p.z);
            if (new Vector2(f.x, f.z).sqrMagnitude > 1e-4f) marker.localRotation = Quaternion.Euler(0, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 0);
            marker.localScale = Vector3.one * (0.045f * inv);

            if (Time.time >= flashUntil && status.color == UiKit.Error) UpdateVisuals();
        }
    }
}
