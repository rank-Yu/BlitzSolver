using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

// Regent cards whose effect depends on a per-card or per-instance ledger. Existing-engine paths are
// named per card; the decompiled models only confirm constants.

/// <summary>
/// Native <c>Monologue</c>: apply an instanced MonologuePower and copy the card's Power variable into
/// that instance. Existing engine: <c>CardOnPlaySupport.Batch043</c>'s Monologue case.
/// </summary>
[FlashContent("MONOLOGUE")]
public sealed class Monologue : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Power"];

    public override IReadOnlyList<string> AppliedPowers => ["MONOLOGUE_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryPower("MONOLOGUE_POWER", out ushort power))
            throw new FlashUnsupportedException("Monologue 需要 MONOLOGUE_POWER 定义。");
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, 1, FlashLimits.PlayerOwner,
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int row = FlashKernel.FindLastPower(x.B, FlashLimits.PlayerOwner, power);
        if (row < 0)
            return;
        // Monologue.OnPlay sets DynamicVars.Strength after PowerCmd.Apply returns.
        x.B.Powers[row].Aux0 = (int)FlashCardEffects.Var(x, 0);
    }
}

/// <summary>
/// Native <c>SwordSage</c>: apply SwordSagePower. The power's per-blade replay normalization is in
/// <see cref="FlashKernel.NormalizeCardAfflictions"/>; existing engine
/// <c>CardPowerOnPlaySupport.SToZ</c>'s SwordSage case.
/// </summary>
[FlashContent("SWORD_SAGE")]
public sealed class SwordSage : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["SwordSagePower"];

    public override IReadOnlyList<string> AppliedPowers => ["SWORD_SAGE_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryPower("SWORD_SAGE_POWER", out ushort power))
            throw new FlashUnsupportedException("SwordSage 需要 SWORD_SAGE_POWER 定义。");
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// Native <c>SummonForth</c>: move every Sovereign Blade outside the hand to the hand bottom, then
/// forge. Existing engine: <c>CardPileOnPlaySupport.MoveSovereignBladesToHand</c> followed by
/// <c>CardOnPlaySupport</c>'s SummonForth forge.
/// </summary>
[FlashContent("SUMMON_FORTH")]
public sealed class SummonForth : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryCard(FlashKernel.SovereignBladeId, out ushort blade))
            throw new FlashUnsupportedException("SummonForth 需要 SOVEREIGN_BLADE 定义。");
        FlashPile[] piles = [FlashPile.Draw, FlashPile.Discard, FlashPile.Exhaust, FlashPile.Play];
        foreach (FlashPile pile in piles)
        {
            int index = 0;
            while (index < FlashPiles.Count(x.B, pile))
            {
                if (x.B.Cards[FlashPiles.Start(x.B, pile) + index].Id != blade)
                {
                    index++;
                    continue;
                }
                FlashCard card = FlashPiles.RemoveAt(ref x.B, pile, index);
                FlashPiles.Append(ref x.B, FlashPile.Hand, card);
            }
        }
        if (x.NeedsChoice)
            return;
        FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 0));
    }
}

/// <summary>
/// Native <c>Orbit</c>: add an instanced OrbitPower with the Energy variable as its per-trigger
/// amount. Existing engine: <c>CardPowerOnPlaySupport.AddPowerInstance&lt;OrbitPower&gt;</c>.
/// </summary>
[FlashContent("ORBIT")]
public sealed class Orbit : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override IReadOnlyList<string> AppliedPowers => ["ORBIT_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryPower("ORBIT_POWER", out ushort power))
            throw new FlashUnsupportedException("Orbit 需要 ORBIT_POWER 定义。");
        // SimulatedCombatState.AddPowerInstance sets _target = owner (unlike Apply<T>).
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner, FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// Native <c>PaleBlueDot</c>: apply <c>Cards</c> stacks of PaleBlueDotPower. A first application
/// initializes the per-turn latch to false; stacking keeps it. Existing engine:
/// <c>CardPowerOnPlaySupport</c>'s PaleBlueDot case and <c>InitializePaleBlueDot</c>.
/// </summary>
[FlashContent("PALE_BLUE_DOT")]
public sealed class PaleBlueDot : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["PALE_BLUE_DOT_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryPower("PALE_BLUE_DOT_POWER", out ushort power))
            throw new FlashUnsupportedException("PaleBlueDot 需要 PALE_BLUE_DOT_POWER 定义。");
        bool alreadyHad = FlashKernel.GetAmount(x.B, FlashLimits.PlayerOwner, power) > 0;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice || alreadyHad)
            return;
        int row = FlashKernel.FindLastPower(x.B, FlashLimits.PlayerOwner, power);
        if (row >= 0)
            x.B.Powers[row].Aux0 = 0;
    }
}
