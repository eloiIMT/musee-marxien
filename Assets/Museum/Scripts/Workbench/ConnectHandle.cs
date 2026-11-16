using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Wall handle: pull a cable from the selected room and release it over another room to connect them.
    /// The room under the hand lights up green while the cable is held.
    /// </summary>
    public class ConnectHandle : DragHandle
    {
        Room room;
        Museum museum;
        string target;
        LineRenderer cable;

        public override void Init(Maquette maquette, Color color)
        {
            base.Init(maquette, color);
            var go = new GameObject("Pulled Cable");
            go.transform.SetParent(transform, false);
            cable = go.AddComponent<LineRenderer>();
            cable.useWorldSpace = true;
            cable.positionCount = 2;
            cable.widthMultiplier = 0.004f;
            cable.sharedMaterial = maquette.HandleMaterial;
            Tint.Set(cable, color);
            cable.enabled = false;
        }

        protected override void BeginDrag()
        {
            room = Maquette.Api.GetRoom(Maquette.SelectedRoomId);
            museum = Maquette.Api.Data;
            cable.enabled = room != null;
            if (room != null) GrabPointer();
        }

        Vector3 releasePoint;

        protected override void Drag(Vector3 handPosition, Quaternion handRotation)
        {
            if (room == null) return;
            // The cable end sits where the ray meets the plan, so it is dropped exactly on the aimed room.
            releasePoint = Pointer();
            cable.SetPosition(0, transform.position);
            cable.SetPosition(1, Maquette.MuseumToWorld(releasePoint + Vector3.up * 0.3f));
            target = MuseumGeometry.RoomAt(museum, releasePoint, room.id);
            Maquette.SetTargetRoom(target);
        }

        protected override void EndDrag()
        {
            Cleanup();
            if (room == null) return;
            if (target == null) { Maquette.Toast("Relâchez le câble au-dessus d'une autre salle", true); return; }
            var other = museum.rooms.Find(r => r.id == target);
            if (room.connections.Contains(target)) { Maquette.Toast($"« {room.name} » et « {other.name} » sont déjà reliées", true); return; }
            // Spec 2.5: free attachment along the walls — from the pulled wall to the wall point nearest the release.
            // The gizmo is a child of the room view, so the handle's local position is room-local.
            var localA = MuseumGeometry.ClosestPointOnPerimeter(room.width, room.length, transform.localPosition);
            var localB = MuseumGeometry.ClosestPointOnPerimeter(other.width, other.length, MuseumSpatial.ToRoom(other, releasePoint));
            ConnectionAnchors.Set(room.id, localA, other.id, localB);
            Maquette.Api.ConnectRooms(room.id, target);
            Maquette.Toast($"« {room.name} » ↔ « {other.name} » reliées");
        }

        protected override void CancelDrag() => Cleanup();

        void Cleanup()
        {
            cable.enabled = false;
            Maquette.SetTargetRoom(null);
        }
    }
}
