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

        public const string ReservedReason = "Devant le plan / levier : gardez la place libre";
    }
}
