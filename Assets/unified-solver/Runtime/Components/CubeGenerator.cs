using UnityEngine;

// Spawns a 3D grid of particles into the SolverManager and optionally
// wires them up with structural, shear and volume distance constraints
// (XPBD). Useful as a quick test asset
public class CubeGenerator : MonoBehaviour
{
    [Header("Grid")]
    public int countX = 5;
    public int countY = 5;
    public int countZ = 5;

    [Header("Particle Settings")]
    public float spacing = 0.25f;
    public float particleMass = 1f;
    public Color particleColor = new Color(0.3f, 0.6f, 1.0f);
    [Range(0f, 1f)] public float colorVariation = 0.15f;

    [Header("Constraints")]
    [Tooltip("Whether to build distance constraints between particles (XPBD)")]
    public bool buildDistanceConstraints = true;
    [Tooltip("Include diagonal constraints for shear stiffness")]
    public bool includeDiagonals = true;
    [Tooltip("XPBD compliance (alpha). 0 = rigid, larger = softer. Try 0.0 for stiff, 0.0001 for soft.")]
    public float compliance = 0f;
    [Tooltip("Force threshold in Newton for breaking. 0 = unbreakable.")]
    public float breakForce = 0f;
    [Tooltip("Constraint damping (beta). 0 = no damping.")]
    public float damping = 0f;

    [Header("Fixed Particles")]
    [Tooltip("Fix the top row (y = countY-1) in place (invMass = 0)")]
    public bool fixTopRow = false;
    [Tooltip("Fix the bottom row (y = 0) in place (invMass = 0)")]
    public bool fixBottomRow = false;

    int[,,] _particleIndices;

    void Start()
    {
        var manager = SolverManager.Instance;
        if (manager == null)
        {
            Debug.LogError("CubeGenerator: No SolverManager found in scene.");
            return;
        }

        Vector3 origin = transform.position;
        Quaternion rot = transform.rotation;

        Vector3 offset = new Vector3(
            (countX - 1) * spacing * 0.5f,
            (countY - 1) * spacing * 0.5f,
            (countZ - 1) * spacing * 0.5f
        );

        _particleIndices = new int[countX, countY, countZ];

        // --- Spawn particles ---
        for (int x = 0; x < countX; x++)
        {
            for (int y = 0; y < countY; y++)
            {
                for (int z = 0; z < countZ; z++)
                {
                    Vector3 localPos = new Vector3(
                        x * spacing,
                        y * spacing,
                        z * spacing
                    ) - offset;

                    Vector3 pos = origin + rot * localPos;
                    bool isFixed = (fixBottomRow && y == 0) || (fixTopRow && y == countY - 1);
                    float mass = isFixed ? 0f : particleMass;
                    int idx = manager.AddParticle(pos, Vector3.zero, mass, VariedColor());
                    _particleIndices[x, y, z] = idx;
                }
            }
        }

        int particleCount = countX * countY * countZ;
        Debug.Log($"CubeGenerator: Spawned {particleCount} particles.");

        if (buildDistanceConstraints)
        {
            // --- Build distance constraints ---
            int constraintCount = 0;

            for (int x = 0; x < countX; x++)
            {
                for (int y = 0; y < countY; y++)
                {
                    for (int z = 0; z < countZ; z++)
                    {
                        int a = _particleIndices[x, y, z];

                        // Structural (axis-aligned)
                        if (x + 1 < countX)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x + 1, y, z], compliance, breakForce, damping);
                            constraintCount++;
                        }
                        if (y + 1 < countY)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x, y + 1, z], compliance, breakForce, damping);
                            constraintCount++;
                        }
                        if (z + 1 < countZ)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x, y, z + 1], compliance, breakForce, damping);
                            constraintCount++;
                        }

                        if (!includeDiagonals) continue;

                        // Shear (face diagonals)
                        if (x + 1 < countX && y + 1 < countY)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x + 1, y + 1, z], compliance, breakForce, damping);
                            constraintCount++;
                        }
                        if (x + 1 < countX && y - 1 >= 0)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x + 1, y - 1, z], compliance, breakForce, damping);
                            constraintCount++;
                        }
                        if (x + 1 < countX && z + 1 < countZ)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x + 1, y, z + 1], compliance, breakForce, damping);
                            constraintCount++;
                        }
                        if (x + 1 < countX && z - 1 >= 0)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x + 1, y, z - 1], compliance, breakForce, damping);
                            constraintCount++;
                        }
                        if (y + 1 < countY && z + 1 < countZ)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x, y + 1, z + 1], compliance, breakForce, damping);
                            constraintCount++;
                        }
                        if (y + 1 < countY && z - 1 >= 0)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x, y + 1, z - 1], compliance, breakForce, damping);
                            constraintCount++;
                        }

                        // Volume (body diagonals)
                        if (x + 1 < countX && y + 1 < countY && z + 1 < countZ)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x + 1, y + 1, z + 1], compliance, breakForce, damping);
                            constraintCount++;
                        }
                        if (x + 1 < countX && y + 1 < countY && z - 1 >= 0)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x + 1, y + 1, z - 1], compliance, breakForce, damping);
                            constraintCount++;
                        }
                        if (x + 1 < countX && y - 1 >= 0 && z + 1 < countZ)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x + 1, y - 1, z + 1], compliance, breakForce, damping);
                            constraintCount++;
                        }
                        if (x + 1 < countX && y - 1 >= 0 && z - 1 >= 0)
                        {
                            manager.AddDistanceConstraint(a, _particleIndices[x + 1, y - 1, z - 1], compliance, breakForce, damping);
                            constraintCount++;
                        }
                    }
                }
            }

            Debug.Log($"CubeGenerator: Created {constraintCount} distance constraints (compliance={compliance}, breakForce={breakForce}N).");
        }
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
        Gizmos.color = selected ? new Color(0.4f, 0.7f, 1f, 0.25f) : new Color(0.3f, 0.6f, 1f, 0.15f);
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.color = selected ? new Color(0.4f, 0.7f, 1f, 1f) : new Color(0.3f, 0.6f, 1f, 0.6f);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }

    void OnDrawGizmos() => DrawGizmo(false);
    void OnDrawGizmosSelected() => DrawGizmo(true);
}
