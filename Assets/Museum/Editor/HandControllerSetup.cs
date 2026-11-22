using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualMuseum.App;

namespace VirtualMuseum.AppEditor
{
    /// <summary>
    /// Puts an XR Hands hand model (copied from the com.unity.xr.hands "Hand Visualizer" sample into Assets/3D/XRHands)
    /// on each XRI starter controller of the scene's rig, and keeps only the trigger and the thumbstick of the controller.
    /// The grip pose is fitted automatically:
    /// 1. the hand is closed into a fist (fingers curl about the hand's own lateral axis);
    /// 2. the fist's hole is aligned with the controller handle, turned about it so the thumb is on top and the forearm
    ///    goes down and back, and centred on the handle;
    /// 3. middle/ring/little fingers are opened or closed until they just touch the body (temporary convex colliders),
    ///    the index is aimed at the trigger and the thumb at the thumbstick cap.
    /// </summary>
    public static class HandControllerSetup
    {
        const string HandFolder = "Assets/3D/XRHands/";
        const string HandMaterialPath = "Assets/Museum/Materials/MainXR.mat";
        static readonly string[] HiddenParts = { "Button_A", "Button_B", "Button_Home", "TouchPad", "Bumper" };

        // Controller space (metres), right hand; mirrored in x for the left one.
        static readonly Vector3 HandleAxis = new Vector3(0f, -0.425f, -0.905f).normalized;  // principal axis of Controller_Base
        static readonly Vector3 HandleCentre = new(0f, -0.032f, -0.088f);
        static readonly Vector3 ForearmDir = new Vector3(0f, -0.35f, -0.94f).normalized;     // forearm comes back towards the user
        static readonly Vector3 PalmSide = new Vector3(0.6f, 0.72f, -0.34f).normalized;      // palm against the back/outer side
        static readonly Vector3 TriggerTip = new(0f, -0.027f, -0.017f);                       // index tip on the trigger blade
        static readonly (string finger, Vector3 angles)[] Fist =
        {
            ("Index", new Vector3(30f, 40f, 15f)), ("Middle", new Vector3(60f, 80f, 35f)),
            ("Ring", new Vector3(62f, 82f, 35f)), ("Little", new Vector3(65f, 80f, 35f)),
        };
        const float FingerRadius = 0.0085f;
        // The starter controller's handle is only 25 degrees below the aim axis, almost along the forearm: no hand can hold
        // it naturally. The visual is pitched about the thumbstick so the handle drops like a real Quest controller's
        // (the ray, i.e. the controller pose, is unchanged). Constants above are for the untilted model.
        const float ControllerPitch = -35f;
        static readonly Vector3 PitchPivot = new(0.009f, 0.002f, -0.043f);
        static readonly Vector3 VisualRestPos = new(0f, 0f, -0.05f);
        static readonly Quaternion VisualRestRot = Quaternion.Euler(0f, 180f, 0f);
        static Quaternion Pitch => Quaternion.AngleAxis(ControllerPitch, Vector3.right);
        static Vector3 Tilted(Vector3 p, float s)
        {
            var pv = new Vector3(PitchPivot.x * s, PitchPivot.y, PitchPivot.z);
            return pv + Pitch * (new Vector3(p.x * s, p.y, p.z) - pv);
        }

        [MenuItem("Tools/Virtual Museum/Install Hand Controllers")]
        public static void Install()
        {
            var rig = GameObject.Find("XR Origin (XR Rig)");
            if (rig == null) { Debug.LogWarning("No 'XR Origin (XR Rig)' in the open scene."); return; }
            foreach (var (side, hand, s) in new[] { ("Left", "LeftHand", -1f), ("Right", "RightHand", 1f) })
            {
                var ctrl = rig.transform.Find($"Camera Offset/{side} Controller");
                var visual = ctrl != null ? ctrl.Find($"{side} Controller Visual") : null;
                if (visual == null) { Debug.LogWarning($"No '{side} Controller Visual' under the rig."); continue; }
                Install(ctrl, visual, hand, s);
            }
            EditorSceneManager.MarkSceneDirty(rig.scene);
            Debug.Log("XR Hands hands installed on the XRI controllers. Save the scene.");
        }

        static Vector3 C(Transform ctrl, Vector3 v, float s) => ctrl.TransformPoint(new Vector3(v.x * s, v.y, v.z));
        static Vector3 D(Transform ctrl, Vector3 v, float s) => ctrl.TransformDirection(new Vector3(v.x * s, v.y, v.z));
        // points / directions of the (tilted) controller model
        static Vector3 CT(Transform ctrl, Vector3 v, float s) => ctrl.TransformPoint(Tilted(v, s));
        static Vector3 DT(Transform ctrl, Vector3 v, float s) => ctrl.TransformDirection(Pitch * new Vector3(v.x * s, v.y, v.z));

