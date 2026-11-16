using System;
using System.Collections.Generic;
using System.Linq;
using static VirtualMuseum.MuseumSpatial;

namespace VirtualMuseum
{
    public static class MuseumValidation
    {
        public static List<string> Validate(Museum museum, VisitorSession session, ArtworkCatalog catalog)
        {
            var errors = catalog == null ? new List<string> { "Missing catalog." } : catalog.Validate();
            if (museum == null || museum.configuration == null || museum.rooms == null || museum.artworks == null || museum.tours == null)
            { errors.Add("Missing museum configuration or collections."); return errors; }
            var ids = new HashSet<string>();
            foreach (var r in museum.rooms)
            {
                if (r == null) { errors.Add("Null room."); continue; }
                CheckId(r.id, ids, errors);
                if (!Finite(r.position) || !ValidRotation(r.rotation) || !Positive(new UnityEngine.Vector3(r.width, r.height, r.length)))
                    errors.Add("Invalid room transform or dimensions: " + r.id);
                if (r.connections == null) { errors.Add("Missing connections: " + r.id); continue; }
                var neighbours = new HashSet<string>();
                foreach (var id in r.connections)
                {
                    var other = museum.rooms.Find(n => n != null && n.id == id);
                    if (!neighbours.Add(id) || id == r.id || other == null || other.connections == null || !other.connections.Contains(r.id))
                        errors.Add("Invalid bidirectional connection: " + r.id);
                }
            }
            foreach (var a in museum.artworks)
            {
                if (a == null) { errors.Add("Null artwork."); continue; }
                CheckId(a.id, ids, errors);
                if (!museum.rooms.Any(r => r != null && r.id == a.roomId) || catalog == null || catalog.Find(a.definitionId) == null)
                    errors.Add("Missing room or artwork definition: " + a.id);
                if (!Finite(a.localPosition) || !ValidRotation(a.localRotation) || !Positive(a.localScale))
                    errors.Add("Invalid artwork transform: " + a.id);
            }
            foreach (var t in museum.tours)
            {
                if (t == null) { errors.Add("Null tour."); continue; }
                CheckId(t.id, ids, errors);
                if (t.artworkIds == null || t.artworkIds.Any(id => !museum.artworks.Any(a => a != null && a.id == id)))
                    errors.Add("Missing tour artwork: " + t.id);
            }
            errors.AddRange(ValidateSession(museum, session));
            return errors;
        }
        private static void CheckId(string id, HashSet<string> ids, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) errors.Add("Empty or duplicate entity ID: " + id);
        }
        public static List<string> ValidateSession(Museum museum, VisitorSession session)
        {
            var errors = new List<string>();
            if (session == null) { errors.Add("Missing session."); return errors; }
            if (string.IsNullOrEmpty(session.tourId))
            {
                if (session.index != 0) errors.Add("Unselected session must have index zero.");
                return errors;
            }
            var tour = museum?.tours?.Find(t => t != null && t.id == session.tourId);
            if (tour == null || tour.artworkIds == null || session.index < 0 || session.index > tour.artworkIds.Count)
                errors.Add("Invalid visitor session tour or index.");
            return errors;
        }
    }
}
