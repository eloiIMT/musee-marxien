using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum
{
    [Serializable]
    public sealed class ArtworkDefinition
    {
        public string id;
        public string title;
        [TextArea] public string description;
        public GameObject prefab;
        public string artist;
        public string year;
    }

    [CreateAssetMenu(menuName = "Virtual Museum/Artwork Catalog")]
    public sealed class ArtworkCatalog : ScriptableObject
    {
        [SerializeField] private List<ArtworkDefinition> definitions = new List<ArtworkDefinition>();
        public IReadOnlyList<ArtworkDefinition> Definitions => definitions.AsReadOnly();
        public ArtworkDefinition Find(string id) => definitions.Find(d => d != null && d.id == id);

        public List<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>();
            foreach (var d in definitions)
                if (d == null || string.IsNullOrWhiteSpace(d.id) || !ids.Add(d.id) || d.prefab == null)
                    errors.Add("Catalog entries need unique nonempty IDs and prefab assets.");
            return errors;
        }
    }
}
