using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VirtualMuseum.App;

namespace VirtualMuseum.AppEditor
{
    /// <summary>
    /// Builds the organizer test setup in the open scene: backend, immersive museum, vertical plan board with the
    /// interactive miniature (Maquette; rooms are created by clicking the empty plan), tutorial board below it,
    /// XR rig facing it.
    /// </summary>
    public static class MaquetteSandboxSetup
    {
        const string MaterialsFolder = "Assets/Museum/Materials";
        const string RigPath = "Assets/XR/Essentials/Prefabs/XR Origin (XR Rig).prefab";
        const string SandboxScenePath = "Assets/Museum/Scenes/Sandbox_Maquette.unity";
        const string MainScenePath = "Assets/Scenes/VirtualMuseum.unity";
        const string GachaModelPath = "Assets/Models/gashapon_machine/source/Gashapon_Machine_Low.fbx";
        const float BoardCenterY = 1.3f;                               // centre a little below eye height
        static readonly Vector3 BoardSize = new Vector3(2.0f, 1.4f, 0.03f); // architect plan sheet: width, height, thickness
        const float BoardTilt = 15f;                                   // on an easel: top leans away from the user
        const float UserDistance = 1.0f;                               // user stands this far in front of the plan
        static readonly Vector3 BenchPosition = new Vector3(5f, 0f, -10f);

        [MenuItem("Tools/Virtual Museum/Create Maquette Sandbox")]
        public static void Create()
        {
            if (UnityEngine.Object.FindAnyObjectByType<MuseumView>() != null)
            {
                Debug.LogWarning("This scene already contains a MuseumView. Nothing created.");
                return;
            }
            if (UnityEngine.Object.FindAnyObjectByType<MuseumBackend>() == null)
                VirtualMuseum.Editor.MuseumSetup.AddBackendToScene();
            var backend = UnityEngine.Object.FindAnyObjectByType<MuseumBackend>();

            var floor = GetOrCreateMaterial("Floor", new Color(0.75f, 0.75f, 0.72f), false);
            var wall = GetOrCreateMaterial("Wall", new Color(0.95f, 0.95f, 0.93f), false);
            var cable = GetOrCreateMaterial("Cable", new Color(1f, 0.55f, 0.1f), true);
            var boardMat = GetOrCreateMaterial("PlanBoard", new Color(0.22f, 0.24f, 0.27f), false);
            var handle = GetOrCreateMaterial("Handle", Color.white, true);

            // Immersive museum, scale 1, at the origin (museum coordinates = world coordinates).
            var immersive = new GameObject("Museum Immersive").AddComponent<MuseumView>();
            Configure(immersive, backend, floor, wall, cable, expanded: true, wallColliders: true, labelHeight: 0f, warnings: false, cables: false);

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
            board.transform.localRotation = Quaternion.Euler(BoardTilt, 0f, 0f);
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
            // No "new room" button: rooms are created by clicking the skeleton shown on the empty plan (RoomCreator,
            // added by the Maquette at runtime).

            Undo.RegisterCreatedObjectUndo(immersive.gameObject, "Create Maquette Sandbox");
            Undo.RegisterCreatedObjectUndo(bench.gameObject, "Create Maquette Sandbox");
            AddWorkshopFloor();
            AddTutorialBoard();
            CreateGachaMachine(bench, new Vector3(bench.position.x - 1.2f, bench.position.y, bench.position.z - 0.1f));
            AddRig();

            EditorSceneManager.MarkSceneDirty(immersive.gameObject.scene);
            Selection.activeGameObject = bench.gameObject;
            Debug.Log("Maquette sandbox created. Play (Quest Link or XR Device Simulator): the museum appears at scale 1 and on the plan board.");
        }

