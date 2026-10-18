namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Ironclad;

// Existing engine paths for this batch:
// - BloodWall infers Block; CardEffectSpecRegistry applies the self HpLoss afterwards.
// - Taunt infers Block; CorePowerSupport applies Vulnerable afterwards.
// - Tremble has no mirror; CorePowerSupport applies Vulnerable only.
// - Colossus infers Block; CardEffectSpecRegistry applies ColossusPower afterwards.

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

[FlashContent("COLOSSUS")]
public sealed class Colossus : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Colossus"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ColossusPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

// P4 batch:
// - EvilEye infers one Block; CardEffectSpecRegistry gains a second Block when a card was exhausted
//   this turn (flash keeps that per-turn counter).
// - ForgottenRitual gains energy (CardOnPlaySupport: ForgottenRitual / Fuel / Luminesce).
// - DrumOfBattle draws Cards and, as its own AfterCardExhausted listener, gains Energy when it is the
//   exhausted card (M/Hooks/Card/AfterCardExhaustedMirrors.HandleDrumOfBattle).
// - NotYet heals its Heal var (CardOnPlaySupport).

[FlashContent("EVIL_EYE")]
public sealed class EvilEye : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (x.B.Counter(FlashCounter.CardsExhaustedThisTurn) > 0)
            FlashCardEffects.GeneralBlock(ref x, 0);
    }
}

[FlashContent("FORGOTTEN_RITUAL")]
public sealed class ForgottenRitual : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
}

[FlashContent("DRUM_OF_BATTLE")]
public sealed class DrumOfBattle : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Energy"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);

    public override void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card, bool causedByEthereal)
    {
        // The listener's card slot is the exhausted copy only when that copy is the last card moved
        // into the exhaust pile; other identical copies must not trigger.
        if (x.B.ExhaustCount == 0)
            return;
        int exhaustedPosition = FlashPiles.Start(x.B, FlashPile.Exhaust) + x.B.ExhaustCount - 1;
        if (self.Index != exhaustedPosition || x.B.Cards[exhaustedPosition].Id != Id)
            return;
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        // GeneratePlayCount has no supported ModifyCardPlayCount listeners, so it is one.
        FlashKernel.GainEnergy(ref x, stats.Vars[1] + card.Var1);
    }
}

[FlashContent("NOT_YET")]
public sealed class NotYet : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Heal"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Heal(ref x, FlashLimits.PlayerOwner, FlashCardEffects.Var(x, 0));
}

[FlashContent("BATTLE_TRANCE")]
public sealed class BattleTrance : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // BattleTrance: the inferred owner draw (Cards) runs first, then
    // CorePowerSupport.ApplyCardPowers applies NoDrawPower 1 to the owner.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.NoDrawPower>(),
            1, FlashLimits.PlayerOwner);
    }
}
