using UnityEngine;

// Spawns a 3D grid of particles and registers them as a single rigid body
// via shape matching (Müller 2005 / Müller-Bender 2016). No distance
// constraints — rigidity comes purely from the GPU shape-matching
// projection in SolveRigidBody.
public class RigidCubeGenerator : MonoBehaviour
{
    [Header("Grid")]
    public int countX = 5;
    public int countY = 5;
    public int countZ = 5;

    [Header("Particle Settings")]
    public float spacing       = 0.25f;
    public float particleMass  = 1f;
    public Color particleColor = new Color(1.0f, 0.6f, 0.3f);
    [Range(0f, 1f)] public float colorVariation = 0.15f;

    void Start()
    {
        var manager = SolverManager.Instance;
        if (manager == null)
        {
            Debug.LogError("RigidCubeGenerator: No SolverManager found in scene.");
            return;
        }

        // Particles within a rigid body should never collide with each
        // other, so always allocate a unique phase (Macklin et al. 2014).
        int phase = PhaseManager.AllocatePhase();

        Vector3    origin = transform.position;
        Quaternion rot    = transform.rotation;

        Vector3 offset = new Vector3(
            (countX - 1) * spacing * 0.5f,
            (countY - 1) * spacing * 0.5f,
            (countZ - 1) * spacing * 0.5f
        );

        int   particleCount = countX * countY * countZ;
        int[] indices       = new int[particleCount];
        int   write         = 0;

        for (int x = 0; x < countX; x++)
        for (int y = 0; y < countY; y++)
        for (int z = 0; z < countZ; z++)
        {
            Vector3 localPos = new Vector3(x * spacing, y * spacing, z * spacing) - offset;
            Vector3 pos      = origin + rot * localPos;
            int idx = manager.AddParticle(pos, Vector3.zero, particleMass, VariedColor(), phase);
            indices[write++] = idx;
        }

        int rigidID = manager.AddRigidBody(indices, rot);
        if (rigidID < 0)
        {
            Debug.LogError("RigidCubeGenerator: AddRigidBody failed.");
            return;
        }

        Debug.Log($"RigidCubeGenerator: Spawned {particleCount} particles as rigid body #{rigidID}.");
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
            (countX - 1) * spacing,
            (countY - 1) * spacing,
            (countZ - 1) * spacing
        );

        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.color  = selected ? new Color(1f, 0.7f, 0.4f, 0.25f) : new Color(1f, 0.6f, 0.3f, 0.15f);
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.color  = selected ? new Color(1f, 0.7f, 0.4f, 1f) : new Color(1f, 0.6f, 0.3f, 0.6f);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }

    void OnDrawGizmos()         => DrawGizmo(false);
    void OnDrawGizmosSelected() => DrawGizmo(true);
}
