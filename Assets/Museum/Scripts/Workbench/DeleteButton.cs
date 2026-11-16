using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Red gizmo cube: first trigger click asks for confirmation (shows how many artworks will be lost),
    /// a second click within 3 s deletes the room.
    /// </summary>
    public class DeleteButton : MonoBehaviour
    {
        const float ConfirmWindow = 3f;

        Maquette maquette;
        string pendingRoomId;
        float deadline;

        public void Init(Maquette owner)
        {
            maquette = owner;
            gameObject.AddComponent<Clickable>().Clicked += (_, _) => OnClick();
        }

        void OnClick()
        {
            var id = maquette.SelectedRoomId;
            if (id == null) return;
            var room = maquette.Api.GetRoom(id);
            if (room == null) return;

            if (pendingRoomId == id && Time.time <= deadline)
            {
                pendingRoomId = null;
                maquette.SelectRoom(null); // detach the gizmo before the room view is destroyed
                maquette.Api.DeleteRoom(id);
                maquette.Toast($"« {room.name} » supprimée");
                return;
            }
            pendingRoomId = id;
            deadline = Time.time + ConfirmWindow;
            int count = maquette.Api.GetArtworksInRoom(id).Count;
            maquette.Toast($"Supprimer « {room.name} »{(count > 0 ? $" et ses {count} œuvre(s)" : "")} ?\nCliquez à nouveau pour confirmer", true);
        }
    }
}
