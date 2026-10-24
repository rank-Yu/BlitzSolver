using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

/// <summary>
/// <c>CollisionCourse</c>: the inferred attack, then CardEffectSpecRegistry adds one Debris to hand.
/// </summary>
[FlashContent("COLLISION_COURSE")]
public sealed class CollisionCourse : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DEBRIS", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryCard("DEBRIS", out ushort debris))
            throw new FlashUnsupportedException("CollisionCourse 需要 DEBRIS 的 Flash 定义。");
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, debris), FlashPile.Hand);
    }
}

/// <summary>
/// <c>GuidingStar</c> (1 star cost): the inferred attack, then CardEffectSpecRegistry's
/// <c>GuidingStar</c> entry applies DrawCardsNextTurnPower with the Cards variable.
/// </summary>
[FlashContent("GUIDING_STAR")]
public sealed class GuidingStar : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["DRAW_CARDS_NEXT_TURN_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.DrawCardsNextTurnPower>(), (int)FlashCardEffects.Var(x, 1),
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>CrushUnder</c>: attack every enemy, then CardEffectSpecRegistry applies CrushUnderPower (a
/// temporary Strength loss) to every hittable enemy in roster order.
/// </summary>
[FlashContent("CRUSH_UNDER")]
public sealed class CrushUnder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StrengthLoss"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        ushort power = FlashRegistry.PowerId<Powers.CrushUnderPower>();
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

/// <summary><c>CelestialMight</c>: the general attack with the Repeat variable as its hit count.</summary>
[FlashContent("CELESTIAL_MIGHT")]
public sealed class CelestialMight : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: (int)FlashCardEffects.Var(x, 1));
}

/// <summary>
/// <c>CrescentSpear</c> (1 star cost): CalculatedDamage = CalculationBase + ExtraDamage × the number
/// of cards in the combat piles whose canonical star cost is non-negative or X
/// (<c>CalculatedVarSpecRegistry.CrescentSpear</c>).
/// </summary>
[FlashContent("CRESCENT_SPEAR")]
public sealed class CrescentSpear : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        int starCards = 0;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref readonly FlashCard candidate = ref x.B.Cards[position];
            FlashCardStats stats = x.R.Card(candidate.Id, candidate.Upgrade);
            if (stats.StarCost >= 0 || stats.StarCostsX)
                starCards++;
        }
        decimal damage = FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * starCards;
        FlashCardEffects.Attack(ref x, damage);
    }
}

/// <summary>
/// <c>PhotonCut</c>: attack, draw the Cards variable, then choose the PutBack number of hand cards and
/// put them on top of the draw pile. The played card is in the play pile, so every hand card is an
/// option.
/// </summary>
[FlashContent("PHOTON_CUT")]
public sealed class PhotonCut : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards", "PutBack"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers settles newly dead enemies after the mirror attack and
        // before the card-specific draw compensation; HittableEnemies/Hellraiser read that phase.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }

    public override void OwnChoice(ref FlashExec x)
    {
        int count = Math.Min((int)FlashCardEffects.Var(x, 2), x.B.HandCount);
        if (count <= 0)
            return;
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToDrawTop, FlashPile.Hand, options, count,
                selected, out int chosen))
        {
            return;
        }
        FlashKernel.MoveSelected(ref x.B, FlashPile.Hand, selected[..chosen], FlashPile.Draw,
            FlashPilePosition.Top);
    }
}

/// <summary><c>WroughtInWar</c>: the inferred attack, then CardEffectSpecRegistry forges.</summary>
[FlashContent("WROUGHT_IN_WAR")]
public sealed class WroughtInWar : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Forge"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 1));
    }
}

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

/// <summary><c>AstralPulse</c> (3 star cost): BespokeCardMirrors attacks every enemy twice.</summary>
[FlashContent("ASTRAL_PULSE")]
public sealed class AstralPulse : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: 2);
}
