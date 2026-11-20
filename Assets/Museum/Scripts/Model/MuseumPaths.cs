using System.Collections.Generic;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Pure graph helpers on the room network (connections are bidirectional edges). No scene access: unit-testable.
    /// Used by the visitor wall plan: which room to cross first to reach a room that is not directly connected.
    /// </summary>
    public static class MuseumPaths
    {
        /// <summary>
        /// Fewest-rooms path (breadth-first search) from <paramref name="fromRoomId"/> to <paramref name="toRoomId"/>,
        /// both included. Same room = single element. Null if either room is missing or the target is unreachable.
        /// </summary>
        public static List<string> ShortestPath(Museum museum, string fromRoomId, string toRoomId)
        {
            if (museum == null || fromRoomId == null || toRoomId == null) return null;
            var byId = new Dictionary<string, Room>();
            foreach (var r in museum.rooms) byId[r.id] = r;
            if (!byId.ContainsKey(fromRoomId) || !byId.ContainsKey(toRoomId)) return null;

            var previous = new Dictionary<string, string> { [fromRoomId] = null };
            var queue = new Queue<string>();
            queue.Enqueue(fromRoomId);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (id == toRoomId) break;
                foreach (var next in byId[id].connections)
                {
                    if (!byId.ContainsKey(next) || previous.ContainsKey(next)) continue;
                    previous[next] = id;
                    queue.Enqueue(next);
                }
            }
            if (!previous.ContainsKey(toRoomId)) return null;

            var path = new List<string>();
            for (var id = toRoomId; id != null; id = previous[id]) path.Add(id);
            path.Reverse();
            return path;
        }

        /// <summary>Are two rooms directly connected?</summary>
        public static bool AreConnected(Museum museum, string a, string b)
        {
            if (museum == null || a == null || b == null) return false;
            var room = museum.rooms.Find(r => r.id == a);
            return room != null && room.connections.Contains(b);
        }
    }
}
