using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static VirtualMuseum.MuseumSpatial;

namespace VirtualMuseum
{
    /// <summary>All queries return snapshots. Edit through methods to validate data and notify subscribers.</summary>
    public sealed class MuseumApi
    {
        private Museum museum = new Museum();
        private VisitorSession session = new VisitorSession();
        public ArtworkCatalog Catalog { get; }
        public Museum Data => Copy(museum);
        public VisitorSession Session => Copy(session);
        public event Action<MuseumChange> Changed;
        public event Action SessionChanged;

        public MuseumApi(ArtworkCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var errors = catalog.Validate();
            Require(errors.Count == 0, string.Join("\n", errors));
            Catalog = catalog;
        }
        internal static T Copy<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        private static string NewId() => Guid.NewGuid().ToString("N");
        private Room RoomRef(string id) => museum.rooms.Find(r => r.id == id) ?? throw new ArgumentException("Unknown room: " + id);
        private ArtworkInstance ArtworkRef(string id) => museum.artworks.Find(a => a.id == id) ?? throw new ArgumentException("Unknown artwork: " + id);
        private VisitorTour TourRef(string id) => museum.tours.Find(t => t.id == id) ?? throw new ArgumentException("Unknown tour: " + id);
        private void Notify(MuseumEntity entity, MuseumOperation operation, string id) => Changed?.Invoke(new MuseumChange(entity, operation, id));
        public Room GetRoom(string id) => museum.rooms.Find(r => r.id == id) is Room r ? Copy(r) : null;
        public ArtworkInstance GetArtwork(string id) => museum.artworks.Find(a => a.id == id) is ArtworkInstance a ? Copy(a) : null;
        public VisitorTour GetTour(string id) => museum.tours.Find(t => t.id == id) is VisitorTour t ? Copy(t) : null;
        public IReadOnlyList<Room> GetConnectedRooms(string roomId) => RoomRef(roomId).connections.Select(id => Copy(RoomRef(id))).ToList();
        public IReadOnlyList<ArtworkInstance> GetArtworksInRoom(string roomId)
        {
            RoomRef(roomId);
            return museum.artworks.Where(a => a.roomId == roomId).Select(Copy).ToList();
        }
        public IReadOnlyList<ArtworkInstance> GetTourArtworks(string tourId) => TourRef(tourId).artworkIds.Select(id => Copy(ArtworkRef(id))).ToList();

        public void RenameMuseum(string name)
        {
            museum.configuration.name = name ?? "";
            Notify(MuseumEntity.Museum, MuseumOperation.Updated, null);
        }
        public Room CreateRoom(string name, Vector3 position, float width = 6, float length = 6, float height = 3)
        {
            Require(Finite(position) && Positive(new Vector3(width, height, length)), "Room position must be finite and dimensions positive.");
            var r = new Room { id = NewId(), name = name ?? "", position = position, width = width, length = length, height = height };
            museum.rooms.Add(r);
            Notify(MuseumEntity.Room, MuseumOperation.Added, r.id);
            return Copy(r);
        }
        public void RenameRoom(string id, string name)
        {
            RoomRef(id).name = name ?? "";
            Notify(MuseumEntity.Room, MuseumOperation.Updated, id);
        }
        public void SetRoomTransform(string id, Vector3 position, Quaternion rotation)
        {
            Require(Finite(position) && ValidRotation(rotation), "Use finite positions and unit quaternions.");
            var r = RoomRef(id); r.position = position; r.rotation = rotation;
            Notify(MuseumEntity.Room, MuseumOperation.Updated, id);
        }
        public void ResizeRoom(string id, float width, float length, float height)
        {
            Require(Positive(new Vector3(width, height, length)), "Dimensions must be finite and positive.");
            var r = RoomRef(id); r.width = width; r.length = length; r.height = height;
            Notify(MuseumEntity.Room, MuseumOperation.Updated, id);
        }
        public void ConnectRooms(string a, string b) => SetConnection(a, b, true);
        public void DisconnectRooms(string a, string b) => SetConnection(a, b, false);
        private void SetConnection(string a, string b, bool connected)
        {
            Require(a != b, "Cannot connect a room to itself.");
            var first = RoomRef(a); var second = RoomRef(b);
            if (first.connections.Contains(b) == connected) return;
            if (connected) { first.connections.Add(b); second.connections.Add(a); }
            else { first.connections.Remove(b); second.connections.Remove(a); }
            Notify(MuseumEntity.Room, MuseumOperation.Updated, a);
            Notify(MuseumEntity.Room, MuseumOperation.Updated, b);
        }
        public void DeleteRoom(string id)
        {
            var room = RoomRef(id);
            var removed = museum.artworks.Where(a => a.roomId == id).Select(a => a.id).ToList();
            var neighbours = room.connections.ToList();
            museum.rooms.Remove(room);
            museum.artworks.RemoveAll(a => removed.Contains(a.id));
            foreach (var r in museum.rooms) r.connections.Remove(id);
            PruneTours(removed);
            foreach (var a in removed) Notify(MuseumEntity.Artwork, MuseumOperation.Removed, a);
            foreach (var n in neighbours) Notify(MuseumEntity.Room, MuseumOperation.Updated, n);
            Notify(MuseumEntity.Room, MuseumOperation.Removed, id);
        }

