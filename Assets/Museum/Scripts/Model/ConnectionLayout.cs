using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>One end of a connection: a door on one wall of a room. Room-local, y = 0.</summary>
    public readonly struct Door
    {
        public readonly string RoomId, OtherRoomId;
        /// <summary>Middle of the door on the wall line (room-local, y = 0).</summary>
        public readonly Vector3 Local;
        /// <summary>Outward wall normal (room-local): ±X or ±Z.</summary>
        public readonly Vector3 Normal;
        /// <summary>Door width along the wall (museum metres).</summary>
        public readonly float Width;

        public Door(string roomId, string otherRoomId, Vector3 local, Vector3 normal, float width)
        { RoomId = roomId; OtherRoomId = otherRoomId; Local = local; Normal = normal; Width = width; }

        /// <summary>Along the X axis of the room (north / south wall) or along Z (east / west wall)?</summary>
        public bool AlongX => Mathf.Abs(Normal.z) > 0.5f;
    }

    /// <summary>
    /// Where the connections between rooms are drawn: each connection gets a door on the wall of each room that
    /// faces the other room — the wall across the axis where the two rooms are apart (side by side: the side walls,
    /// one above the other: the top / bottom walls). When the two rooms share a span along that wall, both doors sit
    /// in the middle of it, face to face, so the line between them is straight and short. Several doors on the same
    /// wall keep their place when they fit, otherwise they are pushed apart (in the order of the rooms they lead to)
    /// so they never overlap. Doors stay away from the corners. Pure computation from the model (nothing stored):
    /// unit-testable, identical for every view, recomputed after any change.
    /// </summary>
    public static class ConnectionLayout
    {
        public const float DoorWidth = 1.2f;
        const float CornerClearance = 0.4f; // museum metres kept free at each wall end

        /// <summary>Doors of every connection, by <see cref="MuseumView.ConnectionKey"/>: (door in room a, door in room b).</summary>
        public static Dictionary<string, (Door a, Door b)> Compute(Museum museum)
        {
            var result = new Dictionary<string, (Door a, Door b)>();
            if (museum == null) return result;
            var byId = new Dictionary<string, Room>();
            foreach (var r in museum.rooms) byId[r.id] = r;

            // 1. Which wall of each room every connection leaves from.
            var walls = new Dictionary<(string room, int wall), List<(string other, float along)>>();
            var pairs = new List<(string key, Room a, Room b)>();
            var seen = new HashSet<string>();
            foreach (var a in museum.rooms)
            {
                foreach (var otherId in a.connections)
                {
                    if (!byId.TryGetValue(otherId, out var b) || a.id == otherId) continue;
                    var key = MuseumView.ConnectionKey(a.id, otherId);
                    if (!seen.Add(key)) continue;
                    pairs.Add((key, a, b));
                    AddEnd(walls, a, b);
                    AddEnd(walls, b, a);
                }
            }

            // 2. Door positions along each wall.
            var doors = new Dictionary<(string room, string other), Door>();
            foreach (var pair in walls)
            {
                var room = byId[pair.Key.room];
                int wall = pair.Key.wall;
                var list = pair.Value;
                list.Sort((p, q) => p.along.CompareTo(q.along));
                float usable = Mathf.Max(WallLength(room, wall) - 2f * CornerClearance, 0.1f);
                int n = list.Count;
                float width = Mathf.Min(DoorWidth, usable / n * 0.75f);
                var positions = Spread(list.ConvertAll(e => e.along), usable / 2f - width / 2f, width + 0.2f);
                for (int i = 0; i < n; i++)
                    doors[(room.id, list[i].other)] = new Door(room.id, list[i].other, WallPoint(room, wall, positions[i]), WallNormal(wall), width);
            }

            foreach (var (key, a, b) in pairs)
                result[key] = (doors[(a.id, b.id)], doors[(b.id, a.id)]);
            return result;
        }

        /// <summary>Museum-space middle of a door, at floor level.</summary>
        public static Vector3 ToMuseum(Room room, Door door) => MuseumSpatial.ToMuseum(room, door.Local);

        static void AddEnd(Dictionary<(string, int), List<(string, float)>> walls, Room room, Room other)
        {
            var (wall, along) = FacingWall(room, other);
            var k = (room.id, wall);
            if (!walls.TryGetValue(k, out var list)) walls[k] = list = new List<(string, float)>();
            list.Add((other.id, along));
        }

        /// <summary>
        /// Wall of <paramref name="room"/> facing <paramref name="other"/> (0 = +Z north, 1 = -Z south, 2 = +X east,
        /// 3 = -X west) and the ideal door position along it (room-local): the middle of the span the two rooms share
        /// along that wall, or in front of the other room's centre when they share none.
        /// </summary>
        public static (int wall, float along) FacingWall(Room room, Room other)
        {
            // The other room's footprint in this room's frame.
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var c in MuseumGeometry.Corners(other.position, MuseumGeometry.Yaw(other.rotation), other.width, other.length))
            {
                var p = MuseumSpatial.ToRoom(room, new Vector3(c.x, 0f, c.y));
                x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z);
            }
            float hw = room.width / 2f, hl = room.length / 2f;
            float gapX = Mathf.Max(x0 - hw, -hw - x1), gapZ = Mathf.Max(z0 - hl, -hl - z1);
            var centre = MuseumSpatial.ToRoom(room, other.position);
            if (gapX > gapZ)
            {
                // Apart along X: east / west wall, door in the middle of the shared Z span.
                float lo = Mathf.Max(z0, -hl), hi = Mathf.Min(z1, hl);
                return (centre.x >= 0 ? 2 : 3, lo <= hi ? (lo + hi) / 2f : centre.z);
            }
            float a = Mathf.Max(x0, -hw), b = Mathf.Min(x1, hw);
            return (centre.z >= 0 ? 0 : 1, a <= b ? (a + b) / 2f : centre.x);
        }

        /// <summary>
        /// Final positions of doors along one wall, from their sorted ideal positions: clamped to ±<paramref name="max"/>,
        /// then pushed apart to <paramref name="gap"/>; evenly spread if they cannot all fit.
        /// </summary>
        static float[] Spread(List<float> ideal, float max, float gap)
        {
            int n = ideal.Count;
            var p = new float[n];
            if (max < 0f) max = 0f;
            if (n > 1 && gap * (n - 1) > 2f * max)
            {
                for (int i = 0; i < n; i++) p[i] = -max + 2f * max * i / (n - 1);
                return p;
            }
            for (int i = 0; i < n; i++) p[i] = Mathf.Clamp(ideal[i], -max, max);
            for (int i = 1; i < n; i++) p[i] = Mathf.Max(p[i], p[i - 1] + gap);       // push right
            if (n > 0 && p[n - 1] > max) p[n - 1] = max;
            for (int i = n - 2; i >= 0; i--) p[i] = Mathf.Min(p[i], p[i + 1] - gap);   // push back left
            return p;
        }

        static float WallLength(Room room, int wall) => wall <= 1 ? room.width : room.length;

        static Vector3 WallNormal(int wall) => wall switch
        {
            0 => Vector3.forward,
            1 => Vector3.back,
            2 => Vector3.right,
            _ => Vector3.left,
        };

        static Vector3 WallPoint(Room room, int wall, float along) => wall switch
        {
            0 => new Vector3(along, 0, room.length / 2f),
            1 => new Vector3(along, 0, -room.length / 2f),
            2 => new Vector3(room.width / 2f, 0, along),
            _ => new Vector3(-room.width / 2f, 0, along),
        };
    }
}
