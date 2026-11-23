using System.IO;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VirtualMuseum.App
{
    [RequireComponent(typeof(Clickable))]
    public sealed class GachaDispenser : MonoBehaviour
    {
        [SerializeField] Transform capsuleSpawnPoint;
        [SerializeField] Material capsuleMaterial;
        [SerializeField] float capsuleDiameter = 0.12f;

        Clickable button;
        int capsuleCount;
        GameObject waitingCapsule;
        MuseumBackend backend;
        bool museumReady;

        void Awake()
        {
            button = GetComponent<Clickable>();
            if (TriggerRouter.Instance == null && FindAnyObjectByType<TriggerRouter>() == null)
                new GameObject("Trigger Router").AddComponent<TriggerRouter>();

            var tray = transform.parent != null ? transform.parent.Find("Capsule Output Tray") : null;
            if (tray != null && tray.GetComponent<Collider>() == null)
                tray.gameObject.AddComponent<BoxCollider>();
        }

        void OnEnable()
        {
            if (button == null) button = GetComponent<Clickable>();
            if (button != null) button.Clicked += OnClicked;
        }

        void OnDisable()
        {
            if (button != null) button.Clicked -= OnClicked;
        }

        void Start()
        {
            backend = FindAnyObjectByType<MuseumBackend>();
            if (backend == null)
            {
                Debug.LogError("Gacha dispenser requires a MuseumBackend to register artwork.", this);
                return;
            }

            var savePath = MuseumPersistence.DefaultPath;
            if (File.Exists(savePath) && !backend.Load(out var error))
            {
                Debug.LogError("Could not load the saved museum before using the Gacha dispenser: " + error, this);
                return;
            }

            museumReady = true;
        }

        void OnClicked(Clickable _, IXRInteractor __) => Dispense();

        public void Dispense()
        {
            if (capsuleSpawnPoint == null)
            {
                Debug.LogError("Gacha dispenser has no capsule spawn point.", this);
                return;
            }
            if (capsuleMaterial == null)
            {
                Debug.LogError("Gacha dispenser has no capsule material.", this);
                return;
            }
            if (waitingCapsule != null) return;

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            capsule.name = $"Gacha Capsule {++capsuleCount}";
            capsule.transform.SetPositionAndRotation(capsuleSpawnPoint.position, Quaternion.identity);
            capsule.transform.localScale = Vector3.one * capsuleDiameter;
            capsule.GetComponent<Renderer>().sharedMaterial = capsuleMaterial;
            var body = capsule.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var grab = capsule.AddComponent<XRGrabInteractable>();
            capsule.AddComponent<GachaCapsuleTriggerGrab>();
            waitingCapsule = capsule;
            var converted = false;
            grab.selectEntered.AddListener(_ =>
            {
                if (waitingCapsule == capsule) waitingCapsule = null;
                if (!converted && ConvertToArtwork(capsule)) converted = true;
            });
        }

        bool ConvertToArtwork(GameObject capsule)
        {
            if (capsule == null) return false;
            if (!museumReady)
            {
                Debug.LogError("Gacha capsule cannot become an artwork because the museum is not ready.", this);
                return false;
            }

            var definitions = backend.Catalog.Definitions;
            if (definitions.Count == 0)
            {
                Debug.LogError("The artwork catalog is empty.", backend);
                return false;
            }

            var definition = definitions[Random.Range(0, definitions.Count)];
            if (definition == null || definition.prefab == null)
            {
                Debug.LogError("The selected artwork definition has no prefab.", backend);
                return false;
            }

            var api = backend.Api;
            if (!TryFindArtworkPlacement(api.Data, out var room, out var localPosition))
            {
                Debug.LogError("No museum room has an available place for the Gacha artwork.", backend);
                return false;
            }

            var visual = Instantiate(definition.prefab, capsule.transform, false);
            visual.name = definition.title;
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            visual.transform.localScale = Vector3.one * 0.25f;
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            var artwork = api.AddArtwork(definition.id, room.id, localPosition);
            if (!backend.Save(out var error))
            {
                api.RemoveArtwork(artwork.id);
                Destroy(visual);
                Debug.LogError("Gacha artwork was not saved; the capsule was not converted: " + error, backend);
                return false;
            }

            var capsuleRenderer = capsule.GetComponent<Renderer>();
            if (capsuleRenderer != null) capsuleRenderer.enabled = false;
            Debug.Log($"Gacha capsule became '{definition.title}' and was saved to the museum.", this);
            return true;
        }

        static bool TryFindArtworkPlacement(Museum museum, out Room room, out Vector3 localPosition)
        {
            foreach (var candidateRoom in museum.rooms)
            {
                var minX = -candidateRoom.width / 2f + 0.75f;
                var maxX = candidateRoom.width / 2f - 0.75f;
                var minZ = -candidateRoom.length / 2f + 0.75f;
                var maxZ = candidateRoom.length / 2f - 0.75f;
                if (minX > maxX) minX = maxX = 0f;
                if (minZ > maxZ) minZ = maxZ = 0f;

                for (var z = minZ; z <= maxZ + 0.001f; z += 1f)
                {
                    for (var x = minX; x <= maxX + 0.001f; x += 1f)
                    {
                        var position = new Vector3(x, 1f, z);
                        var occupied = false;
                        foreach (var existing in museum.artworks)
                        {
                            if (existing.roomId != candidateRoom.id)
                                continue;

                            var delta = new Vector2(existing.localPosition.x - x, existing.localPosition.z - z);
                            if (delta.magnitude < ArtworkHandle.ArtworkMargin)
                            {
                                occupied = true;
                                break;
                            }
                        }

                        if (occupied) continue;
                        room = candidateRoom;
                        localPosition = position;
                        return true;
                    }
                }
            }

            room = null;
            localPosition = Vector3.zero;
            return false;
        }
    }
}