        public ArtworkInstance AddArtwork(string definitionId, string roomId, Vector3 localPosition)
        {
            RoomRef(roomId);
            Require(Finite(localPosition), "Position must be finite.");
            var d = Catalog.Find(definitionId) ?? throw new ArgumentException("Unknown artwork definition: " + definitionId);
            var a = new ArtworkInstance { id = NewId(), definitionId = d.id, roomId = roomId,
                localPosition = localPosition, title = d.title ?? "", description = d.description ?? "" };
            museum.artworks.Add(a);
            Notify(MuseumEntity.Artwork, MuseumOperation.Added, a.id);
            return Copy(a);
        }
        public void RemoveArtwork(string id)
        {
            museum.artworks.Remove(ArtworkRef(id));
            PruneTours(new List<string> { id });
            Notify(MuseumEntity.Artwork, MuseumOperation.Removed, id);
        }
        public void SetArtworkTransform(string id, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            Require(Finite(localPosition) && ValidRotation(localRotation) && Positive(localScale), "Use finite positions, unit quaternions and positive scales.");
            var a = ArtworkRef(id); a.localPosition = localPosition; a.localRotation = localRotation; a.localScale = localScale;
            Notify(MuseumEntity.Artwork, MuseumOperation.Updated, id);
        }
        /// <summary>Preserves museum position/orientation by default; scale is unchanged (rooms never scale).</summary>
        public void TransferArtwork(string id, string roomId, bool preserveMuseumTransform = true)
        {
            var a = ArtworkRef(id); var target = RoomRef(roomId); var source = RoomRef(a.roomId);
            if (preserveMuseumTransform)
            {
                a.localPosition = ToRoom(target, ToMuseum(source, a.localPosition));
                a.localRotation = Quaternion.Inverse(target.rotation) * source.rotation * a.localRotation;
            }
            a.roomId = roomId;
            Notify(MuseumEntity.Artwork, MuseumOperation.Updated, id);
        }
        public void EditArtwork(string id, string title, string description)
        {
            var a = ArtworkRef(id); a.title = title ?? ""; a.description = description ?? "";
            Notify(MuseumEntity.Artwork, MuseumOperation.Updated, id);
        }
        public VisitorTour CreateTour(string name, string description = "")
        {
            var t = new VisitorTour { id = NewId(), name = name ?? "", description = description ?? "" };
            museum.tours.Add(t);
            Notify(MuseumEntity.Tour, MuseumOperation.Added, t.id);
            return Copy(t);
        }
        public void EditTour(string id, string name, string description)
        {
            var t = TourRef(id); t.name = name ?? ""; t.description = description ?? "";
            Notify(MuseumEntity.Tour, MuseumOperation.Updated, id);
        }
        public void DeleteTour(string id)
        {
            museum.tours.Remove(TourRef(id));
            if (session.tourId == id) { session = new VisitorSession(); SessionChanged?.Invoke(); }
            Notify(MuseumEntity.Tour, MuseumOperation.Removed, id);
        }
        public void AddTourArtwork(string tourId, string artworkId, int insertAt = -1)
        {
            var t = TourRef(tourId); ArtworkRef(artworkId);
            if (insertAt == -1) insertAt = t.artworkIds.Count;
            Require(insertAt >= 0 && insertAt <= t.artworkIds.Count, "Invalid insertion index.");
            t.artworkIds.Insert(insertAt, artworkId); TourContentsChanged(tourId);
        }
        public void RemoveTourArtwork(string tourId, int index)
        {
            var t = TourRef(tourId);
            Require(index >= 0 && index < t.artworkIds.Count, "Invalid artwork index.");
            t.artworkIds.RemoveAt(index); TourContentsChanged(tourId);
        }
        /// <summary>toIndex is the final zero-based index, including when moving forward.</summary>
        public void ReorderTourArtwork(string tourId, int fromIndex, int toIndex)
        {
            var t = TourRef(tourId);
            Require(fromIndex >= 0 && fromIndex < t.artworkIds.Count && toIndex >= 0 && toIndex < t.artworkIds.Count, "Invalid artwork index.");
            var id = t.artworkIds[fromIndex]; t.artworkIds.RemoveAt(fromIndex); t.artworkIds.Insert(toIndex, id);
            TourContentsChanged(tourId);
        }
        private void PruneTours(List<string> removed)
        {
            var affected = museum.tours.Where(t => t.artworkIds.RemoveAll(removed.Contains) > 0).Select(t => t.id).ToList();
            // Finish all pruning before any observer can query the museum.
            if (affected.Contains(session.tourId)) { session.index = 0; SessionChanged?.Invoke(); }
            foreach (var id in affected) Notify(MuseumEntity.Tour, MuseumOperation.Updated, id);
        }
        private void TourContentsChanged(string id)
        {
            if (session.tourId == id) { session.index = 0; SessionChanged?.Invoke(); }
            Notify(MuseumEntity.Tour, MuseumOperation.Updated, id);
        }

