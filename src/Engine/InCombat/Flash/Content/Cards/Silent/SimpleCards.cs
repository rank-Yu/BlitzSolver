namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Silent;

// Straightforward attacks, blocks, draw/energy cards and the simple powers. Existing engine:
// CardOnPlayInferrer's attack/block/draw recipes, CorePowerSupport.ApplyCardPowers
// (SuckerPunch/LegSweep/Assassinate/Footwork/Abrasive/Sidestep/...), CardEffectSpecRegistry
// (Blur/Predator/Strangle), CardPowerOnPlaySupport (Speedster/Tracking) and CardDrawCardMirrors
// (Adrenaline).

[FlashContent("SUCKER_PUNCH")]
public sealed class SuckerPunch : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[FlashContent("LEG_SWEEP")]
public sealed class LegSweep : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[FlashContent("ASSASSINATE")]
public sealed class Assassinate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[FlashContent("SLICE")]
public sealed class Slice : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("BACKSTAB")]
public sealed class Backstab : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("DAGGER_SPRAY")]
public sealed class DaggerSpray : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // BespokeCardMirrors.DaggerSprayOnPlay: two hits to all opponents.
    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hitCount: 2);
}

[FlashContent("PREDATOR")]
public sealed class Predator : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry.Predator: DrawCardsNextTurnPower with the fixed amount 2.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.DrawCardsNextTurnPower>(), 2, FlashLimits.PlayerOwner);
    }
}

[FlashContent("ADRENALINE")]
public sealed class Adrenaline : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    // CardDrawCardMirrors.AdrenalineOnPlay: gain Energy, then draw Cards.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[FlashContent("BACKFLIP")]
public sealed class Backflip : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[FlashContent("DEFLECT")]
public sealed class Deflect : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("BLUR")]
public sealed class Blur : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Blur"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.BlurPower>(), 1);
    }
}

[FlashContent("SIDESTEP")]
public sealed class Sidestep : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    // CorePowerSupport.ApplyCardPowers.Sidestep: AddEnergyNextTurn (EnergyNextTurnPower amount).
    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(), 0);
}

[FlashContent("TRACKING")]
public sealed class Tracking : FlashCardDef
{
    // CardOnPlaySupport.Batch042.Tracking: TrackingPower 50.
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.TrackingPower>(), 50, FlashLimits.PlayerOwner);
}

[FlashContent("SPEEDSTER")]
public sealed class Speedster : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SpeedsterPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.SpeedsterPower>(), 0);
}

[FlashContent("STRANGLE")]
public sealed class Strangle : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StranglePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.StranglePower>(), 1);
    }
}

[FlashContent("PINPOINT")]
public sealed class Pinpoint : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    // Pinpoint.cs: entering combat reduces its cost by the Skills already finished this turn.
    public override void AfterEnteredCombat(ref FlashExec x, FlashPile pile, int index)
    {
        int amount = x.B.Counter(FlashCounter.SkillCardsPlayedThisTurn);
        if (amount <= 0)
            return;
        ref FlashCard card = ref FlashPiles.At(ref x.B, pile, index);
        if (card.Id != Id)
            return;
        FlashKernel.AddEnergyCostModifier(ref card, -amount, FlashCostType.Relative,
            FlashCostExpiration.EndOfTurn, reduceOnly: false);
    }

    // Pinpoint.cs.AfterCardPlayed: each Skill its owner plays reduces its cost by 1 this turn, while
    // it sits in any combat pile.
    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        if (FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Skill)
            return;
        ref FlashCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        FlashKernel.AddEnergyCostModifier(ref card, -1, FlashCostType.Relative,
            FlashCostExpiration.EndOfTurn, reduceOnly: false);
    }
}

[FlashContent("GRAND_FINALE")]
public sealed class GrandFinale : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // GrandFinale.cs.IsPlayable: only when the draw pile is empty (CardIsPlayableMirrors).
    public override bool IsPlayable(in FlashBattle battle, FlashRules rules, in FlashCard card)
        => battle.DrawCount == 0;

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("ABRASIVE")]
public sealed class Abrasive : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["ThornsPower", "DexterityPower"];

    // CorePowerSupport.Abrasive: Dexterity then Thorns (the canonical variable order is Thorns,
    // Dexterity).
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.DexterityPower>(), 1);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.ThornsPower>(), 0);
    }
}

[FlashContent("FOOTWORK")]
public sealed class Footwork : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.DexterityPower>(), 0);
}