        [MenuItem("Tools/Virtual Museum/Add Gacha Machine to Sandbox")]
        public static void AddGachaMachineToSandbox()
        {
            var scene = SceneManager.GetSceneByPath(SandboxScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(SandboxScenePath, OpenSceneMode.Additive);

            var previousActiveScene = SceneManager.GetActiveScene();
            try
            {
                SceneManager.SetActiveScene(scene);
                Transform bench = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == "Workbench") bench = root.transform;
                }

                if (bench == null) throw new System.InvalidOperationException("Sandbox scene has no Workbench.");
                CreateGachaMachine(bench, new Vector3(bench.position.x - 1.2f, bench.position.y, bench.position.z - 0.1f));
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new System.InvalidOperationException("Could not save the Gacha Machine to the sandbox scene.");
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Tools/Virtual Museum/Add Gacha Machine to VirtualMuseum Scene")]
        public static void AddGachaMachineToVirtualMuseumScene()
        {
            var scene = SceneManager.GetSceneByPath(MainScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);

            if (!scene.IsValid() || !scene.isLoaded)
                throw new System.InvalidOperationException("Could not open " + MainScenePath);

            GameObject plan = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Plan") { plan = root; break; }

            if (plan == null)
                throw new System.InvalidOperationException("The VirtualMuseum scene has no Plan object.");
            var planRenderer = plan.GetComponent<Renderer>();
            if (planRenderer == null)
                throw new System.InvalidOperationException("The Plan object has no Renderer.");

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != "Gacha Machine") continue;
                Selection.activeGameObject = root;
                SceneView.lastActiveSceneView?.FrameSelected();
                Debug.Log("Gacha Machine is already present in the VirtualMuseum scene.", root);
                return;
            }

            var planBounds = planRenderer.bounds;
            var floorCenter = new Vector3(planBounds.max.x + 0.75f, 0f, planBounds.center.z - 0.2f);
            var machine = CreateGachaMachine(null, floorCenter);
            SceneManager.MoveGameObjectToScene(machine, scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException("Could not save the Gacha Machine to the VirtualMuseum scene.");

            Selection.activeGameObject = machine;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log("Gacha Machine added beside Plan in the VirtualMuseum scene.", machine);
        }

        static GameObject CreateGachaMachine(Transform parent, Vector3 floorCenter)
        {
            var existing = parent != null ? parent.Find("Gacha Machine") : null;
            if (existing != null) return existing.gameObject;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GachaModelPath);
            if (prefab == null)
                throw new System.InvalidOperationException("Gacha FBX not found at " + GachaModelPath);

            var machine = new GameObject("Gacha Machine");
            if (parent != null) machine.transform.SetParent(parent, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.name = "Gacha Machine Model";
            model.transform.SetParent(machine.transform, false);

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new System.InvalidOperationException("The Gacha FBX contains no renderers.");

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.y <= 0f)
                throw new System.InvalidOperationException("The Gacha FBX has no measurable height.");

            machine.transform.localScale = Vector3.one * (1.25f / bounds.size.y);
            bounds = GetCombinedBounds(renderers);
            var targetCenter = floorCenter + Vector3.up * bounds.extents.y;
            machine.transform.position += targetCenter - bounds.center;
            bounds = GetCombinedBounds(renderers);
            var originalCenter = bounds.center;
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            model.transform.position += originalCenter - GetCombinedBounds(renderers).center;

            var displayMaterial = GetOrCreateMaterial("GachaDisplay", new Color(0.08f, 0.09f, 0.11f), false);

            var front = bounds.min.z - 0.06f;
            var signPosition = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.62f + 0.17f, front);
            var sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sign.name = "Gacha Instructions";
            UnityEngine.Object.DestroyImmediate(sign.GetComponent<Collider>());
            sign.transform.SetParent(machine.transform, true);
            sign.transform.position = signPosition;
            sign.transform.localScale = Vector3.one * (1f / machine.transform.lossyScale.x);
            sign.transform.localScale = Vector3.Scale(sign.transform.localScale, new Vector3(0.42f, 0.18f, 0.025f));
            sign.GetComponent<Renderer>().sharedMaterial = displayMaterial;

            var instructions = CreateText(machine.transform, "Gacha Instructions Text", GachaInstructions,
                signPosition + new Vector3(0f, 0f, -0.014f), 0.0028f, 42, Color.white);
            instructions.alignment = TextAlignment.Center;
            instructions.anchor = TextAnchor.MiddleCenter;