        public void StartTour(string id) { TourRef(id); session = new VisitorSession { tourId = id }; SessionChanged?.Invoke(); }
        public void ResetTour() { session.index = 0; SessionChanged?.Invoke(); }
        public int TourLength => string.IsNullOrEmpty(session.tourId) ? 0 : TourRef(session.tourId).artworkIds.Count;
        public bool HasSelectedTour => !string.IsNullOrEmpty(session.tourId);
        public bool IsComplete => HasSelectedTour && session.index == TourLength;
        public float Progress => !HasSelectedTour ? 0 : TourLength == 0 ? 1 : (float)session.index / TourLength;
        private ArtworkInstance SessionArtwork(int index)
        {
            if (!HasSelectedTour || index < 0 || index >= TourLength) return null;
            return GetArtwork(TourRef(session.tourId).artworkIds[index]);
        }
        public ArtworkInstance CurrentArtwork => SessionArtwork(session.index);
        public ArtworkInstance PreviousArtwork => SessionArtwork(session.index - 1);
        public ArtworkInstance NextArtwork => SessionArtwork(session.index + 1);
        public bool Advance()
        {
            if (!HasSelectedTour || IsComplete) return false;
            session.index++; SessionChanged?.Invoke(); return true;
        }
        public bool GoBack()
        {
            if (!HasSelectedTour || session.index == 0) return false;
            session.index--; SessionChanged?.Invoke(); return true;
        }
        public void RestoreSession(VisitorSession restored)
        {
            var errors = MuseumValidation.ValidateSession(museum, restored);
            Require(errors.Count == 0, string.Join("\n", errors));
            session = Copy(restored); SessionChanged?.Invoke();
        }
        public List<string> Validate() => MuseumValidation.Validate(museum, session, Catalog);
        public void Replace(Museum data, VisitorSession restored)
        {
            var errors = MuseumValidation.Validate(data, restored, Catalog);
            Require(errors.Count == 0, string.Join("\n", errors));
            museum = Copy(data); session = Copy(restored);
            Notify(MuseumEntity.Museum, MuseumOperation.Reset, null); SessionChanged?.Invoke();
        }
        public void ResetToExample()
        {
            var example = MuseumExample.Create(Catalog);
            Replace(example.Data, example.Session);
        }
    }
}
