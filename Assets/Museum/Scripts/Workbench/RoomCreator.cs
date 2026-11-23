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
    /// and selects it. With something selected, a small green "+" shows where a room would fit instead, and a click on
    /// the empty plan only deselects (see <see cref="Maquette"/>).
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

        void OnDisable()
        {
            HideSkeleton();
            HideMarker();
        }

        void Update()
        {
            var ray = PointingRay();
            if (ray == null) { HideSkeleton(); HideMarker(); return; }

            reason = null;
            bool onPlan = maquette.RayToMuseum(ray.Value, out var p);
            if (!onPlan) reason = "Visez le plan";
            target = MuseumGeometry.SnapXZ(p);
            if (reason == null && !maquette.IsOverBoard(maquette.MuseumToWorld(target))) reason = "Hors du plan";
            if (reason == null && MuseumGeometry.OverlapsAny(museum, null, target, 0f, DefaultSize, DefaultSize))
                reason = "Trop proche d'une autre salle (1 m min.)";
            valid = reason == null;

            if (maquette.SelectedRoomId != null)
            {
                // A room is selected: no full skeleton (it would hide the selection), only a small green "+" where a
                // room fits. The click on the empty plan still deselects; the skeleton then shows.
                HideSkeleton();
                if (valid && onPlan) ShowMarker(p); else HideMarker();
                return;
            }
            HideMarker();
            maquette.Ghost.Show(target, 0f, DefaultSize, DefaultSize, valid, reason ?? "Gâchette : créer une salle 6 × 6 m");
            showing = true;
        }

        // ---------------------------------------------------------------- "+" marker (while a room is selected)

        const float MarkerWorldSize = 0.035f;
        static Material markerMaterial;
        Transform marker;

        void ShowMarker(Vector3 museumPoint)
        {
            if (marker == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "Create Room Marker";
                Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = MarkerMaterial();
                go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                marker = go.transform;
                marker.SetParent(transform, false);
            }
            marker.gameObject.SetActive(true);
            // Flat on the plan (quad normal -Z turned to museum up = towards the user), slightly above the floors.
            marker.SetLocalPositionAndRotation(new Vector3(museumPoint.x, 0.4f, museumPoint.z), Quaternion.Euler(90f, 0f, 0f));
            marker.localScale = Vector3.one * (MarkerWorldSize / Mathf.Max(maquette.Scale, 1e-5f));
        }

        void HideMarker()
        {
            if (marker != null && marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
        }

        static Material MarkerMaterial()
        {
            if (markerMaterial != null) return markerMaterial;
            const int size = 64;
            var green = new Color(0.2f, 1f, 0.4f, 1f);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "CreateRoomMarker" };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size - 0.5f, v = (y + 0.5f) / size - 0.5f;
                bool border = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) is > 0.40f and < 0.48f;
                bool plus = (Mathf.Abs(u) < 0.05f && Mathf.Abs(v) < 0.28f) || (Mathf.Abs(v) < 0.05f && Mathf.Abs(u) < 0.28f);
                pixels[y * size + x] = border || plus ? green : Color.clear;
            }
            tex.SetPixels(pixels);
            tex.Apply(true, true);
            var urp = Shader.Find("Universal Render Pipeline/Unlit");
            if (urp != null)
            {
                markerMaterial = new Material(urp) { name = "Create Room Marker (runtime)" };
                markerMaterial.SetTexture("_BaseMap", tex);
                markerMaterial.SetFloat("_AlphaClip", 1f);
                markerMaterial.SetFloat("_Cutoff", 0.5f);
                markerMaterial.EnableKeyword("_ALPHATEST_ON");
            }
            else markerMaterial = new Material(Shader.Find("Unlit/Transparent Cutout")) { name = "Create Room Marker (runtime)" };
            markerMaterial.mainTexture = tex;
            return markerMaterial;
        }

        /// <summary>Ray of the hand pointing at the empty plan, or null (the ray is elsewhere).</summary>
        Ray? PointingRay()
        {
            if (board == null || !board.IsHovered) return null;
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
