using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Elevator well at this transform (origin = centre of the platform at rest, floor level), built procedurally:
    ///  - a round tube going UP through a ceiling opening (workshop) or DOWN through a floor opening (museum room),
    ///    closed at its far end and seen from inside;
    ///  - a collar filling the square opening around the round tube;
    ///  - the platform the user stands on (with a collider), moved by <see cref="PlatformHeight"/>;
    ///  - DOWN only: a hatch just under the floor opening, closed while the user is below it (<see cref="HatchOpen"/>),
    ///    so that from inside, the top of this tube looks like the closed top of the workshop tube;
    ///  - a light that rides with the platform, only on during a ride.
    /// The two tubes are identical inside (same radius, material, light): the ride swaps worlds while the user is
    /// between their walls, with no fade.
    /// The ride itself (moving the user with the platform) is <see cref="ElevatorRide"/>.
    /// </summary>
    public class ElevatorShaft : MonoBehaviour
    {
        public enum Direction { Up, Down }

        const float PlatformThickness = 0.04f;
        const int Segments = 32;

        [SerializeField] Direction direction = Direction.Up;
        [Tooltip("Radius of the tube; the opening in the ceiling / floor is a square of this half-size.")]
        [SerializeField] float radius = 0.55f;
        [Tooltip("Up: height of the ceiling's underside. Down: height of the floor's top (0).")]
        [SerializeField] float openingY = 2.6f;
        [Tooltip("Thickness of the ceiling / floor slab the opening goes through.")]
        [SerializeField] float openingThickness = 0.1f;
        [Tooltip("Length of the tube beyond the slab.")]
        [SerializeField] float length = 3.4f;
        [Tooltip("Height of the platform's top at rest (on the workshop floor: a little above 0; in a museum floor: 0).")]
        [SerializeField] float restHeight = 0.04f;
        [SerializeField] Material tubeMaterial;
        [SerializeField] Material collarMaterial;
        [SerializeField] Material platformMaterial;

        public float Radius => radius;
        public float RestHeight => restHeight;
        public Transform Platform { get; private set; }

        Light tubeLight;
        Transform hatch;
        float platformHeight, hatchOpen = 1f, slabBottomY, farEndY;

        /// <summary>Settings for a shaft created at runtime: call on an INACTIVE object, before it is activated.</summary>
        public void Configure(Direction dir, float r, float opening, float thickness, float len, float rest,
                              Material tube, Material collar, Material platform)
        {
            direction = dir; radius = r; openingY = opening; openingThickness = thickness; length = len; restHeight = rest;
            tubeMaterial = tube; collarMaterial = collar; platformMaterial = platform;
        }

        void Awake()
        {
            bool up = direction == Direction.Up;
            float slabBottom = up ? openingY : openingY - openingThickness;
            float slabTop = slabBottom + openingThickness;
            float tubeStart = up ? slabBottom : slabTop;
            float tubeEnd = up ? slabTop + length : slabBottom - length;
            slabBottomY = slabBottom;
            farEndY = tubeEnd;

            // texture origin: the closed top (up) / the hatch (down), where the two tubes meet during a ride
            Part("Tube", ElevatorGeometry.Tube(radius, tubeStart, tubeEnd, Segments, true, up ? tubeEnd : slabBottom - 0.02f), tubeMaterial);
            Part("Collar", ElevatorGeometry.Collar(radius, radius, slabBottom, slabTop, Segments), collarMaterial != null ? collarMaterial : tubeMaterial);

            var platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            platform.name = "Platform";
            Destroy(platform.GetComponent<Collider>());
            platform.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f); // square slab under the round top: walkable to the collar
            platform.transform.SetParent(transform, false);
            platform.transform.localScale = new Vector3(2f * (radius - 0.01f), PlatformThickness / 2f, 2f * (radius - 0.01f));
            if (platformMaterial != null) platform.GetComponent<Renderer>().sharedMaterial = platformMaterial;
            Platform = platform.transform;

            tubeLight = new GameObject("Tube Light").AddComponent<Light>();
            tubeLight.transform.SetParent(transform, false);
            tubeLight.type = LightType.Point;
            tubeLight.range = 4f;
            tubeLight.intensity = 1.6f;
            tubeLight.color = new Color(1f, 0.95f, 0.85f);
            tubeLight.shadows = LightShadows.None;
            tubeLight.enabled = false;

            if (!up)
            {
                // seen from below, like the closed top of the workshop tube
                hatch = Part("Hatch", ElevatorGeometry.Disc(radius + 0.01f, 0f, Vector3.down, Segments), tubeMaterial).transform;
            }

            PlatformHeight = restHeight;
            HatchOpen = 1f;
        }

        /// <summary>Local height of the closed far end of the tube (top of the workshop tube, bottom of a museum tube).</summary>
        public float FarEndY => farEndY;
        /// <summary>Local height of the hatch (DOWN tubes: just under the floor slab).</summary>
        public float HatchY => slabBottomY - 0.02f;

        /// <summary>DOWN tubes: 0 = hatch closed over the tube, 1 = slid away under the floor (out of sight).</summary>
        public float HatchOpen
        {
            get => hatchOpen;
            set
            {
                hatchOpen = Mathf.Clamp01(value);
                if (hatch == null) return;
                hatch.localPosition = new Vector3(hatchOpen * (2f * radius + 0.1f), HatchY, 0f);
                hatch.gameObject.SetActive(hatchOpen < 0.999f);
            }
        }

        /// <summary>Height of the platform's top, local to this transform.</summary>
        public float PlatformHeight
        {
            get => platformHeight;
            set
            {
                platformHeight = value;
                if (Platform != null) Platform.localPosition = Vector3.up * (value - PlatformThickness / 2f);
                if (tubeLight != null) tubeLight.transform.localPosition = Vector3.up * (value + 2.1f);
            }
        }

        /// <summary>World position of the centre of the platform's top.</summary>
        public Vector3 PlatformTop => transform.TransformPoint(Vector3.up * platformHeight);

        public bool LightOn
        {
            get => tubeLight != null && tubeLight.enabled;
            set { if (tubeLight != null) tubeLight.enabled = value; }
        }

        // Built at runtime only: show the platform and the tube in the Scene view to place the shaft.
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.1f);
            Gizmos.matrix = transform.localToWorldMatrix;
            float far = direction == Direction.Up ? openingY + openingThickness + length : openingY - openingThickness - length;
            for (int i = 0; i < 24; i++)
            {
                float a0 = i * Mathf.PI / 12f, a1 = (i + 1) * Mathf.PI / 12f;
                var p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                var p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                Gizmos.DrawLine(p0 + Vector3.up * restHeight, p1 + Vector3.up * restHeight);
                Gizmos.DrawLine(p0 + Vector3.up * far, p1 + Vector3.up * far);
                if (i % 6 == 0) Gizmos.DrawLine(p0 + Vector3.up * restHeight, p0 + Vector3.up * far);
            }
        }

        GameObject Part(string partName, Mesh mesh, Material material)
        {
            var go = new GameObject(partName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
