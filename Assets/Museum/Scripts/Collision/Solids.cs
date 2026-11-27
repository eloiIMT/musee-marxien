using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// What nothing may go through: walls, floor and furniture of the workshop, the plan board, the gacha machine, and
    /// the walls, floors, artworks and labels of the life-size museum. They sit on the "Blocker" layer, which only holds
    /// solid, static geometry: interactive objects (handles, capsules, buttons...) stay on Default. Used by:
    ///  - the user's head (<see cref="PlayerCollision"/>; the joystick already collides through the CharacterController);
    ///  - grabbed objects (<see cref="HeldCollision"/>);
    ///  - the hands' rays, which stop on them: nothing can be pointed at or grabbed through a wall.
    /// Moving things are swept as spheres: a sphere does not change when it turns, so turning never pushes into a wall.
    /// </summary>
    public static class Solids
    {
        public const string LayerName = "Blocker";
        /// <summary>Gap kept between a swept sphere and the surface it stops against.</summary>
        public const float Skin = 0.005f;
        const int MaxContacts = 3;
        const int ProbeLayer = 2; // Ignore Raycast: never part of the mask

        static int layer = -1;
        static SphereCollider probe;
        static readonly Collider[] overlaps = new Collider[16];

        /// <summary>Index of the solid layer (Default, with a warning, if the project has no "Blocker" layer).</summary>
        public static int Layer
        {
            get
            {
                if (layer >= 0) return layer;
                layer = LayerMask.NameToLayer(LayerName);
                if (layer < 0)
                {
                    Debug.LogWarning($"No '{LayerName}' layer (Project Settings > Tags and Layers): solids fall back to Default.");
                    layer = 0;
                }
                return layer;
            }
        }

        public static int Mask => 1 << Layer;

        /// <summary>Puts a collider's GameObject on the solid layer.</summary>
        public static void MakeSolid(Collider collider)
        {
            if (collider != null) collider.gameObject.layer = Layer;
        }

        /// <summary>
        /// Moves a sphere of <paramref name="radius"/> from <paramref name="from"/> by <paramref name="motion"/> without
        /// entering any solid: it stops against the first one met and slides along it (corners: up to three contacts).
        /// A solid the sphere already overlaps at the start is not seen (see <see cref="Depenetrate"/>).
        /// </summary>
        /// <returns>Where the sphere's centre ends.</returns>
        public static Vector3 Slide(Vector3 from, Vector3 motion, float radius, int mask)
        {
            var position = from;
            for (int contact = 0; contact < MaxContacts; contact++)
            {
                float distance = motion.magnitude;
                if (distance < 1e-6f) break;
                var direction = motion / distance;
                if (!Physics.SphereCast(position, radius, direction, out var hit, distance + Skin, mask, QueryTriggerInteraction.Ignore))
                    return position + motion;
                float travel = Mathf.Max(0f, hit.distance - Skin);
                position += direction * travel;
                motion -= direction * travel;
                motion -= Mathf.Min(0f, Vector3.Dot(motion, hit.normal)) * hit.normal; // the rest slides along the surface
            }
            return position;
        }

        /// <summary>
        /// Smallest move that takes a sphere out of the solids it overlaps (zero when it overlaps none). Used when a sphere
        /// starts inside something (grabbed where it lay, grown bigger), which <see cref="Slide"/> cannot see.
        /// </summary>
        public static Vector3 Depenetrate(Vector3 center, float radius, int mask)
        {
            var offset = Vector3.zero;
            int count = Physics.OverlapSphereNonAlloc(center, radius, overlaps, mask, QueryTriggerInteraction.Ignore);
            if (count == 0) return offset;
            var sphere = Probe(radius);
            for (int i = 0; i < count; i++)
            {
                var other = overlaps[i];
                if (other == null) continue;
                if (Physics.ComputePenetration(sphere, center + offset, Quaternion.identity,
                                               other, other.transform.position, other.transform.rotation,
                                               out var direction, out float depth))
                    offset += direction * (depth + Skin);
            }
            System.Array.Clear(overlaps, 0, count);
            return offset;
        }

        /// <summary>
        /// Sphere collider for <see cref="Physics.ComputePenetration"/>, which ignores disabled colliders: an enabled
        /// trigger parked far below the world, on a layer no query here looks at.
        /// </summary>
        static SphereCollider Probe(float radius)
        {
            if (probe == null)
            {
                var go = new GameObject("Solids Probe") { hideFlags = HideFlags.HideAndDontSave, layer = ProbeLayer };
                go.transform.position = new Vector3(0f, -10000f, 0f);
                probe = go.AddComponent<SphereCollider>();
                probe.isTrigger = true;
            }
            probe.radius = radius;
            return probe;
        }
    }
}
