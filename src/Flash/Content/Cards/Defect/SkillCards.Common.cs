using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Defect;

[FlashContent("COOLHEADED")]
public sealed class CoolHeaded : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // OrbCardMirrors.CoolheadedOnPlay: channel Frost, then draw Cards.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("FROST_ORB"));
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);
    }
}

[FlashContent("LIGHTNING_ROD")]
public sealed class LightningRod : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "LightningRodPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.LightningRodPower>(), 1);
    }
}

[FlashContent("HOTFIX")]
public sealed class Hotfix : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyTemporaryFocus(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.HotfixPower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner,
            sign: 1);
}

[FlashContent("LEAP")]
public sealed class Leap : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("BOOST_AWAY")]
public sealed class BoostAway : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DAZED", 0)];

    // CardEffectSpecRegistry: gain block, then AddFixed<Dazed>(PileType.Discard, 1).
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryCard("DAZED", out ushort dazed))
            throw new FlashUnsupportedException("Dazed 没有 Flash 定义。");
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, dazed), FlashPile.Discard);
    }
}

[FlashContent("CHARGE_BATTERY")]
public sealed class ChargeBattery : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(), 1);
    }
}

[FlashContent("HOLOGRAM")]
public sealed class Hologram : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        // CardChoiceSupport.GetSpec: Hologram moves one discard card to hand.
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Discard, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen > 0)
            FlashKernel.MoveSelected(ref x.B, FlashPile.Discard, selected[..chosen], FlashPile.Hand,
                FlashPilePosition.Bottom);
    }
}

[FlashContent("TURBO")]
public sealed class Turbo : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("VOID", 0)];

    // CardOnPlaySupport.Turbo: gain Energy, then generate one Void to the discard pile.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryCard("VOID", out ushort id))
            throw new FlashUnsupportedException("涡轮生成需要 VOID 的 Flash 定义。");
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, id), FlashPile.Discard);
    }
}
