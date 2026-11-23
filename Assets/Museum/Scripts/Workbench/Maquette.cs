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
    /// - plan view: rooms flat, the hovered / selected room expands to 3D
    /// - selection of rooms and connections (trigger), gizmo on the selected room
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
        [SerializeField] Color targetColor = new Color(0.4f, 1f, 0.5f);
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
        public string SelectedConnection { get; private set; }
        /// <summary>Raised when the selected room or connection changes (for the screen UI).</summary>
        public event Action SelectionChanged;

        /// <summary>Which handles the user works with (picked with the mode selector above the board).</summary>
        public EditMode Mode { get; private set; } = EditMode.Move;
        public event Action ModeChanged;
        ModeSelector modeSelector;

        string targetRoomId;
        RoomGizmo gizmo;
        Transform disconnectButton;
        TextMesh toast;
        float toastUntil;
        readonly HashSet<RoomView> wiredRooms = new();
        readonly HashSet<GameObject> wiredArtworks = new();
        readonly Dictionary<LineRenderer, string> wiredCables = new();

        void Awake()
        {
            View = GetComponent<MuseumView>();
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

            disconnectButton = CreateButton("Disconnect", new Color(1f, 0.25f, 0.2f), DisconnectSelected);

            if (board != null)
            {
                var c = board.GetComponent<Clickable>();
                if (c == null) c = board.gameObject.AddComponent<Clickable>();
                c.Clicked += (_, _) => ClearSelection();
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
            UpdateDisconnectButton();
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
                var c = rv.Floor.AddComponent<Clickable>();
                c.Clicked += (_, _) => SelectRoom(id);
                c.HoverChanged += hovered =>
                {
                    if (hovered) HoveredRoomId = id;
                    else if (HoveredRoomId == id) HoveredRoomId = null;
                    UpdateVisuals();
                };
                // Grip on the floor = grab and move the room directly, no prior selection needed.
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

            var dead = new List<LineRenderer>();
            foreach (var line in wiredCables.Keys) if (line == null) dead.Add(line);
            foreach (var line in dead) wiredCables.Remove(line);
            foreach (var pair in View.Cables)
            {
                if (!wiredCables.ContainsKey(pair.Value))
                {
                    wiredCables[pair.Value] = pair.Key;
                    var key = pair.Key;
                    var hit = new GameObject("Hit", typeof(BoxCollider));
                    hit.transform.SetParent(pair.Value.transform, false);
                    hit.AddComponent<Clickable>().Clicked += (_, _) => { if (Mode == EditMode.Link) SelectConnection(key); };
                }
                FitCableCollider(pair.Value);
            }

            if (SelectedRoomId != null && View.GetRoomView(SelectedRoomId) == null) SelectRoom(null);
            if (SelectedConnection != null && !View.Cables.ContainsKey(SelectedConnection)) SelectConnection(null);
            ApplyModeToHandles();
            UpdateVisuals();
        }

        // ---------------------------------------------------------------- edit mode

        public void SetMode(EditMode mode)
        {
            if (mode == Mode) return;
            Mode = mode;
            if (mode != EditMode.Link && SelectedConnection != null) SelectConnection(null);
            ApplyModeToHandles();
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

        static void FitCableCollider(LineRenderer line)
        {
            var hit = line.transform.Find("Hit");
            if (hit == null) return;
            var a = line.GetPosition(0);
            var b = line.GetPosition(1);
            var dir = b - a;
            hit.localPosition = (a + b) / 2f;
            hit.localRotation = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            hit.GetComponent<BoxCollider>().size = new Vector3(0.8f, 0.8f, Mathf.Max(dir.magnitude - 0.5f, 0.2f));
        }

        // ---------------------------------------------------------------- selection

        public void SelectRoom(string roomId)
        {
            SelectedRoomId = roomId;
            SelectedConnection = null;
            SyncModeSelector();
            UpdateVisuals();
            SelectionChanged?.Invoke();
        }

        public void SelectConnection(string key)
        {
            SelectedConnection = key;
            if (key != null) SelectedRoomId = null;
            SyncModeSelector();
            UpdateVisuals();
            SelectionChanged?.Invoke();
        }

        public void ClearSelection()
        {
            if (SelectedRoomId == null && SelectedConnection == null) return;
            SelectedRoomId = null;
            SelectedConnection = null;
            SyncModeSelector();
            UpdateVisuals();
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// The mode selector (on the controller) is shown while a room or a connection is selected. With nothing
        /// selected the user is back in the Move mode, so the next selection starts there.
        /// </summary>
        void SyncModeSelector()
        {
            bool show = SelectedRoomId != null || SelectedConnection != null;
            if (!show && Mode != EditMode.Move) SetMode(EditMode.Move);
            if (modeSelector != null && modeSelector.gameObject.activeSelf != show) modeSelector.gameObject.SetActive(show);
        }

        /// <summary>Highlight a room as a drop target (connection drag); null clears.</summary>
        public void SetTargetRoom(string roomId)
        {
            if (targetRoomId == roomId) return;
            targetRoomId = roomId;
            UpdateVisuals();
        }

        void UpdateVisuals()
        {
            foreach (var rv in View.Rooms.Values)
            {
                var id = rv.RoomId;
                bool selected = id == SelectedRoomId;
                bool hovered = id == HoveredRoomId;
                rv.Expanded = selected || (hovered && SelectedRoomId == null);
                rv.SetFloorTint(id == targetRoomId ? targetColor : selected ? selectedColor : hovered ? hoverColor : (Color?)null);
            }
            foreach (var pair in View.Cables)
            {
                var r = pair.Value.GetComponent<Renderer>();
                if (pair.Key == SelectedConnection) Tint.Set(r, selectedCableColor); else Tint.Clear(r);
            }
            gizmo.Attach(View.GetRoomView(SelectedRoomId));
        }

        // ---------------------------------------------------------------- connection removal

        void UpdateDisconnectButton()
        {
            bool show = SelectedConnection != null && View.Cables.TryGetValue(SelectedConnection, out var line) && line != null;
            disconnectButton.gameObject.SetActive(show);
            if (!show) return;
            line = View.Cables[SelectedConnection];
            var mid = (line.GetPosition(0) + line.GetPosition(1)) / 2f;
            disconnectButton.localPosition = mid + Vector3.up * 1.2f;
            disconnectButton.localScale = Vector3.one * (0.03f / Scale);
        }

        /// <summary>Remove the selected connection (also callable from the screen UI).</summary>
        public void DisconnectSelected()
        {
            if (SelectedConnection == null) return;
            var (a, b) = MuseumView.SplitKey(SelectedConnection);
            SelectConnection(null);
            Api.DisconnectRooms(a, b);
            Toast("Connexion supprimée");
        }

        Transform CreateButton(string buttonName, Color color, Action onClick)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = buttonName;
            go.transform.SetParent(transform, false);
            go.GetComponent<Renderer>().sharedMaterial = handleMaterial;
            Tint.Set(go.GetComponent<Renderer>(), color);
            go.AddComponent<Clickable>().Clicked += (_, _) => onClick();
            go.SetActive(false);
            return go.transform;
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
