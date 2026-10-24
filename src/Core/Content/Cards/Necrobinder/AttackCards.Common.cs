using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

/// <summary>
/// <c>BlightStrike</c>: attack, then apply Doom equal to the attack's blocked + unblocked damage against
/// the target (<c>CorePowerSupport</c>'s BlightStrike case, which sums the play's damage entries;
/// the single-target attack makes the last damage call that total).
/// </summary>
[FlashContent("BLIGHT_STRIKE")]
public sealed class BlightStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        int damage = x.LastDamageTotal;
        if (damage > 0)
            FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.DoomPower>(), damage,
                FlashLimits.PlayerOwner);
    }
}

[FlashContent("POKE")]
public sealed class Poke : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage"];

    public override void OnPlay(ref FlashExec x) => NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
}

/// <summary>
/// <c>Snap</c>: Osty attack, then select a hand card without the local Retain keyword and add Retain
/// (<c>CardChoiceSupport</c>'s ApplyRetain Spec and CardCmd.ApplyKeyword).
/// </summary>
[FlashContent("SNAP")]
public sealed class Snap : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage"];

    public override void OnPlay(ref FlashExec x) => NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[FlashLimits.Cards];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (!x.B.Cards[index].HasKeyword(FlashKeywords.Retain))
                options[count++] = index;
        }
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.ApplyRetain, FlashPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        if (chosen > 0)
            FlashPiles.At(ref x.B, FlashPile.Hand, selected[0]).Keywords |= FlashKeywords.Retain;
    }
}

[FlashContent("FLATTEN")]
public sealed class Flatten : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage"];

    public override void OnPlay(ref FlashExec x) => NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));

    // AfterAttackMirrors.HandleFlatten: after any attack by the owner's Osty, every Flatten copy in
    // combat gets SetThisTurn(0) (an absolute zero EndOfTurn modifier, one per trigger).
    public override void AfterAttack(ref FlashExec x, FlashListener self, byte attacker)
    {
        if (attacker != FlashLimits.OstyOwner)
            return;
        // AfterAttackMirrors.HandleFlatten runs once per Flatten listener and zeroes that listener's
        // own card via State.FindCard(card): one modifier per Flatten copy, not per copy per listener.
        if ((uint)self.Index >= (uint)x.B.CardCount)
            return;
        ref FlashCard listenerCard = ref x.B.Cards[self.Index];
        if (listenerCard.Id != Id)
            return;
        FlashKernel.AddEnergyCostModifier(ref listenerCard, 0, FlashCostType.Absolute,
            FlashCostExpiration.EndOfTurn, reduceOnly: false);
    }

    // SimulatedCombatState.CardEventHistory.AfterCardEnteredCombat: a Flatten that enters combat after
    // Osty attacked this turn gets the same zero-cost modifier.
    public override void AfterEnteredCombat(ref FlashExec x, FlashPile pile, int index)
    {
        if (x.B.Counter(FlashCounter.OstyAttacksThisTurn) <= 0)
            return;
        ref FlashCard card = ref FlashPiles.At(ref x.B, pile, index);
        FlashKernel.AddEnergyCostModifier(ref card, 0, FlashCostType.Absolute, FlashCostExpiration.EndOfTurn,
            reduceOnly: false);
    }

}

/// <summary><c>Fear</c>: player attack, then Vulnerable on the target (<c>CorePowerSupport</c>).</summary>
[FlashContent("FEAR")]
public sealed class Fear : FlashCardDef
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

[FlashContent("SOW")]
public sealed class Sow : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("REAP")]
public sealed class Reap : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("DEFILE")]
public sealed class Defile : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

/// <summary>
/// <c>Graveblast</c>: attack, then move one discard-pile card to hand
/// (<c>CardChoiceSupport</c>'s MoveToHand spec over the discard pile).
/// </summary>
[FlashContent("GRAVEBLAST")]
public sealed class Graveblast : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Discard, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen > 0)
            FlashKernel.MoveSelected(ref x.B, FlashPile.Discard, selected[..chosen], FlashPile.Hand,
                FlashPilePosition.Bottom);
    }
}

/// <summary>
/// <c>Reave</c>: attack, then the Cards variable's Souls (upgraded with the card) at random draw-pile
/// positions (<c>CardEffectSpecRegistry</c>'s Reave completion).
/// </summary>
[FlashContent("REAVE")]
public sealed class Reave : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0), ("SOUL", 1)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = (int)NecrobinderContent.Var(ref x, 1);
        bool upgraded = x.Card.Upgrade > 0;
        for (int index = 0; index < count; index++)
        {
            FlashCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded);
            if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Draw, FlashPilePosition.Random)
                || x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>SculptingStrike</c>: attack, then add the local Ethereal keyword to a selected hand card
/// (<c>CardChoiceSupport</c>'s ApplyEthereal spec + <c>CardCmd.ApplyKeyword</c>).
/// </summary>
[FlashContent("SCULPTING_STRIKE")]
public sealed class SculptingStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[FlashLimits.Cards];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (!x.B.Cards[index].HasKeyword(FlashKeywords.Ethereal))
                options[count++] = index;
        }
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.ApplyEthereal, FlashPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        if (chosen > 0)
            FlashPiles.At(ref x.B, FlashPile.Hand, selected[0]).Keywords |= FlashKeywords.Ethereal;
    }
}

/// <summary>
/// <c>DrainPower</c>: single-target attack, then upgrade up to Cards random upgradable discard cards
/// (<c>CardSelectionCardMirrors.DrainPowerOnPlay</c>: filtered list, full Fisher-Yates with the
/// CombatCardSelection stream, take the first Cards).
/// </summary>
[FlashContent("DRAIN_POWER")]
public sealed class DrainPower : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int discardStart = FlashPiles.Start(x.B, FlashPile.Discard);
        int discardCount = x.B.DiscardCount;
        Span<int> order = stackalloc int[discardCount];
        int candidates = 0;
        for (int index = 0; index < discardCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[discardStart + index];
            FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if (card.Upgrade < stats.MaxUpgradeLevel)
                order[candidates++] = index;
        }
        if (candidates == 0)
            return;
        ref FlashRngState rng = ref x.B.RngStream(FlashRngStream.CombatCardSelection);
        for (int n = candidates; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (order[k], order[n]) = (order[n], order[k]);
        }
        int take = Math.Min((int)NecrobinderContent.Var(ref x, 1), candidates);
        // CombatPredictionSimulator.Upgrade is a no-op while the combat is ending; the random
        // selection (and its RNG draws) still happen first. A killing DrainPower must not upgrade.
        if (FlashKernel.IsEnding(x.B))
            return;
        for (int index = 0; index < take; index++)
            FlashKernel.UpgradeCard(x.R, ref x.B.Cards[discardStart + order[index]]);
    }
}
