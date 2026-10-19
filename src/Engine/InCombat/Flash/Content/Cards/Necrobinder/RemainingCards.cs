using CombatSolver.Engine.InCombat.Flash.Content.Powers;

namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Necrobinder;

// The remaining Necrobinder cards. Existing engine paths: TurnStartPowerSupport (SentryMode/
// CallOfTheVoid), CardPowerOnPlaySupport (Pagestorm), BespokeCardMirrors.MiseryOnPlay, and the
// generic attack/block recipes for SweepingGaze.

/// <summary>
/// <c>SweepingGaze</c> token: 0-cost Ethereal/Exhaust Osty attack with random target. Existing engine:
/// <c>GeneralCardMirrors.GeneralAttackOnPlay</c> (the Osty tag selects the pet as dealer and the rule
/// table's RandomEnemy target type selects a random opponent).
/// </summary>
[FlashContent("SWEEPING_GAZE")]
public sealed class SweepingGaze : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage"];

    public override void OnPlay(ref FlashExec x)
        => NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
}

[FlashContent("SENTRY_MODE")]
public sealed class SentryMode : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SentryModePower"];

    public override IReadOnlyList<string> AppliedPowers => ["SENTRY_MODE_POWER"];

    // CardPowerOnPlaySupport.SToZ: combat.Apply<SentryModePower>(owner, power var, owner).
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<SentryModePower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("CALL_OF_THE_VOID")]
public sealed class CallOfTheVoid : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["CALL_OF_THE_VOID_POWER"];

    // CardPowerOnPlaySupport: combat.Apply<CallOfTheVoidPower>(owner, card.DynamicVars.Cards.IntValue, owner).
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<CallOfTheVoidPower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("PAGESTORM")]
public sealed class Pagestorm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["PAGESTORM_POWER"];

    // CardPowerOnPlaySupport: combat.Apply<PagestormPower>(owner, card.DynamicVars.Cards.IntValue, owner).
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<PagestormPower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>Misery</c>: deal the attack, then copy every debuff on the selected target to every other
/// hittable enemy. Existing engine: <c>BespokeCardMirrors.MiseryOnPlay</c>. The clone carries the
/// original amount and applier. Temporary powers are the existing engine's <c>ITemporaryPower</c>
/// merge path (temporary → internal power amount); Flash has no per-instance dynamic-variable copy,
/// so a target carrying one is an explicit unsupported boundary rather than a half-clone.
/// </summary>
[FlashContent("MISERY")]
public sealed class Misery : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        Span<ushort> ids = stackalloc ushort[FlashLimits.Powers];
        Span<int> amounts = stackalloc int[FlashLimits.Powers];
        Span<byte> appliers = stackalloc byte[FlashLimits.Powers];
        Span<byte> targets = stackalloc byte[FlashLimits.Powers];
        int count = 0;
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly FlashPower power = ref x.B.Powers[index];
            if (power.Owner != x.Target || power.Amount == 0)
                continue;
            FlashPowerDef def = FlashRegistry.Power(power.Id);
            if (def.TypeForAmount(power.Amount) != FlashPowerType.Debuff)
                continue;
            ids[count] = power.Id;
            amounts[count] = power.Amount;
            appliers[count] = power.Applier;
            targets[count] = power.Target;
            count++;
        }
        // BespokeCardMirrors.MiseryOnPlay: a temporary power's amount is also merged into the
        // internally applied stat when that stat is itself among the cloned debuffs. The temporary
        // power row is still cloned (the native ApplyClonedPower skips BeforeApplied), exactly as the
        // existing engine does.
        for (int index = 0; index < count; index++)
        {
            FlashPowerDef def = FlashRegistry.Power(ids[index]);
            if (!def.IsTemporaryPower || def.InternallyAppliedPowerId is not { } internalId)
                continue;
            if (!FlashRegistry.TryPower(internalId, out ushort internalPower))
                continue;
            for (int targetIndex = 0; targetIndex < count; targetIndex++)
            {
                if (ids[targetIndex] == internalPower)
                {
                    amounts[targetIndex] = checked(amounts[targetIndex] + amounts[index]);
                    break;
                }
            }
        }
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        byte original = x.Target;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (enemy == original || !FlashKernel.IsHittable(x.B, enemy))
                continue;
            for (int item = 0; item < count; item++)
            {
                FlashKernel.ApplyPower(ref x, enemy, ids[item], amounts[item], appliers[item], targets[item]);
                if (x.NeedsChoice)
                    return;
                FlashKernel.ResolvePowerAmountChanges(ref x);
                if (x.NeedsChoice)
                    return;
            }
        }
    }
}
