namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>Native <c>DamageResult</c> fields the pipeline uses.</summary>
public struct FlashDamageResult
{
    public byte Receiver;
    public FlashValueProps Props;
    public bool WasTargetKilled;
    public bool WasBlockBroken;
    public bool WasFullyBlocked;
    public int UnblockedDamage;
    public int OverkillDamage;
    public int BlockedDamage;
}

public enum FlashAttackTargeting : byte
{
    Single = 0,
    AllOpponents = 1,
    Random = 2,
}

public static partial class FlashKernel
{
    private const int MaxDamageResults = FlashLimits.Enemies * 2 + 2;

    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.ExecuteAttack</c> for a card attack.
    /// </summary>
    public static void CardAttack(ref FlashExec x, decimal damagePerHit, int hitCount, FlashAttackTargeting targeting,
        byte singleTarget, FlashValueProps props = FlashValueProps.Move)
    {
        if (IsOverOrEnding(x.B))
            return;
        byte attacker = FlashLimits.PlayerOwner;
        if (x.B.Creature(attacker).IsDead)
            return;
        // BeforeAttack and ModifyAttackHitCount have no supported listeners yet.
        Span<byte> targets = stackalloc byte[FlashLimits.Enemies + 1];
        for (int hit = 0; hit < hitCount; hit++)
        {
            if (x.B.Creature(attacker).IsDead)
                break;
            int count = 0;
            if (targeting == FlashAttackTargeting.Single)
            {
                if (x.B.Creature(singleTarget).IsAlive)
                    targets[count++] = singleTarget;
            }
            else
            {
                for (int position = 0; position < x.B.RosterCount; position++)
                {
                    byte owner = EnemyOwner(x.B.Roster[position]);
                    if (x.B.Creature(owner).IsAlive)
                        targets[count++] = owner;
                }
            }
            if (count == 0)
                break;
            if (targeting == FlashAttackTargeting.Random)
            {
                // Random attacks without duplicates are not supported yet.
                int pick = x.B.RngStream(FlashRngStream.CombatTargets).NextInt(0, count);
                targets[0] = targets[pick];
                count = 1;
            }
            Damage(ref x, targets[..count], damagePerHit, props, attacker, hasCardSource: true, x.Card);
            if (x.NeedsChoice)
                return;
        }
        // EndAttackContext(completed): RecordCreatureAttacked, then AfterAttack iterates the combat
        // listeners without the ending gate.
        if (attacker == FlashLimits.PlayerOwner)
            x.B.AddCounter(FlashCounter.PlayerAttacksThisTurn, 1);
        AfterAttackHook after = new() { Attacker = attacker };
        FlashHooks.Dispatch(ref x, ref after, gatedByEnding: false);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.Damage</c>.</summary>
    public static void Damage(ref FlashExec x, scoped ReadOnlySpan<byte> targets, decimal amount, FlashValueProps props,
        byte dealer, bool hasCardSource, in FlashCard cardSource)
    {
        if (dealer != FlashLimits.NoOwner && x.B.Creature(dealer).IsDead || targets.Length == 0)
            return;
        Span<FlashDamageResult> results = stackalloc FlashDamageResult[MaxDamageResults];
        int count = 0;
        foreach (byte target in targets)
        {
            if (!TryDamageTarget(ref x, target, amount, props, dealer, hasCardSource, cardSource, results, ref count))
                return;
        }
        ProcessDamageResults(ref x, results[..count], dealer);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.TryDamageTarget</c>.</summary>
    private static bool TryDamageTarget(ref FlashExec x, byte target, decimal amount, FlashValueProps props, byte dealer,
        bool hasCardSource, in FlashCard cardSource, scoped Span<FlashDamageResult> results, ref int count)
    {
        if (x.B.Creature(target).IsDead)
            return true;
        decimal modified = FlashHooks.ModifyDamage(ref x, target, dealer, amount, props, hasCardSource, cardSource);
        if (x.NeedsChoice)
            return false;
        // BeforeDamageReceived has no supported listeners yet.
        byte blockTarget = target == FlashLimits.OstyOwner ? FlashLimits.PlayerOwner : target;
        decimal blocked = DamageBlock(ref x.B.Creature(blockTarget), modified, props);
        // ModifyHpLost (before and after Osty), the Osty redirect and AfterModifyingHpLostAfterOsty
        // have no supported listeners yet; Osty itself is rejected at admission.
        decimal unblocked = Math.Max(modified - blocked, 0m);
        ref FlashCreature creature = ref x.B.Creature(target);
        FlashDamageResult result = LoseHp(ref creature, target, unblocked, props);
        result.BlockedDamage = (int)blocked;
        result.WasBlockBroken = creature.Block <= 0 && blocked > 0m;
        result.WasFullyBlocked = (props & FlashValueProps.Unblockable) == 0
            && (blocked > 0m || creature.Block > 0)
            && (int)unblocked == 0;
        RecordDamageReceived(ref x.B, target, result);
        results[count++] = result;
        return true;
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.ProcessDamageResults</c>.</summary>
    private static void ProcessDamageResults(ref FlashExec x, scoped ReadOnlySpan<FlashDamageResult> results, byte dealer)
    {
        Span<byte> killed = stackalloc byte[MaxDamageResults];
        int killedCount = 0;
        foreach (FlashDamageResult result in results)
        {
            // AfterBlockBroken and AfterCurrentHpChanged have no supported listeners yet.
            AfterDamageGivenHook given = new() { Result = result, Dealer = dealer };
            FlashHooks.Dispatch(ref x, ref given, gatedByEnding: false);
            if (x.NeedsChoice)
                return;
            if (!result.WasTargetKilled || !x.B.Creature(result.Receiver).IsDead)
            {
                AfterDamageReceivedHook received = new() { Result = result, Dealer = dealer };
                FlashHooks.Dispatch(ref x, ref received, gatedByEnding: false);
                if (x.NeedsChoice)
                    return;
                AfterDamageReceivedLateHook late = new() { Result = result, Dealer = dealer };
                FlashHooks.Dispatch(ref x, ref late, gatedByEnding: false);
                if (x.NeedsChoice)
                    return;
            }
            else
            {
                killed[killedCount++] = result.Receiver;
            }
        }
        if (killedCount > 0)
            Kill(ref x, killed[..killedCount], force: false);
        else if (x.B.Player.IsDead)
            LoseCombat(ref x.B);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.Kill(IReadOnlyList&lt;Creature&gt;, bool)</c>.</summary>
    public static void Kill(ref FlashExec x, scoped ReadOnlySpan<byte> creatures, bool force)
    {
        foreach (byte creature in creatures)
        {
            KillWithoutCheckingWinCondition(ref x, creature, force);
            if (x.NeedsChoice)
                return;
        }
        if (x.B.Player.IsDead)
            LoseCombat(ref x.B);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.KillWithoutCheckingWinCondition</c>.</summary>
    private static void KillWithoutCheckingWinCondition(ref FlashExec x, byte owner, bool force)
    {
        ref FlashCreature creature = ref x.B.Creature(owner);
        if (creature.CurrentHp > 0)
            LoseHp(ref creature, owner, creature.CurrentHp, FlashValueProps.Unblockable | FlashValueProps.Unpowered);
        // AfterCurrentHpChanged, BeforeDeath and ShouldDie have no supported listeners yet, so the
        // creature always dies.
        if (IsEnemyOwner(owner))
        {
            Span<byte> aliveTeammates = stackalloc byte[FlashLimits.Enemies];
            int aliveCount = 0;
            for (int position = 0; position < x.B.RosterCount; position++)
            {
                byte teammate = EnemyOwner(x.B.Roster[position]);
                if (teammate != owner && x.B.Creature(teammate).IsAlive)
                    aliveTeammates[aliveCount++] = teammate;
            }
            bool isPrimary = IsPrimaryEnemy(x.B, owner);
            if (InRoster(x.B, owner))
                RemoveFromRoster(ref x.B, owner);
            if (isPrimary && aliveCount > 0)
            {
                bool allSecondary = true;
                for (int index = 0; index < aliveCount; index++)
                    allSecondary &= !IsPrimaryEnemy(x.B, aliveTeammates[index]);
                if (allSecondary)
                    Kill(ref x, aliveTeammates[..aliveCount], force: false);
            }
        }
        else if (owner == FlashLimits.PlayerOwner)
        {
            HandlePlayerDeath(ref x);
        }
        else
        {
            throw new FlashUnsupportedException("Osty 死亡尚未实现。");
        }
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.HandlePlayerDeath</c> for a single-player combat.</summary>
    private static void HandlePlayerDeath(ref FlashExec x)
    {
        RemovePowersOf(ref x.B, FlashLimits.PlayerOwner);
        x.B.OrbCount = 0;
        for (int index = 0; index < FlashLimits.Orbs; index++)
            x.B.Orbs[index] = default;
        x.B.Flags &= ~FlashBattleFlags.PlayerHooksActive;
        // With every player dead the native flow does not remove the cards from combat.
    }

    /// <summary>Mirrors <c>SimCreatureState.DamageBlock</c>.</summary>
    public static decimal DamageBlock(ref FlashCreature creature, decimal amount, FlashValueProps props)
    {
        decimal blocked = (props & FlashValueProps.Unblockable) != 0 ? 0m : Math.Min(creature.Block, amount);
        creature.Block -= (int)blocked;
        return blocked;
    }

    /// <summary>Mirrors <c>SimCreatureState.LoseHp</c>.</summary>
    public static FlashDamageResult LoseHp(ref FlashCreature creature, byte owner, decimal amount, FlashValueProps props)
    {
        bool killed = creature.CurrentHp > 0 && amount >= creature.CurrentHp;
        int previous = creature.CurrentHp;
        int damage = (int)Math.Min(amount, 999_999_999m);
        creature.CurrentHp = Math.Max(previous - damage, 0);
        return new FlashDamageResult
        {
            Receiver = owner,
            Props = props,
            UnblockedDamage = previous - creature.CurrentHp,
            WasTargetKilled = killed,
            OverkillDamage = killed ? Math.Max(damage - previous, 0) : 0,
        };
    }

    /// <summary>
    /// Mirrors the parts of <c>SimulatedCombatState.RecordDamageReceived</c> that Flash tracks: the
    /// lost-HP-this-turn mark of every receiver and the player's cumulative HP loss. Powered attack
    /// hit records are not tracked yet.
    /// </summary>
    private static void RecordDamageReceived(ref FlashBattle battle, byte receiver, in FlashDamageResult result)
    {
        if (result.UnblockedDamage <= 0)
            return;
        battle.Creature(receiver).Flags |= FlashCreatureFlags.LostHpThisTurn;
        if (receiver == FlashLimits.PlayerOwner)
            battle.AddCounter(FlashCounter.CumulativeHpLost, result.UnblockedDamage);
    }

    /// <summary>
    /// Mirrors <c>CorePowerSupport.ApplyEnemyDeathPowers</c> for enemies without death powers:
    /// marks new deaths processed, removes the dead enemy's powers and completes its death phase.
    /// </summary>
    public static void ApplyEnemyDeathPowers(ref FlashExec x)
    {
        Span<byte> newlyDead = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        for (int index = 0; index < x.B.EnemyCount; index++)
        {
            ref FlashEnemy enemy = ref x.B.Enemies[index];
            if ((enemy.Flags & FlashEnemyFlags.DeathProcessed) != 0 || enemy.Creature.IsAlive)
                continue;
            if (enemy.DeathPhase is FlashDeathPhase.Reviving or FlashDeathPhase.PermanentlyDead)
            {
                enemy.Flags |= FlashEnemyFlags.DeathProcessed;
                continue;
            }
            newlyDead[count++] = (byte)index;
        }
        for (int index = 0; index < count; index++)
        {
            byte owner = EnemyOwner(newlyDead[index]);
            x.B.Enemies[newlyDead[index]].Flags |= FlashEnemyFlags.DeathProcessed;
            // DeathPowerSupport.Trigger: no supported death powers yet. Stolen-resource recovery is
            // rejected at admission until theft is supported.
            RemovePowersOf(ref x.B, owner);
            ref FlashEnemy enemy = ref x.B.Enemies[newlyDead[index]];
            if (enemy.DeathPhase != FlashDeathPhase.Reviving)
                enemy.DeathPhase = FlashDeathPhase.PermanentlyDead;
        }
    }
}
