namespace CombatSolver.Engine.InCombat.Flash;

public static partial class FlashKernel
{
    private const int MonsterAdvanceGuard = 32;

    /// <summary>Native id of the synthetic stunned move (<c>SimulatedCombatState.ForceStunnedMove</c>).</summary>
    public const string StunnedNodeId = "STUNNED";

    /// <summary>Native id of the synthetic revive move (<c>SimulatedCombatState.BeginIllusionRevive</c>).</summary>
    public const string ReviveNodeId = "REVIVE_MOVE";

    /// <summary><see cref="FlashEnemy.Slot"/> value of a monster with no encounter slot name.</summary>
    public const int NoSlot = byte.MaxValue;

    /// <summary>The rule entry of an enemy, resolved through the <see cref="FlashEnemy.Rules"/> index.</summary>
    public static FlashMonsterRules MonsterRules(ref FlashExec x, byte owner)
        => x.R.MonsterRule(x.B.Enemy(owner).Rules);

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.NextFreeSlot</c>: the first encounter slot with no roster
    /// enemy, <c>null</c> when the battle has no encounter, or <c>""</c> when every slot is taken.
    /// </summary>
    public static string? NextFreeSlot(in FlashBattle battle, FlashRules rules)
    {
        if (!rules.HasEncounter)
            return null;
        for (int slot = 0; slot < rules.EncounterSlots.Count; slot++)
        {
            if (!SlotOccupied(battle, slot))
                return rules.EncounterSlots[slot];
        }
        return string.Empty;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.LastFreeSlot</c>: the last free encounter slot, <c>null</c>
    /// when the battle has no encounter or when every slot is taken.
    /// </summary>
    public static string? LastFreeSlot(in FlashBattle battle, FlashRules rules)
    {
        if (!rules.HasEncounter)
            return null;
        for (int slot = rules.EncounterSlots.Count - 1; slot >= 0; slot--)
        {
            if (!SlotOccupied(battle, slot))
                return rules.EncounterSlots[slot];
        }
        return null;
    }

    /// <summary>Index of <paramref name="slotName"/> in the encounter slots, or <see cref="NoSlot"/> for null.</summary>
    public static int SlotIndex(FlashRules rules, string? slotName)
    {
        if (slotName == null)
            return NoSlot;
        for (int slot = 0; slot < rules.EncounterSlots.Count; slot++)
        {
            if (string.Equals(rules.EncounterSlots[slot], slotName, StringComparison.Ordinal))
                return slot;
        }
        throw new FlashUnsupportedException($"遭遇没有槽位 {slotName}。");
    }

    private static bool SlotOccupied(in FlashBattle battle, int slot)
    {
        for (int position = 0; position < battle.RosterCount; position++)
        {
            if (battle.Enemies[battle.Roster[position]].Slot == slot)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Mirrors <c>MonsterSpawnSupport.AddCreated</c> for one created monster: assigns the CombatId and
    /// initial HP (<c>SimulatedCombatState.CreatePredictedMonster</c>), appends it to the known
    /// enemies, inserts it into the roster in encounter-slot order
    /// (<c>AddCreature</c>/<c>SortEnemiesBySlotName</c>), applies the compiled <c>SetUpForCombat</c>
    /// values and entrance powers, rolls or defers the initial action
    /// (<c>PreparePredictedMonster</c>), and finally applies Minion.
    /// </summary>
    public static byte SpawnMonster(ref FlashExec x, byte source, int rulesId, int slotIndex, bool minion)
    {
        if (rulesId < 0 || rulesId >= FlashLimits.MonsterRules)
            throw new ArgumentOutOfRangeException(nameof(rulesId), rulesId, "怪物规则条目越界。");
        FlashMonsterRules rules = x.R.MonsterRule(checked((byte)rulesId));
        if (slotIndex != NoSlot && (uint)slotIndex >= (uint)x.R.EncounterSlots.Count)
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "遭遇槽位越界。");
        int enemyIndex = x.B.EnemyCount;
        if (enemyIndex >= FlashLimits.Enemies)
            throw new FlashCapacityException("Enemies", FlashLimits.Enemies);
        byte owner = EnemyOwner(enemyIndex);
        ref FlashEnemy enemy = ref x.B.Enemies[enemyIndex];
        enemy = default;
        enemy.MonsterId = rules.MonsterId;
        enemy.Rules = checked((byte)rulesId);
        enemy.Slot = checked((byte)slotIndex);
        enemy.CombatId = x.B.NextCombatId++;
        enemy.Creature.CurrentHp = RollInitialHp(ref x.B, rules);
        enemy.Creature.MaxHp = enemy.Creature.CurrentHp;
        enemy.Creature.Flags = FlashCreatureFlags.Present;
        enemy.Flags = FlashEnemyFlags.InCombat;
        if (rules.InitialAiRegisters.Length > FlashLimits.EnemyAiRegisters)
            throw new FlashCapacityException("Enemy.Ai", FlashLimits.EnemyAiRegisters);
        for (int register = 0; register < rules.InitialAiRegisters.Length; register++)
            enemy.Ai[register] = rules.InitialAiRegisters[register];
        for (int register = rules.InitialAiRegisters.Length; register < FlashLimits.EnemyAiRegisters; register++)
            enemy.Ai[register] = 0;
        x.B.EnemyCount = checked((byte)(enemyIndex + 1));
        InsertIntoRoster(ref x.B, enemyIndex, slotIndex);
        FlashMonsterDef def = FlashRegistry.Monster(rules.MonsterId);
        def.ApplyEntrancePowers(ref x, owner);
        if (x.NeedsChoice)
            return owner;
        PrepareSpawnedMonster(ref x, owner, rules);
        if (x.NeedsChoice)
            return owner;
        // The summoning move applies Minion after CreatureCmd.Add and its entrance hooks finish.
        if (minion && FlashRegistry.TryPower("MINION_POWER", out ushort minionPower)
            && GetAmount(x.B, owner, minionPower) <= 0)
        {
            ApplyPower(ref x, owner, minionPower, 1, source);
        }
        return owner;
    }

    /// <summary>
    /// <c>CreatePredictedMonster</c>'s HP roll: the eligible values are <c>[MinInitialHp,
    /// MaxInitialHp]</c> minus the MaxHp of every roster enemy, in ascending order (the native
    /// <c>HashSet&lt;int&gt;</c> enumeration order); with none left it draws
    /// <c>NextInt(MinInitialHp, MaxInitialHp + 1)</c>.
    /// </summary>
    private static int RollInitialHp(ref FlashBattle battle, in FlashMonsterRules rules)
    {
        int eligible = 0;
        for (int hp = rules.InitialHpMin; hp <= rules.InitialHpMax; hp++)
        {
            if (!RosterMaxHp(battle, hp))
                eligible++;
        }
        if (eligible == 0)
            return battle.RngStream(FlashRngStream.Niche).NextInt(rules.InitialHpMin, rules.InitialHpMax + 1);
        int index = battle.RngStream(FlashRngStream.Niche).NextInt(0, eligible);
        for (int hp = rules.InitialHpMin; hp <= rules.InitialHpMax; hp++)
        {
            if (RosterMaxHp(battle, hp))
                continue;
            if (index-- == 0)
                return hp;
        }
        throw new InvalidOperationException("怪物初始生命在可取值上越界。");
    }

    private static bool RosterMaxHp(in FlashBattle battle, int maxHp)
    {
        for (int position = 0; position < battle.RosterCount; position++)
        {
            if (battle.Enemies[battle.Roster[position]].Creature.MaxHp == maxHp)
                return true;
        }
        return false;
    }

    /// <summary>
    /// <c>AddCreature</c> then <c>SortEnemiesBySlotName</c> (stable ascending slot index, a missing
    /// slot name sorting as -1). The known-enemy array keeps its append order.
    /// </summary>
    private static void InsertIntoRoster(ref FlashBattle battle, int enemyIndex, int slotIndex)
    {
        int position = battle.RosterCount;
        // A null slot name sorts as -1, before every named slot but after earlier nulls.
        int key = slotIndex == NoSlot ? -1 : slotIndex;
        while (position > 0)
        {
            int previousSlot = battle.Enemies[battle.Roster[position - 1]].Slot;
            int previousKey = previousSlot == NoSlot ? -1 : previousSlot;
            if (previousKey <= key)
                break;
            position--;
        }
        for (int index = battle.RosterCount; index > position; index--)
            battle.Roster[index] = battle.Roster[index - 1];
        battle.Roster[position] = checked((byte)enemyIndex);
        battle.RosterCount = checked((byte)(battle.RosterCount + 1));
    }

    /// <summary>
    /// Mirrors <c>PreparePredictedMonster</c>: during the player turn the initial action is rolled
    /// immediately; during the enemy turn the creature keeps the pending-roll placeholder until the
    /// end-of-enemy-turn pass (<see cref="PrepareMonsterMovesForNextRound"/>).
    /// </summary>
    private static void PrepareSpawnedMonster(ref FlashExec x, byte owner, in FlashMonsterRules rules)
    {
        ref FlashEnemy enemy = ref x.B.Enemy(owner);
        if (x.B.Side == FlashSide.Player)
        {
            ResolveMonsterState(ref x, owner, rules.InitialNode);
            return;
        }
        enemy.Move = checked((byte)rules.PendingInitialMoveNode);
        enemy.ForcedMove = byte.MaxValue;
        enemy.Flags |= FlashEnemyFlags.NeedsInitialRoll;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.BeginIllusionRevive</c>
    /// (<c>DeathLifecycle.cs:100-119</c>): sets the reviving death phase and forces the synthetic
    /// REVIVE_MOVE whose dynamic follow-up is the last logged real move.
    /// </summary>
    public static void BeginIllusionRevive(ref FlashExec x, byte owner)
    {
        FlashMonsterRules rules = MonsterRules(ref x, owner);
        if (rules.ReviveNode < 0)
            throw new FlashUnsupportedException($"怪物 {FlashRegistry.Monster(x.B.Enemy(owner).MonsterId).NativeId} 没有复活行动节点。");
        ref FlashEnemy enemy = ref x.B.Enemy(owner);
        if (enemy.DeathPhase == FlashDeathPhase.Reviving)
            return;
        int followUp = LastLoggedMove(enemy, rules);
        if (followUp < 0)
            throw new FlashUnsupportedException("幻象进入复活时没有可恢复的正式行动记录。");
        if ((enemy.Flags & (FlashEnemyFlags.PerformedMoveThisRound | FlashEnemyFlags.ActingThisRound)) != 0)
            enemy.Flags |= FlashEnemyFlags.ForcedMustPerformAfterMove;
        enemy.DeathPhase = FlashDeathPhase.Reviving;
        enemy.ForcedMove = checked((byte)followUp);
        enemy.Move = checked((byte)rules.ReviveNode);
        enemy.Flags &= ~FlashEnemyFlags.NeedsInitialRoll;
    }

    private static int LastLoggedMove(in FlashEnemy enemy, in FlashMonsterRules rules)
    {
        for (int index = Math.Min((int)enemy.LoggedMoves, FlashLimits.EnemyRecentMoves) - 1; index >= 0; index--)
        {
            if (enemy.RecentMoves[index] != rules.ReviveNode)
                return enemy.RecentMoves[index];
        }
        return -1;
    }

    /// <summary>
    /// Mirrors <c>MonsterMoveSemantics.ApplyForecastMove</c>'s revive handling and the caller's
    /// <c>processedEnemyDeaths.Remove</c>: a living monster after its move is no longer marked as a
    /// processed death.
    /// </summary>
    public static bool ExecuteMonsterMove(ref FlashExec x, byte owner)
    {
        x.B.Enemy(owner).Flags |= FlashEnemyFlags.ActingThisRound;
        bool playerDied = ExecuteMonsterMoveCore(ref x, owner);
        ref FlashEnemy enemy = ref x.B.Enemy(owner);
        enemy.Flags &= ~FlashEnemyFlags.ActingThisRound;
        if (enemy.Creature.IsAlive)
            enemy.Flags &= ~FlashEnemyFlags.DeathProcessed;
        return playerDied;
    }

    private static bool ExecuteMonsterMoveCore(ref FlashExec x, byte owner)
    {
        FlashMonsterRules rules = MonsterRules(ref x, owner);
        int moveNode = x.B.Enemy(owner).Move;
        FlashMonsterNode node = rules.Nodes[moveNode];
        FlashMonsterDef def = FlashRegistry.Monster(x.B.Enemy(owner).MonsterId);
        // MonsterMoveEffects.ApplyBeforeAttack has no supported moves yet.
        bool playerDied = false;
        if (node.Attacks.Length > 0)
        {
            // BeginAttackContext: BeforeAttack iterates the combat listeners behind the ending gate
            // (HookMirrors.BeforeAttack).
            BeforeAttackHook before = new() { Attacker = owner, Props = FlashValueProps.Move };
            FlashHooks.Dispatch(ref x, ref before, gatedByEnding: true);
            if (x.NeedsChoice)
                return x.B.Player.IsDead;
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
        // MonsterMoveEffects.Apply starts with ResolveReviveMove; the synthetic revive node has no
        // attacks, so this is the same position as running it before the action's effects.
        if (moveNode == rules.ReviveNode)
        {
            ref FlashCreature creature = ref x.B.Creature(owner);
            creature.CurrentHp = creature.MaxHp;
            x.B.Enemy(owner).DeathPhase = FlashDeathPhase.None;
        }
        bool ownerAliveBefore = x.B.Creature(owner).IsAlive;
        def.ApplyMoveEffects(ref x, owner, moveNode);
        if (x.NeedsChoice)
            return x.B.Player.IsDead;
        if (ownerAliveBefore && x.B.Creature(owner).IsDead
            && (x.B.Enemy(owner).Flags & FlashEnemyFlags.DeathProcessed) == 0)
        {
            ApplyEnemyDeathPowers(ref x);
        }
        // MonsterMoveSemantics resolves the move's queued power amount changes before normalizing.
        if (!x.NeedsChoice)
            ResolvePowerAmountChanges(ref x);
        return x.B.Player.IsDead;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.ForceMonsterMove</c> (<c>MonsterAi.cs:58-74</c>): replaces the
    /// current move and clears the pending initial roll. Nothing is logged here; the state machine log
    /// only receives moves resolved by <see cref="ResolveMonsterState"/>.
    /// </summary>
    public static void ForceMonsterMove(ref FlashExec x, byte owner, int node)
    {
        FlashMonsterRules rules = MonsterRules(ref x, owner);
        if ((uint)node >= (uint)rules.Nodes.Length)
            throw new ArgumentOutOfRangeException(nameof(node), node, "强制行动节点越界。");
        if (node == rules.StunnedNode)
            throw new FlashUnsupportedException("合成 STUNNED 节点只能由 ForceStunnedMove 设置。");
        if (node == rules.ReviveNode)
            throw new FlashUnsupportedException("合成 REVIVE_MOVE 节点只能由 BeginIllusionRevive 设置。");
        ref FlashEnemy enemy = ref x.B.Enemy(owner);
        FlashMonsterNode target = rules.Nodes[node];
        // Native ForceMonsterMove stores a MoveState object. If it differs from the move performed this
        // round, a must-perform-once target must not transition (MonsterAi.cs:122-126); the external
        // performed flag is only set after the action (Replay.cs:1164), so the in-action marker counts.
        if (node != enemy.Move
            && target.MustPerformOnceBeforeTransitioning
            && (enemy.Flags & (FlashEnemyFlags.PerformedMoveThisRound | FlashEnemyFlags.ActingThisRound)) != 0)
        {
            enemy.Flags |= FlashEnemyFlags.ForcedMustPerformAfterMove;
        }
        enemy.Move = checked((byte)node);
        enemy.ForcedMove = byte.MaxValue;
        enemy.Flags &= ~FlashEnemyFlags.NeedsInitialRoll;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.ForceStunnedMove</c> (<c>MonsterAi.cs:76-86</c>): builds a new
    /// <c>MoveState("STUNNED")</c> with no attacks or effects, <c>MustPerformOnceBeforeTransitioning</c>,
    /// and a dynamic follow-up. <paramref name="nextNode"/> below zero takes the native default,
    /// <c>StateLog.LastOrDefault() ?? Current.Id</c> (the most recently logged move, newest last, or the
    /// current node when nothing has been logged). Like the native method it writes no state-log entry.
    /// </summary>
    public static void ForceStunnedMove(ref FlashExec x, byte owner, int nextNode)
    {
        FlashMonsterRules rules = MonsterRules(ref x, owner);
        ref FlashEnemy enemy = ref x.B.Enemy(owner);
        if (nextNode < 0)
            nextNode = DefaultStunnedFollowUp(enemy, rules);
        if ((uint)nextNode >= (uint)rules.Nodes.Length)
            throw new ArgumentOutOfRangeException(nameof(nextNode), nextNode, "STUNNED 后继节点越界。");
        if (nextNode == rules.StunnedNode)
            throw new FlashUnsupportedException("STUNNED 的后继不能是合成 STUNNED 节点。");
        if (nextNode == rules.ReviveNode)
            throw new FlashUnsupportedException("STUNNED 的后继不能是合成 REVIVE_MOVE 节点。");
        // A forced STUNNED is always a new MoveState object, so after this enemy already performed the
        // round the native must-perform-once check refuses to advance (MonsterAi.cs:122-126).
        if ((enemy.Flags & (FlashEnemyFlags.PerformedMoveThisRound | FlashEnemyFlags.ActingThisRound)) != 0)
            enemy.Flags |= FlashEnemyFlags.ForcedMustPerformAfterMove;
        enemy.ForcedMove = checked((byte)nextNode);
        enemy.Move = checked((byte)rules.StunnedNode);
        enemy.Flags &= ~FlashEnemyFlags.NeedsInitialRoll;
    }

    private static int DefaultStunnedFollowUp(in FlashEnemy enemy, in FlashMonsterRules rules)
    {
        int recent = Math.Min((int)enemy.LoggedMoves, FlashLimits.EnemyRecentMoves);
        int next = recent > 0 ? enemy.RecentMoves[recent - 1] : enemy.Move;
        if (next == rules.StunnedNode)
            throw new FlashUnsupportedException("STUNNED 的缺省后继仍是合成 STUNNED 节点。");
        if (next == rules.ReviveNode)
            throw new FlashUnsupportedException("STUNNED 的缺省后继是合成 REVIVE_MOVE 节点。");
        return next;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.PrepareMonsterMoveForNextRound</c> (<c>MonsterAi.cs:102-128</c>)
    /// for every roster enemy.
    /// </summary>
    public static void PrepareMonsterMovesForNextRound(ref FlashExec x)
    {
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = EnemyOwner(x.B.Roster[position]);
            ref FlashEnemy enemy = ref x.B.Enemy(owner);
            bool forcedAfterMove = (enemy.Flags & FlashEnemyFlags.ForcedMustPerformAfterMove) != 0;
            enemy.Flags &= ~FlashEnemyFlags.ForcedMustPerformAfterMove;
            if (!CanPerformMonsterMove(enemy))
                continue;
            if ((enemy.Flags & FlashEnemyFlags.NeedsInitialRoll) != 0)
            {
                ResolveMonsterState(ref x, owner, MonsterRules(ref x, owner).InitialNode);
                x.B.Enemy(owner).Flags &= ~FlashEnemyFlags.NeedsInitialRoll;
                continue;
            }
            if ((enemy.Flags & FlashEnemyFlags.PerformedMoveThisRound) == 0)
                continue;
            FlashMonsterRules rules = MonsterRules(ref x, owner);
            if (enemy.Move == rules.StunnedNode)
            {
                // Native checks STUNNED first, then the must-perform-once gate (MonsterAi.cs:120-126).
                if ((enemy.Flags & FlashEnemyFlags.SkipNextMove) != 0)
                    continue;
                if (forcedAfterMove)
                    continue;
                int next = enemy.ForcedMove;
                if (next == byte.MaxValue)
                    throw new FlashUnsupportedException("STUNNED 缺少动态后继。");
                if (next == rules.StunnedNode)
                    throw new FlashUnsupportedException("STUNNED 的动态后继是合成 STUNNED 节点。");
                if (next == rules.ReviveNode)
                    throw new FlashUnsupportedException("STUNNED 的动态后继是合成 REVIVE_MOVE 节点。");
                enemy.ForcedMove = byte.MaxValue;
                ResolveMonsterState(ref x, owner, next);
                continue;
            }
            if (enemy.Move == rules.ReviveNode)
            {
                // SimulatedCombatState.BeginIllusionRevive builds a MoveState with
                // MustPerformOnceBeforeTransitioning and a dynamic FollowUpStateId; the native
                // skip-next-move check only covers STUNNED.
                if (forcedAfterMove)
                    continue;
                int next = enemy.ForcedMove;
                if (next == byte.MaxValue)
                    throw new FlashUnsupportedException("REVIVE_MOVE 缺少动态后继。");
                if (next == rules.StunnedNode || next == rules.ReviveNode)
                    throw new FlashUnsupportedException("REVIVE_MOVE 的动态后继是合成节点。");
                enemy.ForcedMove = byte.MaxValue;
                ResolveMonsterState(ref x, owner, next);
                continue;
            }
            FlashMonsterNode current = rules.Nodes[enemy.Move];
            if (current.MustPerformOnceBeforeTransitioning && forcedAfterMove)
                continue;
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
        FlashMonsterRules rules = MonsterRules(ref x, owner);
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
                    // A resolved node is a native state; the dynamic STUNNED follow-up no longer applies.
                    enemy.ForcedMove = byte.MaxValue;
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
