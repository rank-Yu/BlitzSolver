// Ported from CombatSolver src/Runtime/SolverSettings.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace BlitzSolver.Search.Policy;

internal enum PotionUseMode
{
    Disabled,
    Smart,
    RequireAtLeastOne,
}