            var dispenser = machine.AddComponent<GachaDispenser>();
            var (capsuleColor, capsuleClear) = GachaCapsuleMaterials.GetOrCreate();
            GachaCapsuleMaterials.Assign(dispenser, capsuleColor, capsuleClear);
            SetupGachaCrank(machine.transform);

            Undo.RegisterCreatedObjectUndo(machine, "Add Gacha Machine");
            return machine;
        }

        const string GachaInstructions = "TOURNEZ LA MANIVELLE\nGACHETTE + TOURNER LE POIGNET";
        const string GachaKnobName = "Top_Circle_Low";
        const float GachaCapsuleDiameter = 0.1f;
        // Machine-local points (every machine is built by CreateGachaMachine, so they hold for every copy), measured
        // on the FBX for a 0.1 m capsule: inside the chute behind the round opening on the right, and in the cup below.
        static readonly Vector3 GachaChutePoint = new Vector3(1.37f, 1.72f, -0.45f);
        static readonly Vector3 GachaCupPoint = new Vector3(1.37f, 1.34f, -1.62f);

        [MenuItem("Tools/Virtual Museum/Upgrade Gacha Machines to Crank")]
        public static void UpgradeGachaMachinesToCrank()
        {
            foreach (var path in new[] { SandboxScenePath, MainScenePath })
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool openedHere = !scene.IsValid() || !scene.isLoaded;
                if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    var machines = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                        .Where(t => t.name == "Gacha Machine" && t.GetComponentInChildren<GachaCrank>(true) == null)
                        .ToList();
                    foreach (var machine in machines) UpgradeGachaMachine(machine);
                    int upgraded = machines.Count;
                    if (upgraded == 0 && !scene.isDirty) continue;
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene))
                        throw new System.InvalidOperationException("Could not save " + path);
                    Debug.Log($"{upgraded} gacha machine(s) upgraded to the crank in {path}.");
                }
                finally
                {
                    if (openedHere) EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        /// <summary>Old machines: front button + tray at the bottom. Replace them with the knob and the right-hand cup.</summary>
        static void UpgradeGachaMachine(Transform machine)
        {
            foreach (var name in new[] { "Press To Dispense Button", "Capsule Output Tray", "Capsule Output Label" })
            {
                var old = machine.Find(name);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            }

            var text = machine.Find("Gacha Instructions Text");
            if (text != null && text.TryGetComponent<TextMesh>(out var mesh))
            {
                Undo.RecordObject(mesh, "Gacha instructions");
                mesh.text = GachaInstructions;
            }

            var dispenser = machine.GetComponent<GachaDispenser>();
            if (dispenser == null) dispenser = Undo.AddComponent<GachaDispenser>(machine.gameObject);
            var (capsuleColor, capsuleClear) = GachaCapsuleMaterials.GetOrCreate();
            GachaCapsuleMaterials.Assign(dispenser, capsuleColor, capsuleClear);
            SetupGachaCrank(machine);
        }

        /// <summary>Knob pivot (collider + XR interactable + <see cref="GachaCrank"/>), chute and cup points.</summary>
        static void SetupGachaCrank(Transform machine)
        {
            var knob = machine.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == GachaKnobName);
            if (knob == null || !knob.TryGetComponent<Renderer>(out var knobRenderer))
                throw new System.InvalidOperationException($"The gacha model has no '{GachaKnobName}' knob mesh.");

            var spawn = machine.Find("Capsule Spawn Point");
            if (spawn == null)
            {
                spawn = new GameObject("Capsule Spawn Point").transform;
                Undo.RegisterCreatedObjectUndo(spawn.gameObject, "Gacha chute");
                spawn.SetParent(machine, false);
            }
            Undo.RecordObject(spawn, "Gacha chute");
            spawn.SetLocalPositionAndRotation(GachaChutePoint, Quaternion.identity);

            var rest = new GameObject("Capsule Rest Point").transform;
            Undo.RegisterCreatedObjectUndo(rest.gameObject, "Gacha cup");
            rest.SetParent(machine, false);
            rest.SetLocalPositionAndRotation(GachaCupPoint, Quaternion.identity);

            var crank = new GameObject("Gacha Crank");
            Undo.RegisterCreatedObjectUndo(crank, "Gacha crank");
            crank.transform.SetParent(machine, false);
            crank.transform.position = knobRenderer.bounds.center;
            // Forward out of the machine, toward the user: positive turns are clockwise for them (see GachaCrank).
            crank.transform.rotation = Quaternion.LookRotation(-machine.forward, machine.up);
            var sphere = crank.AddComponent<SphereCollider>();
            sphere.radius = 0.11f / crank.transform.lossyScale.x;
            crank.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            var turn = crank.AddComponent<GachaCrank>();

            var dispenser = machine.GetComponent<GachaDispenser>();
            var serializedDispenser = new SerializedObject(dispenser);
            serializedDispenser.FindProperty("capsuleSpawnPoint").objectReferenceValue = spawn;
            serializedDispenser.FindProperty("capsuleRestPoint").objectReferenceValue = rest;
            serializedDispenser.FindProperty("capsuleDiameter").floatValue = GachaCapsuleDiameter;
            serializedDispenser.ApplyModifiedPropertiesWithoutUndo();

            var serializedCrank = new SerializedObject(turn);
            serializedCrank.FindProperty("knob").objectReferenceValue = knob;
            serializedCrank.FindProperty("dispenser").objectReferenceValue = dispenser;
            serializedCrank.ApplyModifiedPropertiesWithoutUndo();

            var (neonTube, neonGlow) = GachaCrankHintSetup.GetOrCreateMaterials();
            GachaCrankHintSetup.AddTo(turn, dispenser, neonTube, neonGlow);
        }

        static Bounds GetCombinedBounds(Renderer[] renderers)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static TextMesh CreateText(Transform parent, string objectName, string value, Vector3 position,
                                   float characterSize, int fontSize, Color color)
        {
            var textObject = new GameObject(objectName);
            textObject.transform.SetParent(parent, true);
            textObject.transform.position = position;
            textObject.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var inverseScale = 1f / parent.lossyScale.x;
            textObject.transform.localScale = new Vector3(-inverseScale, inverseScale, inverseScale);
            var text = textObject.AddComponent<TextMesh>();
            text.text = value;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = fontSize;
            text.characterSize = characterSize;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = color;
            text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            return text;
        }

        static void Configure(MuseumView view, MuseumBackend backend, Material floor, Material wall, Material cable,
                              bool expanded, bool wallColliders, float labelHeight, bool warnings, bool cables = true)
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
            so.FindProperty("showCables").boolValue = cables;
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

        const string WorkshopRoomPath = "Assets/3D/Atelier.fbx";

        /// <summary>
        /// Closed workshop room around the plan (Assets/3D/Atelier.fbx, built from workshop.glb by Assets/3D/Source~/build.py):
        /// room, wooden easel holding the plan, tutorial lectern on its right, storage table with the blue bin for unused
        /// artworks (Table_Bac, Bac_Oeuvres). Child of the Workbench so it is hidden in
        /// visitor mode; its origin is the Workbench origin. Spot_* empties mark the stations (gacha, PC screen, lever,
        /// elevator, bin...); <see cref="ApplyWorkshopLayout"/> moves the existing plan, buttons, tutorial and rig onto them.
        /// Decor has no colliders (rays must reach the plan) and casts no shadows (closed ceiling).
        /// </summary>
        [MenuItem("Tools/Virtual Museum/Add Workshop Room")]
        public static void AddWorkshopRoom()
        {
            var bench = GameObject.Find("Workbench");
            if (bench == null) { Debug.LogWarning("No 'Workbench' in the scene: create the maquette sandbox first."); return; }
            if (bench.transform.Find("Atelier") != null) { ApplyWorkshopLayout(); return; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorkshopRoomPath);
            if (prefab == null) { Debug.LogWarning("Workshop room model not found at " + WorkshopRoomPath); return; }

            var room = (GameObject)PrefabUtility.InstantiatePrefab(prefab, bench.transform);
            room.name = "Atelier";
            room.transform.localPosition = Vector3.zero;
            room.transform.localRotation = Quaternion.identity;
            room.transform.localScale = Vector3.one;
            foreach (var r in room.GetComponentsInChildren<MeshRenderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Undo.RegisterCreatedObjectUndo(room, "Add Workshop Room");

            // The room has its own floor and wall: keep the floor collider (the rig has gravity), hide the old visuals.
            HideRenderer(GameObject.Find("Workshop Floor"));
            HideRenderer(bench.transform.Find("Board Stand")?.gameObject);

            // Soft fill light under the ceiling (the directional light still lights the room: decor casts no shadows).
            var fill = new GameObject("Atelier Fill Light").AddComponent<Light>();
            fill.transform.SetParent(room.transform, false);
            fill.transform.localPosition = new Vector3(0f, 2.4f, -1.4f);
            fill.type = LightType.Point;
            fill.range = 6f;
            fill.intensity = 1.2f;
            fill.shadows = LightShadows.None;
            Undo.RegisterCreatedObjectUndo(fill.gameObject, "Add Workshop Room");

            ApplyWorkshopLayout();
            Debug.Log("Workshop room added under 'Workbench'. Spot_* children mark the stations. Save the scene.");
        }

        const string BlueprintTexturePath = "Assets/Museum/Textures/PlanBlueprint.png";
        const string BlueprintMaterialPath = MaterialsFolder + "/PlanBlueprint.mat";

        /// <summary>
        /// Puts the existing workshop objects on the Atelier's spots: plan board on the easel (size, tilt, blueprint paper),
        /// tutorial on the lectern, XR rig at Spot_Joueur. Re-run after re-exporting the FBX.
        /// Spot convention: the object's front (-Z) faces the spot's +Z.
        /// </summary>
        [MenuItem("Tools/Virtual Museum/Apply Workshop Layout")]
        public static void ApplyWorkshopLayout()
        {
            var bench = GameObject.Find("Workbench");
            var room = bench != null ? bench.transform.Find("Atelier") : null;
            if (room == null) { Debug.LogWarning("No 'Workbench/Atelier': run Tools > Virtual Museum > Add Workshop Room."); return; }
            var facing = Quaternion.Euler(0f, 180f, 0f);
            EnsureEmissiveCeilingLights();
            foreach (var mr in room.GetComponentsInChildren<MeshRenderer>())   // closed ceiling: no shadows from the decor
            {
                if (mr.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) continue;
                Undo.RecordObject(mr, "Apply Workshop Layout");
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Transform Spot(string name)
            {
                var s = room.Find(name);
                if (s == null) Debug.LogWarning("Atelier has no " + name + " (re-export Assets/3D/Atelier.fbx).");
                return s;
            }
            void Place(Transform t, Transform spot, Vector3 offset = default)
            {
                if (t == null || spot == null) return;
                Undo.RecordObject(t, "Apply Workshop Layout");
                var rot = spot.rotation * facing;
                t.SetPositionAndRotation(spot.position + rot * offset, rot);
            }

            var board = bench.transform.Find("Plan Board");
            var planSpot = Spot("Spot_Plan");
            Place(board, planSpot);
            if (board != null)
            {
                board.localScale = BoardSize;
                var r = board.GetComponent<Renderer>();
                Undo.RecordObject(r, "Apply Workshop Layout");
                r.sharedMaterial = GetOrCreateBlueprintMaterial();
                // The miniature is re-fitted at runtime (Maquette.Fit); keep its edit-time pose consistent with the board.
                var mini = bench.transform.Find("Museum Miniature");
                if (mini != null)
                {
                    Undo.RecordObject(mini, "Apply Workshop Layout");
                    mini.SetPositionAndRotation(board.position - board.forward * (BoardSize.z / 2f + 0.002f), board.rotation * Quaternion.Euler(-90f, 0f, 0f));
                }
            }

            var tutoSpot = Spot("Spot_Tuto");
            Place(bench.transform.Find("Tutorial Board"), tutoSpot);
            Place(bench.transform.Find("Tutorial Text"), tutoSpot, new Vector3(0f, 0f, -0.006f));

            // Reserve of unused artworks: the ArtworkBin sits on the bin's bottom (Spot_Bac), lights up its mesh.
            var binSpot = Spot("Spot_Bac");
            if (binSpot != null)
            {
                var bin = bench.transform.Find("Artwork Bin");
                if (bin == null)
                {
                    bin = new GameObject("Artwork Bin").transform;
                    bin.SetParent(bench.transform, false);
                    bin.gameObject.AddComponent<ArtworkBin>();
                    Undo.RegisterCreatedObjectUndo(bin.gameObject, "Apply Workshop Layout");
                }
                Place(bin, binSpot);
                var so = new SerializedObject(bin.GetComponent<ArtworkBin>());
                so.FindProperty("binRenderer").objectReferenceValue = room.Find("Bac_Oeuvres")?.GetComponent<Renderer>();
                so.ApplyModifiedProperties();
            }

            var stand = bench.transform.Find("Board Stand");
            if (stand != null && stand.gameObject.activeSelf)
            {
                Undo.RecordObject(stand.gameObject, "Apply Workshop Layout");
                stand.gameObject.SetActive(false); // the easel holds the plan now
            }

            var fill = room.Find("Atelier Fill Light");
            if (fill != null) { Undo.RecordObject(fill, "Apply Workshop Layout"); fill.localPosition = new Vector3(0f, 2.4f, -1.0f); }

            var rig = GameObject.Find("XR Origin (XR Rig)");
            var userSpot = Spot("Spot_Joueur");
            if (rig != null && userSpot != null)
            {
                Undo.RecordObject(rig.transform, "Apply Workshop Layout");
                rig.transform.SetPositionAndRotation(userSpot.position, userSpot.rotation);
            }
            EditorSceneManager.MarkSceneDirty(room.gameObject.scene);
        }

        const string TourScreenModelPath = "Assets/3D/EcranParcours.fbx";
        // On the workbench right behind the user (Spot_Joueur is at x = 0; top at y = 0.9), so it takes the place
        // reserved for the gacha (Spot_Gacha). Foot flush with the workbench front edge (z = -2.55, build.py Z_BENCH) and
        // facing straight +Z: the shelf above the workbench comes down to y = 1.45 from z = -2.8, and the screen top
        // (y = 1.64) must stay in front of it.
        static readonly Vector3 TourScreenPosition = new Vector3(0f, 0.9f, -2.68f);
        const float TourScreenModelScale = 2.5f;

        /// <summary>
        /// Tour editor screen (<see cref="TourScreen"/>) on the workbench behind the user: Kenney's "Computer Screen"
        /// (Assets/3D/EcranParcours.fbx, built by Assets/3D/Source~/build_screen.py), child of the Workbench so it is
        /// hidden in visitor mode. Re-running re-places it.
        /// </summary>
        [MenuItem("Tools/Virtual Museum/Add Tour Screen")]
        public static void AddTourScreen()
        {
            var bench = GameObject.Find("Workbench");
            if (bench == null) { Debug.LogWarning("No 'Workbench' in the scene: create the maquette sandbox first."); return; }
            var screen = bench.transform.Find("Tour Screen");
            if (screen == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TourScreenModelPath);
                if (prefab == null) { Debug.LogWarning("Tour screen model not found at " + TourScreenModelPath); return; }
                screen = new GameObject("Tour Screen").transform;
                screen.SetParent(bench.transform, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, screen);
                model.name = "Model";
                model.transform.localScale = Vector3.one * TourScreenModelScale;
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var tour = screen.gameObject.AddComponent<TourScreen>();
                var so = new SerializedObject(tour);
                so.FindProperty("backend").objectReferenceValue = Object.FindAnyObjectByType<MuseumBackend>();
                so.FindProperty("modelScale").floatValue = TourScreenModelScale;
                so.ApplyModifiedPropertiesWithoutUndo();
                Undo.RegisterCreatedObjectUndo(screen.gameObject, "Add Tour Screen");
            }
            // Front (-Z) towards the room (+Z), i.e. towards the user once turned around.
            Undo.RecordObject(screen, "Add Tour Screen");
            screen.SetLocalPositionAndRotation(TourScreenPosition, Quaternion.Euler(0f, 180f, 0f));
            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            Selection.activeGameObject = screen.gameObject;
        }

        const string CeilingLightMaterialPath = MaterialsFolder + "/AtelierLumiere.mat";

        /// <summary>The FBX's "Atelier_Lumiere" material (ceiling panels) is remapped to an emissive project material.</summary>
        static void EnsureEmissiveCeilingLights()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(CeilingLightMaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "AtelierLumiere" };
                var warm = new Color(1f, 0.96f, 0.88f);
                mat.SetColor("_BaseColor", warm);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", warm * 1.6f);
                AssetDatabase.CreateAsset(mat, CeilingLightMaterialPath);
                // set after CreateAsset: URP's material validation otherwise drops the keyword
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(mat);
            }
            var importer = AssetImporter.GetAtPath(WorkshopRoomPath) as ModelImporter;
            if (importer == null) return;
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), "Atelier_Lumiere");
            if (importer.GetExternalObjectMap().TryGetValue(id, out var current) && current == mat) return;
            importer.AddRemap(id, mat);
            importer.SaveAndReimport();
        }

        static Material GetOrCreateBlueprintMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(BlueprintMaterialPath);
            if (mat != null) return mat;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = "PlanBlueprint" };
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(BlueprintTexturePath);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);
            AssetDatabase.CreateAsset(mat, BlueprintMaterialPath);
            return mat;
        }

        static void HideRenderer(GameObject go)
        {
            if (go == null) return;
            var r = go.GetComponent<Renderer>();
            if (r == null || !r.enabled) return;
            Undo.RecordObject(r, "Add Workshop Room");
            r.enabled = false;
        }

        const string TutorialText =
            "MAQUETTE — COMMANDES\n" +
            "\n" +
            "Rayon : pointer · Gâchette (seul bouton) : cliquer et saisir\n" +
            "Salle sélectionnée, rayon sur le plan : joystick droit ← → = Déplacer / Redimensionner / Liaison\n" +
            "\n" +
            "Salles\n" +
            "  Gâchette sur une salle : sélectionner (passe en 3D)\n" +
            "  Gâchette sur le plan vide : désélectionner la salle\n" +
            "  Rien de sélectionné, rayon sur le plan vide : squelette 6 × 6 m, gâchette : créer\n" +
            "  Déplacer · gâchette sur le sol (ou sphère bleue) : bouger · poignet : 15°\n" +
            "  Redimensionner · gâchette cubes jaunes (coins) : largeur / longueur\n" +
            "  Redimensionner · slider sur le mur du bas : hauteur (glisser, ou gâchette sur la piste)\n" +
            "  Cube rouge, 2 clics : supprimer la salle\n" +
            "\n" +
            "Connexions\n" +
            "  Liaison · gâchette sur une autre salle : relier (vert)\n" +
            "  Liaison · gâchette sur une salle déjà reliée : délier\n" +
            "\n" +
            "Œuvres (salle sélectionnée, mode Déplacer)\n" +
            "  Gâchette sur l'œuvre : déplacer · près d'un mur : aimantée\n" +
            "  Relâcher dans une autre salle : transférer\n" +
            "  Relâcher avec le rayon sur le bac bleu : ranger dans la réserve\n" +
            "\n" +
            "Réserve (bac bleu)\n" +
            "  Œuvre en main lâchée dans le bac : rangée · la saisir dans le bac : replacer sur le plan\n" +
            "\n" +
            "Vert = possible · Rouge = refusé (retour en place)";

        /// <summary>Static board listing every command, just below the plan board (spec 2.1).</summary>
        [MenuItem("Tools/Virtual Museum/Add Tutorial Board")]
        public static void AddTutorialBoard()
        {
            if (GameObject.Find("Tutorial Board") != null)
            {
                // Already there: refresh its text (the commands changed).
                var existing = GameObject.Find("Tutorial Text")?.GetComponent<TextMesh>();
                if (existing != null && existing.text != TutorialText)
                {
                    Undo.RecordObject(existing, "Add Tutorial Board");
                    existing.text = TutorialText;
                    EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
                }
                return;
            }
            var bench = GameObject.Find("Workbench");
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Tutorial Board";
            UnityEngine.Object.DestroyImmediate(board.GetComponent<Collider>()); // must not catch the ray aimed at the plan
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
            rig.transform.SetPositionAndRotation(BenchPosition + new Vector3(0f, 0f, -UserDistance), Quaternion.identity);
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
