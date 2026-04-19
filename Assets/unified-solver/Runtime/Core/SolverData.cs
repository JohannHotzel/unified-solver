using UnityEngine;

// All GPU-resident structs for the unified solver. Layouts must match
// UnifiedSolver.compute byte-for-byte. Strides below are the canonical
// sizes used when constructing ComputeBuffers.
public static class SolverData
{
    // Fixed-point scale used for atomic Jacobi accumulation in the
    // compute shader (HLSL has no atomic float). Must match FP_SCALE
    // in UnifiedSolver.compute.
    public const float FP_SCALE = 100000.0f;

    // GPU buffer strides (bytes)
    public const int ParticleStride            = 56; // 3*Vec3 + float + int + Vec3
    public const int DistanceConstraintStride  = 24; // 2*int + 4*float
    public const int SphereColliderStride      = 16; // Vec3 + float
    public const int CapsuleColliderStride     = 32; // Vec3 + float + Vec3 + float
    public const int BoxColliderStride         = 60; // 5*Vec3
    public const int DeltaPosStride            = 12; // int3
    public const int IntStride                 = 4;
    public const int Vec3Stride                = 12; // float3
    public const int RigidBodyStride           = 24; // 2*int + Quaternion
}

// Particle GPU layout — 56 bytes.
// Radius is a global uniform (_ParticleRadius) shared by all particles,
// as per Macklin et al. 2014, section 3: fixed particle radius per scene.
public struct ParticleGPU
{
    public Vector3 position;
    public Vector3 velocity;
    public Vector3 prevPosition;
    public float   invMass;
    public int     phase;
    public Vector3 color;
}

// XPBD distance constraint — 24 bytes.
// restLength == -1 marks a broken constraint (skipped in solve & rendering).
public struct DistanceConstraintGPU
{
    public int   particleA;
    public int   particleB;
    public float restLength;
    public float compliance;   // alpha: 0 = infinitely stiff
    public float breakForce;   // 0 = unbreakable, >0 = force threshold in N
    public float damping;      // beta: 0 = no damping
}

// Sphere collider GPU layout — 16 bytes.
public struct SphereColliderGPU
{
    public Vector3 center;
    public float   radius;
}

// Capsule collider GPU layout — 32 bytes.
public struct CapsuleColliderGPU
{
    public Vector3 pointA;
    public float   radius;
    public Vector3 pointB;
    public float   _pad0;
}

// Oriented box collider GPU layout — 60 bytes.
public struct BoxColliderGPU
{
    public Vector3 center;
    public Vector3 halfExtents;
    public Vector3 axisX;
    public Vector3 axisY;
    public Vector3 axisZ;
}

// Rigid body GPU layout — 24 bytes.
// Particles belonging to this body are stored in a separate flat index
// buffer (_RigidParticleIndices) indexed by [particleOffset, particleOffset+particleCount).
// Rest offsets q_i = x_i^0 - x_cm^0 live in _RigidRestOffsets, parallel
// to the index buffer. The quaternion persists across frames and is
// used as warm-start for the Müller/Bender rotation extraction.
public struct RigidBodyGPU
{
    public int particleOffset;
    public int particleCount;
    public Quaternion quaternion;
}
