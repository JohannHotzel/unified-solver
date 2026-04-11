// Hands out unique phase IDs to bodies. Particles that share a phase ID
// do not collide with each other (Macklin et al. 2014, "Unified Particle
// Physics for Real-Time Applications", section 3).

public static class PhaseManager
{
    public const int PhaseNone = 0;

    static int _nextPhase = 1;

    // Reserve and return a new unique phase ID.
    public static int AllocatePhase()
    {
        return _nextPhase++;
    }

    // Reset the phase counter. Call this when wiping the solver state
    // (e.g. SolverManager.ResetSimulation).
    public static void Reset()
    {
        _nextPhase = 1;
    }

    // Highest phase ID handed out so far (0 if none).
    public static int HighestPhase => _nextPhase - 1;
}
