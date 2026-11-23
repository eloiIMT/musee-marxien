using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Turns a trigger press into a click on what that hand is pointing at.
    /// A tool (e.g. room creation) can temporarily <see cref="Capture"/> all trigger presses.
    /// </summary>
    public class TriggerRouter : MonoBehaviour
    {
        public static TriggerRouter Instance { get; private set; }
        public event Action<InteractorHandedness> TriggerReleased;

        /// <summary>When set, receives every trigger press first. Return true to consume it.</summary>
        public Func<InteractorHandedness, bool> Capture;

        InputAction left, right;

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

        void OnEnable() { left.Enable(); right.Enable(); }
        void OnDisable() { left.Disable(); right.Disable(); }
        void OnDestroy() { left.Dispose(); right.Dispose(); if (Instance == this) Instance = null; }

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
