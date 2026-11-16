using System;
using UnityEngine;

namespace VirtualMuseum
{
    public static class MuseumExample
    {
        public static MuseumApi Create(ArtworkCatalog catalog)
        {
            if (catalog == null || catalog.Definitions.Count < 5) throw new ArgumentException("The example requires at least five catalog definitions.");
            var api = new MuseumApi(catalog);
            var a = api.CreateRoom("Welcome Gallery", Vector3.zero, 8, 6, 3);
            var b = api.CreateRoom("Sculpture Hall", new Vector3(9, 0, 0), 10, 8, 4);
            var c = api.CreateRoom("Small Collection", new Vector3(0, 0, 8), 6, 10, 3.5f);
            api.ConnectRooms(a.id, b.id); api.ConnectRooms(a.id, c.id); api.ConnectRooms(b.id, c.id);
            var all = new string[6];
            for (int i = 0; i < all.Length; i++)
            {
                var room = i < 2 ? a : i < 4 ? b : c;
                all[i] = api.AddArtwork(catalog.Definitions[i % catalog.Definitions.Count].id, room.id, new Vector3(i % 2 == 0 ? -1.5f : 1.5f, 1, 0)).id;
            }
            var highlights = api.CreateTour("Highlights", "Three stops across the museum.");
            foreach (int i in new[] { 0, 2, 4 }) api.AddTourArtwork(highlights.id, all[i]);
            var collection = api.CreateTour("Full Collection", "All six exhibits in a different order.");
            foreach (int i in new[] { 1, 0, 3, 2, 5, 4 }) api.AddTourArtwork(collection.id, all[i]);
            api.StartTour(highlights.id);
            return api;
        }
    }
}
