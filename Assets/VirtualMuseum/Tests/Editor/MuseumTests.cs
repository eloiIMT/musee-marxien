using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VirtualMuseum.Tests
{
    public class MuseumTests
    {
        private ArtworkCatalog catalog;
        private GameObject prefab;
        private MuseumApi api;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("Test artwork");
            catalog = ScriptableObject.CreateInstance<ArtworkCatalog>();
            var serialized = new SerializedObject(catalog);
            var entries = serialized.FindProperty("definitions"); entries.arraySize = 6;
            for (int i = 0; i < 6; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("id").stringValue = "definition-" + i;
                entry.FindPropertyRelative("title").stringValue = "Title " + i;
                entry.FindPropertyRelative("description").stringValue = "Description " + i;
                entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            api = MuseumExample.Create(catalog);
        }
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(prefab);
        }
        [Test]
        public void ExampleIsValidAndSnapshotsCannotMutateIt()
        {
            Assert.That(api.Validate(), Is.Empty);
            Assert.That(api.Data.rooms.Count, Is.EqualTo(3));
            Assert.That(api.Data.artworks.Count, Is.EqualTo(6));
            Assert.That(api.Data.tours.Count, Is.EqualTo(2));
            api.Data.rooms.Clear();
            var r = api.Data.rooms[0]; api.GetRoom(r.id).connections.Clear();
            Assert.That(api.GetConnectedRooms(r.id).Count, Is.EqualTo(2));
            Assert.That(api.CurrentArtwork, Is.Not.Null);
        }
        [Test]
        public void ConnectionsAreBidirectionalAndIdempotent()
        {
            var rooms = api.Data.rooms;
            api.DisconnectRooms(rooms[0].id, rooms[1].id);
            Assert.That(api.GetRoom(rooms[1].id).connections, Does.Not.Contain(rooms[0].id));
            api.ConnectRooms(rooms[0].id, rooms[1].id);
            api.ConnectRooms(rooms[0].id, rooms[1].id);
            Assert.That(api.GetRoom(rooms[0].id).connections.Count, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => api.ConnectRooms(rooms[0].id, rooms[0].id));
        }
        [Test]
        public void DeleteRoomRemovesArtworksConnectionsAndEveryTourReference()
        {
            var room = api.Data.rooms[0];
            var removed = api.GetArtworksInRoom(room.id).Select(a => a.id).ToArray();
            api.Advance();
            api.DeleteRoom(room.id);
            Assert.That(api.GetRoom(room.id), Is.Null);
            Assert.That(api.Data.artworks.Any(a => removed.Contains(a.id)), Is.False);
            Assert.That(api.Data.tours.Any(t => t.artworkIds.Any(removed.Contains)), Is.False);
            Assert.That(api.Session.index, Is.Zero);
            Assert.That(api.Validate(), Is.Empty);
        }
        [Test]
        public void InstancesHaveIndependentMetadataAndTourRemovalPrunesRepeatedReferences()
        {
            var room = api.Data.rooms[0];
            var a = api.AddArtwork("definition-0", room.id, Vector3.zero);
            var b = api.AddArtwork("definition-0", room.id, Vector3.one);
            Assert.That(a.id, Is.Not.EqualTo(b.id));
            Assert.That(a.title, Is.EqualTo("Title 0"));
            api.EditArtwork(a.id, "Edited", "New text");
            Assert.That(api.GetArtwork(b.id).title, Is.EqualTo("Title 0"));
            var tour = api.CreateTour("Repeat");
            api.AddTourArtwork(tour.id, a.id); api.AddTourArtwork(tour.id, a.id);
            api.RemoveArtwork(a.id);
            Assert.That(api.GetTourArtworks(tour.id), Is.Empty);
            Assert.That(api.Validate(), Is.Empty);
        }
        [Test]
        public void TransformsRoundTripAndTransferPreservesMuseumPose()
        {
            var rooms = api.Data.rooms;
            api.SetRoomTransform(rooms[0].id, new Vector3(5, 2, -3), Quaternion.Euler(10, 75, 15));
            api.SetRoomTransform(rooms[1].id, new Vector3(-4, 1, 8), Quaternion.Euler(0, -20, 0));
            var a = api.GetArtworksInRoom(rooms[0].id)[0];
            api.SetArtworkTransform(a.id, Vector3.one, Quaternion.Euler(5, 20, 7), new Vector3(1, 2, 3));
            a = api.GetArtwork(a.id);
            var room = api.GetRoom(a.roomId);
            var position = MuseumSpatial.ToMuseum(room, a.localPosition);
            var rotation = room.rotation * a.localRotation;
            Assert.That(Vector3.Distance(a.localPosition, MuseumSpatial.ToRoom(room, position)), Is.LessThan(0.0001f));
            api.TransferArtwork(a.id, rooms[1].id);
            var moved = api.GetArtwork(a.id); var target = api.GetRoom(moved.roomId);
            Assert.That(Vector3.Distance(position, MuseumSpatial.ToMuseum(target, moved.localPosition)), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(rotation, target.rotation * moved.localRotation), Is.LessThan(0.01f));
            Assert.That(moved.localScale, Is.EqualTo(a.localScale));
        }
        [Test]
        public void BoundsEncloseRotatedRoomAndWorldConversionUsesRoot()
        {
            var r = api.Data.rooms[0];
            api.SetRoomTransform(r.id, new Vector3(2, 0, 3), Quaternion.Euler(0, 90, 0));
            r = api.GetRoom(r.id);
            var bounds = MuseumSpatial.MuseumBounds(r);
            Assert.That(bounds.size.x, Is.EqualTo(r.length).Within(0.0001));
            Assert.That(bounds.size.z, Is.EqualTo(r.width).Within(0.0001));
            var root = new GameObject("Museum root");
            try
            {
                root.transform.SetPositionAndRotation(Vector3.one * 10, Quaternion.Euler(0, 33, 0));
                root.transform.localScale = Vector3.one * 0.1f;
                var world = MuseumSpatial.ToWorld(r, Vector3.one, root.transform);
                Assert.That(Vector3.Distance(Vector3.one, MuseumSpatial.WorldToRoom(r, world, root.transform)), Is.LessThan(0.0001));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [Test]
        public void InvalidEditsDoNotMutateData()
        {
            var r = api.Data.rooms[0]; var before = MuseumPersistence.ToJson(api);
            Assert.Throws<ArgumentException>(() => api.ResizeRoom(r.id, -1, 4, 3));
            Assert.Throws<ArgumentException>(() => api.SetRoomTransform(r.id, Vector3.zero, new Quaternion()));
            Assert.Throws<ArgumentException>(() => api.AddArtwork("missing", r.id, Vector3.zero));
            Assert.Throws<ArgumentException>(() => api.SetArtworkTransform(api.Data.artworks[0].id, Vector3.zero, Quaternion.identity, Vector3.zero));
            Assert.That(MuseumPersistence.ToJson(api), Is.EqualTo(before));
        }
        [Test]
        public void SessionCompletesOnlyAfterLastArtworkAndCanGoBack()
        {
            Assert.That(api.Progress, Is.Zero);
            Assert.That(api.PreviousArtwork, Is.Null);
            Assert.That(api.NextArtwork, Is.Not.Null);
            while (api.Advance()) { }
            Assert.That(api.IsComplete, Is.True);
            Assert.That(api.Progress, Is.EqualTo(1));
            Assert.That(api.CurrentArtwork, Is.Null);
            Assert.That(api.PreviousArtwork, Is.Not.Null);
            Assert.That(api.GoBack(), Is.True);
            Assert.That(api.IsComplete, Is.False);
            api.ResetTour(); Assert.That(api.Session.index, Is.Zero);
            var empty = api.CreateTour("Empty"); api.StartTour(empty.id);
            Assert.That(api.IsComplete, Is.True);
            Assert.That(api.Advance(), Is.False);
            api.DeleteTour(empty.id);
            Assert.That(api.HasSelectedTour, Is.False);
            Assert.That(api.IsComplete, Is.False);
        }
        [Test]
        public void TourReorderUsesFinalIndexAndContentEditsResetActiveSession()
        {
            var t = api.GetTour(api.Session.tourId);
            api.Advance(); api.ReorderTourArtwork(t.id, 0, 2);
            Assert.That(api.GetTour(t.id).artworkIds[2], Is.EqualTo(t.artworkIds[0]));
            Assert.That(api.Session.index, Is.Zero);
            api.RemoveTourArtwork(t.id, 1);
            Assert.That(api.GetTourArtworks(t.id).Count, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => api.AddTourArtwork(t.id, "unknown"));
        }
        [Test]
        public void JsonAndDiskRoundTripPreserveIdsMetadataAndSession()
        {
            api.Advance(); api.RenameMuseum("Saved Museum");
            var expected = MuseumPersistence.ToJson(api);
            var path = Path.Combine(Path.GetTempPath(), "museum-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Assert.That(MuseumPersistence.TrySave(api, out var error, path), Is.True, error);
                // Exercise replacement of an existing file as well.
                Assert.That(MuseumPersistence.TrySave(api, out error, path), Is.True, error);
                api.ResetToExample();
                Assert.That(MuseumPersistence.TryLoad(api, out error, path), Is.True, error);
                Assert.That(MuseumPersistence.ToJson(api), Is.EqualTo(expected));
            }
            finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
        }
        [TestCase("")]
        [TestCase("{}")]
        [TestCase("not json")]
        [TestCase("{\"version\":999}")]
        public void InvalidJsonIsRejectedWithoutChangingMuseum(string json)
        {
            var before = MuseumPersistence.ToJson(api);
            Assert.That(MuseumPersistence.TryLoadJson(api, json, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(MuseumPersistence.ToJson(api), Is.EqualTo(before));
        }
        [Test]
        public void BrokenReferencesAndSessionsAreRejected()
        {
            var before = MuseumPersistence.ToJson(api);
            var broken = before.Replace("definition-0", "missing-definition");
            Assert.That(MuseumPersistence.TryLoadJson(api, broken, out _), Is.False);
            Assert.Throws<ArgumentException>(() => api.RestoreSession(new VisitorSession { tourId = "missing", index = 0 }));
            var data = api.Data; data.rooms[0].connections.Clear();
            Assert.Throws<ArgumentException>(() => api.Replace(data, api.Session));
            Assert.That(MuseumPersistence.TryLoad(api, out _, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")), Is.False);
            Assert.That(MuseumPersistence.ToJson(api), Is.EqualTo(before));
        }
        [Test]
        public void EventsReportChangesAfterValidStateIsAvailable()
        {
            var count = 0; var sessionCount = 0;
            api.Changed += change => { Assert.That(api.Validate(), Is.Empty); count++; };
            api.SessionChanged += () => { Assert.That(api.Validate(), Is.Empty); sessionCount++; };
            var r = api.CreateRoom("New", Vector3.zero);
            api.SetRoomTransform(r.id, Vector3.one, Quaternion.identity);
            api.ResizeRoom(r.id, 5, 4, 3);
            api.Advance(); api.DeleteRoom(api.Data.rooms[0].id);
            Assert.That(count, Is.GreaterThanOrEqualTo(4));
            Assert.That(sessionCount, Is.GreaterThanOrEqualTo(2));
        }
        [Test]
        public void SuppliedPrefabResolvesCatalogAndInstantiatesArtworkOnRequest()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VirtualMuseum/Sample/MuseumBackend.prefab");
            Assert.That(asset, Is.Not.Null, "Run Tools > Virtual Museum > Create Missing Example Assets.");
            var go = UnityEngine.Object.Instantiate(asset);
            var room = new GameObject("Student room representation");
            try
            {
                var backend = go.GetComponent<MuseumBackend>();
                Assert.That(backend.Api.Validate(), Is.Empty);
                Assert.That(backend.Catalog.Definitions.Count, Is.EqualTo(6));
                var a = backend.Api.Data.artworks[0];
                var artwork = backend.InstantiateArtwork(a.id, room.transform);
                Assert.That(artwork.transform.parent, Is.EqualTo(room.transform));
                Assert.That(artwork.transform.localPosition, Is.EqualTo(a.localPosition));
                Assert.That(artwork.transform.localScale, Is.EqualTo(a.localScale));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(room); }
        }
    }
}
