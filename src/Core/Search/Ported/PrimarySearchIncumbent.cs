// Ported from CombatSolver src/Search/CombatBeamSolver.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace BlitzSolver.Search;

internal readonly record struct PrimarySearchIncumbent(
    int StrategicHpDeficit,
    int CombatEndedTurn,
    int? ExplicitPotionStrategicCost = null);
