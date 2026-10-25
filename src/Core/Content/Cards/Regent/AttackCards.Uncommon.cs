using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary>
/// <c>Supermassive</c>: a calculated attack (<c>CalculatedVarSpecRegistry.Supermassive</c>):
/// CalculationBase + ExtraDamage × cards generated this combat. The generated-card count is a
/// combat-long counter imported from the root history.
/// </summary>
[BlitzContent("SUPERMASSIVE")]
public sealed class Supermassive : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref BlitzExec x)
    {
        int generated = x.B.Counter(BlitzCounter.CardsGeneratedThisCombat);
        decimal damage = BlitzCardEffects.Var(x, 0)
            + BlitzCardEffects.Var(x, 1) * generated;
        BlitzCardEffects.Attack(ref x, damage);
    }
}

/// <summary>
/// <c>KinglyKick</c>: the inferred attack; drawing it lowers its own cost by 1 for the rest of combat
/// (<c>AfterCardDrawnMirrors.HandleKinglyKick</c> → <c>EnergyCost.AddThisCombat(-1)</c>).
/// </summary>
[BlitzContent("KINGLY_KICK")]
public sealed class KinglyKick : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);

    public override void AfterCardDrawn(ref BlitzExec x, BlitzListener self, int drawnPosition, bool fromHandDraw)
    {
        if (!BlitzKernel.TakeIntrinsicDraw(ref x, self, drawnPosition))
            return;
        // The drawn card is this card, at its post-draw hand position (AddThisCombat(-1)).
        BlitzKernel.AddEnergyCostModifier(ref x.B.Cards[drawnPosition], -1, BlitzCostType.Relative,
            BlitzCostExpiration.EndOfCombat, reduceOnly: false);
    }
}

/// <summary>
/// <c>KinglyPunch</c>: the inferred attack; drawing it adds the Increase variable to its Damage for
/// the rest of combat (<c>AfterCardDrawnMirrors.HandleKinglyPunch</c>). The accumulated increase is
/// kept in <see cref="BlitzCard.Private"/> so <c>AfterDowngraded</c> can re-add it after Dampen.
/// </summary>
[BlitzContent("KINGLY_PUNCH")]
public sealed class KinglyPunch : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);

    public override void AfterCardDrawn(ref BlitzExec x, BlitzListener self, int drawnPosition, bool fromHandDraw)
    {
        if (!BlitzKernel.TakeIntrinsicDraw(ref x, self, drawnPosition))
            return;
        ref BlitzCard drawn = ref x.B.Cards[drawnPosition];
        int increase = (int)x.R.Card(drawn.Id, drawn.Upgrade).Vars[1];
        if (increase > short.MaxValue - drawn.Var0 || increase > short.MaxValue - drawn.Private)
            throw new BlitzUnsupportedException("KinglyPunch 的伤害累加超出卡牌私有计数范围。");
        drawn.Var0 = (short)(drawn.Var0 + increase);
        drawn.Private = (short)(drawn.Private + increase);
    }

    public override void ProjectPrivate(ref BlitzCard card) => card.Private = card.Var0;

    public override void AfterDowngraded(ref BlitzBattle battle, int position)
        => battle.Cards[position].Var0 = battle.Cards[position].Private;
}

/// <summary>
/// <c>Radiate</c>: CalculatedHits = CalculationBase + CalculationExtra × stars gained this turn
/// (<c>CalculatedVarSpecRegistry.Radiate</c> → <c>GetStarsGainedThisTurn</c>), used as the all-enemy
/// attack's hit count.
/// </summary>
[BlitzContent("RADIATE")]
public sealed class Radiate : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref BlitzExec x)
    {
        int hits = (int)(BlitzCardEffects.Var(x, 1)
            + BlitzCardEffects.Var(x, 2) * x.B.Counter(BlitzCounter.StarsGainedThisTurn));
        BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: hits);
    }
}

/// <summary><c>Devastate</c> (4 star cost): the general attack.</summary>
[BlitzContent("DEVASTATE")]
public sealed class Devastate : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

/// <summary><c>GammaBlast</c> (3 star cost): attack, then Weak and Vulnerable after the enemy deaths.</summary>
[BlitzContent("GAMMA_BLAST")]
public sealed class GammaBlast : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower", "WeakPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.WeakPower>(),
            (int)BlitzCardEffects.Var(x, 2), BlitzLimits.PlayerOwner);
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.VulnerablePower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}

/// <summary><c>Hegemony</c>: the inferred attack, then EnergyNextTurnPower with the Energy variable.</summary>
[BlitzContent("HEGEMONY")]
public sealed class Hegemony : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Energy"];

    public override IReadOnlyList<string> AppliedPowers => ["ENERGY_NEXT_TURN_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>ShiningStrike</c>: attack, then the spec gains Stars; CardResultLocationMirrors sends a
/// discard result to the top of the draw pile instead.
/// </summary>
[BlitzContent("SHINING_STRIKE")]
public sealed class ShiningStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Stars"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.GainStars(ref x, (int)BlitzCardEffects.Var(x, 1));
    }

    public override BlitzPile ResultLocation(in BlitzBattle battle, BlitzRules rules, in BlitzCard card,
        BlitzPile baseLocation, out BlitzPilePosition position)
    {
        position = BlitzPilePosition.Bottom;
        if (baseLocation != BlitzPile.Discard)
            return baseLocation;
        position = BlitzPilePosition.Top;
        return BlitzPile.Draw;
    }
}

/// <summary>
/// <c>KnockoutBlow</c>: attack, then CorePowerSupport's case gains Stars when the attack killed its
/// target; the deaths settle before that compensation.
/// </summary>
[BlitzContent("KNOCKOUT_BLOW")]
public sealed class KnockoutBlow : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Stars"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || !x.CardKilledTarget)
            return;
        BlitzKernel.GainStars(ref x, (int)BlitzCardEffects.Var(x, 1));
    }
}

/// <summary>
/// <c>Stardust</c> (X star cost): RandomTargetAttackCardMirrors attacks a random opponent once per star
/// spent (<c>ResolveStarXValue</c>).
/// </summary>
[BlitzContent("STARDUST")]
public sealed class Stardust : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        int hits = BlitzKernel.ResolveStarXValue(ref x);
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), hits);
    }
}
