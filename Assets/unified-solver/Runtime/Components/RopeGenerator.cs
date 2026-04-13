using UnityEngine;

// Spawns a chain of particles forming a rope into the SolverManager.
// Each consecutive pair is connected by a distance constraint. The rope
// hangs along the local Y axis from the transform position downward.
// Renders as a camera-facing billboard strip whose vertex shader reads
// positions from the GPU particle buffer (no readback).
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RopeGenerator : MonoBehaviour
{
    [Header("Rope")]
    [Tooltip("Number of particles in the rope.")]
    public int segments = 30;
    [Tooltip("World-space distance between adjacent particles.")]
    public float spacing = 0.1f;

    [Header("Particle Settings")]
    public float particleMass = 1f;
    public Color particleColor = new Color(0.8f, 0.6f, 0.2f);
    [Range(0f, 1f)] public float colorVariation = 0.1f;

    [Header("Constraints")]
    [Tooltip("XPBD compliance (alpha). 0 = rigid, larger = softer.")]
    public float compliance = 0f;
    [Tooltip("Constraint damping (beta). 0 = no damping.")]
    public float constraintDamping = 0f;

    [Header("Collision")]
    [Tooltip("When enabled, rope particles collide with each other.")]
    public bool enableSelfCollision = false;

    [Header("Pinning")]
    [Tooltip("Fix the first particle (top) in place.")]
    public bool fixStart = true;
    [Tooltip("Fix the last particle (bottom) in place.")]
    public bool fixEnd = false;

    [Header("Rendering")]
    [Tooltip("Material using the RopeRenderer shader.")]
    public Material ropeMaterial;
    [Tooltip("Half-width of the billboard strip in world units.")]
    public float ropeWidth = 0.03f;

    int _particleOffset;
    int _particleCount;
    Material _materialInstance;
    MeshFilter _meshFilter;
    MeshRenderer _meshRenderer;

    float RopeLength => (segments - 1) * spacing;

    void Start()
    {
        var manager = SolverManager.Instance;
        if (manager == null)
        {
            Debug.LogError("RopeGenerator: No SolverManager found in scene.");
            return;
        }

        int phase = enableSelfCollision ? PhaseManager.PhaseNone : PhaseManager.AllocatePhase();

        Vector3 origin = transform.position;
        Quaternion rot = transform.rotation;

        _particleCount = segments;
        int[] indices = new int[segments];

        // Spawn particles along local -Y (downward).
        for (int i = 0; i < segments; i++)
        {
            Vector3 localPos = new Vector3(0f, -i * spacing, 0f);
            Vector3 pos = origin + rot * localPos;

            bool isFixed = (fixStart && i == 0) || (fixEnd && i == segments - 1);
            float mass = isFixed ? 0f : particleMass;

            int idx = manager.AddParticle(pos, Vector3.zero, mass, VariedColor(), phase);
            indices[i] = idx;

            if (i == 0) _particleOffset = idx;
        }

        // Build distance constraints between consecutive particles.
        int constraintCount = 0;
        for (int i = 0; i < segments - 1; i++)
        {
            manager.AddDistanceConstraint(indices[i], indices[i + 1], compliance, 0f, constraintDamping);
            constraintCount++;
        }

        Debug.Log($"RopeGenerator: Spawned {segments} particles, {constraintCount} constraints (offset={_particleOffset}).");

        // --- Build billboard strip mesh ---
        BuildMesh();

        _meshRenderer = GetComponent<MeshRenderer>();
        if (ropeMaterial != null)
        {
            _materialInstance = new Material(ropeMaterial);
            _meshRenderer.material = _materialInstance;
        }
    }

    void Update()
    {
        var manager = SolverManager.Instance;
        if (manager == null || _materialInstance == null) return;

        _materialInstance.SetBuffer("_Particles", manager.ParticleBuffer);
        _materialInstance.SetInt("_ParticleOffset", _particleOffset);
        _materialInstance.SetInt("_SegmentCount", _particleCount);
        _materialInstance.SetFloat("_RopeWidth", ropeWidth);
    }

    void OnDestroy()
    {
        if (_materialInstance != null)
            Destroy(_materialInstance);
    }

    void BuildMesh()
    {
        _meshFilter = GetComponent<MeshFilter>();

        // 2 vertices per segment (left + right of the strip).
        int vertCount = segments * 2;
        int quadCount = segments - 1;

        Vector3[] verts = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];

        for (int s = 0; s < segments; s++)
        {
            float v = (float)s / (segments - 1);
            int left  = s * 2;
            int right = s * 2 + 1;
            verts[left]  = Vector3.zero;
            verts[right] = Vector3.zero;
            uvs[left]  = new Vector2(0f, v);
            uvs[right] = new Vector2(1f, v);
        }

        // Two triangles per quad, double-sided (4 tris per quad).
        int[] tris = new int[quadCount * 4 * 3];
        int t = 0;
        for (int s = 0; s < segments - 1; s++)
        {
            int bl = s * 2;
            int br = s * 2 + 1;
            int tl = (s + 1) * 2;
            int tr = (s + 1) * 2 + 1;

            // Front
            tris[t++] = bl; tris[t++] = tl; tris[t++] = tr;
            tris[t++] = bl; tris[t++] = tr; tris[t++] = br;
            // Back
            tris[t++] = bl; tris[t++] = tr; tris[t++] = tl;
            tris[t++] = bl; tris[t++] = br; tris[t++] = tr;
        }

        Mesh mesh = new Mesh();
        mesh.name = "RopeStripMesh";
        if (vertCount > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

        _meshFilter.mesh = mesh;
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
        float r = 0.1f;
        if (SolverManager.Instance != null)
            r = SolverManager.Instance.particleRadius;
        float diameter = r * 2f;

        Vector3 size = new Vector3(diameter, RopeLength, diameter);
        Vector3 center = new Vector3(0f, -RopeLength * 0.5f, 0f);

        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.color = selected ? new Color(0.4f, 0.7f, 1f, 0.25f) : new Color(0.3f, 0.6f, 1f, 0.15f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = selected ? new Color(0.4f, 0.7f, 1f, 1f) : new Color(0.3f, 0.6f, 1f, 0.6f);
        Gizmos.DrawWireCube(center, size);
    }

    void OnDrawGizmos() => DrawGizmo(false);
    void OnDrawGizmosSelected() => DrawGizmo(true);
}
