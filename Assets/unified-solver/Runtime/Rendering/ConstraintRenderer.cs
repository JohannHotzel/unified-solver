using UnityEngine;

// Renders every active distance constraint as a colored line. Reads
// directly from SolverManager's particle and constraint buffers — broken
// constraints (restLength < 0) are skipped in the vertex shader by being
// pushed beyond the far plane.
public class ConstraintRenderer : MonoBehaviour
{
    public Material constraintMaterial;

    SolverManager _manager;

    void Start()
    {
        _manager = SolverManager.Instance;

        if (constraintMaterial == null)
            Debug.LogError("ConstraintRenderer: No material assigned.");
    }

    void Update()
    {
        if (_manager == null || _manager.ConstraintCount == 0) return;
        if (constraintMaterial == null) return;

        constraintMaterial.SetBuffer("_Particles",   _manager.ParticleBuffer);
        constraintMaterial.SetBuffer("_Constraints", _manager.ConstraintBuffer);

        var bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

        // 2 vertices per constraint line.
        // SV_VertexID / 2 = constraint index, % 2 = endpoint A or B.
        Graphics.DrawProcedural(
            constraintMaterial,
            bounds,
            MeshTopology.Lines,
            _manager.ConstraintCount * 2
        );
    }
}
