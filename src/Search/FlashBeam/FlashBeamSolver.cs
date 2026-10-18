using System.Diagnostics;
using CombatSolver.Engine.InCombat.Flash;

namespace CombatSolver.FlashBeam;

internal sealed record FlashBeamOptions(
    int BeamWidth,
    int MaxExpandedNodes,
    int MaxTurns,
    TimeSpan SoftTimeBudget,
    int InitialPlayerMaxHp)
{
    /// <summary>Share of each beam layer reserved for the most offensive states (lowest enemy HP).</summary>
    public double OffensiveShare { get; init; } = 0.25;

    /// <summary>Which root potions may be used and what each use costs; disabled unless the caller sets it.</summary>
    public FlashPotionPolicy Potions { get; init; } = FlashPotionPolicy.Disabled;
}

/// <summary>
/// The existing search's potion constraints at the root (<c>EnumeratePlannedPotionActions</c>): slots whose
/// potion may be used explicitly, the earliest player turn, the maximum number of uses, and each slot's
/// strategic HP cost (<c>PotionUsePolicy.StrategicHpCost</c>). Only the potions present at the root are
/// covered; a potion gained during the search is not offered.
/// </summary>
internal sealed record FlashPotionPolicy(byte AllowedSlots, int EarliestPlayerTurn, int? MaximumUses, int[] SlotCosts)
{
    public static FlashPotionPolicy Disabled { get; } = new(0, int.MaxValue, 0, new int[FlashLimits.PotionSlots]);
}

internal sealed record FlashBeamResult(
    FlashAction[] BestRoute,
    bool BestIsVictory,
    bool BestIsDefeat,
    double BestScore,
    int ExpandedNodes,
    int Transitions,
    int Duplicates,
    int TurnsSearched,
    TimeSpan Elapsed);

/// <summary>
/// Turn-layered beam search over <see cref="FlashBattle"/>. Within a turn it expands card plays
/// depth by depth; end-turn children seed the next turn layer. Children are deduplicated by full
/// byte equality and the best score is kept. Only <see cref="FlashBattle"/> and
/// <see cref="FlashRules"/> are touched; translation and materialization happen in the bridge.
/// </summary>
internal sealed class FlashBeamSolver
{
    private const double DeathPenalty = -1_000_000_000_000d;
    private const double VictoryBonus = 10_000_000_000d;
    private const double HpWeight = 30_000d;
    private const double EnemyHpWeight = -10_000d;
    private const double ActionPenalty = -1d;
    /// <summary>Selections explored per choice request (lexicographic within each size).</summary>
    private const int MaxChoiceBranches = 32;

    private readonly FlashRules _rules;
    private readonly FlashBeamOptions _options;
    private readonly List<(int Parent, FlashAction Action)> _nodes = new(4096);
    private readonly FlashAction[] _actions = new FlashAction[FlashKernel.MaxLegalActions];
    private readonly ushort[] _rootPotions = new ushort[FlashLimits.PotionSlots];
    private int _expanded;
    private int _transitions;
    private int _duplicates;

    public FlashBeamSolver(FlashRules rules, FlashBeamOptions options)
    {
        _rules = rules;
        _options = options;
    }

