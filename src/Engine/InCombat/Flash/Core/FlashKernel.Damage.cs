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
    // Every target may redirect once (Osty) and then spill over to the original receiver, so one
    // Damage call over all enemies plus the player can produce two results per target.
    private const int MaxDamageResults = (FlashLimits.Enemies + 1) * 2;

    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.ExecuteAttack</c> for a card attack.
    /// </summary>
    public static void CardAttack(ref FlashExec x, decimal damagePerHit, int hitCount, FlashAttackTargeting targeting,
        byte singleTarget, FlashValueProps props = FlashValueProps.Move, byte attacker = FlashLimits.PlayerOwner)
    {
        // Single-target attacks read Creature(singleTarget); a missing target is an unsupported
        // caller assumption, not an index error.
        if (targeting == FlashAttackTargeting.Single && singleTarget == FlashLimits.NoOwner)
            throw new FlashUnsupportedException("单体攻击需要目标 owner。");
        if (IsOverOrEnding(x.B))
            return;
        // A card attack from Osty (<c>AttackCommand.FromOsty</c>) uses Osty as the attacker, so strength,
        // weak and the attack history apply to the pet. The existing engine returns early while the
        // attacker is dead.
        if (x.B.Creature(attacker).IsDead)
            return;
        x.AttackUnblockedHits = 0;
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
                // DamageCmd's TargetingRandomOpponents defaults to allowDuplicates: true; every
                // vanilla random-target attack uses that default, so each hit picks among all living
                // enemies and consumes one CombatTargets draw.
                targets[0] = targets[x.B.RngStream(FlashRngStream.CombatTargets).NextInt(0, count)];
                count = 1;
            }
            Damage(ref x, targets[..count], damagePerHit, props, attacker, hasCardSource: true, x.Card);
            if (x.NeedsChoice)
                return;
        }
        // EndAttackContext(completed): RecordCreatureAttacked, then AfterAttack iterates the combat
        // listeners without the ending gate. The per-creature attack counter is keyed by the attacker
        // (<c>SimulatedCombatState.RecordCreatureAttacked</c>): the player's is PlayerAttacksThisTurn and
        // Osty's is OstyAttacksThisTurn (Rattle).
        if (attacker == FlashLimits.PlayerOwner)
            x.B.AddCounter(FlashCounter.PlayerAttacksThisTurn, 1);
        else if (attacker == FlashLimits.OstyOwner)
            x.B.AddCounter(FlashCounter.OstyAttacksThisTurn, 1);
        AfterAttackHook after = new() { Attacker = attacker };
        FlashHooks.Dispatch(ref x, ref after, gatedByEnding: false);
    }

    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.Damage</c>. Returns whether any result was fully blocked,
    /// which the monster-move path reads for the Imbalanced stun (<c>MonsterMoveSemantics</c> keeps the
    /// same per-hit flag).
    /// </summary>
    public static bool Damage(ref FlashExec x, scoped ReadOnlySpan<byte> targets, decimal amount, FlashValueProps props,
        byte dealer, bool hasCardSource, in FlashCard cardSource)
    {
        if (dealer != FlashLimits.NoOwner && x.B.Creature(dealer).IsDead || targets.Length == 0)
            return false;
        Span<FlashDamageResult> results = stackalloc FlashDamageResult[MaxDamageResults];
        int count = 0;
        foreach (byte target in targets)
        {
            if (!TryDamageTarget(ref x, target, amount, props, dealer, hasCardSource, cardSource, results, ref count))
                return false;
        }
        // Capture the damage before the result hooks run: a hook may deal nested damage and overwrite
        // the "last damage" fields. Fisticuffs sums the whole card play; Omnislice reads this call's
        // amount (TotalDamage + OverkillDamage) for the copied hit.
        int total = 0;
        int overkill = 0;
        for (int index = 0; index < count; index++)
        {
            // Native DamageResult.TotalDamage is BlockedDamage + UnblockedDamage; OverkillDamage is
            // the part beyond the target's HP. Fisticuffs sums TotalDamage + OverkillDamage.
            total += results[index].BlockedDamage + results[index].UnblockedDamage;
            overkill += results[index].OverkillDamage;
            if (hasCardSource)
            {
                x.CardDamageTotal += results[index].BlockedDamage + results[index].UnblockedDamage
                    + results[index].OverkillDamage;
                if (results[index].WasTargetKilled && dealer == FlashLimits.PlayerOwner)
                    x.CardKilledTarget = true;
            }
        }
        x.LastDamageTotal = total;
        x.LastDamageOverkill = overkill;
        ProcessDamageResults(ref x, results[..count], dealer);
        for (int index = 0; index < count; index++)
        {
            if (results[index].Receiver == FlashLimits.PlayerOwner && results[index].UnblockedDamage > 0)
                x.AttackUnblockedHits++;
        }
        for (int index = 0; index < count; index++)
        {
            if (results[index].WasFullyBlocked)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.TryDamageTarget</c>, including the <c>ModifyUnblockedDamageTarget</c>
    /// redirect (Osty's <c>DieForYouPower</c>) and its overkill spill-over to the original receiver.
    /// </summary>
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
        // The block holder is the pet owner (CreatureCmd.UseBlockTarget): Osty's incoming damage spends
        // the player's block.
        byte blockTarget = target == FlashLimits.OstyOwner ? FlashLimits.PlayerOwner : target;
        decimal blocked = DamageBlock(ref x.B.Creature(blockTarget), modified, props);
        decimal unblocked = Math.Max(modified - blocked, 0m);
        // ModifyHpLost's before-Osty phase is evaluated on the original target, then
        // Hook.ModifyUnblockedDamageTarget chooses the HP-loss receiver, then the after-Osty phase runs
        // on that receiver. A listener that changed the truncated amount is notified afterwards. The
        // redirect query itself is dispatched even without HP-loss hooks (DieForYouPower has none).
        FlashHpLossContext loss = new(target, props, dealer, hasCardSource, cardSource);
        Span<FlashListener> changed = stackalloc FlashListener[FlashHooks.MaxListenerCount * 2];
        bool hasHpLossHooks = (FlashRegistry.AnyHooks & HpLossHooks) != 0;
        if (hasHpLossHooks)
        {
            unblocked = FlashHooks.ModifyHpLost(ref x, loss, unblocked, afterOsty: false, changed, out _);
            if (x.NeedsChoice)
                return false;
        }
        byte redirect = FlashHooks.ModifyUnblockedDamageTarget(ref x, target, unblocked, props, dealer);
        if (x.NeedsChoice)
            return false;
        if (hasHpLossHooks && (redirect == target
                || (FlashRegistry.AnyHooks & AfterOstyHooks) != 0))
        {
            FlashHpLossContext redirectedLoss = new(redirect, props, dealer, hasCardSource, cardSource);
            unblocked = FlashHooks.ModifyHpLost(ref x, redirectedLoss, unblocked, afterOsty: true, changed, out int changedCount);
            FlashHooks.AfterModifyingHpLostAfterOsty(ref x, changed[..changedCount]);
            if (x.NeedsChoice)
                return false;
        }
        if (redirect != target)
            return RecordRedirectedDamage(ref x, target, redirect, blocked, unblocked, props, dealer,
                hasCardSource, cardSource, results, ref count);
        target = redirect;
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

    /// <summary>
    /// The two-result branch of <c>CombatPredictionSimulator.TryDamageTarget</c> when
    /// <c>ModifyUnblockedDamageTarget</c> moved the HP loss off the original receiver: the redirected
    /// receiver loses <paramref name="unblocked"/>, then the original receiver loses the modified overkill.
    /// </summary>
    private static bool RecordRedirectedDamage(ref FlashExec x, byte originalTarget, byte redirect,
        decimal blocked, decimal unblocked, FlashValueProps props, byte dealer, bool hasCardSource,
        in FlashCard cardSource, scoped Span<FlashDamageResult> results, ref int count)
    {
        ref FlashCreature redirected = ref x.B.Creature(redirect);
        FlashDamageResult redirectedResult = LoseHp(ref redirected, redirect, unblocked, props);
        redirectedResult.HasCardSource = hasCardSource;
        redirectedResult.CardSource = cardSource;
        redirectedResult.BlockedDamage = (int)blocked;
        ref FlashCreature original = ref x.B.Creature(originalTarget);
        redirectedResult.WasBlockBroken = original.Block <= 0 && blocked > 0m;
        redirectedResult.WasFullyBlocked = (props & FlashValueProps.Unblockable) == 0
            && (blocked > 0m || original.Block > 0)
            && (int)unblocked == 0;
        RecordDamageReceived(ref x.B, redirect, redirectedResult);
        results[count++] = redirectedResult;

        decimal overflow = redirectedResult.OverkillDamage;
        if ((FlashRegistry.AnyHooks & HpLossHooks) != 0)
        {
            Span<FlashListener> changed = stackalloc FlashListener[FlashHooks.MaxListenerCount * 2];
            FlashHpLossContext loss = new(originalTarget, props, dealer, hasCardSource, cardSource);
            overflow = FlashHooks.ModifyHpLost(ref x, loss, overflow, afterOsty: true, changed, out int changedCount);
            FlashHooks.AfterModifyingHpLostAfterOsty(ref x, changed[..changedCount]);
            if (x.NeedsChoice)
                return false;
        }
        FlashDamageResult originalResult = overflow > 0m
            ? LoseHp(ref original, originalTarget, overflow, props)
            : new FlashDamageResult { Receiver = originalTarget, Props = props };
        originalResult.HasCardSource = hasCardSource;
        originalResult.CardSource = cardSource;
        originalResult.BlockedDamage = (int)blocked;
        originalResult.WasBlockBroken = redirectedResult.WasBlockBroken;
        originalResult.WasFullyBlocked = redirectedResult.WasFullyBlocked;
        RecordDamageReceived(ref x.B, originalTarget, originalResult);
        results[count++] = originalResult;
        return true;
    }

    private const FlashHookMask HpLossHooks = FlashHookMask.ModifyHpLostBeforeOsty | FlashHookMask.ModifyHpLostBeforeOstyLate
        | FlashHookMask.ModifyHpLostAfterOsty | FlashHookMask.ModifyHpLostAfterOstyLate | FlashHookMask.AfterModifyingHpLostAfterOsty;

    private const FlashHookMask AfterOstyHooks = FlashHookMask.ModifyHpLostAfterOsty | FlashHookMask.ModifyHpLostAfterOstyLate
        | FlashHookMask.AfterModifyingHpLostAfterOsty;

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
        // Osty: DieForYouPower.ShouldCreatureBeRemovedFromCombatAfterDeath keeps the pet in combat, and
        // the existing engine's KillWithoutCheckingWinCondition runs no player-death flow for a pet, so
        // the dead Osty stays in place with its powers (a later summon revives it). No extra work here.
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
        // CreatureCmd's player-death cleanup calls OrbQueue.Clear(), which also drops the capacity.
        x.B.OrbCapacity = 0;
        x.B.OrbCount = 0;
        for (int index = 0; index < FlashLimits.Orbs; index++)
            x.B.Orbs[index] = default;
        // The native flow kills a still-alive Osty with force after clearing the orbs and before the
        // player's hooks are deactivated (<c>HandlePlayerDeath</c> in CombatPredictionSimulator).
        if ((x.B.Flags & FlashBattleFlags.OstyPresent) != 0 && x.B.Osty.IsAlive)
        {
            Span<byte> osty = stackalloc byte[1];
            osty[0] = FlashLimits.OstyOwner;
            Kill(ref x, osty, force: true);
            if (x.NeedsChoice)
                return;
        }
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
            // DeathPowerSupport.Trigger runs the death cases of every positive power over a snapshot
            // taken before this death's power cleanup; stolen-resource recovery is rejected at
            // admission until theft is supported.
            TriggerDeathPowers(ref x, owner);
            if (x.NeedsChoice)
                return;
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
    /// The death cases of <c>DeathPowerSupport.Trigger</c>: every positive power's
    /// <see cref="FlashPowerDef.DeathTrigger"/>, over the listener order snapshot taken before this
    /// death's own power cleanup (so newly spawned powers are not visited by the same pass).
    /// </summary>
    private static void TriggerDeathPowers(ref FlashExec x, byte dead)
    {
        Span<byte> owners = stackalloc byte[FlashLimits.Powers];
        Span<ushort> ids = stackalloc ushort[FlashLimits.Powers];
        int count = 0;
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly FlashPower power = ref x.B.Powers[index];
            if (power.Amount <= 0)
                continue;
            owners[count] = power.Owner;
            ids[count] = power.Id;
            count++;
        }
        for (int index = 0; index < count; index++)
        {
            int position = FindPower(x.B, owners[index], ids[index]);
            if (position < 0 || x.B.Powers[position].Amount <= 0)
                continue;
            FlashRegistry.Power(ids[index]).DeathTrigger(ref x,
                new FlashListener(FlashListenerKind.Power, checked((byte)position), owners[index]), dead);
            if (x.NeedsChoice)
                return;
        }
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
