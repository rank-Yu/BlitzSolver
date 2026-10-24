// Ported from CombatSolver src/Search/NoveltySearchTelemetry.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

internal sealed record NoveltySearchImprovement(long ElapsedMs, int Expanded, int Loss, int Potions, int Turn);

internal sealed record NoveltySearchTelemetry(
    long Generated, int PeakOpen, int HorizonLeaves, int NonNovelPruned,
    int FamiliarAdmitted, int OpenDropped, int Wins, long? FirstVictoryMs,
    int? FirstVictoryExpanded, int? MinimumCumulativeLoss, int? MinimumLossExpanded,
    long? MinimumLossMs, string Stop, int NoveltyEntries, int NoveltyAtoms,
    int NoveltyPartitions, long Unary, long Binary, long Familiar,
    IReadOnlyList<NoveltySearchImprovement> Improvements);
