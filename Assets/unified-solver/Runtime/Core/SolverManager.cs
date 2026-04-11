using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Singleton MonoBehaviour orchestrating the unified particle solver.
//
// Holds every GPU buffer (particles, constraints, deltas, spatial hash,
// world colliders) and dispatches the compute kernels every FixedUpdate
// in a small-steps loop (Macklin et al. 2019). Public API for adding
// particles and distance constraints lives here.

public class SolverManager : MonoBehaviour
{
    public static SolverManager Instance { get; private set; }

    [Header("Simulation")]
    [Tooltip("Compute shader containing every solver kernel.")]
    public ComputeShader computeShader;
    [Tooltip("Number of substeps per FixedUpdate. One XPBD iteration per substep (Macklin 2019).")]
    public int substeps = 30;
    [Range(1f, 2f)]
    [Tooltip("Successive Over-Relaxation factor (Flex paper Eq. 13). 1 = pure averaging, up to 2 for faster convergence.")]
    public float sor = 1.5f;
    [Tooltip("Constant external acceleration applied each substep.")]
    public Vector3 gravity = new Vector3(0, -9.81f, 0);
    [Tooltip("World-space Y of the ground plane.")]
    public float groundY = 0f;
    [Tooltip("Global velocity damping. 0 = no damping, higher = more damping.")]
    public float damping = 0f;

    [Header("Particles")]
    [Tooltip("Default radius assigned by AddParticle when callers do not specify one.")]
    public float defaultRadius = 0.1f;
    [Tooltip("Default mass assigned by AddParticle when callers do not specify one.")]
    public float defaultMass = 1f;
    [Tooltip("Maximum particle capacity of the GPU buffer. Picking too low truncates spawned particles.")]
    public int maxParticles = 100_000;
    [Tooltip("Maximum distance constraint capacity of the GPU buffer.")]
    public int maxConstraints = 500_000;
    [Tooltip("Maximum number of sphere world colliders that may be registered at once.")]
    public int maxSphereColliders = 64;
    [Tooltip("Maximum number of capsule world colliders that may be registered at once.")]
    public int maxCapsuleColliders = 64;
    [Tooltip("Maximum number of box world colliders that may be registered at once.")]
    public int maxBoxColliders = 64;

    [Header("Particle Collisions")]
    [Tooltip("Toggle particle-particle contact via the spatial hash.")]
    public bool enableParticleCollisions = true;
    [Tooltip("Spatial hash cell size. Must be >= 2 * largest particle radius so all collision pairs fit in adjacent cells.")]
    public float cellSize = 0.2f;
    [Tooltip("Hash table size. Prime number, ideally 2-3x maxParticles.")]
    public int tableSize = 262139;

    [Header("Friction")]
    [Tooltip("Static friction (mu_s): tangential motion below mu_s * penetration is fully stopped.")]
    [Range(0f, 2f)] public float frictionStatic  = 0.4f;
    [Tooltip("Kinetic friction (mu_k): caps tangential correction at mu_k * penetration. Keep <= mu_s.")]
    [Range(0f, 2f)] public float frictionKinetic = 0.2f;

    [Header("Contact")]
    [Tooltip("Max depenetration speed (m/s). Limits how fast overlapping particles separate per substep to prevent velocity explosions. Lower = gentler separation, higher = snappier but can explode.")]
    public float maxDepenetrationSpeed = 5f;

    // GPU buffers
    ComputeBuffer _particleBuffer;
    ComputeBuffer _constraintBuffer;
    ComputeBuffer _deltaBuffer;            // int3 per particle (fixed-point Jacobi accumulator)
    ComputeBuffer _constraintCountBuffer;  // int per particle
    ComputeBuffer _sphereColliderBuffer;
    ComputeBuffer _capsuleColliderBuffer;
    ComputeBuffer _boxColliderBuffer;
    ComputeBuffer _hashHeadBuffer;         // int[tableSize]    — head of linked list per cell
    ComputeBuffer _hashNextBuffer;         // int[maxParticles] — next pointer per particle

