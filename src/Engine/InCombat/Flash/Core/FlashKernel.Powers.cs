namespace CombatSolver.Engine.InCombat.Flash;

public static partial class FlashKernel
{
    /// <summary>Index of the stacking instance of <paramref name="powerId"/> on <paramref name="owner"/>, or -1.</summary>
    public static int FindPower(in FlashBattle battle, byte owner, ushort powerId)
    {
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (power.Owner == owner && power.Id == powerId)
                return index;
            if (power.Owner > owner)
                break;
        }
        return -1;
    }

    /// <summary>
    /// Index of the <paramref name="occurrence"/>-th instance of <paramref name="powerId"/> on
    /// <paramref name="owner"/> in table order, or -1. Instanced powers share (owner, id), so a
    /// listener snapshot must keep the occurrence to address the exact row after a removal.
    /// </summary>
    public static int FindPowerOccurrence(in FlashBattle battle, byte owner, ushort powerId, int occurrence)
    {
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (power.Owner != owner || power.Id != powerId)
                continue;
            if (occurrence-- == 0)
                return index;
        }
        return -1;
    }

    /// <summary>
    /// Index of the most recently added instance of <paramref name="powerId"/> on <paramref name="owner"/>,
    /// or -1. Used by a card after applying an instanced power to initialize that exact instance.
    /// </summary>
    public static int FindLastPower(in FlashBattle battle, byte owner, ushort powerId)
    {
        int found = -1;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (power.Owner == owner && power.Id == powerId)
                found = index;
        }
        return found;
    }

    /// <summary>
    /// <c>CorePowerSupport</c>'s <c>targetDeathTriggersFatal</c>: every power on the target allows its
    /// owner's death to count as a fatal kill (HandOfGreed, Feed, TheHunt).
    /// </summary>
    public static bool TargetDeathTriggersFatal(in FlashBattle battle, byte owner)
    {
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (power.Owner != owner)
                continue;
            if (!FlashRegistry.Power(power.Id).ShouldOwnerDeathTriggerFatal(battle,
                    new FlashListener(FlashListenerKind.Power, (byte)index, owner)))
                return false;
        }
        return true;
    }

    /// <summary>Mirrors <c>SimulatedCombatState.GetAmount&lt;T&gt;</c>.</summary>
    public static int GetAmount(in FlashBattle battle, byte owner, ushort powerId)
    {
        int index = FindPower(battle, owner, powerId);
        return index < 0 ? 0 : battle.Powers[index].Amount;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.ApplyWithBeforeApplied</c> for the supported powers and
    /// returns the applied delta.
    /// </summary>
    public static int ApplyPower(ref FlashExec x, byte target, ushort powerId, int amount, byte applier,
        byte powerTarget = FlashLimits.NoOwner)
    {
        TryResolveMonologuesBeforeCompensation(ref x);
        if (amount == 0 || !CanReceivePowers(x.B, target))
            return 0;
        FlashPowerDef def = FlashRegistry.Power(powerId);
        // SimulatedCombatState.ModifyPowerAmountForRelics: ObserveUnsettlingLamp (the native
        // BeforePowerAmountChanged pass), the additive given pass (SneckoSkull), the multiplicative
        // given pass (UnsettlingLamp), then the received pass (Ruined Helmet), all before Artifact.
        // UnsettlingLamp is the only listener of the two lamp-specific passes.
        int cardSourceSerial = x.CardPowerSourceActive ? x.CardPowerSourceSerial : 0;
        bool lampHooks = x.R.UnsettlingLampRelicId != ushort.MaxValue
            || (x.R.HasIdentityContent && x.B.RelicCount != 0 && HasRelic(x.B, "UNSETTLING_LAMP"));
        if (lampHooks)
        {
            FlashHooks.BeforePowerAmountChanged(ref x, powerId, amount, target, applier, cardSourceSerial);
            if (x.NeedsChoice)
                return 0;
        }
        amount = (int)FlashHooks.ModifyPowerAmountGiven(x.B, x.R, powerId, applier, target, amount);
        if (lampHooks)
        {
            amount = (int)FlashHooks.ModifyPowerAmountGivenMultiplicative(ref x, powerId, applier, target,
                amount, cardSourceSerial);
        }
        Span<FlashListener> received = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        amount = FlashHooks.ModifyPowerAmountReceived(x.B, x.R, powerId, target, amount, received,
            out int receivedCount);
        if (receivedCount > 0)
        {
            FlashHooks.AfterModifyingPowerAmountReceived(ref x, powerId, target, received[..receivedCount]);
            if (x.NeedsChoice)
                return 0;
        }
        if (def.TypeForAmount(amount) == FlashPowerType.Debuff && ConsumeArtifact(ref x.B, target))
            return 0;
        return AddPowerAmount(ref x, target, powerId, def, amount, applier, out _, powerTarget);
    }

    /// <summary>
    /// <c>SimulatedCombatState.ApplyPowerFromSource(..., cardSource: null)</c>: the application is not
    /// part of the current card's power scope, so card-source relic passes such as UnsettlingLamp do
    /// not observe or double it. Power hooks whose native mirror passes a null card source use this
    /// (Envenom/ReaperForm doom and MonarchsGaze's temporary Strength loss).
    /// </summary>
    public static int ApplyPowerWithoutCardSource(ref FlashExec x, byte target, ushort powerId, int amount,
        byte applier)
    {
        bool active = x.CardPowerSourceActive;
        int serial = x.CardPowerSourceSerial;
        x.CardPowerSourceActive = false;
        int applied = ApplyPower(ref x, target, powerId, amount, applier);
        x.CardPowerSourceActive = active;
        x.CardPowerSourceSerial = serial;
        return applied;
    }

    /// <summary>
    /// <see cref="ApplyTemporaryStrength"/> with <c>ApplyPowerFromSource(..., cardSource: null)</c>'s
    /// suppressed card scope (MonarchsGazePower).
    /// </summary>
    public static int ApplyTemporaryStrengthWithoutCardSource(ref FlashExec x, byte target, ushort temporaryId,
        int amount, byte applier, int sign)
    {
        bool active = x.CardPowerSourceActive;
        int serial = x.CardPowerSourceSerial;
        x.CardPowerSourceActive = false;
        int applied = ApplyTemporaryStrength(ref x, target, temporaryId, amount, applier, sign);
        x.CardPowerSourceActive = active;
        x.CardPowerSourceSerial = serial;
        return applied;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.ApplyTemporaryStrength</c> (<c>ApplyTemporaryStrengthGain</c> with
    /// <paramref name="sign"/> 1, <c>ApplyTemporaryStrengthLoss</c> with -1): Artifact is checked against
    /// the temporary power; a first (or instanced) application runs its native <c>BeforeApplied</c>,
    /// changing Strength by sign × amount before the temporary power is added; afterwards a Strength
    /// change by sign × amount follows when the requested amount differs from the resulting counter
    /// (the stacking case, native <c>AfterPowerAmountChanged</c>). Returns the applied delta.
    /// </summary>
    public static int ApplyTemporaryStrength(ref FlashExec x, byte target, ushort temporaryId, int amount, byte applier, int sign)
    {
        TryResolveMonologuesBeforeCompensation(ref x);
        if (amount == 0 || !CanReceivePowers(x.B, target))
            return 0;
        FlashPowerDef def = FlashRegistry.Power(temporaryId);
        // ApplyWithBeforeApplied runs ModifyPowerAmountForRelics before the native BeforeApplied, so the
        // modified amount is what the inner Strength change uses (and what UnsettlingLamp doubles).
        Span<FlashListener> received = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        amount = ApplyTemporaryPowerAmountModifiers(ref x, temporaryId, amount, target, applier, received,
            out int receivedCount);
        if (receivedCount > 0)
        {
            FlashHooks.AfterModifyingPowerAmountReceived(ref x, temporaryId, target, received[..receivedCount]);
            if (x.NeedsChoice)
                return 0;
        }
        if (def.TypeForAmount(amount) == FlashPowerType.Debuff && ConsumeArtifact(ref x.B, target))
            return 0;
        ushort strength = FlashRegistry.TryPower("STRENGTH_POWER", out ushort id)
            ? id
            : throw new FlashUnsupportedException("临时力量需要力量的 Flash 定义。");
        if (def.IsInstanced || GetAmount(x.B, target, temporaryId) == 0)
            ApplyPower(ref x, target, strength, sign * amount, applier);
        int applied = AddPowerAmount(ref x, target, temporaryId, def, amount, applier, out int resulting);
        if (amount != resulting)
            ApplyPower(ref x, target, strength, sign * amount, applier);
        return applied;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.ApplyTemporaryFocus</c> (<c>ApplyTemporaryFocusGain</c> with
    /// <paramref name="sign"/> 1, <c>ApplyTemporaryFocusLoss</c> with -1): the temporary power's native
    /// <c>BeforeApplied</c> applies sign × amount for a first/instanced application, and a stacking
    /// application applies sign × the resulting delta after the counter changes.
    /// </summary>
    public static int ApplyTemporaryFocus(ref FlashExec x, byte target, ushort temporaryId, int amount,
        byte applier, int sign)
    {
        TryResolveMonologuesBeforeCompensation(ref x);
        if (amount == 0 || !CanReceivePowers(x.B, target))
            return 0;
        FlashPowerDef def = FlashRegistry.Power(temporaryId);
        Span<FlashListener> received = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        amount = ApplyTemporaryPowerAmountModifiers(ref x, temporaryId, amount, target, applier, received,
            out int receivedCount);
        if (receivedCount > 0)
        {
            FlashHooks.AfterModifyingPowerAmountReceived(ref x, temporaryId, target, received[..receivedCount]);
            if (x.NeedsChoice)
                return 0;
        }
        if (def.TypeForAmount(amount) == FlashPowerType.Debuff && ConsumeArtifact(ref x.B, target))
            return 0;
        ushort focus = FlashRegistry.TryPower("FOCUS_POWER", out ushort id)
            ? id
            : throw new FlashUnsupportedException("临时集中需要集中的 Flash 定义。");
        bool alreadyApplied = GetAmount(x.B, target, temporaryId) != 0;
        if (def.IsInstanced || !alreadyApplied)
            ApplyPower(ref x, target, focus, sign * amount, applier);
        int applied = AddPowerAmount(ref x, target, temporaryId, def, amount, applier, out _);
        if (alreadyApplied && applied != 0)
            ApplyPower(ref x, target, focus, sign * applied, applier);
        return applied;
    }

    /// <summary>
    /// The relic part of <c>ApplyWithBeforeApplied</c> for a temporary stat power: the lamp observation,
    /// additive/multiplicative given passes and the received pass. The caller runs Artifact after it.
    /// </summary>
    private static int ApplyTemporaryPowerAmountModifiers(ref FlashExec x,
        ushort powerId, int amount, byte target, byte applier, scoped Span<FlashListener> received,
        out int receivedCount)
    {
        int cardSourceSerial = x.CardPowerSourceActive ? x.CardPowerSourceSerial : 0;
        FlashHooks.BeforePowerAmountChanged(ref x, powerId, amount, target, applier, cardSourceSerial);
        amount = (int)FlashHooks.ModifyPowerAmountGiven(x.B, x.R, powerId, applier, target, amount);
        amount = (int)FlashHooks.ModifyPowerAmountGivenMultiplicative(ref x, powerId, applier, target,
            amount, cardSourceSerial);
        amount = FlashHooks.ModifyPowerAmountReceived(x.B, x.R, powerId, target, amount, received,
            out receivedCount);
        return amount;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.RetireTemporaryStat</c> for a temporary Focus power: removes it,
    /// then changes Focus by -amount for a buff and +amount for a debuff (type for the current amount).
    /// </summary>
    public static void RetireTemporaryFocus(ref FlashExec x, FlashListener self)
    {
        ref FlashPower power = ref x.B.Powers[self.Index];
        ushort id = power.Id;
        int amount = power.Amount;
        int delta = FlashRegistry.Power(id).TypeForAmount(amount) == FlashPowerType.Buff ? -amount : amount;
        SetPowerAmount(ref x.B, self.Owner, id, 0);
        if (delta == 0)
            return;
        ushort focus = FlashRegistry.TryPower("FOCUS_POWER", out ushort focusId)
            ? focusId
            : throw new FlashUnsupportedException("临时集中需要集中的 Flash 定义。");
        ApplyPower(ref x, self.Owner, focus, delta, self.Owner);
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.RetireTemporaryStat</c> for a temporary Strength power: removes it,
    /// then changes Strength by -amount for a buff and +amount for a debuff (type for the current amount).
    /// </summary>
    public static void RetireTemporaryStrength(ref FlashExec x, FlashListener self)
    {
        ref FlashPower power = ref x.B.Powers[self.Index];
        ushort id = power.Id;
        int amount = power.Amount;
        int delta = FlashRegistry.Power(id).TypeForAmount(amount) == FlashPowerType.Buff ? -amount : amount;
        SetPowerAmount(ref x.B, self.Owner, id, 0);
        if (delta == 0)
            return;
        ushort strength = FlashRegistry.TryPower("STRENGTH_POWER", out ushort strengthId)
            ? strengthId
            : throw new FlashUnsupportedException("临时力量需要力量的 Flash 定义。");
        ApplyPower(ref x, self.Owner, strength, delta, self.Owner);
    }

    /// <summary>The counter update of <c>ApplyWithBeforeApplied</c> after its guards and Artifact check.</summary>
    private static int AddPowerAmount(ref FlashExec x, byte target, ushort powerId, FlashPowerDef def, int amount,
        byte applier, out int resulting, byte powerTarget = FlashLimits.NoOwner)
    {
        ref FlashBattle battle = ref x.B;
        int index;
        if (def.IsInstanced)
            index = InsertPower(ref battle, target, powerId, applier, powerTarget);
        else
        {
            index = FindPower(battle, target, powerId);
            if (index < 0)
                index = InsertPower(ref battle, target, powerId, applier, powerTarget);
        }

        ref FlashPower power = ref battle.Powers[index];
        int previous = power.Amount;
        power.Amount = Clamp((long)previous + amount);
        if (previous == 0 && power.Amount != 0 && IsPlayerSide(target) && def.UsesNativeDurationSkip)
            power.Flags |= FlashPowerFlags.SkipNextDurationTick;
        int applied = power.Amount - previous;
        resulting = power.Amount;
        if (def.MarksJustAppliedByEnemy && previous <= 0 && power.Amount > 0 && IsEnemyOwner(target))
            power.Flags |= FlashPowerFlags.WasJustAppliedByEnemy;
        // SimulatedCombatState.ApplyWithBeforeApplied records every positive Doom application by the
        // player for DeathsDoor's WasDoomAppliedThisTurn.
        if (applied > 0 && applier == FlashLimits.PlayerOwner
            && FlashRegistry.TryPower("DOOM_POWER", out ushort doomId) && powerId == doomId)
        {
            battle.SetCounter(FlashCounter.DoomAppliedThisTurn, 1);
        }
        if (power.Amount == 0)
            RemovePowerAt(ref battle, index);
        // RecordPowerAmountChange: resolved later by ResolvePowerAmountChanges at the same points as
        // the existing engine (after card effects, turn phases, enemy moves and enemy death powers).
        x.RecordPowerAmountChange(powerId, applied, applier, target);
        return applied;
    }

    /// <summary>
    /// Mirrors <c>PowerLifecycleSupport.ResolvePowerAmountChanges</c>: drains the queued changes in
    /// order; for each change it visits the current powers in listener order (Vicious draws after a
    /// Vulnerable gain whose applier is its owner), stops at a pending choice, and repeats while the
    /// handlers appended new changes.
    /// </summary>
    public static void ResolvePowerAmountChanges(ref FlashExec x)
    {
        // PowerLifecycleSupport.ResolvePowerAmountChanges drains the whole pending list before it
        // dispatches any handler. Keeping the batch in flight in FlashExec would let a nested
        // resolution (Vicious -> draw -> Hellraiser auto-play -> PlayFromPlay) consume the outer
        // batch and leave its captured count stale.
        Span<FlashPowerAmountChange> drained = stackalloc FlashPowerAmountChange[FlashLimits.PendingPowerAmountChanges];
        while (x.PendingPowerChangeCount > 0)
        {
            int count = x.PendingPowerChangeCount;
            for (int index = 0; index < count; index++)
                drained[index] = x.PendingPowerChanges[index];
            x.ClearPowerAmountChanges();
            for (int index = 0; index < count; index++)
            {
                FlashPowerAmountChange change = drained[index];
                AfterPowerAmountChangedHook hook = new()
                {
                    PowerId = change.PowerId,
                    Delta = change.Delta,
                    Applier = change.Applier,
                    Target = change.Target,
                };
                FlashHooks.DispatchPowerHook(ref x, ref hook);
                if (x.NeedsChoice)
                    return;
            }
        }
    }

    /// <summary>Mirrors <c>SimulatedCombatState.ApplyFromMonster&lt;T&gt;</c>.</summary>
    public static void ApplyPowerFromMonster(ref FlashExec x, byte target, ushort powerId, int amount, byte applier)
    {
        bool alreadyPresent = GetAmount(x.B, target, powerId) > 0;
        ApplyPower(ref x, target, powerId, amount, applier);
        FlashPowerDef def = FlashRegistry.Power(powerId);
        if (!def.UsesNativeDurationSkip && !alreadyPresent && amount > 0)
        {
            int index = FindPower(x.B, target, powerId);
            if (index >= 0 && x.B.Powers[index].Amount > 0)
                x.B.Powers[index].Flags |= FlashPowerFlags.SkipNextTickFromMonster;
        }
    }

    /// <summary>Mirrors <c>SimulatedCombatState.SetAmount&lt;T&gt;</c>.</summary>
    public static void SetPowerAmount(ref FlashBattle battle, byte owner, ushort powerId, int amount)
    {
        int index = FindPower(battle, owner, powerId);
        int current = index < 0 ? 0 : battle.Powers[index].Amount;
        if (current == amount)
            return;
        if (index < 0)
            index = InsertPower(ref battle, owner, powerId, FlashLimits.NoOwner);
        battle.Powers[index].Amount = Clamp(amount);
        if (battle.Powers[index].Amount == 0)
            RemovePowerAt(ref battle, index);
    }

    /// <summary>
    /// <c>SimulatedCombatState.SetPowerAmount</c> addressed by table row rather than (owner, id):
    /// instanced powers share both keys, and the existing engine's Sandpit decrement targets the exact
    /// instance it is visiting.
    /// </summary>
    public static void SetPowerAmountAt(ref FlashBattle battle, int index, int amount)
    {
        if ((uint)index >= (uint)battle.PowerCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Power 行越界。");
        battle.Powers[index].Amount = Clamp(amount);
        if (battle.Powers[index].Amount == 0)
            RemovePowerAt(ref battle, index);
    }

    /// <summary>Mirrors <c>SimulatedCombatState.TickDuration&lt;T&gt;</c>.</summary>
    public static void TickDuration(ref FlashBattle battle, byte owner, ushort powerId)
    {
        int index = FindPower(battle, owner, powerId);
        if (index >= 0)
        {
            ref FlashPower power = ref battle.Powers[index];
            if ((power.Flags & FlashPowerFlags.SkipNextTickFromMonster) != 0)
            {
                power.Flags &= ~FlashPowerFlags.SkipNextTickFromMonster;
                return;
            }
            if ((power.Flags & FlashPowerFlags.SkipNextDurationTick) != 0)
            {
                power.Flags &= ~FlashPowerFlags.SkipNextDurationTick;
                return;
            }
        }
        int amount = index < 0 ? 0 : battle.Powers[index].Amount;
        if (amount > 0)
            SetPowerAmount(ref battle, owner, powerId, amount - 1);
    }

    /// <summary>Mirrors <c>SimulatedCombatState.CanReceivePredictedPowers</c>.</summary>
    public static bool CanReceivePowers(in FlashBattle battle, byte owner)
    {
        if (!IsEnemyOwner(owner))
            return true;
        ref readonly FlashEnemy enemy = ref battle.Enemies[owner - FlashLimits.FirstEnemyOwner];
        return enemy.DeathPhase == FlashDeathPhase.None && (enemy.Flags & FlashEnemyFlags.InCombat) != 0;
    }

    public static bool IsPlayerSide(byte owner) => owner == FlashLimits.PlayerOwner || owner == FlashLimits.OstyOwner;

    /// <summary>Mirrors <c>SimulatedCombatState.ConsumeArtifact</c>.</summary>
    private static bool ConsumeArtifact(ref FlashBattle battle, byte target)
    {
        if (!FlashRegistry.TryPower("ARTIFACT_POWER", out ushort artifact))
            return false;
        int amount = GetAmount(battle, target, artifact);
        if (amount <= 0)
            return false;
        SetPowerAmount(ref battle, target, artifact, amount - 1);
        return true;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.ApplyTargeted</c>: the same stacking/Artifact path as
    /// <see cref="ApplyPower"/>, then writes the native <c>PowerModel._target</c> on the resulting row.
    /// A zero amount or an Artifact consumption leaves the target untouched, exactly like the native
    /// early returns.
    /// </summary>
    public static int ApplyPowerTargeted(ref FlashExec x, byte owner, byte powerTarget, ushort powerId,
        int amount, byte applier)
    {
        if (amount == 0)
            return 0;
        int applied = ApplyPower(ref x, owner, powerId, amount, applier);
        if (applied == 0 || x.NeedsChoice)
            return applied;
        int index = FindLastPower(x.B, owner, powerId);
        if (index >= 0)
            x.B.Powers[index].Target = powerTarget;
        return applied;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.IncrementSandpitTargeting</c>: the first positive Sandpit power
    /// owned by a roster creature and targeting <paramref name="target"/> gains one stack.
    /// </summary>
    public static void IncrementSandpitTargeting(ref FlashExec x, byte target)
    {
        if (!FlashRegistry.TryPower("SANDPIT_POWER", out ushort sandpit))
            return;
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref FlashPower power = ref x.B.Powers[index];
            if (power.Id != sandpit || power.Amount <= 0 || power.Target != target)
                continue;
            if (!IsEnemyOwner(power.Owner) || !InRoster(x.B, power.Owner))
                continue;
            SetPowerAmountAt(ref x.B, index, power.Amount + 1);
            return;
        }
    }

    /// <summary>Inserts a zero-amount power after the owner's last power, keeping the table grouped by owner.</summary>
    private static int InsertPower(ref FlashBattle battle, byte owner, ushort powerId, byte applier,
        byte powerTarget = FlashLimits.NoOwner)
    {
        int count = battle.PowerCount;
        if (count >= FlashLimits.Powers)
            throw new FlashCapacityException("Powers", FlashLimits.Powers);
        int position = count;
        for (int index = 0; index < count; index++)
        {
            if (battle.Powers[index].Owner > owner)
            {
                position = index;
                break;
            }
        }
        for (int index = count; index > position; index--)
            battle.Powers[index] = battle.Powers[index - 1];
        battle.Powers[position] = new FlashPower
        {
            Id = powerId,
            Owner = owner,
            Applier = applier,
            Target = powerTarget,
        };
        battle.PowerCount++;
        return position;
    }

    /// <summary>Removes a power and compacts the table, mirroring the retirement of its listener slot.</summary>
    public static void RemovePowerAt(ref FlashBattle battle, int index)
    {
        if ((uint)index >= (uint)battle.PowerCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Power 行越界。");
        ref FlashPower removed = ref battle.Powers[index];
        if (FlashRegistry.Power(removed.Id).UsesDetachedCard && removed.Aux0 != 0)
        {
            int slot = removed.Aux0 - 1;
            if ((uint)slot >= (uint)FlashLimits.DetachedCards)
                throw new FlashUnsupportedException($"Power 行 {removed.Id} 的 detached 槽位 {removed.Aux0} 越界。");
            battle.DetachedCards[slot] = default;
        }
        int count = battle.PowerCount;
        for (int next = index + 1; next < count; next++)
            battle.Powers[next - 1] = battle.Powers[next];
        battle.Powers[count - 1] = default;
        battle.PowerCount--;
    }

    /// <summary>Allocates the first zero detached-card slot, or throws the capacity boundary.</summary>
    public static int AllocateDetachedCard(ref FlashBattle battle, in FlashCard card)
    {
        for (int slot = 0; slot < FlashLimits.DetachedCards; slot++)
        {
            if (battle.DetachedCards[slot].Id != 0)
                continue;
            battle.DetachedCards[slot] = card;
            return slot;
        }
        throw new FlashCapacityException("DetachedCards", FlashLimits.DetachedCards);
    }

    /// <summary>Reads the detached card referenced by <paramref name="power"/>.</summary>
    public static FlashCard DetachedCard(in FlashBattle battle, in FlashPower power)
    {
        if (power.Aux0 == 0)
            throw new FlashUnsupportedException($"Power 行 {power.Id} 没有 detached 卡。");
        int slot = power.Aux0 - 1;
        if ((uint)slot >= (uint)FlashLimits.DetachedCards)
            throw new FlashUnsupportedException($"Power 行 {power.Id} 的 detached 槽位 {power.Aux0} 越界。");
        return battle.DetachedCards[slot];
    }

    /// <summary>Mirrors <c>SimulatedCombatState.RemovePowersAfterDeath</c>: drops every power of the owner.</summary>
    public static void RemovePowersOf(ref FlashBattle battle, byte owner)
        => RemovePowersOfInternal(ref battle, owner, keepPendingAmountStates: false);

    /// <summary>
    /// <c>RemovePowersAfterDeath</c> for the player. A row whose native mirror created a
    /// <c>PowerAmountPredictionState</c> during this action is not removed: the boundary
    /// synchronization writes the stored amount back into the model, so the native state keeps the
    /// power (GigantificationPower's BeforeAttack reads it for every attack).
    /// </summary>
    private static void RemovePowersOfInternal(ref FlashBattle battle, byte owner, bool keepPendingAmountStates)
    {
        for (int index = battle.PowerCount - 1; index >= 0; index--)
        {
            if (battle.Powers[index].Owner != owner)
                continue;
            if (keepPendingAmountStates
                && (battle.Powers[index].Flags & FlashPowerFlags.PendingAmountState) != 0)
            {
                continue;
            }
            RemovePowerAt(ref battle, index);
        }
    }

    /// <summary>
    /// Drops the transient <see cref="FlashPowerFlags.PendingAmountState"/> markers at a stable
    /// boundary; the native entry is consumed by <c>SynchronizePowerAmountPredictionStates</c> there.
    /// </summary>
    public static void ClearPendingPowerAmountStates(ref FlashBattle battle)
    {
        for (int index = 0; index < battle.PowerCount; index++)
            battle.Powers[index].Flags &= ~FlashPowerFlags.PendingAmountState;
    }
    private static bool HasRelic(in FlashBattle battle, string nativeId)
    {
        if (!FlashRegistry.TryRelic(nativeId, out ushort id))
            return false;
        for (int index = 0; index < battle.RelicCount; index++)
        {
            ref readonly FlashRelic relic = ref battle.Relics[index];
            if (relic.Id == id && (relic.Flags & FlashRelicFlags.Melted) == 0)
                return true;
        }
        return false;
    }

}
