using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace VirtualMuseum.App
{
    public enum AppMode { Organizer, Visitor }

    /// <summary>
    /// Switches between the two modes, which read the SAME backend data:
    ///  - Organizer: the workshop (plan board, elevator lever) is shown, the life-size museum is hidden;
    ///  - Visitor: the life-size museum (scale 1) is shown, the workshop is hidden.
    /// Hiding the other world keeps the two representations from overlapping, whatever the organizer does with rooms.
    /// The mode changes are done by the elevator (<see cref="ElevatorRide"/>); room to room goes through a short fade
    /// to black (no text), so the user never sees the world slide (comfort). The movement vignette is on in both modes.
    /// Also owns the rig moves and the visitor locomotion settings (hand-relative continuous move, 45° snap turn).
    /// </summary>
    [DefaultExecutionOrder(-50)] // Awake before the plan / buttons, which use its UI material
    public class VisitorMode : MonoBehaviour
    {
        [SerializeField] MuseumBackend backend;
        [Tooltip("Life-size museum (scale 1).")]
        [SerializeField] MuseumView immersive;
        [Tooltip("Workshop objects hidden while visiting (workbench, workshop floor…).")]
        [SerializeField] GameObject[] organizerObjects;
        [Tooltip("Objects only shown while visiting (room plan…). The immersive museum is handled separately.")]
        [SerializeField] GameObject[] visitorObjects;
        [SerializeField] RoomPlan roomPlan;
        [Tooltip("Museum/Overlay material (fade + vignette).")]
        [SerializeField] Material overlayMaterial;
        [Tooltip("Museum/WorldText material (depth-tested text: labels never show through walls).")]
        [SerializeField] Material worldTextMaterial;
        [Tooltip("Unlit material for panels and buttons.")]
        [SerializeField] Material uiMaterial;

        [Header("Visitor locomotion")]
        [SerializeField] float visitorMoveSpeed = 1.4f;
        [SerializeField] float roomFade = 0.18f;

        public AppMode Mode { get; private set; } = AppMode.Organizer;
        /// <summary>A transition (fade, elevator ride) is running: other moves are refused.</summary>
        public bool Busy { get; internal set; }
        public event Action<AppMode> ModeChanged;

        public MuseumApi Api => backend.Api;
        public MuseumBackend Backend => backend;
        public MuseumView Immersive => immersive;
        public RoomPlan Plan => roomPlan;
        public CameraOverlay Overlay { get; private set; }
        public XROrigin Rig { get; private set; }
        public VisitorGuide Guide { get; private set; }

        DynamicMoveProvider move;
        DynamicMoveProvider.MovementDirection savedDirection;
        float savedSpeed;

        void Awake()
        {
            if (backend == null) backend = FindAnyObjectByType<MuseumBackend>();
            if (uiMaterial != null) UiKit.Material = uiMaterial;
            if (worldTextMaterial != null) UiKit.TextMaterialTemplate = worldTextMaterial;
            Rig = FindAnyObjectByType<XROrigin>();
            Guide = GetComponent<VisitorGuide>();
            if (TriggerRouter.Instance == null && FindAnyObjectByType<TriggerRouter>() == null)
                new GameObject("Trigger Router").AddComponent<TriggerRouter>();
        }

        void Start()
        {
            if (Rig != null && Rig.Camera != null) Overlay = CameraOverlay.Create(Rig.Camera, overlayMaterial, Rig.transform);
            move = Rig != null ? Rig.GetComponentInChildren<DynamicMoveProvider>(true) : null;
            if (move != null) { savedDirection = move.leftHandMovementDirection; savedSpeed = move.moveSpeed; }
            ApplyVisibility(AppMode.Organizer);
            // The session starts from the example museum (no save is loaded): its artworks face the wall plan, like the
            // ones placed from the gacha, so the visitor arriving by the elevator sees their fronts and labels.
            MuseumStation.TurnArtworksToPlan(Api);
        }

        // ---------------------------------------------------------------- mode changes (driven by the elevator)

        /// <summary>Whether the visitor view can be entered now, and the room of arrival (the first room).</summary>
        public bool CanEnterVisitor(out Room room, out string error)
        {
            room = null;
            error = null;
            if (Busy) { error = "Transition en cours…"; return false; }
            var data = Api.Data;
            if (data.rooms.Count == 0) { error = "Le musée ne contient aucune salle : créez-en une sur le plan."; return false; }
            room = data.rooms[0];
            return true;
        }

        /// <summary>
        /// Switch to the visitor view at once (the elevator calls it inside its tube): museum shown, visitor locomotion,
        /// user at the centre of <paramref name="room"/> facing its wall plan (the elevator then places them).
        /// </summary>
        public void ArriveAsVisitor(Room room)
        {
            ApplyVisibility(AppMode.Visitor);
            ConfigureLocomotion(true);
            PlaceInRoom(room, PlanFacingPoint(room));
            Guide?.OnArrived(room.id);
        }

        /// <summary>Switch to the organizer view at once (the elevator calls it inside its tube); the caller places the user.</summary>
        public void ArriveAsOrganizer()
        {
            ConfigureLocomotion(false);
            ApplyVisibility(AppMode.Organizer);
        }

        // ---------------------------------------------------------------- room to room

        /// <summary>
        /// Teleport to a room (short fade): on its elevator platform, in front of the wall plan, facing into the room,
        /// exactly where the elevator brings the visitor (<see cref="ElevatorRide"/>).
        /// </summary>
        public void TeleportToRoom(string roomId, string fromRoomId)
        {
            if (Busy || Mode != AppMode.Visitor) return;
            var data = Api.Data;
            var room = data.rooms.Find(r => r.id == roomId);
            if (room == null) return;
            StartCoroutine(QuickTransition(() =>
            {
                Guide?.OnArrived(room.id); // moves the elevator station (and its floor opening) to this room
                PlaceOnStation(room, data);
            }));
        }

        // ---------------------------------------------------------------- helpers

        IEnumerator QuickTransition(Action swap)
        {
            Busy = true;
            if (Overlay != null) yield return Overlay.FadeTo(1f, roomFade);
            try { swap(); }
            catch (Exception e) { Debug.LogException(e, this); }
            yield return null; // let physics settle on the new floor
            if (Overlay != null) yield return Overlay.FadeTo(0f, roomFade * 1.5f);
            Busy = false;
        }

        void ApplyVisibility(AppMode mode)
        {
            Mode = mode;
            bool visiting = mode == AppMode.Visitor;
            foreach (var go in organizerObjects) if (go != null) go.SetActive(!visiting);
            foreach (var go in visitorObjects) if (go != null) go.SetActive(visiting);
            if (immersive != null) immersive.gameObject.SetActive(visiting);
            ModeChanged?.Invoke(mode);
        }

        /// <summary>Visitor: continuous move in the direction the LEFT controller points, slower (comfort).</summary>
        void ConfigureLocomotion(bool visitor)
        {
            if (move == null) return;
            move.leftHandMovementDirection = visitor ? DynamicMoveProvider.MovementDirection.HandRelative : savedDirection;
            move.moveSpeed = visitor ? visitorMoveSpeed : savedSpeed;
        }

        Vector3 PlanFacingPoint(Room room)
        {
            var (centre, _, _) = RoomPlan.ChooseWall(room, Api.Data);
            return centre;
        }

        /// <summary>Feet on the room's elevator platform (<see cref="MuseumStation"/>), back to the plan wall.</summary>
        void PlaceOnStation(Room room, Museum data)
        {
            var station = MuseumStation.Compute(room, data);
            var root = immersive.transform;
            var feet = root.TransformPoint(MuseumSpatial.ToMuseum(room, new Vector3(station.spot.x, 0f, station.spot.y)));
            MoveRig(feet, root.TransformDirection(room.rotation * -station.normal));
        }

        /// <summary>Put the user's feet at the centre of a room, looking towards a museum-space point.</summary>
        void PlaceInRoom(Room room, Vector3 lookAtMuseum)
        {
            var root = immersive.transform;
            var feet = root.TransformPoint(room.position);
            var look = root.TransformPoint(lookAtMuseum) - feet;
            look.y = 0;
            if (look.sqrMagnitude < 0.01f) look = root.TransformDirection(room.rotation * Vector3.forward);
            MoveRig(feet, look);
        }

        /// <summary>Move the rig so the user's head is above <paramref name="feetWorld"/>, looking along <paramref name="forward"/>.</summary>
        public void MoveRig(Vector3 feetWorld, Vector3 forward)
        {
            if (Rig == null) return;
            var cc = Rig.GetComponent<CharacterController>();
            bool ccWasEnabled = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            forward.y = 0;
            if (forward.sqrMagnitude > 1e-4f) Rig.MatchOriginUpCameraForward(Vector3.up, forward.normalized);
            var head = Rig.Camera.transform.position - Rig.transform.position;
            head.y = 0;
            Rig.transform.position = feetWorld - head;
            Physics.SyncTransforms();
            if (cc != null) cc.enabled = ccWasEnabled;
        }
    }
}
