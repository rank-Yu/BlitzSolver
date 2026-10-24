// Ported from CombatSolver src/Search/PotionStrategySnapshot.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

internal enum SolverPotionDirective
{
    Smart,
    Force,
    Disabled,
}

internal readonly record struct PotionSlotDirective(
    int Slot,
    string PotionId,
    SolverPotionDirective Directive);

internal sealed class PotionStrategySnapshot
{
    private readonly Dictionary<(int Slot, string PotionId), SolverPotionDirective> _directives;
    private readonly bool _onlyForcedUses;

    public PotionStrategySnapshot(
        SolverPotionPolicy defaultPolicy,
        IEnumerable<PotionSlotDirective> directives)
        : this(defaultPolicy, directives, onlyForcedUses: false) { }

    private PotionStrategySnapshot(
        SolverPotionPolicy defaultPolicy,
        IEnumerable<PotionSlotDirective> directives,
        bool onlyForcedUses)
    {
        DefaultPolicy = defaultPolicy;
        _onlyForcedUses = onlyForcedUses;
        _directives = directives.ToDictionary(
            directive => (directive.Slot, directive.PotionId),
            directive => directive.Directive);
        Directives = _directives
            .Select(item => new PotionSlotDirective(item.Key.Slot, item.Key.PotionId, item.Value))
            .OrderBy(item => item.Slot)
            .ToArray();
    }

    public SolverPotionPolicy DefaultPolicy { get; }
    public IReadOnlyList<PotionSlotDirective> Directives { get; }

    public bool AllowsExplicitUse(
        int slot,
        string potionId,
        SolverPotionPolicy effectivePolicy,
        bool forceAllDisabled)
    {
        if (forceAllDisabled)
            return false;
        if (_onlyForcedUses)
            return _directives.TryGetValue((slot, potionId), out SolverPotionDirective forced)
                && forced == SolverPotionDirective.Force;
        return _directives.TryGetValue((slot, potionId), out SolverPotionDirective directive)
            ? directive != SolverPotionDirective.Disabled
            : effectivePolicy != SolverPotionPolicy.Disabled;
    }
}
