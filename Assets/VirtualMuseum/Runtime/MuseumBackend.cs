using System;
using UnityEngine;

namespace VirtualMuseum
{
    /// <summary>Attach once to a student-owned scene; creates data only, never representations.</summary>
    public sealed class MuseumBackend : MonoBehaviour
    {
        [SerializeField] private ArtworkCatalog catalog;
        [SerializeField] private bool initializeWithExample = true;
        private MuseumApi api;
        public MuseumApi Api { get { Initialize(); return api; } }
        public ArtworkCatalog Catalog => catalog;
        private void Awake() => Initialize();
        public void Initialize()
        {
            if (api != null) return;
            if (catalog == null) throw new InvalidOperationException("Assign an ArtworkCatalog to MuseumBackend (or use the supplied prefab).");
            api = initializeWithExample ? MuseumExample.Create(catalog) : new MuseumApi(catalog);
        }
        public bool Save(out string error) => MuseumPersistence.TrySave(Api, out error);
        public bool Load(out string error) => MuseumPersistence.TryLoad(Api, out error);
        [ContextMenu("Museum/Initialize")]
        private void InitializeMenu() => Initialize();
        [ContextMenu("Museum/Reset Example")]
        public void ResetToExample() => Api.ResetToExample();
        [ContextMenu("Museum/Save")]
        private void SaveMenu()
        {
            if (Save(out var error)) Debug.Log("Saved museum: " + MuseumPersistence.DefaultPath, this);
            else Debug.LogWarning(error, this);
        }
        [ContextMenu("Museum/Load")]
        private void LoadMenu()
        {
            if (Load(out var error)) Debug.Log("Loaded museum: " + MuseumPersistence.DefaultPath, this);
            else Debug.LogWarning("Museum unchanged: " + error, this);
        }
        [ContextMenu("Museum/Verify")]
        private void VerifyMenu()
        {
            var errors = Api.Validate();
            Debug.Log(errors.Count == 0 ? "Museum valid: " + Api.Data.rooms.Count + " rooms, " + Api.Data.artworks.Count + " artworks, " + Api.Data.tours.Count + " tours." : string.Join("\n", errors), this);
        }
        /// <summary>Explicit opt-in helper. Parent must represent the artwork's room; applies room-local TRS.</summary>
        public GameObject InstantiateArtwork(string instanceId, Transform roomParent)
        {
            if (roomParent == null) throw new ArgumentNullException(nameof(roomParent));
            var a = Api.GetArtwork(instanceId) ?? throw new ArgumentException("Unknown artwork: " + instanceId);
            var definition = catalog.Find(a.definitionId);
            if (definition == null || definition.prefab == null) throw new InvalidOperationException("Missing definition or prefab.");
            var go = Instantiate(definition.prefab, roomParent, false);
            go.name = a.title;
            go.transform.SetLocalPositionAndRotation(a.localPosition, a.localRotation);
            go.transform.localScale = a.localScale;
            return go;
        }
    }
}

