// Ported from CombatSolver src/Search/PotionStrategySnapshot.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members Sts2Solver reads.
namespace Sts2Solver.Ai.Combat.Policy;

internal enum PotionDirective
{
    Smart,
    Force,
    Disabled,
}

internal readonly record struct PotionSlotDirective(
    int Slot,
    string PotionId,
    PotionDirective Directive);

internal sealed class PotionStrategySnapshot
{
    private readonly Dictionary<(int Slot, string PotionId), PotionDirective> _directives;
    private readonly bool _onlyForcedUses;

    public PotionStrategySnapshot(
        PotionUseMode defaultPolicy,
        IEnumerable<PotionSlotDirective> directives)
        : this(defaultPolicy, directives, onlyForcedUses: false) { }

    private PotionStrategySnapshot(
        PotionUseMode defaultPolicy,
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

    public PotionUseMode DefaultPolicy { get; }
    public IReadOnlyList<PotionSlotDirective> Directives { get; }

    public bool AllowsExplicitUse(
        int slot,
        string potionId,
        PotionUseMode effectivePolicy,
        bool forceAllDisabled)
    {
        if (forceAllDisabled)
            return false;
        if (_onlyForcedUses)
            return _directives.TryGetValue((slot, potionId), out PotionDirective forced)
                && forced == PotionDirective.Force;
        return _directives.TryGetValue((slot, potionId), out PotionDirective directive)
            ? directive != PotionDirective.Disabled
            : effectivePolicy != PotionUseMode.Disabled;
    }
}
