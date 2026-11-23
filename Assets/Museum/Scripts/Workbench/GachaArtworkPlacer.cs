using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Opened gacha capsule holding an artwork miniature that is not in the museum yet (spec 2.6).
    /// While held in front of the plan board, a ghost shows where it would land (green / red); releasing it there adds
    /// the artwork to the room under it (<see cref="MuseumApi.AddArtwork"/>) and saves the museum. Released anywhere
    /// else, the capsule simply drops and can be grabbed again.
    ///  - any room of the plan, the room then gets selected so the new artwork shows in 3D
    ///  - turning the wrist rotates it by 15°; within 0.5 m of a wall it snaps to the wall, facing into the room
    ///  - closer than 0.8 m to another artwork = red, release keeps the capsule in the hand's world
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class GachaArtworkPlacer : MonoBehaviour
    {
        const float GhostSize = 0.6f;
        const float ArtworkHeight = 1f;
        /// <summary>How close (world metres) to the board the capsule must be to aim at the plan.</summary>
        const float MaxBoardDistance = 0.3f;

        ArtworkDefinition definition;
        MuseumBackend backend;
        Maquette maquette;
        XRGrabInteractable grab;
        bool ghostShown;

        // Last preview
        Room targetRoom;
        Vector3 local;
        float localYaw;
        string reason;

        public ArtworkDefinition Definition => definition;

        public void Init(ArtworkDefinition artwork, MuseumBackend museumBackend)
        {
            definition = artwork;
            backend = museumBackend;
        }

        void Awake() => grab = GetComponent<XRGrabInteractable>();

        void OnEnable() => grab.selectExited.AddListener(OnReleased);

        void OnDisable()
        {
            grab.selectExited.RemoveListener(OnReleased);
            HideGhost();
        }

        void Update()
        {
            if (definition == null || !grab.isSelected) { HideGhost(); return; }
            if (!Preview()) HideGhost();
        }

        /// <summary>Compute where the artwork would land and show the ghost. False when the capsule is not near the plan.</summary>
        bool Preview()
        {
            targetRoom = null;
            reason = null;
            if (maquette == null) maquette = FindAnyObjectByType<Maquette>();
            if (maquette == null || !maquette.isActiveAndEnabled) return false;

            var p = transform.position;
            float height = maquette.MuseumHeight(p) * maquette.Scale; // world distance in front of the board
            if (height < -0.05f || height > MaxBoardDistance || !maquette.IsOverBoard(p)) return false;

            var museum = maquette.Api.Data;
            var point = maquette.HandToMuseum(p);
            var roomId = MuseumGeometry.RoomAt(museum, point);
            if (roomId == null)
            {
                reason = "Relâchez dans une salle";
                ShowGhost(point, 0f, false, reason, p);
                return true;
            }

            targetRoom = museum.rooms.Find(r => r.id == roomId);
            local = MuseumSpatial.ToRoom(targetRoom, point);
            local.y = ArtworkHeight;
            if (!MuseumGeometry.SnapToWall(targetRoom.width, targetRoom.length, ref local, out localYaw))
                localYaw = MuseumGeometry.SnapAngle(maquette.HandTwist(transform.rotation));

            foreach (var other in museum.artworks)
            {
                if (other.roomId != targetRoom.id) continue;
                var d = other.localPosition - local;
                d.y = 0f;
                if (d.magnitude < ArtworkHandle.ArtworkMargin) { reason = "Trop proche d'une autre œuvre (0,8 m min.)"; break; }
            }

            var center = MuseumSpatial.ToMuseum(targetRoom, new Vector3(local.x, 0f, local.z));
            ShowGhost(center, MuseumGeometry.Yaw(targetRoom.rotation) + localYaw, reason == null,
                      reason ?? $"{definition.title} → {targetRoom.name}", p);
            return true;
        }

        void ShowGhost(Vector3 center, float yaw, bool valid, string text, Vector3 hand)
        {
            maquette.Ghost.Show(center, yaw, GhostSize, GhostSize, valid, text, hand);
            ghostShown = true;
        }

        void HideGhost()
        {
            if (!ghostShown) return;
            ghostShown = false;
            if (maquette != null) maquette.Ghost.Hide();
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (definition == null) return;
            bool aimed = !args.isCanceled && Preview();
            HideGhost();
            if (!aimed) return; // not over the plan: the capsule just drops
            if (targetRoom == null || reason != null)
            {
                maquette.Toast((reason ?? "Placement refusé") + " : œuvre non placée", true);
                return;
            }

            var api = maquette.Api;
            var artwork = api.AddArtwork(definition.id, targetRoom.id, local);
            api.SetArtworkTransform(artwork.id, local, Quaternion.Euler(0f, localYaw, 0f), artwork.localScale);
            if (!backend.Save(out var error))
            {
                api.RemoveArtwork(artwork.id);
                maquette.Toast("Œuvre non sauvegardée : " + error, true);
                return;
            }

            maquette.SelectRoom(targetRoom.id); // show the room in 3D so the new artwork is visible and editable
            maquette.Toast($"« {definition.title} » placée dans « {targetRoom.name} »");
            definition = null;
            Destroy(gameObject);
        }
    }
}
