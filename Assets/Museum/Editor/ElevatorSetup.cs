using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualMuseum.App;

namespace VirtualMuseum.AppEditor
{
    /// <summary>
    /// Builds the elevator in the open sandbox scene: workshop shaft at Atelier/Spot_Ascenseur (under the ceiling
    /// opening made by Assets/3D/Source~/build.py), lever on the wall at Atelier/Spot_Levier (machine cabinet), both children of the Workbench (hidden
    /// in visitor mode), and the <see cref="ElevatorRide"/> on the Visitor Mode object (it builds the museum station
    /// at runtime). Re-running re-places everything.
    /// </summary>
    public static class ElevatorSetup
    {
        const string LeverModelPath = "Assets/3D/Levier.fbx";
        const string MaterialsFolder = "Assets/Museum/Materials";
        const float Radius = 0.55f;        // build.py ELEV_R
        const float CeilingHeight = 2.6f;  // build.py H
        const float LeverScale = 0.3f;

        [MenuItem("Tools/Virtual Museum/Add Elevator")]
        public static void AddElevator()
        {
            var bench = GameObject.Find("Workbench");
            var room = bench != null ? bench.transform.Find("Atelier") : null;
            var spotShaft = room != null ? room.Find("Spot_Ascenseur") : null;
            var spotLever = room != null ? room.Find("Spot_Levier") : null;
            var mode = Object.FindAnyObjectByType<VisitorMode>();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(LeverModelPath);
            if (spotShaft == null || spotLever == null) { Debug.LogWarning("No Workbench/Atelier/Spot_Ascenseur or Spot_Levier: run Add Workshop Room (re-export Atelier.fbx)."); return; }
            if (mode == null) { Debug.LogWarning("No VisitorMode in the scene: run Add Visitor Mode first."); return; }
            if (model == null) { Debug.LogWarning("Lever model not found at " + LeverModelPath + " (Assets/3D/Source~/build_lever.py)."); return; }

            var tube = GetOrCreateMaterial("ElevatorTube", new Color(0.22f, 0.24f, 0.27f), 0.55f, 0.35f);
            ConfigureTubeMaterial(tube);
            var collar = GetOrCreateMaterial("ElevatorCollar", new Color(0.95f, 0.72f, 0.1f), 0.2f, 0.4f);
            var platform = GetOrCreateMaterial("ElevatorPlatform", new Color(0.55f, 0.57f, 0.6f), 0.8f, 0.45f);

            // Workshop shaft (origin = platform centre on the floor)
            var shaft = GetOrCreateChild(bench.transform, "Elevator Shaft");
            Undo.RecordObject(shaft, "Add Elevator");
            shaft.SetPositionAndRotation(spotShaft.position, bench.transform.rotation);
            var es = shaft.GetComponent<ElevatorShaft>();
            if (es == null) es = Undo.AddComponent<ElevatorShaft>(shaft.gameObject);
            var so = new SerializedObject(es);
            so.FindProperty("direction").enumValueIndex = (int)ElevatorShaft.Direction.Up;
            so.FindProperty("radius").floatValue = Radius;
            so.FindProperty("openingY").floatValue = CeilingHeight;
            so.FindProperty("openingThickness").floatValue = 0.1f;
            so.FindProperty("length").floatValue = 3.6f;
            so.FindProperty("restHeight").floatValue = 0.04f;
            so.FindProperty("tubeMaterial").objectReferenceValue = tube;
            so.FindProperty("collarMaterial").objectReferenceValue = platform; // seen from below: metal ring in the ceiling
            so.FindProperty("platformMaterial").objectReferenceValue = platform;
            so.ApplyModifiedProperties();

            // Workshop lever (spot convention: the object's front, -Z, faces the spot's +Z)
            var lever = GetOrCreateChild(bench.transform, "Elevator Lever");
            Undo.RecordObject(lever, "Add Elevator");
            lever.SetPositionAndRotation(spotLever.position, spotLever.rotation * Quaternion.Euler(0f, 180f, 0f));
            var el = lever.GetComponent<ElevatorLever>();
            if (el == null) el = Undo.AddComponent<ElevatorLever>(lever.gameObject);
            so = new SerializedObject(el);
            so.FindProperty("model").objectReferenceValue = model;
            so.FindProperty("modelScale").floatValue = LeverScale;
            so.FindProperty("label").stringValue = "Monter au musée :\nabaisser le levier";
            so.ApplyModifiedProperties();

            var ride = mode.GetComponent<ElevatorRide>();
            if (ride == null) ride = Undo.AddComponent<ElevatorRide>(mode.gameObject);
            so = new SerializedObject(ride);
            so.FindProperty("workshopShaft").objectReferenceValue = es;
            so.FindProperty("workshopLever").objectReferenceValue = el;
            so.FindProperty("leverModel").objectReferenceValue = model;
            so.FindProperty("leverScale").floatValue = LeverScale;
            so.FindProperty("leverHeight").floatValue = 1.45f; // museum lever on the wall beside the plan, head height
            so.FindProperty("tubeMaterial").objectReferenceValue = tube;
            so.FindProperty("collarMaterial").objectReferenceValue = collar;   // museum: ring around the opening, seen from above
            so.FindProperty("platformMaterial").objectReferenceValue = platform;
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(bench.scene);
            Selection.activeGameObject = lever.gameObject;
            Debug.Log("Elevator added: shaft at Spot_Ascenseur, lever at Spot_Levier, ElevatorRide on " + mode.name + ". Save the scene.");
        }

        const string TubeTexturePath = "Assets/Museum/Textures/ElevatorTube.png";

        /// <summary>
        /// Unlit tube: dark walls and a light ring every metre (texture v = metres from the tube's closed end). Unlit,
        /// it does not depend on the scene's lights or sky, so the workshop and museum tubes look the same at the swap.
        /// </summary>
        static void ConfigureTubeMaterial(Material mat)
        {
            if (!System.IO.File.Exists(TubeTexturePath))
            {
                const int w = 8, h = 128;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                for (int y = 0; y < h; y++)
                {
                    float v = (y + 0.5f) / h;                                  // one metre
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(v - 0.5f) / 0.035f);
                    float groove = Mathf.Abs(v - 0.15f) < 0.01f || Mathf.Abs(v - 0.85f) < 0.01f ? 0.6f : 1f;
                    float g = Mathf.Lerp(0.2f * groove, 1f, ring);
                    for (int x = 0; x < w; x++) tex.SetPixel(x, y, new Color(g, g, g));
                }
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(TubeTexturePath));
                System.IO.File.WriteAllBytes(TubeTexturePath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(TubeTexturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(TubeTexturePath);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(TubeTexturePath);
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null && mat.shader != unlit) mat.shader = unlit;
            mat.SetTexture("_BaseMap", t);
            mat.SetColor("_BaseColor", new Color(0.55f, 0.54f, 0.5f));
            EditorUtility.SetDirty(mat);
        }

        static Transform GetOrCreateChild(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Add Elevator");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Material GetOrCreateMaterial(string name, Color color, float metallic, float smoothness)
        {
            var path = MaterialsFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
