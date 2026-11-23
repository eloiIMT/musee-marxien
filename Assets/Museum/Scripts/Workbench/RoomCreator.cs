using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Room creation straight on the plan, no button: while nothing is selected and a hand's ray points at an empty
    /// part of the plan board, a 6 × 6 m skeleton (the ghost) follows the ray end on the 0.5 m grid, green when the
    /// room fits, red otherwise. Clicking the skeleton (the click button, on the empty plan) creates the room there
    /// and selects it. With something selected, a click on the empty plan only deselects (see <see cref="Maquette"/>).
    /// Added by <see cref="Maquette"/> on itself.
    /// </summary>
    [RequireComponent(typeof(Maquette))]
    public class RoomCreator : MonoBehaviour
    {
        const float DefaultSize = 6f;

        Maquette maquette;
        Clickable board;
        Museum museum;
        Vector3 target;
        bool showing, valid;
        string reason;

        void Awake() => maquette = GetComponent<Maquette>();

        /// <summary>The plan board's Clickable: hovered (as closest target) = the ray is on an empty part of the plan.</summary>
        public void Init(Clickable boardClickable) => board = boardClickable;

        void Start()
        {
            maquette.Api.Changed += OnModelChanged;
            museum = maquette.Api.Data;
        }

        void OnDestroy()
        {
            if (maquette != null && maquette.View != null && maquette.View.Backend != null) maquette.Api.Changed -= OnModelChanged;
        }

        void OnModelChanged(MuseumChange _) => museum = maquette.Api.Data;

        void OnDisable() => HideSkeleton();

        void Update()
        {
            var ray = PointingRay();
            if (ray == null) { HideSkeleton(); return; }

            reason = null;
            if (!maquette.RayToMuseum(ray.Value, out var p)) reason = "Visez le plan";
            target = MuseumGeometry.SnapXZ(p);
            if (reason == null && !maquette.IsOverBoard(maquette.MuseumToWorld(target))) reason = "Hors du plan";
            if (reason == null && MuseumGeometry.OverlapsAny(museum, null, target, 0f, DefaultSize, DefaultSize))
                reason = "Trop proche d'une autre salle (1 m min.)";
            valid = reason == null;
            maquette.Ghost.Show(target, 0f, DefaultSize, DefaultSize, valid, reason ?? "Gâchette : créer une salle 6 × 6 m");
            showing = true;
        }

        /// <summary>Ray of the hand pointing at the empty plan, or null (something selected, or the ray is elsewhere).</summary>
        Ray? PointingRay()
        {
            if (board == null || !board.IsHovered || maquette.SelectedRoomId != null) return null;
            if (!board.TryGetHoveringInteractor(out var interactor)) return null;
            var origin = interactor is ICurveInteractionDataProvider curve && curve.curveOrigin != null ? curve.curveOrigin : interactor.transform;
            return new Ray(origin.position, origin.forward);
        }

        void HideSkeleton()
        {
            // The ghost is shared with the drag handles: only hide it if this tool is the one showing it.
            if (!showing) return;
            showing = false;
            if (maquette != null && maquette.Ghost != null) maquette.Ghost.Hide();
        }

        /// <summary>Click on the empty plan with nothing selected: create the room under the skeleton.</summary>
        public void Click(IXRInteractor interactor)
        {
            if (!showing) Update(); // clicked in the same frame the ray arrived
            if (!showing) return;
            if (!valid) { Sounds.Play(Sounds.Error, 0.5f); maquette.Toast(reason, true); return; }
            try
            {
                var name = MuseumGeometry.NextRoomName(museum);
                var room = maquette.Api.CreateRoom(name, target);
                HideSkeleton();
                maquette.SelectRoom(room.id);
                maquette.Toast($"« {name} » créée");
            }
            catch (ArgumentException e) { maquette.Toast(e.Message, true); }
        }
    }
}
