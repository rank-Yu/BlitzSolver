// Ported from CombatSolver src/Search/CombatPlan.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace BlitzSolver.Ai.Combat;

[Flags]
internal enum SearchRouteTraits
{
    None = 0,
    Scaling = 1 << 0,
    Resource = 1 << 1,
    Control = 1 << 2,
    RevivalWindow = 1 << 3,
    DeclinedExtraTurn = 1 << 4,
    ReactiveDamage = 1 << 5,
    EndTurnDeckCompression = 1 << 6,
    LongTermResource = 1 << 7,
    HpInvestment = 1 << 8,
}

[Flags]
internal enum PersistentSetupTraits
{
    None = 0,
    Curious = 1 << 0,
    EchoForm = 1 << 1,
    Buffer = 1 << 2,
    Focus = 1 << 3,
    Thunder = 1 << 4,
    RecurringScaling = 1 << 5,
    OrbEngine = 1 << 6,
}
