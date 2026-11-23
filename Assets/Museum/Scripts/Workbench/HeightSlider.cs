using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using static VirtualMuseum.App.GlassUi;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Horizontal "glass" slider for the wall height of the selected room (2 to 8 m, 0.5 m steps), drawn flat on top of
    /// the room's bottom wall (the one lowest on the board), facing the user. Shown in the Height mode.
    ///  - GRIP on the slider and slide along it: preview with the ghost outline, applied on release
    ///  - TRIGGER on the track: jump straight to that height
    /// Sizes are design px (360 × 124, see <see cref="GlassUi"/>); the root keeps a world scale of 1.
    /// </summary>
    public class HeightSlider : DragHandle
    {
        // Layout (design px, origin at the panel centre, +Y up).
        const float PanelW = 360, PanelH = 124, PanelR = 32, Padding = 20;
        const float TrackW = 320, TrackH = 44, TrackY = -20, Knob = 44;
        const float HeaderY = 28;

        static float Min => MuseumGeometry.MinRoomSize;
        static float Max => MuseumGeometry.MaxRoomHeight;
        static float Step => MuseumGeometry.GridStep;

        Renderer panel;
        MeshFilter fill;
        Transform grip;
        TextMesh value;
        readonly List<(Renderer r, float metres)> ticks = new();

        Room room;
        float shown = -1f, dragValue;

        /// <summary>Builds the slider under the gizmo. Call once; place it each frame with <see cref="Place"/>.</summary>
        public static HeightSlider Create(Transform parent, Maquette owner)
        {
            var root = new GameObject("Height Slider");
            root.transform.SetParent(parent, false);
            // Children are laid out in world metres: cancel the miniature scale before building the texts.
            root.transform.localScale = Vector3.one / Mathf.Max(parent.lossyScale.x, 1e-5f);
            root.AddComponent<BoxCollider>().size = new Vector3(PanelW * PxToWorld, PanelH * PxToWorld, 0.01f);
            var slider = root.AddComponent<HeightSlider>();
            slider.InitWithoutTint(owner);
            slider.Build();
            return slider;
        }

        protected override void Awake()
        {
            base.Awake();
            var clickable = gameObject.AddComponent<Clickable>();
            clickable.Clicked += (_, interactor) => OnTriggerClick(interactor);
        }

        void Build()
        {
            var t = transform;
            Part(t, "Rim", PanelW + 2, PanelH + 2, PanelR + 1, 0, 0, 0.001f, Rim);
            panel = Part(t, "Panel", PanelW, PanelH, PanelR, 0, 0, 0f, Panel).GetComponent<Renderer>();
            Part(t, "Track", TrackW, TrackH, TrackH / 2, 0, TrackY, -0.001f, Track);

            fill = Part(t, "Fill", Knob, TrackH, TrackH / 2, 0, 0, -0.002f, Fill).GetComponent<MeshFilter>();
            grip = Part(t, "Grip", 4, 20, 2, 0, TrackY, -0.003f, Grip);

            for (int m = Mathf.CeilToInt(Min + 1); m < Max; m++)
            {
                var tick = Part(t, $"Tick {m} m", 2, 14, 1, TickX(m), TrackY, -0.003f, TickOnTrack);
                ticks.Add((tick.GetComponent<Renderer>(), m));
            }

            float left = -PanelW / 2 + Padding, right = PanelW / 2 - Padding;
            UiKit.Text(t, "Hauteur", Px(left, HeaderY, -0.002f), LineHeight(15), TextColor(TextMuted), TextAnchor.MiddleLeft, TextAlignment.Left);
            value = UiKit.Text(t, "", Px(right, HeaderY, -0.002f), LineHeight(22), TextColor(TextMain), TextAnchor.MiddleRight, TextAlignment.Right);
            value.fontStyle = FontStyle.Bold;
        }

        /// <summary>Put the slider on top of the bottom wall of the room, facing the user, upright on the board.</summary>
        public void Place(Room data, Transform roomTransform, Transform museum)
        {
            float hw = data.width / 2f, hl = data.length / 2f;
            // The "bottom" wall is the one whose middle is lowest on the board (museum -Z), whatever the room yaw.
            var best = Vector3.zero;
            float bestZ = float.MaxValue;
            foreach (var mid in new[] { new Vector3(0, 0, -hl), new Vector3(0, 0, hl), new Vector3(-hw, 0, 0), new Vector3(hw, 0, 0) })
            {
                float z = (roomTransform.localRotation * mid).z;
                if (z < bestZ) { bestZ = z; best = mid; }
            }
            // Slightly above the wall top so it never z-fights with it.
            transform.localPosition = best + Vector3.up * (data.height + 0.002f / Mathf.Max(museum.lossyScale.x, 1e-5f));
            // Local -Z towards the user (museum +Y), local +Y up the board (museum +Z).
            transform.rotation = museum.rotation * Quaternion.LookRotation(Vector3.down, Vector3.forward);
            transform.localScale = Vector3.one / Mathf.Max(transform.parent.lossyScale.x, 1e-5f);

            if (!IsDragging) Show(data.height);
            Tint.Set(panel, IsHovered || IsDragging ? PanelHover : Panel);
        }

        // ---------------------------------------------------------------- interaction

        protected override void BeginDrag()
        {
            room = Maquette.Api.GetRoom(Maquette.SelectedRoomId);
            if (room != null) dragValue = room.height;
        }

        protected override void Drag(Vector3 handPosition, Quaternion handRotation)
        {
            if (room == null) return;
            var ray = RayTransform;
            float v = ValueAt(new Ray(ray.position, ray.forward), handPosition);
            dragValue = v;
            Show(v);
            // Outline only: the value is already on the slider.
            Maquette.Ghost.Show(room.position, MuseumGeometry.Yaw(room.rotation), room.width, room.length, true,
                                null, null, v);
        }

        protected override void EndDrag()
        {
            Maquette.Ghost.Hide();
            Apply(room, dragValue);
        }

        void OnTriggerClick(IXRInteractor interactor)
        {
            if (IsDragging || interactor == null) return;
            var r = Maquette.Api.GetRoom(Maquette.SelectedRoomId);
            if (r == null) return;
            var origin = interactor is ICurveInteractionDataProvider curve && curve.curveOrigin != null
                ? curve.curveOrigin : interactor.transform;
            float v = ValueAt(new Ray(origin.position, origin.forward), origin.position);
            Show(v);
            Apply(r, v);
        }

        void Apply(Room r, float height)
        {
            if (r == null || Mathf.Approximately(height, r.height)) return;
            Maquette.Api.ResizeRoom(r.id, r.width, r.length, height);
        }

        /// <summary>Height under the ray on the track (or under the hand, projected, if the ray misses the slider plane).</summary>
        float ValueAt(Ray worldRay, Vector3 fallback)
        {
            var plane = new Plane(-transform.forward, transform.position);
            var hit = plane.Raycast(worldRay, out float d) ? worldRay.GetPoint(d) : plane.ClosestPointOnPlane(fallback);
            // Root has a world scale of 1: local units are metres. The grip centre sits Knob/2 inside the fill edge.
            float x = transform.InverseTransformPoint(hit).x / PxToWorld + TrackW / 2;
            float t = Mathf.Clamp01((x - Knob / 2) / (TrackW - Knob));
            return Mathf.Clamp(MuseumGeometry.Snap(Min + t * (Max - Min), Step), Min, Max);
        }

        // ---------------------------------------------------------------- visuals

        void Show(float metres)
        {
            if (Mathf.Approximately(metres, shown)) return;
            shown = metres;
            float t = Mathf.InverseLerp(Min, Max, metres);
            float w = Knob + (TrackW - Knob) * t;
            fill.sharedMesh = RoundedRect(w * PxToWorld, TrackH * PxToWorld, TrackH / 2 * PxToWorld, fill.sharedMesh);
            fill.transform.localPosition = Px(-TrackW / 2 + w / 2, TrackY, -0.002f);
            grip.localPosition = Px(-TrackW / 2 + w - Knob / 2, TrackY, -0.003f);
            foreach (var (r, m) in ticks) Tint.Set(r, m <= metres ? TickOnFill : TickOnTrack);
            value.text = metres.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))
                         + $"<size=44><color=#{ColorUtility.ToHtmlStringRGB(TextColor(TextMuted))}> m</color></size>";
        }

        static float TickX(float metres) => -TrackW / 2 + Knob / 2 + (TrackW - Knob) * Mathf.InverseLerp(Min, Max, metres);
    }
}
