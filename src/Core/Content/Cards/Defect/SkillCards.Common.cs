using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Content.Cards.Defect;

[ContentId("COOLHEADED")]
public sealed class CoolHeaded : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // OrbCardMirrors.CoolheadedOnPlay: channel Frost, then draw Cards.
    public override void OnPlay(ref Exec x)
    {
        Kernel.ChannelOrb(ref x, Kernel.OrbId("FROST_ORB"));
        if (x.NeedsChoice)
            return;
        Kernel.Draw(ref x, (int)CardEffects.Var(x, 0), fromHandDraw: false);
    }
}

[ContentId("LIGHTNING_ROD")]
public sealed class LightningRod : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "LightningRodPower"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.LightningRodPower>(), 1);
    }
}

[ContentId("HOTFIX")]
public sealed class Hotfix : CardDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyTemporaryFocus(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.HotfixPower>(), (int)CardEffects.Var(x, 0), Limits.PlayerOwner,
            sign: 1);
}

[ContentId("LEAP")]
public sealed class Leap : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralBlock(ref x, 0);
}

[ContentId("BOOST_AWAY")]
public sealed class BoostAway : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DAZED", 0)];

    // CardEffectSpecRegistry: gain block, then AddFixed<Dazed>(PileType.Discard, 1).
    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!Registry.TryCard("DAZED", out ushort dazed))
            throw new UnsupportedException("Dazed 没有 Blitz 定义。");
        Kernel.AddGeneratedCard(ref x, Kernel.CreateCard(x.R, dazed), Pile.Discard);
    }
}

[ContentId("CHARGE_BATTERY")]
public sealed class ChargeBattery : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.EnergyNextTurnPower>(), 1);
    }
}

[ContentId("HOLOGRAM")]
public sealed class Hologram : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref Exec x)
    {
        // CardChoiceSupport.GetSpec: Hologram moves one discard card to hand.
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!Kernel.ChooseCards(ref x, ChoiceEffect.MoveToHand, Pile.Discard, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen > 0)
            Kernel.MoveSelected(ref x.B, Pile.Discard, selected[..chosen], Pile.Hand,
                PilePosition.Bottom);
    }
}

[ContentId("TURBO")]
public sealed class Turbo : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("VOID", 0)];

    // CardOnPlaySupport.Turbo: gain Energy, then generate one Void to the discard pile.
    public override void OnPlay(ref Exec x)
    {
        Kernel.GainEnergy(ref x, CardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        if (!Registry.TryCard("VOID", out ushort id))
            throw new UnsupportedException("涡轮生成需要 VOID 的 Blitz 定义。");
        Kernel.AddGeneratedCard(ref x, Kernel.CreateCard(x.R, id), Pile.Discard);
    }
}
