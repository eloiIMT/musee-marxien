using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>An artwork waiting in the bin: what is needed to put it back in the museum.</summary>
    [Serializable]
    public sealed class StoredArtwork
    {
        public string id;
        public string definitionId;
        public string title;
        public string description;
    }

    /// <summary>
    /// Contents of the <see cref="ArtworkBin"/>, saved apart from the museum (the museum save only holds artworks
    /// placed in a room). Plain data, no Unity objects.
    /// </summary>
    public sealed class ArtworkBinStore
    {
        [Serializable]
        sealed class SaveData
        {
            public int version = 1;
            public List<StoredArtwork> items = new();
        }

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "artwork-bin.json");

        readonly List<StoredArtwork> items = new();

        public IReadOnlyList<StoredArtwork> Items => items;

        public StoredArtwork Add(string definitionId, string title, string description)
        {
            var entry = new StoredArtwork
            {
                id = Guid.NewGuid().ToString("N"),
                definitionId = definitionId,
                title = title ?? "",
                description = description ?? "",
            };
            items.Add(entry);
            return entry;
        }

        public StoredArtwork Find(string id) => items.Find(e => e.id == id);

        public bool Remove(string id) => items.RemoveAll(e => e.id == id) > 0;

        public string ToJson() => JsonUtility.ToJson(new SaveData { items = items }, true);

        /// <summary>Entries whose artwork is no longer in the catalog are dropped (<paramref name="dropped"/>).</summary>
        public static ArtworkBinStore FromJson(string json, Func<string, bool> isKnownDefinition, out int dropped)
        {
            dropped = 0;
            var store = new ArtworkBinStore();
            if (string.IsNullOrWhiteSpace(json)) return store;
            var data = JsonUtility.FromJson<SaveData>(json);
            if (data == null || data.version != 1 || data.items == null)
                throw new ArgumentException("Missing bin data or unsupported version.");
            foreach (var e in data.items)
            {
                if (e == null || string.IsNullOrEmpty(e.id) || !isKnownDefinition(e.definitionId)) { dropped++; continue; }
                e.title ??= "";
                e.description ??= "";
                store.items.Add(e);
            }
            return store;
        }

        /// <summary>No file yet = empty bin. An unreadable file gives an empty bin and an error.</summary>
        public static ArtworkBinStore Load(string path, Func<string, bool> isKnownDefinition, out string error)
        {
            error = null;
            if (!File.Exists(path)) return new ArtworkBinStore();
            try
            {
                var store = FromJson(File.ReadAllText(path), isKnownDefinition, out int dropped);
                if (dropped > 0) error = $"{dropped} œuvre(s) inconnue(s) du catalogue retirée(s) du bac.";
                return store;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                error = ex.Message;
                return new ArtworkBinStore();
            }
        }

        /// <summary>Written to a temporary file first, like the museum save.</summary>
        public bool TrySave(string path, out string error)
        {
            try
            {
                path = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, ToJson());
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
