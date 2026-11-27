using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Something the user points at with the ray and "clicks" with the index TRIGGER (the app's only button).
    /// XRI's select is also the trigger (grabbing handles, see TriggerRouter); clicks are routed by <see cref="TriggerRouter"/> to the
    /// Clickable that is that hand's closest target (the one a grab would take).
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
            bool was = IsHovered;
            hoverStart.Clear();
            hovering.Clear();
            if (was) HoverChanged?.Invoke(false);
        }

        // XRI hovers every object inside the ray's cone (cone cast): neighbouring rooms, the plan board behind a room,
        // small buttons next to each other. Only the hand's closest target counts as hovered (and gets the click),
        // so `hovering` holds XRI's raw hovers and `hoverStart` only the hands for which this is the closest target.
        void OnHoverEntered(HoverEnterEventArgs args)
        {
            hovering[args.interactorObject.handedness] = args.interactorObject;
            UpdateEffectiveHover();
        }

        void OnHoverExited(HoverExitEventArgs args)
        {
            hovering.Remove(args.interactorObject.handedness);
            UpdateEffectiveHover();
        }

        void Update()
        {
            if (hovering.Count > 0 || hoverStart.Count > 0) UpdateEffectiveHover();
        }

        static readonly List<InteractorHandedness> hands = new();

        void UpdateEffectiveHover()
        {
            bool was = IsHovered;
            hands.Clear();
            hands.AddRange(hoverStart.Keys);
            foreach (var hand in hands)
                if (!hovering.ContainsKey(hand)) hoverStart.Remove(hand);
            foreach (var pair in hovering)
            {
                bool closest = ClosestTarget(pair.Value) == (IXRInteractable)Interactable;
                if (closest && !hoverStart.ContainsKey(pair.Key)) hoverStart[pair.Key] = Time.unscaledTime;
                else if (!closest) hoverStart.Remove(pair.Key);
            }
            if (IsHovered != was) HoverChanged?.Invoke(IsHovered);
        }

        static readonly Dictionary<IXRInteractor, IXRInteractable> closestCache = new();
        static readonly List<IXRInteractable> targets = new();
        static int closestFrame = -1;

        /// <summary>First of the interactor's valid targets (XRI sorts them, closest first), cached per frame.</summary>
        static IXRInteractable ClosestTarget(IXRInteractor interactor)
        {
            if (closestFrame != Time.frameCount) { closestCache.Clear(); closestFrame = Time.frameCount; }
            if (closestCache.TryGetValue(interactor, out var cached)) return cached;
            IXRInteractable first = null;
            if (interactor is XRBaseInteractor b)
            {
                targets.Clear();
                b.GetValidTargets(targets);
                if (targets.Count > 0) first = targets[0];
            }
            closestCache[interactor] = first;
            return first;
        }

        public void Click(IXRInteractor interactor) => Clicked?.Invoke(this, interactor);

        /// <summary>The interactor of the hand that most recently started hovering this (as its closest target).</summary>
        public bool TryGetHoveringInteractor(out IXRInteractor interactor)
        {
            interactor = null;
            float best = float.MinValue;
            foreach (var pair in hoverStart)
                if (pair.Value > best && hovering.TryGetValue(pair.Key, out var i)) { best = pair.Value; interactor = i; }
            return interactor != null;
        }

        /// <summary>The interactor of <paramref name="hand"/> if this is that hand's hovered target.</summary>
        public bool TryGetHoveringInteractor(InteractorHandedness hand, out IXRInteractor interactor)
        {
            interactor = null;
            return hoverStart.ContainsKey(hand) && hovering.TryGetValue(hand, out interactor);
        }

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
