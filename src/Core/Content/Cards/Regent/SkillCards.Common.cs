using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

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

/// <summary><c>CloakOfStars</c> (1 star cost): the general block.</summary>
[FlashContent("CLOAK_OF_STARS")]
public sealed class CloakOfStars : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

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

/// <summary>
/// <c>CosmicIndifference</c>: block, then choose one discard card and put it on top of the draw pile
/// (<c>CardChoiceSupport.GetSpec</c> CosmicIndifference → <c>Apply</c> MoveToDrawTop).
/// </summary>
[FlashContent("COSMIC_INDIFFERENCE")]
public sealed class CosmicIndifference : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToDrawTop, FlashPile.Discard, options, 1,
                selected, out int chosen))
        {
            return;
        }
        FlashKernel.MoveSelected(ref x.B, FlashPile.Discard, selected[..chosen], FlashPile.Draw,
            FlashPilePosition.Top);
    }
}

/// <summary>
/// <c>Begone</c>: choose one hand card and transform it into the canonical Minion Strike, upgraded
/// when Begone is upgraded.
/// </summary>
[FlashContent("BEGONE")]
public sealed class Begone : FlashCardDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("MINION_STRIKE", 0)];

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Transform, FlashPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        Charge.TransformSelected(ref x, FlashPile.Hand, selected[..chosen], "MINION_STRIKE");
    }
}

/// <summary>
/// <c>RefineBlade</c>: CorePowerSupport forges the Forge variable, then queues EnergyNextTurnPower
/// (<c>combat.AddEnergyNextTurn</c>).
/// </summary>
[FlashContent("REFINE_BLADE")]
public sealed class RefineBlade : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge", "Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 0)))
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>GatherLight</c>: the inferred recipe gains Block, then CardEffectSpecRegistry's
/// <c>GatherLight</c> gains Stars.
/// </summary>
[FlashContent("GATHER_LIGHT")]
public sealed class GatherLight : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Stars"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 1));
    }
}

/// <summary>
/// <c>Glow</c>: the inferred recipe draws the Cards variable, then CardEffectSpecRegistry's
/// <c>Glow</c> entry applies DrawCardsNextTurnPower (Cards) and the resource switch gains Stars, in
/// that order.
/// </summary>
[FlashContent("GLOW")]
public sealed class Glow : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars", "Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["DRAW_CARDS_NEXT_TURN_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 1), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.DrawCardsNextTurnPower>(), (int)FlashCardEffects.Var(x, 1),
            FlashLimits.PlayerOwner);
        FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 0));
    }
}

/// <summary>
/// <c>HiddenCache</c>: CardOnPlaySupport gains Stars, then applies StarNextTurnPower with the
/// "StarNextTurnPower" variable.
/// </summary>
[FlashContent("HIDDEN_CACHE")]
public sealed class HiddenCache : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars", "StarNextTurnPower"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 0)))
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StarNextTurnPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}
