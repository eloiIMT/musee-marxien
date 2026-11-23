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
            grab.selectEntered.AddListener(_ =>
            {
                if (waitingCapsule == capsule) waitingCapsule = null;
            });
        }
    }
}
