using System.Collections.Generic;
using UnityEngine;

// Voxelizes a source mesh into a particle grid and registers the result as
// a single rigid body via shape matching (Müller 2005 / Müller-Bender 2016).
// Rigidity comes purely from SolveRigidBody, no distance constraints.
//
// Step 1: static voxelization at spawn time — the visual mesh is NOT moved
// to track the rigid body (that's step 2).
public class MeshVoxelRigidGenerator : MonoBehaviour
{
    [Header("Voxelization")]
    [Tooltip("Distance between adjacent particle centers inside the voxelized mesh. All particles share the global radius set on SolverManager.")]
    public float spacing = 0.1f;

    [Header("Particle Settings")]
    public float particleMass  = 1f;
    public Color particleColor = new Color(0.3f, 0.6f, 1.0f);
    [Range(0f, 1f)] public float colorVariation = 0.15f;

    [Header("Visualization")]
    [Tooltip("Hide the underlying voxel particles (only the source mesh is rendered). Applied once at spawn time.")]
    public bool hideParticles = true;

    // Cached for gizmo preview
    int _lastParticleCount = -1;

    // Solver-side rigid body ID, used in LateUpdate to drive the mesh transform.
    int _rigidID = -1;

    void Start()
    {
        Mesh mesh = ResolveMesh();
        if (mesh == null)
        {
            Debug.LogError("MeshVoxelRigidGenerator: No MeshFilter with sharedMesh on this GameObject.");
            return;
        }

        var manager = SolverManager.Instance;
        if (manager == null)
        {
            Debug.LogError("MeshVoxelRigidGenerator: No SolverManager in scene.");
            return;
        }

        // Particles within a rigid body share one phase so they skip mutual
        // collision (Macklin et al. 2014, section 3/5).
        int phase = PhaseManager.AllocatePhase();

        Vector3    origin = transform.position;
        Quaternion rot    = transform.rotation;
        Vector3    scale  = transform.lossyScale;

        // Apply the transform's scale to mesh vertices once so voxelization
        // matches the visual size. Everything below runs in this scaled
        // mesh-local space (pre-rotation, pre-translation).
        Vector3[] verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++) verts[i] = Vector3.Scale(verts[i], scale);
        int[] tris = mesh.triangles;

        // Bounds of scaled mesh
        Bounds bounds = ComputeBounds(verts);
        Vector3 bMin = bounds.min;
        Vector3 bMax = bounds.max;
        Vector3 bCenter = bounds.center;

        // Grid dimensions — one particle center per cell
        int nx = Mathf.Max(1, Mathf.CeilToInt((bMax.x - bMin.x) / spacing));
        int ny = Mathf.Max(1, Mathf.CeilToInt((bMax.y - bMin.y) / spacing));
        int nz = Mathf.Max(1, Mathf.CeilToInt((bMax.z - bMin.z) / spacing));

        Vector3 gridOrigin = bMin + 0.5f * spacing * Vector3.one;

        var indices = new List<int>(1024);

        for (int x = 0; x < nx; x++)
        for (int y = 0; y < ny; y++)
        for (int z = 0; z < nz; z++)
        {
            Vector3 localPos = gridOrigin + new Vector3(x * spacing, y * spacing, z * spacing);
            if (!IsInsideMesh(localPos, verts, tris)) continue;

            // Center the particle grid on the mesh bounds so rotation/position
            // reference the visual centroid, not the mesh origin.
            Vector3 centered = localPos - bCenter;
            Vector3 world    = origin + rot * centered;

            int idx = manager.AddParticle(world, Vector3.zero, particleMass, VariedColor(), phase, !hideParticles);
            indices.Add(idx);
        }

        _lastParticleCount = indices.Count;
        if (indices.Count == 0)
        {
            Debug.LogWarning("MeshVoxelRigidGenerator: Voxelization produced 0 particles. Check spacing / mesh.");
            return;
        }

        int rigidID = manager.AddRigidBody(indices.ToArray(), origin, rot);
        if (rigidID < 0)
        {
            Debug.LogError("MeshVoxelRigidGenerator: AddRigidBody failed.");
            return;
        }
        _rigidID = rigidID;

