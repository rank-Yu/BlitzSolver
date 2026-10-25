using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Ironclad;

[BlitzContent("BLUDGEON")]
public sealed class Bludgeon : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("HEMOKINESIS")]
public sealed class Hemokinesis : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 1);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.LoseHp(ref x, BlitzCardEffects.Var(x, 0));
    }
}

[BlitzContent("WHIRLWIND")]
public sealed class Whirlwind : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: BlitzKernel.ResolveEnergyXValue(ref x));
}

[BlitzContent("PILLAGE")]
public sealed class Pillage : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardDrawCardMirrors.PillageOnPlay: draw one card at a time until the drawn card is not an
        // Attack or the hand is full.
        while (true)
        {
            if (!BlitzKernel.TryDrawOne(ref x, fromHandDraw: false, out BlitzCard drawn))
                return;
            if (x.B.HandCount >= x.B.MaxHandSize)
                return;
            // CardDrawCardMirrors.PillageOnPlay tests the returned drawn card, which stays the same
            // value even when AfterCardDrawn auto-played (Hellraiser) and removed it from hand.
            if (BlitzKernel.CardType(x.B, x.R, drawn) != BlitzCardType.Attack)
                return;
        }
    }
}

[BlitzContent("BULLY")]
public sealed class Bully : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override bool TryGetCalculatedDamage(ref BlitzExec x, in BlitzCard self, byte target, out decimal damage)
    {
        int vulnerable = target == BlitzLimits.NoOwner
            ? 0
            : BlitzKernel.GetAmount(x.B, target, BlitzRegistry.PowerId<Powers.VulnerablePower>());
        damage = BodySlam.SelfVar(x, self, 0) + BodySlam.SelfVar(x, self, 1) * vulnerable;
        return true;
    }

    public override void OnPlay(ref BlitzExec x)
    {
        int vulnerable = x.Target == BlitzLimits.NoOwner
            ? 0
            : BlitzKernel.GetAmount(x.B, x.Target, BlitzRegistry.PowerId<Powers.VulnerablePower>());
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0) + BlitzCardEffects.Var(x, 1) * vulnerable);
    }
}

[BlitzContent("DISMANTLE")]
public sealed class Dismantle : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        int vulnerable = x.Target == BlitzLimits.NoOwner
            ? 0
            : BlitzKernel.GetAmount(x.B, x.Target, BlitzRegistry.PowerId<Powers.VulnerablePower>());
        BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: vulnerable > 0 ? 2 : 1);
    }
}

[BlitzContent("ASHEN_STRIKE")]
public sealed class AshenStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.Attack(ref x,
            BlitzCardEffects.Var(x, 0) + BlitzCardEffects.Var(x, 1) * x.B.ExhaustCount);

    public override bool TryGetCalculatedDamage(ref BlitzExec x, in BlitzCard self, byte target, out decimal damage)
    {
        damage = BodySlam.SelfVar(x, self, 0) + BodySlam.SelfVar(x, self, 1) * x.B.ExhaustCount;
        return true;
    }
}

[BlitzContent("FIGHT_ME")]
public sealed class FightMe : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat", "StrengthPower", "EnemyStrength"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: (int)BlitzCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        ushort strength = BlitzRegistry.PowerId<Powers.StrengthPower>();
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, strength,
            (int)BlitzCardEffects.Var(x, 2), BlitzLimits.PlayerOwner);
        BlitzCardEffects.ApplyToTarget(ref x, strength, 3);
    }
}

[BlitzContent("SPITE")]
public sealed class Spite : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref BlitzExec x)
    {
        bool lostHp = (x.B.Player.Flags & BlitzCreatureFlags.LostHpThisTurn) != 0;
        BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: lostHp ? (int)BlitzCardEffects.Var(x, 1) : 1);
    }
}

[BlitzContent("RAMPAGE")]
public sealed class Rampage : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    // Rampage.AfterDowngraded re-adds _extraDamageFromPlays; the delta in Var0 is that accumulator.
    public override bool PreservesVar0OnDowngrade => true;

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // The native DynamicVars are read from the live CardModel after the attack; a mid-play
        // Dampen restore can upgrade this card before the Increase is added.
        BlitzKernel.SyncCurrentPlayCard(ref x);
        BlitzCardEffects.AddCurrentCardVar(ref x, 0, BlitzCardEffects.Var(x, 1));
    }
}

[BlitzContent("UNRELENTING")]
public sealed class Unrelenting : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // Unrelenting.cs: the single-target attack runs first, then FreeAttackPower 1 is applied.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.FreeAttackPower>(),
            1, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("STOMP")]
public sealed class Stomp : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.GeneralAttack(ref x, 0);

    // Stomp.AfterCardEnteredCombat: reduce the cost by the Attacks finished this turn.
    public override void AfterEnteredCombat(ref BlitzExec x, BlitzPile pile, int index)
    {
        int attacks = x.B.Counter(BlitzCounter.AttacksPlayedThisTurn);
        if (attacks <= 0)
            return;
        ref BlitzCard card = ref BlitzPiles.At(ref x.B, pile, index);
        AddThisTurn(ref card, -attacks);
    }

    // BeforeCardPlayedMirrors.HandleStomp: every Attack the owner plays reduces this card by 1.
    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Attack)
            return;
        ref BlitzCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        AddThisTurn(ref card, -1);
    }

    private static void AddThisTurn(ref BlitzCard card, int amount)
    {
        if (amount == 0)
            return;
        if (card.EnergyModifierCount >= BlitzLimits.CardEnergyCostModifiers)
            throw new BlitzCapacityException("Card.EnergyModifiers", BlitzLimits.CardEnergyCostModifiers);
        card.EnergyModifiers[card.EnergyModifierCount] = BlitzCostModifier.Create(amount, BlitzCostType.Relative,
            BlitzCostExpiration.EndOfTurn, reduceOnly: false);
        card.EnergyModifierCount++;
    }
}

[BlitzContent("UPPERCUT")]
public sealed class Uppercut : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Power"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.WeakPower>(), 1);
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[BlitzContent("HOWL_FROM_BEYOND")]
public sealed class HowlFromBeyond : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);

    public override void AfterAutoPostPlayPhaseEntered(ref BlitzExec x, BlitzListener self)
    {
        if (self.Kind != BlitzListenerKind.Card || self.Owner != BlitzLimits.PlayerOwner)
            return;
        int exhaustStart = x.B.HandCount + x.B.DrawCount + x.B.DiscardCount;
        int exhaustEnd = exhaustStart + x.B.ExhaustCount;
        if (self.Index < exhaustStart || self.Index >= exhaustEnd)
            return;
        BlitzKernel.AutoPlay(ref x, BlitzPile.Exhaust, self.Index - exhaustStart);
    }
}
