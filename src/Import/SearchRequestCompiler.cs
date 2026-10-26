// Ported semantics from CombatSolver src/Flash/Bridge/FlashRouteMaterializer.cs at commit a62f1850
// (MIT, Copyright (c) 2026 Torch1230 and contributors). It assembles the primary Beam request from
// the already imported root, the compiled rules and the captured policy; the difference is that the
// Blitz request uses the kernel's own root/options types instead of the old engine's bridges.
using BlitzSolver.Engine;
using BlitzSolver.Search;
using BlitzSolver.Search.Policy;
using BlitzSolver.Search.Scoring;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BlitzSolver.Import;

/// <summary>Performance tiers exposed by the Flash solver settings.</summary>
public enum SearchPerformanceTier
{
    Low,
    Medium,
    High,
    VeryHigh,
}

/// <summary>
/// The CombatSolver performance profile values the primary Beam reads. The branch caps are carried
/// for the report and for parity with <c>SolverSearchProfile</c>; Blitz enforces the same fixed
/// 32-selection branch cap in the kernel, so no <see cref="BeamOptions"/> field receives them.
/// </summary>
public readonly record struct SearchPerformanceProfile(
    int BeamWidth,
    int MaxExpandedNodes,
    int SoftTimeBudgetMilliseconds,
    int MaxCardBranchesPerNode,
    int MaxPileChoiceBranchesPerAction,
    int MaxHandChoiceBranchesPerAction)
{
    /// <summary>The fixed turn cap of the primary member (<c>FlashRouteMaterializer.cs:360</c>).</summary>
    public const int MaxTurns = 64;

    public static SearchPerformanceProfile For(SearchPerformanceTier tier) => tier switch
    {
        SearchPerformanceTier.Low => new(
            180, 240_000, 60_000, 24, 12, 16),
        SearchPerformanceTier.Medium => new(
            240, 480_000, 120_000, 32, 18, 24),
        SearchPerformanceTier.High => new(
            360, 1_000_000, 180_000, 48, 28, 36),
        SearchPerformanceTier.VeryHigh => new(
            540, 2_000_000, 300_000, 72, 42, 54),
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    public static SearchPerformanceTier ParseTier(string text)
    {
        if (!Enum.TryParse(text, ignoreCase: true, out SearchPerformanceTier tier)
            || !Enum.IsDefined(tier))
        {
            throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                $"未知搜索性能档位：{text}");
        }
        return tier;
    }
}

/// <summary>
/// Assembles a complete <see cref="SearchCase"/> for the primary Beam member. The offline host and
/// the in-game Mod call this with the same imported root and captured policy, so the encoded request
/// is identical on both sides.
/// </summary>
internal static class SearchRequestCompiler
{
    public static SearchCase CompileRequest(
        CombatState state,
        Player player,
        Rules rules,
        Battle root,
        SearchPolicy policy,
        EvaluationRootFacts facts,
        SearchPerformanceProfile profile,
        bool deterministic,
        long searchMemoryBudgetBytes = 0)
    {
        if (profile.BeamWidth <= 0)
            throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                $"Beam 宽度必须为正：{profile.BeamWidth}");
        if (profile.MaxExpandedNodes <= 0)
            throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                $"节点上限必须为正：{profile.MaxExpandedNodes}");
        if (!deterministic && profile.SoftTimeBudgetMilliseconds <= 0)
            throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                $"软时间预算必须为正：{profile.SoftTimeBudgetMilliseconds}");

        Scorer scorer = EvaluationConfigCompiler.CompileScorer(
            state, player, rules, root, policy, facts);
        BoundFeatures bound = EvaluationConfigCompiler.CompileBound(
            state, player, root, policy, facts);
        ScoreRules score = scorer.RulesForCodec;

        // Deterministic test requests run only on the node cap. Zero would stop the solver before
        // the first parent (`OutOfBudget` compares elapsed >= budget); MaxValue disables that clock.
        TimeSpan softTime = deterministic
            ? TimeSpan.MaxValue
            : TimeSpan.FromMilliseconds(profile.SoftTimeBudgetMilliseconds);
        BeamOptions options = new(
            profile.BeamWidth,
            profile.MaxExpandedNodes,
            SearchPerformanceProfile.MaxTurns,
            softTime,
            root.Player.MaxHp)
        {
            Scorer = scorer,
            Bound = bound,
            Ordering = new RouteOrdering(
                root.Player.CurrentHp,
                root.Player.MaxHp,
                score.BossHpRelief,
                score.PostCombatRelicHeal),
            Potions = DefaultPotionPolicy(),
            EffectivePotionPolicy = PotionUseMode.Smart,
            TheftPolicy = policy.TheftPolicy is { } theft ? (TheftPolicy)(int)theft : null,
            InitialActionCount = facts.InitialActionCount,
            InitialExplicitPotionUses = facts.InitialExplicitPotionUses,
            InitialAutomaticPotionUses = facts.InitialAutomaticPotionUses,
            AmbergrisPotionId = score.AmbergrisPotionId,
            Act3BossStrategy = policy.Act3BossStrategy,
            InitialPlayerTurn = root.PlayerTurn,
            InitialSearchTurn = root.PlayerTurn,
            SecondRankBandWidth = profile.BeamWidth,
            PreserveReplayAllocatorOpening = HasUnusedCardReplayAllocator(player),
            SearchMemoryBudgetBytes = searchMemoryBudgetBytes,
        };
        return new SearchCase(rules, [root], options);
    }

    private static PotionPolicy DefaultPotionPolicy()
        => new(
            int.MinValue,
            MaximumUses: null,
            new PotionStrategySnapshot(PotionUseMode.Smart, Array.Empty<PotionSlotDirective>()),
            PotionUseMode.Smart,
            ForceAllDisabled: false);

    /// <summary><c>CombatRootSnapshot.HasUnusedCardReplayAllocator</c>.</summary>
    private static bool HasUnusedCardReplayAllocator(Player player)
    {
        foreach (RelicModel relic in player.Relics)
        {
            if (!relic.IsMelted && relic is ThrowingAxe axe)
                return !NativeAccess.ThrowingAxeUsed(axe);
        }
        return false;
    }
}