        Debug.Log($"MeshVoxelRigidGenerator: Voxelized '{mesh.name}' into {indices.Count} particles as rigid body #{rigidID}.");
    }

    void LateUpdate()
    {
        if (_rigidID < 0) return;
        var manager = SolverManager.Instance;
        if (manager == null) return;
        if (manager.TryGetRigidBodyMeshPose(_rigidID, out Vector3 pos, out Quaternion rot))
            transform.SetPositionAndRotation(pos, rot);
    }

    Mesh ResolveMesh()
    {
        var mf = GetComponent<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }

    static Bounds ComputeBounds(Vector3[] verts)
    {
        if (verts.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
        Vector3 mn = verts[0], mx = verts[0];
        for (int i = 1; i < verts.Length; i++)
        {
            mn = Vector3.Min(mn, verts[i]);
            mx = Vector3.Max(mx, verts[i]);
        }
        var b = new Bounds();
        b.SetMinMax(mn, mx);
        return b;
    }

    // Ray-parity inside test: cast a ray from p in an irrational direction
    // and count triangle crossings. Odd = inside.
    //
    // The direction is deliberately non-axis-aligned with irrational-looking
    // components so that axis-aligned mesh edges/vertices never lie exactly
    // on the ray. This eliminates the degenerate edge/vertex hits that cause
    // parity-flip artifacts in orientations where many triangle edges are
    // parallel to a canonical axis.
    static readonly Vector3 RayDir = new Vector3(1f, 0.1273f, 0.2831f).normalized;

    static bool IsInsideMesh(Vector3 p, Vector3[] verts, int[] tris)
    {
        int crossings = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            Vector3 v0 = verts[tris[t]];
            Vector3 v1 = verts[tris[t + 1]];
            Vector3 v2 = verts[tris[t + 2]];
            if (RayHitsTriangle(p, RayDir, v0, v1, v2)) crossings++;
        }
        return (crossings & 1) == 1;
    }

    // Möller-Trumbore ray-triangle intersection. Returns true if the ray
    // origin + t*dir hits the triangle at t > 0.
    static bool RayHitsTriangle(Vector3 o, Vector3 d, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        const float EPS = 1e-7f;
        Vector3 e1 = v1 - v0;
        Vector3 e2 = v2 - v0;
        Vector3 h  = Vector3.Cross(d, e2);
        float   a  = Vector3.Dot(e1, h);
        if (a > -EPS && a < EPS) return false;  // Parallel

        float   f  = 1f / a;
        Vector3 s  = o - v0;
        float   u  = f * Vector3.Dot(s, h);
        if (u < 0f || u > 1f) return false;

        Vector3 q  = Vector3.Cross(s, e1);
        float   v  = f * Vector3.Dot(d, q);
        if (v < 0f || u + v > 1f) return false;

        float   tHit = f * Vector3.Dot(e2, q);
        return tHit > EPS;
    }

    Color VariedColor()
    {
        Color.RGBToHSV(particleColor, out float h, out float s, out float v);
        h = Mathf.Repeat(h + Random.Range(-colorVariation * 0.5f, colorVariation * 0.5f), 1f);
        s = Mathf.Clamp01(s + Random.Range(-colorVariation * 0.2f, colorVariation * 0.2f));
        v = Mathf.Clamp01(v + Random.Range(-colorVariation * 0.2f, colorVariation * 0.2f));
        return Color.HSVToRGB(h, s, v);
    }

    void DrawGizmo(bool selected)
    {
        Mesh mesh = ResolveMesh();
        if (mesh == null) return;

        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        Gizmos.color  = selected ? new Color(0.4f, 0.7f, 1f, 0.25f) : new Color(0.3f, 0.6f, 1f, 0.15f);
        Gizmos.DrawMesh(mesh, Vector3.zero);
        Gizmos.color  = selected ? new Color(0.4f, 0.7f, 1f, 1f) : new Color(0.3f, 0.6f, 1f, 0.6f);
        Gizmos.DrawWireMesh(mesh, Vector3.zero);
    }

    void OnDrawGizmos()         => DrawGizmo(false);
    void OnDrawGizmosSelected() => DrawGizmo(true);
}
