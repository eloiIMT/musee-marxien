using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualMuseum
{
    [Serializable]
    public sealed class MuseumConfiguration
    {
        public string name = "Virtual Museum";
    }

    [Serializable]
    public sealed class Museum
    {
        public MuseumConfiguration configuration = new MuseumConfiguration();
        public List<Room> rooms = new List<Room>();
        public List<ArtworkInstance> artworks = new List<ArtworkInstance>();
        public List<VisitorTour> tours = new List<VisitorTour>();
    }

    [Serializable]
    public sealed class Room
    {
        public string id;
        public string name;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public float width = 6, length = 6, height = 3;
        public List<string> connections = new List<string>();
    }

    [Serializable]
    public sealed class ArtworkInstance
    {
        public string id;
        public string definitionId;
        public string roomId;
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
        public string title;
        public string description;
    }

    [Serializable]
    public sealed class VisitorTour
    {
        public string id;
        public string name;
        public string description;
        public List<string> artworkIds = new List<string>();
    }

    [Serializable]
    public sealed class VisitorSession
    {
        public string tourId;
        // Count means completed; otherwise this is the current artwork's zero-based index.
        public int index;
    }

    public enum MuseumEntity { Museum, Room, Artwork, Tour }
    public enum MuseumOperation { Reset, Added, Updated, Removed }

    public readonly struct MuseumChange
    {
        public readonly MuseumEntity Entity;
        public readonly MuseumOperation Operation;
        public readonly string Id;
        public MuseumChange(MuseumEntity entity, MuseumOperation operation, string id)
        { Entity = entity; Operation = operation; Id = id; }
    }
}
