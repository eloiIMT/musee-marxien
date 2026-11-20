using NUnit.Framework;
using UnityEngine;

namespace VirtualMuseum.App.Tests
{
    public class MuseumPathsTests
    {
        static Room R(string id, params string[] links)
        {
            var r = new Room { id = id, name = id, position = Vector3.zero };
            r.connections.AddRange(links);
            return r;
        }

        // A - B - C - D, plus a shortcut A - D? no: a branch B - E and a loop C - E.
        static Museum Sample()
        {
            var m = new Museum();
            m.rooms.Add(R("A", "B"));
            m.rooms.Add(R("B", "A", "C", "E"));
            m.rooms.Add(R("C", "B", "D", "E"));
            m.rooms.Add(R("D", "C"));
            m.rooms.Add(R("E", "B", "C"));
            m.rooms.Add(R("F")); // isolated
            return m;
        }

        [Test]
        public void SameRoom_IsSingleStep()
        {
            CollectionAssert.AreEqual(new[] { "A" }, MuseumPaths.ShortestPath(Sample(), "A", "A"));
        }

        [Test]
        public void FindsFewestRooms()
        {
            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D" }, MuseumPaths.ShortestPath(Sample(), "A", "D"));
            CollectionAssert.AreEqual(new[] { "D", "C", "E" }, MuseumPaths.ShortestPath(Sample(), "D", "E"));
        }

        [Test]
        public void Unreachable_OrMissing_IsNull()
        {
            Assert.IsNull(MuseumPaths.ShortestPath(Sample(), "A", "F"));
            Assert.IsNull(MuseumPaths.ShortestPath(Sample(), "A", "Z"));
            Assert.IsNull(MuseumPaths.ShortestPath(Sample(), null, "A"));
        }

        [Test]
        public void Connected_IsDirectLinkOnly()
        {
            Assert.IsTrue(MuseumPaths.AreConnected(Sample(), "A", "B"));
            Assert.IsFalse(MuseumPaths.AreConnected(Sample(), "A", "C"));
        }
    }
}
