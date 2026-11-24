using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Neon signs around the gacha knob, in the style of bent neon tubing tracing an arrow's outline:
    ///  - a curved arrow around the knob showing the turning direction (clockwise for the user), slowly turning that way;
    ///  - straight arrows pointing at the knob, poking toward it.
    /// Built at runtime (procedural meshes) under the <see cref="GachaCrank"/> pivot, whose forward axis points out of
    /// the machine toward the user. While the knob is held only the curved arrow stays; all hide while it is locked
    /// (a capsule waits in the cup).
    /// </summary>
    public sealed class GachaCrankHint : MonoBehaviour
    {
        [SerializeField] GachaCrank crank;
        [SerializeField] GachaDispenser dispenser;
        [Tooltip("Museum/NeonTube material: the bright tube.")]
        [SerializeField] Material tubeMaterial;
        [Tooltip("Museum/NeonGlow material: the additive halo around it.")]
        [SerializeField] Material glowMaterial;

        [Header("Layout (metres, around the knob centre, as the user sees it)")]
        [Tooltip("How far in front of the knob centre (toward the user) the signs are.")]
        [SerializeField] float planeForward = 0.07f; // clear of the machine's window, right of the knob
        [Tooltip("Pointing arrows: angles around the knob, 0 = right, 90 = above.")]
        [SerializeField] float[] angles = { 40f, 140f, 230f }; // lower right is the instructions sign
        [SerializeField] float tipDistance = 0.18f;
        [SerializeField] float ringInnerRadius = 0.125f;
        [SerializeField] float ringOuterRadius = 0.155f;
        [Tooltip("Where the curved arrow starts (its gap faces the instructions sign), it then runs clockwise.")]
        [SerializeField] float ringStartAngle = -80f;
        [SerializeField] float ringSweep = 255f;

        [Header("Animation")]
        [SerializeField] float pokeDistance = 0.015f;
        [SerializeField] float pokeSpeed = 4f;
        [SerializeField] float pulseSpeed = 3f;
        [Tooltip("Degrees per second the curved arrow turns, clockwise, swinging back after this many degrees.")]
        [SerializeField] float ringSpeed = 40f;
        [SerializeField] float ringSwing = 30f;

        const float TubeRadius = 0.0032f;
        const float GlowRadius = 0.011f;
        const float CornerRadius = 0.006f;
        const float ArrowLength = 0.1f;

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        readonly List<(Transform arrow, Vector3 tip, Vector3 direction)> arrows = new();
        readonly List<Renderer> arrowRenderers = new();
        readonly List<Renderer> renderers = new();
        readonly List<Mesh> meshes = new();
        Transform ring;
        MaterialPropertyBlock tubeBlock, glowBlock;
        bool arrowsShown = true, ringShown = true;

        void Awake()
        {
            if (crank == null) crank = GetComponentInParent<GachaCrank>();
            if (tubeMaterial == null || glowMaterial == null)
            {
                Debug.LogError("Gacha crank hint needs its neon tube and glow materials.", this);
                enabled = false;
                return;
            }
            tubeBlock = new MaterialPropertyBlock();
            glowBlock = new MaterialPropertyBlock();

            // Work in metres whatever the machine's scale.
            var parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
            transform.localScale = Vector3.one / Mathf.Max(parentScale, 1e-5f);
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Curved arrow. Shapes are drawn as the user sees them; the user faces -Z, so their right is local -X.
            var ringShape = Mirror(NeonOutlineMesh.CircularArrow(ringInnerRadius, ringOuterRadius, ringStartAngle, ringSweep,
                                                                 headDeg: 22f, headFlare: 0.014f));
            ring = new GameObject("Neon Turn Arrow").transform;
            ring.SetParent(transform, false);
            ring.localPosition = Vector3.forward * planeForward;
            AddSign(ring, ringShape, null);

            // Straight arrows pointing at the knob.
            var arrowShape = NeonOutlineMesh.StraightArrow(ArrowLength, headLength: 0.038f, headHalfWidth: 0.032f, shaftHalfWidth: 0.011f);
            foreach (var angle in angles)
            {
                float a = angle * Mathf.Deg2Rad;
                var radial = new Vector3(-Mathf.Cos(a), Mathf.Sin(a), 0f);
                var tip = radial * tipDistance + Vector3.forward * planeForward;
                var arrow = new GameObject("Neon Arrow").transform;
                arrow.SetParent(transform, false);
                arrow.localRotation = Quaternion.FromToRotation(Vector3.up, -radial); // shape points +Y
                arrow.localPosition = tip;
                AddSign(arrow, arrowShape, arrowRenderers);
                arrows.Add((arrow, tip, -radial));
            }
        }

        void OnDestroy()
        {
            foreach (var m in meshes) if (m != null) Destroy(m);
        }

        void AddSign(Transform parent, List<Vector2> shape, List<Renderer> group)
        {
            AddPart(parent, NeonOutlineMesh.Build(shape, TubeRadius, CornerRadius, "Neon Tube"), tubeMaterial, group);
            AddPart(parent, NeonOutlineMesh.Build(shape, GlowRadius, CornerRadius, "Neon Glow"), glowMaterial, group);
        }

        void AddPart(Transform parent, Mesh mesh, Material material, List<Renderer> group)
        {
            meshes.Add(mesh);
            var part = new GameObject(mesh.name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = part.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            renderers.Add(r);
            group?.Add(r);
        }

        static List<Vector2> Mirror(List<Vector2> shape)
        {
            for (int i = 0; i < shape.Count; i++) shape[i] = new Vector2(-shape[i].x, shape[i].y);
            return shape;
        }

        void Update()
        {
            bool locked = dispenser != null && dispenser.HasWaitingCapsule;
            bool turning = crank != null && crank.IsTurning;
            SetShown(ref ringShown, !locked, renderers, except: arrowRenderers);
            SetShown(ref arrowsShown, !locked && !turning, arrowRenderers, except: null);
            if (locked) return;

            float t = Time.time;
            for (int i = 0; i < arrows.Count; i++)
            {
                var (arrow, tip, direction) = arrows[i];
                // Back off, then poke at the knob; each arrow a little after the previous one.
                float poke = 0.5f - 0.5f * Mathf.Cos(t * pokeSpeed - i * 0.6f);
                arrow.localPosition = tip - direction * (pokeDistance * (1f - poke));
            }

            // Turn clockwise for the user (positive around forward, see GachaCrank), then ease back and go again.
            float swing = ringSwing > 0f ? Mathf.Repeat(t * ringSpeed, ringSwing * 1.6f) : 0f;
            float ringAngle = swing <= ringSwing ? swing : ringSwing * (1f - (swing - ringSwing) / (ringSwing * 0.6f));
            ring.localRotation = Quaternion.AngleAxis(ringAngle, Vector3.forward);

            float pulse = 0.5f + 0.5f * Mathf.Sin(t * pulseSpeed);
            tubeBlock.SetFloat(IntensityId, Mathf.Lerp(0.9f, 1.1f, pulse));
            glowBlock.SetFloat(IntensityId, Mathf.Lerp(0.7f, 1.3f, pulse));
            foreach (var r in renderers) r.SetPropertyBlock(r.sharedMaterial == tubeMaterial ? tubeBlock : glowBlock);
        }

        static void SetShown(ref bool shown, bool show, List<Renderer> group, List<Renderer> except)
        {
            if (show == shown) return;
            shown = show;
            foreach (var r in group)
                if (except == null || !except.Contains(r)) r.enabled = show;
        }
    }
}
