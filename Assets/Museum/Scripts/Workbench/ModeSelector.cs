using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using static VirtualMuseum.App.GlassUi;

namespace VirtualMuseum.App
{
    /// <summary>What the gizmo of the selected room lets the user do, in the selector's order (left to right).</summary>
    public enum EditMode { Move, Resize, Link }

    /// <summary>
    /// "Glass" segmented control floating above the right controller while a room (or a connection) is selected:
    /// Déplacer · Redimensionner · Liaison (see the "Slider Hauteur Glass" design). Shown / hidden by <see cref="Maquette"/>.
    ///  - right thumbstick left / right: previous / next mode, one step per flick (no auto-repeat); the list wraps
    ///    (right on Liaison goes back to Déplacer, left on Déplacer goes to Liaison)
    /// No collider on purpose: anything grabbable this close to the controller would become the Near-Far interactor's
    /// near target and hide the right hand's ray.
    /// The right stick normally turns the rig (snap / smooth turn): turning is paused while the selector is shown,
    /// teleport (stick forward) keeps working. Without a tracked right controller it sits above the plan board.
    /// </summary>
    public class ModeSelector : MonoBehaviour
    {
        // Layout (design px, origin at the panel centre, +Y up).
        // Segments sized for the longest label ("Redimensionner" + icon).
        const float PanelW = 516, PanelH = 116, PanelR = 32;
        const float TrackW = 488, TrackH = 56, TrackY = 14;
        const float SegW = 160, SegH = 48, FooterY = -33, FooterW = 472;
        const float IconSize = 18, IconGap = 6;
        // Thumbstick flick: fire above Press, re-arm below Release.
        const float Press = 0.6f, Release = 0.3f;
        const float PillSmoothTime = 0.06f;
        // On the controller: world scale of the panel (1 = 21.6 cm wide) and height above the controller.
        const float ControllerScale = 0.6f, ControllerLift = 0.08f;

        static readonly string[] Names = { "Déplacer", "Redimensionner", "Liaison" };
        static readonly IconKind[] Kinds = { IconKind.Move, IconKind.Resize, IconKind.Link };
        static readonly string[] Hints =
        {
            "Gâchette sur une salle pour la bouger",
            "Cubes jaunes : largeur / longueur · slider : hauteur",
            "Gâchette sphère orange vers une salle",
        };

        /// <summary>Fallback place when no right controller is tracked (desktop / simulator).</summary>
        public Transform Board { get; set; }

        Maquette maquette;
        Transform controller;
        Transform pill;
        float pillX, pillVelocity;
        readonly Renderer[] icons = new Renderer[3];
        readonly TextMesh[] labels = new TextMesh[3];
        TextMesh hint;

        InputAction stick;
        bool armed = true;
        readonly List<Behaviour> pausedTurn = new();

        public static ModeSelector Create(Transform parent, Maquette owner)
        {
            var root = new GameObject("Mode Selector");
            root.transform.SetParent(parent, false);
            // Children are laid out in world metres: cancel the parent scale before building the texts.
            root.transform.localScale = Vector3.one / Mathf.Max(parent != null ? parent.lossyScale.x : 1f, 1e-5f);
            var selector = root.AddComponent<ModeSelector>();
            selector.maquette = owner;
            selector.Build();
            owner.ModeChanged += selector.Refresh;
            selector.Refresh();
            root.SetActive(false); // shown while something is selected
            return selector;
        }

        void LateUpdate()
        {
            if (controller == null) controller = FindRightController();
            if (controller != null)
            {
                // Follows the controller (parented, so it also gets the late pose update) and faces the eyes.
                if (transform.parent != controller) transform.SetParent(controller, false);
                transform.localScale = Vector3.one * (ControllerScale / Mathf.Max(controller.lossyScale.x, 1e-5f));
                transform.position = controller.position + Vector3.up * ControllerLift;
                var cam = Camera.main;
                if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);
            }
            else if (Board != null)
            {
                // Centred just above the top edge of the board, flat against it, facing the user.
                float top = Board.lossyScale.y / 2f + PanelH / 2f * PxToWorld + 0.015f;
                transform.SetPositionAndRotation(Board.position + Board.up * top - Board.forward * (Board.lossyScale.z / 2f + 0.002f),
                                                 Board.rotation);
            }
        }

