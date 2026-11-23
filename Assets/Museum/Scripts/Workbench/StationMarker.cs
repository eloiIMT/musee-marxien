using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// On a miniature room: where the visitor's wall plan and its lever will be
    /// (<see cref="MuseumStation"/>). The plan board and the lever are drawn in 3D against the plan wall, at their real
    /// height while the room is expanded, squeezed into the low plan walls otherwise. The outline of the area in front
    /// of them where artworks may not go is hidden, shown only while an artwork is dragged into it (<see cref="ShowZone"/>).
    /// No collider: the ray goes through. Placed by <see cref="Maquette"/>.
    /// </summary>
    public class StationMarker : MonoBehaviour
    {
        // Sizes match the visitor's station (RoomPlan board, ElevatorRide lever height).
        const float BoardWidth = 2f * MuseumStation.PlanHalfWidth, BoardHeight = 0.72f, BoardCenterY = 1.5f, BoardDepth = 0.04f;
        const float LeverHeight = 1.45f;

        static readonly Color FrameColor = new Color(0.16f, 0.17f, 0.2f);
        static readonly Color BlueprintColor = new Color(0.25f, 0.45f, 0.85f);
        static readonly Color LeverBaseColor = new Color(0.35f, 0.36f, 0.4f);
        static readonly Color LeverKnobColor = new Color(1f, 0.55f, 0.1f);
        static readonly Color ZoneColor = new Color(1f, 0.55f, 0.1f);
        const float Lift = 0.02f, Thin = 0.04f, ZoneBar = 0.06f;

        RoomView room;
        Transform root, fixtures, zoneRoot;
        readonly Transform[] zone = new Transform[4];
        Transform frame, blueprint, leverBase, leverStick, leverKnob;
        float roomHeight = 3f;

        void Awake() => room = GetComponent<RoomView>();

        public void Apply(Room data, Museum museum, Material material)
        {
            if (data == null) return;
            if (root == null) Build(material);
            roomHeight = Mathf.Max(data.height, 0.01f);
            var s = MuseumStation.Compute(data, museum);
            var rot = Quaternion.LookRotation(s.normal, Vector3.up); // local z = into the wall, x = plan's right

            // Plan board flat against the wall's inner face (s.wallCentre), facing into the room.
            var boardPos = s.wallCentre - s.normal * (BoardDepth / 2f + 0.005f) + Vector3.up * BoardCenterY;
            Place(frame, boardPos, rot, new Vector3(BoardWidth, BoardHeight, BoardDepth));
            Place(blueprint, boardPos - s.normal * (BoardDepth / 2f + 0.002f), rot,
                  new Vector3(BoardWidth - 0.08f, BoardHeight - 0.08f, 0.004f));

            // Lever beside it: base plate on the wall, a handle sticking out and up, an orange knob.
            var basePos = s.wallCentre + s.along * (s.side * s.offset) + Vector3.up * LeverHeight;
            Place(leverBase, basePos - s.normal * 0.015f, rot, new Vector3(0.14f, 0.3f, 0.03f));
            var stickDir = (Vector3.up - s.normal * 1.2f).normalized; // out of the wall, tilted up
            const float stickLength = 0.3f;
            Place(leverStick, basePos - s.normal * 0.03f + stickDir * (stickLength / 2f),
                  Quaternion.FromToRotation(Vector3.up, stickDir), new Vector3(0.035f, stickLength / 2f, 0.035f)); // cylinder: 2 units tall
            Place(leverKnob, basePos - s.normal * 0.03f + stickDir * stickLength, Quaternion.identity, Vector3.one * 0.09f);

            // Reserved area (MuseumStation.IsReserved): outline on the floor, from the plan's far edge to past the lever,
            // ClearDepth into the room.
            float a0 = -s.ClearPlanSide * s.side, a1 = s.ClearLeverSide * s.side, depth = MuseumStation.ClearDepth;
            float width = Mathf.Abs(a1 - a0);
            var mid = s.wallCentre + s.along * ((a0 + a1) / 2f) + Vector3.up * (Lift + Thin / 2f);
            Place(zone[0], mid - s.normal * depth, rot, new Vector3(width, Thin, ZoneBar));
            Place(zone[1], mid - s.normal * (ZoneBar / 2f), rot, new Vector3(width, Thin, ZoneBar));
            Place(zone[2], mid + s.along * (width / 2f) - s.normal * (depth / 2f), rot, new Vector3(ZoneBar, Thin, depth));
            Place(zone[3], mid - s.along * (width / 2f) - s.normal * (depth / 2f), rot, new Vector3(ZoneBar, Thin, depth));
            LateUpdate();
        }

        /// <summary>Show / hide the outline of the area kept free in front of the plan and lever.</summary>
        public void ShowZone(bool visible)
        {
            if (zoneRoot != null && zoneRoot.gameObject.activeSelf != visible) zoneRoot.gameObject.SetActive(visible);
        }

        void LateUpdate()
        {
            // Wall fixtures follow the walls as drawn: real height when expanded, squeezed into the low plan walls.
            if (fixtures == null || room == null) return;
            float k = room.DrawnWallHeight / roomHeight;
            var scale = new Vector3(1f, k, 1f);
            if (fixtures.localScale != scale) fixtures.localScale = scale;
        }

        void Build(Material material)
        {
            root = new GameObject("Station Marker").transform;
            root.SetParent(transform, false);
            fixtures = new GameObject("Wall Fixtures").transform; // plan + lever, scaled vertically with the walls
            fixtures.SetParent(root, false);
            frame = Part(fixtures, "Plan Board", PrimitiveType.Cube, material, FrameColor);
            blueprint = Part(fixtures, "Plan Blueprint", PrimitiveType.Cube, material, BlueprintColor);
            leverBase = Part(fixtures, "Lever Base", PrimitiveType.Cube, material, LeverBaseColor);
            leverStick = Part(fixtures, "Lever Handle", PrimitiveType.Cylinder, material, LeverBaseColor);
            leverKnob = Part(fixtures, "Lever Knob", PrimitiveType.Sphere, material, LeverKnobColor);
            zoneRoot = new GameObject("Reserved Area").transform;
            zoneRoot.SetParent(root, false);
            for (int i = 0; i < zone.Length; i++) zone[i] = Part(zoneRoot, "Reserved", PrimitiveType.Cube, material, ZoneColor);
            zoneRoot.gameObject.SetActive(false); // shown only while an artwork is dragged into it
        }

        static Transform Part(Transform parent, string partName, PrimitiveType shape, Material material, Color color)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = partName;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            if (material != null) r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Tint.Set(r, color);
            return go.transform;
        }

        static void Place(Transform t, Vector3 localPosition, Quaternion rotation, Vector3 size)
        {
            t.SetLocalPositionAndRotation(localPosition, rotation);
            t.localScale = size;
        }
    }
}
