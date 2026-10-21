using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

[FlashContent("ACROBATICS")]
public sealed class Acrobatics : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: Acrobatics → Discard, Hand, 1, every hand card.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Discard, FlashPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        FlashKernel.DiscardAndDraw(ref x, FlashPile.Hand, selected[..chosen], 0);
    }
}

[FlashContent("CALCULATED_GAMBLE")]
public sealed class CalculatedGamble : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        // CardDrawCardMirrors.CalculatedGambleOnPlay: DiscardAndDraw(hand, hand.Length), where the
        // draw count is the hand size captured before the discard.
        int hand = x.B.HandCount;
        FlashKernel.DiscardAllInHand(ref x, hand);
    }
}

[FlashContent("REFLEX")]
public sealed class Reflex : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);
}

[FlashContent("TACTICIAN")]
public sealed class Tactician : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
}

[FlashContent("HIDDEN_DAGGERS")]
public sealed class HiddenDaggers : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Shivs"];

    // CardOnPlaySupport has no OnPlay for HiddenDaggers: everything is the choice and its
    // ApplyPostChoiceEffects.
    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: Discard, Hand, Cards, every hand card; BuildRequiredEmptyChoice asks an empty
        // Discard choice when the hand is empty. ApplyPostChoiceEffects then adds Shivs Shivs,
        // upgraded when this card is upgraded.
        int cards = (int)FlashCardEffects.Var(x, 0);
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
            else if (!FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.Discard, FlashPile.Hand, options, 0, 0,
                         selected, out chosen))
            {
                return;
            }
        }
        else if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Discard, FlashPile.Hand, options, cards,
                     selected, out chosen))
        {
            return;
        }
        FlashKernel.DiscardAndDraw(ref x, FlashPile.Hand, selected[..chosen], 0);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)FlashCardEffects.Var(x, 1), upgraded: x.Card.Upgrade != 0);
    }
}

[FlashContent("HAND_TRICK")]
public sealed class HandTrick : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: ApplySly, Hand, 1, hand Skills that are not already Sly this turn. With no
        // option, CardChoiceSupport.Spec returns null and nothing is asked.
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Skill || FlashKernel.IsSlyThisTurn(x.B, card))
                continue;
            options[count++] = index;
        }
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.ApplySly, FlashPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        for (int index = 0; index < chosen; index++)
        {
            x.B.Cards[selected[index]].Flags |= FlashCardFlags.SlyThisTurn;
        }
    }
}

[FlashContent("ESCAPE_PLAN")]
public sealed class EscapePlan : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    // CardDrawCardMirrors.EscapePlanOnPlay: draw one card; gain block only when the single drawn card
    // is a Skill.
    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashKernel.TryDrawOne(ref x, fromHandDraw: false, out FlashCard card))
            return;
        if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Skill)
            return;
        FlashCardEffects.GeneralBlock(ref x, 0);
    }
}

[FlashContent("EXPERTISE")]
public sealed class Expertise : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // CardDrawCardMirrors.ExpertiseOnPlay: draw Cards, then give every drawn card single-turn Retain.
    public override void OnPlay(ref FlashExec x)
    {
        int count = (int)FlashCardEffects.Var(x, 0);
        for (int index = 0; index < count; index++)
        {
            int before = x.B.HandCount;
            if (!FlashKernel.TryDrawOne(ref x, fromHandDraw: false, out FlashCard card))
                return;
            if (x.B.HandCount != before + 1)
                continue;
            ref FlashCard drawn = ref x.B.Cards[x.B.HandCount - 1];
            if (FlashFingerprint.Equal(drawn, card))
            {
                drawn.Flags |= FlashCardFlags.RetainThisTurn;
            }
        }
    }
}

[FlashContent("BUBBLE_BUBBLE")]
public sealed class BubbleBubble : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    // CardOnPlaySupport.BubbleBubble: only when the target already has Poison.
    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        if (FlashKernel.GetAmount(x.B, x.Target, FlashRegistry.PowerId<Powers.PoisonPower>()) <= 0)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.PoisonPower>(), 0);
    }
}

