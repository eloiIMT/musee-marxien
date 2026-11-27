using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualMuseum.App
{
    /// <summary>Look of the life-size museum's ceilings (set on the immersive <see cref="MuseumView"/>).</summary>
    [Serializable]
    public class CeilingStyle
    {
        [Tooltip("White plaster: flat ceiling, cornices and the skylight well.")]
        public Material plaster;
        [Tooltip("Skylight pane model (frame + glass), lying flat, centred (Assets/3D/GlassWindow.fbx).")]
        public GameObject skylightPane;
        [Tooltip("Frosted glass of the panes (matte, luminous).")]
        public Material glass;
        [Tooltip("Frame of the panes.")]
        public Material frame;
        [Tooltip("Distance between the walls and the skylight opening, as a fraction of the room's smaller side.")]
        [Range(0.1f, 0.4f)] public float skylightMargin = 0.22f;
        [Tooltip("Depth of the light well between the ceiling and the glass (m).")]
        public float wellDepth = 0.45f;
    }

    /// <summary>
    /// Ceiling of one room of the life-size museum, after a classic gallery: a moulded cornice along the walls, a flat
    /// plaster ceiling and, in the centre, a recessed skylight (moulded frame, light well, frosted panes) that spreads
    /// soft daylight into the room. Procedural (rebuilt when the room is resized). Casts no shadow: the room's light
    /// comes from <see cref="SkylightLighting"/> (sun from above + even ambient), as if through the skylight, without any
    /// extra realtime light (cheap on Quest).
    /// </summary>
    public class RoomCeiling : MonoBehaviour
    {
        // Cornice along the walls, (inset from the wall, height relative to the ceiling) in metres, from the wall's
        // bottom edge inwards and up to the ceiling. Faces point to the room (see AddRing).
        static readonly Vector2[] Cornice = BuildCornice();
        const float MinSkylight = 1.2f;   // smallest opening worth a skylight (m)
        const float GlassInset = 0.08f;   // the well's top cove overlaps the glass by this much

        CeilingStyle style;
        Mesh mesh;
        Transform panesRoot;
        Vector3 builtSize = new(-1f, -1f, -1f);
        Vector3 paneSize, paneCentre;
        bool paneMeasured;

        public void Init(CeilingStyle ceilingStyle)
        {
            style = ceilingStyle;
            mesh = new Mesh { name = "Ceiling" };
            var go = new GameObject("Plafond + moulures");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = style.plaster;
            r.shadowCastingMode = ShadowCastingMode.Off;

            panesRoot = new GameObject("Verrière").transform;
            panesRoot.SetParent(transform, false);
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        /// <summary>Fit the ceiling to a room of inner size <paramref name="width"/> × <paramref name="length"/> (between
        /// the walls' inner faces) and <paramref name="height"/>.</summary>
        public void Build(float width, float length, float height)
        {
            var size = new Vector3(width, height, length);
            if (size == builtSize) return;
            builtSize = size;

            float a = width / 2f, b = length / 2f, small = Mathf.Min(width, length);
            float corniceDepth = Cornice[^1].x;
            float margin = Mathf.Max(style.skylightMargin * small, corniceDepth + 0.4f);
            bool skylight = small - 2f * margin >= MinSkylight;

            // One profile from the wall to the glass: cornice, flat ceiling, skylight frame, well, cove under the glass.
            var profile = new List<Vector2>();
            foreach (var p in Cornice) profile.Add(p);
            if (skylight) foreach (var p in SkylightProfile(margin, style.wellDepth)) profile.Add(p);
            else profile.Add(new Vector2(Mathf.Min(a, b), 0f)); // closed flat ceiling

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            AddRing(a, b, height, profile, vertices, normals, uvs, triangles);
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            float glassY = height + (skylight ? style.wellDepth : 0f);
            float openA = a - margin - GlassInset, openB = b - margin - GlassInset;
            BuildPanes(skylight, 2f * openA + 0.02f, 2f * openB + 0.02f, glassY);
        }

        // ---------------------------------------------------------------- skylight panes

        void BuildPanes(bool show, float openWidth, float openLength, float y)
        {
            for (int i = panesRoot.childCount - 1; i >= 0; i--) Destroy(panesRoot.GetChild(i).gameObject);
            if (!show || style.skylightPane == null) return;
            if (!paneMeasured) MeasurePane();
            if (paneSize.x <= 0f || paneSize.z <= 0f) return;

            // As many panes as fit at roughly their own size, stretched to fill the opening exactly.
            // Rotated a quarter turn when the opening's proportions match the pane better that way.
            bool turn = openWidth > openLength != paneSize.x > paneSize.z;
            float pw = turn ? paneSize.z : paneSize.x, pl = turn ? paneSize.x : paneSize.z;
            int nx = Mathf.Max(1, Mathf.RoundToInt(openWidth / pw));
            int nz = Mathf.Max(1, Mathf.RoundToInt(openLength / pl));
            float cw = openWidth / nx, cl = openLength / nz;
            for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                var cell = new GameObject($"Vitre {ix + 1}-{iz + 1}").transform;
                cell.SetParent(panesRoot, false);
                cell.localPosition = new Vector3(-openWidth / 2f + cw * (ix + 0.5f), y + paneSize.y / 2f,
                                                 -openLength / 2f + cl * (iz + 0.5f));
                cell.localRotation = turn ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
                // scale in the pane's own frame (x, z swapped when turned)
                cell.localScale = turn ? new Vector3(cl / paneSize.x, 1f, cw / paneSize.z)
                                       : new Vector3(cw / paneSize.x, 1f, cl / paneSize.z);
                var pane = Instantiate(style.skylightPane, cell, false);
                pane.transform.localPosition -= paneCentre;
                foreach (var r in pane.GetComponentsInChildren<Renderer>())
                {
                    bool isGlass = r.name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0;
                    var m = isGlass ? style.glass : style.frame;
                    if (m != null)
                    {
                        var mats = r.sharedMaterials;
                        for (int k = 0; k < mats.Length; k++) mats[k] = m;
                        r.sharedMaterials = mats;
                    }
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                foreach (var c in pane.GetComponentsInChildren<Collider>()) Destroy(c);
            }
        }

        /// <summary>Size and centre of the pane model's meshes in its own frame.</summary>
        void MeasurePane()
        {
            paneMeasured = true;
            bool any = false;
            var bounds = new Bounds();
            foreach (var mf in style.skylightPane.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                // prefab asset: the root has no parent, so this is the frame an instance gets under its cell
                var m = mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { bounds = new Bounds(corner, Vector3.zero); any = true; }
                    else bounds.Encapsulate(corner);
                }
            }
            paneSize = bounds.size;
            paneCentre = bounds.center;
        }

        // ---------------------------------------------------------------- profiles

        /// <summary>Crown moulding: a fillet and bead on the wall, a large concave cove, steps into the ceiling.</summary>
        static Vector2[] BuildCornice()
        {
            var p = new List<Vector2>
            {
                new(0f, -0.34f), new(0.012f, -0.34f), new(0.012f, -0.315f), // fillet on the wall
                new(0.022f, -0.315f),
            };
            // small convex bead
            for (int i = 0; i <= 4; i++)
            {
                float t = Mathf.PI * i / 4f;
                p.Add(new Vector2(0.022f + 0.012f * Mathf.Sin(t), -0.303f - 0.012f * Mathf.Cos(t)));
            }
            p.Add(new Vector2(0.03f, -0.285f));
            // large concave cove up to the ceiling side
            const float x0 = 0.03f, y0 = -0.27f, x1 = 0.2f, y1 = -0.06f;
            for (int i = 0; i <= 10; i++)
            {
                float t = Mathf.PI / 2f * i / 10f;
                p.Add(new Vector2(x1 - (x1 - x0) * Mathf.Cos(t), y0 + (y1 - y0) * Mathf.Sin(t)));
            }
            p.Add(new Vector2(0.2f, -0.04f));
            p.Add(new Vector2(0.23f, -0.04f));
            p.Add(new Vector2(0.23f, -0.02f));
            p.Add(new Vector2(0.26f, -0.02f));
            p.Add(new Vector2(0.26f, 0f));
            return p.ToArray();
        }

        /// <summary>
        /// From the flat ceiling to the glass, around an opening <paramref name="margin"/> from the walls: a stepped frame
        /// hanging under the ceiling, the light well going up, and a cove meeting the glass.
        /// </summary>
        static IEnumerable<Vector2> SkylightProfile(float margin, float depth)
        {
            float m = margin;
            yield return new Vector2(m - 0.2f, 0f);       // end of the flat ceiling
            yield return new Vector2(m - 0.2f, -0.025f);
            yield return new Vector2(m - 0.14f, -0.025f);
            yield return new Vector2(m - 0.14f, -0.05f);
            yield return new Vector2(m - 0.06f, -0.05f);
            yield return new Vector2(m - 0.06f, -0.08f);
            yield return new Vector2(m, -0.08f);           // inner edge of the frame
            float coveStart = Mathf.Max(0.02f, depth - GlassInset - 0.02f);
            yield return new Vector2(m, coveStart);        // well wall
            for (int i = 1; i <= 6; i++)
            {
                float t = Mathf.PI / 2f * i / 6f;
                yield return new Vector2(m + GlassInset * (1f - Mathf.Cos(t)), coveStart + (depth - coveStart) * Mathf.Sin(t));
            }
        }

        // ---------------------------------------------------------------- mesh

        /// <summary>
        /// Sweep <paramref name="profile"/> (x = inset from the walls' inner faces, y = height above the ceiling plane)
        /// around the room: mitred at the corners because each profile point follows the rectangle inset by its x.
        /// Faces point to the side the profile turns away from, i.e. into the room for a profile going from the wall
        /// inwards and up. Smooth across gentle bends (coves), hard across steps.
        /// </summary>
        static void AddRing(float a, float b, float ceilingY, List<Vector2> profile,
                            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            int n = profile.Count;
            // 2D normal of each segment in (inward, up): (dy, -dx)
            var segNormal = new Vector2[n - 1];
            var segLength = new float[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                var d = profile[i + 1] - profile[i];
                segLength[i] = d.magnitude;
                segNormal[i] = segLength[i] > 1e-6f ? new Vector2(d.y, -d.x) / segLength[i] : Vector2.zero;
            }
            Vector2 EndNormal(int seg, int point)
            {
                int other = point == seg ? seg - 1 : seg + 1;
                if (other < 0 || other >= n - 1) return segNormal[seg];
                return Vector2.Dot(segNormal[seg], segNormal[other]) > 0.85f
                    ? (segNormal[seg] + segNormal[other]).normalized : segNormal[seg];
            }

            // Sides: inward normal (n), direction along the wall (t, chosen so that t × n = up).
            var sides = new (Vector3 n, Vector3 t, bool alongX)[]
            {
                (Vector3.back, Vector3.right, true),    // north wall (z = +b)
                (Vector3.forward, Vector3.left, true),  // south wall (z = -b)
                (Vector3.left, Vector3.back, false),    // east wall (x = +a)
                (Vector3.right, Vector3.forward, false) // west wall (x = -a)
            };
            foreach (var (nrm, t, alongX) in sides)
            {
                float v = 0f;
                for (int i = 0; i < n - 1; i++)
                {
                    if (segLength[i] <= 1e-6f) continue;
                    var p0 = profile[i];
                    var p1 = profile[i + 1];
                    float ea0 = Mathf.Max(0f, (alongX ? a : b) - p0.x), en0 = (alongX ? b : a) - p0.x;
                    float ea1 = Mathf.Max(0f, (alongX ? a : b) - p1.x), en1 = (alongX ? b : a) - p1.x;
                    var up0 = Vector3.up * (ceilingY + p0.y);
                    var up1 = Vector3.up * (ceilingY + p1.y);
                    var A = -t * ea0 - nrm * en0 + up0;
                    var B = t * ea0 - nrm * en0 + up0;
                    var C = t * ea1 - nrm * en1 + up1;
                    var D = -t * ea1 - nrm * en1 + up1;
                    var n0 = EndNormal(i, i);
                    var n1 = EndNormal(i, i + 1);
                    var N0 = nrm * n0.x + Vector3.up * n0.y;
                    var N1 = nrm * n1.x + Vector3.up * n1.y;
                    int k = vertices.Count;
                    vertices.Add(A); vertices.Add(B); vertices.Add(C); vertices.Add(D);
                    normals.Add(N0); normals.Add(N0); normals.Add(N1); normals.Add(N1);
                    float v1 = v + segLength[i];
                    uvs.Add(new Vector2(-ea0, v)); uvs.Add(new Vector2(ea0, v));
                    uvs.Add(new Vector2(ea1, v1)); uvs.Add(new Vector2(-ea1, v1));
                    v = v1;
                    triangles.Add(k); triangles.Add(k + 3); triangles.Add(k + 1);
                    triangles.Add(k + 1); triangles.Add(k + 3); triangles.Add(k + 2);
                }
            }
        }
    }
}
