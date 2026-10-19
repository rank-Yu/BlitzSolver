namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Regent;

// Additional Regent cards whose existing-engine path is a single compensation case.
// - KnowThyPlace / GammaBlast: CorePowerSupport's Weak-then-Vulnerable case.
// - Resonance: CorePowerSupport's StrengthPower case (owner gains, every hittable enemy loses 1).
// - Hegemony: CardEffectSpecRegistry's EnergyNextTurnPower entry.
// - Terraforming / NeutronAegis: CardPowerOnPlaySupport's late Vigor/Plating applications.
// - Royalties: CardPowerOnPlaySupport's RoyaltiesPower plus the long-term resource record.
// - PillarOfCreation: CardPowerOnPlaySupport's PillarOfCreationPower application.

/// <summary><c>KnowThyPlace</c> (Exhaust): Weak then Vulnerable on the chosen enemy.</summary>
[FlashContent("KNOW_THY_PLACE")]
public sealed class KnowThyPlace : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["WeakPower", "VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.WeakPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.VulnerablePower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary><c>GammaBlast</c> (3 star cost): attack, then Weak and Vulnerable after the enemy deaths.</summary>
[FlashContent("GAMMA_BLAST")]
public sealed class GammaBlast : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.WeakPower>(),
            (int)FlashCardEffects.Var(x, 2), FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.VulnerablePower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Resonance</c> (2 star cost): CorePowerSupport gains the Strength variable for the owner, then
/// applies -1 Strength to every hittable enemy in roster order.
/// </summary>
[FlashContent("RESONANCE")]
public sealed class Resonance : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref FlashExec x)
    {
        ushort strength = FlashRegistry.PowerId<Powers.StrengthPower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, strength, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            FlashKernel.ApplyPower(ref x, enemy, strength, -1, FlashLimits.PlayerOwner);
        }
    }
}

/// <summary><c>Hegemony</c>: the inferred attack, then EnergyNextTurnPower with the Energy variable.</summary>
[FlashContent("HEGEMONY")]
public sealed class Hegemony : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Energy"];

    public override IReadOnlyList<string> AppliedPowers => ["ENERGY_NEXT_TURN_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary><c>Terraforming</c>: CardPowerOnPlaySupport's late pass applies VigorPower.</summary>
[FlashContent("TERRAFORMING")]
public sealed class Terraforming : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["VigorPower"];

    public override IReadOnlyList<string> AppliedPowers => ["VIGOR_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.VigorPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>NeutronAegis</c>: CardPowerOnPlaySupport applies PlatingPower.</summary>
[FlashContent("NEUTRON_AEGIS")]
public sealed class NeutronAegis : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    public override IReadOnlyList<string> AppliedPowers => ["PLATING_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.PlatingPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>Royalties</c>: CardPowerOnPlaySupport applies RoyaltiesPower with the Gold variable and records
/// the same amount as long-term resource value (<c>combat.RecordLongTermResource</c>).
/// </summary>
[FlashContent("ROYALTIES")]
public sealed class Royalties : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Gold"];

    public override IReadOnlyList<string> AppliedPowers => ["ROYALTIES_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        int gold = (int)FlashCardEffects.Var(x, 0);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RoyaltiesPower>(),
            gold, FlashLimits.PlayerOwner);
        x.B.AddCounter(FlashCounter.LongTermResourceValue, gold);
        FlashGrowthRewards.Add(ref x.B, FlashGrowthSource.Royalties);
    }
}

/// <summary>
/// <c>PillarOfCreation</c>: CardPowerOnPlaySupport applies PillarOfCreationPower with the Block
/// variable; the power grants Unpowered block for every card generated for its owner.
/// </summary>
[FlashContent("PILLAR_OF_CREATION")]
public sealed class PillarOfCreation : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<string> AppliedPowers => ["PILLAR_OF_CREATION_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.PillarOfCreationPower>(), (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
}

/// <summary><c>MonarchsGaze</c>: CardPowerOnPlaySupport applies MonarchsGazePower with "StrengthLoss".</summary>
[FlashContent("MONARCHS_GAZE")]
public sealed class MonarchsGaze : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    public override IReadOnlyList<string> AppliedPowers => ["MONARCHS_GAZE_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.MonarchsGazePower>(), (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
}
