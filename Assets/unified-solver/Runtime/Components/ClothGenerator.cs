using UnityEngine;

// Spawns a 2D grid of particles forming a cloth sheet into the
// SolverManager. Builds structural + shear distance constraints and
// creates a static mesh whose vertex shader reads positions directly
// from the GPU particle buffer (no readback).
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ClothGenerator : MonoBehaviour
{
    [Header("Grid")]
    [Tooltip("Number of particles along the X axis.")]
    public int resolutionX = 20;
    [Tooltip("Number of particles along the Y axis.")]
    public int resolutionY = 20;
    [Tooltip("World-space distance between adjacent particles.")]
    public float spacing = 0.1f;

    [Header("Particle Settings")]
    public float particleMass = 1f;
    public Color particleColor = new Color(0.9f, 0.2f, 0.3f);
    [Range(0f, 1f)] public float colorVariation = 0.15f;

    [Header("Constraints")]
    [Tooltip("XPBD compliance (alpha). 0 = rigid, larger = softer.")]
    public float compliance = 0f;
    [Tooltip("Force threshold in Newton for breaking. 0 = unbreakable.")]
    public float breakForce = 0f;
    [Tooltip("Constraint damping (beta). 0 = no damping.")]
    public float constraintDamping = 0f;
    [Tooltip("Include diagonal (shear) constraints for wrinkle resistance.")]
    public bool includeShear = true;

    [Header("Collision")]
    [Tooltip("When enabled, cloth particles collide with each other.")]
    public bool enableSelfCollision = false;

    [Header("Pinning")]
    [Tooltip("Fix the top edge (y = resolutionY-1) in place.")]
    public bool fixTopEdge = true;
    [Tooltip("Fix the bottom edge (y = 0) in place.")]
    public bool fixBottomEdge = false;
    [Tooltip("Fix the left edge (x = 0) in place.")]
    public bool fixLeftEdge = false;
    [Tooltip("Fix the right edge (x = resolutionX-1) in place.")]
    public bool fixRightEdge = false;
    [Tooltip("Fix only the two top corners instead of the full top edge.")]
    public bool fixTopCornersOnly = false;

    [Header("Rendering")]
    [Tooltip("Material using the ClothRenderer shader.")]
    public Material clothMaterial;
    [Tooltip("Render backfaces (double-sided cloth).")]
    public bool doubleSided = true;

    // Index of the first cloth particle in the global particle buffer.
    int _particleOffset;
    Material _materialInstance;
    MeshFilter _meshFilter;
    MeshRenderer _meshRenderer;

    void Start()
    {
        var manager = SolverManager.Instance;
        if (manager == null)
        {
            Debug.LogError("ClothGenerator: No SolverManager found in scene.");
            return;
        }

        int phase = enableSelfCollision ? PhaseManager.PhaseNone : PhaseManager.AllocatePhase();

        Vector3 origin = transform.position;
        Quaternion rot = transform.rotation;

        // Center the grid on the transform.
        Vector3 offset = new Vector3(
            (resolutionX - 1) * spacing * 0.5f,
            (resolutionY - 1) * spacing * 0.5f,
            0f
        );

        int totalParticles = resolutionX * resolutionY;
        int[] indices = new int[totalParticles];

        // --- Spawn particles ---
        for (int y = 0; y < resolutionY; y++)
        {
            for (int x = 0; x < resolutionX; x++)
            {
                Vector3 localPos = new Vector3(x * spacing, y * spacing, 0f) - offset;
                Vector3 pos = origin + rot * localPos;

                bool isFixed = IsFixed(x, y);
                float mass = isFixed ? 0f : particleMass;

                int idx = manager.AddParticle(pos, Vector3.zero, mass, VariedColor(), phase);
                int flat = y * resolutionX + x;
                indices[flat] = idx;

                // Store offset from the first particle.
                if (flat == 0) _particleOffset = idx;
            }
        }

        Debug.Log($"ClothGenerator: Spawned {totalParticles} particles (offset={_particleOffset}).");

        // --- Build constraints ---
        int constraintCount = 0;
        for (int y = 0; y < resolutionY; y++)
        {
            for (int x = 0; x < resolutionX; x++)
            {
                int a = indices[y * resolutionX + x];

                // Structural: horizontal
                if (x + 1 < resolutionX)
                {
                    manager.AddDistanceConstraint(a, indices[y * resolutionX + x + 1], compliance, breakForce, constraintDamping);
                    constraintCount++;
                }
                // Structural: vertical
                if (y + 1 < resolutionY)
                {
                    manager.AddDistanceConstraint(a, indices[(y + 1) * resolutionX + x], compliance, breakForce, constraintDamping);
                    constraintCount++;
                }

                if (!includeShear) continue;

                // Shear diagonals
                if (x + 1 < resolutionX && y + 1 < resolutionY)
                {
                    manager.AddDistanceConstraint(a, indices[(y + 1) * resolutionX + x + 1], compliance, breakForce, constraintDamping);
                    constraintCount++;
                }
                if (x - 1 >= 0 && y + 1 < resolutionY)
                {
                    manager.AddDistanceConstraint(a, indices[(y + 1) * resolutionX + x - 1], compliance, breakForce, constraintDamping);
                    constraintCount++;
                }
            }
        }

        Debug.Log($"ClothGenerator: Created {constraintCount} constraints.");

        // --- Build mesh ---
        BuildMesh();

        // --- Setup material ---
        // Each cloth instance needs its own material copy because the
        // shader uniforms (_ParticleOffset, _ResolutionX/Y) differ per instance.
        _meshRenderer = GetComponent<MeshRenderer>();
        if (clothMaterial != null)
        {
            _materialInstance = new Material(clothMaterial);
            _meshRenderer.material = _materialInstance;
        }
    }

    void Update()
    {
        var manager = SolverManager.Instance;
        if (manager == null || _materialInstance == null) return;

        // Pass particle buffer and grid info to the shader every frame.
        _materialInstance.SetBuffer("_Particles", manager.ParticleBuffer);
        _materialInstance.SetInt("_ParticleOffset", _particleOffset);
        _materialInstance.SetInt("_ResolutionX", resolutionX);
        _materialInstance.SetInt("_ResolutionY", resolutionY);
    }

    void OnDestroy()
    {
        if (_materialInstance != null)
            Destroy(_materialInstance);
    }

    bool IsFixed(int x, int y)
    {
        if (fixTopCornersOnly)
        {
            return y == resolutionY - 1 && (x == 0 || x == resolutionX - 1);
        }
        if (fixTopEdge && y == resolutionY - 1) return true;
        if (fixBottomEdge && y == 0) return true;
        if (fixLeftEdge && x == 0) return true;
        if (fixRightEdge && x == resolutionX - 1) return true;
        return false;
    }

    void BuildMesh()
    {
        _meshFilter = GetComponent<MeshFilter>();

        int vertCount = resolutionX * resolutionY;
        int quadCount = (resolutionX - 1) * (resolutionY - 1);
        int triCount = quadCount * (doubleSided ? 4 : 2);

        // Vertex positions are placeholders — the shader overrides them.
        Vector3[] verts = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];

        for (int y = 0; y < resolutionY; y++)
        {
            for (int x = 0; x < resolutionX; x++)
            {
                int i = y * resolutionX + x;
                verts[i] = Vector3.zero;
                uvs[i] = new Vector2((float)x / (resolutionX - 1), (float)y / (resolutionY - 1));
            }
        }

        int[] tris = new int[triCount * 3];
        int t = 0;
        for (int y = 0; y < resolutionY - 1; y++)
        {
            for (int x = 0; x < resolutionX - 1; x++)
            {
                int bl = y * resolutionX + x;
                int br = bl + 1;
                int tl = bl + resolutionX;
                int tr = tl + 1;

                // Front face
                tris[t++] = bl; tris[t++] = tl; tris[t++] = tr;
                tris[t++] = bl; tris[t++] = tr; tris[t++] = br;

                if (doubleSided)
                {
                    // Back face (reversed winding)
                    tris[t++] = bl; tris[t++] = tr; tris[t++] = tl;
                    tris[t++] = bl; tris[t++] = br; tris[t++] = tr;
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "ClothMesh";
        if (vertCount > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;

        // Large bounds so the mesh is never culled (positions are on GPU).
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
        Vector3 size = new Vector3(
            (resolutionX - 1) * spacing,
            (resolutionY - 1) * spacing,
            0.01f
        );

        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.color = selected ? new Color(1f, 0.4f, 0.5f, 0.25f) : new Color(0.9f, 0.3f, 0.4f, 0.15f);
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.color = selected ? new Color(1f, 0.4f, 0.5f, 1f) : new Color(0.9f, 0.3f, 0.4f, 0.6f);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }

    void OnDrawGizmos() => DrawGizmo(false);
    void OnDrawGizmosSelected() => DrawGizmo(true);
}
