namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Regent;

// Regent batch four: cards whose existing-engine path is CardEffectSpecRegistry or the bespoke
// CardDrawCardMirrors.

/// <summary>
/// <c>GuidingStar</c> (1 star cost): the inferred attack, then CardEffectSpecRegistry's
/// <c>GuidingStar</c> entry applies DrawCardsNextTurnPower with the Cards variable.
/// </summary>
[FlashContent("GUIDING_STAR")]
public sealed class GuidingStar : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["DRAW_CARDS_NEXT_TURN_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.DrawCardsNextTurnPower>(), (int)FlashCardEffects.Var(x, 1),
            FlashLimits.PlayerOwner);
    }
}

/// <summary><c>Patter</c>: the inferred block, then CardEffectSpecRegistry's VigorPower application.</summary>
[FlashContent("PATTER")]
public sealed class Patter : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "VigorPower"];

    public override IReadOnlyList<string> AppliedPowers => ["VIGOR_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.VigorPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>CrushUnder</c>: attack every enemy, then CardEffectSpecRegistry applies CrushUnderPower (a
/// temporary Strength loss) to every hittable enemy in roster order.
/// </summary>
[FlashContent("CRUSH_UNDER")]
public sealed class CrushUnder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StrengthLoss"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        ushort power = FlashRegistry.PowerId<Powers.CrushUnderPower>();
        int amount = (int)FlashCardEffects.Var(x, 1);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            FlashKernel.ApplyTemporaryStrength(ref x, enemy, power, amount, FlashLimits.PlayerOwner, -1);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>BlackHole</c>: CardPowerOnPlaySupport applies BlackHolePower with the "BlackHolePower" variable;
/// the power damages every enemy after a star gain and after a manual card series that spent stars.
/// </summary>
[FlashContent("BLACK_HOLE")]
public sealed class BlackHole : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["BlackHolePower"];

    public override IReadOnlyList<string> AppliedPowers => ["BLACK_HOLE_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.BlackHolePower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>SpoilsOfBattle</c>: CardDrawCardMirrors forges the Forge variable, then draws the Cards variable.
/// </summary>
[FlashContent("SPOILS_OF_BATTLE")]
public sealed class SpoilsOfBattle : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 0)))
            return;
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

/// <summary>
/// <c>Glitterstream</c>: the inferred block, then CardEffectSpecRegistry computes the "BlockNextTurn"
/// variable through <c>HookMirrors.ModifyBlock</c> and applies BlockNextTurnPower with the truncated
/// amount.
/// </summary>
[FlashContent("GLITTERSTREAM")]
public sealed class Glitterstream : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "BlockNextTurn"];

    public override IReadOnlyList<string> AppliedPowers => ["BLOCK_NEXT_TURN_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        decimal nextTurn = FlashHooks.ModifyBlock(ref x, FlashLimits.PlayerOwner,
            FlashCardEffects.Var(x, 1), FlashValueProps.Move, hasCardSource: true, x.Card, out _);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.BlockNextTurnPower>(),
            (int)nextTurn, FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>CrashLanding</c> (Rare, attack every enemy): CardEffectSpecRegistry then adds one Debris to the
/// hand for each empty hand slot (<c>GetMaxHandSize - Hand.Count</c>).
/// </summary>
[FlashContent("CRASH_LANDING")]
public sealed class CrashLanding : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [(DebrisId, 0)];

    private const string DebrisId = "DEBRIS";

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = x.B.MaxHandSize - x.B.HandCount;
        if (count <= 0)
            return;
        if (!FlashRegistry.TryCard(DebrisId, out ushort debris))
            throw new FlashUnsupportedException("坠毁需要 DEBRIS 的 Flash 定义。");
        for (int index = 0; index < count; index++)
        {
            if (!FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, debris), FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>Radiate</c>: CalculatedHits = CalculationBase + CalculationExtra × stars gained this turn
/// (<c>CalculatedVarSpecRegistry.Radiate</c> → <c>GetStarsGainedThisTurn</c>), used as the all-enemy
/// attack's hit count.
/// </summary>
[FlashContent("RADIATE")]
public sealed class Radiate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref FlashExec x)
    {
        int hits = (int)(FlashCardEffects.Var(x, 1)
            + FlashCardEffects.Var(x, 2) * x.B.Counter(FlashCounter.StarsGainedThisTurn));
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: hits);
    }
}
