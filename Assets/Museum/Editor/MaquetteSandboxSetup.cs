using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualMuseum.App;

namespace VirtualMuseum.AppEditor
{
    /// <summary>
    /// Builds the organizer test setup in the open scene: backend, immersive museum, vertical plan board with the
    /// interactive miniature (Maquette + tour path + "new room" button), tutorial board below it, XR rig facing it.
    /// </summary>
    public static class MaquetteSandboxSetup
    {
        const string MaterialsFolder = "Assets/Museum/Materials";
        const string RigPath = "Assets/XR/Essentials/Prefabs/XR Origin (XR Rig).prefab";
        const float BoardCenterY = 1.35f;                              // centre at chest / eye height
        static readonly Vector3 BoardSize = new Vector3(1.4f, 1.0f, 0.04f); // width, height, thickness
        static readonly Vector3 BenchPosition = new Vector3(5f, 0f, -10f);

        [MenuItem("Tools/Virtual Museum/Create Maquette Sandbox")]
        public static void Create()
        {
            if (Object.FindAnyObjectByType<MuseumView>() != null)
            {
                Debug.LogWarning("This scene already contains a MuseumView. Nothing created.");
                return;
            }
            if (Object.FindAnyObjectByType<MuseumBackend>() == null)
                VirtualMuseum.Editor.MuseumSetup.AddBackendToScene();
            var backend = Object.FindAnyObjectByType<MuseumBackend>();

            var floor = GetOrCreateMaterial("Floor", new Color(0.75f, 0.75f, 0.72f), false);
            var wall = GetOrCreateMaterial("Wall", new Color(0.95f, 0.95f, 0.93f), false);
            var cable = GetOrCreateMaterial("Cable", new Color(1f, 0.55f, 0.1f), true);
            var boardMat = GetOrCreateMaterial("PlanBoard", new Color(0.22f, 0.24f, 0.27f), false);
            var handle = GetOrCreateMaterial("Handle", Color.white, true);

            // Immersive museum, scale 1, at the origin (museum coordinates = world coordinates).
            var immersive = new GameObject("Museum Immersive").AddComponent<MuseumView>();
            Configure(immersive, backend, floor, wall, cable, expanded: true, wallColliders: true, labelHeight: 0f, warnings: false);

            // Workshop "plan": a vertical board, like a wall map, facing the user (front face towards -Z).
            var bench = new GameObject("Workbench").transform;
            bench.position = BenchPosition;
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Board Stand";
            post.transform.SetParent(bench, false);
            post.transform.localScale = new Vector3(0.12f, BoardCenterY, 0.06f);
            post.transform.localPosition = new Vector3(0f, BoardCenterY / 2f, BoardSize.z / 2f + 0.03f);
            post.GetComponent<Renderer>().sharedMaterial = boardMat;
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Plan Board";
            board.transform.SetParent(bench, false);
            board.transform.localScale = BoardSize;
            board.transform.localPosition = new Vector3(0f, BoardCenterY, 0f);
            board.GetComponent<Renderer>().sharedMaterial = boardMat;

            // Miniature: floor in the board plane, rooms flat, the selected one comes out in 3D towards the user.
            // No wall colliders (the ray reaches floors and handles), labels, artwork warnings.
            var miniatureGo = new GameObject("Museum Miniature");
            miniatureGo.transform.SetParent(bench, false);
            miniatureGo.transform.localPosition = new Vector3(0f, BoardCenterY, -BoardSize.z / 2f);
            miniatureGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            miniatureGo.transform.localScale = Vector3.one * 0.04f; // overwritten by Maquette.Fit at runtime
            var miniature = miniatureGo.AddComponent<MuseumView>();
            Configure(miniature, backend, floor, wall, cable, expanded: false, wallColliders: false, labelHeight: 0.022f, warnings: true);

            var maquette = miniatureGo.AddComponent<Maquette>();
            var so = new SerializedObject(maquette);
            so.FindProperty("board").objectReferenceValue = board.GetComponent<Collider>();
            so.FindProperty("handleMaterial").objectReferenceValue = handle;
            so.ApplyModifiedPropertiesWithoutUndo();

            // "New room" button to the right of the board, at hand height.
            var buttonPos = new Vector3(BoardSize.x / 2f + 0.15f, BoardCenterY - 0.25f, -0.05f);
            var button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = "New Room Button";
            button.transform.SetParent(bench, false);
            button.transform.localScale = new Vector3(0.08f, 0.08f, 0.04f);
            button.transform.localPosition = buttonPos;
            button.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Button", new Color(0.2f, 0.75f, 0.35f), false);
            button.AddComponent<Clickable>();
            var creator = button.AddComponent<RoomCreator>();
            so = new SerializedObject(creator);
            so.FindProperty("maquette").objectReferenceValue = maquette;
            so.ApplyModifiedPropertiesWithoutUndo();
            var caption = new GameObject("Caption").AddComponent<TextMesh>();
            caption.transform.SetParent(bench, false);
            caption.transform.localPosition = buttonPos + new Vector3(0f, 0.08f, -0.02f);
            caption.text = "Nouvelle salle";
            caption.anchor = TextAnchor.MiddleCenter;
            caption.fontSize = 64;
            caption.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            caption.GetComponent<MeshRenderer>().sharedMaterial = caption.font.material;
            caption.characterSize = 0.003f;

            Undo.RegisterCreatedObjectUndo(immersive.gameObject, "Create Maquette Sandbox");
            Undo.RegisterCreatedObjectUndo(bench.gameObject, "Create Maquette Sandbox");
            AddWorkshopFloor();
            AddTutorialBoard();
            AddRig();

            EditorSceneManager.MarkSceneDirty(immersive.gameObject.scene);
            Selection.activeGameObject = bench.gameObject;
            Debug.Log("Maquette sandbox created. Play (Quest Link or XR Device Simulator): the museum appears at scale 1 and on the plan board.");
        }

