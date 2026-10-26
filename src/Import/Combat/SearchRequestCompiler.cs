// Ported semantics from CombatSolver src/Flash/Bridge/FlashRouteMaterializer.cs at commit a62f1850
// (MIT, Copyright (c) 2026 Torch1230 and contributors). It assembles the primary Beam request from
// the already imported root, the compiled rules and the captured policy; the difference is that the
// Blitz request uses the kernel's own root/options types instead of the old engine's bridges.
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Ai.Combat;
using BlitzSolver.Ai.Combat.Policy;
using BlitzSolver.Ai.Combat.Scoring;
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
    ExtraHigh,
}

/// <summary>
/// The performance tiers exposed by the Flash solver settings. A decision starts at
/// <see cref="StartWidth"/> and doubles the beam until <see cref="MaxWidth"/>; every round's node
/// cap is <c>width × MaxExpandedNodesPerWidth</c>, and all rounds of one decision share
/// <see cref="TotalTimeBudgetMilliseconds"/> in the game. The branch caps are carried for the
/// report and for parity with <c>SolverSearchProfile</c>; Blitz enforces the same fixed
/// 32-selection branch cap in the kernel, so no <see cref="BeamOptions"/> field receives them.
/// </summary>
public readonly record struct SearchPerformanceProfile(
    int StartWidth,
    int MaxWidth,
    int MaxExpandedNodesPerWidth,
    int TotalTimeBudgetMilliseconds,
    int MaxCardBranchesPerNode,
    int MaxPileChoiceBranchesPerAction,
    int MaxHandChoiceBranchesPerAction)
{
    /// <summary>The fixed turn cap of the primary member (<c>FlashRouteMaterializer.cs:360</c>).</summary>
    public const int MaxTurns = 64;

    public int StartMaxExpandedNodes
    {
        get
        {
            long nodes = (long)StartWidth * MaxExpandedNodesPerWidth;
            return nodes <= int.MaxValue
                ? (int)nodes
                : throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                    $"起始宽度 {StartWidth} × 每宽节点 {MaxExpandedNodesPerWidth} 超过 32 位上限。");
        }
    }

    public static SearchPerformanceProfile For(SearchPerformanceTier tier) => tier switch
    {
        SearchPerformanceTier.Low => new(
            256, 256, 2_000, 60_000, 24, 12, 16),
        SearchPerformanceTier.Medium => new(
            256, 512, 2_000, 120_000, 32, 18, 24),
        SearchPerformanceTier.High => new(
            256, 1_024, 2_000, 180_000, 48, 28, 36),
        SearchPerformanceTier.ExtraHigh => new(
            256, 2_048, 2_000, 240_000, 72, 42, 54),
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    public static SearchPerformanceTier ParseTier(string text)
    {
        // The old fourth tier name was renamed; keep rejecting it explicitly so a stale config
        // or script gets a migration message instead of a generic unknown-tier error.
        if (string.Equals(text.Trim(), "VeryHigh", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                $"搜索性能档位 VeryHigh 已更名为 ExtraHigh，旧名不再接受；请改用 ExtraHigh。");
        }
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
        if (profile.StartWidth <= 0 || profile.MaxWidth < profile.StartWidth)
            throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                $"Beam 宽度范围无效：{profile.StartWidth}..{profile.MaxWidth}");
        if (profile.MaxExpandedNodesPerWidth <= 0)
            throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                $"每轮节点上限系数必须为正：{profile.MaxExpandedNodesPerWidth}");
        if (!deterministic && profile.TotalTimeBudgetMilliseconds <= 0)
            throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                $"决策总时间必须为正：{profile.TotalTimeBudgetMilliseconds}");

        Scorer scorer = EvaluationConfigCompiler.CompileScorer(
            state, player, rules, root, policy, facts);
        BoundFeatures bound = EvaluationConfigCompiler.CompileBound(
            state, player, root, policy, facts);
        ScoreRules score = scorer.RulesForCodec;

        // Deterministic test requests run only on the node cap. Zero would stop the solver before
        // the first parent (`OutOfBudget` compares elapsed >= budget); MaxValue disables that clock.
        TimeSpan softTime = deterministic
            ? TimeSpan.MaxValue
            : TimeSpan.FromMilliseconds(profile.TotalTimeBudgetMilliseconds);
        BeamOptions options = new(
            profile.StartWidth,
            profile.StartMaxExpandedNodes,
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
            SecondRankBandWidth = profile.StartWidth,
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