    public FlashBeamResult Solve(in FlashBattle root, CancellationToken cancellationToken)
    {
        Stopwatch watch = Stopwatch.StartNew();
        for (int slot = 0; slot < FlashLimits.PotionSlots; slot++)
            _rootPotions[slot] = slot < root.PotionSlotCount ? root.Potions[slot] : (ushort)0;
        _nodes.Clear();
        _nodes.Add((-1, default));
        int width = Math.Max(1, _options.BeamWidth);
        int poolCapacity = width * (FlashKernel.MaxLegalActions + 1);
        Pool current = new(poolCapacity);
        Pool next = new(poolCapacity);
        Pool ended = new(poolCapacity);
        Pool turnFrontier = new(poolCapacity);

        double bestScore = double.NegativeInfinity;
        int bestNode = 0;
        bool bestVictory = false, bestDefeat = false;
        void ConsiderTerminal(in FlashBattle state, int node, double score)
        {
            if (score > bestScore)
            {
                bestScore = score;
                bestNode = node;
                bestVictory = state.Outcome == FlashOutcome.Victory;
                bestDefeat = state.Outcome == FlashOutcome.Defeat;
            }
        }

        turnFrontier.Add(root, 0, Score(root, 0));
        int turns = 0;
        while (turnFrontier.Count > 0 && turns < _options.MaxTurns && !OutOfBudget(watch))
        {
            turns++;
            current.Clear();
            turnFrontier.CopyTopTo(current, width);
            ended.Clear();
            while (current.Count > 0 && !OutOfBudget(watch))
            {
                next.Clear();
                for (int index = 0; index < current.Count && !OutOfBudget(watch); index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _expanded++;
                    ref FlashBattle parent = ref current.State(index);
                    int parentNode = current.Node(index);
                    int count = FlashKernel.LegalActions(parent, _rules, _actions);
                    for (int actionIndex = 0; actionIndex < count; actionIndex++)
                    {
                        if (_actions[actionIndex].Kind == FlashActionKind.UsePotion && !PotionAllowed(parent, _actions[actionIndex].PotionSlot))
                            continue;
                        ExpandAction(ref parent, parentNode, _actions[actionIndex], next, ended, ConsiderTerminal);
                    }
                }
                current.Clear();
                next.CopyTopTo(current, width, _options.OffensiveShare);
            }
            turnFrontier.Clear();
            ended.CopyTopTo(turnFrontier, width, _options.OffensiveShare);
        }

        // Without a terminal route, fall back to the best surviving turn-start state.
        if (double.IsNegativeInfinity(bestScore) || (!bestVictory && turnFrontier.Count > 0 && turnFrontier.BestScore() > bestScore))
        {
            int best = turnFrontier.BestIndex();
            if (best >= 0)
            {
                bestScore = turnFrontier.Score(best);
                bestNode = turnFrontier.Node(best);
                bestVictory = false;
                bestDefeat = false;
            }
        }
        return new FlashBeamResult(Route(bestNode), bestVictory, bestDefeat, bestScore,
            _expanded, _transitions, _duplicates, turns, watch.Elapsed);
    }

    private delegate void TerminalSink(in FlashBattle state, int node, double score);

    private void ExpandAction(ref FlashBattle parent, int parentNode, FlashAction action, Pool next, Pool ended, TerminalSink terminal)
    {
        FlashBattle child = parent;
        _transitions++;
        if (FlashKernel.Step(ref child, action, _rules, out FlashChoiceRequest request) == FlashStepResult.NeedsChoice)
        {
            // Every executed selection is exact; the cap only bounds how many are explored.
            Span<ulong> selections = stackalloc ulong[MaxChoiceBranches];
            int count = FlashChoiceEnumerator.Enumerate(request, selections);
            for (int index = 0; index < count; index++)
                ExpandAction(ref parent, parentNode, action.WithChoice(selections[index]), next, ended, terminal);
            return;
        }
        int node = _nodes.Count;
        _nodes.Add((parentNode, action));
        double score = Score(child, Depth(node));
        if (!FlashKernel.IsInProgress(child))
        {
            terminal(child, node, score);
            return;
        }
        Pool target = action.Kind == FlashActionKind.EndTurn || child.PlayerTurn != parent.PlayerTurn ? ended : next;
        if (!target.Add(child, node, score))
            _duplicates++;
    }

    private bool OutOfBudget(Stopwatch watch)
        => _expanded >= _options.MaxExpandedNodes || watch.Elapsed >= _options.SoftTimeBudget;

    private int Depth(int node)
    {
        int depth = 0;
        for (int cursor = node; _nodes[cursor].Parent >= 0; cursor = _nodes[cursor].Parent)
            depth++;
        return depth;
    }

    private FlashAction[] Route(int node)
    {
        List<FlashAction> route = [];
        for (int cursor = node; _nodes[cursor].Parent >= 0; cursor = _nodes[cursor].Parent)
            route.Add(_nodes[cursor].Action);
        route.Reverse();
        return [.. route];
    }

