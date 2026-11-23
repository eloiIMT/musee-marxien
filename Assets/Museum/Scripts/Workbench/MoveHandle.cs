using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Centre handle: grab it with the ray (trigger) and the room follows the point the ray aims at on the plan,
    /// keeping the grab offset, so the cursor stays on the room. A ghost shows the landing spot, snapped to a
    /// 0.5 m grid; turning the wrist like a knob rotates it in 15° steps. The API is called once, on release,
    /// and refused if the room would come closer than 1 m to another one or leave the plan board.
    /// </summary>
    public class MoveHandle : DragHandle
    {
        Room room;
        Museum museum;
        Vector3 target, grabOffset;
        float yaw, yawOffset;
        bool valid;
        string reason;

        /// <summary>
        /// Set when the handle is a room's floor: trigger anywhere on the floor moves that room, selected or not
        /// (it gets selected on grab). Null for the gizmo's centre handle, which moves the selected room.
        /// </summary>
        public string FixedRoomId { get; set; }

        protected override void BeginDrag()
        {
            if (FixedRoomId != null && Maquette.SelectedRoomId != FixedRoomId) Maquette.SelectRoom(FixedRoomId);
            room = Maquette.Api.GetRoom(FixedRoomId ?? Maquette.SelectedRoomId);
            museum = Maquette.Api.Data; // other rooms do not change during the gesture
            if (room == null) return;
            grabOffset = room.position - GrabPointer();
            grabOffset.y = 0f;
            yawOffset = MuseumGeometry.Yaw(room.rotation) - Maquette.HandTwist(Hand.rotation);
        }

        protected override void Drag(Vector3 handPosition, Quaternion handRotation)
        {
            if (room == null) return;
            target = MuseumGeometry.SnapXZ(Pointer() + grabOffset);
            yaw = MuseumGeometry.SnapAngle(Maquette.HandTwist(handRotation) + yawOffset);
            reason = null;
            if (!Maquette.IsOverBoard(Maquette.MuseumToWorld(target))) reason = "Hors du plan";
            else if (MuseumGeometry.OverlapsAny(museum, room.id, target, yaw, room.width, room.length))
                reason = "Trop proche d'une autre salle (1 m min.)";
            valid = reason == null;
            Maquette.Ghost.Show(target, yaw, room.width, room.length, valid, reason ?? $"{room.name} · {yaw:0}°");
        }

        protected override void EndDrag()
        {
            Maquette.Ghost.Hide();
            if (room == null) return;
            if (!valid) { Maquette.Toast(reason + " : salle remise en place", true); return; }
            bool moved = (target - room.position).sqrMagnitude > 1e-4f || Mathf.Abs(Mathf.DeltaAngle(yaw, MuseumGeometry.Yaw(room.rotation))) > 0.1f;
            if (!moved) return;
            Maquette.Api.SetRoomTransform(room.id, target, Quaternion.Euler(0f, yaw, 0f));
            Maquette.Toast($"« {room.name} » déplacée");
        }
    }
}
