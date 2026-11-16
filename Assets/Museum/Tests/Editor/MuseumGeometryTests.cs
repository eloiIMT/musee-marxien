using NUnit.Framework;
using UnityEngine;

namespace VirtualMuseum.App.Tests
{
    public class MuseumGeometryTests
    {
        static Room RoomAt(Vector3 position, float yaw = 0, float w = 6, float l = 6) => new Room
        {
            id = System.Guid.NewGuid().ToString("N"),
            position = position,
            rotation = Quaternion.Euler(0, yaw, 0),
            width = w,
            length = l,
        };

        [Test]
        public void Snap_RoundsToGrid()
        {
            Assert.AreEqual(new Vector3(1.5f, 0, -2f), MuseumGeometry.SnapXZ(new Vector3(1.6f, 3f, -2.2f)));
            Assert.AreEqual(30f, MuseumGeometry.SnapAngle(37f));
            Assert.AreEqual(345f, MuseumGeometry.SnapAngle(-14f));
        }

        [Test]
        public void Rooms_TooClose_Overlap()
        {
            var other = RoomAt(Vector3.zero);
            // Walls 0.9 m apart (< 1 m margin).
            Assert.IsTrue(MuseumGeometry.OverlapsRoom(new Vector3(6.9f, 0, 0), 0, 6, 6, other));
        }

        [Test]
        public void Rooms_FarEnough_DoNotOverlap()
        {
            var other = RoomAt(Vector3.zero);
            // Walls 1.1 m apart.
            Assert.IsFalse(MuseumGeometry.OverlapsRoom(new Vector3(7.1f, 0, 0), 0, 6, 6, other));
        }

        [Test]
        public void RotatedRoom_UsesOrientedFootprint()
        {
            var other = RoomAt(Vector3.zero);
            // 45° square of half-diagonal 4.24 m: its corner reaches x = 8 - 4.24 = 3.76, inside margin of the other.
            Assert.IsTrue(MuseumGeometry.OverlapsRoom(new Vector3(8f, 0, 0), 45, 6, 6, other));
            // Axis aligned at the same spot: walls 2 m apart, fine.
            Assert.IsFalse(MuseumGeometry.OverlapsRoom(new Vector3(8f, 0, 0), 0, 6, 6, other));
        }

        [Test]
        public void RoomAt_FindsContainingRoom()
        {
            var museum = new Museum();
            var a = RoomAt(Vector3.zero);
            var b = RoomAt(new Vector3(10, 0, 0), 90, 4, 8);
            museum.rooms.Add(a);
            museum.rooms.Add(b);
            Assert.AreEqual(a.id, MuseumGeometry.RoomAt(museum, new Vector3(2, 0, 2)));
            Assert.AreEqual(b.id, MuseumGeometry.RoomAt(museum, new Vector3(13.5f, 0, 0))); // rotated: 8 m along X
            Assert.IsNull(MuseumGeometry.RoomAt(museum, new Vector3(5, 0, 0)));
        }

        [Test]
        public void NextRoomName_SkipsUsedLetters()
        {
            var museum = new Museum();
            museum.rooms.Add(new Room { name = "Salle A" });
            museum.rooms.Add(new Room { name = "Salle C" });
            Assert.AreEqual("Salle B", MuseumGeometry.NextRoomName(museum));
        }
    }
}
