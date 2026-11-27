using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Opened gacha capsule holding an artwork miniature that is not in the museum yet (spec 2.6).
    /// While held in front of the plan board, a ghost shows where it would land (green / red); releasing it there adds
    /// the artwork to the room under it (<see cref="MuseumApi.AddArtwork"/>) and saves the museum. Released in the blue
    /// bin, or on the plan outside every room, it is stored there (<see cref="ArtworkBin"/>); anywhere else, the capsule
    /// simply drops and can be grabbed
    /// again. Also drives the miniatures taken from the bin (<see cref="TakeFromBin"/>), which go back to it when not
    /// placed.
    ///  - any room of the plan, the room then gets selected so the new artwork shows in 3D
    ///  - it faces the room's wall plan by default (where the visitor arrives, <see cref="MuseumStation"/>); turning the
    ///    wrist from there rotates it by 15° steps; within 0.5 m of a wall it snaps to the wall, facing into the room
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
        Transform visual;
        Bounds visualBounds;
        bool ghostShown;
        ArtworkBin bin;          // set for a miniature taken from the bin
        string binEntryId;

        // Last preview
        Room targetRoom;
        Vector3 local;
        float localYaw;
        string reason;
        float grabTwist; // wrist twist when grabbed: the artwork turns by the twist since then

        public ArtworkDefinition Definition => definition;

        /// <summary>False while the artwork is still shut in the capsule (see <see cref="Open"/>).</summary>
        public bool Opened { get; private set; }

        public bool IsHeld => grab != null && grab.isSelected;

        /// <summary>World size of the artwork miniature (zero without one).</summary>
        public Vector3 VisualWorldSize => visual == null ? Vector3.zero : visualBounds.size * visual.lossyScale.x;

        /// <summary>This miniature is the bin's entry <paramref name="entryId"/>: it leaves the bin once placed.</summary>
        public void TakeFromBin(ArtworkBin owner, string entryId)
        {
            bin = owner;
            binEntryId = entryId;
        }

        /// <param name="artworkVisual">Artwork miniature, child of the capsule, at scale 1 with no offset.</param>
        public void Init(ArtworkDefinition artwork, MuseumBackend museumBackend, Transform artworkVisual = null)
        {
            definition = artwork;
            backend = museumBackend;
            visual = artworkVisual;
            if (visual != null) visualBounds = LocalBounds(visual);
            MatchPlanScale();
        }

        /// <summary>The capsule was grabbed: the artwork no longer has to fit inside it.</summary>
        public void Open()
        {
            Opened = true;
            MatchPlanScale();
        }

        void Awake() => grab = GetComponent<XRGrabInteractable>();

        /// <summary>
        /// Same world size as the artworks drawn on the plan (prefab at scale 1 under the maquette, see RoomView),
        /// but while the capsule is closed never bigger than its inside. Centred on the capsule. Re-applied every
        /// frame: the maquette rescales itself when rooms change.
        /// </summary>
        void MatchPlanScale()
        {
            if (visual == null) return;
            if (maquette == null) maquette = FindAnyObjectByType<Maquette>();
            float fit = 2f * GachaCapsuleMesh.InnerRadius / Mathf.Max(visualBounds.size.magnitude, 1e-4f);
            float scale = fit;
            if (maquette != null)
            {
                scale = maquette.Scale / Mathf.Max(transform.lossyScale.x, 1e-5f);
                if (!Opened) scale = Mathf.Min(scale, fit);
            }
            var localScale = Vector3.one * scale;
            if (visual.localScale != localScale) visual.localScale = localScale;
            visual.localPosition = -visualBounds.center * scale;
        }

        /// <summary>Bounds of the renderers under <paramref name="root"/>, in its own space.</summary>
        static Bounds LocalBounds(Transform root)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = root.InverseTransformPoint(new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    if (any) bounds.Encapsulate(corner);
                    else { bounds = new Bounds(corner, Vector3.zero); any = true; }
                }
            }
            return bounds;
        }

        void OnEnable()
        {
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            if (maquette == null) maquette = FindAnyObjectByType<Maquette>();
            grabTwist = maquette != null ? maquette.HandTwist(transform.rotation) : 0f;
        }

        void OnDisable()
        {
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnReleased);
            HideGhost();
        }

        void Update()
        {
            MatchPlanScale();
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
                maquette.SetDropTarget(null);
                maquette.ShowReservedZone(null);
                if (ArtworkBin.Active == null) { ShowGhost(point, 0f, false, reason, p); return true; }
                // On the plan but outside every room: released there, it goes to the bin (a bin miniature goes back).
                maquette.Ghost.Show(point, 0f, GhostSize, GhostSize, true, $"Relâcher : ranger « {definition.title} » dans le bac",
                                    p, colorOverride: Maquette.HeldColor);
                ghostShown = true;
                return true;
            }

            targetRoom = museum.rooms.Find(r => r.id == roomId);
            maquette.SetDropTarget(roomId); // the room it goes to shows in 3D
            local = MuseumSpatial.ToRoom(targetRoom, point);
            local.y = ArtworkHeight;
            if (!MuseumGeometry.SnapToWall(targetRoom.width, targetRoom.length, ref local, out localYaw))
                localYaw = MuseumGeometry.SnapAngle(MuseumStation.FacePlanYaw(targetRoom, museum, local)
                                                    + Mathf.DeltaAngle(grabTwist, maquette.HandTwist(transform.rotation)));

            foreach (var other in museum.artworks)
            {
                if (other.roomId != targetRoom.id) continue;
                var d = other.localPosition - local;
                d.y = 0f;
                if (d.magnitude < ArtworkHandle.ArtworkMargin) { reason = "Trop proche d'une autre œuvre (0,8 m min.)"; break; }
            }
            bool reserved = MuseumStation.IsReserved(targetRoom, museum, local);
            if (reason == null && reserved) reason = MuseumStation.ReservedReason;
            if (reason == null && MuseumStation.InFrontOfDoor(targetRoom, museum, local)) reason = MuseumStation.DoorReason;
            maquette.ShowReservedZone(reserved ? targetRoom.id : null); // the no-go area shows only while aimed at

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
            if (maquette != null) maquette.ShowReservedZone(null);
            if (!ghostShown) return; // only the placer that showed the preview clears it (others call this every frame)
            ghostShown = false;
            if (maquette != null) maquette.Ghost.Hide();
            if (maquette != null) maquette.SetDropTarget(null);
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (definition == null) return;
            bool aimed = !args.isCanceled && Preview();
            HideGhost();
            if (aimed && targetRoom == null && ArtworkBin.Active != null)
            {
                // Outside every room: into the bin. A bin miniature simply goes back to its place (ArtworkBin.Update).
                if (bin == null && ArtworkBin.Active.Store(definition))
                {
                    definition = null;
                    Destroy(gameObject);
                }
                return;
            }
            if (aimed && (targetRoom == null || reason != null))
                maquette.Toast((reason ?? "Placement refusé") + " : œuvre non placée", true);
            if (!aimed || targetRoom == null || reason != null)
            {
                // Not placed. A bin miniature goes back to its place (ArtworkBin.Update); a capsule let go in the bin
                // is stored there; anywhere else the capsule just drops.
                var target = ArtworkBin.Active;
                if (bin == null && !args.isCanceled && target != null && target.Contains(transform.position)
                    && target.Store(definition))
                {
                    definition = null;
                    Destroy(gameObject);
                }
                return;
            }

            var api = maquette.Api;
            var artwork = api.AddArtwork(definition.id, targetRoom.id, local);
            api.SetArtworkTransform(artwork.id, local, Quaternion.Euler(0f, localYaw, 0f), artwork.localScale);
            if (bin != null) bin.ApplyStoredTexts(binEntryId, artwork.id); // title / description edited before storing
            if (!backend.Save(out var error))
            {
                api.RemoveArtwork(artwork.id);
                maquette.Toast("Œuvre non sauvegardée : " + error, true);
                return;
            }
            if (bin != null) bin.Remove(binEntryId);

            maquette.SelectRoom(targetRoom.id); // show the room in 3D so the new artwork is visible and editable
            maquette.Toast($"« {definition.title} » placée dans « {targetRoom.name} »");
            definition = null;
            Destroy(gameObject);
        }
    }
}
