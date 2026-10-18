namespace CombatSolver.Engine.InCombat.Flash;

public static partial class FlashKernel
{
    private const int MonsterAdvanceGuard = 32;

    /// <summary>
    /// Mirrors <c>MonsterMoveSemantics.ApplyForecastMove</c>. Returns true when the player died.
    /// </summary>
    public static bool ExecuteMonsterMove(ref FlashExec x, byte owner)
    {
        FlashMonsterRules rules = x.R.Monster(owner);
        int moveNode = x.B.Enemy(owner).Move;
        FlashMonsterNode node = rules.Nodes[moveNode];
        FlashMonsterDef def = FlashRegistry.Monster(x.B.Enemy(owner).MonsterId);
        // MonsterMoveEffects.ApplyBeforeAttack has no supported moves yet.
        bool playerDied = false;
        if (node.Attacks.Length > 0)
        {
            // BeginAttackContext: BeforeAttack has no supported listeners yet.
            Span<byte> player = stackalloc byte[1];
            player[0] = FlashLimits.PlayerOwner;
            foreach (FlashMonsterAttack attack in node.Attacks)
            {
                int repeats = Math.Max(attack.Repeats, 1);
                for (int hit = 0; hit < repeats; hit++)
                {
                    // AdjustMonsterMoveDamage has no supported special moves yet.
                    Damage(ref x, player, attack.BaseDamage, FlashValueProps.Move, owner, hasCardSource: false, default);
                    if (x.NeedsChoice)
                        return x.B.Player.IsDead;
                    ApplyEnemyDeathPowers(ref x);
                    if (x.B.Player.IsDead)
                    {
                        playerDied = true;
                        goto AttackDone;
                    }
                    if (x.B.Creature(owner).IsDead)
                        goto AttackDone;
                }
            }
        AttackDone:
            // EndAttackContext(completed): AfterAttack iterates the combat listeners without the ending gate.
            AfterAttackHook after = new() { Attacker = owner };
            FlashHooks.Dispatch(ref x, ref after, gatedByEnding: false);
            if (x.NeedsChoice)
                return x.B.Player.IsDead;
        }
        if (playerDied)
            return true;
        // Imbalanced stun is not supported yet.
        bool ownerAliveBefore = x.B.Creature(owner).IsAlive;
        def.ApplyMoveEffects(ref x, owner, moveNode);
        if (x.NeedsChoice)
            return x.B.Player.IsDead;
        if (ownerAliveBefore && x.B.Creature(owner).IsDead
            && (x.B.Enemy(owner).Flags & FlashEnemyFlags.DeathProcessed) == 0)
        {
            ApplyEnemyDeathPowers(ref x);
        }
        return x.B.Player.IsDead;
    }

