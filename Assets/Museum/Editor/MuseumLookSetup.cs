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
    /// Also the doors at the connections: arch "Arch7" of MuseumAsset by Vira Voloshyn (viravoloshyn.itch.io).
    /// </summary>
    public static class MuseumLookSetup
    {
        const string Materials = "Assets/Museum/Materials";
        const string Wood = "Assets/Museum/Textures/WoodFloor";
        const string PanePath = "Assets/3D/GlassWindow.fbx";
        const string DoorPath = "Assets/3D/MuseumDoor.fbx";
        const float FloorTile = 1.6f; // metres per repeat of the parquet texture (two chevron columns)

        [MenuItem("Tools/Virtual Museum/Apply Museum Look (parquet, skylights, doors)")]
        public static void Apply()
        {
            var floor = FloorMaterial();
            // Cream walls, ceiling and doors (the doors are part of the wall), a lighter cream baseboard.
            var wall = Lit("MuseumWall", new Color(0.96f, 0.92f, 0.83f), smoothness: 0.1f);
            var trim = Lit("MuseumTrim", new Color(0.98f, 0.96f, 0.9f), smoothness: 0.3f);
            var plaster = Lit("CeilingPlaster", new Color(0.95f, 0.91f, 0.82f), smoothness: 0.12f);
            // The sun lights the ceiling from above only: a little emission stands for the daylight bounced off the
            // floor and walls, so the plaster reads white like in a real gallery.
            plaster.SetColor("_EmissionColor", new Color(0.43f, 0.4f, 0.34f));
            plaster.EnableKeyword("_EMISSION");
            plaster.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // None loses the keyword on reload
            var frame = Lit("SkylightFrame", new Color(0.8f, 0.8f, 0.78f), smoothness: 0.35f, metallic: 0.3f);
            var glass = GlassMaterial();
            var pane = Model(PanePath);
            var door = Model(DoorPath);
            var doorDark = DoorDepthMaterial();
            AssetDatabase.SaveAssets();

            var view = FindImmersive();
            if (view == null)
            {
                Debug.LogError("Museum look: no immersive MuseumView in the open scene (VisitorMode.immersive).");
                return;
            }
            var so = new SerializedObject(view);
            so.FindProperty("floorMaterial").objectReferenceValue = floor;
            so.FindProperty("wallMaterial").objectReferenceValue = wall;
            so.FindProperty("floorTextureTile").floatValue = FloorTile;
            so.FindProperty("buildCeilings").boolValue = true;
            var c = so.FindProperty("ceiling");
            c.FindPropertyRelative("plaster").objectReferenceValue = plaster;
            c.FindPropertyRelative("trim").objectReferenceValue = trim;
            c.FindPropertyRelative("skylightPane").objectReferenceValue = pane;
            c.FindPropertyRelative("glass").objectReferenceValue = glass;
            c.FindPropertyRelative("frame").objectReferenceValue = frame;
            so.FindProperty("buildDoors").boolValue = true;
            var d = so.FindProperty("doorStyle");
            d.FindPropertyRelative("model").objectReferenceValue = door;
            d.FindPropertyRelative("frame").objectReferenceValue = wall;
            d.FindPropertyRelative("dark").objectReferenceValue = doorDark;
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

        static GameObject Model(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) { Debug.LogError("Museum look: missing " + path); return null; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importAnimation)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None; // materials given by MuseumView
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // ---------------------------------------------------------------- doors

        /// <summary>Inside of the doors: a passage going on behind the wall into the dark (parquet floor, cream walls).</summary>
        static Material DoorDepthMaterial()
        {
            const string path = Materials + "/DoorDark.mat";
            var shader = Shader.Find("Museum/DoorDepth");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_FloorTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Wood + "/WoodFloor_Color.jpg"));
            m.SetFloat("_FloorTile", FloorTile);
            // Like the lit room at the threshold, measured on it (linear): its walls, and its floor as
            // albedo * diffuse + sheen + grazing sheen (sky reflection). Then a short passage, dark quickly.
            m.SetColor("_WallColor", new Color(0.62f, 0.53f, 0.39f));
            m.SetColor("_FloorDiffuse", new Color(1.08f, 1.075f, 1.006f));
            m.SetColor("_FloorSheen", new Color(0.059f, 0.048f, 0.047f));
            m.SetColor("_FloorFresnel", new Color(0.023f, 0.12f, 0.224f));
            m.SetFloat("_Depth", 2.5f);
            m.SetFloat("_Falloff", 2f);
            EditorUtility.SetDirty(m);
            return m;
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
