using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// One artwork of the life-size museum, with its museum label ("cartel"): a lectern standing on the floor beside
    /// the artwork, on the viewer's right, showing the title and description of THIS instance. The text is part of
    /// the world (diegetic), like in a real museum: nothing floats in front of the user, nothing follows the head.
    /// The artwork itself is inert for the visitor: no collider, nothing happens when the ray passes over it.
    /// </summary>
    public class ArtworkTarget : MonoBehaviour
    {
        const float PostHeight = 0.95f, PlateW = 0.5f, PlateH = 0.38f, Tilt = 35f;
        const float TitleH = 0.034f, BodyH = 0.017f, Margin = 0.025f;
        const float WallClearance = 0.3f;
        static readonly Color PlateColor = new Color(0.12f, 0.13f, 0.15f);

        public string ArtworkId { get; private set; }
        /// <summary>Artwork bounds in its own local space.</summary>
        public Bounds LocalBounds { get; private set; }
        /// <summary>The floor-standing label (for tests / other tools).</summary>
        public Transform Label => label;

        VisitorGuide guide;
        Transform label, plate, strip;
        TextMesh title, body;

        public void Init(VisitorGuide owner, string artworkId)
        {
            guide = owner;
            ArtworkId = artworkId;
            LocalBounds = ComputeLocalBounds();
            BuildLabel();
            Refresh();
        }

        /// <summary>Lectern: a post on the floor and a tilted plate at reading height (~1 m), facing the viewer.</summary>
        void BuildLabel()
        {
            label = new GameObject("Label (cartel)").transform;
            label.SetParent(transform, false);
            var s = transform.lossyScale; // keep real size whatever the artwork scale
            label.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);

            UiKit.Plate(label, "Post", new Vector3(0, PostHeight / 2f, 0.03f), new Vector3(0.05f, PostHeight, 0.05f), new Color(0.3f, 0.31f, 0.33f));
            UiKit.Plate(label, "Foot", new Vector3(0, 0.01f, 0.03f), new Vector3(0.22f, 0.02f, 0.22f), new Color(0.3f, 0.31f, 0.33f));
            var head = new GameObject("Head").transform;
            head.SetParent(label, false);
            head.SetLocalPositionAndRotation(new Vector3(0, PostHeight + 0.08f, 0), Quaternion.Euler(Tilt, 0, 0));
            plate = UiKit.Plate(head, "Plate", new Vector3(0, 0, 0.012f), new Vector3(PlateW, PlateH, 0.02f), PlateColor);
            strip = UiKit.Plate(head, "Strip", new Vector3(0, PlateH / 2f - 0.006f, -0.001f), new Vector3(PlateW, 0.012f, 0.004f), UiKit.MutedText);
            float left = -PlateW / 2f + Margin;
            title = UiKit.Text(head, "", new Vector3(left, PlateH / 2f - 0.03f, -0.002f), TitleH, Color.white);
            body = UiKit.Text(head, "", new Vector3(left, 0, -0.002f), BodyH, UiKit.TextColor);
        }

        Bounds ComputeLocalBounds()
        {
            bool any = false;
            var result = new Bounds();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is LineRenderer || r.GetComponent<TextMesh>() != null) continue;
                var lb = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(i % 2 == 0 ? lb.min.x : lb.max.x, (i / 2) % 2 == 0 ? lb.min.y : lb.max.y, i / 4 == 0 ? lb.min.z : lb.max.z);
                    var p = transform.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return any ? result : new Bounds(Vector3.up * 0.5f, Vector3.one * 0.5f);
        }

        /// <summary>Re-read the texts and re-place the label (after any museum refresh).</summary>
        public void Refresh()
        {
            if (label == null) return;
            var a = guide.GetArtwork(ArtworkId);
            if (a != null)
            {
                var t = UiKit.Wrap(a.title, UiKit.CharsFor(PlateW - 2 * Margin, TitleH), 2);
                title.text = t;
                var desc = string.IsNullOrWhiteSpace(a.description) ? "" : a.description;
                int lines = Mathf.Max(1, Mathf.FloorToInt((PlateH - 0.05f - UiKit.LineCount(t) * TitleH * 1.05f - Margin) / (BodyH * 1.1f)));
                body.text = UiKit.Wrap(desc, UiKit.CharsFor(PlateW - 2 * Margin, BodyH), lines);
                body.transform.localPosition = new Vector3(body.transform.localPosition.x,
                    PlateH / 2f - 0.03f - UiKit.LineCount(t) * TitleH * 1.05f - 0.012f, -0.002f);
            }
            PlaceLabel(a != null ? guide.GetRoom(a.roomId) : null);
            RefreshColours();
        }

        /// <summary>
        /// On the floor beside the artwork, on the right of a visitor coming from where they entered the room
        /// (<see cref="VisitorGuide.ArrivalLocal"/>: elevator platform or door), slightly towards them, kept inside the
        /// room. The plate turns to face that point, so it reads from where the visitor arrives, whichever way.
        /// </summary>
        void PlaceLabel(Room room)
        {
            var roomSpace = transform.parent; // RoomView's artworks root: same frame as the room
            if (roomSpace == null) return;
            var art = transform.localPosition;
            float yaw = transform.localRotation.eulerAngles.y; // facing the artwork's front (+Z) by default
            if (room != null)
            {
                var toVisitor = guide.ArrivalLocal(room) - art;
                toVisitor.y = 0f;
                if (toVisitor.sqrMagnitude > 0.01f) yaw = Mathf.Atan2(toVisitor.x, toVisitor.z) * Mathf.Rad2Deg;
            }
            var s = transform.localScale;
            float half = Mathf.Max(Mathf.Max(LocalBounds.extents.x * s.x, LocalBounds.extents.z * s.z), 0.15f);
            // Visitor looks along -yaw: their right is -X in that frame.
            var local = art + Quaternion.Euler(0, yaw, 0) * new Vector3(-(half + 0.45f), 0, 0.35f);
            local.y = 0;
            if (room != null)
            {
                float hw = Mathf.Max(room.width / 2f - WallClearance, 0f), hl = Mathf.Max(room.length / 2f - WallClearance, 0f);
                local.x = Mathf.Clamp(local.x, -hw, hw);
                local.z = Mathf.Clamp(local.z, -hl, hl);
            }
            if (room != null) // the plate itself faces the visitor's arrival point
            {
                var toVisitor = guide.ArrivalLocal(room) - local;
                toVisitor.y = 0f;
                if (toVisitor.sqrMagnitude > 0.01f) yaw = Mathf.Atan2(toVisitor.x, toVisitor.z) * Mathf.Rad2Deg;
            }
            label.SetPositionAndRotation(roomSpace.TransformPoint(local), roomSpace.rotation * Quaternion.Euler(0, yaw + 180f, 0));
        }

        void RefreshColours()
        {
            // RoomView clears every child renderer's tint on refresh (artwork warnings): re-apply ours.
            Tint.Set(plate.GetComponent<Renderer>(), PlateColor);
            Tint.Set(strip.GetComponent<Renderer>(), UiKit.MutedText);
            foreach (var r in label.GetComponentsInChildren<Renderer>())
                if (r.name == "Post" || r.name == "Foot") Tint.Set(r, new Color(0.3f, 0.31f, 0.33f));
        }
    }
}
