using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Animates an XR Hands hand model (Assets/3D/XRHands) posed on the XRI controller (see HandControllerSetup):
    /// - trigger: the index finger presses on the trigger (the trigger blade itself is animated by the XRI ControllerAnimator);
    /// - grip: the middle, ring and little fingers squeeze the controller (relax a little when released);
    /// - thumbstick: the thumb follows the stick cap (thumbTarget is a child of the stick).
    /// The fitted pose is the rest pose. Each finger curls about the normal of the plane of its joints, in the direction that
    /// brings its tip towards the palm (computed at start from the bone positions, independent of the bones' local axes).
    /// </summary>
    public class HandControllerAnimator : MonoBehaviour
    {
        [Header("Inputs (same field names as the XRI starter ControllerAnimator, so the editor setup can copy them)")]
        [SerializeField] XRInputValueReader<float> m_TriggerInput = new("Trigger");
        [SerializeField] XRInputValueReader<float> m_GripInput = new("Grip");

        [Header("Targets (set by the editor setup)")]
        [Tooltip("Child of the thumbstick, where the thumb tip rests: the thumb follows it when the stick tilts.")]
        [SerializeField] Transform thumbTarget;

        [Header("Ranges (degrees per joint)")]
        [Tooltip("Index: trigger released (finger slightly lifted) / fully pressed.")]
        [SerializeField] Vector2 indexCurl = new(-8f, 10f);
        [Tooltip("Middle, ring, little: grip released (relaxed) / fully pressed (fitted pose).")]
        [SerializeField] Vector2 gripCurl = new(-12f, 4f);
        [SerializeField, Range(1f, 40f)] float smoothing = 18f;

        struct Bone { public Transform t; public Quaternion rest; public Vector3 axis; }
        readonly List<Bone> index = new(), grip = new(), thumb = new();
        Transform thumbTip;
        bool ready;
        float trig, grp;

        public Transform ThumbTarget { get => thumbTarget; set => thumbTarget = value; }

        void Awake() => Setup();

        Transform Find(string part)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name.EndsWith(part)) return t;
            return null;
        }

        void Setup()
        {
            if (ready) return;
            var palm = Find("_Palm");
            void Chain(string finger, List<Bone> into)
            {
                var b = new[] { Find(finger + "Proximal"), Find(finger + "Intermediate"), Find(finger + "Distal") };
                var tip = Find(finger + "Tip");
                if (b[0] == null || b[1] == null || b[2] == null || tip == null || palm == null) return;
                var n = Vector3.Cross(b[1].position - b[0].position, tip.position - b[1].position).normalized;
                var moved = b[2].position + Quaternion.AngleAxis(5f, n) * (tip.position - b[2].position);
                if ((moved - palm.position).sqrMagnitude > (tip.position - palm.position).sqrMagnitude) n = -n;
                foreach (var t in b) into.Add(new Bone { t = t, rest = t.localRotation, axis = Quaternion.Inverse(t.rotation) * n });
            }
            Chain("Index", index);
            Chain("Middle", grip); Chain("Ring", grip); Chain("Little", grip);
            foreach (var p in new[] { "ThumbMetacarpal", "ThumbProximal", "ThumbDistal" })
            {
                var t = Find(p);
                if (t != null) thumb.Add(new Bone { t = t, rest = t.localRotation });
            }
            thumbTip = Find("ThumbTip");
            ready = true;
        }

        void OnEnable()
        {
            m_TriggerInput?.EnableDirectActionIfModeUsed();
            m_GripInput?.EnableDirectActionIfModeUsed();
        }

        void OnDisable()
        {
            m_TriggerInput?.DisableDirectActionIfModeUsed();
            m_GripInput?.DisableDirectActionIfModeUsed();
        }

        void LateUpdate()   // after the XRI ControllerAnimator has moved the stick
        {
            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            trig = Mathf.Lerp(trig, m_TriggerInput != null ? m_TriggerInput.ReadValue() : 0f, k);
            grp = Mathf.Lerp(grp, m_GripInput != null ? m_GripInput.ReadValue() : 0f, k);
            Apply(trig, grp);
        }

        /// <summary>Poses the fingers for the given input values (also used by editor previews).</summary>
        public void Apply(float triggerValue, float gripValue)
        {
            Setup();
            var ic = Mathf.Lerp(indexCurl.x, indexCurl.y, triggerValue);
            foreach (var b in index) b.t.localRotation = b.rest * Quaternion.AngleAxis(ic, b.axis);
            var gc = Mathf.Lerp(gripCurl.x, gripCurl.y, gripValue);
            foreach (var b in grip) b.t.localRotation = b.rest * Quaternion.AngleAxis(gc, b.axis);

            // thumb: back to its fitted pose, then a couple of CCD passes so the tip reaches the (tilted) stick cap
            foreach (var b in thumb) b.t.localRotation = b.rest;
            if (thumbTarget == null || thumbTip == null) return;
            for (int pass = 0; pass < 2; pass++)
                for (int i = thumb.Count - 1; i >= 0; i--)
                {
                    var j = thumb[i].t;
                    var from = thumbTip.position - j.position;
                    var to = thumbTarget.position - j.position;
                    j.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(from, to), 0.5f) * j.rotation;
                }
        }
    }
}
