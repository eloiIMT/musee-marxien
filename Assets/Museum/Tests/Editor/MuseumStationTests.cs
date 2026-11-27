using NUnit.Framework;
using UnityEngine;

namespace VirtualMuseum.App.Tests
{
    public class MuseumStationTests
    {
        // 6 x 8 m room: the plan goes on a long wall, the first one tried being the west one (-X).
        static Room Room() => new Room { id = "r", position = Vector3.zero, width = 6, length = 8 };

        static Museum With(Room room, params Vector3[] artworks)
        {
            var m = new Museum();
            m.rooms.Add(room);
            for (int i = 0; i < artworks.Length; i++)
                m.artworks.Add(new ArtworkInstance { id = "a" + i, roomId = room.id, localPosition = artworks[i] });
            return m;
        }

        [Test]
        public void EmptyRoom_PlanOnLongWall()
        {
            var s = MuseumStation.Compute(Room(), With(Room()));
            Assert.AreEqual(Vector3.left, s.normal);
        }

        [Test]
        public void FrontOfPlan_IsReserved_RestOfRoomIsNot()
        {
            var room = Room();
            var museum = With(room);
            Assert.IsTrue(MuseumStation.IsReserved(room, museum, new Vector3(-2.5f, 1f, 0f)));   // just in front of the plan
            Assert.IsTrue(MuseumStation.IsReserved(room, museum, new Vector3(-2.5f, 1f, 1.2f))); // in front of the lever (plan's right = +Z)
            Assert.IsFalse(MuseumStation.IsReserved(room, museum, new Vector3(-2.5f, 1f, -1.2f))); // plan's left, past its edge
            Assert.IsFalse(MuseumStation.IsReserved(room, museum, Vector3.zero));                 // room centre
            Assert.IsFalse(MuseumStation.IsReserved(room, museum, new Vector3(-2.5f, 1f, 3.5f))); // same wall, far along
        }

        [Test]
        public void Lever_StaysOnPlanRight_WhateverTheArtworks()
        {
            var room = Room();
            var empty = MuseumStation.Compute(room, With(room));
            // An artwork right where the lever's platform would go (allowed before the reserved area existed).
            var crowded = MuseumStation.Compute(room, With(room, new Vector3(-1.5f, 1f, 2.5f), new Vector3(-2.5f, 1f, -2.5f)));
            Assert.AreEqual(1, empty.side);
            Assert.AreEqual(empty.side, crowded.side);
            Assert.AreEqual(empty.spot, crowded.spot);
        }

        [Test]
        public void MovedArtwork_IsIgnored()
        {
            var room = Room();
            // An artwork in front of the west wall pushes the plan to the east wall...
            var museum = With(room, new Vector3(-2.5f, 1f, 0f));
            Assert.AreEqual(Vector3.right, MuseumStation.Compute(room, museum).normal);
            // ...unless it is the one being moved: then the west wall stays the plan wall and its spot is reserved.
            Assert.IsTrue(MuseumStation.IsReserved(room, museum, new Vector3(-2.5f, 1f, 0f), "a0"));
        }

        static Museum Linked(Room room, Vector3 otherPosition)
        {
            var m = With(room);
            var other = new Room { id = "o", position = otherPosition, width = 6, length = 8 };
            room.connections.Add(other.id);
            other.connections.Add(room.id);
            m.rooms.Add(other);
            return m;
        }

        [Test]
        public void FrontOfDoor_IsBlocked_RestOfRoomIsNot()
        {
            var room = Room();
            var m = Linked(room, new Vector3(10, 0, 0)); // door on the east wall (x = 3), middle of the shared span
            Assert.IsTrue(MuseumStation.InFrontOfDoor(room, m, new Vector3(2.2f, 1, 0)));
            Assert.IsFalse(MuseumStation.InFrontOfDoor(room, m, new Vector3(0, 1, 0)));     // far enough from the wall
            Assert.IsFalse(MuseumStation.InFrontOfDoor(room, m, new Vector3(2.2f, 1, 3.5f))); // beside the door
        }

        [Test]
        public void Plan_AvoidsTheWallWithADoor()
        {
            var room = Room();
            var m = Linked(room, new Vector3(-10, 0, 0)); // door in the middle of the west wall, the plan's usual wall
            var s = MuseumStation.Compute(room, m);
            Assert.AreNotEqual(Vector3.left, s.normal);
        }
    }
}
