using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VirtualMuseum.App
{
    /// <summary>
    /// "New room" button on the workbench. Click (trigger) to enter creation mode: a 6 × 6 m ghost follows the
    /// ray of that hand on the plan board (0.5 m grid). Trigger again places the room (green) or is refused (red).
    /// Clicking the button again cancels.
    /// </summary>
    [RequireComponent(typeof(Clickable))]
    public class RoomCreator : MonoBehaviour
    {
        const float DefaultSize = 6f;

        [SerializeField] Maquette maquette;

        Clickable button;
        Transform hand;
        InteractorHandedness handedness;
        Museum museum;
        Vector3 target;
        bool active, valid;
        string reason;

        public bool IsActive => active;

        void Awake()
        {
            button = GetComponent<Clickable>();
            button.Clicked += (_, interactor) =>
            {
                if (active) Exit("Création annulée");
                else Enter(interactor);
            };
        }

        void Enter(IXRInteractor interactor)
        {
            if (maquette == null || TriggerRouter.Instance == null) return;
            // Cast from the drawn XRI ray's origin so the ghost sits exactly at the ray's end.
            hand = interactor is UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.ICurveInteractionDataProvider curve && curve.curveOrigin != null
                ? curve.curveOrigin
                : interactor != null ? interactor.transform : Camera.main.transform;
            handedness = interactor != null ? interactor.handedness : InteractorHandedness.Right;
            museum = maquette.Api.Data;
            active = true;
            maquette.ClearSelection();
            TriggerRouter.Instance.Capture = OnTrigger;
            maquette.Toast("Visez le plan, gâchette pour poser la salle");
        }

        void Exit(string message = null)
        {
            active = false;
            maquette.Ghost.Hide();
            if (TriggerRouter.Instance != null && TriggerRouter.Instance.Capture == OnTrigger) TriggerRouter.Instance.Capture = null;
            if (message != null) maquette.Toast(message);
        }

        void OnDisable() { if (active) Exit(); }

        void Update()
        {
            if (!active || hand == null) return;
            reason = null;
            if (!maquette.RayToMuseum(new Ray(hand.position, hand.forward), out var p)) reason = "Visez le plan";
            target = MuseumGeometry.SnapXZ(p);
            if (reason == null && !maquette.IsOverBoard(maquette.MuseumToWorld(target))) reason = "Hors du plan";
            if (reason == null && MuseumGeometry.OverlapsAny(museum, null, target, 0f, DefaultSize, DefaultSize))
                reason = "Trop proche d'une autre salle (1 m min.)";
            valid = reason == null;
            maquette.Ghost.Show(target, 0f, DefaultSize, DefaultSize, valid, reason ?? "Nouvelle salle 6 × 6 m");
        }

        bool OnTrigger(InteractorHandedness pressed)
        {
            if (pressed != handedness) return false; // other hand keeps working normally
            if (button.IsHovered) return false;     // let the button's own click cancel
            if (!valid) { maquette.Toast(reason, true); return true; }
            var name = MuseumGeometry.NextRoomName(museum);
            var room = maquette.Api.CreateRoom(name, target);
            Exit($"« {name} » créée");
            maquette.SelectRoom(room.id);
            return true;
        }
    }
}
