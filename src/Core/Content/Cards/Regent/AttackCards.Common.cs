using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary>
/// <c>CollisionCourse</c>: the inferred attack, then CardEffectSpecRegistry adds one Debris to hand.
/// </summary>
[BlitzContent("COLLISION_COURSE")]
public sealed class CollisionCourse : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DEBRIS", 0)];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!BlitzRegistry.TryCard("DEBRIS", out ushort debris))
            throw new BlitzUnsupportedException("CollisionCourse 需要 DEBRIS 的 Blitz 定义。");
        BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CreateCard(x.R, debris), BlitzPile.Hand);
    }
}

/// <summary>
/// <c>GuidingStar</c> (1 star cost): the inferred attack, then CardEffectSpecRegistry's
/// <c>GuidingStar</c> entry applies DrawCardsNextTurnPower with the Cards variable.
/// </summary>
[BlitzContent("GUIDING_STAR")]
public sealed class GuidingStar : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["DRAW_CARDS_NEXT_TURN_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.DrawCardsNextTurnPower>(), (int)BlitzCardEffects.Var(x, 1),
            BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>CrushUnder</c>: attack every enemy, then CardEffectSpecRegistry applies CrushUnderPower (a
/// temporary Strength loss) to every hittable enemy in roster order.
/// </summary>
[BlitzContent("CRUSH_UNDER")]
public sealed class CrushUnder : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StrengthLoss"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        ushort power = BlitzRegistry.PowerId<Powers.CrushUnderPower>();
        int amount = (int)BlitzCardEffects.Var(x, 1);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (!BlitzKernel.IsHittable(x.B, enemy))
                continue;
            BlitzKernel.ApplyTemporaryStrength(ref x, enemy, power, amount, BlitzLimits.PlayerOwner, -1);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary><c>CelestialMight</c>: the general attack with the Repeat variable as its hit count.</summary>
[BlitzContent("CELESTIAL_MIGHT")]
public sealed class CelestialMight : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: (int)BlitzCardEffects.Var(x, 1));
}

/// <summary>
/// <c>CrescentSpear</c> (1 star cost): CalculatedDamage = CalculationBase + ExtraDamage × the number
/// of cards in the combat piles whose canonical star cost is non-negative or X
/// (<c>CalculatedVarSpecRegistry.CrescentSpear</c>).
/// </summary>
[BlitzContent("CRESCENT_SPEAR")]
public sealed class CrescentSpear : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref BlitzExec x)
    {
        int starCards = 0;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref readonly BlitzCard candidate = ref x.B.Cards[position];
            BlitzCardStats stats = x.R.Card(candidate.Id, candidate.Upgrade);
            if (stats.StarCost >= 0 || stats.StarCostsX)
                starCards++;
        }
        decimal damage = BlitzCardEffects.Var(x, 0) + BlitzCardEffects.Var(x, 1) * starCards;
        BlitzCardEffects.Attack(ref x, damage);
    }
}

/// <summary>
/// <c>PhotonCut</c>: attack, draw the Cards variable, then choose the PutBack number of hand cards and
/// put them on top of the draw pile. The played card is in the play pile, so every hand card is an
/// option.
/// </summary>
[BlitzContent("PHOTON_CUT")]
public sealed class PhotonCut : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards", "PutBack"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers settles newly dead enemies after the mirror attack and
        // before the card-specific draw compensation; HittableEnemies/Hellraiser read that phase.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 1), fromHandDraw: false);
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        int count = Math.Min((int)BlitzCardEffects.Var(x, 2), x.B.HandCount);
        if (count <= 0)
            return;
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.MoveToDrawTop, BlitzPile.Hand, options, count,
                selected, out int chosen))
        {
            return;
        }
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Hand, selected[..chosen], BlitzPile.Draw,
            BlitzPilePosition.Top);
    }
}

/// <summary><c>WroughtInWar</c>: the inferred attack, then CardEffectSpecRegistry forges.</summary>
[BlitzContent("WROUGHT_IN_WAR")]
public sealed class WroughtInWar : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Forge"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Forge(ref x, (int)BlitzCardEffects.Var(x, 1));
    }
}

/// <summary><c>SolarStrike</c>: attack, then CardEffectSpecRegistry's <c>SolarStrike</c> gains Stars.</summary>
[BlitzContent("SOLAR_STRIKE")]
public sealed class SolarStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Stars"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.GainStars(ref x, (int)BlitzCardEffects.Var(x, 1));
    }
}

/// <summary><c>AstralPulse</c> (3 star cost): BespokeCardMirrors attacks every enemy twice.</summary>
[BlitzContent("ASTRAL_PULSE")]
public sealed class AstralPulse : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: 2);
}
