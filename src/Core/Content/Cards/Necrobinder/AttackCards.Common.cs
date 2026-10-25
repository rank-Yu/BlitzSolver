using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Necrobinder;

/// <summary>
/// <c>BlightStrike</c>: attack, then apply Doom equal to the attack's blocked + unblocked damage against
/// the target (<c>CorePowerSupport</c>'s BlightStrike case, which sums the play's damage entries;
/// the single-target attack makes the last damage call that total).
/// </summary>
[BlitzContent("BLIGHT_STRIKE")]
public sealed class BlightStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        if (x.NeedsChoice || x.Target == BlitzLimits.NoOwner)
            return;
        int damage = x.LastDamageTotal;
        if (damage > 0)
            BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.DoomPower>(), damage,
                BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("POKE")]
public sealed class Poke : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage"];

    public override void OnPlay(ref BlitzExec x) => NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
}

/// <summary>
/// <c>Snap</c>: Osty attack, then select a hand card without the local Retain keyword and add Retain
/// (<c>CardChoiceSupport</c>'s ApplyRetain Spec and CardCmd.ApplyKeyword).
/// </summary>
[BlitzContent("SNAP")]
public sealed class Snap : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage"];

    public override void OnPlay(ref BlitzExec x) => NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[BlitzLimits.Cards];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (!x.B.Cards[index].HasKeyword(BlitzKeywords.Retain))
                options[count++] = index;
        }
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.ApplyRetain, BlitzPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        if (chosen > 0)
            BlitzPiles.At(ref x.B, BlitzPile.Hand, selected[0]).Keywords |= BlitzKeywords.Retain;
    }
}

[BlitzContent("FLATTEN")]
public sealed class Flatten : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage"];

    public override void OnPlay(ref BlitzExec x) => NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));

    // AfterAttackMirrors.HandleFlatten: after any attack by the owner's Osty, every Flatten copy in
    // combat gets SetThisTurn(0) (an absolute zero EndOfTurn modifier, one per trigger).
    public override void AfterAttack(ref BlitzExec x, BlitzListener self, byte attacker)
    {
        if (attacker != BlitzLimits.OstyOwner)
            return;
        // AfterAttackMirrors.HandleFlatten runs once per Flatten listener and zeroes that listener's
        // own card via State.FindCard(card): one modifier per Flatten copy, not per copy per listener.
        if ((uint)self.Index >= (uint)x.B.CardCount)
            return;
        ref BlitzCard listenerCard = ref x.B.Cards[self.Index];
        if (listenerCard.Id != Id)
            return;
        BlitzKernel.AddEnergyCostModifier(ref listenerCard, 0, BlitzCostType.Absolute,
            BlitzCostExpiration.EndOfTurn, reduceOnly: false);
    }

    // SimulatedCombatState.CardEventHistory.AfterCardEnteredCombat: a Flatten that enters combat after
    // Osty attacked this turn gets the same zero-cost modifier.
    public override void AfterEnteredCombat(ref BlitzExec x, BlitzPile pile, int index)
    {
        if (x.B.Counter(BlitzCounter.OstyAttacksThisTurn) <= 0)
            return;
        ref BlitzCard card = ref BlitzPiles.At(ref x.B, pile, index);
        BlitzKernel.AddEnergyCostModifier(ref card, 0, BlitzCostType.Absolute, BlitzCostExpiration.EndOfTurn,
            reduceOnly: false);
    }

}

/// <summary><c>Fear</c>: player attack, then Vulnerable on the target (<c>CorePowerSupport</c>).</summary>
[BlitzContent("FEAR")]
public sealed class Fear : BlitzCardDef
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

[BlitzContent("SOW")]
public sealed class Sow : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("REAP")]
public sealed class Reap : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("DEFILE")]
public sealed class Defile : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

/// <summary>
/// <c>Graveblast</c>: attack, then move one discard-pile card to hand
/// (<c>CardChoiceSupport</c>'s MoveToHand spec over the discard pile).
/// </summary>
[BlitzContent("GRAVEBLAST")]
public sealed class Graveblast : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.MoveToHand, BlitzPile.Discard, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen > 0)
            BlitzKernel.MoveSelected(ref x.B, BlitzPile.Discard, selected[..chosen], BlitzPile.Hand,
                BlitzPilePosition.Bottom);
    }
}

/// <summary>
/// <c>Reave</c>: attack, then the Cards variable's Souls (upgraded with the card) at random draw-pile
/// positions (<c>CardEffectSpecRegistry</c>'s Reave completion).
/// </summary>
[BlitzContent("REAVE")]
public sealed class Reave : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0), ("SOUL", 1)];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = (int)NecrobinderContent.Var(ref x, 1);
        bool upgraded = x.Card.Upgrade > 0;
        for (int index = 0; index < count; index++)
        {
            BlitzCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded);
            if (!BlitzKernel.AddGeneratedCard(ref x, soul, BlitzPile.Draw, BlitzPilePosition.Random)
                || x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>SculptingStrike</c>: attack, then add the local Ethereal keyword to a selected hand card
/// (<c>CardChoiceSupport</c>'s ApplyEthereal spec + <c>CardCmd.ApplyKeyword</c>).
/// </summary>
[BlitzContent("SCULPTING_STRIKE")]
public sealed class SculptingStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[BlitzLimits.Cards];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (!x.B.Cards[index].HasKeyword(BlitzKeywords.Ethereal))
                options[count++] = index;
        }
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.ApplyEthereal, BlitzPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        if (chosen > 0)
            BlitzPiles.At(ref x.B, BlitzPile.Hand, selected[0]).Keywords |= BlitzKeywords.Ethereal;
    }
}

/// <summary>
/// <c>DrainPower</c>: single-target attack, then upgrade up to Cards random upgradable discard cards
/// (<c>CardSelectionCardMirrors.DrainPowerOnPlay</c>: filtered list, full Fisher-Yates with the
/// CombatCardSelection stream, take the first Cards).
/// </summary>
[BlitzContent("DRAIN_POWER")]
public sealed class DrainPower : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int discardStart = BlitzPiles.Start(x.B, BlitzPile.Discard);
        int discardCount = x.B.DiscardCount;
        Span<int> order = stackalloc int[discardCount];
        int candidates = 0;
        for (int index = 0; index < discardCount; index++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[discardStart + index];
            BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if (card.Upgrade < stats.MaxUpgradeLevel)
                order[candidates++] = index;
        }
        if (candidates == 0)
            return;
        ref BlitzRngState rng = ref x.B.RngStream(BlitzRngStream.CombatCardSelection);
        for (int n = candidates; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (order[k], order[n]) = (order[n], order[k]);
        }
        int take = Math.Min((int)NecrobinderContent.Var(ref x, 1), candidates);
        // CombatPredictionSimulator.Upgrade is a no-op while the combat is ending; the random
        // selection (and its RNG draws) still happen first. A killing DrainPower must not upgrade.
        if (BlitzKernel.IsEnding(x.B))
            return;
        for (int index = 0; index < take; index++)
            BlitzKernel.UpgradeCard(x.R, ref x.B.Cards[discardStart + order[index]]);
    }
}