[FlashContent("BOUNCING_FLASK")]
public sealed class BouncingFlask : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower", "Repeat"];

    // CardOnPlaySupport.Batch042.ApplyBouncingFlask: Repeat times, a random living hittable enemy
    // (CombatTargets NextItem), applying Poison; an empty candidate list stops without consuming RNG.
    public override void OnPlay(ref FlashExec x)
    {
        int repeats = (int)FlashCardEffects.Var(x, 1);
        ushort poison = FlashRegistry.PowerId<Powers.PoisonPower>();
        int amount = (int)FlashCardEffects.Var(x, 0);
        Span<byte> candidates = stackalloc byte[FlashLimits.Enemies];
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            int count = 0;
            for (int position = 0; position < x.B.RosterCount; position++)
            {
                byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
                if (FlashKernel.IsHittable(x.B, owner))
                    candidates[count++] = owner;
            }
            if (count == 0)
                break;
            byte target = candidates[x.B.RngStream(FlashRngStream.CombatTargets).NextInt(0, count)];
            FlashKernel.ApplyPower(ref x, target, poison, amount, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("HAZE")]
public sealed class Haze : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower", "WeakPower"];

    // CardOnPlaySupport.Haze: Poison to every hittable enemy, then Weak to every hittable enemy. The
    // existing case applies both passes without resolving the power amount changes in between.
    public override void OnPlay(ref FlashExec x)
    {
        ushort poison = FlashRegistry.PowerId<Powers.PoisonPower>();
        ushort weak = FlashRegistry.PowerId<Powers.WeakPower>();
        int poisonAmount = (int)FlashCardEffects.Var(x, 0);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyPower(ref x, owner, poison, poisonAmount, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
        int weakAmount = (int)FlashCardEffects.Var(x, 1);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyPower(ref x, owner, weak, weakAmount, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("EXPOSE")]
public sealed class Expose : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    // CardOnPlaySupport.Expose: lose all block as Move block damage, clear Artifact, then Vulnerable.
    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        ref FlashCreature target = ref x.B.Creature(x.Target);
        if (target.Block > 0)
            FlashKernel.DamageBlock(ref target, target.Block, FlashValueProps.Move);
        if (FlashKernel.GetAmount(x.B, x.Target, FlashRegistry.PowerId<Powers.ArtifactPower>()) > 0)
            FlashKernel.SetPowerAmount(ref x.B, x.Target, FlashRegistry.PowerId<Powers.ArtifactPower>(), 0);
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 0);
    }
}

[FlashContent("FLECHETTES")]
public sealed class Flechettes : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    // CalculatedVarSpecRegistry.Flechettes: CalculatedHits counts Skill cards in hand.
    public override void OnPlay(ref FlashExec x)
    {
        int skills = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            if (FlashKernel.CardType(x.B, x.R, card) == FlashCardType.Skill)
                skills++;
        }
        int hits = (int)(FlashCardEffects.Var(x, 1) + FlashCardEffects.Var(x, 2) * skills);
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hits);
    }
}

[FlashContent("UP_MY_SLEEVE")]
public sealed class UpMySleeve : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        SilentContent.GenerateShivs(ref x, (int)FlashCardEffects.Var(x, 0), upgraded: false);
        if (x.NeedsChoice)
            return;
        // UpMySleeve.cs: playedCard.EnergyCost.AddThisCombat(-1). The played card is the last entry of
        // the play pile while its OnPlay runs.
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("袖中藏刃没有正在打出的卡。");
        FlashKernel.AddEnergyCostModifier(ref FlashPiles.At(ref x.B, FlashPile.Play, position), -1,
            FlashCostType.Relative, FlashCostExpiration.EndOfCombat, reduceOnly: false);
    }
}

[FlashContent("LEG_SWEEP")]
public sealed class LegSweep : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[FlashContent("BLUR")]
public sealed class Blur : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Blur"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.BlurPower>(), 1);
    }
}

[FlashContent("SIDESTEP")]
public sealed class Sidestep : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    // CorePowerSupport.ApplyCardPowers.Sidestep: AddEnergyNextTurn (EnergyNextTurnPower amount).
    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(), 0);
}

/// <summary>
/// <c>Mirage</c>: CalculatedBlock = CalculationBase + CalculationExtra × the poison on every living
/// enemy (<c>CalculatedVarSpecRegistry</c>'s Mirage multiplier). The card gains that block as a Move
/// card block; upgraded Mirage drops Exhaust, which is part of the rule-table keyword set.
/// </summary>
[FlashContent("MIRAGE")]
public sealed class Mirage : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref FlashExec x)
    {
        int poison = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (x.B.Creature(owner).IsAlive)
                poison += FlashKernel.GetAmount(x.B, owner, FlashRegistry.PowerId<PoisonPower>());
        }
        decimal block = FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * poison;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, block, FlashValueProps.Move, fromCard: true);
    }
}
