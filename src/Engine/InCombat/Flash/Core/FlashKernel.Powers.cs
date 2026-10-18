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
    public static int ApplyPower(ref FlashExec x, byte target, ushort powerId, int amount, byte applier)
    {
        ref FlashBattle battle = ref x.B;
        if (amount == 0 || !CanReceivePowers(battle, target))
            return 0;
        FlashPowerDef def = FlashRegistry.Power(powerId);
        // ModifyPowerAmountForRelics: Unsettling Lamp, Snecko Skull and Ruined Helmet are not
        // supported yet; admission rejects roots holding them.
        if (def.TypeForAmount(amount) == FlashPowerType.Debuff && ConsumeArtifact(ref battle, target))
            return 0;
        return AddPowerAmount(ref battle, target, powerId, def, amount, applier, out _);
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
        if (amount == 0 || !CanReceivePowers(x.B, target))
            return 0;
        FlashPowerDef def = FlashRegistry.Power(temporaryId);
        if (def.TypeForAmount(amount) == FlashPowerType.Debuff && ConsumeArtifact(ref x.B, target))
            return 0;
        ushort strength = FlashRegistry.TryPower("STRENGTH_POWER", out ushort id)
            ? id
            : throw new FlashUnsupportedException("临时力量需要力量的 Flash 定义。");
        if (def.IsInstanced || GetAmount(x.B, target, temporaryId) == 0)
            ApplyPower(ref x, target, strength, sign * amount, applier);
        int applied = AddPowerAmount(ref x.B, target, temporaryId, def, amount, applier, out int resulting);
        if (amount != resulting)
            ApplyPower(ref x, target, strength, sign * amount, applier);
        return applied;
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
    private static int AddPowerAmount(ref FlashBattle battle, byte target, ushort powerId, FlashPowerDef def, int amount,
        byte applier, out int resulting)
    {
        int index;
        if (def.IsInstanced)
            index = InsertPower(ref battle, target, powerId, applier);
        else
        {
            index = FindPower(battle, target, powerId);
            if (index < 0)
                index = InsertPower(ref battle, target, powerId, applier);
        }

        ref FlashPower power = ref battle.Powers[index];
        int previous = power.Amount;
        power.Amount = Clamp((long)previous + amount);
        if (previous == 0 && power.Amount != 0 && IsPlayerSide(target) && def.UsesNativeDurationSkip)
            power.Flags |= FlashPowerFlags.SkipNextDurationTick;
        int applied = power.Amount - previous;
        resulting = power.Amount;
        if (power.Amount == 0)
            RemovePowerAt(ref battle, index);
        // RecordPowerAmountChange only reacts for Shroud, Sleight of Flesh and Vicious, which are
        // not supported yet.
        return applied;
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

    /// <summary>Inserts a zero-amount power after the owner's last power, keeping the table grouped by owner.</summary>
    private static int InsertPower(ref FlashBattle battle, byte owner, ushort powerId, byte applier)
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
            Target = FlashLimits.NoOwner,
        };
        battle.PowerCount++;
        return position;
    }

    /// <summary>Removes a power and compacts the table, mirroring the retirement of its listener slot.</summary>
    public static void RemovePowerAt(ref FlashBattle battle, int index)
    {
        int count = battle.PowerCount;
        for (int next = index + 1; next < count; next++)
            battle.Powers[next - 1] = battle.Powers[next];
        battle.Powers[count - 1] = default;
        battle.PowerCount--;
    }

    /// <summary>Mirrors <c>SimulatedCombatState.RemovePowersAfterDeath</c>: drops every power of the owner.</summary>
    public static void RemovePowersOf(ref FlashBattle battle, byte owner)
    {
        for (int index = battle.PowerCount - 1; index >= 0; index--)
        {
            if (battle.Powers[index].Owner == owner)
                RemovePowerAt(ref battle, index);
        }
    }
}
