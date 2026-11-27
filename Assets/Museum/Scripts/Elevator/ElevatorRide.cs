using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace VirtualMuseum.App
{
    /// <summary>
    /// The elevator between the two modes, one continuous ride with no fade:
    ///  - workshop: lowering the lever (by hand) lifts the platform up a tube through the ceiling. Near the closed top,
    ///    while the user sees nothing but the tube's walls, the worlds are swapped: the user is now in the museum tube
    ///    of the first room, at the same height under its hatch (identical tube inside). The hatch slides open and the
    ///    platform keeps rising until it is flush with the floor, at the foot of the room's lever, back to the wall;
    ///  - museum: each room has a lever on the wall beside its wall plan, at head height, with the platform at its foot.
    ///    Lowering it sends the platform down; the hatch closes overhead, the worlds are swapped, and the user comes down
    ///    the workshop tube onto the floor, back to the workshop lever.
    /// The ride keeps the user's place on the platform (room-scale offset); locomotion and gravity are off meanwhile.
    /// If the user is not on the platform when the lever is lowered, they glide onto it first.
    /// </summary>
    [RequireComponent(typeof(VisitorMode))]
    public class ElevatorRide : MonoBehaviour
    {
        const float RoomFloorThickness = 0.05f; // RoomView.FloorThickness
        const float PlanCentreHeight = 1.5f;    // RoomPlan.BoardCenterY

        [Header("Workshop station (in the scene)")]
        [SerializeField] ElevatorShaft workshopShaft;
        [SerializeField] ElevatorLever workshopLever;

        [Header("Museum station (built at runtime)")]
        [Tooltip("Lever model (Assets/3D/Levier.fbx).")]
        [SerializeField] GameObject leverModel;
        [SerializeField] float leverScale = 0.3f;
        [Tooltip("Height of the museum lever (middle of its base) on the wall.")]
        [SerializeField] float leverHeight = 1.45f;
        [SerializeField] Material tubeMaterial;
        [SerializeField] Material collarMaterial;
        [SerializeField] Material platformMaterial;

        [Header("Ride")]
        [Tooltip("Head-to-ceiling distance at the swap: the closed top of the workshop tube / the closed hatch, just overhead.")]
        [SerializeField] float swapHeadroom = 0.8f;
        [SerializeField] float speed = 1.0f;
        [SerializeField] float hatchTime = 0.45f;
        [SerializeField] float boardTime = 0.5f;

        VisitorMode mode;
        ElevatorShaft museumShaft;
        ElevatorLever museumLever;
        RoomView holedRoom;
        Vector3 awayFromWall = Vector3.forward; // world direction the visitor faces on arrival: back to the plan and lever
        bool riding;
        readonly List<Behaviour> frozen = new();
        CharacterController frozenController;

        public bool Riding => riding;
        public ElevatorShaft WorkshopShaft => workshopShaft;
        public ElevatorShaft MuseumShaft => museumShaft;
        public ElevatorLever WorkshopLever => workshopLever;
        public ElevatorLever MuseumLever => museumLever;

        void Awake() => mode = GetComponent<VisitorMode>();

        void Start()
        {
            if (workshopShaft == null || workshopLever == null) { Debug.LogError("ElevatorRide: workshop shaft / lever not assigned.", this); enabled = false; return; }
            workshopLever.Pulled += () => StartCoroutine(GoUp());

            // Museum station: opening + tube + platform under the immersive museum (hidden with it in organizer mode).
            var shaftGo = new GameObject("Museum Elevator");
            shaftGo.SetActive(false);
            shaftGo.transform.SetParent(mode.Immersive.transform, false);
            museumShaft = shaftGo.AddComponent<ElevatorShaft>();
            museumShaft.Configure(ElevatorShaft.Direction.Down, workshopShaft.Radius, 0f, RoomFloorThickness, 3.6f, 0f,
                                  tubeMaterial, collarMaterial, platformMaterial);
            shaftGo.SetActive(true);

            if (mode.Plan != null)
            {
                // Child of the wall plan: shown with it (visitor mode), placed on its wall by PlaceMuseumStation.
                var leverGo = new GameObject("Museum Elevator Lever");
                leverGo.SetActive(false);
                leverGo.transform.SetParent(mode.Plan.transform, false);
                museumLever = leverGo.AddComponent<ElevatorLever>();
                museumLever.Configure(leverModel, leverScale, "Retour à l'atelier :\nabaisser le levier");
                leverGo.SetActive(true);
                museumLever.Pulled += () => StartCoroutine(GoDown());
            }

            if (mode.Guide != null) mode.Guide.RoomChanged += _ => PlaceMuseumStation(false);
            mode.Immersive.Refreshed += () => PlaceMuseumStation(false);
            mode.ModeChanged += _ => PlaceMuseumStation(false);
            PlaceMuseumStation(false);
        }

        // ---------------------------------------------------------------- museum station: beside the wall plan

        /// <summary>
        /// Lever on the plan's wall, on the plan's right, at head height; opening, tube and
        /// platform at its foot, against the wall. Left alone during a ride in the museum unless <paramref name="force"/>.
        /// </summary>
        void PlaceMuseumStation(bool force)
        {
            if (museumShaft == null || (!force && riding && mode.Mode == AppMode.Visitor)) return;
            var id = mode.Guide != null ? mode.Guide.CurrentRoomId : null;
            var rv = mode.Immersive.GetRoomView(id);
            var room = mode.Guide != null ? mode.Guide.GetRoom(id) : null;
            if (holedRoom != rv)
            {
                if (holedRoom != null) holedRoom.SetFloorHole(Vector2.zero, 0f);
                holedRoom = rv;
            }
            museumShaft.gameObject.SetActive(rv != null && room != null);
            if (rv == null || room == null) return;

            // Same layout as the markers on the organizer's plan (MuseumStation): lever always on the plan's right.
            float r = museumShaft.Radius;
            var station = MuseumStation.Compute(room, mode.Guide.Data, r);
            var normal = room.rotation * station.normal; // museum space
            int side = station.side;
            float offset = station.offset;
            var spot = station.spot;

            rv.SetFloorHole(spot, r);
            museumShaft.transform.SetPositionAndRotation(rv.transform.TransformPoint(new Vector3(spot.x, 0, spot.y)), rv.transform.rotation);
            museumShaft.HatchOpen = 1f;
            awayFromWall = -mode.Immersive.transform.TransformDirection(normal);
            if (museumLever != null) // plan frame: x = along the wall (right), z = into the wall, origin 0.03 off the wall
                museumLever.transform.SetLocalPositionAndRotation(new Vector3(side * offset, leverHeight - PlanCentreHeight, 0.03f), Quaternion.identity);
        }

        // ---------------------------------------------------------------- workshop -> museum

        IEnumerator GoUp()
        {
            if (riding || mode.Busy || mode.Mode != AppMode.Organizer) { workshopLever.ResetLever(); yield break; }
            if (!mode.CanEnterVisitor(out var room, out var error)) { workshopLever.Refuse(error); yield break; }

            Begin();
            yield return Board(workshopShaft);
            float head = HeadHeight();
            float wFrom = workshopShaft.RestHeight, wTo = workshopShaft.FarEndY - swapHeadroom - head;
            float mFrom = museumShaft.HatchY - swapHeadroom - head, mTo = 0f;
            float leg1 = wTo - wFrom;
            workshopShaft.LightOn = true;
            Sounds.Play(Sounds.Whoosh, 0.4f);

            bool swapped = false;
            float hatchStart = 0f;
            yield return Ride(leg1, mTo - mFrom, s =>
            {
                if (!swapped && s < leg1) { workshopShaft.PlatformHeight = wFrom + s; Stick(workshopShaft); return; }
                if (!swapped)
                {
                    // inside the closed workshop tube: swap to the identical museum tube, same height under its hatch
                    swapped = true;
                    var offset = HeadOffset(workshopShaft);
                    mode.ArriveAsVisitor(room);               // museum shown, current room set
                    PlaceMuseumStation(true);
                    museumShaft.HatchOpen = 0f;
                    museumShaft.LightOn = true;
                    museumShaft.PlatformHeight = mFrom + (s - leg1);
                    PutOnPlatform(museumShaft, awayFromWall, offset);
                    workshopShaft.PlatformHeight = workshopShaft.RestHeight;
                    workshopShaft.LightOn = false;
                    workshopLever.ResetLever(true);
                    hatchStart = Time.time;
                }
                museumShaft.PlatformHeight = mFrom + (s - leg1);
                museumShaft.HatchOpen = (Time.time - hatchStart) / hatchTime;
                Stick(museumShaft);
            });
            museumShaft.HatchOpen = 1f;
            museumShaft.LightOn = false;
            End();
        }

        // ---------------------------------------------------------------- museum -> workshop

        IEnumerator GoDown()
        {
            if (riding || mode.Busy || mode.Mode != AppMode.Visitor || museumShaft == null || !museumShaft.gameObject.activeInHierarchy)
            {
                museumLever.ResetLever();
                yield break;
            }

            Begin();
            yield return Board(museumShaft);
            float head = HeadHeight();
            float mFrom = 0f, mTo = museumShaft.HatchY - swapHeadroom - head;
            float wFrom = workshopShaft.FarEndY - swapHeadroom - head, wTo = workshopShaft.RestHeight;
            float leg1 = mFrom - mTo;
            museumShaft.LightOn = true;
            Sounds.Play(Sounds.Whoosh, 0.4f);

            bool swapped = false;
            float closeStart = -1f;
            yield return Ride(leg1, wFrom - wTo, s =>
            {
                if (!swapped && s < leg1)
                {
                    museumShaft.PlatformHeight = mFrom - s;
                    Stick(museumShaft);
                    // close the hatch as soon as the head is under it
                    if (closeStart < 0 && museumShaft.PlatformHeight + head < museumShaft.HatchY - 0.12f) closeStart = Time.time;
                    if (closeStart >= 0) museumShaft.HatchOpen = 1f - (Time.time - closeStart) / hatchTime;
                    return;
                }
                if (!swapped)
                {
                    // under the closed hatch: swap to the identical workshop tube, same height under its closed top
                    swapped = true;
                    var offset = HeadOffset(museumShaft);
                    mode.ArriveAsOrganizer();
                    museumShaft.PlatformHeight = 0f;
                    museumShaft.HatchOpen = 1f;
                    museumShaft.LightOn = false;
                    museumLever.ResetLever(true);
                    workshopShaft.LightOn = true;
                    workshopShaft.PlatformHeight = wFrom - (s - leg1);
                    PutOnPlatform(workshopShaft, -workshopLever.transform.forward, offset); // back to the lever
                }
                workshopShaft.PlatformHeight = wFrom - (s - leg1);
                Stick(workshopShaft);
            });
            workshopShaft.LightOn = false;
            End();
        }

        // ---------------------------------------------------------------- ride helpers

        /// <summary>
        /// One continuous move over two legs (first shaft, then the other), smooth start and stop: calls
        /// <paramref name="step"/> with the distance covered so far (0 .. leg1 + leg2), every frame.
        /// </summary>
        IEnumerator Ride(float leg1, float leg2, System.Action<float> step)
        {
            float total = Mathf.Max(0.01f, leg1 + leg2);
            float duration = Mathf.Max(3f, 1.5f * total / speed); // smoothstep: peak speed = 1.5 x average
            float nextPulse = 0f;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                step(total * ElevatorGeometry.Ease(t / duration));
                if (t >= nextPulse) { Rumble(t / duration); nextPulse = t + RumblePeriod; }
                yield return null;
            }
            step(total);
        }

        const float RumblePeriod = 0.05f;

        /// <summary>
        /// Both controllers tremble during the ride: a light shaking that grows with the platform's speed
        /// (4x(1-x) = smoothstep speed, normalized), with some noise so it feels mechanical.
        /// </summary>
        static void Rumble(float x)
        {
            float speedFactor = 4f * x * (1f - x);
            float noise = Mathf.PerlinNoise(Time.time * 9f, 0.3f);
            float amplitude = Mathf.Clamp01(0.08f + 0.3f * speedFactor * (0.6f + 0.8f * noise));
            UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics.HapticsUtility.SendHapticImpulse(
                amplitude, RumblePeriod * 1.5f, UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics.HapticsUtility.Controller.Both);
        }

        float HeadHeight()
        {
            if (mode.Rig == null) return 1.6f;
            return Mathf.Clamp(mode.Rig.Camera.transform.position.y - mode.Rig.transform.position.y, 1.0f, 2.0f);
        }

        /// <summary>Head position relative to the platform centre, in the user's own frame (x = right, y = forward).</summary>
        Vector2 HeadOffset(ElevatorShaft shaft)
        {
            if (mode.Rig == null) return Vector2.zero;
            var cam = mode.Rig.Camera.transform;
            var d = cam.position - shaft.PlatformTop; d.y = 0;
            var f = cam.forward; f.y = 0;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            var r = Vector3.Cross(Vector3.up, f);
            return new Vector2(Vector3.Dot(d, r), Vector3.Dot(d, f));
        }

        /// <summary>Put the user on a platform facing <paramref name="forward"/>, keeping their offset from its centre.</summary>
        void PutOnPlatform(ElevatorShaft shaft, Vector3 forward, Vector2 offset)
        {
            forward.y = 0;
            if (forward.sqrMagnitude < 1e-4f) forward = shaft.transform.forward;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward);
            mode.MoveRig(shaft.PlatformTop + right * offset.x + forward * offset.y, forward);
        }

        void Begin()
        {
            riding = true;
            mode.Busy = true;
            frozen.Clear();
            if (mode.Rig == null) return;
            foreach (var p in mode.Rig.GetComponentsInChildren<LocomotionProvider>())
                if (p.enabled) { p.enabled = false; frozen.Add(p); }
            frozenController = mode.Rig.GetComponent<CharacterController>();
            if (frozenController != null && frozenController.enabled) frozenController.enabled = false; else frozenController = null;
        }

        void End()
        {
            foreach (var b in frozen) if (b != null) b.enabled = true;
            frozen.Clear();
            if (frozenController != null) frozenController.enabled = true;
            frozenController = null;
            riding = false;
            mode.Busy = false;
        }

        /// <summary>
        /// Put the user on the platform: as they are if already on it; if the lever was lowered from outside the pod,
        /// a forced glide (no fade) brings their head over the middle of the platform.
        /// </summary>
        IEnumerator Board(ElevatorShaft shaft)
        {
            if (mode.Rig == null) yield break;
            var head = mode.Rig.Camera.transform.position;
            var centre = shaft.PlatformTop;
            var offset = head - centre;
            offset.y = 0;
            if (offset.magnitude > shaft.Radius - 0.15f)
            {
                var shift = -offset;                                     // head to the middle of the platform
                var start = mode.Rig.transform.position;
                float time = Mathf.Max(boardTime, shift.magnitude / 1.2f); // ~1.2 m/s at most
                for (float t = 0; t < time; t += Time.deltaTime)
                {
                    var p = start + shift * ElevatorGeometry.Ease(t / time);
                    p.y = centre.y;
                    mode.Rig.transform.position = p;
                    yield return null;
                }
                var end = start + shift; end.y = centre.y;
                mode.Rig.transform.position = end;
            }
            Stick(shaft);
        }

        /// <summary>The rig's floor on the platform's top (horizontal position unchanged).</summary>
        void Stick(ElevatorShaft shaft)
        {
            if (mode.Rig == null) return;
            var p = mode.Rig.transform.position;
            p.y = shaft.PlatformTop.y;
            mode.Rig.transform.position = p;
        }
    }
}
