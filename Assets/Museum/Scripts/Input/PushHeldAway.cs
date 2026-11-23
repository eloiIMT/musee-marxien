using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VirtualMuseum.App
{
    /// <summary>
    /// XRI only pushes / pulls a grabbed object along the ray (manipulation stick, Y axis) while it is away from the hand:
    /// once pulled into the hand (or grabbed near) its offset is reset and pushing the stick does nothing. This gives
    /// the object a small offset along the ray as soon as the stick is pushed forward, so XRI's manipulation takes over
    /// again and the object can be sent back out. Added by <see cref="TriggerRouter"/> to every hand Near-Far interactor.
    /// </summary>
    [RequireComponent(typeof(NearFarInteractor))]
    public class PushHeldAway : MonoBehaviour
    {
        const float Threshold = 0.5f, StartOffset = 0.08f;

        NearFarInteractor interactor;

        void Awake() => interactor = GetComponent<NearFarInteractor>();

        void Update()
        {
            if (!interactor.hasSelection) return;
            var attach = interactor.interactionAttachController;
            if (attach == null || attach.hasOffset) return;
            if (attach is not InteractionAttachController controller || !controller.useManipulationInput
                || controller.manipulationYAxisMode != InteractionAttachController.ManipulationYAxisMode.Translate) return;
            if (!controller.manipulationInput.TryReadValue(out var stick) || stick.y < Threshold) return;

            // Along the drawn ray, from the attach anchor (the hand).
            var origin = interactor.curveOrigin != null ? interactor.curveOrigin : interactor.transform;
            var from = attach.transformToFollow != null ? attach.transformToFollow.position : origin.position;
            attach.MoveTo(from + origin.forward * StartOffset);
        }
    }
}