    /// <summary>
    /// Core terms of the existing <c>CombatBeamSolver.Snapshot</c> score: projected HP after the
    /// stand-pat enemy threat, max-HP change, cumulative HP lost, victory bonus, enemy HP and the
    /// action penalty.
    /// </summary>
    private double Score(in FlashBattle battle, int actionCount)
    {
        bool dead = battle.Player.IsDead || battle.Outcome == FlashOutcome.Defeat;
        bool won = !dead && battle.Outcome == FlashOutcome.Victory;
        int projectedHp = won ? battle.Player.CurrentHp : ProjectHpAfterThreat(battle);
        double score = dead || projectedHp <= 0 ? DeathPenalty : projectedHp * HpWeight;
        score += (battle.Player.MaxHp - _options.InitialPlayerMaxHp) * HpWeight;
        score -= battle.Counter(FlashCounter.CumulativeHpLost) * HpWeight;
        if (won)
            score += VictoryBonus;
        int enemyHp = 0;
        for (int index = 0; index < battle.EnemyCount; index++)
            enemyHp += Math.Max(0, battle.Enemies[index].Creature.CurrentHp);
        score += enemyHp * EnemyHpWeight;
        score += actionCount * ActionPenalty;
        score -= UsedPotionCost(battle) * HpWeight;
        return score;
    }

    /// <summary>A slot still holds its root potion and the policy offers it now.</summary>
    private bool PotionAllowed(in FlashBattle battle, int slot)
    {
        FlashPotionPolicy policy = _options.Potions;
        if ((policy.AllowedSlots & (1 << slot)) == 0 || battle.PlayerTurn < policy.EarliestPlayerTurn)
            return false;
        if (battle.Potions[slot] == 0 || battle.Potions[slot] != _rootPotions[slot])
            return false;
        if (policy.MaximumUses is { } maximum && UsedPotions(battle) >= maximum)
            return false;
        return true;
    }

    private int UsedPotions(in FlashBattle battle)
    {
        int used = 0;
        for (int slot = 0; slot < FlashLimits.PotionSlots; slot++)
        {
            if (_rootPotions[slot] != 0 && (slot >= battle.PotionSlotCount || battle.Potions[slot] != _rootPotions[slot]))
                used++;
        }
        return used;
    }

    private int UsedPotionCost(in FlashBattle battle)
    {
        int cost = 0;
        for (int slot = 0; slot < FlashLimits.PotionSlots; slot++)
        {
            if (_rootPotions[slot] != 0 && (slot >= battle.PotionSlotCount || battle.Potions[slot] != _rootPotions[slot]))
                cost += _options.Potions.SlotCosts[slot];
        }
        return cost;
    }

    /// <summary>Mirrors <c>ProjectHpAfterThreat</c> for the supported monsters: floored hits absorbed by block.</summary>
    private int ProjectHpAfterThreat(in FlashBattle battle)
    {
        FlashBattle probe = battle;
        FlashExec x = new(ref probe, _rules, default);
        int hp = battle.Player.CurrentHp;
        int block = battle.Player.Block;
        for (int position = 0; position < battle.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(battle.Roster[position]);
            ref readonly FlashEnemy enemy = ref battle.Enemies[battle.Roster[position]];
            if (enemy.Creature.IsDead || (enemy.Flags & FlashEnemyFlags.SkipNextMove) != 0)
                continue;
            FlashMonsterNode move = _rules.Monster(owner).Nodes[enemy.Move];
            foreach (FlashMonsterAttack attack in move.Attacks)
            {
                for (int hit = 0; hit < Math.Max(attack.Repeats, 1); hit++)
                {
                    decimal modified = FlashHooks.ModifyDamage(ref x, FlashLimits.PlayerOwner, owner, attack.BaseDamage,
                        FlashValueProps.Move, false, default);
                    int damage = Math.Max(0, (int)Math.Floor(modified));
                    int blocked = Math.Min(block, damage);
                    block -= blocked;
                    hp -= damage - blocked;
                }
            }
        }
        return hp;
    }

    /// <summary>Preallocated state pool with byte-exact deduplication keeping the best score.</summary>
    private sealed class Pool
    {
        private readonly FlashBattle[] _states;
        private readonly int[] _nodes;
        private readonly double[] _scores;
        private readonly ulong[] _hashes;
        private readonly int[] _table;
        private readonly int[] _order;
        private readonly int[] _enemyHp;
        private readonly bool[] _taken;

