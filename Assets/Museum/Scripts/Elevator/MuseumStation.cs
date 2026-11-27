using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Where a room's visitor station goes, in room-local coordinates (museum metres): the wall plan
    /// (<see cref="RoomPlan.ChooseWall"/>), the lever beside it and the elevator platform at the lever's foot
    /// (<see cref="ElevatorRide"/>). Shared by the visitor (to build it) and the organizer's miniature (to show it and keep
    /// artworks out of the way, <see cref="IsReserved"/>).
    /// </summary>
    public static class MuseumStation
    {
        public const float PlanHalfWidth = 0.475f; // RoomPlan.BoardWidth / 2
        public const float DefaultRadius = 0.55f;  // ElevatorShaft.radius
        /// <summary>Depth in front of the plan wall kept free of artworks (metres).</summary>
        public const float ClearDepth = 1.5f;
        /// <summary>Extra free width beyond the platform, on each side (metres).</summary>
        public const float ClearMargin = 0.3f;

        public struct Layout
        {
            /// <summary>Middle of the plan wall's inner face, floor level.</summary>
            public Vector3 wallCentre;
            /// <summary>Outward normal of the plan wall (towards the wall).</summary>
            public Vector3 normal;
            /// <summary>Along the wall, towards the plan's right (seen from inside the room).</summary>
            public Vector3 along;
            public float wallLength;
            /// <summary>Distance from the plan centre to the lever, along the wall.</summary>
            public float offset;
            /// <summary>+1: lever on the plan's right, -1: on its left.</summary>
            public int side;
            public float radius;
            /// <summary>Platform centre (room-local x, z).</summary>
            public Vector2 spot;

            /// <summary>Free width along the wall from the plan centre, away from the lever (past the plan's edge).</summary>
            public float ClearPlanSide => PlanHalfWidth + ClearMargin;
            /// <summary>Free width along the wall from the plan centre, towards the lever (past the platform).</summary>
            public float ClearLeverSide => offset + radius + ClearMargin;
        }

        /// <summary>The lever is always on the plan's right, whatever the artworks, so it never moves when they do.</summary>
        public const int LeverSide = 1;

        /// <param name="ignoreArtworkId">Artwork left out of the plan wall choice (e.g. the one being moved).</param>
        public static Layout Compute(Room room, Museum museum, float radius = DefaultRadius, string ignoreArtworkId = null)
        {
            var (centre, normal, wallLength) = RoomPlan.ChooseWallLocal(room, museum, ignoreArtworkId);
            var along = Vector3.Cross(Vector3.up, normal);
            // lever beside the plan, but the platform must stay off the side walls
            float offset = Mathf.Min(PlanHalfWidth + 0.3f, wallLength / 2f - radius - 0.15f);
            var spot = centre + along * (LeverSide * offset) - normal * (radius + 0.08f);
            return new Layout
            {
                wallCentre = centre, normal = normal, along = along, wallLength = wallLength,
                offset = offset, side = LeverSide, radius = radius, spot = new Vector2(spot.x, spot.z),
            };
        }

        /// <summary>Is a room-local point in front of the plan / lever / platform, where artworks may not go?</summary>
        public static bool IsReserved(in Layout s, Vector3 local)
        {
            var d = local - s.wallCentre;
            float toWall = -Vector3.Dot(d, s.normal); // > 0 inside the room
            float a = Vector3.Dot(d, s.along) * s.side; // > 0 towards the lever
            return toWall < ClearDepth && a > -s.ClearPlanSide && a < s.ClearLeverSide;
        }

        /// <summary>Artwork placement check: is <paramref name="local"/> (in <paramref name="room"/>) in front of its station?</summary>
        public static bool IsReserved(Room room, Museum museum, Vector3 local, string ignoreArtworkId = null) =>
            IsReserved(Compute(room, museum, DefaultRadius, ignoreArtworkId), local);

        /// <summary>
        /// Room-local yaw turning an artwork at <paramref name="local"/> to face the room's wall plan (artworks face their
        /// local +Z): its front, and its label, are then seen by the visitor arriving by the elevator in front of the plan.
        /// </summary>
        public static float FacePlanYaw(Room room, Museum museum, Vector3 local)
        {
            var d = Compute(room, museum).wallCentre - local;
            d.y = 0f;
            return d.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Turn every free-standing artwork (not against a wall, <see cref="MuseumGeometry.SnapToWall"/>) to face its
        /// room's wall plan, in <see cref="MuseumGeometry.AngleStep"/> steps. Wall artworks keep facing into the room.
        /// Returns how many artworks turned.
        /// </summary>
        public static int TurnArtworksToPlan(MuseumApi api)
        {
            var museum = api.Data;
            int turned = 0;
            foreach (var art in museum.artworks)
            {
                var room = museum.rooms.Find(r => r.id == art.roomId);
                if (room == null) continue;
                var probe = art.localPosition;
                if (MuseumGeometry.SnapToWall(room.width, room.length, ref probe, out _)) continue;
                float yaw = MuseumGeometry.SnapAngle(FacePlanYaw(room, museum, art.localPosition));
                if (Mathf.Abs(Mathf.DeltaAngle(MuseumGeometry.Yaw(art.localRotation), yaw)) < 0.5f) continue;
                api.SetArtworkTransform(art.id, art.localPosition, Quaternion.Euler(0f, yaw, 0f), art.localScale);
                turned++;
            }
            return turned;
        }

        /// <summary>Depth in front of a door kept free of artworks (the arch, the doorway, where visitors arrive).</summary>
        public const float DoorClearDepth = 1.5f;
        public const string DoorReason = "Devant une porte : gardez le passage libre";

        /// <summary>Is a room-local point in front of one of the room's doors (<see cref="ConnectionLayout"/>)?</summary>
        public static bool InFrontOfDoor(Room room, Museum museum, Vector3 local)
        {
            foreach (var (a, b) in ConnectionLayout.Compute(museum).Values)
                foreach (var door in new[] { a, b })
                {
                    if (door.RoomId != room.id) continue;
                    var d = local - door.Local;
                    float toWall = -Vector3.Dot(d, door.Normal);                       // > 0 inside the room
                    float along = Vector3.Dot(d, Vector3.Cross(Vector3.up, door.Normal));
                    if (toWall < DoorClearDepth && Mathf.Abs(along) < door.Width / 2f + ClearMargin) return true;
                }
            return false;
        }

        public const string ReservedReason = "Devant le plan / levier : gardez la place libre";
    }
}
