using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Handles shown on the selected room. Parented to the RoomView so it follows its position and rotation.
    ///  - centre sphere (blue): move / rotate          (grip)
    ///  - 4 floor corners (yellow): width / length     (grip, opposite corner fixed)
    ///  - top-left corner (green): height              (grip, vertical)
    ///  - 4 wall spheres (orange): pull a connection   (grip, release over another room)
    ///  - top-right cube (red): delete, click twice    (trigger)
    /// Handle size is constant in world space so they stay grabbable whatever the miniature scale.
    /// </summary>
    public class RoomGizmo : MonoBehaviour
    {
        const float MoveSize = 0.035f, CornerSize = 0.022f, HeightSize = 0.022f, ConnectSize = 0.02f, DeleteSize = 0.022f;
        const float ConnectorOffset = 0.8f; // museum metres outside the wall

        Maquette maquette;
        RoomView room;
        readonly List<(Transform t, float size)> handles = new();
        Transform move, height, delete;
        readonly ResizeHandle[] corners = new ResizeHandle[4];
        readonly ConnectHandle[] connectors = new ConnectHandle[4];
        DeleteButton deleteButton;

        public void Init(Maquette owner)
        {
            maquette = owner;
            move = Create<MoveHandle>(PrimitiveType.Sphere, "Move", new Color(0.25f, 0.55f, 1f), MoveSize).transform;
            int i = 0;
            foreach (var (sx, sz) in new[] { (1, 1), (1, -1), (-1, -1), (-1, 1) })
            {
                corners[i] = Create<ResizeHandle>(PrimitiveType.Cube, "Resize", new Color(1f, 0.85f, 0.2f), CornerSize);
                corners[i].SetCorner(sx, sz);
                i++;
            }
            height = Create<HeightHandle>(PrimitiveType.Cylinder, "Height", new Color(0.3f, 0.9f, 0.4f), HeightSize).transform;
            for (int w = 0; w < 4; w++)
                connectors[w] = Create<ConnectHandle>(PrimitiveType.Sphere, "Connect", new Color(1f, 0.55f, 0.1f), ConnectSize);

            var del = GameObject.CreatePrimitive(PrimitiveType.Cube);
            del.name = "Delete";
            del.transform.SetParent(transform, false);
            del.GetComponent<Renderer>().sharedMaterial = owner.HandleMaterial;
            Tint.Set(del.GetComponent<Renderer>(), new Color(1f, 0.2f, 0.2f));
            deleteButton = del.AddComponent<DeleteButton>();
            deleteButton.Init(owner);
            delete = del.transform;
            handles.Add((delete, DeleteSize));

            Attach(null);
        }

        T Create<T>(PrimitiveType shape, string handleName, Color color, float size) where T : DragHandle
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = handleName;
            go.transform.SetParent(transform, false);
            go.GetComponent<Renderer>().sharedMaterial = maquette.HandleMaterial;
            var h = go.AddComponent<T>();
            h.Init(maquette, color);
            handles.Add((go.transform, size));
            return h;
        }

        /// <summary>Show the gizmo on a room, or hide it (null).</summary>
        public void Attach(RoomView target)
        {
            if (target == room && gameObject.activeSelf == (target != null)) return;
            room = target;
            // Never stay parented to a room that may be destroyed.
            transform.SetParent(target != null ? target.transform : maquette.transform, false);
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            gameObject.SetActive(target != null);
            if (target != null) LateUpdate();
        }

        void LateUpdate()
        {
            if (room == null || room.Data == null) return;
            var d = room.Data;
            float hw = d.width / 2f, hl = d.length / 2f, h = d.height;
            move.localPosition = new Vector3(0, 0.6f, 0);
            foreach (var c in corners) c.transform.localPosition = new Vector3(c.SignX * hw, 0.3f, c.SignZ * hl);
            height.localPosition = new Vector3(-hw, h, -hl);
            delete.localPosition = new Vector3(hw, h, -hl);
            connectors[0].transform.localPosition = new Vector3(0, 0.3f, hl + ConnectorOffset);
            connectors[1].transform.localPosition = new Vector3(0, 0.3f, -hl - ConnectorOffset);
            connectors[2].transform.localPosition = new Vector3(hw + ConnectorOffset, 0.3f, 0);
            connectors[3].transform.localPosition = new Vector3(-hw - ConnectorOffset, 0.3f, 0);

            float parentScale = Mathf.Max(transform.lossyScale.x, 1e-5f);
            foreach (var (t, size) in handles) t.localScale = Vector3.one * (size / parentScale);
        }
    }
}
