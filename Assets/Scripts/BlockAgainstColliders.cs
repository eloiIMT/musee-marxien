using UnityEngine;

/// <summary>
/// Empêche un objet saisi en XR (XRGrabInteractable en Velocity Tracking, main ou rayon)
/// de traverser les colliders des layers "blockers".
///
/// Fonctionne AVANT chaque pas physique (FixedUpdate, après XRI) :
/// 1) Si l'objet chevauche un blocker (ex. rotation contre le mur), il revient à sa
///    dernière pose valide.
/// 2) Sa vitesse est balayée (Rigidbody.SweepTestAll) : si un blocker est sur la
///    trajectoire de ce pas, la composante qui rentre dans la surface est limitée pour
///    s'arrêter au contact. La composante tangentielle est gardée (l'objet glisse).
/// </summary>
[DefaultExecutionOrder(10000)]
[RequireComponent(typeof(Rigidbody))]
public class BlockAgainstColliders : MonoBehaviour
{
    [Tooltip("Layers contre lesquels l'objet doit bloquer (mettre le plan sur ce layer).")]
    public LayerMask blockers;

    [Tooltip("Distance gardée entre l'objet et le blocker.")]
    public float skin = 0.005f;

    [Tooltip("Pénétration tolérée avant de revenir à la dernière pose valide.")]
    public float penetrationTolerance = 0.003f;

    Rigidbody _rb;
    Collider[] _own;
    Vector3 _lastValid;
    Quaternion _lastValidRot;
    readonly Collider[] _overlaps = new Collider[16];

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _own = GetComponentsInChildren<Collider>();
        _lastValid = _rb.position;
        _lastValidRot = _rb.rotation;
    }

    void FixedUpdate()
    {
        if (blockers.value == 0) return;

        // 1) Chevauchement : retour à la dernière pose valide.
        if (IsPenetrating(_rb.position, _rb.rotation))
        {
            _rb.position = _lastValid;
            _rb.rotation = _lastValidRot;
            transform.SetPositionAndRotation(_lastValid, _lastValidRot);
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            return;
        }
        _lastValid = _rb.position;
        _lastValidRot = _rb.rotation;

        // 2) Limiter la vitesse pour s'arrêter contre le blocker (2 passes pour gérer un coin).
        for (int pass = 0; pass < 2; pass++)
        {
            Vector3 v = _rb.linearVelocity;
            float dt = Time.fixedDeltaTime;
            float step = v.magnitude * dt;
            if (step < 1e-5f) return;
            Vector3 dir = v / (step / dt);

            if (!NearestBlockerHit(dir, step + skin, out RaycastHit hit)) return;

            Vector3 n = hit.normal;                 // normale tournée vers nous
            float into = -Vector3.Dot(v, n);         // vitesse vers la surface
            if (into <= 0f) return;

            float normalDist = Mathf.Max(0f, hit.distance - skin) * -Vector3.Dot(dir, n);
            float allowedInto = normalDist / dt;
            if (into > allowedInto)
                _rb.linearVelocity = v + n * (into - allowedInto);
            else
                return;
        }
    }

    bool NearestBlockerHit(Vector3 dir, float dist, out RaycastHit best)
    {
        best = default;
        float bestDist = float.MaxValue;
        foreach (var h in _rb.SweepTestAll(dir, dist, QueryTriggerInteraction.Ignore))
        {
            if ((blockers.value & (1 << h.collider.gameObject.layer)) == 0) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h; }
        }
        return bestDist < float.MaxValue;
    }

    bool IsPenetrating(Vector3 pos, Quaternion rot)
    {
        Matrix4x4 rootToWorld = Matrix4x4.TRS(pos, rot, Vector3.one);
        Matrix4x4 worldToRoot = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;

        foreach (var c in _own)
        {
            if (!c.enabled || c.isTrigger) continue;

            // Pose du collider pour la pose (pos, rot) du Rigidbody.
            Matrix4x4 m = rootToWorld * worldToRoot * c.transform.localToWorldMatrix;
            Vector3 cPos = m.GetColumn(3);
            Quaternion cRot = Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));

            Bounds b = c.bounds;
            int n = Physics.OverlapSphereNonAlloc(cPos, b.extents.magnitude + skin, _overlaps,
                                                  blockers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider other = _overlaps[i];
                if (other.attachedRigidbody == _rb) continue;
                if (Physics.ComputePenetration(c, cPos, cRot,
                                               other, other.transform.position, other.transform.rotation,
                                               out _, out float d) && d > penetrationTolerance)
                    return true;
            }
        }
        return false;
    }
}
