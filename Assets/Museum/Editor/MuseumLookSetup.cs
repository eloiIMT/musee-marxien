using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualMuseum.App;

namespace VirtualMuseum.AppEditor
{
    /// <summary>
    /// Look of the life-size museum: chevron parquet floor (Wood 0058, texturecan.com) and moulded ceilings with a
    /// frosted skylight (pane model "glass window" by Noboa, CC-BY-4.0, see Assets/3D/Source~). Makes the materials and
    /// gives them to the immersive <see cref="MuseumView"/> of the open scene. The miniatures keep their plain floor.
    /// </summary>
    public static class MuseumLookSetup
    {
        const string Materials = "Assets/Museum/Materials";
        const string Wood = "Assets/Museum/Textures/WoodFloor";
        const string PanePath = "Assets/3D/GlassWindow.fbx";
        const float FloorTile = 1.6f; // metres per repeat of the parquet texture (two chevron columns)

        [MenuItem("Tools/Virtual Museum/Apply Museum Look (parquet + skylights)")]
        public static void Apply()
        {
            var floor = FloorMaterial();
            var plaster = Lit("CeilingPlaster", new Color(0.94f, 0.93f, 0.9f), smoothness: 0.12f);
            // The sun lights the ceiling from above only: a little emission stands for the daylight bounced off the
            // floor and walls, so the plaster reads white like in a real gallery.
            plaster.SetColor("_EmissionColor", new Color(0.42f, 0.41f, 0.39f));
            plaster.EnableKeyword("_EMISSION");
            plaster.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // None loses the keyword on reload
            var frame = Lit("SkylightFrame", new Color(0.8f, 0.8f, 0.78f), smoothness: 0.35f, metallic: 0.3f);
            var glass = GlassMaterial();
            var pane = PaneModel();
            AssetDatabase.SaveAssets();

            var view = FindImmersive();
            if (view == null)
            {
                Debug.LogError("Museum look: no immersive MuseumView in the open scene (VisitorMode.immersive).");
                return;
            }
            var so = new SerializedObject(view);
            so.FindProperty("floorMaterial").objectReferenceValue = floor;
            so.FindProperty("floorTextureTile").floatValue = FloorTile;
            so.FindProperty("buildCeilings").boolValue = true;
            var c = so.FindProperty("ceiling");
            c.FindPropertyRelative("plaster").objectReferenceValue = plaster;
            c.FindPropertyRelative("skylightPane").objectReferenceValue = pane;
            c.FindPropertyRelative("glass").objectReferenceValue = glass;
            c.FindPropertyRelative("frame").objectReferenceValue = frame;
            so.ApplyModifiedProperties();
            // Skylight lighting while visiting (sun from above, even ambient), workshop lighting restored on leaving.
            if (view.GetComponent<SkylightLighting>() == null) Undo.AddComponent<SkylightLighting>(view.gameObject);
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            Debug.Log($"Museum look applied to '{view.name}'. Save the scene to keep it.", view);
        }

        static MuseumView FindImmersive()
        {
            var mode = Object.FindAnyObjectByType<VisitorMode>(FindObjectsInactive.Include);
            if (mode != null && mode.Immersive != null) return mode.Immersive;
            foreach (var v in Object.FindObjectsByType<MuseumView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (new SerializedObject(v).FindProperty("startExpanded").boolValue) return v;
            return null;
        }

        // ---------------------------------------------------------------- floor

        static Material FloorMaterial()
        {
            SetTexture(Wood + "/WoodFloor_Color.jpg", srgb: true);
            SetTexture(Wood + "/WoodFloor_AO.jpg", srgb: false);
            SetTexture(Wood + "/WoodFloor_Normal.png", srgb: false, normal: true);
            var smoothness = SmoothnessMap(Wood + "/WoodFloor_Roughness.jpg", Wood + "/WoodFloor_MetallicSmoothness.png");

            var m = Lit("MuseumFloorWood", Color.white, smoothness: 0.9f);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Wood + "/WoodFloor_Color.jpg"));
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Wood + "/WoodFloor_Normal.png"));
            m.SetFloat("_BumpScale", 1f);
            m.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Wood + "/WoodFloor_AO.jpg"));
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetTexture("_MetallicGlossMap", smoothness);
            m.SetFloat("_SmoothnessTextureChannel", 0f); // metallic map's alpha
            Validate(m);
            return m;
        }

        /// <summary>URP reads smoothness from the metallic map's alpha: write (0, 0, 0, 1 - roughness).</summary>
        static Texture2D SmoothnessMap(string roughnessPath, string outPath)
        {
            if (!File.Exists(outPath))
            {
                var rough = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                rough.LoadImage(File.ReadAllBytes(roughnessPath));
                var px = rough.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, (byte)(255 - px[i].r));
                var output = new Texture2D(rough.width, rough.height, TextureFormat.RGBA32, false, true);
                output.SetPixels32(px);
                File.WriteAllBytes(outPath, output.EncodeToPNG());
                Object.DestroyImmediate(rough);
                Object.DestroyImmediate(output);
                AssetDatabase.ImportAsset(outPath);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(outPath);
            if (importer.sRGBTexture || importer.alphaSource != TextureImporterAlphaSource.FromInput)
            {
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        static void SetTexture(string path, bool srgb, bool normal = false)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType == type && importer.sRGBTexture == srgb && importer.anisoLevel == 8) return;
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.anisoLevel = 8; // the floor is seen at grazing angles
            importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------- skylight

        /// <summary>Frosted glass: matte, opaque (no overdraw on Quest), glowing with the daylight behind it.</summary>
        static Material GlassMaterial()
        {
            var m = Lit("SkylightGlass", new Color(0.95f, 0.97f, 1f, 1f), smoothness: 0.2f);
            m.SetFloat("_Surface", 0f);   // opaque: frosted glass shows nothing of the sky anyway
            m.SetFloat("_Cull", 0f);      // both faces
            m.SetFloat("_ReceiveShadows", 0f);
            m.SetColor("_EmissionColor", new Color(0.82f, 0.86f, 0.9f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            Validate(m);
            m.SetShaderPassEnabled("ShadowCaster", false);
            return m;
        }

        static GameObject PaneModel()
        {
            var importer = AssetImporter.GetAtPath(PanePath) as ModelImporter;
            if (importer == null) { Debug.LogError("Museum look: missing " + PanePath); return null; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importAnimation)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None; // materials given by MuseumView
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(PanePath);
        }

        // ---------------------------------------------------------------- materials

        static Material Lit(string name, Color color, float smoothness, float metallic = 0f)
        {
            var path = Materials + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            Validate(m);
            return m;
        }

        /// <summary>Let the Lit shader GUI derive keywords, blend states and render queue from the properties.</summary>
        static void Validate(Material m)
        {
            var editor = (MaterialEditor)UnityEditor.Editor.CreateEditor(m);
            try { editor.customShaderGUI?.ValidateMaterial(m); }
            finally { Object.DestroyImmediate(editor); }
            EditorUtility.SetDirty(m);
        }
    }
}
