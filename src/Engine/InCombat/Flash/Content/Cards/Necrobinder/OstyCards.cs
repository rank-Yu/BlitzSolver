namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Necrobinder;

// Osty attack cards. Existing engine paths: the general attack recipe with `FromOsty` (the
// CardTag.OstyAttack branch of GeneralCardMirrors.GeneralAttackOnPlay), then the card's compensation:
// CorePowerSupport (HighFive's Vulnerable to all), CardEffectSpecRegistry (SicEmPower), or
// BespokeCardMirrors.SacrificeOnPlay. Calculated multipliers are CalculatedVarSpecRegistry entries.

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

/// <summary>
/// <c>HighFive</c>: playable only while Osty is alive; Osty hits all enemies, then every hittable enemy
/// gains Vulnerable (<c>CorePowerSupport</c>'s HighFive case).
/// </summary>
[FlashContent("HIGH_FIVE")]
public sealed class HighFive : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "VulnerablePower"];

    public override bool IsPlayable(in FlashBattle battle, FlashRules rules, in FlashCard card)
        => NecrobinderContent.HasOsty(battle);

    public override void OnPlay(ref FlashExec x)
    {
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToAllEnemies(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

/// <summary>
/// <c>BoneShards</c>: Osty hits all enemies, the player gains block, then the pet is killed with force
/// (<c>CardEffectSpecRegistry</c>'s BoneShards completion, after the inferred attack/block mirror).
/// </summary>
[FlashContent("BONE_SHARDS")]
public sealed class BoneShards : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "Block"];

    public override void OnPlay(ref FlashExec x)
    {
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        FlashCardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice || !NecrobinderContent.HasOsty(x.B))
            return;
        Span<byte> osty = stackalloc byte[1];
        osty[0] = FlashLimits.OstyOwner;
        FlashKernel.Kill(ref x, osty, force: true);
    }
}

[FlashContent("RIGHT_HAND_HAND")]
public sealed class RightHandHand : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "Energy"];

    public override void OnPlay(ref FlashExec x) => NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));

    // RightHandHand.AfterCardPlayedLate: the listener does NOT check that the played card is this copy;
    // after any play that spent at least the Energy variable, each RightHandHand copy sitting in the
    // discard pile goes back to hand (AfterCardPlayedMirrors.HandleRightHandHand).
    public override void AfterCardPlayedLate(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if ((uint)self.Index >= (uint)x.B.CardCount)
            return;
        ref readonly FlashCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        if (x.CurrentPlayEnergyValue < x.R.Card(card.Id, card.Upgrade).Vars[1])
            return;
        int start = FlashPiles.Start(x.B, FlashPile.Discard);
        int count = FlashPiles.Count(x.B, FlashPile.Discard);
        if (self.Index < start || self.Index >= start + count)
            return;
        FlashKernel.MoveCard(ref x.B, FlashPile.Discard, self.Index - start, FlashPile.Hand,
            FlashPilePosition.Bottom);
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

/// <summary>
/// <c>Rattle</c>: Osty attack whose hit count is the CalculatedHits variable,
/// CalculationBase + CalculationExtra × (1 + Osty's attacks this turn).
/// </summary>
[FlashContent("RATTLE")]
public sealed class Rattle : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        decimal multiplier = 1 + x.B.Counter(FlashCounter.OstyAttacksThisTurn);
        int hits = (int)NecrobinderContent.Calculated(ref x, 1, 2, multiplier);
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0), hits);
    }
}

/// <summary>
/// <c>Squeeze</c>: Osty attack whose calculated damage is CalculationBase + ExtraDamage × the number of
/// other OstyAttack cards in the combat piles (<c>CalculatedVarSpecRegistry.Squeeze</c>).
/// </summary>
[FlashContent("SQUEEZE")]
public sealed class Squeeze : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        int others = NecrobinderContent.CountOstyAttackCards(x.B, x.R) - 1;
        decimal damage = NecrobinderContent.Calculated(ref x, 0, 1, others);
        NecrobinderContent.OstyAttack(ref x, damage);
    }
}

/// <summary>
/// <c>Protector</c>: Osty attack whose calculated damage is CalculationBase + ExtraDamage × Osty's max HP
/// (<c>CalculatedVarSpecRegistry.Protector</c>).
/// </summary>
[FlashContent("PROTECTOR")]
public sealed class Protector : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        decimal damage = NecrobinderContent.Calculated(ref x, 0, 1, x.B.Osty.MaxHp);
        NecrobinderContent.OstyAttack(ref x, damage);
    }
}

/// <summary>
/// <c>Sacrifice</c>: block for three times Osty's max HP, then kill the pet with force
/// (<c>BespokeCardMirrors.SacrificeOnPlay</c>). Nothing happens while Osty is missing.
/// </summary>
[FlashContent("SACRIFICE")]
public sealed class Sacrifice : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        decimal block = x.B.Osty.MaxHp * 3;
        Span<byte> osty = stackalloc byte[1];
        osty[0] = FlashLimits.OstyOwner;
        FlashKernel.Kill(ref x, osty, force: true);
        if (x.NeedsChoice)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, block, FlashValueProps.Move, fromCard: true);
    }
}

/// <summary>
/// <c>SicEm</c>: Osty attack, then apply SicEmPower to the target even when the pet was missing
/// (<c>CardEffectSpecRegistry</c>'s SicEm target power).
/// </summary>
[FlashContent("SIC_EM")]
public sealed class SicEm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "SicEmPower"];

    public override void OnPlay(ref FlashExec x)
    {
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.SicEmPower>(), 1);
    }
}
