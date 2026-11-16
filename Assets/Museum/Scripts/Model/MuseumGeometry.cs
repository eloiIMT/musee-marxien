using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Pure geometry helpers in museum coordinates (metres, XZ ground plane). No scene access: unit-testable.
    /// </summary>
    public static class MuseumGeometry
    {
        public const float GridStep = 0.5f;
        public const float AngleStep = 15f;
        public const float RoomMargin = 1f;      // minimum distance between walls of two rooms
        public const float MinRoomSize = 2f;
        public const float MaxRoomHeight = 8f;

        public static float Snap(float value, float step) => Mathf.Round(value / step) * step;

        public static Vector3 SnapXZ(Vector3 p, float step = GridStep) => new Vector3(Snap(p.x, step), 0f, Snap(p.z, step));

        public static float SnapAngle(float degrees, float step = AngleStep) => Mathf.Repeat(Snap(degrees, step), 360f);

        public static float Yaw(Quaternion q) => q.eulerAngles.y;

        /// <summary>Four XZ corners of a room footprint, counter-clockwise.</summary>
        public static Vector2[] Corners(Vector3 center, float yaw, float width, float length)
        {
            var r = Quaternion.Euler(0f, yaw, 0f);
            float hw = width / 2f, hl = length / 2f;
            return new[]
            {
                XZ(center + r * new Vector3(-hw, 0, -hl)),
                XZ(center + r * new Vector3(hw, 0, -hl)),
                XZ(center + r * new Vector3(hw, 0, hl)),
                XZ(center + r * new Vector3(-hw, 0, hl)),
            };
        }

        /// <summary>Separating-axis test between two convex quads. Touching edges do not count as overlap.</summary>
        public static bool Overlaps(Vector2[] a, Vector2[] b)
        {
            return !HasSeparatingAxis(a, a, b) && !HasSeparatingAxis(b, a, b);
        }

        /// <summary>True if a footprint, inflated by <paramref name="margin"/> on each side, overlaps another room.</summary>
        public static bool OverlapsRoom(Vector3 center, float yaw, float width, float length, Room other, float margin = RoomMargin)
        {
            var a = Corners(center, yaw, width + 2f * margin, length + 2f * margin);
            var b = Corners(other.position, Yaw(other.rotation), other.width, other.length);
            return Overlaps(a, b);
        }

        public static bool OverlapsAny(Museum museum, string ignoreRoomId, Vector3 center, float yaw, float width, float length, float margin = RoomMargin)
        {
            foreach (var room in museum.rooms)
                if (room.id != ignoreRoomId && OverlapsRoom(center, yaw, width, length, room, margin))
                    return true;
            return false;
        }

        /// <summary>Is a room-local point inside the room footprint?</summary>
        public static bool IsInside(float width, float length, Vector3 local, float tolerance = 0f)
        {
            return Mathf.Abs(local.x) <= width / 2f + tolerance && Mathf.Abs(local.z) <= length / 2f + tolerance;
        }

        /// <summary>Id of the room whose footprint contains a museum-space point, or null.</summary>
        public static string RoomAt(Museum museum, Vector3 museumPoint, string ignoreRoomId = null)
        {
            foreach (var room in museum.rooms)
            {
                if (room.id == ignoreRoomId) continue;
                if (IsInside(room.width, room.length, MuseumSpatial.ToRoom(room, museumPoint))) return room.id;
            }
            return null;
        }

        /// <summary>Closest point on the walls (footprint perimeter) to a room-local point, y = 0.</summary>
        public static Vector3 ClosestPointOnPerimeter(float width, float length, Vector3 local)
        {
            float hw = width / 2f, hl = length / 2f;
            float x = Mathf.Clamp(local.x, -hw, hw), z = Mathf.Clamp(local.z, -hl, hl);
            float toE = hw - x, toW = x + hw, toN = hl - z, toS = z + hl;
            float min = Mathf.Min(Mathf.Min(toE, toW), Mathf.Min(toN, toS));
            if (min == toE) x = hw;
            else if (min == toW) x = -hw;
            else if (min == toN) z = hl;
            else z = -hl;
            return new Vector3(x, 0f, z);
        }

        /// <summary>
        /// Snap a room-local artwork position to the nearest wall if closer than <paramref name="threshold"/>,
        /// facing into the room. Returns false (unchanged) when no wall is close enough.
        /// </summary>
        public static bool SnapToWall(float width, float length, ref Vector3 local, out float yaw, float threshold = 0.5f, float inset = 0.15f)
        {
            float hw = width / 2f, hl = length / 2f;
            float toE = hw - local.x, toW = local.x + hw, toN = hl - local.z, toS = local.z + hl;
            float min = Mathf.Min(Mathf.Min(toE, toW), Mathf.Min(toN, toS));
            yaw = 0f;
            if (min > threshold) return false;
            if (min == toN) { local.z = hl - inset; yaw = 180f; }
            else if (min == toS) { local.z = -hl + inset; yaw = 0f; }
            else if (min == toE) { local.x = hw - inset; yaw = 270f; }
            else { local.x = -hw + inset; yaw = 90f; }
            return true;
        }

        /// <summary>Middle of each wall (N, S, E, W) in museum coordinates.</summary>
        public static Vector3[] WallMidpoints(Room room)
        {
            float hw = room.width / 2f, hl = room.length / 2f;
            return new[]
            {
                MuseumSpatial.ToMuseum(room, new Vector3(0, 0, hl)),
                MuseumSpatial.ToMuseum(room, new Vector3(0, 0, -hl)),
                MuseumSpatial.ToMuseum(room, new Vector3(hw, 0, 0)),
                MuseumSpatial.ToMuseum(room, new Vector3(-hw, 0, 0)),
            };
        }

        /// <summary>Pair of wall midpoints (one per room) closest to each other: where a connection is drawn.</summary>
        public static (Vector3 a, Vector3 b) ClosestWallMidpoints(Room a, Room b)
        {
            var pa = WallMidpoints(a);
            var pb = WallMidpoints(b);
            var best = (pa[0], pb[0]);
            float bestDistance = float.MaxValue;
            foreach (var x in pa)
                foreach (var y in pb)
                {
                    float d = (x - y).sqrMagnitude;
                    if (d < bestDistance) { bestDistance = d; best = (x, y); }
                }
            return best;
        }

        /// <summary>First free name "Salle A", "Salle B"... then "Salle 27"...</summary>
        public static string NextRoomName(Museum museum)
        {
            var used = new HashSet<string>();
            foreach (var r in museum.rooms) used.Add(r.name);
            for (char c = 'A'; c <= 'Z'; c++)
                if (!used.Contains("Salle " + c)) return "Salle " + c;
            for (int i = 27; ; i++)
                if (!used.Contains("Salle " + i)) return "Salle " + i;
        }

        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        static bool HasSeparatingAxis(Vector2[] edgesOf, Vector2[] a, Vector2[] b)
        {
            for (int i = 0; i < edgesOf.Length; i++)
            {
                var edge = edgesOf[(i + 1) % edgesOf.Length] - edgesOf[i];
                var axis = new Vector2(-edge.y, edge.x);
                Project(a, axis, out float minA, out float maxA);
                Project(b, axis, out float minB, out float maxB);
                if (maxA <= minB + 1e-4f || maxB <= minA + 1e-4f) return true;
            }
            return false;
        }

        static void Project(Vector2[] points, Vector2 axis, out float min, out float max)
        {
            min = max = Vector2.Dot(points[0], axis);
            for (int i = 1; i < points.Length; i++)
            {
                float d = Vector2.Dot(points[i], axis);
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }
    }
}