        static void Configure(MuseumView view, MuseumBackend backend, Material floor, Material wall, Material cable,
                              bool expanded, bool wallColliders, float labelHeight, bool warnings)
        {
            var so = new SerializedObject(view);
            so.FindProperty("backend").objectReferenceValue = backend;
            so.FindProperty("floorMaterial").objectReferenceValue = floor;
            so.FindProperty("wallMaterial").objectReferenceValue = wall;
            so.FindProperty("cableMaterial").objectReferenceValue = cable;
            so.FindProperty("startExpanded").boolValue = expanded;
            so.FindProperty("wallColliders").boolValue = wallColliders;
            so.FindProperty("labelHeight").floatValue = labelHeight;
            so.FindProperty("showArtworkWarnings").boolValue = warnings;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Ground of the workshop: the XR rig has gravity and would fall without it.</summary>
        [MenuItem("Tools/Virtual Museum/Add Workshop Floor")]
        public static void AddWorkshopFloor()
        {
            if (GameObject.Find("Workshop Floor") != null) return;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Workshop Floor";
            // 20 × 12 m around the workbench, top at y = 0, stops before the museum (z > -4.5).
            ground.transform.position = BenchPosition + new Vector3(0f, -0.05f, -0.5f);
            ground.transform.localScale = new Vector3(20f, 0.1f, 12f);
            ground.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("WorkshopFloor", new Color(0.3f, 0.3f, 0.32f), false);
            Undo.RegisterCreatedObjectUndo(ground, "Add Workshop Floor");
            EditorSceneManager.MarkSceneDirty(ground.scene);
        }

        const string TutorialText =
            "MAQUETTE — COMMANDES\n" +
            "\n" +
            "Rayon : pointer · Gâchette : cliquer · Grip : saisir\n" +
            "Salle sélectionnée : joystick droit ← → = mode Déplacer / Hauteur / Liaison\n" +
            "\n" +
            "Salles\n" +
            "  Gâchette sur une salle : sélectionner (passe en 3D)\n" +
            "  Gâchette sur le plan vide : désélectionner\n" +
            "  Bouton vert « Nouvelle salle » puis gâchette sur le plan : créer\n" +
            "  Déplacer · grip sur le sol (ou sphère bleue) : bouger · poignet : 15°\n" +
            "  Déplacer · grip cubes jaunes (coins) : largeur / longueur\n" +
            "  Hauteur · slider sur le mur du bas : grip et glisser, ou gâchette\n" +
            "  Cube rouge, 2 clics : supprimer la salle\n" +
            "\n" +
            "Connexions\n" +
            "  Liaison · grip sphère orange, relâcher sur une salle : relier\n" +
            "  Liaison · gâchette sur un câble puis cube rouge : déconnecter\n" +
            "\n" +
            "Œuvres (salle sélectionnée, mode Déplacer)\n" +
            "  Grip sur l'œuvre : déplacer · près d'un mur : aimantée\n" +
            "  Relâcher dans une autre salle : transférer\n" +
            "\n" +
            "Vert = possible · Rouge = refusé (retour en place)";

        /// <summary>Static board listing every command, just below the plan board (spec 2.1).</summary>
        [MenuItem("Tools/Virtual Museum/Add Tutorial Board")]
        public static void AddTutorialBoard()
        {
            if (GameObject.Find("Tutorial Board") != null) return;
            var bench = GameObject.Find("Workbench");
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Tutorial Board";
            Object.DestroyImmediate(board.GetComponent<Collider>()); // must not catch the ray aimed at the plan
            if (bench != null) board.transform.SetParent(bench.transform, false);
            else board.transform.position = BenchPosition;
            board.transform.localPosition = new Vector3(0f, BoardCenterY - BoardSize.y / 2f - 0.3f, 0f);
            board.transform.localScale = new Vector3(1.2f, 0.5f, 0.01f);
            board.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Board", new Color(0.12f, 0.14f, 0.16f), false);

            var text = new GameObject("Text").AddComponent<TextMesh>();
            text.transform.SetParent(board.transform.parent, false);
            text.transform.localPosition = board.transform.localPosition + new Vector3(0f, 0f, -0.006f);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.fontSize = 48;
            text.characterSize = 0.0042f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Left;
            text.color = Color.white;
            text.text = TutorialText;
            text.gameObject.name = "Tutorial Text";
            Undo.RegisterCreatedObjectUndo(board, "Add Tutorial Board");
            Undo.RegisterCreatedObjectUndo(text.gameObject, "Add Tutorial Board");
            EditorSceneManager.MarkSceneDirty(board.scene);
        }

        static void AddRig()
        {
            if (GameObject.Find("XR Origin (XR Rig)") != null) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            if (prefab == null) { Debug.LogWarning("XR rig prefab not found at " + RigPath + ": add one manually."); return; }
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.transform.SetPositionAndRotation(BenchPosition + new Vector3(0f, 0f, -0.7f), Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(rig, "Add XR Rig");

            // The default scene camera would fight with the rig's camera.
            var defaultCamera = GameObject.Find("Main Camera");
            if (defaultCamera != null && defaultCamera.transform.parent == null && defaultCamera.transform.root != rig.transform)
            {
                Undo.DestroyObjectImmediate(defaultCamera);
                Debug.Log("Removed the default 'Main Camera' (the XR rig has its own).");
            }
        }

        static Material GetOrCreateMaterial(string name, Color color, bool unlit)
        {
            var path = MaterialsFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Directory.CreateDirectory(MaterialsFolder);
            var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find(unlit ? "Unlit/Color" : "Standard");
            mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
