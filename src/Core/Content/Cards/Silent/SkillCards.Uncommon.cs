using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

[BlitzContent("ACROBATICS")]
public sealed class Acrobatics : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref BlitzExec x)
    {
        // GetSpec: Acrobatics → Discard, Hand, 1, every hand card.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Discard, BlitzPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        BlitzKernel.DiscardAndDraw(ref x, BlitzPile.Hand, selected[..chosen], 0);
    }
}

[BlitzContent("CALCULATED_GAMBLE")]
public sealed class CalculatedGamble : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        // CardDrawCardMirrors.CalculatedGambleOnPlay: DiscardAndDraw(hand, hand.Length), where the
        // draw count is the hand size captured before the discard.
        int hand = x.B.HandCount;
        BlitzKernel.DiscardAllInHand(ref x, hand);
    }
}

[BlitzContent("REFLEX")]
public sealed class Reflex : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 0), fromHandDraw: false);
}

[BlitzContent("TACTICIAN")]
public sealed class Tactician : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 0));
}

[BlitzContent("HIDDEN_DAGGERS")]
public sealed class HiddenDaggers : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Shivs"];

    // CardOnPlaySupport has no OnPlay for HiddenDaggers: everything is the choice and its
    // ApplyPostChoiceEffects.
    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        // GetSpec: Discard, Hand, Cards, every hand card; BuildRequiredEmptyChoice asks an empty
        // Discard choice when the hand is empty. ApplyPostChoiceEffects then adds Shivs Shivs,
        // upgraded when this card is upgraded.
        int cards = (int)BlitzCardEffects.Var(x, 0);
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[x.B.HandCount];
        int chosen;
        if (options.IsEmpty)
        {
            // The existing engine's nested-choice sink applies BuildRequiredEmptyChoice without
            // asking during an auto-play (ResolveNestedCardChoice); a manual play asks it.
            if (x.CurrentPlayIsAutoPlay)
            {
                chosen = 0;
            }
            else if (!BlitzKernel.ChooseCardsInRange(ref x, BlitzChoiceEffect.Discard, BlitzPile.Hand, options, 0, 0,
                         selected, out chosen))
            {
                return;
            }
        }
        else if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Discard, BlitzPile.Hand, options, cards,
                     selected, out chosen))
        {
            return;
        }
        BlitzKernel.DiscardAndDraw(ref x, BlitzPile.Hand, selected[..chosen], 0);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)BlitzCardEffects.Var(x, 1), upgraded: x.Card.Upgrade != 0);
    }
}

[BlitzContent("HAND_TRICK")]
public sealed class HandTrick : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref BlitzExec x)
    {
        // GetSpec: ApplySly, Hand, 1, hand Skills that are not already Sly this turn. With no
        // option, CardChoiceSupport.Spec returns null and nothing is asked.
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[index];
            if (BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Skill || BlitzKernel.IsSlyThisTurn(x.B, card))
                continue;
            options[count++] = index;
        }
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.ApplySly, BlitzPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        for (int index = 0; index < chosen; index++)
        {
            x.B.Cards[selected[index]].Flags |= BlitzCardFlags.SlyThisTurn;
        }
    }
}

[BlitzContent("ESCAPE_PLAN")]
public sealed class EscapePlan : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    // CardDrawCardMirrors.EscapePlanOnPlay: draw one card; gain block only when the single drawn card
    // is a Skill.
    public override void OnPlay(ref BlitzExec x)
    {
        if (!BlitzKernel.TryDrawOne(ref x, fromHandDraw: false, out BlitzCard card))
            return;
        if (BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Skill)
            return;
        BlitzCardEffects.GeneralBlock(ref x, 0);
    }
}

[BlitzContent("EXPERTISE")]
public sealed class Expertise : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // CardDrawCardMirrors.ExpertiseOnPlay: draw Cards, then give every drawn card single-turn Retain.
    public override void OnPlay(ref BlitzExec x)
    {
        int count = (int)BlitzCardEffects.Var(x, 0);
        for (int index = 0; index < count; index++)
        {
            int before = x.B.HandCount;
            if (!BlitzKernel.TryDrawOne(ref x, fromHandDraw: false, out BlitzCard card))
                return;
            if (x.B.HandCount != before + 1)
                continue;
            ref BlitzCard drawn = ref x.B.Cards[x.B.HandCount - 1];
            if (BlitzFingerprint.Equal(drawn, card))
            {
                drawn.Flags |= BlitzCardFlags.RetainThisTurn;
            }
        }
    }
}

[BlitzContent("BUBBLE_BUBBLE")]
public sealed class BubbleBubble : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    // CardOnPlaySupport.BubbleBubble: only when the target already has Poison.
    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        if (BlitzKernel.GetAmount(x.B, x.Target, BlitzRegistry.PowerId<Powers.PoisonPower>()) <= 0)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.PoisonPower>(), 0);
    }
}

