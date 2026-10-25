using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Ironclad;

[BlitzContent("CONFLAGRATION")]
public sealed class Conflagration : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: (int)BlitzCardEffects.Var(x, 1));
}

[BlitzContent("FEED")]
public sealed class Feed : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "MaxHp"];

    public override void OnPlay(ref BlitzExec x)
    {
        byte target = x.Target;
        // CorePowerSupport captures targetDeathTriggersFatal before the card's effects run. MinionPower
        // is the only supported power that rejects a fatal kill, and it does so by making the owner a
        // secondary enemy, which IsPrimaryEnemy reports.
        bool fatal = target != BlitzLimits.NoOwner && BlitzKernel.IsPrimaryEnemy(x.B, target);
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice || target == BlitzLimits.NoOwner || x.B.Creature(target).IsAlive)
            return;
        // CorePowerSupport.Feed only gains MaxHp after a fatal hit and only when every power of the
        // target lets its owner's death trigger fatal (MinionPower is the supported such power).
        if (!fatal)
            return;
        BlitzKernel.GainMaxHp(ref x, BlitzLimits.PlayerOwner, BlitzCardEffects.Var(x, 1));
        BlitzGrowthRewards.Add(ref x.B, BlitzGrowthSource.Feed);
    }
}

[BlitzContent("PACTS_END")]
public sealed class PactsEnd : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        // BespokeCardMirrors.PactsEndOnPlay only attacks when the exhaust pile already has the
        // CardsVar amount; the card itself stays in the play pile during this check.
        if (x.B.ExhaustCount < (int)BlitzCardEffects.Var(x, 1))
            return;
        BlitzCardEffects.GeneralAttack(ref x, 0);
    }
}

[BlitzContent("MANGLE")]
public sealed class Mangle : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StrengthLoss"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice || x.Target == BlitzLimits.NoOwner)
            return;
        // CardEffectSpecRegistry: ApplyTemporaryStrengthLoss<ManglePower>.
        BlitzKernel.ApplyTemporaryStrength(ref x, x.Target, BlitzRegistry.PowerId<Powers.ManglePower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner, sign: -1);
    }
}

[BlitzContent("FIEND_FIRE")]
public sealed class FiendFire : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        int cardCount = x.B.HandCount;
        for (int index = 0; index < cardCount; index++)
        {
            if (BlitzKernel.IsOverOrEnding(x.B))
                return;
            BlitzKernel.Exhaust(ref x, BlitzPile.Hand, 0);
            if (x.NeedsChoice)
                return;
        }
        BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: cardCount);
    }
}

[BlitzContent("TEAR_ASUNDER")]
public sealed class TearAsunder : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CalculatedHits = 1 + History DamageReceived entries for the owner with unblocked damage.
    public override void OnPlay(ref BlitzExec x)
    {
        int hits = 1 + x.B.Counter(BlitzCounter.PlayerUnblockedDamageEvents);
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), hits);
    }
}

[BlitzContent("THRASH")]
public sealed class Thrash : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // The native card adds ModifyDamage's decimal result to DynamicVars.Damage.BaseValue and to
    // the private ExtraDamage field, and AfterDowngraded re-adds ExtraDamage. Blitz keeps the
    // exact delta in the battle's decimal table; downgrade leaves the table untouched.
    public override bool UsesDecimalDamageVar => true;

    public override void OnPlay(ref BlitzExec x)
    {
        // Thrash.cs: two hits, then a random Attack in hand is read for its damage, that value is
        // added to this card's Damage (unmodified by Strength/Vulnerable because target is null in
        // the native ModifyDamage call), and the selected card is exhausted.
        BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: 2);
        if (x.NeedsChoice)
            return;
        int selected = PickRandomHandAttack(ref x);
        if (selected < 0)
            return;
        BlitzCard victim = x.B.Cards[selected];
        BlitzCardDef def = BlitzRegistry.Card(victim.Id);
        if (!def.TryGetCalculatedDamage(ref x, victim, BlitzLimits.NoOwner, out decimal damage))
            damage = NamedDamage(ref x, victim, def);
        damage = BlitzHooks.ModifyDamage(ref x, BlitzLimits.NoOwner, BlitzLimits.PlayerOwner, damage,
            BlitzValueProps.Move, hasCardSource: true, victim);
        BlitzCardEffects.AddCurrentCardVar(ref x, 0, damage);
        BlitzKernel.Exhaust(ref x, BlitzPile.Hand, selected);
    }

    private static int PickRandomHandAttack(ref BlitzExec x)
    {
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (BlitzKernel.CardType(x.B, x.R, x.B.Cards[index]) == BlitzCardType.Attack)
                count++;
        }
        if (count == 0)
            return -1;
        int pick = x.B.RngStream(BlitzRngStream.CombatCardSelection).NextInt(0, count);
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (BlitzKernel.CardType(x.B, x.R, x.B.Cards[index]) != BlitzCardType.Attack)
                continue;
            if (pick-- == 0)
                return index;
        }
        throw new InvalidOperationException("Thrash 的手牌随机选择没有落到任何候选。");
    }

    private static decimal NamedDamage(ref BlitzExec x, in BlitzCard card, BlitzCardDef def)
    {
        BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
        for (int index = 0; index < def.VarNames.Count && index < stats.Vars.Length; index++)
        {
            string name = def.VarNames[index];
            if (name is not ("Damage" or "OstyDamage"))
                continue;
            return stats.Vars[index] + BlitzDecimalVars.Delta(in x.B, in card, index);
        }
        throw new BlitzUnsupportedException($"Thrash 选中的攻击 {def.NativeId} 没有 Damage 变量。");
    }
}