    // CPU-side mirror data
    List<ParticleGPU> _particles = new List<ParticleGPU>();
    ParticleGPU[]     _particleArray;
    bool              _particlesDirty = true;

    List<DistanceConstraintGPU> _constraints = new List<DistanceConstraintGPU>();
    DistanceConstraintGPU[]     _constraintArray;
    bool                        _constraintsDirty = true;

    int _activeCount = 0;
    int _constraintCount = 0;

    // Registered world colliders
    List<SolverSphereCollider>  _sphereColliders  = new List<SolverSphereCollider>();
    SphereColliderGPU[]         _sphereColliderArray;

    List<SolverCapsuleCollider> _capsuleColliders = new List<SolverCapsuleCollider>();
    CapsuleColliderGPU[]        _capsuleColliderArray;

    List<SolverBoxCollider>     _boxColliders     = new List<SolverBoxCollider>();
    BoxColliderGPU[]            _boxColliderArray;

    // Kernel IDs
    int _kernelPredict;
    int _kernelClearDeltas;
    int _kernelSolveDistance;
    int _kernelSolveContact;
    int _kernelApplyDeltas;
    int _kernelSolveGround;
    int _kernelSolveSphere;
    int _kernelSolveCapsule;
    int _kernelSolveBox;
    int _kernelUpdateVelocity;
    int _kernelClearHash;
    int _kernelBuildHash;
    // CPU-side timing breakdown (rough — Dispatch is async so this is mostly
    // command submission cost; useful as a relative indicator in the editor).
    Stopwatch _swTotal   = new Stopwatch();
    Stopwatch _swUpload  = new Stopwatch();
    Stopwatch _swHash    = new Stopwatch();
    Stopwatch _swSubsteps = new Stopwatch();
    public double LastFrameTotalMs    { get; private set; }
    public double LastFrameUploadMs   { get; private set; }
    public double LastFrameHashMs     { get; private set; }
    public double LastFrameSubstepsMs { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        _kernelPredict        = computeShader.FindKernel("Predict");
        _kernelClearDeltas    = computeShader.FindKernel("ClearDeltas");
        _kernelSolveDistance  = computeShader.FindKernel("SolveDistance");
        _kernelSolveContact   = computeShader.FindKernel("SolveContact");
        _kernelApplyDeltas    = computeShader.FindKernel("ApplyDeltas");
        _kernelSolveGround    = computeShader.FindKernel("SolveGround");
        _kernelSolveSphere    = computeShader.FindKernel("SolveSphere");
        _kernelSolveCapsule   = computeShader.FindKernel("SolveCapsule");
        _kernelSolveBox       = computeShader.FindKernel("SolveBox");
        _kernelUpdateVelocity = computeShader.FindKernel("UpdateVelocity");
        _kernelClearHash      = computeShader.FindKernel("ClearHash");
        _kernelBuildHash      = computeShader.FindKernel("BuildHash");
        _particleBuffer        = new ComputeBuffer(maxParticles,        SolverData.ParticleStride);
        _constraintBuffer      = new ComputeBuffer(maxConstraints,      SolverData.DistanceConstraintStride);
        _deltaBuffer           = new ComputeBuffer(maxParticles,        SolverData.DeltaPosStride);
        _constraintCountBuffer = new ComputeBuffer(maxParticles,        SolverData.IntStride);
        _sphereColliderBuffer  = new ComputeBuffer(maxSphereColliders,  SolverData.SphereColliderStride);
        _capsuleColliderBuffer = new ComputeBuffer(maxCapsuleColliders, SolverData.CapsuleColliderStride);
        _boxColliderBuffer     = new ComputeBuffer(maxBoxColliders,     SolverData.BoxColliderStride);
        _hashHeadBuffer        = new ComputeBuffer(tableSize,           SolverData.IntStride);
        _hashNextBuffer        = new ComputeBuffer(maxParticles,        SolverData.IntStride);

        _particleArray         = new ParticleGPU[maxParticles];
        _constraintArray       = new DistanceConstraintGPU[maxConstraints];
        _sphereColliderArray   = new SphereColliderGPU[maxSphereColliders];
        _capsuleColliderArray  = new CapsuleColliderGPU[maxCapsuleColliders];
        _boxColliderArray      = new BoxColliderGPU[maxBoxColliders];
    }

