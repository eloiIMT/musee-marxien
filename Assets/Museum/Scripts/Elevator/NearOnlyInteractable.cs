using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Simple interactable that only a HAND can hover / grab: the controller must be within <see cref="MaxDistance"/>
    /// of the collider. The Near-Far interactor's ray (far mode) is ignored, so the object must be reached physically.
    /// </summary>
    public class NearOnlyInteractable : XRSimpleInteractable
    {
        public Collider Volume { get; set; }
        public float MaxDistance { get; set; } = 0.15f;

        /// <summary>Once grabbed, the hand may drift this far from the grip before the grab is lost.</summary>
        public float HoldDistance { get; set; } = 0.45f;

        public bool IsWithinReach(Vector3 handPosition, float distance)
        {
            if (Volume == null || !Volume.enabled) return false;
            return Vector3.Distance(Volume.ClosestPoint(handPosition), handPosition) <= distance;
        }

        public override bool IsHoverableBy(IXRHoverInteractor interactor) =>
            base.IsHoverableBy(interactor) && IsWithinReach(interactor.transform.position, MaxDistance);

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if (!base.IsSelectableBy(interactor)) return false;
            bool holding = interactorsSelecting.Contains(interactor);
            return IsWithinReach(interactor.transform.position, holding ? HoldDistance : MaxDistance);
        }
    }
}
