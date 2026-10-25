using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[BlitzContent("CLAW")]
public sealed class Claw : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    // CardEffectSpecRegistry.Claw: attack, then every Claw in combat gains Increase damage.
    // ExtraDamageFromClawPlays is carried in Private (ProjectPrivate) so DowngradeCard can restore it.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int increase = (int)BlitzCardEffects.Var(x, 1);
        if (increase == 0)
            return;
        if (!BlitzRegistry.TryCard("CLAW", out ushort claw))
            return;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref BlitzCard card = ref x.B.Cards[position];
            if (card.Id != claw)
                continue;
            AddIncrease(ref card, increase);
        }
    }

    public override void ProjectPrivate(ref BlitzCard card) => card.Private = card.Var0;

    public override void AfterDowngraded(ref BlitzBattle battle, int position)
    {
        // Claw.AfterDowngraded: DynamicVars.Damage.BaseValue += ExtraDamageFromClawPlays.
        battle.Cards[position].Var0 = battle.Cards[position].Private;
    }

    private static void AddIncrease(ref BlitzCard card, int increase)
    {
        if (increase > short.MaxValue - card.Var0 || increase > short.MaxValue - card.Private)
            throw new BlitzUnsupportedException("利爪的伤害累加超出卡牌私有计数范围。");
        card.Var0 = (short)(card.Var0 + increase);
        card.Private = (short)(card.Private + increase);
    }
}

[BlitzContent("FOCUSED_STRIKE")]
public sealed class FocusedStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "FocusPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardEffectSpecRegistry's FocusedStrike → ApplyTemporaryFocus<FocusedStrikePower>.
        BlitzKernel.ApplyTemporaryFocus(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.FocusedStrikePower>(), (int)BlitzCardEffects.Var(x, 1),
            BlitzLimits.PlayerOwner, sign: 1);
    }
}

[BlitzContent("MOMENTUM_STRIKE")]
public sealed class MomentumStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry: AttackSingle, then playedCard.MutablePreview.EnergyCost.SetThisCombat(0).
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.AddEnergyCostModifier(ref BlitzKernel.CurrentPlayCard(ref x), 0,
            BlitzCostType.Absolute, BlitzCostExpiration.EndOfCombat, reduceOnly: false);
    }
}

[BlitzContent("BARRAGE")]
public sealed class Barrage : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // GeneralAttackOnPlay reads CalculatedHits = orb count.
    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), x.B.OrbCount);
}

[BlitzContent("SWEEPING_BEAM")]
public sealed class SweepingBeam : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

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
}

[BlitzContent("BEAM_CELL")]
public sealed class BeamCell : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

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
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[BlitzContent("COLD_SNAP")]
public sealed class ColdSnap : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("FROST_ORB"));
    }
}

[BlitzContent("BALL_LIGHTNING")]
public sealed class BallLightning : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("LIGHTNING_ORB"));
    }
}

[BlitzContent("GO_FOR_THE_EYES")]
public sealed class GoForTheEyes : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

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
        // CardEffectSpecRegistry.GoForTheEyes: only against an enemy whose current move attacks.
        if (x.Target != BlitzLimits.NoOwner && BlitzKernel.EnemyIntendsToAttack(x.B, x.R, x.Target))
            BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[BlitzContent("COMPILE_DRIVER")]
public sealed class CompileDriver : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardDrawCardMirrors.CompileDriverOnPlay: attack, then draw one per distinct orb id.
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
        BlitzKernel.Draw(ref x, BlitzKernel.DistinctOrbCount(x.B), fromHandDraw: false);
    }
}

[BlitzContent("UPROAR")]
public sealed class Uproar : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        // CardSelectionCardMirrors.UproarOnPlay: two hits, then auto-play the first attack of a stable
        // shuffle of the draw pile (playable attacks first, then every attack).
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), 2);
        if (x.NeedsChoice)
            return;
        int drawCount = x.B.DrawCount;
        if (drawCount == 0)
            return;
        Span<BlitzCard> buffer = stackalloc BlitzCard[drawCount];
        Span<int> origins = stackalloc int[drawCount];
        int eligible = 0;
        for (int index = 0; index < drawCount; index++)
        {
            BlitzCard card = x.B.Cards[x.B.HandCount + index];
            if (BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Attack)
                continue;
            if ((card.Keywords & BlitzKeywords.Unplayable) != 0)
                continue;
            buffer[eligible] = card;
            origins[eligible] = index;
            eligible++;
        }
        int count = eligible;
        if (count == 0)
        {
            for (int index = 0; index < drawCount; index++)
            {
                BlitzCard card = x.B.Cards[x.B.HandCount + index];
                if (BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Attack)
                    continue;
                buffer[count] = card;
                origins[count] = index;
                count++;
            }
        }
        if (count == 0)
            return;
        BlitzCardSort.SortWithOrigins(buffer[..count], origins[..count], x.R);
        ref BlitzRngState rng = ref x.B.RngStream(BlitzRngStream.Shuffle);
        for (int n = count; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (buffer[k], buffer[n]) = (buffer[n], buffer[k]);
            (origins[k], origins[n]) = (origins[n], origins[k]);
        }
        int position = origins[0];
        if (position >= x.B.DrawCount
            || !BlitzFingerprint.Equal(BlitzPiles.At(ref x.B, BlitzPile.Draw, position), buffer[0]))
        {
            throw new BlitzUnsupportedException("Uproar 选中的抽牌在结算前离开了抽牌堆。");
        }
        BlitzKernel.AutoPlay(ref x, BlitzPile.Draw, position);
    }
}

[BlitzContent("GUNK_UP")]
public sealed class GunkUp : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SLIMED", 0)];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), (int)BlitzCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        if (!BlitzRegistry.TryCard("SLIMED", out ushort id))
            throw new BlitzUnsupportedException("黏液需要 SLIMED 的 Blitz 定义。");
        BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CreateCard(x.R, id), BlitzPile.Discard);
    }
}
