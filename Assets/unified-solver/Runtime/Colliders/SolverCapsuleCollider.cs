using UnityEngine;

// Capsule world collider for the unified solver. Capsule is aligned to
// the local Y axis (matching the Unity capsule mesh: height 2, radius
// 0.5 along Y). World-space segment endpoints WorldPointA / WorldPointB
// are recomputed from the transform every time the manager uploads.
[DisallowMultipleComponent]
public class SolverCapsuleCollider : MonoBehaviour
{
    public float radius
    {
        get
        {
            Vector3 s = transform.lossyScale;
            return 0.5f * Mathf.Max(s.x, s.z);
        }
    }

    float HalfSegmentLength
    {
        get
        {
            float r = radius;
            float halfHeight = Mathf.Max(transform.lossyScale.y, r);
            return halfHeight - r;
        }
    }

    public Vector3 WorldPointA => transform.position - transform.up * HalfSegmentLength;
    public Vector3 WorldPointB => transform.position + transform.up * HalfSegmentLength;

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
            SolverManager.Instance.RegisterCapsuleCollider(this);
            _registered = true;
            Debug.Log($"SolverCapsuleCollider: Registered '{gameObject.name}' (radius={radius})");
        }
    }

    void OnDisable()
    {
        if (_registered && SolverManager.Instance != null)
        {
            SolverManager.Instance.UnregisterCapsuleCollider(this);
            _registered = false;
            Debug.Log($"SolverCapsuleCollider: Unregistered '{gameObject.name}'");
        }
    }

    void OnDrawGizmos()
    {
        DrawCapsuleGizmo(
            new Color(1f, 0.4f, 0.1f, 0.15f),
            new Color(1f, 0.4f, 0.1f, 0.6f)
        );
    }

    void OnDrawGizmosSelected()
    {
        DrawCapsuleGizmo(
            new Color(1f, 0.6f, 0.2f, 0.25f),
            new Color(1f, 0.6f, 0.2f, 1f)
        );
    }

    void DrawCapsuleGizmo(Color fill, Color wire)
    {
        Vector3 a = WorldPointA;
        Vector3 b = WorldPointB;
        Vector3 axis = b - a;
        float   r = radius;

        // Caps
        Gizmos.color = fill;
        Gizmos.DrawSphere(a, r);
        Gizmos.DrawSphere(b, r);

        Gizmos.color = wire;
        Gizmos.DrawWireSphere(a, r);
        Gizmos.DrawWireSphere(b, r);

        // Connecting lines along 4 sides of the cylinder.
        if (axis.sqrMagnitude > 1e-5f)
        {
            Vector3 dir = axis.normalized;
            Vector3 right = Vector3.Cross(dir, Vector3.up);
            if (right.sqrMagnitude < 1e-5f)
                right = Vector3.Cross(dir, Vector3.forward);
            right = right.normalized * r;
            Vector3 forward = Vector3.Cross(dir, right.normalized).normalized * r;

            Gizmos.DrawLine(a + right,   b + right);
            Gizmos.DrawLine(a - right,   b - right);
            Gizmos.DrawLine(a + forward, b + forward);
            Gizmos.DrawLine(a - forward, b - forward);
        }
    }
}
