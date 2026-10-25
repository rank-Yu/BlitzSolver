using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary>
/// <c>BeatIntoShape</c>: the inferred attack, then forge
/// <c>CalculatedForge - Results.Count × CalculationExtra</c> (CardOnPlaySupport). The calculated
/// multiplier is the player's powered attack hits on the target this turn; the attack's own hits
/// cancel out, so the forge is CalculationBase + CalculationExtra × hits before this attack.
/// </summary>
[BlitzContent("BEAT_INTO_SHAPE")]
public sealed class BeatIntoShape : BlitzCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [(BlitzKernel.SovereignBladeId, 0)];

    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            throw new BlitzUnsupportedException("BeatIntoShape 缺少目标，无法读取按目标命中数。");
        int hits = BlitzKernel.PoweredAttackHitsByPlayer(x.B, x.Target);
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        decimal forge = BlitzCardEffects.Var(x, 1) + BlitzCardEffects.Var(x, 2) * hits;
        if (forge < int.MinValue || forge > int.MaxValue || forge != decimal.Truncate(forge))
            throw new BlitzUnsupportedException($"BeatIntoShape 的锻造量 {forge} 无法用整数表示。");
        BlitzKernel.Forge(ref x, (int)forge);
    }
}

/// <summary>
/// <c>MakeItSo</c>: the inferred attack; <c>AfterCardPlayedLate</c> returns this card to hand when the
/// number of finished Skill plays this turn is a multiple of the Cards variable
/// (<c>AfterCardPlayedMirrors.HandleMakeItSo</c>; vanilla records CardPlayFinished before the late
/// pass, so the current Skill is included).
/// </summary>
[BlitzContent("MAKE_IT_SO")]
public sealed class MakeItSo : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);

    public override void AfterCardPlayedLate(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Kind != BlitzListenerKind.Card)
            return;
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Skill)
            return;
        int position = self.Index;
        if (position >= x.B.CardCount)
            return;
        int handEnd = x.B.HandCount;
        if (position < handEnd)
            return;
        int cards = (int)x.R.Card(x.B.Cards[position].Id, x.B.Cards[position].Upgrade).Vars[1];
        if (cards <= 0)
            throw new BlitzUnsupportedException("MakeItSo 的 Cards 变量不为正。");
        int finished = x.B.Counter(BlitzCounter.SkillCardsPlayedThisTurn) + 1;
        if (finished % cards != 0)
            return;
        BlitzPile pile = BlitzPiles.PileOf(x.B, position, out int index);
        BlitzKernel.MoveCard(ref x.B, pile, index, BlitzPile.Hand);
    }
}

/// <summary>
/// <c>CrashLanding</c> (Rare, attack every enemy): CardEffectSpecRegistry then adds one Debris to the
/// hand for each empty hand slot (<c>GetMaxHandSize - Hand.Count</c>).
/// </summary>
[BlitzContent("CRASH_LANDING")]
public sealed class CrashLanding : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [(DebrisId, 0)];

    private const string DebrisId = "DEBRIS";

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = x.B.MaxHandSize - x.B.HandCount;
        if (count <= 0)
            return;
        if (!BlitzRegistry.TryCard(DebrisId, out ushort debris))
            throw new BlitzUnsupportedException("坠毁需要 DEBRIS 的 Blitz 定义。");
        for (int index = 0; index < count; index++)
        {
            if (!BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CreateCard(x.R, debris), BlitzPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary><c>SevenStars</c> (7 star cost): every enemy takes the Repeat variable's number of hits.</summary>
[BlitzContent("SEVEN_STARS")]
public sealed class SevenStars : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: (int)BlitzCardEffects.Var(x, 1));
}

/// <summary>
/// <c>HeirloomHammer</c>: the inferred attack, then choose one colorless hand card and add Repeat
/// clones of it to hand (<c>CardChoiceResolution.DuplicateSelectedCard</c>: <c>CreateClone</c> per
/// copy, bottom of the hand, fixed result kind).
/// </summary>
[BlitzContent("HEIRLOOM_HAMMER")]
public sealed class HeirloomHammer : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[index];
            if (x.R.Card(card.Id, card.Upgrade).IsColorless)
                options[count++] = index;
        }
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Duplicate, BlitzPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        if (chosen == 0)
            return;
        BlitzCard original = BlitzPiles.At(ref x.B, BlitzPile.Hand, selected[0]);
        int copies = (int)BlitzCardEffects.Var(x, 1);
        for (int index = 0; index < copies; index++)
        {
            BlitzCard clone = BlitzKernel.CloneCard(original);
            if (!BlitzKernel.AddGeneratedCard(ref x, clone, BlitzPile.Hand))
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
[BlitzContent("COMET")]
public sealed class Comet : BlitzCardDef
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

/// <summary>
/// <c>DyingStar</c> (3 star cost, Ethereal): attack every enemy, then CardEffectSpecRegistry applies
/// DyingStarPower (a temporary Strength loss) to every hittable enemy in roster order.
/// </summary>
[BlitzContent("DYING_STAR")]
public sealed class DyingStar : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StrengthLoss"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        ushort power = BlitzRegistry.PowerId<Powers.DyingStarPower>();
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

/// <summary><c>Bombardment</c>: the inferred recipe is a plain single-target attack.</summary>
[BlitzContent("BOMBARDMENT")]
public sealed class Bombardment : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

/// <summary>
/// <c>HeavenlyDrill</c> (X energy cost): BespokeCardMirrors resolves the energy X value and doubles it
/// when it reaches the canonical Energy variable, then attacks once per hit.
/// </summary>
[BlitzContent("HEAVENLY_DRILL")]
public sealed class HeavenlyDrill : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Energy"];

    public override void OnPlay(ref BlitzExec x)
    {
        int hits = BlitzKernel.ResolveEnergyXValue(ref x);
        if (hits >= (int)BlitzCardEffects.Var(x, 1))
            hits *= 2;
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), hits);
    }
}
