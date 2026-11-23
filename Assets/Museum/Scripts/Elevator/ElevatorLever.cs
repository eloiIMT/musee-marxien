using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Wall-mounted elevator lever (Assets/3D/Levier.fbx), pulled DOWN by hand: put the hand on the grip, TRIGGER to
    /// grab (the ray cannot reach it: <see cref="NearOnlyInteractable"/>), and lower it. The handle starts up (OFF,
    /// +42° about the lever's X axis) and points at the hand; past <see cref="FireAngle"/> it locks down (ON, -42°) and
    /// raises <see cref="Pulled"/>. Let go before that, it springs back. <see cref="ResetLever"/> unlocks it.
    /// This transform = point of the wall at the middle of the base; -Z = out of the wall (towards the user). The model
    /// (a floor lever) is turned so its base lies flat on the wall: its "up" points out of the wall, its OFF side up.
    /// </summary>
    public class ElevatorLever : MonoBehaviour
    {
        public const float OffAngle = 42f, OnAngle = -42f, FireAngle = -34f;
        const float ReturnSpeed = 160f, FollowSharpness = 20f;
        /// <summary>Middle of the grip crossbar above the pivot, model units (build_lever.py prints it).</summary>
        const float GripHeight = 0.96f;

        [SerializeField] GameObject model;
        [SerializeField] float modelScale = 0.3f;
        [SerializeField] string label = "";

        /// <summary>The lever reached ON (it stays locked until <see cref="ResetLever"/>).</summary>
        public event Action Pulled;
        public float Angle => angle;
        public bool IsLocked => locked;
        public bool IsHeld => hand != null;
        /// <summary>World position of the grip (to reach it, or for tests).</summary>
        public Vector3 GripPosition => grip != null ? grip.position : transform.position;

        Transform hub, grip, hand;
        NearOnlyInteractable interactable;
        TextMesh message;
        float angle = OffAngle, messageUntil;
        bool locked;

        /// <summary>For a lever created at runtime: call on an INACTIVE object, before it is activated.</summary>
        public void Configure(GameObject leverModel, float scale, string text)
        {
            model = leverModel; modelScale = scale; label = text;
        }

        void Awake()
        {
            if (model == null) { Debug.LogError("ElevatorLever: no lever model assigned.", this); enabled = false; return; }
            var m = Instantiate(model, transform);
            m.name = "Model";
            // model up (+Y) -> out of the wall (-Z); model +Z (OFF side) -> up
            m.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(-90f, 0f, 0f));
            m.transform.localScale = Vector3.one * modelScale;
            foreach (var r in m.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hub = FindDeep(m.transform, "Levier_Manche");
            if (hub == null) { Debug.LogError("ElevatorLever: model has no Levier_Manche (re-export Levier.fbx).", this); enabled = false; return; }

            // Grab volume around the grip crossbar (model units: scaled with the model, turns with the handle).
            var grab = new GameObject("Grip Grab");
            grab.transform.SetParent(hub, false);
            grab.transform.localPosition = Vector3.up * GripHeight;
            grip = grab.transform;
            var volume = grab.AddComponent<BoxCollider>();
            volume.size = new Vector3(1.0f, 0.45f, 0.45f);
            interactable = grab.AddComponent<NearOnlyInteractable>();
            interactable.Volume = volume;
            interactable.selectEntered.AddListener(OnGrab);
            interactable.selectExited.AddListener(_ => hand = null);

            // Texts on the wall, under the base (half-height 0.97 model units): clear of whatever is beside the lever.
            float below = -0.97f * modelScale - 0.03f;
            var labelText = UiKit.Text(transform, label ?? "", new Vector3(0f, below, -0.01f), 0.03f, UiKit.TextColor, TextAnchor.UpperCenter, TextAlignment.Center);
            message = UiKit.Text(transform, "", new Vector3(0f, below - 0.03f * (UiKit.LineCount(label) + 0.4f), -0.01f), 0.026f,
                                 UiKit.Error, TextAnchor.UpperCenter, TextAlignment.Center);
            Apply();
        }

        void OnGrab(SelectEnterEventArgs args)
        {
            if (locked) return;
            hand = args.interactorObject.transform;
        }

        void Update()
        {
            if (hub == null) return;
            if (locked) angle = Mathf.MoveTowards(angle, OnAngle, ReturnSpeed * 2f * Time.deltaTime);
            else if (hand != null)
            {
                // the handle points at the hand (pivot space, x ignored)
                var p = hub.parent.InverseTransformPoint(hand.position) - hub.localPosition;
                float target = Mathf.Clamp(ElevatorGeometry.AngleTowards(p), OnAngle, OffAngle);
                angle = Mathf.Lerp(angle, target, 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));
                if (angle <= FireAngle) Fire();
            }
            else angle = Mathf.MoveTowards(angle, OffAngle, ReturnSpeed * Time.deltaTime);
            Apply();

            if (message.text.Length > 0 && Time.time > messageUntil) message.text = "";
        }

        /// <summary>Lock on ON and raise <see cref="Pulled"/> (what a lever pulled by hand does). Public for tests / desktop debug.</summary>
        public void Fire()
        {
            if (locked) return;
            locked = true;
            hand = null;
            if (interactable != null) interactable.enabled = false; // drops the grab: the ride moves the user away
            Sounds.Play(Sounds.Click, 0.8f);
            Pulled?.Invoke();
        }

        /// <summary>Unlock: the handle goes back to OFF (at once if <paramref name="immediate"/>, e.g. while hidden).</summary>
        public void ResetLever(bool immediate = false)
        {
            locked = false;
            hand = null;
            if (interactable != null) interactable.enabled = true;
            if (immediate) { angle = OffAngle; Apply(); }
        }

        /// <summary>The pull is refused (e.g. empty museum): spring back and show why.</summary>
        public void Refuse(string why)
        {
            ResetLever();
            Sounds.Play(Sounds.Error, 0.6f);
            message.text = UiKit.Wrap(why, 30, 4);
            messageUntil = Time.time + 5f;
        }

        void Apply()
        {
            if (hub != null) hub.localRotation = Quaternion.Euler(angle, 0, 0);
        }

        // Built at runtime only: the lever on its wall in the Scene view (base ~1.5 x 1.94 model units, handle up).
        void OnDrawGizmos()
        {
            if (Application.isPlaying) return;
            Gizmos.color = new Color(0.9f, 0.3f, 0.2f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0, 0, -0.45f) * modelScale, new Vector3(1.5f, 1.94f, 0.9f) * modelScale);
            Gizmos.DrawLine(new Vector3(0, 0, -0.83f) * modelScale, new Vector3(0, 0.64f, -1.54f) * modelScale);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