        static Transform FindRightController()
        {
            foreach (var i in FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>(FindObjectsSortMode.None))
                if (i.handedness == UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Right && i.transform.parent != null)
                    return i.transform.parent;
            return null;
        }

        void Awake()
        {
            stick = new InputAction("ModeStick", InputActionType.Value, expectedControlType: "Vector2");
            stick.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
        }

        void OnEnable()
        {
            stick.Enable();
            armed = false; // ignore a stick already pushed when the selector appears
            if (maquette != null && pill != null)
            {
                pillX = PillTarget(maquette.Mode);
                pillVelocity = 0f;
                Refresh();
            }
            // The stick's X axis now picks modes: pause turning so the view does not spin at the same time.
            pausedTurn.Clear();
            foreach (var p in FindObjectsByType<SnapTurnProvider>(FindObjectsSortMode.None))
                if (p.enabled) { p.enabled = false; pausedTurn.Add(p); }
            foreach (var p in FindObjectsByType<ContinuousTurnProvider>(FindObjectsSortMode.None))
                if (p.enabled) { p.enabled = false; pausedTurn.Add(p); }
        }

        void OnDisable()
        {
            stick.Disable();
            foreach (var p in pausedTurn) if (p != null) p.enabled = true;
            pausedTurn.Clear();
        }

        void OnDestroy()
        {
            stick.Dispose();
            if (maquette != null) maquette.ModeChanged -= Refresh;
        }

        void OnTransformParentChanged()
        {
            // Destroyed with the rig? Fall back to the board next frame.
            if (transform.parent == null) controller = null;
        }

        void Update()
        {
            float x = stick.ReadValue<Vector2>().x;
            if (armed && Mathf.Abs(x) > Press)
            {
                armed = false;
                int n = Names.Length;
                int next = ((int)maquette.Mode + (x > 0 ? 1 : -1) + n) % n;
                maquette.SetMode((EditMode)next);
            }
            else if (Mathf.Abs(x) < Release) armed = true;

            pillX = Mathf.SmoothDamp(pillX, PillTarget(maquette.Mode), ref pillVelocity, PillSmoothTime);
            pill.localPosition = Px(pillX, TrackY, -0.002f);
        }

        void Refresh()
        {
            int mode = (int)maquette.Mode;
            for (int i = 0; i < 3; i++)
            {
                var c = i == mode ? TextOnFill : TextMain;
                Tint.Set(icons[i], c);
                labels[i].color = TextColor(c);
            }
            hint.text = Hints[mode];
        }

        static float PillTarget(EditMode mode) => ((int)mode - 1) * SegW;

        // ---------------------------------------------------------------- build

        void Build()
        {
            var t = transform;
            Part(t, "Rim", PanelW + 2, PanelH + 2, PanelR + 1, 0, 0, 0.001f, Rim);
            Part(t, "Panel", PanelW, PanelH, PanelR, 0, 0, 0f, Panel);
            Part(t, "Track", TrackW, TrackH, TrackH / 2, 0, TrackY, -0.001f, Track);
            pill = Part(t, "Pill", SegW, SegH, SegH / 2, 0, TrackY, -0.002f, Fill);

            for (int i = 0; i < 3; i++)
            {
                float cx = (i - 1) * SegW;

                // Icon + label centred together in the segment.
                var label = UiKit.Text(t, Names[i], Vector3.zero, LineHeight(13), TextColor(TextMain), TextAnchor.MiddleLeft, TextAlignment.Left);
                label.fontStyle = FontStyle.Bold;
                float labelW = TextWidth(label) / PxToWorld;
                float start = cx - (IconSize + IconGap + labelW) / 2f;
                label.transform.localPosition = Px(start + IconSize + IconGap, TrackY, -0.003f);
                labels[i] = label;
                icons[i] = Icon(t, Kinds[i], IconSize, start + IconSize / 2f, TrackY, -0.003f, TextMain);
            }

            float left = -FooterW / 2f, right = FooterW / 2f;
            // Joystick icon on the left, help of the current mode on the right.
            Icon(t, IconKind.Joystick, 16, left + 8, FooterY, -0.002f, TextMuted);
            hint = UiKit.Text(t, "", Px(right, FooterY, -0.002f), LineHeight(12), TextColor(TextMuted), TextAnchor.MiddleRight, TextAlignment.Right);
        }
    }
}
