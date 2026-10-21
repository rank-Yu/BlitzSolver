using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Ironclad;

[FlashContent("CONFLAGRATION")]
public sealed class Conflagration : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: (int)FlashCardEffects.Var(x, 1));
}

[FlashContent("FEED")]
public sealed class Feed : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "MaxHp"];

    public override void OnPlay(ref FlashExec x)
    {
        byte target = x.Target;
        // CorePowerSupport captures targetDeathTriggersFatal before the card's effects run. MinionPower
        // is the only supported power that rejects a fatal kill, and it does so by making the owner a
        // secondary enemy, which IsPrimaryEnemy reports.
        bool fatal = target != FlashLimits.NoOwner && FlashKernel.IsPrimaryEnemy(x.B, target);
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice || target == FlashLimits.NoOwner || x.B.Creature(target).IsAlive)
            return;
        // CorePowerSupport.Feed only gains MaxHp after a fatal hit and only when every power of the
        // target lets its owner's death trigger fatal (MinionPower is the supported such power).
        if (!fatal)
            return;
        FlashKernel.GainMaxHp(ref x, FlashLimits.PlayerOwner, FlashCardEffects.Var(x, 1));
        FlashGrowthRewards.Add(ref x.B, FlashGrowthSource.Feed);
    }
}

[FlashContent("PACTS_END")]
public sealed class PactsEnd : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        // BespokeCardMirrors.PactsEndOnPlay only attacks when the exhaust pile already has the
        // CardsVar amount; the card itself stays in the play pile during this check.
        if (x.B.ExhaustCount < (int)FlashCardEffects.Var(x, 1))
            return;
        FlashCardEffects.GeneralAttack(ref x, 0);
    }
}

[FlashContent("MANGLE")]
public sealed class Mangle : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StrengthLoss"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        // CardEffectSpecRegistry: ApplyTemporaryStrengthLoss<ManglePower>.
        FlashKernel.ApplyTemporaryStrength(ref x, x.Target, FlashRegistry.PowerId<Powers.ManglePower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner, sign: -1);
    }
}

[FlashContent("FIEND_FIRE")]
public sealed class FiendFire : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        int cardCount = x.B.HandCount;
        for (int index = 0; index < cardCount; index++)
        {
            if (FlashKernel.IsOverOrEnding(x.B))
                return;
            FlashKernel.Exhaust(ref x, FlashPile.Hand, 0);
            if (x.NeedsChoice)
                return;
        }
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: cardCount);
    }
}

[FlashContent("TEAR_ASUNDER")]
public sealed class TearAsunder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CalculatedHits = 1 + History DamageReceived entries for the owner with unblocked damage.
    public override void OnPlay(ref FlashExec x)
    {
        int hits = 1 + x.B.Counter(FlashCounter.PlayerUnblockedDamageEvents);
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hits);
    }
}

[FlashContent("THRASH")]
public sealed class Thrash : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // The native card adds ModifyDamage's decimal result to DynamicVars.Damage.BaseValue and to
    // the private ExtraDamage field, and AfterDowngraded re-adds ExtraDamage. Flash keeps the
    // exact delta in the battle's decimal table; downgrade leaves the table untouched.
    public override bool UsesDecimalDamageVar => true;

    public override void OnPlay(ref FlashExec x)
    {
        // Thrash.cs: two hits, then a random Attack in hand is read for its damage, that value is
        // added to this card's Damage (unmodified by Strength/Vulnerable because target is null in
        // the native ModifyDamage call), and the selected card is exhausted.
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: 2);
        if (x.NeedsChoice)
            return;
        int selected = PickRandomHandAttack(ref x);
        if (selected < 0)
            return;
        FlashCard victim = x.B.Cards[selected];
        FlashCardDef def = FlashRegistry.Card(victim.Id);
        if (!def.TryGetCalculatedDamage(ref x, victim, FlashLimits.NoOwner, out decimal damage))
            damage = NamedDamage(ref x, victim, def);
        damage = FlashHooks.ModifyDamage(ref x, FlashLimits.NoOwner, FlashLimits.PlayerOwner, damage,
            FlashValueProps.Move, hasCardSource: true, victim);
        FlashCardEffects.AddCurrentCardVar(ref x, 0, damage);
        FlashKernel.Exhaust(ref x, FlashPile.Hand, selected);
    }

    private static int PickRandomHandAttack(ref FlashExec x)
    {
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (FlashKernel.CardType(x.B, x.R, x.B.Cards[index]) == FlashCardType.Attack)
                count++;
        }
        if (count == 0)
            return -1;
        int pick = x.B.RngStream(FlashRngStream.CombatCardSelection).NextInt(0, count);
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (FlashKernel.CardType(x.B, x.R, x.B.Cards[index]) != FlashCardType.Attack)
                continue;
            if (pick-- == 0)
                return index;
        }
        throw new InvalidOperationException("Thrash 的手牌随机选择没有落到任何候选。");
    }

    private static decimal NamedDamage(ref FlashExec x, in FlashCard card, FlashCardDef def)
    {
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        for (int index = 0; index < def.VarNames.Count && index < stats.Vars.Length; index++)
        {
            string name = def.VarNames[index];
            if (name is not ("Damage" or "OstyDamage"))
                continue;
            return stats.Vars[index] + FlashDecimalVars.Delta(in x.B, in card, index);
        }
        throw new FlashUnsupportedException($"Thrash 选中的攻击 {def.NativeId} 没有 Damage 变量。");
    }
}
