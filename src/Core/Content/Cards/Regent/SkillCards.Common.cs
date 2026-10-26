using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary><c>Patter</c>: the inferred block, then CardEffectSpecRegistry's VigorPower application.</summary>
[ContentId("PATTER")]
public sealed class Patter : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "VigorPower"];

    public override IReadOnlyList<string> AppliedPowers => ["VIGOR_POWER"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.VigorPower>(),
            (int)CardEffects.Var(x, 1), Limits.PlayerOwner);
    }
}

/// <summary>
/// <c>SpoilsOfBattle</c>: CardDrawCardMirrors forges the Forge variable, then draws the Cards variable.
/// </summary>
[ContentId("SPOILS_OF_BATTLE")]
public sealed class SpoilsOfBattle : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge", "Cards"];

    public override void OnPlay(ref Exec x)
    {
        if (!Kernel.Forge(ref x, (int)CardEffects.Var(x, 0)))
            return;
        Kernel.Draw(ref x, (int)CardEffects.Var(x, 1), fromHandDraw: false);
    }
}

/// <summary>
/// <c>Glitterstream</c>: the inferred block, then CardEffectSpecRegistry computes the "BlockNextTurn"
/// variable through <c>HookMirrors.ModifyBlock</c> and applies BlockNextTurnPower with the truncated
/// amount.
/// </summary>
[ContentId("GLITTERSTREAM")]
public sealed class Glitterstream : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "BlockNextTurn"];

    public override IReadOnlyList<string> AppliedPowers => ["BLOCK_NEXT_TURN_POWER"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        decimal nextTurn = HookDispatch.ModifyBlock(ref x, Limits.PlayerOwner,
            CardEffects.Var(x, 1), ValueProps.Move, hasCardSource: true, x.Card, out _);
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.BlockNextTurnPower>(),
            (int)nextTurn, Limits.PlayerOwner);
    }
}

/// <summary><c>CloakOfStars</c> (1 star cost): the general block.</summary>
[ContentId("CLOAK_OF_STARS")]
public sealed class CloakOfStars : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralBlock(ref x, 0);
}

/// <summary><c>KnowThyPlace</c> (Exhaust): Weak then Vulnerable on the chosen enemy.</summary>
[ContentId("KNOW_THY_PLACE")]
public sealed class KnowThyPlace : CardDef
{
    public override IReadOnlyList<string> VarNames => ["WeakPower", "VulnerablePower"];

    public override void OnPlay(ref Exec x)
    {
        if (x.Target == Limits.NoOwner)
            return;
        Kernel.ApplyPower(ref x, x.Target, Registry.PowerId<Powers.WeakPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
        Kernel.ApplyPower(ref x, x.Target, Registry.PowerId<Powers.VulnerablePower>(),
            (int)CardEffects.Var(x, 1), Limits.PlayerOwner);
    }
}

/// <summary>
/// <c>CosmicIndifference</c>: block, then choose one discard card and put it on top of the draw pile
/// (<c>CardChoiceSupport.GetSpec</c> CosmicIndifference → <c>Apply</c> MoveToDrawTop).
/// </summary>
[ContentId("COSMIC_INDIFFERENCE")]
public sealed class CosmicIndifference : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref Exec x)
    {
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!Kernel.ChooseCards(ref x, ChoiceEffect.MoveToDrawTop, Pile.Discard, options, 1,
                selected, out int chosen))
        {
            return;
        }
        Kernel.MoveSelected(ref x.B, Pile.Discard, selected[..chosen], Pile.Draw,
            PilePosition.Top);
    }
}

/// <summary>
/// <c>Begone</c>: choose one hand card and transform it into the canonical Minion Strike, upgraded
/// when Begone is upgraded.
/// </summary>
[ContentId("BEGONE")]
public sealed class Begone : CardDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("MINION_STRIKE", 0)];

    public override void OnPlay(ref Exec x)
    {
    }

    public override void OwnChoice(ref Exec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!Kernel.ChooseCards(ref x, ChoiceEffect.Transform, Pile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        Charge.TransformSelected(ref x, Pile.Hand, selected[..chosen], "MINION_STRIKE");
    }
}

/// <summary>
/// <c>RefineBlade</c>: CorePowerSupport forges the Forge variable, then queues EnergyNextTurnPower
/// (<c>combat.AddEnergyNextTurn</c>).
/// </summary>
[ContentId("REFINE_BLADE")]
public sealed class RefineBlade : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge", "Energy"];

    public override void OnPlay(ref Exec x)
    {
        if (!Kernel.Forge(ref x, (int)CardEffects.Var(x, 0)))
            return;
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)CardEffects.Var(x, 1), Limits.PlayerOwner);
    }
}

/// <summary>
/// <c>GatherLight</c>: the inferred recipe gains Block, then CardEffectSpecRegistry's
/// <c>GatherLight</c> gains Stars.
/// </summary>
[ContentId("GATHER_LIGHT")]
public sealed class GatherLight : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Stars"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        Kernel.GainStars(ref x, (int)CardEffects.Var(x, 1));
    }
}

/// <summary>
/// <c>Glow</c>: the inferred recipe draws the Cards variable, then CardEffectSpecRegistry's
/// <c>Glow</c> entry applies DrawCardsNextTurnPower (Cards) and the resource switch gains Stars, in
/// that order.
/// </summary>
[ContentId("GLOW")]
public sealed class Glow : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars", "Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["DRAW_CARDS_NEXT_TURN_POWER"];

    public override void OnPlay(ref Exec x)
    {
        Kernel.Draw(ref x, (int)CardEffects.Var(x, 1), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.DrawCardsNextTurnPower>(), (int)CardEffects.Var(x, 1),
            Limits.PlayerOwner);
        Kernel.GainStars(ref x, (int)CardEffects.Var(x, 0));
    }
}

/// <summary>
/// <c>HiddenCache</c>: CardOnPlaySupport gains Stars, then applies StarNextTurnPower with the
/// "StarNextTurnPower" variable.
/// </summary>
[ContentId("HIDDEN_CACHE")]
public sealed class HiddenCache : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars", "StarNextTurnPower"];

    public override void OnPlay(ref Exec x)
    {
        if (!Kernel.GainStars(ref x, (int)CardEffects.Var(x, 0)))
            return;
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StarNextTurnPower>(),
            (int)CardEffects.Var(x, 1), Limits.PlayerOwner);
    }
}
