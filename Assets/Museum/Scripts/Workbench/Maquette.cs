using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Organizer controller for the miniature on the wall plan board. Sits next to the miniature <see cref="MuseumView"/>.
    /// The museum floor lies IN the board plane (museum Y = board normal, towards the user): rooms are drawn flat on the
    /// board and the selected room "comes out" of it in 3D.
    /// - fits the miniature on the board (auto scale, no pan/zoom)
    /// - plan view: rooms flat, the selected room AND the hovered one (even while another is selected) expand to 3D
    /// - selection of rooms (trigger), gizmo on the selected room
    /// - connections, in the Link mode: trigger on another room links it to the selected one, or unlinks it if
    ///   already linked; linked rooms are lit green, the selected room's connection lines too
    /// - coordinate helpers between hand (world) and museum space, user feedback (toast)
    /// </summary>
    [RequireComponent(typeof(MuseumView))]
    public class Maquette : MonoBehaviour
    {
        [Tooltip("Plan board: a box whose local X = width, Y = height, Z = thickness; its front face (-Z) faces the user.")]
        [UnityEngine.Serialization.FormerlySerializedAs("tableTop")]
        [SerializeField] Collider board;
        [SerializeField] Material handleMaterial;

        [Header("Auto fit")]
        [SerializeField, Range(0.5f, 1f)] float fitMargin = 0.85f;
        [SerializeField] float minScale = 0.01f;
        [SerializeField] float maxScale = 0.08f;
        [SerializeField] float emptyMuseumScale = 0.04f;

        [Header("Feedback colours")]
        [SerializeField] Color hoverColor = new Color(0.65f, 0.85f, 1f);
        [SerializeField] Color selectedColor = new Color(0.3f, 0.6f, 1f);
        [UnityEngine.Serialization.FormerlySerializedAs("targetColor")]
        [SerializeField] Color linkedColor = new Color(0.4f, 1f, 0.5f);
        [SerializeField] Color unlinkHoverColor = new Color(1f, 0.55f, 0.5f);
        [SerializeField] Color selectedCableColor = new Color(0.2f, 0.9f, 1f);

        public MuseumView View { get; private set; }
        public MuseumApi Api => View.Backend.Api;
        /// <summary>Landing preview projected on the plan (green / red).</summary>
        public GhostRoom Ghost { get; private set; }
        /// <summary>The object "in the hand" (force grab), drawn at miniature scale at the hand position.</summary>
        public GhostRoom Held { get; private set; }
        public static readonly Color HeldColor = new Color(0.35f, 0.65f, 1f);
        public Material HandleMaterial => handleMaterial;
        public float Scale => transform.lossyScale.x;

        public string SelectedRoomId { get; private set; }
        public string HoveredRoomId { get; private set; }
        /// <summary>Raised when the selected room changes.</summary>
        public event Action SelectionChanged;

        /// <summary>Which handles the user works with (picked with the mode selector above the board).</summary>
        public EditMode Mode { get; private set; } = EditMode.Move;
        public event Action ModeChanged;
        ModeSelector modeSelector;

        RoomGizmo gizmo;
        Material translucentWall;
        TextMesh toast;
        float toastUntil;
        readonly HashSet<RoomView> wiredRooms = new();
        readonly HashSet<GameObject> wiredArtworks = new();

        void Awake()
        {
            View = GetComponent<MuseumView>();
            // Translucent walls: the user sees (and, walls having no collider here, acts) through them.
            translucentWall = Resources.Load<Material>("MiniatureWall");
            if (handleMaterial == null)
                handleMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            if (TriggerRouter.Instance == null && FindAnyObjectByType<TriggerRouter>() == null)
                new GameObject("Trigger Router").AddComponent<TriggerRouter>();

            Ghost = new GameObject("Ghost").AddComponent<GhostRoom>();
            Ghost.transform.SetParent(transform, false);
            Ghost.Init(this);
            Held = new GameObject("Held").AddComponent<GhostRoom>();
            Held.transform.SetParent(transform, false);
            Held.Init(this);

            gizmo = new GameObject("Gizmo").AddComponent<RoomGizmo>();
            gizmo.transform.SetParent(transform, false);
            gizmo.Init(this);

            if (board != null)
            {
                var c = board.GetComponent<Clickable>();
                if (c == null) c = board.gameObject.AddComponent<Clickable>();
                // Click on the empty plan: deselect, or with nothing selected create a room under the skeleton.
                var creator = gameObject.AddComponent<RoomCreator>();
                creator.Init(c);
                c.Clicked += (_, interactor) =>
                {
                    if (SelectedRoomId != null) ClearSelection();
                    else creator.Click(interactor);
                };
            }
            toast = Labels.Create(transform.parent != null ? transform.parent : null, "", 0.035f, Color.white);
            toast.gameObject.name = "Toast";
            modeSelector = ModeSelector.Create(transform.parent, this);
        }

        void Start()
        {
            // By default XRI pins the far ray's end on the grabbed object. On the plan the object follows the aimed point
            // instead, so let the ray keep showing where the user points.
            foreach (var curve in FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.CurveVisualController>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                curve.snapToSelectedAttachIfAvailable = false;

            View.Refreshed += OnRefreshed;
            Api.Changed += OnModelChanged;
            Fit();
        }

        void OnDestroy()
        {
            if (View != null) View.Refreshed -= OnRefreshed;
            if (View != null && View.Backend != null) Api.Changed -= OnModelChanged;
        }

        void Update()
        {
            if (toast != null && toast.gameObject.activeSelf && Time.time > toastUntil) toast.gameObject.SetActive(false);
            UpdateModeSelectorVisibility();
        }

        void OnDisable()
        {
            // The selector lives on the controller, outside the workshop: hide it with the workshop (visitor view).
            if (modeSelector != null) modeSelector.gameObject.SetActive(false);
        }

        void OnModelChanged(MuseumChange change)
        {
            // Load / Reset replace the whole museum: re-fit the board and drop a selection that no longer means anything.
            if (change.Entity == MuseumEntity.Museum && change.Operation == MuseumOperation.Reset)
            {
                ClearSelection();
                Fit();
            }
        }

        // ---------------------------------------------------------------- fit

        /// <summary>Scale and centre the miniature so the whole museum fits on the board, floor in the board plane.</summary>
        public void Fit()
        {
            if (board == null) { View.Refresh(); return; }
            var data = Api.Data;
            var bt = board.transform;
            float boardWidth = bt.lossyScale.x, boardHeight = bt.lossyScale.y;
            float scale = emptyMuseumScale;
            var center = Vector3.zero;
            if (data.rooms.Count > 0)
            {
                var bounds = MuseumSpatial.MuseumBounds(data.rooms[0]);
                foreach (var r in data.rooms) bounds.Encapsulate(MuseumSpatial.MuseumBounds(r));
                center = bounds.center;
                // Museum X runs along the board width, museum Z (north) up the board.
                scale = Mathf.Min(boardWidth * fitMargin / Mathf.Max(bounds.size.x, 0.1f),
                                  boardHeight * fitMargin / Mathf.Max(bounds.size.z, 0.1f));
                scale = Mathf.Clamp(scale, minScale, maxScale);
            }
            var parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
            transform.localScale = Vector3.one * (scale / parentScale);
            // Museum up (Y) = out of the board towards the user; museum north (Z) = board up.
            transform.rotation = bt.rotation * Quaternion.Euler(-90f, 0f, 0f);
            var front = bt.position - bt.forward * (bt.lossyScale.z / 2f + 0.002f);
            transform.position = front - transform.rotation * new Vector3(center.x, 0f, center.z) * scale;
            modeSelector.Board = bt;
            toast.transform.position = bt.position + bt.up * (boardHeight / 2f + 0.08f) - bt.forward * 0.05f;
            View.Refresh(); // cable widths and handle sizes depend on scale
        }

        // ---------------------------------------------------------------- refresh / wiring

        void OnRefreshed()
        {
            wiredRooms.RemoveWhere(r => r == null);
            foreach (var rv in View.Rooms.Values)
            {
                if (!wiredRooms.Add(rv)) continue;
                var id = rv.RoomId;
                if (translucentWall != null) rv.SetWallMaterial(translucentWall);
                rv.gameObject.AddComponent<StationMarker>();
                var c = rv.Floor.AddComponent<Clickable>();
                c.Clicked += (_, _) => OnRoomClicked(id);
                c.HoverChanged += hovered =>
                {
                    if (hovered) HoveredRoomId = id;
                    else if (HoveredRoomId == id) HoveredRoomId = null;
                    UpdateVisuals();
                };
                // Trigger on the floor = grab and move the room directly, no prior selection needed.
                var move = rv.Floor.AddComponent<MoveHandle>();
                move.FixedRoomId = id;
                move.InitWithoutTint(this);
            }

            // Artworks: grabbable on the miniature (visible only when their room is expanded).
            wiredArtworks.RemoveWhere(a => a == null);
            foreach (var rv in View.Rooms.Values)
            {
                foreach (var pair in rv.Artworks)
                {
                    var go = pair.Value;
                    if (go == null || !wiredArtworks.Add(go)) continue;
                    // Catalogue prefabs ship without colliders: add them before the interactable collects colliders.
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                        if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<BoxCollider>();
                    var handle = go.AddComponent<ArtworkHandle>();
                    handle.ArtworkId = pair.Key;
                    handle.Init(this, Color.white);
                }
            }

            // Where each room's wall plan and lever will be (they depend on the artworks: re-placed on every change).
            var data = Api.Data;
            foreach (var rv in View.Rooms.Values)
            {
                var marker = rv != null ? rv.GetComponent<StationMarker>() : null;
                if (marker != null) marker.Apply(rv.Data, data, handleMaterial);
            }

            if (SelectedRoomId != null && View.GetRoomView(SelectedRoomId) == null) SelectRoom(null);
            ApplyModeToHandles();
            UpdateVisuals();
        }

        // ---------------------------------------------------------------- edit mode

        public void SetMode(EditMode mode)
        {
            if (mode == Mode) return;
            Mode = mode;
            ApplyModeToHandles();
            UpdateVisuals();
            ModeChanged?.Invoke();
        }

        /// <summary>Grabbing a room floor or an artwork moves it: only in the Move mode.</summary>
        void ApplyModeToHandles()
        {
            bool move = Mode == EditMode.Move;
            foreach (var rv in View.Rooms.Values)
            {
                if (rv == null) continue;
                var floorMove = rv.Floor.GetComponent<MoveHandle>();
                if (floorMove != null) floorMove.enabled = move;
                foreach (var go in rv.Artworks.Values)
                {
                    if (go == null) continue;
                    var handle = go.GetComponent<ArtworkHandle>();
                    if (handle != null) handle.enabled = move;
                }
            }
        }

        // ---------------------------------------------------------------- selection / links

        /// <summary>Trigger on a room floor: in the Link mode, (un)link it with the selected room; otherwise select it.</summary>
        void OnRoomClicked(string id)
        {
            if (Mode == EditMode.Link && SelectedRoomId != null && id != SelectedRoomId) ToggleLink(SelectedRoomId, id);
            else SelectRoom(id);
        }

        /// <summary>Link two rooms, or unlink them if they already are (the selection stays on the first room).</summary>
        public void ToggleLink(string a, string b)
        {
            var ra = Api.GetRoom(a);
            var rb = Api.GetRoom(b);
            if (ra == null || rb == null) return;
            if (ra.connections.Contains(b))
            {
                Api.DisconnectRooms(a, b);
                Toast($"« {ra.name} » et « {rb.name} » ne sont plus reliées");
            }
            else
            {
                Api.ConnectRooms(a, b);
                Toast($"« {ra.name} » ↔ « {rb.name} » reliées");
            }
            UpdateVisuals();
        }

        public void SelectRoom(string roomId)
        {
            SelectedRoomId = roomId;
            SyncModeSelector();
            UpdateVisuals();
            SelectionChanged?.Invoke();
        }

        public void ClearSelection()
        {
            if (SelectedRoomId == null) return;
            SelectedRoomId = null;
            SyncModeSelector();
            UpdateVisuals();
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// The mode selector (on the controller) is shown while a room is selected. The chosen mode is kept when the
        /// selection changes or clears, so the next selection starts in the mode the user picked.
        /// </summary>
        void SyncModeSelector() => UpdateModeSelectorVisibility();

        /// <summary>
        /// Selector shown only while something is selected AND a hand's ray points at the plan board: pointing
        /// elsewhere gives the right stick back to turning.
        /// </summary>
        void UpdateModeSelectorVisibility()
        {
            if (modeSelector == null) return;
            bool show = isActiveAndEnabled && SelectedRoomId != null && IsRayOnBoard();
            if (modeSelector.gameObject.activeSelf != show) modeSelector.gameObject.SetActive(show);
        }

        UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor[] rayInteractors;
        float nextInteractorSearch;

        /// <summary>
        /// Does a hand's ray (as drawn by XRI) cross the plan board (10 cm margin)? True only in a scene without any
        /// hand ray interactor; false while the controllers are not tracked (their interactors are inactive).
        /// </summary>
        bool IsRayOnBoard()
        {
            if (board == null) return true;
            // Inactive ones included: the controllers' objects are disabled until tracked, so a search made at start
            // would find nothing and never search again.
            if ((rayInteractors == null || rayInteractors.Length == 0 || Array.Exists(rayInteractors, i => i == null))
                && Time.unscaledTime >= nextInteractorSearch)
            {
                rayInteractors = Array.FindAll(
                    FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                    i => i is UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.ICurveInteractionDataProvider
                         && i.handedness != UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.None); // not the gaze
                nextInteractorSearch = Time.unscaledTime + 2f;
            }
            if (rayInteractors == null || rayInteractors.Length == 0) return true;

            var bt = board.transform;
            var plane = new Plane(bt.forward, bt.position);
            float mx = 0.5f + 0.1f / Mathf.Max(bt.lossyScale.x, 1e-3f), my = 0.5f + 0.1f / Mathf.Max(bt.lossyScale.y, 1e-3f);
            foreach (var i in rayInteractors)
            {
                if (i == null || !i.isActiveAndEnabled) continue;
                var origin = ((UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.ICurveInteractionDataProvider)i).curveOrigin;
                if (origin == null) origin = i.transform;
                var ray = new Ray(origin.position, origin.forward);
                if (!plane.Raycast(ray, out float d)) continue;
                var p = bt.InverseTransformPoint(ray.GetPoint(d)); // unit cube space
                if (Mathf.Abs(p.x) <= mx && Mathf.Abs(p.y) <= my) return true;
            }
            return false;
        }

        void UpdateVisuals()
        {
            var selectedRoom = SelectedRoomId != null ? Api.GetRoom(SelectedRoomId) : null;
            bool linking = Mode == EditMode.Link && selectedRoom != null;
            foreach (var rv in View.Rooms.Values)
            {
                var id = rv.RoomId;
                bool selected = id == SelectedRoomId;
                bool hovered = id == HoveredRoomId;
                rv.Expanded = selected || hovered;
                Color? tint = null;
                // Blue floor = "grab it to move it": only in the Move mode (the floor is not grabbable otherwise).
                if (selected) tint = Mode == EditMode.Move ? selectedColor : null;
                else if (linking)
                {
                    // Link mode: linked rooms green; hovering shows what the trigger will do (link / unlink).
                    bool linked = selectedRoom.connections.Contains(id);
                    if (hovered) tint = linked ? unlinkHoverColor : Color.Lerp(hoverColor, linkedColor, 0.5f);
                    else if (linked) tint = linkedColor;
                }
                else if (hovered) tint = hoverColor;
                rv.SetFloorTint(tint);
            }
            // The selected room's connection lines stand out.
            foreach (var pair in View.Cables)
            {
                var r = pair.Value.GetComponent<Renderer>();
                var (a, b) = MuseumView.SplitKey(pair.Key);
                if (SelectedRoomId != null && (a == SelectedRoomId || b == SelectedRoomId)) Tint.Set(r, selectedCableColor);
                else Tint.Clear(r);
            }
            gizmo.Attach(View.GetRoomView(SelectedRoomId));
        }

        // ---------------------------------------------------------------- reserved area

        string reservedZoneRoomId;

        /// <summary>
        /// Show the outline of the area kept free in front of a room's wall plan and lever (an artwork is being dragged
        /// into it), or hide it (null). Only one room at a time.
        /// </summary>
        public void ShowReservedZone(string roomId)
        {
            if (roomId == reservedZoneRoomId) return;
            SetZoneVisible(reservedZoneRoomId, false);
            reservedZoneRoomId = roomId;
            SetZoneVisible(roomId, true);
        }

        void SetZoneVisible(string roomId, bool visible)
        {
            var rv = View.GetRoomView(roomId);
            var marker = rv != null ? rv.GetComponent<StationMarker>() : null;
            if (marker != null) marker.ShowZone(visible);
        }

        // ---------------------------------------------------------------- coordinates

        /// <summary>Hand position projected onto the board plane (perpendicularly), in museum coordinates (y = 0).</summary>
        public Vector3 HandToMuseum(Vector3 world)
        {
            var p = transform.InverseTransformPoint(world);
            p.y = 0f;
            return p;
        }

        /// <summary>Distance of a world point from the board plane (museum "height"), in museum metres.</summary>
        public float MuseumHeight(Vector3 world) => transform.InverseTransformPoint(world).y;

        public Vector3 MuseumToWorld(Vector3 museumPoint) => transform.TransformPoint(museumPoint);

        /// <summary>Intersect a world ray with the museum floor plane.</summary>
        public bool RayToMuseum(Ray worldRay, out Vector3 museumPoint)
        {
            var plane = new Plane(transform.up, transform.position);
            if (plane.Raycast(worldRay, out float distance))
            {
                museumPoint = HandToMuseum(worldRay.GetPoint(distance));
                return true;
            }
            museumPoint = default;
            return false;
        }

        /// <summary>Is a world point in front of the board (inside its outline, whatever the distance)?</summary>
        public bool IsOverBoard(Vector3 world)
        {
            if (board == null) return true;
            var p = board.transform.InverseTransformPoint(world); // unit cube space
            return Mathf.Abs(p.x) <= 0.5f && Mathf.Abs(p.y) <= 0.5f;
        }

        /// <summary>
        /// Rotation of the hand around the board normal (museum yaw), from the controller's right axis.
        /// Turning the wrist like a knob in front of the board rotates the object. Robust whatever the plan orientation.
        /// </summary>
        public float HandTwist(Quaternion handRotation)
        {
            var right = Quaternion.Inverse(transform.rotation) * (handRotation * Vector3.right);
            // Quaternion.Euler(0, yaw, 0) maps X to (cos yaw, 0, -sin yaw).
            return Mathf.Atan2(-right.z, right.x) * Mathf.Rad2Deg;
        }

        // ---------------------------------------------------------------- feedback

        /// <summary>Short message floating above the board (green = done, red = refused).</summary>
        public void Toast(string message, bool error = false)
        {
            toast.text = message;
            toast.color = error ? new Color(1f, 0.35f, 0.3f) : new Color(0.4f, 1f, 0.5f);
            toast.gameObject.SetActive(true);
            toastUntil = Time.time + 3f;
            if (error) Debug.LogWarning("[Maquette] " + message, this); else Debug.Log("[Maquette] " + message, this);
        }
    }
}
