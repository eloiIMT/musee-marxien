using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Grab an artwork on the miniature (spec 2.6), same mechanism as rooms: trigger with the ray, the artwork follows
    /// the point aimed at on the plan (grab offset kept), a ghost shows where it lands.
    ///  - only in the SELECTED room (the one shown in 3D)
    ///  - turning the wrist like a knob rotates it by 15°; within 0.5 m of a wall it snaps to the wall, facing into the room
    ///  - closer than 0.8 m to another artwork = red, release puts it back
    ///  - released over another room = <see cref="MuseumApi.TransferArtwork"/>
    ///  - released while the ray points at the blue bin = leaves the museum for the reserve (<see cref="ArtworkBin"/>)
    /// </summary>
    public class ArtworkHandle : DragHandle
    {
        const float GhostSize = 0.6f;
        public const float ArtworkMargin = 0.8f;

        public string ArtworkId { get; set; }

        ArtworkInstance art;
        Museum museum;
        Room targetRoom;
        Vector3 local;
        float localYaw, yawOffset;
        Vector3 grabOffset;
        bool valid;
        bool toBin;
        string reason;

        protected override void BeginDrag()
        {
            art = Maquette.Api.GetArtwork(ArtworkId);
            museum = Maquette.Api.Data;
            if (art == null) return;
            if (art.roomId != Maquette.SelectedRoomId)
            {
                Maquette.Toast("Sélectionnez d'abord la salle de cette œuvre", true);
                art = null;
                return;
            }
            yawOffset = MuseumGeometry.Yaw(art.localRotation) - HandYaw(Hand.rotation);
            var room = museum.rooms.Find(r => r.id == art.roomId);
            grabOffset = MuseumSpatial.ToMuseum(room, art.localPosition) - GrabPointer();
            grabOffset.y = 0f;
        }

        protected override void Drag(Vector3 handPosition, Quaternion handRotation)
        {
            if (art == null) return;
            var bin = ArtworkBin.Active;
            toBin = bin != null && bin.IsAimedBy(new Ray(RayTransform.position, RayTransform.forward));
            if (bin != null) bin.ShowHint(toBin ? $"Relâcher : ranger « {art.title} »" : null);
            if (toBin)
            {
                Maquette.Ghost.Hide();
                Maquette.ShowReservedZone(null);
                return;
            }

            var p = Pointer() + grabOffset; // follows the ray's point on the plan, not the hand
            var roomId = MuseumGeometry.RoomAt(museum, p);
            targetRoom = roomId != null ? museum.rooms.Find(r => r.id == roomId) : null;
            reason = null;
            if (targetRoom == null)
            {
                reason = "Relâchez dans une salle";
                Maquette.Ghost.Show(p, 0f, GhostSize, GhostSize, false, reason);
                Maquette.ShowReservedZone(null);
                return;
            }

            local = MuseumSpatial.ToRoom(targetRoom, p);
            local.y = art.localPosition.y; // keep its height above the floor
            if (!MuseumGeometry.SnapToWall(targetRoom.width, targetRoom.length, ref local, out localYaw))
                localYaw = MuseumGeometry.SnapAngle(HandYaw(handRotation) + yawOffset);

            foreach (var other in museum.artworks)
            {
                if (other.id == art.id || other.roomId != targetRoom.id) continue;
                var d = other.localPosition - local;
                d.y = 0f;
                if (d.magnitude < ArtworkMargin) { reason = "Trop proche d'une autre œuvre (0,8 m min.)"; break; }
            }
            bool reserved = MuseumStation.IsReserved(targetRoom, museum, local, art.id);
            if (reason == null && reserved) reason = MuseumStation.ReservedReason;
            Maquette.ShowReservedZone(reserved ? targetRoom.id : null); // the no-go area shows only while aimed at
            valid = reason == null;
            string text = reason ?? (targetRoom.id != art.roomId ? $"→ {targetRoom.name}" : art.title);
            var center = MuseumSpatial.ToMuseum(targetRoom, new Vector3(local.x, 0f, local.z));
            Maquette.Ghost.Show(center, MuseumGeometry.Yaw(targetRoom.rotation) + localYaw, GhostSize, GhostSize, valid, text);
        }

        protected override void EndDrag()
        {
            bool store = toBin;
            Cleanup();
            if (art == null) return;
            if (store)
            {
                ArtworkBin.Active?.StoreFromMuseum(art.id);
                return;
            }
            if (targetRoom == null || !valid) { Maquette.Toast((reason ?? "Placement refusé") + " : œuvre remise en place", true); return; }
            var api = Maquette.Api;
            var rotation = Quaternion.Euler(0f, localYaw, 0f);
            if (targetRoom.id != art.roomId)
            {
                api.TransferArtwork(art.id, targetRoom.id, false);
                Maquette.SelectRoom(targetRoom.id); // follow the artwork so it stays visible and editable
                api.SetArtworkTransform(art.id, local, rotation, art.localScale);
                Maquette.Toast($"« {art.title} » transférée dans « {targetRoom.name} »");
            }
            else
            {
                api.SetArtworkTransform(art.id, local, rotation, art.localScale);
                Maquette.Toast($"« {art.title} » placée");
            }
        }

        protected override void CancelDrag() => Cleanup();

        void Cleanup()
        {
            Maquette.Ghost.Hide();
            Maquette.ShowReservedZone(null);
            toBin = false;
            if (ArtworkBin.Active != null) ArtworkBin.Active.ShowHint(null);
        }

        float HandYaw(Quaternion handRotation) => Maquette.HandTwist(handRotation);
    }
}
