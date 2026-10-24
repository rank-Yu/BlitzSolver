using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Ironclad;

[FlashContent("BLUDGEON")]
public sealed class Bludgeon : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("HEMOKINESIS")]
public sealed class Hemokinesis : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 1);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.LoseHp(ref x, FlashCardEffects.Var(x, 0));
    }
}

[FlashContent("WHIRLWIND")]
public sealed class Whirlwind : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: FlashKernel.ResolveEnergyXValue(ref x));
}

[FlashContent("PILLAGE")]
public sealed class Pillage : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardDrawCardMirrors.PillageOnPlay: draw one card at a time until the drawn card is not an
        // Attack or the hand is full.
        while (true)
        {
            if (!FlashKernel.TryDrawOne(ref x, fromHandDraw: false, out FlashCard drawn))
                return;
            if (x.B.HandCount >= x.B.MaxHandSize)
                return;
            // CardDrawCardMirrors.PillageOnPlay tests the returned drawn card, which stays the same
            // value even when AfterCardDrawn auto-played (Hellraiser) and removed it from hand.
            if (FlashKernel.CardType(x.B, x.R, drawn) != FlashCardType.Attack)
                return;
        }
    }
}

[FlashContent("BULLY")]
public sealed class Bully : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override bool TryGetCalculatedDamage(ref FlashExec x, in FlashCard self, byte target, out decimal damage)
    {
        int vulnerable = target == FlashLimits.NoOwner
            ? 0
            : FlashKernel.GetAmount(x.B, target, FlashRegistry.PowerId<Powers.VulnerablePower>());
        damage = BodySlam.SelfVar(x, self, 0) + BodySlam.SelfVar(x, self, 1) * vulnerable;
        return true;
    }

    public override void OnPlay(ref FlashExec x)
    {
        int vulnerable = x.Target == FlashLimits.NoOwner
            ? 0
            : FlashKernel.GetAmount(x.B, x.Target, FlashRegistry.PowerId<Powers.VulnerablePower>());
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * vulnerable);
    }
}

[FlashContent("DISMANTLE")]
public sealed class Dismantle : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        int vulnerable = x.Target == FlashLimits.NoOwner
            ? 0
            : FlashKernel.GetAmount(x.B, x.Target, FlashRegistry.PowerId<Powers.VulnerablePower>());
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: vulnerable > 0 ? 2 : 1);
    }
}

[FlashContent("ASHEN_STRIKE")]
public sealed class AshenStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x,
            FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * x.B.ExhaustCount);

    public override bool TryGetCalculatedDamage(ref FlashExec x, in FlashCard self, byte target, out decimal damage)
    {
        damage = BodySlam.SelfVar(x, self, 0) + BodySlam.SelfVar(x, self, 1) * x.B.ExhaustCount;
        return true;
    }
}

[FlashContent("FIGHT_ME")]
public sealed class FightMe : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat", "StrengthPower", "EnemyStrength"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: (int)FlashCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        ushort strength = FlashRegistry.PowerId<Powers.StrengthPower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, strength,
            (int)FlashCardEffects.Var(x, 2), FlashLimits.PlayerOwner);
        FlashCardEffects.ApplyToTarget(ref x, strength, 3);
    }
}

[FlashContent("SPITE")]
public sealed class Spite : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
    {
        bool lostHp = (x.B.Player.Flags & FlashCreatureFlags.LostHpThisTurn) != 0;
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: lostHp ? (int)FlashCardEffects.Var(x, 1) : 1);
    }
}

[FlashContent("RAMPAGE")]
public sealed class Rampage : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    // Rampage.AfterDowngraded re-adds _extraDamageFromPlays; the delta in Var0 is that accumulator.
    public override bool PreservesVar0OnDowngrade => true;

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // The native DynamicVars are read from the live CardModel after the attack; a mid-play
        // Dampen restore can upgrade this card before the Increase is added.
        FlashKernel.SyncCurrentPlayCard(ref x);
        FlashCardEffects.AddCurrentCardVar(ref x, 0, FlashCardEffects.Var(x, 1));
    }
}

[FlashContent("UNRELENTING")]
public sealed class Unrelenting : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // Unrelenting.cs: the single-target attack runs first, then FreeAttackPower 1 is applied.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FreeAttackPower>(),
            1, FlashLimits.PlayerOwner);
    }
}

[FlashContent("STOMP")]
public sealed class Stomp : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0);

    // Stomp.AfterCardEnteredCombat: reduce the cost by the Attacks finished this turn.
    public override void AfterEnteredCombat(ref FlashExec x, FlashPile pile, int index)
    {
        int attacks = x.B.Counter(FlashCounter.AttacksPlayedThisTurn);
        if (attacks <= 0)
            return;
        ref FlashCard card = ref FlashPiles.At(ref x.B, pile, index);
        AddThisTurn(ref card, -attacks);
    }

    // BeforeCardPlayedMirrors.HandleStomp: every Attack the owner plays reduces this card by 1.
    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Attack)
            return;
        ref FlashCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        AddThisTurn(ref card, -1);
    }

    private static void AddThisTurn(ref FlashCard card, int amount)
    {
        if (amount == 0)
            return;
        if (card.EnergyModifierCount >= FlashLimits.CardEnergyCostModifiers)
            throw new FlashCapacityException("Card.EnergyModifiers", FlashLimits.CardEnergyCostModifiers);
        card.EnergyModifiers[card.EnergyModifierCount] = FlashCostModifier.Create(amount, FlashCostType.Relative,
            FlashCostExpiration.EndOfTurn, reduceOnly: false);
        card.EnergyModifierCount++;
    }
}

[FlashContent("UPPERCUT")]
public sealed class Uppercut : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Power"];

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
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[FlashContent("HOWL_FROM_BEYOND")]
public sealed class HowlFromBeyond : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void AfterAutoPostPlayPhaseEntered(ref FlashExec x, FlashListener self)
    {
        if (self.Kind != FlashListenerKind.Card || self.Owner != FlashLimits.PlayerOwner)
            return;
        int exhaustStart = x.B.HandCount + x.B.DrawCount + x.B.DiscardCount;
        int exhaustEnd = exhaustStart + x.B.ExhaustCount;
        if (self.Index < exhaustStart || self.Index >= exhaustEnd)
            return;
        FlashKernel.AutoPlay(ref x, FlashPile.Exhaust, self.Index - exhaustStart);
    }
}
