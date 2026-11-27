using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualMuseum.App
{
    /// <summary>Look of the life-size museum's doors (set on the immersive <see cref="MuseumView"/>).</summary>
    [Serializable]
    public class DoorStyle
    {
        [Tooltip("Door model: arch against a wall, centred on x, floor at 0, wall plane at z = 0 (Assets/3D/MuseumDoor.fbx).")]
        public GameObject model;
        [Tooltip("Material of the arch: the walls' material, so the door is part of the wall.")]
        public Material frame;
        [Tooltip("Museum/DoorDepth: the dark passage seen through the arch, going on behind the wall.")]
        public Material dark;
        [Tooltip("Largest scale of the model (4 m tall at scale 1). Lower rooms get smaller doors.")]
        public float maxScale = 0.7f;
        public Color engraving = new(0.3f, 0.26f, 0.21f);
        public Color engravingLight = new(1f, 0.98f, 0.94f);
    }

    /// <summary>
    /// One end of a connection in the life-size museum: a dark arched passage in the wall facing the other room (an arch
    /// frame in the walls' colour), with that room's name engraved on its top band. Walking into it (the head passing
    /// the arch's front) takes the
    /// visitor to the other room, in front of the matching door, facing into that room (short fade). Both doors of a
    /// connection are identical (same model, size, scale), placed by <see cref="ConnectionLayout"/>, so they appear,
    /// move and disappear with the connection, in every view (the plans draw them as door blocks).
    /// </summary>
    public class DoorPortal : MonoBehaviour
    {
        // Model measurements at scale 1 (MuseumDoor.fbx, see Assets/3D/Source~/build_door.py).
        public const float ModelWidth = 4.06f, ModelHeight = 4f, ModelDepth = 0.416f;
        const float OpeningHalf = 0.72f, OpeningTop = 2.95f, DarkTop = 3.2f;
        const float BandCentre = 3.8f, BandFront = 0.416f, BandWidth = 3.7f, LetterHeight = 0.2f;
        /// <summary>Gap kept between the top of the door and the ceiling's cornice (m).</summary>
        public const float CeilingClearance = 0.38f;
        const float ArrivalGap = 0.6f; // in front of the destination door (m)

        static VisitorMode mode;

        public string Key { get; private set; }
        public string RoomId { get; private set; }
        public string OtherRoomId { get; private set; }
        public float Scale { get; private set; }

        MuseumView view;
        DoorStyle style;
        Transform model, darkQuad, textRoot, colliderL, colliderR;
        TextMesh engraved, highlight;
        Quaternion modelRotation = Quaternion.identity; // model's own import rotation, turned to face the room

        /// <summary>Scale of both doors of a connection: as large as allowed by the lower room and the narrower door slot.</summary>
        public static float ScaleFor(DoorStyle style, Room a, Door da, Room b, Door db)
        {
            float byHeight = (Mathf.Min(a.height, b.height) - CeilingClearance) / ModelHeight;
            float byWidth = Mathf.Min(da.Width, db.Width) / ModelWidth;
            return Mathf.Max(0.2f, Mathf.Min(style.maxScale, byHeight, byWidth));
        }

        public void Init(MuseumView owner, DoorStyle doorStyle, string key, string roomId, string otherRoomId)
        {
            view = owner;
            style = doorStyle;
            Key = key;
            RoomId = roomId;
            OtherRoomId = otherRoomId;
            name = "Porte → " + otherRoomId;

            if (style.model != null)
            {
                model = Instantiate(style.model, transform, false).transform;
                model.name = "Arche";
                // the portal must stand in front of the wall (-Z): turn the model if it was exported the other way
                modelRotation = model.localRotation;
                if (ModelBounds(model, transform).center.z > 0f) modelRotation = Quaternion.Euler(0f, 180f, 0f) * modelRotation;
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    if (style.frame != null) r.sharedMaterial = style.frame;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
                foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); // faces -Z: seen from the room
            quad.name = "Passage sombre (profondeur)";
            Destroy(quad.GetComponent<Collider>());
            darkQuad = quad.transform;
            darkQuad.SetParent(transform, false);
            var qr = quad.GetComponent<Renderer>();
            if (style.dark != null) qr.sharedMaterial = style.dark;
            qr.shadowCastingMode = ShadowCastingMode.Off;
            qr.receiveShadows = false;

            textRoot = new GameObject("Nom gravé").transform;
            textRoot.SetParent(transform, false);
            highlight = UiKit.Text(textRoot, "", Vector3.zero, LetterHeight, style.engravingLight, TextAnchor.MiddleCenter, TextAlignment.Center);
            engraved = UiKit.Text(textRoot, "", Vector3.zero, LetterHeight, style.engraving, TextAnchor.MiddleCenter, TextAlignment.Center);
            // Fixed draw order (dark letters over their light edge): sorting the two transparent texts by distance
            // flips from frame to frame as the head moves, and the letters flicker.
            highlight.GetComponent<MeshRenderer>().sortingOrder = 0;
            engraved.GetComponent<MeshRenderer>().sortingOrder = 1;

            colliderL = new GameObject("Montant G").transform;
            colliderR = new GameObject("Montant D").transform;
            foreach (var c in new[] { colliderL, colliderR })
            {
                c.SetParent(transform, false);
                c.gameObject.AddComponent<BoxCollider>();
            }
        }

        /// <param name="innerFaceLocal">Door middle on the inner face of the wall, museum coordinates (floor level).</param>
        /// <param name="rotation">Museum-space rotation: +Z = out of the room, through the wall.</param>
        /// <param name="room">This door's room: the passage's parquet continues the room's floor pattern.</param>
        public void Apply(Vector3 innerFaceLocal, Quaternion rotation, float scale, string otherRoomName, Room room)
        {
            Scale = scale;
            transform.SetLocalPositionAndRotation(innerFaceLocal, rotation);
            SetFloorFrame(room);
            float s = scale;
            if (model != null)
            {
                model.localPosition = Vector3.zero;
                model.localRotation = modelRotation;
                model.localScale = Vector3.one * s;
            }
            // up to the arch's soffit (hidden behind the arch above the opening)
            // well clear of the baseboard (18 mm) and the wall: no depth fighting at a distance
            darkQuad.localPosition = new Vector3(0f, DarkTop * s / 2f, -0.05f);
            // exactly the opening's width: the passage drawn behind it (Museum/DoorDepth) lines up with the jambs
            darkQuad.localScale = new Vector3(OpeningHalf * 2f * s + 0.04f, DarkTop * s, 1f);

            float sideCentre = (OpeningHalf + ModelWidth / 2f) / 2f * s, sideWidth = (ModelWidth / 2f - OpeningHalf) * s;
            colliderL.localPosition = new Vector3(-sideCentre, ModelHeight * s / 2f, -ModelDepth * s / 2f);
            colliderR.localPosition = new Vector3(sideCentre, ModelHeight * s / 2f, -ModelDepth * s / 2f);
            colliderL.localScale = colliderR.localScale = new Vector3(sideWidth, ModelHeight * s, ModelDepth * s);

            // Engraved name: dark letters on the arch's top band, a light edge just below (light from above).
            var text = (otherRoomName ?? "").ToUpperInvariant();
            textRoot.localPosition = new Vector3(0f, BandCentre * s, -BandFront * s - 0.008f);
            foreach (var tm in new[] { engraved, highlight })
            {
                tm.text = text;
                Labels.SetWorldHeight(tm, LetterHeight * s);
            }
            highlight.transform.localPosition = new Vector3(0f, -0.012f * s, 0.004f);
            engraved.transform.localPosition = new Vector3(0f, 0f, -0.001f);
            FitWidth(BandWidth * s);
        }

        static readonly int FloorFrame = Shader.PropertyToID("_FloorFrame");
        MaterialPropertyBlock floorBlock;

        /// <summary>The room floor's frame in world space (centre, local +X), for Museum/DoorDepth's parquet.</summary>
        void SetFloorFrame(Room room)
        {
            var museumSpace = transform.parent;
            if (room == null || museumSpace == null) return;
            var origin = museumSpace.TransformPoint(room.position);
            var right = museumSpace.TransformDirection(room.rotation * Vector3.right);
            floorBlock ??= new MaterialPropertyBlock();
            var r = darkQuad.GetComponent<Renderer>();
            r.GetPropertyBlock(floorBlock);
            floorBlock.SetVector(FloorFrame, new Vector4(origin.x, origin.z, right.x, right.z));
            r.SetPropertyBlock(floorBlock);
        }

        /// <summary>Shrink long names so they stay on the band.</summary>
        void FitWidth(float width)
        {
            var r = engraved.GetComponent<Renderer>();
            // world-space bounds, measured along the door's x axis
            float w = Mathf.Abs(transform.InverseTransformVector(r.bounds.size).x);
            if (w <= width || w <= 0f) return;
            float k = width / w;
            engraved.transform.localScale *= k;
            highlight.transform.localScale *= k;
        }

        /// <summary>Where the visitor arrives when coming through the matching door: in front of this one, facing the room.</summary>
        public (Vector3 feet, Vector3 forward) Arrival()
        {
            var inward = -transform.forward;
            return (transform.position + inward * (ModelDepth * Scale + ArrivalGap), inward);
        }

        void Update()
        {
            if (mode == null) mode = FindAnyObjectByType<VisitorMode>();
            if (mode == null || mode.Mode != AppMode.Visitor || mode.Busy || mode.Rig == null) return;
            if (mode.Guide == null || mode.Guide.CurrentRoomId != RoomId) return;
            var head = transform.InverseTransformPoint(mode.Rig.Camera.transform.position);
            float s = Scale;
            // head in the doorway: between the jambs, under the arch, 5 cm past the arch's front
            if (Mathf.Abs(head.x) > OpeningHalf * s || head.y > OpeningTop * s + 0.3f || head.z < -ModelDepth * s + 0.05f || head.z > 0.6f) return;
            var other = view.GetDoor(Key, OtherRoomId);
            if (other == null) return;
            var (feet, forward) = other.Arrival();
            mode.TeleportThroughDoor(OtherRoomId, feet, forward);
        }

        /// <summary>Bounds of the meshes under <paramref name="root"/>, in <paramref name="frame"/>'s space.</summary>
        static Bounds ModelBounds(Transform root, Transform frame)
        {
            bool any = false;
            var bounds = new Bounds();
            var toRoot = frame.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = toRoot * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { bounds = new Bounds(corner, Vector3.zero); any = true; }
                    else bounds.Encapsulate(corner);
                }
            }
            return bounds;
        }
    }
}
