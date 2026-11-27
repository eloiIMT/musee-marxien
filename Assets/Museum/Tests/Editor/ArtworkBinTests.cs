using NUnit.Framework;
using UnityEngine;

namespace VirtualMuseum.App.Tests
{
    public class ArtworkBinTests
    {
        [Test]
        public void StoreRoundTripsThroughJsonAndDropsUnknownArtworks()
        {
            var store = new ArtworkBinStore();
            var kept = store.Add("a", "Titre modifié", "Description");
            store.Add("gone", "Retirée du catalogue", null);

            var loaded = ArtworkBinStore.FromJson(store.ToJson(), id => id == "a", out int dropped);

            Assert.That(dropped, Is.EqualTo(1));
            Assert.That(loaded.Items.Count, Is.EqualTo(1));
            Assert.That(loaded.Items[0].id, Is.EqualTo(kept.id));
            Assert.That(loaded.Items[0].definitionId, Is.EqualTo("a"));
            Assert.That(loaded.Items[0].title, Is.EqualTo("Titre modifié"));
            Assert.That(loaded.Items[0].description, Is.EqualTo("Description"));
        }

        [Test]
        public void RemoveTakesOnlyThatEntry()
        {
            var store = new ArtworkBinStore();
            var a = store.Add("a", "A", "");
            var b = store.Add("a", "B", "");
            Assert.That(store.Remove(a.id), Is.True);
            Assert.That(store.Remove(a.id), Is.False);
            Assert.That(store.Items, Is.EqualTo(new[] { b }));
        }

        [Test]
        public void EmptyJsonIsAnEmptyBin()
        {
            Assert.That(ArtworkBinStore.FromJson("", _ => true, out _).Items, Is.Empty);
        }

        [Test]
        public void SlotsFillTheBinFloorThenStack()
        {
            var bin = new GameObject("Test Bin").AddComponent<ArtworkBin>();
            try
            {
                int perLayer = ArtworkBin.Columns * ArtworkBin.Rows;
                for (int i = 0; i < perLayer * 2; i++)
                {
                    var world = bin.transform.TransformPoint(ArtworkBin.SlotPosition(i) + Vector3.up * 0.01f);
                    Assert.That(bin.Contains(world), Is.True, $"slot {i}");
                }
                Assert.That(ArtworkBin.SlotPosition(perLayer).y, Is.GreaterThan(ArtworkBin.SlotPosition(0).y));
                Assert.That(bin.Contains(new Vector3(0f, 0.1f, -0.5f)), Is.False, "in front of the bin");
                Assert.That(bin.IsAimedBy(new Ray(new Vector3(1.5f, 1.6f, 0f), new Vector3(-1.5f, -1.5f, 0f).normalized)), Is.True);
                Assert.That(bin.IsAimedBy(new Ray(new Vector3(1.5f, 1.6f, 0f), Vector3.right)), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(bin.gameObject);
            }
        }
    }
}
