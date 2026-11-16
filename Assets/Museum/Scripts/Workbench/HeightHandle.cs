using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Top corner handle: pull the hand towards you / push it back towards the plan to change the wall height
    /// (0.5 m steps, 2 to 8 m). Relative to the hand movement, so no cursor is needed.
    /// </summary>
    public class HeightHandle : DragHandle
    {
        Room room;
        float startHandHeight, height;

        protected override void BeginDrag()
        {
            room = Maquette.Api.GetRoom(Maquette.SelectedRoomId);
            if (room == null) return;
            startHandHeight = Maquette.MuseumHeight(Hand.position);
            height = room.height;
        }

        protected override void Drag(Vector3 handPosition, Quaternion handRotation)
        {
            if (room == null) return;
            float delta = Maquette.MuseumHeight(handPosition) - startHandHeight;
            height = Mathf.Clamp(MuseumGeometry.Snap(room.height + delta, MuseumGeometry.GridStep),
                                 MuseumGeometry.MinRoomSize, MuseumGeometry.MaxRoomHeight);
            Maquette.Ghost.Show(room.position, MuseumGeometry.Yaw(room.rotation), room.width, room.length, true,
                                $"Hauteur {height:0.0} m", null, height);
        }

        protected override void EndDrag()
        {
            Maquette.Ghost.Hide();
            if (room == null || Mathf.Approximately(height, room.height)) return;
            Maquette.Api.ResizeRoom(room.id, room.width, room.length, height);
            Maquette.Toast($"« {room.name} » : hauteur {height:0.0} m");
        }
    }
}
