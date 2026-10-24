using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

/// <summary>
/// <c>BeatIntoShape</c>: the inferred attack, then forge
/// <c>CalculatedForge - Results.Count × CalculationExtra</c> (CardOnPlaySupport). The calculated
/// multiplier is the player's powered attack hits on the target this turn; the attack's own hits
/// cancel out, so the forge is CalculationBase + CalculationExtra × hits before this attack.
/// </summary>
[FlashContent("BEAT_INTO_SHAPE")]
public sealed class BeatIntoShape : FlashCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [(FlashKernel.SovereignBladeId, 0)];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            throw new FlashUnsupportedException("BeatIntoShape 缺少目标，无法读取按目标命中数。");
        int hits = FlashKernel.PoweredAttackHitsByPlayer(x.B, x.Target);
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        decimal forge = FlashCardEffects.Var(x, 1) + FlashCardEffects.Var(x, 2) * hits;
        if (forge < int.MinValue || forge > int.MaxValue || forge != decimal.Truncate(forge))
            throw new FlashUnsupportedException($"BeatIntoShape 的锻造量 {forge} 无法用整数表示。");
        FlashKernel.Forge(ref x, (int)forge);
    }
}

/// <summary>
/// <c>MakeItSo</c>: the inferred attack; <c>AfterCardPlayedLate</c> returns this card to hand when the
/// number of finished Skill plays this turn is a multiple of the Cards variable
/// (<c>AfterCardPlayedMirrors.HandleMakeItSo</c>; vanilla records CardPlayFinished before the late
/// pass, so the current Skill is included).
/// </summary>
[FlashContent("MAKE_IT_SO")]
public sealed class MakeItSo : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void AfterCardPlayedLate(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Kind != FlashListenerKind.Card)
            return;
        if (FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Skill)
            return;
        int position = self.Index;
        if (position >= x.B.CardCount)
            return;
        int handEnd = x.B.HandCount;
        if (position < handEnd)
            return;
        int cards = (int)x.R.Card(x.B.Cards[position].Id, x.B.Cards[position].Upgrade).Vars[1];
        if (cards <= 0)
            throw new FlashUnsupportedException("MakeItSo 的 Cards 变量不为正。");
        int finished = x.B.Counter(FlashCounter.SkillCardsPlayedThisTurn) + 1;
        if (finished % cards != 0)
            return;
        FlashPile pile = FlashPiles.PileOf(x.B, position, out int index);
        FlashKernel.MoveCard(ref x.B, pile, index, FlashPile.Hand);
    }
}

/// <summary>
/// <c>CrashLanding</c> (Rare, attack every enemy): CardEffectSpecRegistry then adds one Debris to the
/// hand for each empty hand slot (<c>GetMaxHandSize - Hand.Count</c>).
/// </summary>
[FlashContent("CRASH_LANDING")]
public sealed class CrashLanding : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [(DebrisId, 0)];

    private const string DebrisId = "DEBRIS";

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = x.B.MaxHandSize - x.B.HandCount;
        if (count <= 0)
            return;
        if (!FlashRegistry.TryCard(DebrisId, out ushort debris))
            throw new FlashUnsupportedException("坠毁需要 DEBRIS 的 Flash 定义。");
        for (int index = 0; index < count; index++)
        {
            if (!FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, debris), FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary><c>SevenStars</c> (7 star cost): every enemy takes the Repeat variable's number of hits.</summary>
[FlashContent("SEVEN_STARS")]
public sealed class SevenStars : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: (int)FlashCardEffects.Var(x, 1));
}

/// <summary>
/// <c>HeirloomHammer</c>: the inferred attack, then choose one colorless hand card and add Repeat
/// clones of it to hand (<c>CardChoiceResolution.DuplicateSelectedCard</c>: <c>CreateClone</c> per
/// copy, bottom of the hand, fixed result kind).
/// </summary>
[FlashContent("HEIRLOOM_HAMMER")]
public sealed class HeirloomHammer : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            if (x.R.Card(card.Id, card.Upgrade).IsColorless)
                options[count++] = index;
        }
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Duplicate, FlashPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        if (chosen == 0)
            return;
        FlashCard original = FlashPiles.At(ref x.B, FlashPile.Hand, selected[0]);
        int copies = (int)FlashCardEffects.Var(x, 1);
        for (int index = 0; index < copies; index++)
        {
            FlashCard clone = FlashKernel.CloneCard(original);
            if (!FlashKernel.AddGeneratedCard(ref x, clone, FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
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

/// <summary><c>Bombardment</c>: the inferred recipe is a plain single-target attack.</summary>
[FlashContent("BOMBARDMENT")]
public sealed class Bombardment : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
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
