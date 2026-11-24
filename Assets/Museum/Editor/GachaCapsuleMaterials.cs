using UnityEditor;
using UnityEngine;
using VirtualMuseum.App;

namespace VirtualMuseum.AppEditor
{
    /// <summary>
    /// Plastic materials of the gacha capsule (URP Lit): glossy red for the bottom half, clear glossy plastic for the
    /// top half (transparent, specular highlights kept at full strength so it reads as plastic, not as a ghost).
    /// </summary>
    public static class GachaCapsuleMaterials
    {
        const string Folder = "Assets/Museum/Materials";
        // Kept under its old name: scenes and prefabs already reference this asset for the capsule.
        const string ColorPath = Folder + "/GachaCapsule.mat";
        const string ClearPath = Folder + "/GachaCapsuleClear.mat";

        static readonly Color Red = new Color(0.86f, 0.06f, 0.08f, 1f);
        static readonly Color Clear = new Color(0.92f, 0.96f, 1f, 0.22f);

        public static (Material color, Material clear) GetOrCreate()
        {
            var color = Load(ColorPath);
            ConfigurePlastic(color, Red, transparent: false);
            var clear = Load(ClearPath);
            ConfigurePlastic(clear, Clear, transparent: true);
            AssetDatabase.SaveAssets();
            return (color, clear);
        }

        /// <summary>(Re)make both materials and give them to every gacha dispenser in the open scenes and prefab.</summary>
        [MenuItem("Tools/Virtual Museum/Apply Gacha Capsule Materials")]
        public static void ApplyToDispensers()
        {
            var (color, clear) = GetOrCreate();
            foreach (var dispenser in Object.FindObjectsByType<GachaDispenser>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Assign(dispenser, color, clear);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(dispenser.gameObject.scene);
            }

            const string prefabPath = "Assets/Prefabs/Gacha Machine.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (var dispenser in prefab.GetComponentsInChildren<GachaDispenser>(true)) Assign(dispenser, color, clear);
                PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        public static void Assign(GachaDispenser dispenser, Material color, Material clear)
        {
            var serialized = new SerializedObject(dispenser);
            serialized.FindProperty("capsuleColorMaterial").objectReferenceValue = color;
            serialized.FindProperty("capsuleClearMaterial").objectReferenceValue = clear;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material Load(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            System.IO.Directory.CreateDirectory(Folder);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void ConfigurePlastic(Material material, Color color, bool transparent)
        {
            material.shader = Shader.Find("Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", transparent ? 0.95f : 0.82f);
            material.SetFloat("_SpecularHighlights", 1f);
            material.SetFloat("_EnvironmentReflections", 1f);
            material.SetFloat("_Surface", transparent ? 1f : 0f);
            material.SetFloat("_Blend", 0f); // alpha
            material.SetFloat("_BlendModePreserveSpecular", 1f);
            material.SetFloat("_Cull", 2f); // back: the shell's inner walls are modelled
            material.SetFloat("_ReceiveShadows", 1f);
            // Let the Lit shader GUI derive keywords, blend states and render queue from these properties.
            var editor = (MaterialEditor)UnityEditor.Editor.CreateEditor(material);
            try
            {
                editor.customShaderGUI?.ValidateMaterial(material);
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
            // Clear plastic casts no shadow: it would darken the artwork inside the capsule.
            material.SetShaderPassEnabled("ShadowCaster", !transparent);
            EditorUtility.SetDirty(material);
        }
    }
}
