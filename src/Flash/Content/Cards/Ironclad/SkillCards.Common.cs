using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Ironclad;

[FlashContent("SHRUG_IT_OFF")]
public sealed class ShrugItOff : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[FlashContent("ARMAMENTS")]
public sealed class Armaments : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        // Inferred BlockVar recipe, then CorePowerSupport: the upgraded card upgrades every upgradable
        // hand card in hand order.
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice || x.Card.Upgrade == 0)
            return;
        for (int index = 0; index < x.B.HandCount; index++)
            FlashKernel.UpgradeCard(x.R, ref x.B.Cards[index]);
    }

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: Armaments when !IsUpgraded → Upgrade, Hand, 1, upgradable hand cards.
        if (x.Card.Upgrade != 0)
            return;
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            if (card.Upgrade < x.R.Card(card.Id, card.Upgrade).MaxUpgradeLevel)
                options[count++] = index;
        }
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Upgrade, FlashPile.Hand, options[..count], 1, selected, out int chosen))
            return;
        FlashKernel.UpgradeSelected(ref x, FlashPile.Hand, selected[..chosen]);
    }
}

[FlashContent("TRUE_GRIT")]
public sealed class TrueGrit : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        // CardSelectionCardMirrors.TrueGritOnPlay: block, then the base card exhausts a random hand
        // card (Rng.CombatCardSelection.NextItem; no draw from an empty hand).
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice || x.Card.Upgrade != 0 || x.B.HandCount == 0)
            return;
        int pick = x.B.RngStream(FlashRngStream.CombatCardSelection).NextInt(0, x.B.HandCount);
        FlashKernel.Exhaust(ref x, FlashPile.Hand, pick);
    }

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: TrueGrit when IsUpgraded → Exhaust, Hand, 1, every hand card.
        if (x.Card.Upgrade == 0)
            return;
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand, options, 1, selected, out int chosen))
            return;
        FlashKernel.ExhaustSelected(ref x, FlashPile.Hand, selected[..chosen]);
    }
}

[FlashContent("HAVOC")]
public sealed class Havoc : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.AutoPlayFromDrawPile(ref x, 1, forceExhaust: true);
}

[FlashContent("BLOOD_WALL")]
public sealed class BloodWall : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.LoseHp(ref x, FlashCardEffects.Var(x, 0));
    }
}

[FlashContent("TAUNT")]
public sealed class Taunt : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[FlashContent("TREMBLE")]
public sealed class Tremble : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 0);
}
