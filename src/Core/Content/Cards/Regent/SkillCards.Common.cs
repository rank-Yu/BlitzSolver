using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary><c>Patter</c>: the inferred block, then CardEffectSpecRegistry's VigorPower application.</summary>
[BlitzContent("PATTER")]
public sealed class Patter : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "VigorPower"];

    public override IReadOnlyList<string> AppliedPowers => ["VIGOR_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.VigorPower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>SpoilsOfBattle</c>: CardDrawCardMirrors forges the Forge variable, then draws the Cards variable.
/// </summary>
[BlitzContent("SPOILS_OF_BATTLE")]
public sealed class SpoilsOfBattle : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!BlitzKernel.Forge(ref x, (int)BlitzCardEffects.Var(x, 0)))
            return;
        BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

/// <summary>
/// <c>Glitterstream</c>: the inferred block, then CardEffectSpecRegistry computes the "BlockNextTurn"
/// variable through <c>HookMirrors.ModifyBlock</c> and applies BlockNextTurnPower with the truncated
/// amount.
/// </summary>
[BlitzContent("GLITTERSTREAM")]
public sealed class Glitterstream : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "BlockNextTurn"];

    public override IReadOnlyList<string> AppliedPowers => ["BLOCK_NEXT_TURN_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        decimal nextTurn = BlitzHooks.ModifyBlock(ref x, BlitzLimits.PlayerOwner,
            BlitzCardEffects.Var(x, 1), BlitzValueProps.Move, hasCardSource: true, x.Card, out _);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.BlockNextTurnPower>(),
            (int)nextTurn, BlitzLimits.PlayerOwner);
    }
}

/// <summary><c>CloakOfStars</c> (1 star cost): the general block.</summary>
[BlitzContent("CLOAK_OF_STARS")]
public sealed class CloakOfStars : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);
}

/// <summary><c>KnowThyPlace</c> (Exhaust): Weak then Vulnerable on the chosen enemy.</summary>
[BlitzContent("KNOW_THY_PLACE")]
public sealed class KnowThyPlace : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["WeakPower", "VulnerablePower"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.WeakPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.VulnerablePower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>CosmicIndifference</c>: block, then choose one discard card and put it on top of the draw pile
/// (<c>CardChoiceSupport.GetSpec</c> CosmicIndifference → <c>Apply</c> MoveToDrawTop).
/// </summary>
[BlitzContent("COSMIC_INDIFFERENCE")]
public sealed class CosmicIndifference : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.MoveToDrawTop, BlitzPile.Discard, options, 1,
                selected, out int chosen))
        {
            return;
        }
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Discard, selected[..chosen], BlitzPile.Draw,
            BlitzPilePosition.Top);
    }
}

/// <summary>
/// <c>Begone</c>: choose one hand card and transform it into the canonical Minion Strike, upgraded
/// when Begone is upgraded.
/// </summary>
[BlitzContent("BEGONE")]
public sealed class Begone : BlitzCardDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("MINION_STRIKE", 0)];

    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Transform, BlitzPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        Charge.TransformSelected(ref x, BlitzPile.Hand, selected[..chosen], "MINION_STRIKE");
    }
}

/// <summary>
/// <c>RefineBlade</c>: CorePowerSupport forges the Forge variable, then queues EnergyNextTurnPower
/// (<c>combat.AddEnergyNextTurn</c>).
/// </summary>
[BlitzContent("REFINE_BLADE")]
public sealed class RefineBlade : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge", "Energy"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!BlitzKernel.Forge(ref x, (int)BlitzCardEffects.Var(x, 0)))
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>GatherLight</c>: the inferred recipe gains Block, then CardEffectSpecRegistry's
/// <c>GatherLight</c> gains Stars.
/// </summary>
[BlitzContent("GATHER_LIGHT")]
public sealed class GatherLight : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Stars"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.GainStars(ref x, (int)BlitzCardEffects.Var(x, 1));
    }
}

/// <summary>
/// <c>Glow</c>: the inferred recipe draws the Cards variable, then CardEffectSpecRegistry's
/// <c>Glow</c> entry applies DrawCardsNextTurnPower (Cards) and the resource switch gains Stars, in
/// that order.
/// </summary>
[BlitzContent("GLOW")]
public sealed class Glow : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars", "Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["DRAW_CARDS_NEXT_TURN_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 1), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.DrawCardsNextTurnPower>(), (int)BlitzCardEffects.Var(x, 1),
            BlitzLimits.PlayerOwner);
        BlitzKernel.GainStars(ref x, (int)BlitzCardEffects.Var(x, 0));
    }
}

/// <summary>
/// <c>HiddenCache</c>: CardOnPlaySupport gains Stars, then applies StarNextTurnPower with the
/// "StarNextTurnPower" variable.
/// </summary>
[BlitzContent("HIDDEN_CACHE")]
public sealed class HiddenCache : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars", "StarNextTurnPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!BlitzKernel.GainStars(ref x, (int)BlitzCardEffects.Var(x, 0)))
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StarNextTurnPower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}
