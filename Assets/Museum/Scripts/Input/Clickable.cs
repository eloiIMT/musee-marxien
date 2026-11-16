using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Something the user points at with the ray and "clicks" with the TRIGGER.
    /// XRI's select is bound to the grip (used for grabbing handles), so trigger clicks are routed
    /// by <see cref="TriggerRouter"/> to the most recently hovered Clickable of the same hand.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Clickable : MonoBehaviour
    {
        public event Action<Clickable, IXRInteractor> Clicked;
        public event Action<bool> HoverChanged;

        readonly Dictionary<InteractorHandedness, float> hoverStart = new();
        readonly Dictionary<InteractorHandedness, IXRInteractor> hovering = new();
        static readonly List<Clickable> all = new();

        public bool IsHovered => hoverStart.Count > 0;
        public XRSimpleInteractable Interactable { get; private set; }

        void Awake()
        {
            Interactable = GetComponent<XRSimpleInteractable>();
            if (Interactable == null) Interactable = gameObject.AddComponent<XRSimpleInteractable>();
            Interactable.hoverEntered.AddListener(OnHoverEntered);
            Interactable.hoverExited.AddListener(OnHoverExited);
        }

        void OnEnable() => all.Add(this);

        void OnDisable()
        {
            all.Remove(this);
            if (hoverStart.Count > 0) { hoverStart.Clear(); hovering.Clear(); HoverChanged?.Invoke(false); }
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            bool was = IsHovered;
            hoverStart[args.interactorObject.handedness] = Time.unscaledTime;
            hovering[args.interactorObject.handedness] = args.interactorObject;
            if (!was) HoverChanged?.Invoke(true);
        }

        void OnHoverExited(HoverExitEventArgs args)
        {
            hoverStart.Remove(args.interactorObject.handedness);
            hovering.Remove(args.interactorObject.handedness);
            if (!IsHovered) HoverChanged?.Invoke(false);
        }

        public void Click(IXRInteractor interactor) => Clicked?.Invoke(this, interactor);

        /// <summary>Most recently hovered Clickable for a hand (the one under its ray), or null.</summary>
        public static Clickable HoveredBy(InteractorHandedness hand, out IXRInteractor interactor)
        {
            Clickable best = null;
            float bestTime = float.MinValue;
            interactor = null;
            foreach (var c in all)
            {
                if (c.hoverStart.TryGetValue(hand, out float t) && t > bestTime)
                {
                    best = c;
                    bestTime = t;
                    interactor = c.hovering[hand];
                }
            }
            return best;
        }
    }
}
