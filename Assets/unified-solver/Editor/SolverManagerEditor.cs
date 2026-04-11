using UnityEditor;
using UnityEngine;

// Custom inspector for SolverManager. Shows live counts and CPU-side
// timing breakdown while in play mode, plus capacity / cell-size sanity
// warnings and a couple of utility buttons.
[CustomEditor(typeof(SolverManager))]
public class SolverManagerEditor : Editor
{
    int  _brokenCount      = -1;
    bool _statsFoldout     = true;
    bool _validationFoldout = true;

    public override bool RequiresConstantRepaint()
    {
        // Keep ms / particle counts updating live in play mode.
        return Application.isPlaying;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var mgr = (SolverManager)target;

        EditorGUILayout.Space();
        DrawValidation(mgr);

        EditorGUILayout.Space();
        DrawStats(mgr);

        EditorGUILayout.Space();
        DrawButtons(mgr);
    }

    void DrawValidation(SolverManager mgr)
    {
        _validationFoldout = EditorGUILayout.Foldout(_validationFoldout, "Validation", true);
        if (!_validationFoldout) return;

        // cellSize must be >= 2 * largest particle radius for the spatial
        // hash to find every potential pair in the 3x3x3 neighborhood.
        float minCellSize = 2f * mgr.defaultRadius;
        if (mgr.cellSize < minCellSize)
        {
            EditorGUILayout.HelpBox(
                $"Cell size ({mgr.cellSize:F3}) is below 2 * defaultRadius ({minCellSize:F3}). " +
                "Spatial hash may miss collision pairs.",
                MessageType.Warning);
        }

        if (mgr.tableSize < mgr.maxParticles)
        {
            EditorGUILayout.HelpBox(
                $"tableSize ({mgr.tableSize}) is smaller than maxParticles ({mgr.maxParticles}). " +
                "Hash collisions will degrade performance — pick a prime ~2-3x maxParticles.",
                MessageType.Warning);
        }

        if (mgr.frictionKinetic > mgr.frictionStatic)
        {
            EditorGUILayout.HelpBox(
                $"frictionKinetic ({mgr.frictionKinetic}) > frictionStatic ({mgr.frictionStatic}). " +
                "Coulomb model expects mu_k <= mu_s.",
                MessageType.Warning);
        }

        if (mgr.substeps < 1)
        {
            EditorGUILayout.HelpBox("substeps must be >= 1.", MessageType.Error);
        }

        if (Application.isPlaying)
        {
            float fillP = mgr.ActiveCount    / (float)Mathf.Max(1, mgr.maxParticles);
            float fillC = mgr.ConstraintCount / (float)Mathf.Max(1, mgr.maxConstraints);
            if (fillP > 0.95f)
                EditorGUILayout.HelpBox($"Particle buffer is {fillP*100f:F0}% full ({mgr.ActiveCount}/{mgr.maxParticles}).", MessageType.Warning);
            if (fillC > 0.95f)
                EditorGUILayout.HelpBox($"Constraint buffer is {fillC*100f:F0}% full ({mgr.ConstraintCount}/{mgr.maxConstraints}).", MessageType.Warning);
        }
    }

    void DrawStats(SolverManager mgr)
    {
        _statsFoldout = EditorGUILayout.Foldout(_statsFoldout, "Live Stats (play mode only)", true);
        if (!_statsFoldout) return;

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play mode to see live stats.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Particles",     $"{mgr.ActiveCount} / {mgr.maxParticles}");
        EditorGUILayout.LabelField("Constraints",   $"{mgr.ConstraintCount} / {mgr.maxConstraints}");
        EditorGUILayout.LabelField("Broken",        _brokenCount >= 0 ? _brokenCount.ToString() : "(press Refresh)");
        EditorGUILayout.LabelField("Sphere col.",   $"{mgr.SphereColliderCount} / {mgr.maxSphereColliders}");
        EditorGUILayout.LabelField("Capsule col.",  $"{mgr.CapsuleColliderCount} / {mgr.maxCapsuleColliders}");
        EditorGUILayout.LabelField("Box col.",      $"{mgr.BoxColliderCount} / {mgr.maxBoxColliders}");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("CPU dispatch timing (rough — Dispatch is async)", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField("  Total",     $"{mgr.LastFrameTotalMs:F2} ms");
        EditorGUILayout.LabelField("  Upload",    $"{mgr.LastFrameUploadMs:F2} ms");
        EditorGUILayout.LabelField("  Hash",      $"{mgr.LastFrameHashMs:F2} ms");
        EditorGUILayout.LabelField("  Substeps",  $"{mgr.LastFrameSubstepsMs:F2} ms ({mgr.substeps}x)");
    }

    void DrawButtons(SolverManager mgr)
    {
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Refresh Broken Constraint Count"))
                _brokenCount = mgr.CountBrokenConstraints();

            if (GUILayout.Button("Reset Simulation"))
                mgr.ResetSimulation();

            if (GUILayout.Button("Log Buffer Stats"))
            {
                Debug.Log(
                    $"[SolverManager] particles={mgr.ActiveCount}/{mgr.maxParticles}, " +
                    $"constraints={mgr.ConstraintCount}/{mgr.maxConstraints}, " +
                    $"sphere={mgr.SphereColliderCount}, capsule={mgr.CapsuleColliderCount}, box={mgr.BoxColliderCount}, " +
                    $"total={mgr.LastFrameTotalMs:F2}ms (upload={mgr.LastFrameUploadMs:F2}, hash={mgr.LastFrameHashMs:F2}, substeps={mgr.LastFrameSubstepsMs:F2})");
            }
        }
    }
}
