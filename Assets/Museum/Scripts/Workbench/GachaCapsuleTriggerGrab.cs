using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VirtualMuseum.App
{
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class GachaCapsuleTriggerGrab : MonoBehaviour
    {
        static readonly List<GachaCapsuleTriggerGrab> all = new();

        XRGrabInteractable grabInteractable;
        TriggerRouter triggerRouter;
        readonly Dictionary<InteractorHandedness, float> hoverTimes = new();
        readonly Dictionary<InteractorHandedness, XRBaseInteractor> hoveringInteractors = new();
        readonly Dictionary<InteractorHandedness, XRBaseInteractor> manualInteractors = new();

        void Awake() => grabInteractable = GetComponent<XRGrabInteractable>();

        void OnEnable()
        {
            all.Add(this);
            grabInteractable.hoverEntered.AddListener(OnHoverEntered);
            grabInteractable.hoverExited.AddListener(OnHoverExited);
            triggerRouter = TriggerRouter.Instance;
            if (triggerRouter != null) triggerRouter.TriggerReleased += OnTriggerReleased;
        }

        void OnDisable()
        {
            all.Remove(this);
            grabInteractable.hoverEntered.RemoveListener(OnHoverEntered);
            grabInteractable.hoverExited.RemoveListener(OnHoverExited);
            if (triggerRouter != null) triggerRouter.TriggerReleased -= OnTriggerReleased;
            triggerRouter = null;
            hoverTimes.Clear();
            hoveringInteractors.Clear();

            foreach (var interactor in manualInteractors.Values)
                if (interactor != null && interactor.isPerformingManualInteraction)
                    interactor.EndManualInteraction();
            manualInteractors.Clear();
        }

        public static void TryGrabHoveredBy(InteractorHandedness hand)
        {
            GachaCapsuleTriggerGrab best = null;
            var bestTime = float.MinValue;
            foreach (var capsule in all)
            {
                if (capsule != null && capsule.hoverTimes.TryGetValue(hand, out var time) && time > bestTime)
                {
                    best = capsule;
                    bestTime = time;
                }
            }

            best?.BeginManualGrab(hand);
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (!(args.interactorObject is XRBaseInteractor interactor)) return;
            hoverTimes[interactor.handedness] = Time.unscaledTime;
            hoveringInteractors[interactor.handedness] = interactor;
        }

        void OnHoverExited(HoverExitEventArgs args)
        {
            hoverTimes.Remove(args.interactorObject.handedness);
            hoveringInteractors.Remove(args.interactorObject.handedness);
        }

        void BeginManualGrab(InteractorHandedness hand)
        {
            if (grabInteractable.isSelected || manualInteractors.ContainsKey(hand))
                return;
            if (!hoveringInteractors.TryGetValue(hand, out var interactor) || interactor == null)
                return;

            interactor.StartManualInteraction((IXRSelectInteractable)grabInteractable);
            if (grabInteractable.interactorsSelecting.Contains(interactor))
                manualInteractors.Add(hand, interactor);
        }

        void OnTriggerReleased(InteractorHandedness hand)
        {
            if (!manualInteractors.TryGetValue(hand, out var interactor))
                return;

            if (interactor != null && interactor.isPerformingManualInteraction)
                interactor.EndManualInteraction();
            manualInteractors.Remove(hand);
        }
    }
}
