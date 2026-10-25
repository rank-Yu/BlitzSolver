using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[BlitzContent("COOLHEADED")]
public sealed class CoolHeaded : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // OrbCardMirrors.CoolheadedOnPlay: channel Frost, then draw Cards.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("FROST_ORB"));
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 0), fromHandDraw: false);
    }
}

[BlitzContent("LIGHTNING_ROD")]
public sealed class LightningRod : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "LightningRodPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.LightningRodPower>(), 1);
    }
}

[BlitzContent("HOTFIX")]
public sealed class Hotfix : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyTemporaryFocus(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.HotfixPower>(), (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner,
            sign: 1);
}

[BlitzContent("LEAP")]
public sealed class Leap : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);
}

[BlitzContent("BOOST_AWAY")]
public sealed class BoostAway : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DAZED", 0)];

    // CardEffectSpecRegistry: gain block, then AddFixed<Dazed>(PileType.Discard, 1).
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!BlitzRegistry.TryCard("DAZED", out ushort dazed))
            throw new BlitzUnsupportedException("Dazed 没有 Blitz 定义。");
        BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CreateCard(x.R, dazed), BlitzPile.Discard);
    }
}

[BlitzContent("CHARGE_BATTERY")]
public sealed class ChargeBattery : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.EnergyNextTurnPower>(), 1);
    }
}

[BlitzContent("HOLOGRAM")]
public sealed class Hologram : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref BlitzExec x)
    {
        // CardChoiceSupport.GetSpec: Hologram moves one discard card to hand.
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.MoveToHand, BlitzPile.Discard, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen > 0)
            BlitzKernel.MoveSelected(ref x.B, BlitzPile.Discard, selected[..chosen], BlitzPile.Hand,
                BlitzPilePosition.Bottom);
    }
}

[BlitzContent("TURBO")]
public sealed class Turbo : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("VOID", 0)];

    // CardOnPlaySupport.Turbo: gain Energy, then generate one Void to the discard pile.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        if (!BlitzRegistry.TryCard("VOID", out ushort id))
            throw new BlitzUnsupportedException("涡轮生成需要 VOID 的 Blitz 定义。");
        BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CreateCard(x.R, id), BlitzPile.Discard);
    }
}
