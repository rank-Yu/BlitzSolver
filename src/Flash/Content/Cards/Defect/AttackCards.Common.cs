using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Defect;

[FlashContent("CLAW")]
public sealed class Claw : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    // CardEffectSpecRegistry.Claw: attack, then every Claw in combat gains Increase damage.
    // ExtraDamageFromClawPlays is carried in Private (ProjectPrivate) so DowngradeCard can restore it.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int increase = (int)FlashCardEffects.Var(x, 1);
        if (increase == 0)
            return;
        if (!FlashRegistry.TryCard("CLAW", out ushort claw))
            return;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref FlashCard card = ref x.B.Cards[position];
            if (card.Id != claw)
                continue;
            AddIncrease(ref card, increase);
        }
    }

    public override void ProjectPrivate(ref FlashCard card) => card.Private = card.Var0;

    public override void AfterDowngraded(ref FlashBattle battle, int position)
    {
        // Claw.AfterDowngraded: DynamicVars.Damage.BaseValue += ExtraDamageFromClawPlays.
        battle.Cards[position].Var0 = battle.Cards[position].Private;
    }

    private static void AddIncrease(ref FlashCard card, int increase)
    {
        if (increase > short.MaxValue - card.Var0 || increase > short.MaxValue - card.Private)
            throw new FlashUnsupportedException("利爪的伤害累加超出卡牌私有计数范围。");
        card.Var0 = (short)(card.Var0 + increase);
        card.Private = (short)(card.Private + increase);
    }
}

[FlashContent("FOCUSED_STRIKE")]
public sealed class FocusedStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "FocusPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardEffectSpecRegistry's FocusedStrike → ApplyTemporaryFocus<FocusedStrikePower>.
        FlashKernel.ApplyTemporaryFocus(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.FocusedStrikePower>(), (int)FlashCardEffects.Var(x, 1),
            FlashLimits.PlayerOwner, sign: 1);
    }
}

[FlashContent("MOMENTUM_STRIKE")]
public sealed class MomentumStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry: AttackSingle, then playedCard.MutablePreview.EnergyCost.SetThisCombat(0).
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.AddEnergyCostModifier(ref FlashKernel.CurrentPlayCard(ref x), 0,
            FlashCostType.Absolute, FlashCostExpiration.EndOfCombat, reduceOnly: false);
    }
}

[FlashContent("BARRAGE")]
public sealed class Barrage : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // GeneralAttackOnPlay reads CalculatedHits = orb count.
    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), x.B.OrbCount);
}

[FlashContent("SWEEPING_BEAM")]
public sealed class SweepingBeam : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

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
}

[FlashContent("BEAM_CELL")]
public sealed class BeamCell : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[FlashContent("COLD_SNAP")]
public sealed class ColdSnap : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("FROST_ORB"));
    }
}

[FlashContent("BALL_LIGHTNING")]
public sealed class BallLightning : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("LIGHTNING_ORB"));
    }
}

[FlashContent("GO_FOR_THE_EYES")]
public sealed class GoForTheEyes : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        // CardEffectSpecRegistry.GoForTheEyes: only against an enemy whose current move attacks.
        if (x.Target != FlashLimits.NoOwner && FlashKernel.EnemyIntendsToAttack(x.B, x.R, x.Target))
            FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[FlashContent("COMPILE_DRIVER")]
public sealed class CompileDriver : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardDrawCardMirrors.CompileDriverOnPlay: attack, then draw one per distinct orb id.
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
        FlashKernel.Draw(ref x, FlashKernel.DistinctOrbCount(x.B), fromHandDraw: false);
    }
}

[FlashContent("UPROAR")]
public sealed class Uproar : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        // CardSelectionCardMirrors.UproarOnPlay: two hits, then auto-play the first attack of a stable
        // shuffle of the draw pile (playable attacks first, then every attack).
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), 2);
        if (x.NeedsChoice)
            return;
        int drawCount = x.B.DrawCount;
        if (drawCount == 0)
            return;
        Span<FlashCard> buffer = stackalloc FlashCard[drawCount];
        Span<int> origins = stackalloc int[drawCount];
        int eligible = 0;
        for (int index = 0; index < drawCount; index++)
        {
            FlashCard card = x.B.Cards[x.B.HandCount + index];
            if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Attack)
                continue;
            if ((card.Keywords & FlashKeywords.Unplayable) != 0)
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
                FlashCard card = x.B.Cards[x.B.HandCount + index];
                if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Attack)
                    continue;
                buffer[count] = card;
                origins[count] = index;
                count++;
            }
        }
        if (count == 0)
            return;
        FlashCardSort.SortWithOrigins(buffer[..count], origins[..count], x.R);
        ref FlashRngState rng = ref x.B.RngStream(FlashRngStream.Shuffle);
        for (int n = count; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (buffer[k], buffer[n]) = (buffer[n], buffer[k]);
            (origins[k], origins[n]) = (origins[n], origins[k]);
        }
        int position = origins[0];
        if (position >= x.B.DrawCount
            || !FlashFingerprint.Equal(FlashPiles.At(ref x.B, FlashPile.Draw, position), buffer[0]))
        {
            throw new FlashUnsupportedException("Uproar 选中的抽牌在结算前离开了抽牌堆。");
        }
        FlashKernel.AutoPlay(ref x, FlashPile.Draw, position);
    }
}

[FlashContent("GUNK_UP")]
public sealed class GunkUp : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SLIMED", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), (int)FlashCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryCard("SLIMED", out ushort id))
            throw new FlashUnsupportedException("黏液需要 SLIMED 的 Flash 定义。");
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, id), FlashPile.Discard);
    }
}
