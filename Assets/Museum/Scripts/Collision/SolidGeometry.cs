using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Static decor under this object becomes solid (<see cref="Solids"/>): its colliders go on the solid layer at start.
    /// On the workshop room (its FBX imports with mesh colliders), the workshop floor, the plan board and the gacha machine.
    /// Objects listed in <see cref="passThrough"/> keep no collider at all: the blue bin, whose miniatures must stay
    /// reachable by the rays.
    /// </summary>
    public class SolidGeometry : MonoBehaviour
    {
        [Tooltip("Children whose colliders are switched off (rays and objects go through them).")]
        [SerializeField] GameObject[] passThrough = new GameObject[0];

        void Awake() => Apply(transform, passThrough);

        /// <summary>Solid layer for every non-trigger collider under <paramref name="root"/>; colliders of <paramref name="skip"/> disabled.</summary>
        public static void Apply(Transform root, params GameObject[] skip)
        {
            foreach (var go in skip)
                if (go != null)
                    foreach (var c in go.GetComponentsInChildren<Collider>(true))
                        c.enabled = false;
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
                if (c.enabled && !c.isTrigger) Solids.MakeSolid(c);
        }
    }
}