    void OnDestroy()
    {
        _particleBuffer?.Release();
        _constraintBuffer?.Release();
        _deltaBuffer?.Release();
        _constraintCountBuffer?.Release();
        _sphereColliderBuffer?.Release();
        _capsuleColliderBuffer?.Release();
        _boxColliderBuffer?.Release();
        _hashHeadBuffer?.Release();
        _hashNextBuffer?.Release();

        if (Instance == this) Instance = null;
    }

    // ─────────────────────────────────────────────
    // Public API — particles
    // ─────────────────────────────────────────────

    public int AddParticle(Vector3 position, Vector3 velocity, float mass, float radius, Color color, int phase = PhaseManager.PhaseNone)
    {
        if (_activeCount >= maxParticles)
        {
            Debug.LogWarning("SolverManager: Max particle count reached.");
            return -1;
        }

        var p = new ParticleGPU
        {
            position     = position,
            velocity     = velocity,
            prevPosition = position,
            invMass      = mass > 0 ? 1f / mass : 0f,
            radius       = radius,
            phase        = phase,
            color        = new Vector3(color.r, color.g, color.b)
        };

        if (_activeCount < _particles.Count)
            _particles[_activeCount] = p;
        else
            _particles.Add(p);

        _particlesDirty = true;
        return _activeCount++;
    }

    public int AddDistanceConstraint(int particleA, int particleB, float compliance = 0f, float breakForce = 0f, float damping = 0f)
    {
        if (_constraintCount >= maxConstraints)
        {
            Debug.LogWarning("SolverManager: Max constraint count reached.");
            return -1;
        }

        Vector3 posA = _particles[particleA].position;
        Vector3 posB = _particles[particleB].position;

        var c = new DistanceConstraintGPU
        {
            particleA  = particleA,
            particleB  = particleB,
            restLength = Vector3.Distance(posA, posB),
            compliance = compliance,
            breakForce = breakForce,
            damping    = damping
        };

        if (_constraintCount < _constraints.Count)
            _constraints[_constraintCount] = c;
        else
            _constraints.Add(c);

        _constraintsDirty = true;
        return _constraintCount++;
    }

    public Vector3 GetParticlePosition(int index)
    {
        if (index < 0 || index >= _activeCount) return Vector3.zero;
        return _particles[index].position;
    }

    public void ReadbackParticles(ParticleGPU[] dest)
    {
        if (_activeCount == 0) return;
        _particleBuffer.GetData(dest, 0, 0, _activeCount);
    }

    // Synchronous readback of the constraint buffer to count broken
    // constraints (restLength < 0).
    public int CountBrokenConstraints()
    {
        if (_constraintCount == 0) return 0;
        _constraintBuffer.GetData(_constraintArray, 0, 0, _constraintCount);
        int broken = 0;
        for (int i = 0; i < _constraintCount; i++)
            if (_constraintArray[i].restLength < 0f) broken++;
        return broken;
    }

    // Wipes all particles and constraints. Buffers stay allocated.
    public void ResetSimulation()
    {
        _particles.Clear();
        _constraints.Clear();
        _activeCount     = 0;
        _constraintCount = 0;
        _particlesDirty  = true;
        _constraintsDirty = true;
        PhaseManager.Reset();
    }

    // ─────────────────────────────────────────────
    // Public API — collider registration (TryRegister pattern)
    // ─────────────────────────────────────────────

    public void RegisterSphereCollider(SolverSphereCollider col)
    {
        if (!_sphereColliders.Contains(col))
        {
            _sphereColliders.Add(col);
            Debug.Log($"SolverManager: Registered sphere collider '{col.gameObject.name}' (total: {_sphereColliders.Count})");
        }
    }

