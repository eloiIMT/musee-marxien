using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Floor corner handle: width and length, with the OPPOSITE corner fixed (like drawing tools).
    /// Sizes snap to 0.5 m, minimum 2 m. Artworks keep their place in the museum; those that would end up
    /// outside the walls are shown in red but not moved (the backend does not move them either).
    /// </summary>
    public class ResizeHandle : DragHandle
    {
        public int SignX { get; private set; } = 1;
        public int SignZ { get; private set; } = 1;

        Room room;
        Museum museum;
        RoomView view;
        Vector3 anchorLocal, centerLocal, grabOffset;
        float width, length;
        bool valid;
        readonly List<string> outside = new();

        public void SetCorner(int sx, int sz) { SignX = sx; SignZ = sz; }

        protected override void BeginDrag()
        {
            room = Maquette.Api.GetRoom(Maquette.SelectedRoomId);
            museum = Maquette.Api.Data;
            view = Maquette.View.GetRoomView(Maquette.SelectedRoomId);
            if (room == null) return;
            anchorLocal = new Vector3(-SignX * room.width / 2f, 0f, -SignZ * room.length / 2f);
            // The corner follows the ray's point on the plan, with the offset measured at grab time.
            grabOffset = MuseumSpatial.ToMuseum(room, -anchorLocal) - GrabPointer();
            grabOffset.y = 0f;
        }

        protected override void Drag(Vector3 handPosition, Quaternion handRotation)
        {
            if (room == null) return;
            var local = MuseumSpatial.ToRoom(room, Pointer() + grabOffset);
            // Distance from the fixed corner along each axis, on the handle's side only.
            width = Mathf.Max(MuseumGeometry.MinRoomSize, MuseumGeometry.Snap(SignX * (local.x - anchorLocal.x), MuseumGeometry.GridStep));
            length = Mathf.Max(MuseumGeometry.MinRoomSize, MuseumGeometry.Snap(SignZ * (local.z - anchorLocal.z), MuseumGeometry.GridStep));
            centerLocal = anchorLocal + new Vector3(SignX * width / 2f, 0f, SignZ * length / 2f);
            var center = MuseumSpatial.ToMuseum(room, centerLocal);
            float yaw = MuseumGeometry.Yaw(room.rotation);
            valid = !MuseumGeometry.OverlapsAny(museum, room.id, center, yaw, width, length);

            outside.Clear();
            foreach (var a in museum.artworks)
                if (a.roomId == room.id && !MuseumGeometry.IsInside(width, length, a.localPosition - centerLocal))
                    outside.Add(a.id);
            if (view != null) view.SetArtworkWarnings(outside);

            var text = $"{width:0.0} × {length:0.0} m";
            if (!valid) text += "\nTrop proche d'une autre salle";
            else if (outside.Count > 0) text += $"\n{outside.Count} œuvre(s) hors des murs";
            Maquette.Ghost.Show(center, yaw, width, length, valid, text);
        }

        protected override void EndDrag()
        {
            Cleanup();
            if (room == null) return;
            if (!valid) { Maquette.Toast("Redimensionnement refusé : trop proche d'une autre salle", true); return; }
            if (Mathf.Approximately(width, room.width) && Mathf.Approximately(length, room.length)) return;

            var api = Maquette.Api;
            var artworks = api.GetArtworksInRoom(room.id);
            api.ResizeRoom(room.id, width, length, room.height);
            api.SetRoomTransform(room.id, MuseumSpatial.ToMuseum(room, centerLocal), room.rotation);
            // The room origin moved by centerLocal: shift artworks back so they stay where they were in the museum.
            foreach (var a in artworks)
                api.SetArtworkTransform(a.id, a.localPosition - centerLocal, a.localRotation, a.localScale);
            Maquette.Toast(outside.Count > 0
                ? $"{width:0.0} × {length:0.0} m — {outside.Count} œuvre(s) hors des murs"
                : $"« {room.name} » : {width:0.0} × {length:0.0} m", outside.Count > 0);
            // Afterwards RoomView keeps artworks outside the walls in red by itself.
        }

        protected override void CancelDrag() => Cleanup();

        void Cleanup()
        {
            Maquette.Ghost.Hide();
            if (view != null) view.SetArtworkWarnings(null);
        }
    }
}