[BlitzContent("BOUNCING_FLASK")]
public sealed class BouncingFlask : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower", "Repeat"];

    // CardOnPlaySupport.Batch042.ApplyBouncingFlask: Repeat times, a random living hittable enemy
    // (CombatTargets NextItem), applying Poison; an empty candidate list stops without consuming RNG.
    public override void OnPlay(ref BlitzExec x)
    {
        int repeats = (int)BlitzCardEffects.Var(x, 1);
        ushort poison = BlitzRegistry.PowerId<Powers.PoisonPower>();
        int amount = (int)BlitzCardEffects.Var(x, 0);
        Span<byte> candidates = stackalloc byte[BlitzLimits.Enemies];
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            int count = 0;
            for (int position = 0; position < x.B.RosterCount; position++)
            {
                byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
                if (BlitzKernel.IsHittable(x.B, owner))
                    candidates[count++] = owner;
            }
            if (count == 0)
                break;
            byte target = candidates[x.B.RngStream(BlitzRngStream.CombatTargets).NextInt(0, count)];
            BlitzKernel.ApplyPower(ref x, target, poison, amount, BlitzLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("HAZE")]
public sealed class Haze : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower", "WeakPower"];

    // CardOnPlaySupport.Haze: Poison to every hittable enemy, then Weak to every hittable enemy. The
    // existing case applies both passes without resolving the power amount changes in between.
    public override void OnPlay(ref BlitzExec x)
    {
        ushort poison = BlitzRegistry.PowerId<Powers.PoisonPower>();
        ushort weak = BlitzRegistry.PowerId<Powers.WeakPower>();
        int poisonAmount = (int)BlitzCardEffects.Var(x, 0);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (!BlitzKernel.IsHittable(x.B, owner))
                continue;
            BlitzKernel.ApplyPower(ref x, owner, poison, poisonAmount, BlitzLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
        int weakAmount = (int)BlitzCardEffects.Var(x, 1);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (!BlitzKernel.IsHittable(x.B, owner))
                continue;
            BlitzKernel.ApplyPower(ref x, owner, weak, weakAmount, BlitzLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("EXPOSE")]
public sealed class Expose : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    // CardOnPlaySupport.Expose: lose all block as Move block damage, clear Artifact, then Vulnerable.
    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        ref BlitzCreature target = ref x.B.Creature(x.Target);
        if (target.Block > 0)
            BlitzKernel.DamageBlock(ref target, target.Block, BlitzValueProps.Move);
        if (BlitzKernel.GetAmount(x.B, x.Target, BlitzRegistry.PowerId<Powers.ArtifactPower>()) > 0)
            BlitzKernel.SetPowerAmount(ref x.B, x.Target, BlitzRegistry.PowerId<Powers.ArtifactPower>(), 0);
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.VulnerablePower>(), 0);
    }
}

[BlitzContent("FLECHETTES")]
public sealed class Flechettes : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    // CalculatedVarSpecRegistry.Flechettes: CalculatedHits counts Skill cards in hand.
    public override void OnPlay(ref BlitzExec x)
    {
        int skills = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[index];
            if (BlitzKernel.CardType(x.B, x.R, card) == BlitzCardType.Skill)
                skills++;
        }
        int hits = (int)(BlitzCardEffects.Var(x, 1) + BlitzCardEffects.Var(x, 2) * skills);
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), hits);
    }
}

[BlitzContent("UP_MY_SLEEVE")]
public sealed class UpMySleeve : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        SilentContent.GenerateShivs(ref x, (int)BlitzCardEffects.Var(x, 0), upgraded: false);
        if (x.NeedsChoice)
            return;
        // UpMySleeve.cs: playedCard.EnergyCost.AddThisCombat(-1). The played card is the last entry of
        // the play pile while its OnPlay runs.
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("袖中藏刃没有正在打出的卡。");
        BlitzKernel.AddEnergyCostModifier(ref BlitzPiles.At(ref x.B, BlitzPile.Play, position), -1,
            BlitzCostType.Relative, BlitzCostExpiration.EndOfCombat, reduceOnly: false);
    }
}

[BlitzContent("LEG_SWEEP")]
public sealed class LegSweep : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "WeakPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[BlitzContent("BLUR")]
public sealed class Blur : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Blur"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.BlurPower>(), 1);
    }
}

[BlitzContent("SIDESTEP")]
public sealed class Sidestep : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    // CorePowerSupport.ApplyCardPowers.Sidestep: AddEnergyNextTurn (EnergyNextTurnPower amount).
    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.EnergyNextTurnPower>(), 0);
}

/// <summary>
/// <c>Mirage</c>: CalculatedBlock = CalculationBase + CalculationExtra × the poison on every living
/// enemy (<c>CalculatedVarSpecRegistry</c>'s Mirage multiplier). The card gains that block as a Move
/// card block; upgraded Mirage drops Exhaust, which is part of the rule-table keyword set.
/// </summary>
[BlitzContent("MIRAGE")]
public sealed class Mirage : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref BlitzExec x)
    {
        int poison = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (x.B.Creature(owner).IsAlive)
                poison += BlitzKernel.GetAmount(x.B, owner, BlitzRegistry.PowerId<PoisonPower>());
        }
        decimal block = BlitzCardEffects.Var(x, 0) + BlitzCardEffects.Var(x, 1) * poison;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, block, BlitzValueProps.Move, fromCard: true);
    }
}
