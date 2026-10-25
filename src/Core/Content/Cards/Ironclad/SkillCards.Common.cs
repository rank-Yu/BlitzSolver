using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Ironclad;

[BlitzContent("SHRUG_IT_OFF")]
public sealed class ShrugItOff : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[BlitzContent("ARMAMENTS")]
public sealed class Armaments : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x)
    {
        // Inferred BlockVar recipe, then CorePowerSupport: the upgraded card upgrades every upgradable
        // hand card in hand order.
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice || x.Card.Upgrade == 0)
            return;
        for (int index = 0; index < x.B.HandCount; index++)
            BlitzKernel.UpgradeCard(x.R, ref x.B.Cards[index]);
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        // GetSpec: Armaments when !IsUpgraded → Upgrade, Hand, 1, upgradable hand cards.
        if (x.Card.Upgrade != 0)
            return;
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[index];
            if (card.Upgrade < x.R.Card(card.Id, card.Upgrade).MaxUpgradeLevel)
                options[count++] = index;
        }
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Upgrade, BlitzPile.Hand, options[..count], 1, selected, out int chosen))
            return;
        BlitzKernel.UpgradeSelected(ref x, BlitzPile.Hand, selected[..chosen]);
    }
}

[BlitzContent("TRUE_GRIT")]
public sealed class TrueGrit : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x)
    {
        // CardSelectionCardMirrors.TrueGritOnPlay: block, then the base card exhausts a random hand
        // card (Rng.CombatCardSelection.NextItem; no draw from an empty hand).
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice || x.Card.Upgrade != 0 || x.B.HandCount == 0)
            return;
        int pick = x.B.RngStream(BlitzRngStream.CombatCardSelection).NextInt(0, x.B.HandCount);
        BlitzKernel.Exhaust(ref x, BlitzPile.Hand, pick);
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        // GetSpec: TrueGrit when IsUpgraded → Exhaust, Hand, 1, every hand card.
        if (x.Card.Upgrade == 0)
            return;
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Exhaust, BlitzPile.Hand, options, 1, selected, out int chosen))
            return;
        BlitzKernel.ExhaustSelected(ref x, BlitzPile.Hand, selected[..chosen]);
    }
}

[BlitzContent("HAVOC")]
public sealed class Havoc : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.AutoPlayFromDrawPile(ref x, 1, forceExhaust: true);
}

[BlitzContent("BLOOD_WALL")]
public sealed class BloodWall : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "Block"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.LoseHp(ref x, BlitzCardEffects.Var(x, 0));
    }
}

[BlitzContent("TAUNT")]
public sealed class Taunt : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "VulnerablePower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[BlitzContent("TREMBLE")]
public sealed class Tremble : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.VulnerablePower>(), 0);
}
