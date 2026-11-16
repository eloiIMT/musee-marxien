using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VirtualMuseum.Editor
{
    public static class MuseumSetup
    {
        public const string Folder = "Assets/VirtualMuseum/Sample";
        public const string CatalogPath = Folder + "/ArtworkCatalog.asset";
        public const string BackendPath = Folder + "/MuseumBackend.prefab";

        [MenuItem("Tools/Virtual Museum/Create Missing Example Assets")]
        public static void CreateExampleAssets()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<ArtworkCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ArtworkCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                var serialized = new SerializedObject(catalog);
                var definitions = serialized.FindProperty("definitions");
                var types = new[] { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Capsule, PrimitiveType.Cylinder, PrimitiveType.Cube, PrimitiveType.Sphere };
                var names = new[] { "The Block", "Orb", "Standing Form", "Column", "Study in Planes", "Small World" };
                definitions.arraySize = types.Length;
                for (int i = 0; i < types.Length; i++)
                {
                    var path = Folder + "/Artwork" + (i + 1) + ".prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        var go = GameObject.CreatePrimitive(types[i]);
                        try
                        {
                            go.name = names[i];
                            // Mesh proportions belong to a child; instance TRS always applies to an identity root.
                            var root = new GameObject(names[i]);
                            try
                            {
                                go.transform.SetParent(root.transform, false);
                                if (i == 4) go.transform.localScale = new Vector3(1.4f, 1, 0.12f);
                                if (i == 5) go.transform.localScale = Vector3.one * 0.5f;
                                UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                            }
                            finally { UnityEngine.Object.DestroyImmediate(root); }
                        }
                        finally { if (go != null) UnityEngine.Object.DestroyImmediate(go); }
                    }
                    var entry = definitions.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("id").stringValue = "sample-artwork-" + (i + 1);
                    entry.FindPropertyRelative("title").stringValue = names[i];
                    entry.FindPropertyRelative("description").stringValue = "A primitive study for the Virtual Museum student project.";
                    entry.FindPropertyRelative("artist").stringValue = "Student Collection";
                    entry.FindPropertyRelative("year").stringValue = "";
                    entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(catalog);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BackendPath) == null)
            {
                var root = new GameObject("Museum Backend");
                try
                {
                    var component = root.AddComponent<MuseumBackend>();
                    var serialized = new SerializedObject(component);
                    serialized.FindProperty("catalog").objectReferenceValue = catalog;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, BackendPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Virtual Museum example assets ready. No scenes were modified.");
        }

        [MenuItem("Tools/Virtual Museum/Add Backend to Current Scene")]
        public static void AddBackendToScene()
        {
            if (UnityEngine.Object.FindAnyObjectByType<MuseumBackend>() != null)
            { Debug.LogWarning("This scene already contains a MuseumBackend."); return; }
            CreateExampleAssets();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BackendPath));
            Undo.RegisterCreatedObjectUndo(go, "Add Museum Backend");
            Selection.activeGameObject = go;
        }

        // Invoked by Unity batch mode as an independent smoke check after asset creation.
        public static void VerifyExampleAssets()
        {
            CreateExampleAssets();
            var catalog = AssetDatabase.LoadAssetAtPath<ArtworkCatalog>(CatalogPath);
            var api = MuseumExample.Create(catalog);
            var errors = api.Validate();
            if (errors.Count != 0) throw new Exception(string.Join("\n", errors));
            var json = MuseumPersistence.ToJson(api);
            if (!MuseumPersistence.TryLoadJson(api, json, out var error)) throw new Exception(error);
            Debug.Log("Virtual Museum asset and JSON smoke check passed.");
        }
    }
}