        static void Install(Transform ctrl, Transform visual, string handFile, float s)
        {
            Undo.RecordObject(visual.gameObject, "Install Hand Controllers");
            visual.gameObject.SetActive(true);
            Undo.RecordObject(visual, "Install Hand Controllers");
            var pivot = new Vector3(PitchPivot.x * s, PitchPivot.y, PitchPivot.z);
            visual.localRotation = Pitch * VisualRestRot;
            visual.localPosition = pivot + Pitch * (VisualRestPos - pivot);
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                if (System.Array.IndexOf(HiddenParts, t.name) >= 0) { Undo.RecordObject(t.gameObject, "Install Hand Controllers"); t.gameObject.SetActive(false); }
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                if (t.name == "Cible_Pouce") Undo.DestroyObjectImmediate(t.gameObject);
            foreach (var old in new[] { "Main Manette", "Main XR" })
            {
                var o = ctrl.Find(old);
                if (o != null) Undo.DestroyObjectImmediate(o.gameObject);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandFolder + handFile + ".fbx");
            if (prefab == null) { Debug.LogWarning("Hand model not found: " + HandFolder + handFile + ".fbx"); return; }
            var hand = (GameObject)PrefabUtility.InstantiatePrefab(prefab, ctrl);
            hand.name = "Main XR";
            Undo.RegisterCreatedObjectUndo(hand, "Install Hand Controllers");
            foreach (var r in hand.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.sharedMaterial = GetOrCreateHandMaterial();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.updateWhenOffscreen = true;
            }
            var bones = new Dictionary<string, Transform>();
            foreach (var t in hand.GetComponentsInChildren<Transform>()) bones[t.name.Substring(t.name.IndexOf('_') + 1)] = t;
            Transform B(string n) => bones.TryGetValue(n, out var t) ? t : null;

            // 1. fist: all fingers curl about the hand's lateral axis, towards the palm side
            var fwd = (B("MiddleProximal").position - B("Wrist").position).normalized;
            var palmSide = Vector3.ProjectOnPlane(B("MiddleTip").position - B("MiddleProximal").position, fwd).normalized;
            var curlAxis = Vector3.Cross(fwd, palmSide).normalized;   // positive rotation turns fwd towards palmSide
            foreach (var (f, a) in Fist)
            {
                var chain = new[] { B(f + "Proximal"), B(f + "Intermediate"), B(f + "Distal") };
                for (int i = 0; i < 3; i++) chain[i].Rotate(curlAxis, a[i], Space.World);
            }

            // 2. align the fist's hole with the handle, choose the turn about it, centre it
            Vector3 Hole() => Vector3.Lerp(B("Palm").position, B("MiddleIntermediate").position, 0.5f);
            var lat = (B("LittleProximal").position - B("IndexProximal").position).normalized;
            var axisW = DT(ctrl, HandleAxis, s).normalized;
            hand.transform.rotation = Quaternion.FromToRotation(lat, axisW) * hand.transform.rotation;
            var target = CT(ctrl, HandleCentre, s);
            hand.transform.position += target - Hole();
            var baseRot = hand.transform.rotation; var basePos = hand.transform.position;
            // search the turn about the handle (phi) and a diagonal tilt of the grip (tau, about the palm normal):
            // palm against the back/outer side, forearm coming back towards the user, thumb on top
            void Pose(float phi, float tau)
            {
                hand.transform.SetPositionAndRotation(basePos, baseRot);
                hand.transform.RotateAround(Hole(), axisW, phi);
                var palmN = (B("Palm").position - Hole()).normalized;
                hand.transform.RotateAround(Hole(), palmN, tau);
                hand.transform.position += target - Hole();
            }
            float bestScore = float.MinValue, bestPhi = 0f, bestTau = 0f;
            for (float tau = -45f; tau <= 45f; tau += 5f)
                for (float phi = 0f; phi < 360f; phi += 10f)
                {
                    Pose(phi, tau);
                    var palmBack = Vector3.Dot((B("Palm").position - Hole()).normalized, DT(ctrl, PalmSide, s));
                    var forearm = Vector3.Dot((B("Wrist").position - B("MiddleProximal").position).normalized, D(ctrl, ForearmDir, s));
                    var thumbUp = Vector3.Dot(B("ThumbTip").position - target, ctrl.up);
                    var score = 2f * palmBack + 2f * forearm + 20f * thumbUp;
                    if (score > bestScore) { bestScore = score; bestPhi = phi; bestTau = tau; }
                }
            Pose(bestPhi, bestTau);
            Debug.Log($"{handFile}: grip turn {bestPhi} deg, tilt {bestTau} deg (score {bestScore:F2})");

            // 3. contact / aim refinement
            var body = visual.GetComponentsInChildren<Transform>(true);
            Transform Part(string n) { foreach (var t in body) if (t.name == n) return t; return null; }
            var temp = new List<Collider>();
            Collider AddCollider(Transform t)
            {
                if (t == null) return null;
                var mc = t.gameObject.AddComponent<MeshCollider>(); mc.convex = true; temp.Add(mc); return mc;
            }
            var baseCol = AddCollider(Part("Controller_Base"));
            var stickCol = AddCollider(Part("ThumbStick"));
            Physics.SyncTransforms();
            try
            {
                foreach (var f in new[] { "Middle", "Ring", "Little" })
                    Fit(new[] { B(f + "Proximal"), B(f + "Intermediate"), B(f + "Distal") }, B(f + "Tip"), B("Palm"), baseCol, f == "Little" ? 0.007f : FingerRadius);
                Reach(new[] { B("IndexIntermediate"), B("IndexDistal") }, B("IndexTip"), CT(ctrl, TriggerTip, s));

                var stick = Part("ThumbStick");
                if (stick != null && stickCol != null)
                {
                    var cap = stickCol.bounds.center + ctrl.up * (stickCol.bounds.extents.y + 0.0075f);
                    var tip = B("ThumbTip");
                    Reach(new[] { B("ThumbMetacarpal"), B("ThumbProximal"), B("ThumbDistal") }, tip, cap);
                    var thumbTarget = new GameObject("Cible_Pouce").transform;
                    thumbTarget.SetParent(stick, false);
                    thumbTarget.position = tip.position;
                    Undo.RegisterCreatedObjectUndo(thumbTarget.gameObject, "Install Hand Controllers");

                    var anim = hand.AddComponent<HandControllerAnimator>();
                    anim.ThumbTarget = thumbTarget;
                    var starter = visual.GetComponent("ControllerAnimator");
                    if (starter != null)
                    {
                        var src = new SerializedObject(starter); var dst = new SerializedObject(anim);
                        foreach (var p in new[] { "m_TriggerInput", "m_GripInput" }) dst.CopyFromSerializedProperty(src.FindProperty(p));
                        dst.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }
            finally
            {
                foreach (var c in temp) Object.DestroyImmediate(c);
            }
        }

        /// <summary>Opens (if it goes through the body) then closes each joint in 2 degree steps until the finger touches.</summary>
        static void Fit(Transform[] chain, Transform tip, Transform palm, Collider col, float radius)
        {
            if (chain[0] == null || tip == null || col == null) return;
            var axis = Vector3.Cross(chain[1].position - chain[0].position, tip.position - chain[1].position).normalized;
            var moved = chain[2].position + Quaternion.AngleAxis(5f, axis) * (tip.position - chain[2].position);
            if ((moved - palm.position).sqrMagnitude > (tip.position - palm.position).sqrMagnitude) axis = -axis;
            bool Touches(int from)
            {
                var pts = new List<Vector3>();
                var prev = chain[from].position;
                for (int k = from + 1; k <= chain.Length; k++)
                {
                    var p = k < chain.Length ? chain[k].position : tip.position;
                    pts.Add(p); pts.Add((prev + p) / 2f); prev = p;
                }
                foreach (var p in pts) if ((col.ClosestPoint(p) - p).sqrMagnitude < radius * radius) return true;
                return false;
            }
            for (int i = 0; i < chain.Length; i++)
            {
                for (int step = 0; step < 30 && Touches(i); step++) { chain[i].Rotate(axis, -2f, Space.World); Physics.SyncTransforms(); }
                for (int step = 0; step < 30; step++)
                {
                    chain[i].Rotate(axis, 2f, Space.World); Physics.SyncTransforms();
                    if (Touches(i)) { chain[i].Rotate(axis, -2f, Space.World); break; }
                }
            }
        }

        /// <summary>CCD: turns the chain's joints (tip side first) so the tip reaches the target.</summary>
        static void Reach(Transform[] chain, Transform tip, Vector3 target)
        {
            if (tip == null) return;
            for (int pass = 0; pass < 8; pass++)
                for (int i = chain.Length - 1; i >= 0; i--)
                    if (chain[i] != null)
                        chain[i].rotation = Quaternion.FromToRotation(tip.position - chain[i].position, target - chain[i].position) * chain[i].rotation;
        }

        static Material GetOrCreateHandMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(HandMaterialPath);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "MainXR" };
            mat.SetColor("_BaseColor", new Color(0.86f, 0.80f, 0.74f));
            mat.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(mat, HandMaterialPath);
            return mat;
        }
    }
}
