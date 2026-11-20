using System.Collections;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Head-locked comfort layer, child of the XR camera:
    ///  - screen fade (to black) used by every teleport / mode change, so the user never sees the world jump;
    ///  - comfort vignette (tunnelling): the field of view narrows while the rig moves with the joystick, which
    ///    reduces peripheral optic flow, the main trigger of cybersickness with continuous locomotion.
    /// </summary>
    public class CameraOverlay : MonoBehaviour
    {
        const float FadeDistance = 0.2f, VignetteDistance = 0.25f;
        const float OpenAngle = 70f;     // half-angle: outside the Quest field of view = invisible
        const float ClosedAngle = 36f;   // half-angle while moving
        const float CloseSpeed = 140f, OpenSpeed = 70f; // degrees / second

        Renderer fade, vignette;
        Transform rig;
        Vector3 lastRigPosition;
        float angle = OpenAngle, alpha;
        static MaterialPropertyBlock block;

        /// <summary>Vignette on/off (visitor mode). The fade always works.</summary>
        public bool VignetteEnabled { get; set; } = true;
        public float FadeAlpha => alpha;

        public static CameraOverlay Create(Camera cam, Material overlayMaterial, Transform rig)
        {
            var o = new GameObject("Comfort Overlay").AddComponent<CameraOverlay>();
            o.transform.SetParent(cam.transform, false);
            o.rig = rig;
            o.lastRigPosition = rig != null ? rig.position : Vector3.zero;
            if (overlayMaterial == null) overlayMaterial = new Material(Shader.Find("Museum/Overlay"));

            var vignetteMat = new Material(overlayMaterial) { renderQueue = 3990 };
            var fadeMat = new Material(overlayMaterial) { renderQueue = 4000 };
            o.vignette = o.CreateMeshObject("Vignette", AnnulusMesh(), vignetteMat, VignetteDistance);
            o.fade = o.CreateMeshObject("Fade", QuadMesh(), fadeMat, FadeDistance);
            o.SetColor(o.fade, new Color(0, 0, 0, 0));
            o.fade.enabled = false;
            o.vignette.enabled = false;
            return o;
        }

        Renderer CreateMeshObject(string objectName, Mesh mesh, Material material, float distance)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 0, distance);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        void SetColor(Renderer r, Color c)
        {
            block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }

        // ---------------------------------------------------------------- fade

        public IEnumerator FadeTo(float target, float duration)
        {
            float start = alpha;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                SetAlpha(Mathf.Lerp(start, target, Mathf.SmoothStep(0, 1, t / duration)));
                yield return null;
            }
            SetAlpha(target);
        }

        public void SetAlpha(float a)
        {
            alpha = Mathf.Clamp01(a);
            fade.enabled = alpha > 0.001f;
            SetColor(fade, new Color(0, 0, 0, alpha));
        }

        // ---------------------------------------------------------------- vignette

        void LateUpdate()
        {
            float speed = 0f;
            if (rig != null)
            {
                var delta = rig.position - lastRigPosition;
                delta.y = 0;
                lastRigPosition = rig.position;
                // Large jumps are teleports (already hidden by the fade), not locomotion.
                if (delta.magnitude < 0.5f && Time.deltaTime > 0) speed = delta.magnitude / Time.deltaTime;
            }
            float target = VignetteEnabled && speed > 0.05f ? ClosedAngle : OpenAngle;
            angle = Mathf.MoveTowards(angle, target, (target < angle ? CloseSpeed : OpenSpeed) * Time.deltaTime);
            vignette.enabled = angle < OpenAngle - 1f;
            if (vignette.enabled)
                vignette.transform.localScale = Vector3.one * (VignetteDistance * Mathf.Tan(angle * Mathf.Deg2Rad));
        }

        /// <summary>Ring: transparent inside radius 1, soft edge to 1.35, opaque black out to 40 (covers the view).</summary>
        static Mesh AnnulusMesh()
        {
            const int segments = 64;
            float[] radii = { 1f, 1.35f, 40f };
            float[] alphas = { 0f, 1f, 1f };
            var vertices = new Vector3[segments * radii.Length];
            var colors = new Color[vertices.Length];
            for (int ring = 0; ring < radii.Length; ring++)
                for (int s = 0; s < segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    vertices[ring * segments + s] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radii[ring];
                    colors[ring * segments + s] = new Color(0, 0, 0, alphas[ring]);
                }
            var triangles = new int[(radii.Length - 1) * segments * 6];
            int k = 0;
            for (int ring = 0; ring < radii.Length - 1; ring++)
                for (int s = 0; s < segments; s++)
                {
                    int a = ring * segments + s, b = ring * segments + (s + 1) % segments;
                    int c = a + segments, d = b + segments;
                    triangles[k++] = a; triangles[k++] = c; triangles[k++] = b;
                    triangles[k++] = b; triangles[k++] = c; triangles[k++] = d;
                }
            var m = new Mesh { name = "Vignette", vertices = vertices, colors = colors, triangles = triangles };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100f); // never culled
            return m;
        }

        static Mesh QuadMesh()
        {
            var m = new Mesh
            {
                name = "Fade",
                vertices = new[] { new Vector3(-3, -3, 0), new Vector3(3, -3, 0), new Vector3(3, 3, 0), new Vector3(-3, 3, 0) },
                colors = new[] { Color.white, Color.white, Color.white, Color.white },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            return m;
        }
    }
}
