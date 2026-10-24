using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

/// <summary>
/// <c>Supermassive</c>: a calculated attack (<c>CalculatedVarSpecRegistry.Supermassive</c>):
/// CalculationBase + ExtraDamage × cards generated this combat. The generated-card count is a
/// combat-long counter imported from the root history.
/// </summary>
[FlashContent("SUPERMASSIVE")]
public sealed class Supermassive : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        int generated = x.B.Counter(FlashCounter.CardsGeneratedThisCombat);
        decimal damage = FlashCardEffects.Var(x, 0)
            + FlashCardEffects.Var(x, 1) * generated;
        FlashCardEffects.Attack(ref x, damage);
    }
}

/// <summary>
/// <c>KinglyKick</c>: the inferred attack; drawing it lowers its own cost by 1 for the rest of combat
/// (<c>AfterCardDrawnMirrors.HandleKinglyKick</c> → <c>EnergyCost.AddThisCombat(-1)</c>).
/// </summary>
[FlashContent("KINGLY_KICK")]
public sealed class KinglyKick : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        if (!FlashKernel.TakeIntrinsicDraw(ref x, self, drawnPosition))
            return;
        // The drawn card is this card, at its post-draw hand position (AddThisCombat(-1)).
        FlashKernel.AddEnergyCostModifier(ref x.B.Cards[drawnPosition], -1, FlashCostType.Relative,
            FlashCostExpiration.EndOfCombat, reduceOnly: false);
    }
}

/// <summary>
/// <c>KinglyPunch</c>: the inferred attack; drawing it adds the Increase variable to its Damage for
/// the rest of combat (<c>AfterCardDrawnMirrors.HandleKinglyPunch</c>). The accumulated increase is
/// kept in <see cref="FlashCard.Private"/> so <c>AfterDowngraded</c> can re-add it after Dampen.
/// </summary>
[FlashContent("KINGLY_PUNCH")]
public sealed class KinglyPunch : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        if (!FlashKernel.TakeIntrinsicDraw(ref x, self, drawnPosition))
            return;
        ref FlashCard drawn = ref x.B.Cards[drawnPosition];
        int increase = (int)x.R.Card(drawn.Id, drawn.Upgrade).Vars[1];
        if (increase > short.MaxValue - drawn.Var0 || increase > short.MaxValue - drawn.Private)
            throw new FlashUnsupportedException("KinglyPunch 的伤害累加超出卡牌私有计数范围。");
        drawn.Var0 = (short)(drawn.Var0 + increase);
        drawn.Private = (short)(drawn.Private + increase);
    }

    public override void ProjectPrivate(ref FlashCard card) => card.Private = card.Var0;

    public override void AfterDowngraded(ref FlashBattle battle, int position)
        => battle.Cards[position].Var0 = battle.Cards[position].Private;
}

/// <summary>
/// <c>Radiate</c>: CalculatedHits = CalculationBase + CalculationExtra × stars gained this turn
/// (<c>CalculatedVarSpecRegistry.Radiate</c> → <c>GetStarsGainedThisTurn</c>), used as the all-enemy
/// attack's hit count.
/// </summary>
[FlashContent("RADIATE")]
public sealed class Radiate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref FlashExec x)
    {
        int hits = (int)(FlashCardEffects.Var(x, 1)
            + FlashCardEffects.Var(x, 2) * x.B.Counter(FlashCounter.StarsGainedThisTurn));
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: hits);
    }
}

/// <summary><c>Devastate</c> (4 star cost): the general attack.</summary>
[FlashContent("DEVASTATE")]
public sealed class Devastate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

/// <summary><c>GammaBlast</c> (3 star cost): attack, then Weak and Vulnerable after the enemy deaths.</summary>
[FlashContent("GAMMA_BLAST")]
public sealed class GammaBlast : FlashCardDef
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

/// <summary><c>Hegemony</c>: the inferred attack, then EnergyNextTurnPower with the Energy variable.</summary>
[FlashContent("HEGEMONY")]
public sealed class Hegemony : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Energy"];

    public override IReadOnlyList<string> AppliedPowers => ["ENERGY_NEXT_TURN_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
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
