using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Grab an artwork on the miniature (spec 2.6), same mechanism as rooms: trigger with the ray, the artwork's 3D model
    /// follows the point aimed at on the plan (grab offset kept), already snapped and turned as it would land (red when
    /// refused, blue when going to the bin); an outline stays where it was. The model goes back on refusal / cancel.
    ///  - only in the SELECTED room (the one shown in 3D)
    ///  - turning the wrist like a knob rotates it by 15°; within 0.5 m of a wall it snaps to the wall, facing into the room
    ///  - closer than 0.8 m to another artwork = red, release puts it back
    ///  - released over another room = <see cref="MuseumApi.TransferArtwork"/>
    ///  - released while the ray points at the blue bin, or on the plan outside every room = leaves the museum for the
    ///    reserve (<see cref="ArtworkBin"/>)
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
        // The model as it was, put back when the drag does not move it (the view re-syncs it otherwise).
        Vector3 startPosition;
        Quaternion startRotation;
        Room startRoom;
        Renderer[] renderers;

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
            startRoom = museum.rooms.Find(r => r.id == art.roomId);
            grabOffset = MuseumSpatial.ToMuseum(startRoom, art.localPosition) - GrabPointer();
            grabOffset.y = 0f;
            startPosition = transform.localPosition;
            startRotation = transform.localRotation;
            renderers = GetComponentsInChildren<Renderer>(true);
            // Where it was: neutral outline, until released.
            var origin = MuseumSpatial.ToMuseum(startRoom, new Vector3(art.localPosition.x, 0f, art.localPosition.z));
            Maquette.Held.Show(origin, MuseumGeometry.Yaw(startRoom.rotation) + MuseumGeometry.Yaw(art.localRotation),
                               GhostSize, GhostSize, true, colorOverride: OriginColor);
        }

        static readonly Color OriginColor = new Color(0.75f, 0.75f, 0.8f);
        static readonly Color RefusedColor = new Color(1f, 0.2f, 0.15f);

        protected override void Drag(Vector3 handPosition, Quaternion handRotation)
        {
            if (art == null) return;
            var bin = ArtworkBin.Active;
            toBin = bin != null && bin.IsAimedBy(new Ray(RayTransform.position, RayTransform.forward));
            if (bin != null) bin.ShowHint(toBin ? $"Relâcher : ranger « {art.title} »" : null);
            if (toBin)
            {
                Maquette.SetDropTarget(null);
                Maquette.Ghost.Hide();
                Maquette.ShowReservedZone(null);
                SetModelVisible(false); // it is going into the bin, not onto the plan
                return;
            }
            SetModelVisible(true);

            var p = Pointer() + grabOffset; // follows the ray's point on the plan, not the hand
            var roomId = MuseumGeometry.RoomAt(museum, p);
            targetRoom = roomId != null ? museum.rooms.Find(r => r.id == roomId) : null;
            Maquette.SetDropTarget(targetRoom?.id); // the room it goes to shows in 3D
            reason = null;
            if (targetRoom == null)
            {
                Maquette.ShowReservedZone(null);
                float yaw = MuseumGeometry.Yaw(startRoom.rotation) + MuseumGeometry.SnapAngle(HandYaw(handRotation) + yawOffset);
                // On the plan but outside every room: released there, it leaves the museum for the bin.
                toBin = bin != null && Maquette.IsOverBoard(Maquette.MuseumToWorld(p));
                string text;
                if (toBin)
                {
                    text = $"Relâcher : ranger « {art.title} » dans le bac";
                    bin.ShowHint(text);
                }
                else reason = text = "Relâchez dans une salle";
                MoveModel(p + Vector3.up * art.localPosition.y, yaw, toBin ? Maquette.HeldColor : RefusedColor);
                Maquette.Ghost.Show(p, 0f, GhostSize, GhostSize, toBin, text, colorOverride: toBin ? Maquette.HeldColor : null, outline: false);
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
            string label = reason ?? (targetRoom.id != art.roomId ? $"→ {targetRoom.name}" : art.title);
            float landYaw = MuseumGeometry.Yaw(targetRoom.rotation) + localYaw;
            MoveModel(MuseumSpatial.ToMuseum(targetRoom, local), landYaw, valid ? null : RefusedColor);
            var center = MuseumSpatial.ToMuseum(targetRoom, new Vector3(local.x, 0f, local.z));
            Maquette.Ghost.Show(center, landYaw, GhostSize, GhostSize, valid, label, outline: false); // label only
        }

        /// <summary>Put the model at a museum point / yaw (it stays parented to its room); tint null = its own colours.</summary>
        void MoveModel(Vector3 museumPoint, float museumYaw, Color? tint)
        {
            transform.SetPositionAndRotation(Maquette.MuseumToWorld(museumPoint),
                                             Maquette.transform.rotation * Quaternion.Euler(0f, museumYaw, 0f));
            foreach (var r in renderers)
                if (r != null) { if (tint.HasValue) Tint.Set(r, tint.Value); else Tint.Clear(r); }
        }

        void SetModelVisible(bool visible)
        {
            foreach (var r in renderers) if (r != null) r.enabled = visible;
        }

        /// <summary>Model back where it was, with its own colours (and the red of an artwork outside its walls).</summary>
        void RestoreModel()
        {
            if (renderers == null) return;
            SetModelVisible(true);
            transform.SetLocalPositionAndRotation(startPosition, startRotation);
            var view = Maquette.View.GetRoomView(art?.roomId);
            if (view != null) view.SetArtworkWarnings(null);
            else foreach (var r in renderers) Tint.Clear(r);
            renderers = null;
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
            // Put back first: a successful placement then moves it through the API (view re-sync, or a new object in
            // another room); a refused one leaves it where it was.
            RestoreModel();
            Maquette.Held.Hide();
            Maquette.Ghost.Hide();
            Maquette.ShowReservedZone(null);
            Maquette.SetDropTarget(null);
            toBin = false;
            if (ArtworkBin.Active != null) ArtworkBin.Active.ShowHint(null);
        }

        float HandYaw(Quaternion handRotation) => Maquette.HandTwist(handRotation);
    }
}
