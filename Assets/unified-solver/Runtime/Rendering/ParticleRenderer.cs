using UnityEngine;

// GPU-instanced sphere rendering of every particle in the SolverManager's
// particle buffer. The vertex shader pulls position/radius/color directly
// from the same buffer the compute shader writes to — no readback.
public class ParticleRenderer : MonoBehaviour
{
    public Material particleMaterial;
    public Mesh particleMesh;
    public bool castShadows = true;

    SolverManager _manager;

    void Start()
    {
        _manager = SolverManager.Instance;

        if (particleMaterial == null)
            Debug.LogError("ParticleRenderer: No material assigned.");

        if (particleMesh == null)
            Debug.LogError("ParticleRenderer: No mesh assigned.");
    }

    void Update()
    {
        if (_manager == null || _manager.ActiveCount == 0) return;
        if (particleMaterial == null || particleMesh == null) return;

        particleMaterial.SetBuffer("_Particles", _manager.ParticleBuffer);
        particleMaterial.SetFloat("_ParticleRadius", _manager.particleRadius);

        var bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

        var shadowMode = castShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;

        Graphics.DrawMeshInstancedProcedural(
            particleMesh,
            0,
            particleMaterial,
            bounds,
            _manager.ActiveCount,
            castShadows: shadowMode
        );
    }
}
