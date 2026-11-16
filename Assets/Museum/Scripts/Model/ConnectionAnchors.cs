using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Where each cable touches its two walls, chosen freely during the session (spec 2.5).
    /// The model stores no attachment point, so this lives OUTSIDE the model: it is lost on Load / Reset and the
    /// cable then falls back to the closest wall midpoints. Points are room-local, so they follow moves and rotations.
    /// Shared by every MuseumView so the miniature and the immersive museum agree.
    /// </summary>
    public static class ConnectionAnchors
    {
        readonly struct Anchor
        {
            public readonly string RoomA; public readonly Vector3 LocalA;
            public readonly string RoomB; public readonly Vector3 LocalB;
            public Anchor(string ra, Vector3 la, string rb, Vector3 lb) { RoomA = ra; LocalA = la; RoomB = rb; LocalB = lb; }
        }

        static readonly Dictionary<string, Anchor> anchors = new();

        public static void Set(string roomA, Vector3 localA, string roomB, Vector3 localB)
        {
            anchors[MuseumView.ConnectionKey(roomA, roomB)] = new Anchor(roomA, localA, roomB, localB);
        }

        public static void Clear() => anchors.Clear();

        /// <summary>Museum-space end points for a connection, snapped back onto the walls if a room was resized.</summary>
        public static (Vector3 a, Vector3 b) Endpoints(Room a, Room b)
        {
            if (!anchors.TryGetValue(MuseumView.ConnectionKey(a.id, b.id), out var anchor))
                return MuseumGeometry.ClosestWallMidpoints(a, b);
            var la = anchor.RoomA == a.id ? anchor.LocalA : anchor.LocalB;
            var lb = anchor.RoomA == a.id ? anchor.LocalB : anchor.LocalA;
            return (MuseumSpatial.ToMuseum(a, MuseumGeometry.ClosestPointOnPerimeter(a.width, a.length, la)),
                    MuseumSpatial.ToMuseum(b, MuseumGeometry.ClosestPointOnPerimeter(b.width, b.length, lb)));
        }
    }
}
