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
    /// in the middle of it, face to face, so the line between them is straight and short (rooms sharing a wall: the
    /// two doors back to back, one passage). Both doors of a connection always have the same width. Several doors on
    /// one wall keep the order of the rooms they lead to and never overlap (moved along the shared span, or made
    /// narrower when the wall is too short). Doors stay away from the corners. Pure computation from the model (nothing stored):
    /// unit-testable, identical for every view, recomputed after any change.
    /// </summary>
    public static class ConnectionLayout
    {
        public const float DoorWidth = 2.8f; // slot of a life-size arched door (DoorPortal), also drawn on the plans
        const float CornerClearance = 0.4f; // museum metres kept free at each wall end

        /// <summary>Doors of every connection, by <see cref="MuseumView.ConnectionKey"/>: (door in room a, door in room b).</summary>
        public static Dictionary<string, (Door a, Door b)> Compute(Museum museum)
        {
            var result = new Dictionary<string, (Door a, Door b)>();
            if (museum == null) return result;
            var byId = new Dictionary<string, Room>();
            foreach (var r in museum.rooms) byId[r.id] = r;

            // 1. Which wall of each room every connection leaves from, and how many doors each wall gets.
            var conns = new List<(string key, Room a, Room b, End ea, End eb)>();
            var count = new Dictionary<(string room, int wall), int>();
            var seen = new HashSet<string>();
            foreach (var a in museum.rooms)
            {
                foreach (var otherId in a.connections)
                {
                    if (!byId.TryGetValue(otherId, out var b) || a.id == otherId) continue;
                    var key = MuseumView.ConnectionKey(a.id, otherId);
                    if (!seen.Add(key)) continue;
                    var ea = FacingEnd(a, b);
                    var eb = FacingEnd(b, a);
                    conns.Add((key, a, b, ea, eb));
                    count[(a.id, ea.wall)] = count.TryGetValue((a.id, ea.wall), out var na) ? na + 1 : 1;
                    count[(b.id, eb.wall)] = count.TryGetValue((b.id, eb.wall), out var nb) ? nb + 1 : 1;
                }
            }

            // 2. Both doors of a connection together: same width and, when the rooms share a span along their walls,
            // the same place on it (face to face); never overlapping another door of either wall. Placed one connection
            // at a time, by position along the wall (doors keep the rooms' order); tried with the face-to-face
            // connections first and last, keeping the arrangement with the widest doors (then closest to ideal).
            int Order(bool faceToFaceFirst, (string key, Room a, Room b, End ea, End eb) p, (string key, Room a, Room b, End ea, End eb) q)
            {
                int c = Tight(p.ea, p.eb).CompareTo(Tight(q.ea, q.eb)) * (faceToFaceFirst ? -1 : 1);
                if (c != 0) return c;
                c = p.ea.ideal.CompareTo(q.ea.ideal);
                return c != 0 ? c : string.CompareOrdinal(p.key, q.key);
            }
            Dictionary<string, (Door a, Door b)> best = null;
            float bestWidth = -1f, bestDeviation = float.MaxValue;
            foreach (bool first in new[] { true, false })
            {
                var order = new List<(string key, Room a, Room b, End ea, End eb)>(conns);
                order.Sort((p, q) => Order(first, p, q));
                var (layout, minWidth, deviation) = Place(order, count);
                if (minWidth > bestWidth + 1e-3f || (Mathf.Abs(minWidth - bestWidth) <= 1e-3f && deviation < bestDeviation - 1e-3f))
                {
                    best = layout;
                    bestWidth = minWidth;
                    bestDeviation = deviation;
                }
            }
            foreach (var pair in best) result[pair.Key] = pair.Value;
            return result;
        }

        /// <summary>Both rooms share a span of wall: their doors can face each other.</summary>
        static bool Tight(End ea, End eb) => ea.shared && eb.shared && ea.hi - ea.lo >= DoorWidth / 2f;

        static (Dictionary<string, (Door a, Door b)> layout, float minWidth, float deviation) Place(
            List<(string key, Room a, Room b, End ea, End eb)> order, Dictionary<(string room, int wall), int> count)
        {
            var layout = new Dictionary<string, (Door a, Door b)>();
            var placed = new Dictionary<(string room, int wall), List<(float centre, float half)>>();
            float minWidth = float.MaxValue, deviation = 0f;
            foreach (var (key, a, b, ea, eb) in order)
            {
                float width = Mathf.Min(WidthOn(a, ea.wall, count[(a.id, ea.wall)]), WidthOn(b, eb.wall, count[(b.id, eb.wall)]));
                var (pa, pb, w) = PlacePair(a, ea, b, eb, width, placed);
                Occupy(placed, a.id, ea.wall, pa, w);
                Occupy(placed, b.id, eb.wall, pb, w);
                layout[key] = (new Door(a.id, b.id, WallPoint(a, ea.wall, pa), WallNormal(ea.wall), w),
                               new Door(b.id, a.id, WallPoint(b, eb.wall, pb), WallNormal(eb.wall), w));
                minWidth = Mathf.Min(minWidth, w);
                deviation += Mathf.Abs(pa - ea.ideal) + Mathf.Abs(pb - eb.ideal);
            }
            return (layout, minWidth, deviation);
        }

        /// <summary>One end of a connection: its wall, the ideal door position along it, the span shared with the other room.</summary>
        readonly struct End
        {
            public readonly int wall;
            public readonly float ideal, lo, hi;
            public readonly bool shared;
            public End(int wall, float ideal, float lo, float hi, bool shared)
            { this.wall = wall; this.ideal = ideal; this.lo = lo; this.hi = hi; this.shared = shared; }
        }

        static float WidthOn(Room room, int wall, int doors)
        {
            float usable = Mathf.Max(WallLength(room, wall) - 2f * CornerClearance, 0.1f);
            return Mathf.Min(DoorWidth, usable / doors * 0.75f);
        }

        /// <summary>Allowed door centres along a wall: off the corners, and inside the shared span when it is wide enough.</summary>
        static (float lo, float hi) Range(Room room, End e, float width)
        {
            float max = Mathf.Max(WallLength(room, e.wall) / 2f - CornerClearance - width / 2f, 0f);
            float lo = -max, hi = max;
            if (e.shared && e.hi - e.lo >= width)
            {
                lo = Mathf.Max(lo, e.lo + width / 2f);
                hi = Mathf.Min(hi, e.hi - width / 2f);
            }
            if (lo <= hi) return (lo, hi);
            float c = Mathf.Clamp(e.ideal, -max, max);
            return (c, c);
        }

        /// <summary>Door centres (along each wall) and width for a connection; shrinks the doors when they cannot fit.</summary>
        static (float a, float b, float width) PlacePair(Room a, End ea, Room b, End eb, float width,
                                                          Dictionary<(string room, int wall), List<(float centre, float half)>> placed)
        {
            // Position along b's wall of the point at position x along a's wall (linear: f0 + k x).
            float Map(float x)
            {
                var p = MuseumSpatial.ToRoom(b, MuseumSpatial.ToMuseum(a, WallPoint(a, ea.wall, x)));
                return eb.wall <= 1 ? p.x : p.z;
            }
            float f0 = Map(0f), k = Map(1f) - f0;
            bool parallel = Mathf.Abs(k) > 0.7f;
            var occA = Occupied(placed, a.id, ea.wall);
            var occB = Occupied(placed, b.id, eb.wall);

            for (int attempt = 0; attempt < 5; attempt++, width *= 0.8f)
            {
                var (alo, ahi) = Range(a, ea, width);
                var (blo, bhi) = Range(b, eb, width);
                float w = width;
                if (parallel)
                {
                    // b's range seen from a: the same place on both walls (face to face)
                    float x0 = (blo - f0) / k, x1 = (bhi - f0) / k;
                    float lo = Mathf.Max(alo, Mathf.Min(x0, x1)), hi = Mathf.Min(ahi, Mathf.Max(x0, x1));
                    if (lo <= hi && Search(ea.ideal, lo, hi, x => Free(occA, x, w) && Free(occB, f0 + k * x, w), out var x))
                        return (x, f0 + k * x, w);
                }
                // not face to face (rooms barely touching, or turned): each door on its own wall
                if (Search(ea.ideal, alo, ahi, x => Free(occA, x, w), out var pa) &&
                    Search(eb.ideal, blo, bhi, x => Free(occB, x, w), out var pb))
                    return (pa, pb, w);
            }
            var (l, h) = Range(a, ea, width);
            var (bl, bh) = Range(b, eb, width);
            return (Mathf.Clamp(ea.ideal, l, h), Mathf.Clamp(eb.ideal, bl, bh), width);
        }

        /// <summary>Nearest position to <paramref name="ideal"/> within [lo, hi] (5 cm steps) passing <paramref name="ok"/>.</summary>
        static bool Search(float ideal, float lo, float hi, System.Func<float, bool> ok, out float x)
        {
            float start = Mathf.Clamp(ideal, lo, hi);
            for (int i = 0; ; i++)
            {
                float d = i * 0.05f;
                bool inside = false;
                if (start + d <= hi + 1e-4f)
                {
                    inside = true;
                    x = Mathf.Min(start + d, hi);
                    if (ok(x)) return true;
                }
                if (i > 0 && start - d >= lo - 1e-4f)
                {
                    inside = true;
                    x = Mathf.Max(start - d, lo);
                    if (ok(x)) return true;
                }
                if (!inside) { x = start; return false; }
            }
        }

        const float DoorGap = 0.2f; // between two doors on one wall

        static bool Free(List<(float centre, float half)> occupied, float x, float width)
        {
            foreach (var (c, h) in occupied)
                if (Mathf.Abs(x - c) < h + width / 2f + DoorGap - 1e-4f) return false;
            return true;
        }

        static List<(float centre, float half)> Occupied(Dictionary<(string room, int wall), List<(float centre, float half)>> placed,
                                                         string room, int wall)
        {
            if (!placed.TryGetValue((room, wall), out var list)) placed[(room, wall)] = list = new List<(float centre, float half)>();
            return list;
        }

        static void Occupy(Dictionary<(string room, int wall), List<(float centre, float half)>> placed, string room, int wall,
                           float centre, float width) => Occupied(placed, room, wall).Add((centre, width / 2f));

        /// <summary>Museum-space middle of a door, at floor level.</summary>
        public static Vector3 ToMuseum(Room room, Door door) => MuseumSpatial.ToMuseum(room, door.Local);

        /// <summary>
        /// Wall of <paramref name="room"/> facing <paramref name="other"/> (0 = +Z north, 1 = -Z south, 2 = +X east,
        /// 3 = -X west) and the ideal door position along it (room-local): the middle of the span the two rooms share
        /// along that wall, or in front of the other room's centre when they share none.
        /// </summary>
        public static (int wall, float along) FacingWall(Room room, Room other)
        {
            var e = FacingEnd(room, other);
            return (e.wall, e.ideal);
        }

        static End FacingEnd(Room room, Room other)
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
                return new End(centre.x >= 0 ? 2 : 3, lo <= hi ? (lo + hi) / 2f : centre.z, lo, hi, lo <= hi);
            }
            float a = Mathf.Max(x0, -hw), b = Mathf.Min(x1, hw);
            return new End(centre.z >= 0 ? 0 : 1, a <= b ? (a + b) / 2f : centre.x, a, b, a <= b);
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
