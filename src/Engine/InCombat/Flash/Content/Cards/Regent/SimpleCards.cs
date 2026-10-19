namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Regent;

// Regent card batch. Each definition copies the existing engine path named in its comment: the
// inferred attack/block/draw recipe (CardOnPlayInferrer), CardEffectSpecRegistry, CardOnPlaySupport,
// CorePowerSupport.ApplyCardPowers, CardOnPlayMirrors' bespoke mirrors and CardResultLocationMirrors.

/// <summary><c>SolarStrike</c>: attack, then CardEffectSpecRegistry's <c>SolarStrike</c> gains Stars.</summary>
[FlashContent("SOLAR_STRIKE")]
public sealed class SolarStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Stars"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 1));
    }
}

/// <summary>
/// <c>ShiningStrike</c>: attack, then the spec gains Stars; CardResultLocationMirrors sends a
/// discard result to the top of the draw pile instead.
/// </summary>
[FlashContent("SHINING_STRIKE")]
public sealed class ShiningStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Stars"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 1));
    }

    public override FlashPile ResultLocation(in FlashBattle battle, FlashRules rules, in FlashCard card,
        FlashPile baseLocation, out FlashPilePosition position)
    {
        position = FlashPilePosition.Bottom;
        if (baseLocation != FlashPile.Discard)
            return baseLocation;
        position = FlashPilePosition.Top;
        return FlashPile.Draw;
    }
}

/// <summary>
/// <c>GatherLight</c>: the inferred recipe gains Block, then CardEffectSpecRegistry's
/// <c>GatherLight</c> gains Stars.
/// </summary>
[FlashContent("GATHER_LIGHT")]
public sealed class GatherLight : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Stars"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 1));
    }
}

/// <summary>
/// <c>Glow</c>: the inferred recipe draws the Cards variable, then CardEffectSpecRegistry's
/// <c>Glow</c> entry applies DrawCardsNextTurnPower (Cards) and the resource switch gains Stars, in
/// that order.
/// </summary>
[FlashContent("GLOW")]
public sealed class Glow : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars", "Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["DRAW_CARDS_NEXT_TURN_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 1), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.DrawCardsNextTurnPower>(), (int)FlashCardEffects.Var(x, 1),
            FlashLimits.PlayerOwner);
        FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 0));
    }
}

/// <summary><c>RoyalGamble</c>: CardOnPlaySupport's <c>RoyalGamble</c> case gains Stars.</summary>
[FlashContent("ROYAL_GAMBLE")]
public sealed class RoyalGamble : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 0));
}

/// <summary><c>Alignment</c> (2 star cost): CardOnPlaySupport's <c>Alignment</c> case gains Energy.</summary>
[FlashContent("ALIGNMENT")]
public sealed class Alignment : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
}

/// <summary>
/// <c>HiddenCache</c>: CardOnPlaySupport gains Stars, then applies StarNextTurnPower with the
/// "StarNextTurnPower" variable.
/// </summary>
[FlashContent("HIDDEN_CACHE")]
public sealed class HiddenCache : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars", "StarNextTurnPower"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 0)))
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StarNextTurnPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Convergence</c>: CardOnPlaySupport applies RetainHandPower 1, EnergyNextTurnPower (Energy) and
/// StarNextTurnPower (Stars), in that order.
/// </summary>
[FlashContent("CONVERGENCE")]
public sealed class Convergence : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Stars"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RetainHandPower>(),
            1, FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StarNextTurnPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>KnockoutBlow</c>: attack, then CorePowerSupport's case gains Stars when the attack killed its
/// target; the deaths settle before that compensation.
/// </summary>
[FlashContent("KNOCKOUT_BLOW")]
public sealed class KnockoutBlow : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Stars"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || !x.CardKilledTarget)
            return;
        FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 1));
    }
}

/// <summary>
/// <c>Comet</c> (5 star cost): attack, then CorePowerSupport applies Weak and Vulnerable after the
/// deferred enemy deaths.
/// </summary>
[FlashContent("COMET")]
public sealed class Comet : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.WeakPower>(),
            (int)FlashCardEffects.Var(x, 2), FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.VulnerablePower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>DyingStar</c> (3 star cost, Ethereal): attack every enemy, then CardEffectSpecRegistry applies
/// DyingStarPower (a temporary Strength loss) to every hittable enemy in roster order.
/// </summary>
[FlashContent("DYING_STAR")]
public sealed class DyingStar : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StrengthLoss"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        ushort power = FlashRegistry.PowerId<Powers.DyingStarPower>();
        int amount = (int)FlashCardEffects.Var(x, 1);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            FlashKernel.ApplyTemporaryStrength(ref x, enemy, power, amount, FlashLimits.PlayerOwner, -1);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary><c>AstralPulse</c> (3 star cost): BespokeCardMirrors attacks every enemy twice.</summary>
[FlashContent("ASTRAL_PULSE")]
public sealed class AstralPulse : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: 2);
}

/// <summary><c>Bombardment</c>: the inferred recipe is a plain single-target attack.</summary>
[FlashContent("BOMBARDMENT")]
public sealed class Bombardment : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

/// <summary>
/// <c>IAmInvincible</c>: the inferred recipe gains Block; AfterAutoPostPlayPhaseEnteredMirrors then
/// auto-plays one card from the top of the draw pile when this card is that top card.
/// </summary>
[FlashContent("I_AM_INVINCIBLE")]
public sealed class IAmInvincible : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override void AfterAutoPostPlayPhaseEntered(ref FlashExec x, FlashListener self)
    {
        if (self.Kind != FlashListenerKind.Card || self.Owner != FlashLimits.PlayerOwner)
            return;
        // DrawPile.TopCard?.References(card): the card must be the top of the draw pile. Flash has no
        // instance identity, so the listener's position is the identity; the hand segment precedes it.
        if (self.Index != x.B.HandCount)
            return;
        FlashKernel.AutoPlayFromDrawPile(ref x, 1, forceExhaust: false);
    }
}

/// <summary>
/// <c>Stardust</c> (X star cost): RandomTargetAttackCardMirrors attacks a random opponent once per star
/// spent (<c>ResolveStarXValue</c>).
/// </summary>
[FlashContent("STARDUST")]
public sealed class Stardust : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        int hits = FlashKernel.ResolveStarXValue(ref x);
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hits);
    }
}

/// <summary>
/// <c>HeavenlyDrill</c> (X energy cost): BespokeCardMirrors resolves the energy X value and doubles it
/// when it reaches the canonical Energy variable, then attacks once per hit.
/// </summary>
[FlashContent("HEAVENLY_DRILL")]
public sealed class HeavenlyDrill : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        int hits = FlashKernel.ResolveEnergyXValue(ref x);
        if (hits >= (int)FlashCardEffects.Var(x, 1))
            hits *= 2;
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hits);
    }
}

/// <summary>
/// <c>BigBang</c> (Exhaust): the inferred recipe draws the Cards variable, then
/// CardEffectSpecRegistry's <c>BigBang</c> gains Energy, gains Stars and forges, in that order.
/// </summary>
[FlashContent("BIG_BANG")]
public sealed class BigBang : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Energy", "Stars", "Forge"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 1));
        if (!FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 2)))
            return;
        FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 3));
    }
}