    public void UnregisterSphereCollider(SolverSphereCollider col)
    {
        if (_sphereColliders.Remove(col))
            Debug.Log($"SolverManager: Unregistered sphere collider '{col.gameObject.name}' (total: {_sphereColliders.Count})");
    }

    public void RegisterCapsuleCollider(SolverCapsuleCollider col)
    {
        if (!_capsuleColliders.Contains(col))
        {
            _capsuleColliders.Add(col);
            Debug.Log($"SolverManager: Registered capsule collider '{col.gameObject.name}' (total: {_capsuleColliders.Count})");
        }
    }

    public void UnregisterCapsuleCollider(SolverCapsuleCollider col)
    {
        if (_capsuleColliders.Remove(col))
            Debug.Log($"SolverManager: Unregistered capsule collider '{col.gameObject.name}' (total: {_capsuleColliders.Count})");
    }

    public void RegisterBoxCollider(SolverBoxCollider col)
    {
        if (!_boxColliders.Contains(col))
        {
            _boxColliders.Add(col);
            Debug.Log($"SolverManager: Registered box collider '{col.gameObject.name}' (total: {_boxColliders.Count})");
        }
    }

    public void UnregisterBoxCollider(SolverBoxCollider col)
    {
        if (_boxColliders.Remove(col))
            Debug.Log($"SolverManager: Unregistered box collider '{col.gameObject.name}' (total: {_boxColliders.Count})");
    }

    // ─────────────────────────────────────────────
    // Public accessors
    // ─────────────────────────────────────────────
    public ComputeBuffer ParticleBuffer    => _particleBuffer;
    public ComputeBuffer ConstraintBuffer  => _constraintBuffer;
    public int ActiveCount                 => _activeCount;
    public int ConstraintCount             => _constraintCount;
    public int SphereColliderCount         => _sphereColliders.Count;
    public int CapsuleColliderCount        => _capsuleColliders.Count;
    public int BoxColliderCount            => _boxColliders.Count;

    // ─────────────────────────────────────────────
    // GPU upload helpers
    // ─────────────────────────────────────────────
    void UploadParticlesToGPU()
    {
        for (int i = 0; i < _activeCount; i++)
            _particleArray[i] = _particles[i];
        _particleBuffer.SetData(_particleArray, 0, 0, _activeCount);
        _particlesDirty = false;
    }

    void UploadConstraintsToGPU()
    {
        for (int i = 0; i < _constraintCount; i++)
            _constraintArray[i] = _constraints[i];
        _constraintBuffer.SetData(_constraintArray, 0, 0, _constraintCount);
        _constraintsDirty = false;
    }

    void UploadSphereCollidersToGPU()
    {
        int count = Mathf.Min(_sphereColliders.Count, maxSphereColliders);
        for (int i = 0; i < count; i++)
        {
            var col = _sphereColliders[i];
            _sphereColliderArray[i] = new SphereColliderGPU
            {
                center = col.transform.position,
                radius = col.radius
            };
        }
        _sphereColliderBuffer.SetData(_sphereColliderArray, 0, 0, Mathf.Max(count, 1));
    }

    void UploadCapsuleCollidersToGPU()
    {
        int count = Mathf.Min(_capsuleColliders.Count, maxCapsuleColliders);
        for (int i = 0; i < count; i++)
        {
            var col = _capsuleColliders[i];
            _capsuleColliderArray[i] = new CapsuleColliderGPU
            {
                pointA = col.WorldPointA,
                radius = col.radius,
                pointB = col.WorldPointB,
                _pad0  = 0f
            };
        }
        _capsuleColliderBuffer.SetData(_capsuleColliderArray, 0, 0, Mathf.Max(count, 1));
    }

