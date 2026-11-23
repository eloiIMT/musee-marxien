using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Base for gizmo handles grabbed with the index TRIGGER (near or far, via the Near-Far interactor).
    /// The handle itself never moves: subclasses read the hand pose each frame, show a preview,
    /// and call the API once, on release.
    /// </summary>
    public abstract class DragHandle : MonoBehaviour
    {
        static readonly Color HoverTint = Color.white;

        protected Maquette Maquette { get; private set; }
        protected Transform Hand { get; private set; }
        public bool IsDragging => Hand != null;
        protected bool IsHovered => hovered;
        /// <summary>Origin of the drawn XRI ray of the grabbing hand (falls back to the hand itself).</summary>
        protected Transform RayTransform => rayOrigin != null ? rayOrigin : Hand;

        XRSimpleInteractable interactable;
        Renderer rend;
        Color baseColor;
        bool hovered;

        public virtual void Init(Maquette maquette, Color color)
        {
            Maquette = maquette;
            baseColor = color;
            rend = GetComponent<Renderer>();
            Tint.Set(rend, color);
        }

        /// <summary>Init for a handle living on an object whose colour is managed elsewhere (e.g. a room floor).</summary>
        public void InitWithoutTint(Maquette maquette)
        {
            Maquette = maquette;
            rend = null;
        }

        protected virtual void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            if (interactable == null) interactable = gameObject.AddComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(a =>
            {
                // A disabled handle (e.g. not available in the current edit mode) ignores the press.
                if (!isActiveAndEnabled) return;
                Hand = a.interactorObject.transform;
                // Cast from the same origin as the drawn XRI ray, so the object stays under its visible end.
                rayOrigin = a.interactorObject is ICurveInteractionDataProvider curve ? curve.curveOrigin : null;
                BeginDrag();
                Refresh();
            });
            interactable.selectExited.AddListener(a =>
            {
                if (Hand == null) return;
                Hand = null;
                if (a.isCanceled) CancelDrag(); else EndDrag();
                Refresh();
            });
            interactable.hoverEntered.AddListener(_ => { hovered = isActiveAndEnabled; Refresh(); });
            interactable.hoverExited.AddListener(_ => { hovered = false; Refresh(); });
        }

        protected virtual void Update()
        {
            if (Hand != null) Drag(Hand.position, Hand.rotation);
        }

        Vector3 lastPointer;
        Transform rayOrigin;

        /// <summary>
        /// Where the hand's ray meets the plan, in museum coordinates (y = 0). The object is moved by this point,
        /// not by the hand: the cursor stays ON the element being dragged. Keeps the last hit if the ray leaves the plane.
        /// </summary>
        protected Vector3 Pointer()
        {
            if (Hand == null) return lastPointer;
            var origin = rayOrigin != null ? rayOrigin : Hand;
            if (Maquette.RayToMuseum(new Ray(origin.position, origin.forward), out var p)) lastPointer = p;
            return lastPointer;
        }

        /// <summary>Pointer at grab time: call in BeginDrag, then add (objectPoint - this) to keep the grab offset.</summary>
        protected Vector3 GrabPointer()
        {
            lastPointer = Hand != null ? Maquette.HandToMuseum(transform.position) : Vector3.zero;
            return Pointer();
        }

        void OnDisable()
        {
            if (Hand == null) return;
            Hand = null;
            CancelDrag();
        }

        void Refresh()
        {
            if (rend != null) Tint.Set(rend, hovered || IsDragging ? Color.Lerp(baseColor, HoverTint, 0.5f) : baseColor);
        }

        protected abstract void BeginDrag();
        protected abstract void Drag(Vector3 handPosition, Quaternion handRotation);
        protected abstract void EndDrag();
        protected virtual void CancelDrag() => Maquette.Ghost.Hide();
    }
}
