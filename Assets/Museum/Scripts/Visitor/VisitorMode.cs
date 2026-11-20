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
    ///  - Organizer: the workshop (plan board, "Vue visiteur" button) is shown, the life-size museum is hidden;
    ///  - Visitor: the life-size museum (scale 1) is shown, the workshop is hidden.
    /// Hiding the other world keeps the two representations from overlapping, whatever the organizer does with rooms.
    /// Every change of place (mode change, room to room) goes through a short fade to black (no text),
    /// so the user never sees the world slide (comfort).
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
        [SerializeField] float fadeOut = 0.45f, fadeIn = 0.5f, blackTime = 0.3f, roomFade = 0.18f;

        public AppMode Mode { get; private set; } = AppMode.Organizer;
        public bool Busy { get; private set; }
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
        Vector3 returnPosition, returnForward; // where the user stood in the workshop before teleporting
        bool hasReturnPose;

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
        }

        // ---------------------------------------------------------------- organizer -> visitor

        /// <summary>Teleport into the life-size museum: first room, facing its wall plan.</summary>
        public bool EnterVisitor(out string error)
        {
            error = null;
            if (Busy) { error = "Transition en cours…"; return false; }
            var data = Api.Data;
            if (data.rooms.Count == 0) { error = "Le musée ne contient aucune salle : créez-en une sur le plan."; return false; }

            var room = data.rooms[0];
            StartCoroutine(Transition(() =>
            {
                SaveReturnPose();
                ApplyVisibility(AppMode.Visitor);
                ConfigureLocomotion(true);
                PlaceInRoom(room, PlanFacingPoint(room));
                Guide?.OnArrived(room.id);
            }));
            return true;
        }

        // ---------------------------------------------------------------- visitor -> organizer

        public void ExitToOrganizer()
        {
            if (Busy || Mode != AppMode.Visitor) return;
            StartCoroutine(Transition(() =>
            {
                ConfigureLocomotion(false);
                ApplyVisibility(AppMode.Organizer);
                if (hasReturnPose) MoveRig(returnPosition, returnForward); // back where the user left the workshop
            }));
        }

        // ---------------------------------------------------------------- room to room

        /// <summary>Teleport to the centre of a room, facing the room we come from (short fade).</summary>
        public void TeleportToRoom(string roomId, string fromRoomId)
        {
            if (Busy || Mode != AppMode.Visitor) return;
            var data = Api.Data;
            var room = data.rooms.Find(r => r.id == roomId);
            if (room == null) return;
            var from = data.rooms.Find(r => r.id == fromRoomId);
            StartCoroutine(QuickTransition(() =>
            {
                PlaceInRoom(room, from != null ? from.position : PlanFacingPoint(room));
                Guide?.OnArrived(room.id);
            }));
        }

        // ---------------------------------------------------------------- helpers

        void SaveReturnPose()
        {
            if (Rig == null) return;
            var head = Rig.Camera.transform;
            returnPosition = new Vector3(head.position.x, Rig.transform.position.y, head.position.z);
            returnForward = head.forward;
            hasReturnPose = true;
        }

        IEnumerator Transition(Action swap)
        {
            Busy = true;
            Sounds.Play(Sounds.Whoosh, 0.7f);
            if (Overlay != null) yield return Overlay.FadeTo(1f, fadeOut);
            try { swap(); }
            catch (Exception e) { Debug.LogException(e, this); }
            yield return new WaitForSecondsRealtime(blackTime);
            if (Overlay != null) yield return Overlay.FadeTo(0f, fadeIn);
            Busy = false;
        }

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
            if (Overlay != null) Overlay.VignetteEnabled = visiting;
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
