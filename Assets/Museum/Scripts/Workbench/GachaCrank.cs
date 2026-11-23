using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VirtualMuseum.App
{
    /// <summary>
    /// The gacha machine's knob. Hold the TRIGGER on it (ray or hand) and twist the wrist clockwise: the knob turns
    /// with the controller's roll. It is a ratchet: twisting back does not turn it, so the user can twist back and forth
    /// without letting go. Every <see cref="degreesPerCapsule"/> it asks <see cref="GachaDispenser"/> for a capsule;
    /// while a capsule waits in the cup the knob is locked.
    /// This object sits at the knob's centre, its forward axis pointing out of the machine, toward the user: a positive
    /// angle around it (left-handed) is then clockwise as the user sees it. <see cref="knob"/> is the visible mesh; it may live in another hierarchy (FBX
    /// instance): it is moved around this pivot without being reparented.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class GachaCrank : MonoBehaviour
    {
        [SerializeField] Transform knob;
        [SerializeField] GachaDispenser dispenser;
        [SerializeField] float degreesPerCapsule = 360f;
        [Tooltip("A haptic click every this many degrees.")]
        [SerializeField] float notchDegrees = 45f;

        XRSimpleInteractable interactable;
        IXRSelectInteractor turningInteractor;
        Quaternion previousHandRotation;
        Vector3 knobLocalPosition;
        Quaternion knobLocalRotation;
        float angle;
        float progress;

        /// <summary>Degrees turned toward the next capsule (0 to <see cref="degreesPerCapsule"/>).</summary>
        public float Progress => progress;

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            if (knob != null)
            {
                knobLocalPosition = transform.InverseTransformPoint(knob.position);
                knobLocalRotation = Quaternion.Inverse(transform.rotation) * knob.rotation;
                // The FBX gives the knob a static mesh collider: moving it every frame is costly and it would only
                // shadow this object's own collider.
                foreach (var c in knob.GetComponents<Collider>()) c.enabled = false;
            }
        }

        void OnEnable()
        {
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.selectExited.RemoveListener(OnSelectExited);
            turningInteractor = null;
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            turningInteractor = args.interactorObject;
            previousHandRotation = HandRotation(turningInteractor);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.interactorObject == turningInteractor) turningInteractor = null;
        }

        Quaternion HandRotation(IXRSelectInteractor interactor) => interactor.GetAttachTransform(interactable).rotation;

        void Update()
        {
            if (turningInteractor == null) return;
            var hand = HandRotation(turningInteractor);
            var delta = TwistAngle(hand * Quaternion.Inverse(previousHandRotation), transform.forward);
            previousHandRotation = hand;
            if (delta > 0f) Turn(delta);
        }

        /// <summary>Turn clockwise by <paramref name="degrees"/> (negative values are ignored: ratchet).</summary>
        public void Turn(float degrees)
        {
            if (degrees <= 0f) return;
            if (dispenser != null && dispenser.HasWaitingCapsule)
            {
                Buzz(0.08f, 0.02f); // locked until the capsule is taken
                return;
            }

            float before = progress;
            progress += degrees;
            angle = Mathf.Repeat(angle + degrees, 360f);
            ApplyKnobPose();

            if (notchDegrees > 0f && Mathf.Floor(progress / notchDegrees) > Mathf.Floor(before / notchDegrees))
                Buzz(0.25f, 0.03f);

            if (progress >= degreesPerCapsule)
            {
                progress = 0f;
                if (dispenser != null && dispenser.Dispense()) Buzz(0.7f, 0.15f);
            }
        }

        void ApplyKnobPose()
        {
            if (knob == null) return;
            var spin = Quaternion.AngleAxis(angle, Vector3.forward);
            knob.SetPositionAndRotation(transform.TransformPoint(spin * knobLocalPosition),
                                        transform.rotation * spin * knobLocalRotation);
        }

        void Buzz(float amplitude, float duration)
        {
            if (turningInteractor is XRBaseInputInteractor input) input.SendHapticImpulse(amplitude, duration);
        }

        /// <summary>Signed angle (degrees) of <paramref name="delta"/> around <paramref name="axis"/>.</summary>
        public static float TwistAngle(Quaternion delta, Vector3 axis)
        {
            var reference = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var turned = Vector3.ProjectOnPlane(delta * reference, axis);
            if (turned.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.SignedAngle(reference, turned, axis);
        }
    }
}
