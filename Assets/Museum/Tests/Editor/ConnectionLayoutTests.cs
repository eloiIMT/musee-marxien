using NUnit.Framework;
using UnityEngine;

namespace VirtualMuseum.App.Tests
{
    public class ConnectionLayoutTests
    {
        static Room R(string id, float x, float z, float width = 6, float length = 6, float yaw = 0)
            => new Room { id = id, name = id, position = new Vector3(x, 0, z), rotation = Quaternion.Euler(0, yaw, 0), width = width, length = length };

        static void Link(Room a, Room b) { a.connections.Add(b.id); b.connections.Add(a.id); }

        static Museum M(params Room[] rooms) { var m = new Museum(); m.rooms.AddRange(rooms); return m; }

        [Test]
        public void Doors_AreOnTheFacingWalls()
        {
            var a = R("A", 0, 0); var b = R("B", 10, 0); Link(a, b);
            var (da, db) = ConnectionLayout.Compute(M(a, b))[MuseumView.ConnectionKey("A", "B")];
            Assert.AreEqual(Vector3.right, da.Normal);  // A's east wall
            Assert.AreEqual(Vector3.left, db.Normal);   // B's west wall
            Assert.AreEqual(3f, da.Local.x, 1e-4f);
            Assert.AreEqual(-3f, db.Local.x, 1e-4f);
        }

        [Test]
        public void SingleDoor_FacesTheOtherRoom_ButStaysAwayFromCorners()
        {
            var a = R("A", 0, 0); var b = R("B", 10, 2); var c = R("C", 0, 30); Link(a, b); Link(a, c);
            var layout = ConnectionLayout.Compute(M(a, b, c));
            var (da, db) = layout[MuseumView.ConnectionKey("A", "B")];
            Assert.AreEqual(1f, da.Local.z, 1e-4f);     // middle of the shared span z = -1..3
            Assert.AreEqual(1f, ConnectionLayout.ToMuseum(b, db).z, 1e-4f); // face to face: straight line
            var (dac, _) = layout[MuseumView.ConnectionKey("A", "C")];
            Assert.AreEqual(Vector3.forward, dac.Normal); // C is far north
            Assert.LessOrEqual(Mathf.Abs(dac.Local.x) + dac.Width / 2f, 3f - 0.4f + 1e-4f);
        }

        [Test]
        public void SeveralDoorsOnOneWall_AreSpreadWithoutOverlap_InTheOrderOfTheirRooms()
        {
            var a = R("A", 0, 0);
            var north = R("N", 2, 10); var east = R("E", 10, 1); var south = R("S", 9, -3);
            Link(a, north); Link(a, east); Link(a, south);
            var b2 = R("B", 10, 8); Link(a, b2); // also east
            var layout = ConnectionLayout.Compute(M(a, north, east, south, b2));
            var e1 = layout[MuseumView.ConnectionKey("A", "E")].a;
            var e2 = layout[MuseumView.ConnectionKey("A", "B")].a;
            var e3 = layout[MuseumView.ConnectionKey("A", "S")].a;
            Assert.AreEqual(Vector3.right, e1.Normal);
            Assert.AreEqual(Vector3.right, e2.Normal);
            Assert.AreEqual(Vector3.right, e3.Normal);
            // Ordered like the rooms (south < east < north-east) and not overlapping.
            Assert.Less(e3.Local.z, e1.Local.z);
            Assert.Less(e1.Local.z, e2.Local.z);
            Assert.GreaterOrEqual(e1.Local.z - e3.Local.z, (e1.Width + e3.Width) / 2f);
            Assert.GreaterOrEqual(e2.Local.z - e1.Local.z, (e1.Width + e2.Width) / 2f);
        }

        [Test]
        public void RotatedRoom_UsesItsOwnFrame()
        {
            var a = R("A", 0, 0, yaw: 90); var b = R("B", 10, 0); Link(a, b);
            var (da, _) = ConnectionLayout.Compute(M(a, b))[MuseumView.ConnectionKey("A", "B")];
            // Turned 90°: the room's +Z wall now faces +X (east), towards B.
            Assert.AreEqual(Vector3.forward, da.Normal);
            Assert.Greater(ConnectionLayout.ToMuseum(a, da).x, 2.9f);
        }

        [Test]
        public void OffsetRoomsSideBySide_UseTheSideWalls_NotTheTopWall()
        {
            // B is to the right and mostly above A (tall room): they are apart along X, so the doors go on A's east
            // wall and B's west wall, not on A's north wall (the line would run along the wall).
            var a = R("A", 0, 0); var b = R("B", 7, 4, width: 6, length: 10); Link(a, b);
            var (da, db) = ConnectionLayout.Compute(M(a, b))[MuseumView.ConnectionKey("A", "B")];
            Assert.AreEqual(Vector3.right, da.Normal);
            Assert.AreEqual(Vector3.left, db.Normal);
            Assert.AreEqual(ConnectionLayout.ToMuseum(a, da).z, ConnectionLayout.ToMuseum(b, db).z, 1e-4f);
        }

        [Test]
        public void RoomsSharingAWall_GetBackToBackDoors_EvenWithOtherDoorsOnThatWall()
        {
            // B shares A's east wall; C touches B's west wall at its far end: B's two doors must not move B's door to A
            // off A's (they stay back to back), and both doors of each connection have the same width.
            var a = R("A", 0, 0, 8, 6); var b = R("B", 9, 0, 10, 8); var c = R("C", 0, 8, 6, 10);
            Link(a, b); Link(b, c); Link(a, c);
            var layout = ConnectionLayout.Compute(M(a, b, c));
            foreach (var (da, db) in layout.Values) Assert.AreEqual(da.Width, db.Width, 1e-4f);
            var (ab, ba) = layout[MuseumView.ConnectionKey("A", "B")];
            var pa = ConnectionLayout.ToMuseum(a, ab); var pb = ConnectionLayout.ToMuseum(b, ba);
            Assert.AreEqual(pa.x, pb.x, 1e-4f);
            Assert.AreEqual(pa.z, pb.z, 1e-4f);
            var (bc, _) = layout[MuseumView.ConnectionKey("B", "C")];
            Assert.GreaterOrEqual(Mathf.Abs(bc.Local.z - ba.Local.z), (bc.Width + ba.Width) / 2f);
        }

        [Test]
        public void DanglingConnection_IsIgnored()
        {
            var a = R("A", 0, 0); a.connections.Add("missing");
            Assert.AreEqual(0, ConnectionLayout.Compute(M(a)).Count);
        }
    }
}
