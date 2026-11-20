using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualMuseum.App;

namespace VirtualMuseum.AppEditor
{
    /// <summary>
    /// Adds the visitor mode to a scene that already has the organizer setup (Create Maquette Sandbox):
    /// "Vue visiteur" teleport button on the workbench, wall plan for the rooms, mode manager + guide.
    /// Everything else is built at runtime.
    /// </summary>
    public static class VisitorModeSetup
    {
        const string MaterialsFolder = "Assets/Museum/Materials";

        [MenuItem("Tools/Virtual Museum/Add Visitor Mode")]
        public static void Create()
        {
            if (Object.FindAnyObjectByType<VisitorMode>() != null)
            {
                Debug.LogWarning("This scene already has a Visitor Mode. Nothing created.");
                return;
            }
            var backend = Object.FindAnyObjectByType<MuseumBackend>();
            var immersive = Object.FindObjectsByType<MuseumView>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(v => v.GetComponent<Maquette>() == null);
            var workbench = GameObject.Find("Workbench");
            if (backend == null || immersive == null || workbench == null)
            {
                Debug.LogError("Run Tools > Virtual Museum > Create Maquette Sandbox first (needs a backend, an immersive MuseumView and a Workbench).");
                return;
            }

            var ui = AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/Handle.mat");
            var overlay = GetOrCreateOverlayMaterial();

            // Provisional teleport button, right of the plan board under "Nouvelle salle" (hidden with the workbench).
            var button = new GameObject("Visitor Teleport Button").AddComponent<VisitorTeleportButton>();
            button.transform.SetParent(workbench.transform, false);
            button.transform.localPosition = new Vector3(0.85f, 0.85f, -0.05f);

            // Wall plan, moved at runtime into the visitor's current room.
            var plan = new GameObject("Visitor Room Plan").AddComponent<RoomPlan>();
            var miniGo = new GameObject("Plan Miniature");
            miniGo.transform.SetParent(plan.transform, false);
            var mini = miniGo.AddComponent<MuseumView>();
            var so = new SerializedObject(mini);
            so.FindProperty("backend").objectReferenceValue = backend;
            so.FindProperty("floorMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/Floor.mat");
            so.FindProperty("wallMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/Wall.mat");
            so.FindProperty("cableMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/Cable.mat");
            so.FindProperty("startExpanded").boolValue = false;
            so.FindProperty("wallColliders").boolValue = false;
            so.FindProperty("labelHeight").floatValue = 0.03f;
            so.FindProperty("showArtworkWarnings").boolValue = false;
            so.FindProperty("cableWidth").floatValue = 0.35f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var modeGo = new GameObject("Visitor Mode");
            var mode = modeGo.AddComponent<VisitorMode>();
            modeGo.AddComponent<VisitorGuide>();
            so = new SerializedObject(mode);
            so.FindProperty("backend").objectReferenceValue = backend;
            so.FindProperty("immersive").objectReferenceValue = immersive;
            SetArray(so.FindProperty("organizerObjects"), workbench, GameObject.Find("Workshop Floor"));
            SetArray(so.FindProperty("visitorObjects"), plan.gameObject);
            so.FindProperty("roomPlan").objectReferenceValue = plan;
            so.FindProperty("overlayMaterial").objectReferenceValue = overlay;
            so.FindProperty("worldTextMaterial").objectReferenceValue = GetOrCreateMaterial("WorldText", "Museum/WorldText");
            so.FindProperty("uiMaterial").objectReferenceValue = ui;
            so.ApplyModifiedPropertiesWithoutUndo();

            so = new SerializedObject(plan);
            so.FindProperty("mode").objectReferenceValue = mode;
            so.FindProperty("miniature").objectReferenceValue = mini;
            so.ApplyModifiedPropertiesWithoutUndo();
            so = new SerializedObject(button);
            so.FindProperty("mode").objectReferenceValue = mode;
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (var go in new[] { modeGo, button.gameObject, plan.gameObject })
                Undo.RegisterCreatedObjectUndo(go, "Add Visitor Mode");
            EditorSceneManager.MarkSceneDirty(modeGo.scene);
            Selection.activeGameObject = modeGo;
            Debug.Log("Visitor mode added: 'Vue visiteur' button on the workbench, wall plan, mode manager. Save the scene.");
        }

        static void SetArray(SerializedProperty array, params GameObject[] items)
        {
            items = items.Where(i => i != null).ToArray();
            array.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        static Material GetOrCreateOverlayMaterial() => GetOrCreateMaterial("Overlay", "Museum/Overlay");

        public static Material GetOrCreateMaterial(string name, string shader)
        {
            var path = MaterialsFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Directory.CreateDirectory(MaterialsFolder);
            mat = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
