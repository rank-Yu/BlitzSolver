// Mid-combat live import. The native game removes dead enemies and has no source for a handful of
// old-search ledgers; this tracker keeps the stable known-enemy order and carries those ledgers so
// the reconciliation projection can still be compared byte for byte. Reference semantics follow
// CombatSolver src/Runtime/ContinuationStamp.cs at a62f1850 (MIT, Copyright (c) 2026 Torch1230 and
// contributors), but the field set is BlitzBattle-only and is documented in docs/mod.md.
using BlitzSolver.Engine;
using BlitzSolver.Import;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using NativeCreature = MegaCrit.Sts2.Core.Entities.Creatures.Creature;

namespace BlitzSolver.Import;

internal sealed class LiveCombatTracker
{
    /// <summary>
    /// Ledgers the native game does not expose and the old simulator accumulated during an action.
    /// They are carried from the last predicted boundary; all other counters are imported from
    /// native history or current state and compared strictly.
    /// </summary>
    private static readonly Counter[] CarriedCounters =
    [
        Counter.RecoveredHp,
        Counter.LongTermResourceValue,
        Counter.AngerCopiesGenerated,
        Counter.DeathSaveRelicHpRestored,
        Counter.DeathSavePotionHpRestored,
        Counter.DeathSaveUseCount,
        Counter.AutomaticPotionUseCount,
        Counter.GeneratedPotionUseCount,
        Counter.GeneratedPotionStrategicCost,
        Counter.EntropicFreePotionMask,
        Counter.VitalSparkNormalizedAmount,
        Counter.GrowthRewardsHandOfGreedTheHunt,
        Counter.GrowthRewardsFeedRoyalties,
        Counter.GrowthRewardsAlchemizeGeneticAlgorithm,
        Counter.GrowthRewardsTheScytheGoopy,
        Counter.GrowthRewardsForbiddenGrimoireMadScience,
    ];

    private readonly List<NativeCreature> _known;
    private readonly HashSet<CardModel> _rootCards;
    private readonly int _rootEnemyCount;
    private int _shufflesCrossed;
    private bool _hasRisk;
    private bool _hasPredicted;
    private Battle _predicted;

    internal LiveCombatTracker(CombatState state)
    {
        _known = [.. state.Enemies];
        Player? player = state.Players.Count == 1 ? state.Players[0] : null;
        _rootCards = new HashSet<CardModel>(ReferenceEqualityComparer.Instance);
        if (player?.PlayerCombatState is { } combatState)
        {
            foreach (CardModel card in combatState.AllCards)
                _rootCards.Add(card);
        }
        _rootEnemyCount = _known.Count;
    }

    internal int RootEnemyCount => _rootEnemyCount;

    internal IReadOnlyList<NativeCreature> KnownEnemies => _known;

    /// <summary>Refreshes the known order with summons that appeared after the combat root.</summary>
    internal void Refresh(CombatState state)
    {
        // CombatState.Enemies is sorted by encounter slot, not creation order. The predicted enemy
        // table appends in creation order after the frozen root rows, so recover that order from the
        // monotonically assigned CombatId before appending; otherwise Ovicopter's eggs (and any
        // multi-summon move) land at different indices than the simulation.
        List<NativeCreature> added = [];
        foreach (NativeCreature creature in state.Enemies)
        {
            if (creature.IsAlive && !Contains(creature))
                added.Add(creature);
        }
        added.Sort(static (left, right)
            => (left.CombatId ?? int.MaxValue).CompareTo(right.CombatId ?? int.MaxValue));
        foreach (NativeCreature creature in added)
            _known.Add(creature);
    }

    /// <summary>Imports a live search root, carrying the last observed non-native ledgers.</summary>
    internal Battle ImportRoot(CombatState state, Player player, Rules rules)
    {
        Refresh(state);
        ImportOptions options = new()
        {
            KnownEnemies = _known,
            AllowDeadEnemies = true,
            RootEnemyCount = _rootEnemyCount,
            ShufflesCrossed = _shufflesCrossed,
            HasRisk = _hasRisk,
            RootCards = _rootCards,
            Reconciliation = true,
        };
        if (_hasPredicted)
            options = options with { RevivingEnemies = RevivingEnemies(in _predicted) };
        Battle live = BattleImporter.Import(state, player, rules, options);
        if (_hasPredicted)
            CarryNonNative(ref live, _predicted);
        return live;
    }

    /// <summary>Imports a live boundary for comparison with the predicted route state.</summary>
    internal Battle ImportForComparison(CombatState state, Player player, Rules rules, in Battle expected)
    {
        Refresh(state);
        ImportOptions options = new()
        {
            KnownEnemies = _known,
            AllowDeadEnemies = true,
            RootEnemyCount = _rootEnemyCount,
            ShufflesCrossed = expected.ShufflesCrossed,
            HasRisk = (expected.Flags & BattleFlags.HasRisk) != 0,
            RootCards = _rootCards,
            RevivingEnemies = RevivingEnemies(in expected),
            Reconciliation = true,
        };
        Battle live = BattleImporter.Import(state, player, rules, options);
        CarryNonNative(ref live, expected);
        return live;
    }

    /// <summary>Records the last predicted state; it supplies the carried ledgers of later roots.</summary>
    internal void Observe(in Battle predicted)
    {
        _predicted = predicted;
        _hasPredicted = true;
        _shufflesCrossed = predicted.ShufflesCrossed;
        _hasRisk = (predicted.Flags & BattleFlags.HasRisk) != 0;
    }

    private static void CarryNonNative(ref Battle live, in Battle predicted)
    {
        foreach (Counter counter in CarriedCounters)
            live.SetCounter(counter, predicted.Counter(counter));
        // Shuffle counters are search-only: ShuffleEventCount is an action-local kernel counter and
        // ShufflesCrossed is a derived route-score key. The importer cannot recover either from the
        // native game; the predicted values are carried so later searches keep the same scoring
        // lineage. Card piles and all nine RNG streams remain strictly compared.
        live.ShuffleEventCount = predicted.ShuffleEventCount;
        live.ShufflesCrossed = predicted.ShufflesCrossed;
        live.Flags = (live.Flags & ~BattleFlags.HasRisk) | (predicted.Flags & BattleFlags.HasRisk);
    }

    private static IReadOnlySet<int> RevivingEnemies(in Battle expected)
    {
        HashSet<int> reviving = [];
        for (int index = 0; index < expected.EnemyCount; index++)
        {
            if (expected.Enemies[index].DeathPhase == DeathPhase.Reviving)
                reviving.Add(index);
        }
        return reviving;
    }

    private bool Contains(NativeCreature creature)
    {
        for (int index = 0; index < _known.Count; index++)
        {
            if (ReferenceEquals(_known[index], creature))
                return true;
        }
        return false;
    }
}
