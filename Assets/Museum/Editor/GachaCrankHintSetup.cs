using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualMuseum.App;

namespace VirtualMuseum.AppEditor
{
    /// <summary>Neon arrow materials and the <see cref="GachaCrankHint"/> under every gacha crank.</summary>
    public static class GachaCrankHintSetup
    {
        const string Folder = "Assets/Museum/Materials";
        const string HintName = "Crank Hint Arrows";
        // Red neon: yellow-orange hot core, orange-red tube edges, red halo.
        static readonly Color TubeEdge = new Color(1f, 0.28f, 0.04f, 1f);
        static readonly Color TubeCore = new Color(1f, 0.8f, 0.38f, 1f);
        static readonly Color Halo = new Color(1f, 0.05f, 0.02f, 1f);

        public static (Material tube, Material glow) GetOrCreateMaterials()
        {
            var tube = GetOrCreate(Folder + "/GachaNeonTube.mat", "Museum/NeonTube");
            tube.SetColor("_Color", TubeEdge);
            tube.SetColor("_CoreColor", TubeCore);
            tube.SetFloat("_CorePower", 1.4f);
            var glow = GetOrCreate(Folder + "/GachaNeonGlow.mat", "Museum/NeonGlow");
            glow.SetColor("_Color", Halo);
            glow.SetFloat("_Falloff", 1.6f);
            EditorUtility.SetDirty(tube);
            EditorUtility.SetDirty(glow);
            AssetDatabase.SaveAssets();
            return (tube, glow);
        }

        /// <summary>Add (or refresh) the arrows under <paramref name="crank"/>.</summary>
        public static void AddTo(GachaCrank crank, GachaDispenser dispenser, Material tube, Material glow)
        {
            var existing = crank.transform.Find(HintName);
            var go = existing != null ? existing.gameObject : new GameObject(HintName);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(go, "Gacha crank hint");
                go.transform.SetParent(crank.transform, false);
            }
            // Recreated so the layout goes back to the defaults in code.
            var old = go.GetComponent<GachaCrankHint>();
            if (old != null) Undo.DestroyObjectImmediate(old);
            var hint = Undo.AddComponent<GachaCrankHint>(go);
            var serialized = new SerializedObject(hint);
            serialized.FindProperty("crank").objectReferenceValue = crank;
            serialized.FindProperty("dispenser").objectReferenceValue = dispenser;
            serialized.FindProperty("tubeMaterial").objectReferenceValue = tube;
            serialized.FindProperty("glowMaterial").objectReferenceValue = glow;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Tools/Virtual Museum/Add Gacha Crank Neon Arrows")]
        public static void AddToAll()
        {
            var (tube, glow) = GetOrCreateMaterials();
            foreach (var crank in Object.FindObjectsByType<GachaCrank>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(crank)) continue; // gets them from the prefab below
                AddTo(crank, crank.GetComponentInParent<GachaDispenser>(), tube, glow);
                EditorSceneManager.MarkSceneDirty(crank.gameObject.scene);
            }

            const string prefabPath = "Assets/Prefabs/Gacha Machine.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (var crank in prefab.GetComponentsInChildren<GachaCrank>(true))
                    AddTo(crank, crank.GetComponentInParent<GachaDispenser>(), tube, glow);
                PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        static Material GetOrCreate(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find(shaderName) ??
                         throw new System.InvalidOperationException($"Shader '{shaderName}' not found.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
