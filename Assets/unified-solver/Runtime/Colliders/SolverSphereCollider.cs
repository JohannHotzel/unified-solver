using UnityEngine;

// Sphere world collider for the unified solver. Registers itself with
// the SolverManager via the TryRegister pattern (Awake/Start fallback so
// scene load order does not matter), and uploads its world-space center
// and radius to the GPU each frame.
[DisallowMultipleComponent]
public class SolverSphereCollider : MonoBehaviour
{
    // Unity sphere mesh has radius 0.5, so transform.lossyScale determines
    // the final world radius. We pick the largest axis to stay safe.
    public float radius => 0.5f * Mathf.Max(transform.lossyScale.x,
                                  Mathf.Max(transform.lossyScale.y,
                                            transform.lossyScale.z));

    bool _registered = false;

    void OnEnable()
    {
        TryRegister();
    }

    void Start()
    {
        // Fallback: if OnEnable fired before SolverManager.Awake.
        TryRegister();
    }

    void TryRegister()
    {
        if (_registered) return;
        if (SolverManager.Instance != null)
        {
            SolverManager.Instance.RegisterSphereCollider(this);
            _registered = true;
            Debug.Log($"SolverSphereCollider: Registered '{gameObject.name}' (radius={radius}, pos={transform.position})");
        }
    }

    void OnDisable()
    {
        if (_registered && SolverManager.Instance != null)
        {
            SolverManager.Instance.UnregisterSphereCollider(this);
            _registered = false;
            Debug.Log($"SolverSphereCollider: Unregistered '{gameObject.name}'");
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.15f);
        Gizmos.DrawSphere(transform.position, radius);
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.25f);
        Gizmos.DrawSphere(transform.position, radius);
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 1f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