    void UploadBoxCollidersToGPU()
    {
        int count = Mathf.Min(_boxColliders.Count, maxBoxColliders);
        for (int i = 0; i < count; i++)
        {
            var col = _boxColliders[i];
            _boxColliderArray[i] = new BoxColliderGPU
            {
                center      = col.WorldCenter,
                halfExtents = col.HalfExtents,
                axisX       = col.WorldAxisX,
                axisY       = col.WorldAxisY,
                axisZ       = col.WorldAxisZ
            };
        }
        _boxColliderBuffer.SetData(_boxColliderArray, 0, 0, Mathf.Max(count, 1));
    }

    // ─────────────────────────────────────────────
    // Main simulation step
    // ─────────────────────────────────────────────

    void FixedUpdate()
    {
        if (_activeCount == 0) return;

        _swTotal.Restart();
        _swUpload.Restart();

        if (_particlesDirty)   UploadParticlesToGPU();
        if (_constraintsDirty) UploadConstraintsToGPU();

        int sphereColliderCount  = Mathf.Min(_sphereColliders.Count,  maxSphereColliders);
        int capsuleColliderCount = Mathf.Min(_capsuleColliders.Count, maxCapsuleColliders);
        int boxColliderCount     = Mathf.Min(_boxColliders.Count,     maxBoxColliders);

        if (sphereColliderCount  > 0) UploadSphereCollidersToGPU();
        if (capsuleColliderCount > 0) UploadCapsuleCollidersToGPU();
        if (boxColliderCount     > 0) UploadBoxCollidersToGPU();

        _swUpload.Stop();

        float frameDt = Time.fixedDeltaTime;
        float subDt   = frameDt / substeps;

        int particleGroups   = Mathf.CeilToInt(_activeCount / 256f);
        int constraintGroups = Mathf.CeilToInt(_constraintCount / 256f);
        int tableGroups      = Mathf.CeilToInt(tableSize / 256f);

        // Bind buffers
        computeShader.SetBuffer(_kernelPredict,        "_Particles",        _particleBuffer);
        computeShader.SetBuffer(_kernelSolveGround,    "_Particles",        _particleBuffer);
        computeShader.SetBuffer(_kernelUpdateVelocity, "_Particles",        _particleBuffer);
        computeShader.SetBuffer(_kernelSolveDistance,  "_Particles",        _particleBuffer);
        computeShader.SetBuffer(_kernelApplyDeltas,    "_Particles",        _particleBuffer);
        computeShader.SetBuffer(_kernelSolveSphere,    "_Particles",        _particleBuffer);

        computeShader.SetBuffer(_kernelSolveDistance,  "_Constraints",      _constraintBuffer);
        computeShader.SetBuffer(_kernelSolveDistance,  "_DeltaPos",         _deltaBuffer);
        computeShader.SetBuffer(_kernelSolveDistance,  "_ConstraintCounts", _constraintCountBuffer);

        computeShader.SetBuffer(_kernelApplyDeltas,    "_DeltaPos",         _deltaBuffer);
        computeShader.SetBuffer(_kernelApplyDeltas,    "_ConstraintCounts", _constraintCountBuffer);

        computeShader.SetBuffer(_kernelClearDeltas,    "_DeltaPos",         _deltaBuffer);
        computeShader.SetBuffer(_kernelClearDeltas,    "_ConstraintCounts", _constraintCountBuffer);

        computeShader.SetBuffer(_kernelSolveSphere,    "_SphereColliders",  _sphereColliderBuffer);
        computeShader.SetBuffer(_kernelSolveCapsule,   "_Particles",        _particleBuffer);
        computeShader.SetBuffer(_kernelSolveCapsule,   "_CapsuleColliders", _capsuleColliderBuffer);
        computeShader.SetBuffer(_kernelSolveBox,       "_Particles",        _particleBuffer);
        computeShader.SetBuffer(_kernelSolveBox,       "_BoxColliders",     _boxColliderBuffer);

        computeShader.SetBuffer(_kernelClearHash,      "_HashHead",         _hashHeadBuffer);
        computeShader.SetBuffer(_kernelBuildHash,      "_Particles",        _particleBuffer);
        computeShader.SetBuffer(_kernelBuildHash,      "_HashHead",         _hashHeadBuffer);
        computeShader.SetBuffer(_kernelBuildHash,      "_HashNext",         _hashNextBuffer);
        computeShader.SetBuffer(_kernelSolveContact,   "_Particles",        _particleBuffer);
        computeShader.SetBuffer(_kernelSolveContact,   "_HashHead",         _hashHeadBuffer);
        computeShader.SetBuffer(_kernelSolveContact,   "_HashNext",         _hashNextBuffer);
        computeShader.SetBuffer(_kernelSolveContact,   "_DeltaPos",         _deltaBuffer);
        computeShader.SetBuffer(_kernelSolveContact,   "_ConstraintCounts", _constraintCountBuffer);

        // Constants
        computeShader.SetInt("_ParticleCount",        _activeCount);
        computeShader.SetInt("_ConstraintCount",      _constraintCount);
        computeShader.SetInt("_SphereColliderCount",  sphereColliderCount);
        computeShader.SetInt("_CapsuleColliderCount", capsuleColliderCount);
        computeShader.SetInt("_BoxColliderCount",     boxColliderCount);
        computeShader.SetVector("_Gravity", gravity);
        computeShader.SetFloat("_GroundY",         groundY);
        computeShader.SetFloat("_SOR",             sor);
        computeShader.SetFloat("_Damping",         damping);
        computeShader.SetFloat("_CellSize",        cellSize);
        computeShader.SetInt("_TableSize",         tableSize);
        computeShader.SetFloat("_FrictionStatic",  frictionStatic);
        computeShader.SetFloat("_FrictionKinetic", frictionKinetic);
        computeShader.SetFloat("_MaxDepenetrationSpeed", maxDepenetrationSpeed);

        // Build the spatial hash once per frame from the current
        // (pre-substep) positions. The contact set is reused across
        // every substep (Macklin et al. 2019, section 4.2).
        _swHash.Restart();
        if (enableParticleCollisions)
        {
            computeShader.Dispatch(_kernelClearHash, tableGroups, 1, 1);
            computeShader.Dispatch(_kernelBuildHash, particleGroups, 1, 1);
        }
        _swHash.Stop();

        _swSubsteps.Restart();
        for (int s = 0; s < substeps; s++)
        {
            computeShader.SetFloat("_DeltaTime", subDt);

            // 1. Predict positions
            computeShader.Dispatch(_kernelPredict, particleGroups, 1, 1);

            // 2. Constraints + particle contacts via shared Jacobi delta buffer
            computeShader.Dispatch(_kernelClearDeltas, particleGroups, 1, 1);
            if (_constraintCount > 0)
                computeShader.Dispatch(_kernelSolveDistance, constraintGroups, 1, 1);

            if (enableParticleCollisions)
                computeShader.Dispatch(_kernelSolveContact, particleGroups, 1, 1);

            computeShader.Dispatch(_kernelApplyDeltas, particleGroups, 1, 1);

            // 3. World collision (ground + collider primitives)
            computeShader.Dispatch(_kernelSolveGround, particleGroups, 1, 1);
            if (sphereColliderCount  > 0) computeShader.Dispatch(_kernelSolveSphere,  particleGroups, 1, 1);
            if (capsuleColliderCount > 0) computeShader.Dispatch(_kernelSolveCapsule, particleGroups, 1, 1);
            if (boxColliderCount     > 0) computeShader.Dispatch(_kernelSolveBox,     particleGroups, 1, 1);

            // 4. Update velocities
            computeShader.Dispatch(_kernelUpdateVelocity, particleGroups, 1, 1);
        }
        _swSubsteps.Stop();

        _swTotal.Stop();

        LastFrameTotalMs    = _swTotal.Elapsed.TotalMilliseconds;
        LastFrameUploadMs   = _swUpload.Elapsed.TotalMilliseconds;
        LastFrameHashMs     = _swHash.Elapsed.TotalMilliseconds;
        LastFrameSubstepsMs = _swSubsteps.Elapsed.TotalMilliseconds;
    }
}