    /// <summary>Mirrors <c>SimulatedCombatState.PrepareMonsterMoveForNextRound</c> for every roster enemy.</summary>
    public static void PrepareMonsterMovesForNextRound(ref FlashExec x)
    {
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = EnemyOwner(x.B.Roster[position]);
            ref FlashEnemy enemy = ref x.B.Enemy(owner);
            if (!CanPerformMonsterMove(enemy))
                continue;
            if ((enemy.Flags & FlashEnemyFlags.NeedsInitialRoll) != 0)
            {
                ResolveMonsterState(ref x, owner, x.R.Monster(owner).InitialNode);
                x.B.Enemy(owner).Flags &= ~FlashEnemyFlags.NeedsInitialRoll;
                continue;
            }
            if ((enemy.Flags & FlashEnemyFlags.PerformedMoveThisRound) == 0)
                continue;
            // STUNNED and must-perform-once moves are not supported yet.
            FlashMonsterNode current = x.R.Monster(owner).Nodes[enemy.Move];
            if (current.FollowUp < 0)
                throw new FlashUnsupportedException($"行动 {current.NativeId} 没有后继状态。");
            ResolveMonsterState(ref x, owner, current.FollowUp);
        }
    }

    /// <summary>Mirrors <c>SimulatedCombatState.CanPerformMonsterMove</c>.</summary>
    public static bool CanPerformMonsterMove(in FlashEnemy enemy)
        => enemy.Creature.IsAlive || enemy.DeathPhase == FlashDeathPhase.Reviving;

    /// <summary>
    /// Walks the compiled state machine from <paramref name="start"/> to a move node, mirroring the loop of
    /// <c>BranchMonsterAi.Advance</c> / <c>RollInitial</c>, and logs that move.
    /// </summary>
    private static void ResolveMonsterState(ref FlashExec x, byte owner, int start)
    {
        FlashMonsterRules rules = x.R.Monster(owner);
        FlashMonsterDef def = FlashRegistry.Monster(x.B.Enemy(owner).MonsterId);
        int state = start;
        for (int guard = 0; guard < MonsterAdvanceGuard; guard++)
        {
            FlashMonsterNode node = rules.Nodes[state];
            switch (node.Kind)
            {
                case FlashMonsterNodeKind.Move:
                    ref FlashEnemy enemy = ref x.B.Enemy(owner);
                    enemy.Move = checked((byte)state);
                    LogMonsterMove(ref enemy, state);
                    return;
                case FlashMonsterNodeKind.RandomBranch:
                    state = PickRandomBranch(ref x, owner, rules, state, def);
                    break;
                case FlashMonsterNodeKind.ConditionalBranch:
                    int special = def.ResolveConditional(ref x, owner, state);
                    state = special >= 0 ? special : node.ConditionalTarget >= 0
                        ? node.ConditionalTarget
                        : throw new FlashUnsupportedException($"条件分支 {node.NativeId} 没有根选择。");
                    break;
                default:
                    throw new FlashUnsupportedException($"未知的怪物状态类型 {node.Kind}。");
            }
        }
        throw new InvalidOperationException("怪物行动状态机未能在 32 步内落到行动节点。");
    }

    /// <summary>Mirrors <c>MonsterRandomBranchResolver.Pick</c>, including its float arithmetic order.</summary>
    private static int PickRandomBranch(ref FlashExec x, byte owner, FlashMonsterRules rules, int branch, FlashMonsterDef def)
    {
        FlashRandomBranchOption[] options = rules.Nodes[branch].Options;
        Span<float> weights = stackalloc float[options.Length];
        float total = 0f;
        for (int index = 0; index < options.Length; index++)
        {
            float weight = RepeatWeight(x.B.Enemy(owner), rules, options[index])
                * def.BranchWeight(ref x, owner, branch, index, options[index].BaseWeight);
            weights[index] = weight;
            total += weight;
        }
        float roll = x.B.RngStream(FlashRngStream.MonsterAi).NextFloat(total);
        for (int index = 0; index < options.Length; index++)
        {
            roll -= weights[index];
            if (roll <= 0f)
                return options[index].Target;
        }
        for (int index = options.Length - 1; index >= 0; index--)
        {
            if (weights[index] > 0f)
                return options[index].Target;
        }
        throw new InvalidOperationException($"随机分支 {rules.Nodes[branch].NativeId} 没有可选状态。");
    }

    private static float RepeatWeight(in FlashEnemy enemy, FlashMonsterRules rules, in FlashRandomBranchOption option)
    {
        int target = option.Target;
        bool targetIsMove = rules.Nodes[target].Kind == FlashMonsterNodeKind.Move;
        int recent = Math.Min((int)enemy.LoggedMoves, FlashLimits.EnemyRecentMoves);
        if (option.RepeatType == FlashMoveRepeatType.UseOnlyOnce && targetIsMove && (enemy.EverUsedMoves & (1UL << target)) != 0)
            return 0f;
        if (option.RepeatType == FlashMoveRepeatType.CannotRepeat && recent > 0 && enemy.RecentMoves[recent - 1] == target)
            return 0f;
        if (option.RepeatType == FlashMoveRepeatType.CanRepeatXTimes)
        {
            int consecutive = 0;
            for (int index = recent - 1; index >= 0 && enemy.RecentMoves[index] == target; index--)
                consecutive++;
            if (consecutive >= recent && enemy.LoggedMoves > FlashLimits.EnemyRecentMoves && consecutive < option.MaxTimes)
                throw new FlashUnsupportedException("连续行动次数超出近期日志容量。");
            if (consecutive >= option.MaxTimes)
                return 0f;
        }
        if (option.Cooldown > 0)
        {
            if (option.Cooldown > FlashLimits.EnemyRecentMoves)
                throw new FlashUnsupportedException("怪物行动冷却超出近期日志容量。");
            int window = Math.Min(option.Cooldown, recent);
            for (int index = recent - window; index < recent; index++)
            {
                if (enemy.RecentMoves[index] == target)
                    return 0f;
            }
        }
        return 1f;
    }

    public static void LogMonsterMove(ref FlashEnemy enemy, int moveNode)
    {
        if ((uint)moveNode >= 64)
            throw new FlashCapacityException("Enemy.MoveNodes", 64);
        enemy.EverUsedMoves |= 1UL << moveNode;
        int recent = Math.Min((int)enemy.LoggedMoves, FlashLimits.EnemyRecentMoves);
        if (recent == FlashLimits.EnemyRecentMoves)
        {
            for (int index = 1; index < FlashLimits.EnemyRecentMoves; index++)
                enemy.RecentMoves[index - 1] = enemy.RecentMoves[index];
            enemy.RecentMoves[FlashLimits.EnemyRecentMoves - 1] = (byte)moveNode;
        }
        else
        {
            enemy.RecentMoves[recent] = (byte)moveNode;
        }
        if (enemy.LoggedMoves < byte.MaxValue)
            enemy.LoggedMoves++;
    }
}
