// Ported from CombatSolver src/Search/CombatBeamSolver.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace BlitzSolver.Ai.Combat.Quality;

internal readonly record struct SearchIncumbent(
    int StrategicHpDeficit,
    int CombatEndedTurn,
    int? ExplicitPotionStrategicCost = null);
