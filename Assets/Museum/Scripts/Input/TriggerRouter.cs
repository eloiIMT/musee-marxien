using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VirtualMuseum.App
{
    /// <summary>
    /// The index TRIGGER is the only button of the app:
    ///  - a trigger press is a click on what that hand is pointing at (<see cref="Clickable"/>);
    ///  - the hands' Near-Far interactors also grab (XRI select) with the trigger: the rig's default input maps select
    ///    to the grip and activate to the trigger, so the two are swapped at start. Pressing the trigger on a handle
    ///    both clicks and starts the drag; the grip does nothing.
    /// A tool can temporarily <see cref="Capture"/> all presses.
    /// </summary>
    public class TriggerRouter : MonoBehaviour
    {
        public static TriggerRouter Instance { get; private set; }
        public event Action<InteractorHandedness> TriggerReleased;

        /// <summary>When set, receives every trigger press first. Return true to consume it.</summary>
        public Func<InteractorHandedness, bool> Capture;

        InputAction left, right;
        readonly HashSet<NearFarInteractor> remapped = new();

        void Awake()
        {
            Instance = this;
            left = new InputAction("LeftTrigger", InputActionType.Button);
            left.AddBinding("<XRController>{LeftHand}/triggerPressed");
            left.AddBinding("<XRController>{LeftHand}/{TriggerButton}");
            right = new InputAction("RightTrigger", InputActionType.Button);
            right.AddBinding("<XRController>{RightHand}/triggerPressed");
            right.AddBinding("<XRController>{RightHand}/{TriggerButton}");
            left.performed += _ => Press(InteractorHandedness.Left);
            right.performed += _ => Press(InteractorHandedness.Right);
            left.canceled += _ => Release(InteractorHandedness.Left);
            right.canceled += _ => Release(InteractorHandedness.Right);
        }

        void Start() => RemapSelectToTrigger();

        void OnEnable() { left.Enable(); right.Enable(); }
        void OnDisable() { left.Disable(); right.Disable(); }
        void OnDestroy() { left.Dispose(); right.Dispose(); if (Instance == this) Instance = null; }

        /// <summary>
        /// Grab with the trigger: swap select (grip) and activate (trigger) on every hand Near-Far interactor, once.
        /// Inactive ones included (controllers not tracked yet).
        /// </summary>
        void RemapSelectToTrigger()
        {
            foreach (var i in FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (i.handedness == InteractorHandedness.None || !remapped.Add(i)) continue;
                var select = i.selectInput;
                i.selectInput = i.activateInput;
                i.activateInput = select;
            }
        }

        /// <summary>Public so tests / desktop debug can simulate a click.</summary>
        public void Press(InteractorHandedness hand)
        {
            if (Capture != null && Capture(hand)) return;
            var target = Clickable.HoveredBy(hand, out var interactor);
            if (target != null)
            {
                target.Click(interactor);
                return;
            }

            GachaCapsuleTriggerGrab.TryGrabHoveredBy(hand);
        }

        void Release(InteractorHandedness hand) => TriggerReleased?.Invoke(hand);
    }
}
