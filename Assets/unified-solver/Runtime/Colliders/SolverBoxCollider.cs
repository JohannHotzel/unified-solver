using UnityEngine;

// Oriented box (OBB) world collider for the unified solver. Built from
// the transform: center, half-extents, and the three local axes.
[DisallowMultipleComponent]
public class SolverBoxCollider : MonoBehaviour
{
    // Unity cube mesh is 1x1x1 — scale determines final world size.
    public Vector3 WorldCenter => transform.position;
    public Vector3 HalfExtents => transform.lossyScale * 0.5f;
    public Vector3 WorldAxisX  => transform.right;
    public Vector3 WorldAxisY  => transform.up;
    public Vector3 WorldAxisZ  => transform.forward;

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
            SolverManager.Instance.RegisterBoxCollider(this);
            _registered = true;
            Debug.Log($"SolverBoxCollider: Registered '{gameObject.name}' (scale={transform.lossyScale})");
        }
    }

    void OnDisable()
    {
        if (_registered && SolverManager.Instance != null)
        {
            SolverManager.Instance.UnregisterBoxCollider(this);
            _registered = false;
            Debug.Log($"SolverBoxCollider: Unregistered '{gameObject.name}'");
        }
    }

    void OnDrawGizmos()
    {
        DrawBoxGizmo(
            new Color(1f, 0.4f, 0.1f, 0.15f),
            new Color(1f, 0.4f, 0.1f, 0.6f)
        );
    }

    void OnDrawGizmosSelected()
    {
        DrawBoxGizmo(
            new Color(1f, 0.6f, 0.2f, 0.25f),
            new Color(1f, 0.6f, 0.2f, 1f)
        );
    }

    void DrawBoxGizmo(Color fill, Color wire)
    {
        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);

        Gizmos.color = fill;
        Gizmos.DrawCube(Vector3.zero, Vector3.one);
        Gizmos.color = wire;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        Gizmos.matrix = prev;
    }
}