        public Pool(int capacity)
        {
            _states = new FlashBattle[capacity];
            _nodes = new int[capacity];
            _scores = new double[capacity];
            _hashes = new ulong[capacity];
            _order = new int[capacity];
            _enemyHp = new int[capacity];
            _taken = new bool[capacity];
            int size = 1;
            while (size < capacity * 2)
                size <<= 1;
            _table = new int[size];
        }

        public int Count { get; private set; }

        public ref FlashBattle State(int index) => ref _states[index];

        public int Node(int index) => _nodes[index];

        public double Score(int index) => _scores[index];

        public void Clear()
        {
            Count = 0;
            Array.Clear(_table);
        }

        /// <summary>Adds a state; returns false when an identical state was already present.</summary>
        public bool Add(in FlashBattle state, int node, double score)
        {
            ulong hash = FlashFingerprint.Hash(state);
            int mask = _table.Length - 1;
            int slot = (int)(hash & (ulong)mask);
            while (_table[slot] != 0)
            {
                int existing = _table[slot] - 1;
                if (_hashes[existing] == hash && FlashFingerprint.Equal(_states[existing], state))
                {
                    if (score > _scores[existing])
                    {
                        _scores[existing] = score;
                        _nodes[existing] = node;
                    }
                    return false;
                }
                slot = (slot + 1) & mask;
            }
            if (Count >= _states.Length)
                throw new FlashCapacityException("FlashBeam.Pool", _states.Length);
            int index = Count++;
            _states[index] = state;
            _nodes[index] = node;
            _scores[index] = score;
            _hashes[index] = hash;
            _table[slot] = index + 1;
            int enemyHp = 0;
            for (int enemy = 0; enemy < state.EnemyCount; enemy++)
                enemyHp += Math.Max(0, state.Enemies[enemy].Creature.CurrentHp);
            _enemyHp[index] = enemyHp;
            return true;
        }

        public void CopyTopTo(Pool target, int width) => CopyTopTo(target, width, 0d);

        /// <summary>
        /// Keeps the best <paramref name="width"/> states: the score leaders, plus a reserved share of the
        /// lowest-enemy-HP states so offensive lines survive a defensive score bias.
        /// </summary>
        public void CopyTopTo(Pool target, int width, double offensiveShare)
        {
            int count = Count;
            if (count == 0)
                return;
            for (int index = 0; index < count; index++)
            {
                _order[index] = index;
                _taken[index] = false;
            }
            double[] scores = _scores;
            Array.Sort(_order, 0, count, Comparer<int>.Create((left, right) =>
            {
                int compare = scores[right].CompareTo(scores[left]);
                return compare != 0 ? compare : left.CompareTo(right);
            }));
            int keep = Math.Min(width, count);
            int offensive = (int)(keep * offensiveShare);
            int byScore = keep - offensive;
            for (int index = 0; index < byScore; index++)
                Take(target, _order[index]);
            if (offensive > 0)
            {
                int[] enemyHp = _enemyHp;
                Array.Sort(_order, 0, count, Comparer<int>.Create((left, right) =>
                {
                    int compare = enemyHp[left].CompareTo(enemyHp[right]);
                    if (compare != 0)
                        return compare;
                    compare = scores[right].CompareTo(scores[left]);
                    return compare != 0 ? compare : left.CompareTo(right);
                }));
                int added = 0;
                for (int index = 0; index < count && added < offensive; index++)
                {
                    if (_taken[_order[index]])
                        continue;
                    Take(target, _order[index]);
                    added++;
                }
            }
        }

        private void Take(Pool target, int source)
        {
            _taken[source] = true;
            target.Add(_states[source], _nodes[source], _scores[source]);
        }

        public int BestIndex()
        {
            int best = -1;
            for (int index = 0; index < Count; index++)
            {
                if (best < 0 || _scores[index] > _scores[best])
                    best = index;
            }
            return best;
        }

        public double BestScore() => BestIndex() is var best and >= 0 ? _scores[best] : double.NegativeInfinity;
    }
}
