using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Makes the gacha capsules. <see cref="GachaCrank"/> calls <see cref="Dispense"/> after a full turn of the knob:
    /// the capsule appears inside the machine's chute (<see cref="capsuleSpawnPoint"/>), rolls out and drops into the
    /// output cup (<see cref="capsuleRestPoint"/>), where it waits (kinematic) until it is grabbed. Only one capsule
    /// waits at a time. Grabbing it opens it (<see cref="ConvertToArtwork"/>).
    /// </summary>
    public sealed class GachaDispenser : MonoBehaviour
    {
        [SerializeField] Transform capsuleSpawnPoint;
        [Tooltip("Where the capsule ends in the output cup. Empty: the capsule just falls from the spawn point with physics.")]
        [SerializeField] Transform capsuleRestPoint;
        [Tooltip("Bottom half of the capsule (opaque coloured plastic).")]
        [FormerlySerializedAs("capsuleMaterial")]
        [SerializeField] Material capsuleColorMaterial;
        [Tooltip("Top half of the capsule (clear plastic).")]
        [SerializeField] Material capsuleClearMaterial;
        [SerializeField] float capsuleDiameter = 0.12f;
        [SerializeField] float dropDuration = 0.45f;

        int capsuleCount;
        GameObject waitingCapsule;
        MuseumBackend backend;
        bool museumReady;

        /// <summary>A capsule is falling or waiting in the cup: the next one is held back until it is taken.</summary>
        public bool HasWaitingCapsule => waitingCapsule != null;

        void Awake()
        {
            if (TriggerRouter.Instance == null && FindAnyObjectByType<TriggerRouter>() == null)
                new GameObject("Trigger Router").AddComponent<TriggerRouter>();
        }

        void Start()
        {
            backend = FindAnyObjectByType<MuseumBackend>();
            if (backend == null)
            {
                Debug.LogError("Gacha dispenser requires a MuseumBackend to register artwork.", this);
                return;
            }
            // No load of the previous save: every session starts from the example museum (placed artworks do not stay).
            museumReady = true;
        }

        /// <returns>True when a capsule was released.</returns>
        public bool Dispense()
        {
            if (capsuleSpawnPoint == null)
            {
                Debug.LogError("Gacha dispenser has no capsule spawn point.", this);
                return false;
            }
            if (capsuleColorMaterial == null || capsuleClearMaterial == null)
            {
                Debug.LogError("Gacha dispenser needs both capsule materials (coloured and clear halves).", this);
                return false;
            }
            if (waitingCapsule != null) return false;

            var capsule = new GameObject($"Gacha Capsule {++capsuleCount}");
            capsule.transform.SetPositionAndRotation(capsuleSpawnPoint.position, Quaternion.identity);
            capsule.transform.localScale = Vector3.one * capsuleDiameter;
            capsule.AddComponent<MeshFilter>().sharedMesh = GachaCapsuleMesh.Shared;
            capsule.AddComponent<MeshRenderer>().sharedMaterials = new[] { capsuleColorMaterial, capsuleClearMaterial };
            var shape = capsule.AddComponent<CapsuleCollider>();
            shape.direction = 1; // Y, across the seam
            shape.radius = GachaCapsuleMesh.Radius;
            shape.height = GachaCapsuleMesh.Height;
            shape.center = new Vector3(0f, GachaCapsuleMesh.CenterY, 0f);
            var body = capsule.AddComponent<Rigidbody>();
            body.useGravity = true;
            // Scripted drop into the cup: the cup's mesh collider is not reliable enough to hold a physics capsule.
            // It stays kinematic in the cup; the first release hands it to physics (see below). No interpolation
            // meanwhile: it would lag the transform the animation writes.
            body.isKinematic = capsuleRestPoint != null;
            body.interpolation = body.isKinematic ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            var grab = capsule.AddComponent<XRGrabInteractable>();
            capsule.AddComponent<GachaCapsuleTriggerGrab>();
            waitingCapsule = capsule;
            // The artwork is drawn now, so it shows through the clear half while the capsule falls and waits.
            var placer = museumReady ? Fill(capsule, logErrors: false) : null;

            Coroutine drop = null;
            if (capsuleRestPoint != null && Application.isPlaying)
                drop = StartCoroutine(DropIntoCup(capsule.transform));

            grab.selectEntered.AddListener(_ =>
            {
                if (drop != null) { StopCoroutine(drop); drop = null; }
                if (waitingCapsule == capsule) waitingCapsule = null;
                if (placer == null) placer = Fill(capsule, logErrors: true);
                if (placer != null && !placer.Opened) Open(capsule, placer);
            });
            // XRGrabInteractable restores the kinematic state it found at grab time: release it as a falling object.
            grab.selectExited.AddListener(_ =>
            {
                body.isKinematic = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
            });
            return true;
        }

        /// <summary>
        /// Roll out of the chute with gravity-like acceleration, then two small bounces in the cup. The roll is
        /// stretched to whole turns so the capsule lands as it started: clear half up, artwork in view.
        /// </summary>
        IEnumerator DropIntoCup(Transform capsule)
        {
            var start = capsuleSpawnPoint.position;
            var end = capsuleRestPoint.position;
            var rollAxis = Vector3.Cross(Vector3.up, end - start).normalized;
            var startRotation = capsule.rotation;
            var radius = capsuleDiameter * 0.5f;
            float naturalRoll = Vector3.Distance(start, end) / radius * Mathf.Rad2Deg;
            float totalRoll = Mathf.Max(1f, Mathf.Round(naturalRoll / 360f)) * 360f;

            for (float t = 0f; t < dropDuration; t += Time.deltaTime)
            {
                if (capsule == null) yield break;
                float k = t / dropDuration;
                k *= k;
                capsule.position = Vector3.Lerp(start, end, k);
                capsule.rotation = Quaternion.AngleAxis(totalRoll * k, rollAxis) * startRotation;
                yield return null;
            }
            if (capsule != null) capsule.rotation = startRotation;

            foreach (var (height, duration) in new[] { (0.02f, 0.16f), (0.006f, 0.09f) })
            {
                for (float t = 0f; t < duration; t += Time.deltaTime)
                {
                    if (capsule == null) yield break;
                    float k = t / duration;
                    capsule.position = end + Vector3.up * (height * 4f * k * (1f - k));
                    yield return null;
                }
            }
            if (capsule != null) capsule.position = end;
        }

        /// <summary>
        /// Put a random artwork miniature inside the closed capsule, shrunk to fit it. The artwork is NOT in the
        /// museum yet: <see cref="GachaArtworkPlacer"/> adds it when the capsule is released over a room of the plan.
        /// </summary>
        GachaArtworkPlacer Fill(GameObject capsule, bool logErrors)
        {
            if (capsule == null) return null;
            if (!museumReady)
            {
                if (logErrors) Debug.LogError("Gacha capsule cannot hold an artwork because the museum is not ready.", this);
                return null;
            }

            var definitions = backend.Catalog.Definitions;
            if (definitions.Count == 0)
            {
                if (logErrors) Debug.LogError("The artwork catalog is empty.", backend);
                return null;
            }

            var definition = definitions[Random.Range(0, definitions.Count)];
            if (definition == null || definition.prefab == null)
            {
                if (logErrors) Debug.LogError("The selected artwork definition has no prefab.", backend);
                return null;
            }

            var visual = Instantiate(definition.prefab, capsule.transform, false);
            visual.name = definition.title;
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            visual.transform.localScale = Vector3.one;
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            var placer = capsule.AddComponent<GachaArtworkPlacer>();
            placer.Init(definition, backend, visual.transform);
            return placer;
        }

        /// <summary>Grabbed: the shell goes away and the artwork takes its size on the plan.</summary>
        void Open(GameObject capsule, GachaArtworkPlacer placer)
        {
            placer.Open();
            var capsuleRenderer = capsule.GetComponent<Renderer>();
            if (capsuleRenderer != null) capsuleRenderer.enabled = false;
            Debug.Log($"Gacha capsule opened on '{placer.Definition.title}': release it over a room of the plan to place it.", this);
        }
    }
}
