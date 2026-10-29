// Ported from CombatSolver src/Search/SolverSearchProfile.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members Sts2Solver reads.
namespace Sts2Solver.Ai.Combat.Scoring;

internal enum BeamWeightTerm { CurrentEnergy, PersistentBuffDelta, EnemyHp }

internal sealed record BeamWeightPerturbation
{
    public BeamWeightTerm Term { get; }
    public double Scale { get; }

    public BeamWeightPerturbation(BeamWeightTerm term, double scale)
    {
        if (!Enum.IsDefined(term))
            throw new ArgumentOutOfRangeException(nameof(term));
        if (!double.IsFinite(scale) || scale < 0 || scale > 2)
            throw new ArgumentOutOfRangeException(nameof(scale));
        Term = term;
        Scale = scale;
    }
}
