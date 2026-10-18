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

    /// <summary><c>AfterDamageReceivedMirrorContext.Source</c>: the played card that dealt the damage, if any.</summary>
    public bool HasCardSource;
    public FlashCard CardSource;
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
        // BeginAttackContext: BeforeAttack iterates the combat listeners behind the ending gate
        // (HookMirrors.BeforeAttack). ModifyAttackHitCount has no supported listeners yet.
        BeforeAttackHook before = new() { Attacker = attacker, Props = props };
        FlashHooks.Dispatch(ref x, ref before, gatedByEnding: true);
        if (x.NeedsChoice)
            return;
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
        // HookMirrors.BeforeDamageReceived iterates the run listeners without the ending gate; deck cards
        // outside combat with this hook are rejected at admission, so the combat listeners are the list.
        BeforeDamageReceivedHook before = new()
        {
            Target = target, Amount = modified, Props = props, Dealer = dealer, HasCardSource = hasCardSource, CardSource = cardSource,
        };
        FlashHooks.Dispatch(ref x, ref before, gatedByEnding: false);
        if (x.NeedsChoice)
            return false;
        byte blockTarget = target == FlashLimits.OstyOwner ? FlashLimits.PlayerOwner : target;
        decimal blocked = DamageBlock(ref x.B.Creature(blockTarget), modified, props);
        decimal unblocked = Math.Max(modified - blocked, 0m);
        // ModifyHpLost before the Osty redirect (ModifyUnblockedDamageTarget; Osty is rejected at admission),
        // then after it, then AfterModifyingHpLostAfterOsty for the after-Osty changers.
        if ((FlashRegistry.AnyHooks & HpLossHooks) != 0)
        {
            unblocked = ModifyHpLost(ref x, target, unblocked, props, dealer, hasCardSource, cardSource);
            if (x.NeedsChoice)
                return false;
        }
        ref FlashCreature creature = ref x.B.Creature(target);
        FlashDamageResult result = LoseHp(ref creature, target, unblocked, props);
        result.HasCardSource = hasCardSource;
        result.CardSource = cardSource;
        result.BlockedDamage = (int)blocked;
        result.WasBlockBroken = creature.Block <= 0 && blocked > 0m;
        result.WasFullyBlocked = (props & FlashValueProps.Unblockable) == 0
            && (blocked > 0m || creature.Block > 0)
            && (int)unblocked == 0;
        RecordDamageReceived(ref x.B, target, result);
        results[count++] = result;
        return true;
    }

    private const FlashHookMask HpLossHooks = FlashHookMask.ModifyHpLostBeforeOsty | FlashHookMask.ModifyHpLostBeforeOstyLate
        | FlashHookMask.ModifyHpLostAfterOsty | FlashHookMask.ModifyHpLostAfterOstyLate | FlashHookMask.AfterModifyingHpLostAfterOsty;

    private static decimal ModifyHpLost(ref FlashExec x, byte target, decimal unblocked, FlashValueProps props, byte dealer,
        bool hasCardSource, in FlashCard cardSource)
    {
        FlashHpLossContext loss = new(target, props, dealer, hasCardSource, cardSource);
        Span<FlashListener> changed = stackalloc FlashListener[FlashHooks.MaxListenerCount * 2];
        unblocked = FlashHooks.ModifyHpLost(ref x, loss, unblocked, afterOsty: false, changed, out _);
        unblocked = FlashHooks.ModifyHpLost(ref x, loss, unblocked, afterOsty: true, changed, out int changedCount);
        FlashHooks.AfterModifyingHpLostAfterOsty(ref x, changed[..changedCount]);
        return unblocked;
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.ProcessDamageResults</c>.</summary>
    private static void ProcessDamageResults(ref FlashExec x, scoped ReadOnlySpan<FlashDamageResult> results, byte dealer)
    {
        Span<byte> killed = stackalloc byte[MaxDamageResults];
        int killedCount = 0;
        foreach (FlashDamageResult result in results)
        {
            if (result.WasBlockBroken)
            {
                AfterBlockBrokenHook broken = new() { Target = result.Receiver, Breaker = dealer };
                FlashHooks.Dispatch(ref x, ref broken, gatedByEnding: false);
                if (x.NeedsChoice)
                    return;
            }
            if (result.UnblockedDamage > 0)
            {
                AfterCurrentHpChanged(ref x, result.Receiver, -result.UnblockedDamage);
                if (x.NeedsChoice)
                    return;
            }
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

    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.KillWithoutCheckingWinCondition</c>: lose all HP and
    /// <c>AfterCurrentHpChanged</c>, <c>BeforeDeath</c>, then <c>ShouldDie</c> (normal and Late; skipped when forced or
    /// at zero max HP). A death removes the enemy from the roster unless a power prevents it, runs <c>AfterDeath</c>,
    /// and kills the remaining secondary teammates of a primary enemy; a prevented death runs <c>AfterDeath</c> and
    /// the preventer's <c>AfterPreventingDeath</c>, and retries while the creature is still dead.
    /// </summary>
    private static void KillWithoutCheckingWinCondition(ref FlashExec x, byte owner, bool force, int recursion = 0)
    {
        int currentHp = x.B.Creature(owner).CurrentHp;
        if (currentHp > 0)
        {
            LoseHp(ref x.B.Creature(owner), owner, currentHp, FlashValueProps.Unblockable | FlashValueProps.Unpowered);
            AfterCurrentHpChanged(ref x, owner, -currentHp);
            if (x.NeedsChoice)
                return;
        }
        BeforeDeathHook before = new() { Creature = owner };
        FlashHooks.Dispatch(ref x, ref before, gatedByEnding: false);
        if (x.NeedsChoice)
            return;
        bool shouldDie = force || x.B.Creature(owner).MaxHp <= 0;
        FlashListener preventer = default;
        if (!shouldDie)
            shouldDie = ShouldDie(ref x, owner, out preventer);
        if (x.NeedsChoice)
            return;
        if (!shouldDie)
        {
            AfterDeathHook prevented = new() { Creature = owner, WasRemovalPrevented = true };
            FlashHooks.Dispatch(ref x, ref prevented, gatedByEnding: false);
            if (x.NeedsChoice)
                return;
            FlashHooks.Definition(x.B, preventer).AfterPreventingDeath(ref x, preventer, owner);
            if (x.NeedsChoice)
                return;
            if (x.B.Creature(owner).IsDead)
            {
                if (recursion >= 10)
                    throw new InvalidOperationException("死亡被连续阻止十次后，生物仍未复活。");
                KillWithoutCheckingWinCondition(ref x, owner, force, recursion + 1);
            }
            return;
        }
        bool remove = ShouldRemoveAfterDeath(x.B, owner);
        AfterDeathHook after = new() { Creature = owner, WasRemovalPrevented = false };
        FlashHooks.Dispatch(ref x, ref after, gatedByEnding: false);
        if (x.NeedsChoice)
            return;
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
            if (remove && InRoster(x.B, owner))
                RemoveFromRoster(ref x.B, owner);
            // ICombatPredictionCreatureSemantics.IsPrimaryEnemy reads EffectivePowers, which hold only the powers of
            // roster creatures: once removed, the dead enemy has no secondary-making power there.
            bool isPrimary = !InRoster(x.B, owner) || IsPrimaryEnemy(x.B, owner);
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

    /// <summary>Native <c>ShouldDie</c> then <c>ShouldDieLate</c> over the run listeners; the first refusal is the preventer.</summary>
    private static bool ShouldDie(ref FlashExec x, byte owner, out FlashListener preventer)
    {
        preventer = default;
        if ((FlashRegistry.AnyHooks & (FlashHookMask.ShouldDie | FlashHookMask.ShouldDieLate)) == 0)
            return true;
        Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        for (int pass = 0; pass < 2; pass++)
        {
            int count = FlashHooks.CollectListeners(x.B, pass == 0 ? FlashHookMask.ShouldDie : FlashHookMask.ShouldDieLate, listeners);
            for (int index = 0; index < count; index++)
            {
                FlashListenerDef def = FlashHooks.Definition(x.B, listeners[index]);
                bool dies = pass == 0 ? def.ShouldDie(ref x, listeners[index], owner) : def.ShouldDieLate(ref x, listeners[index], owner);
                if (!dies)
                {
                    preventer = listeners[index];
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary><c>ICombatPredictionCreatureSemantics.ShouldRemoveAfterDeath</c>: no positive removal-preventing power.</summary>
    private static bool ShouldRemoveAfterDeath(in FlashBattle battle, byte owner)
    {
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (power.Owner == owner && power.Amount > 0 && FlashRegistry.Power(power.Id).PreventsRemovalAfterDeath)
                return false;
        }
        return true;
    }

    /// <summary>Native <c>AfterCurrentHpChanged</c> over the run listeners, without the ending gate.</summary>
    internal static void AfterCurrentHpChanged(ref FlashExec x, byte creature, decimal delta)
    {
        AfterCurrentHpChangedHook changed = new() { Creature = creature, Delta = delta };
        FlashHooks.Dispatch(ref x, ref changed, gatedByEnding: false);
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
        {
            battle.AddCounter(FlashCounter.CumulativeHpLost, result.UnblockedDamage);
            battle.AddCounter(FlashCounter.PlayerUnblockedDamageEvents, 1);
        }
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
            // An illusion that just began reviving (IllusionPower.AfterDeath) still goes through the death
            // pass: DeathPowerSupport.Trigger calls BeginIllusionRevive and then RemovePowersAfterDeath for
            // the same creature, so it loses its non-temporary debuffs.
            if (enemy.DeathPhase is FlashDeathPhase.PermanentlyDead)
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
            RemovePowersAfterDeath(ref x, owner);
            ref FlashEnemy enemy = ref x.B.Enemies[newlyDead[index]];
            if (enemy.DeathPhase != FlashDeathPhase.Reviving)
                enemy.DeathPhase = FlashDeathPhase.PermanentlyDead;
            ApplierDeath(ref x, owner);
            if (x.NeedsChoice)
                return;
        }
        ResolvePowerAmountChanges(ref x);
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.RemovePowersAfterDeath</c>
    /// (<c>SimulatedCombatState.DeathLifecycle.cs</c>): an illusion owner keeps its buffs and
    /// temporary debuffs; every other power follows
    /// <see cref="FlashPowerDef.ShouldPowerBeRemovedAfterOwnerDeath"/>. A creature no longer in the
    /// roster loses everything.
    /// </summary>
    private static void RemovePowersAfterDeath(ref FlashExec x, byte owner)
    {
        bool illusionHook = false;
        if (InRoster(x.B, owner))
        {
            for (int index = 0; index < x.B.PowerCount; index++)
            {
                ref readonly FlashPower power = ref x.B.Powers[index];
                if (power.Owner == owner && power.Amount > 0
                    && FlashRegistry.Power(power.Id).KeepsOwnerBuffsAfterDeath)
                {
                    illusionHook = true;
                    break;
                }
            }
        }
        for (int index = x.B.PowerCount - 1; index >= 0; index--)
        {
            ref readonly FlashPower power = ref x.B.Powers[index];
            if (power.Owner != owner || power.Amount == 0)
                continue;
            FlashPowerDef def = FlashRegistry.Power(power.Id);
            bool keep = !def.ShouldPowerBeRemovedAfterOwnerDeath;
            if (illusionHook)
                keep = def.Type != FlashPowerType.Debuff || def.IsTemporaryPower;
            if (!keep)
                SetPowerAmount(ref x.B, owner, power.Id, 0);
        }
        if (!InRoster(x.B, owner))
            RemovePowersOf(ref x.B, owner);
    }
}
