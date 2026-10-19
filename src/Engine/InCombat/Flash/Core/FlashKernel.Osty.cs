namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Osty, the Necrobinder pet. Semantics follow <c>SimulatedCombatState.CardLifecycle</c>'s
/// <c>SummonOsty</c>/<c>HealOsty</c>/<c>GetOstyMaxHp</c> and <c>SimulatedCombatState.Doom</c>'s
/// <c>DoomKill</c>, which are the existing engine's authoritative implementations.
/// </summary>
public static partial class FlashKernel
{
    /// <summary>Mirrors <c>SimulatedCombatState.SummonOsty</c> (<c>OstyCmd.Summon</c>).</summary>
    public static void SummonOsty(ref FlashExec x, int amount)
    {
        if (amount <= 0)
            return;
        if (!FlashRegistry.TryPower("DIE_FOR_YOU_POWER", out ushort dieForYou))
            throw new FlashUnsupportedException("Osty 需要 DieForYouPower 的 Flash 定义。");
        bool created = (x.B.Flags & FlashBattleFlags.OstyPresent) == 0;
        if (created)
        {
            // CreatePredictedMonster consumes one CombatId before any HP is assigned. The native model
            // starts at 1/1 HP; the existing engine overwrites both before anything can observe them
            // (the DieForYouPower application has no HP reader), so Flash initializes the target HP
            // directly instead of hard-coding the model's initial value.
            x.B.NextCombatId = unchecked(x.B.NextCombatId + 1);
            x.B.Flags |= FlashBattleFlags.OstyPresent;
            x.B.Osty = new FlashCreature
            {
                MaxHp = amount,
                CurrentHp = amount,
                Flags = FlashCreatureFlags.Present,
            };
            // OstyCmd.Summon applies DieForYouPower 1 with no applier for a fresh pet, after AddPet and
            // before SetMaxHp(amount).
            ApplyPower(ref x, FlashLimits.OstyOwner, dieForYou, 1, FlashLimits.NoOwner);
            if (x.NeedsChoice)
                return;
            return;
        }
        ref FlashCreature osty = ref x.B.Osty;
        if (osty.IsAlive)
        {
            int newMax = Math.Clamp((int)Math.Min((long)osty.MaxHp + amount, FlashLimits.ValueClamp), 1,
                FlashLimits.ValueClamp);
            osty.MaxHp = newMax;
            osty.CurrentHp = Math.Min(newMax, (int)Math.Min((long)osty.CurrentHp + amount, FlashLimits.ValueClamp));
        }
        else
        {
            // A dead pet keeps its object; the existing engine revives it and resets both HP values.
            osty.MaxHp = Math.Clamp(amount, 1, FlashLimits.ValueClamp);
            osty.CurrentHp = amount;
        }
    }

    /// <summary>Mirrors <c>SimulatedCombatState.HealOsty</c>.</summary>
    public static void HealOsty(ref FlashExec x, int amount)
    {
        if ((x.B.Flags & FlashBattleFlags.OstyPresent) == 0)
            throw new FlashUnsupportedException("治疗 Osty 时它不存在。");
        ref FlashCreature osty = ref x.B.Osty;
        osty.CurrentHp = Math.Min(osty.MaxHp,
            (int)Math.Min((long)osty.CurrentHp + Math.Max(0, amount), FlashLimits.ValueClamp));
    }

    /// <summary>Mirrors <c>SimulatedCombatState.GetOstyMaxHp</c> for the single-player Flash state.</summary>
    public static int OstyMaxHp(in FlashBattle battle)
        => (battle.Flags & FlashBattleFlags.OstyPresent) == 0 ? 0 : battle.Osty.MaxHp;

    /// <summary>Mirrors <c>SimulatedCombatState.DoomKill</c>: kill the doomed creatures one at a time in
    /// order, then heal the player for each fatal kill through a supported <c>BookRepairKnife</c>.</summary>
    public static void DoomKill(ref FlashExec x, scoped ReadOnlySpan<byte> creatures)
    {
        if (creatures.IsEmpty)
            return;
        int fatal = 0;
        foreach (byte creature in creatures)
        {
            if (creature != FlashLimits.PlayerOwner && TargetDeathTriggersFatal(x.B, creature))
                fatal++;
        }
        Span<byte> one = stackalloc byte[1];
        foreach (byte creature in creatures)
        {
            one[0] = creature;
            Kill(ref x, one, force: false);
            if (x.NeedsChoice)
                return;
        }
        if (fatal <= 0 || !FlashRegistry.TryRelic("BOOK_REPAIR_KNIFE", out ushort knife))
            return;
        decimal heal = x.R.RelicVar(knife, 0) * fatal;
        for (int index = 0; index < x.B.RelicCount; index++)
        {
            ref readonly FlashRelic relic = ref x.B.Relics[index];
            if (relic.Id != knife || (relic.Flags & FlashRelicFlags.Melted) != 0)
                continue;
            Heal(ref x, FlashLimits.PlayerOwner, heal);
            if (x.NeedsChoice)
                return;
        }
    }
}

