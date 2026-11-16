using System;
using UnityEngine;

namespace VirtualMuseum
{
    /// <summary>Metres, Y up, X width, Z length; room origin is the floor centre.</summary>
    public static class MuseumSpatial
    {
        public static Matrix4x4 RoomMatrix(Room room) => Matrix4x4.TRS(room.position, room.rotation, Vector3.one);
        public static Matrix4x4 ArtworkMatrix(Room room, ArtworkInstance artwork) =>
            RoomMatrix(room) * Matrix4x4.TRS(artwork.localPosition, artwork.localRotation, artwork.localScale);
        public static Vector3 ToMuseum(Room room, Vector3 local) => room.position + room.rotation * local;
        public static Vector3 ToRoom(Room room, Vector3 museum) => Quaternion.Inverse(room.rotation) * (museum - room.position);
        public static Vector3 ToWorld(Room room, Vector3 local, Transform museumRoot) => museumRoot.TransformPoint(ToMuseum(room, local));
        public static Vector3 WorldToRoom(Room room, Vector3 world, Transform museumRoot) => ToRoom(room, museumRoot.InverseTransformPoint(world));
        public static Bounds LocalBounds(Room room) => new Bounds(new Vector3(0, room.height / 2, 0), new Vector3(room.width, room.height, room.length));
        /// <summary>Axis-aligned museum-space bounds enclosing all eight rotated corners.</summary>
        public static Bounds MuseumBounds(Room room)
        {
            var local = LocalBounds(room);
            var result = new Bounds(ToMuseum(room, local.min), Vector3.zero);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        result.Encapsulate(ToMuseum(room, new Vector3(x == 0 ? local.min.x : local.max.x,
                            y == 0 ? local.min.y : local.max.y, z == 0 ? local.min.z : local.max.z)));
            return result;
        }
        internal static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        internal static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        internal static bool Positive(Vector3 v) => Finite(v) && v.x > 0 && v.y > 0 && v.z > 0;
        internal static bool ValidRotation(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w)
            && Mathf.Abs(q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w - 1) < 0.001f;
        internal static void Require(bool valid, string message)
        { if (!valid) throw new ArgumentException(message); }
    }
}
