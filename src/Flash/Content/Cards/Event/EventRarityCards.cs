using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Event;

// Event-rarity cards (EC batch). They enter a run deck through events and only ever appear as deck
// cards; none of them is in a combat generation pool (native CardFactory.FilterForCombat excludes
// CardRarity.Event). Existing-engine paths:
// - ByrdSwoop/Clash/Exterminate/Peck/RipAndTear/Rebound/Squash: CardOnPlayInferrer / strict recipe
//   attacks, plus the CorePowerSupport.ApplyCardPowers cases (Rebound's CardEffectSpecRegistry entry,
//   Squash's Vulnerable).
// - Caltrops/FeedingFrenzy/Outmaneuver/HelloWorld/ForbiddenGrimoire/WraithForm/Apparition: the
//   CorePowerSupport, CardPowerOnPlaySupport and CardOnPlaySupport compensation switches.
// - Distraction/Metamorphosis/Abundance: CardGenerationCardMirrors (distinct pool sampling).
// - DualWield/Wish: CardChoiceSupport.GetSpec + CardChoiceResolution.
// - ToricToughness: CorePowerSupport.ApplyCardPowers' AddPowerInstance + SetBlock.
// - Entrench/Maul: BespokeCardMirrors.
// The rule compiler reads each declared VarNames entry from the canonical card, so the numbers are
// the native dynamic variables.

[FlashContent("BYRD_SWOOP")]
public sealed class ByrdSwoop : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // Native OnPlay is a plain DamageCmd.Attack; CardOnPlayInferrer infers the attack recipe.
    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("CALTROPS")]
public sealed class Caltrops : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["ThornsPower"];

    // CorePowerSupport.ApplyCardPowers: combat.Apply<ThornsPower>(owner, ThornsPower, owner).
    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.ThornsPower>(), 0);
}

[FlashContent("CLASH")]
public sealed class Clash : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardIsPlayableMirrors.HandleClash: playable only while every hand card is an Attack.
    public override bool IsPlayable(in FlashBattle battle, FlashRules rules, in FlashCard card)
    {
        for (int index = 0; index < battle.HandCount; index++)
        {
            ref readonly FlashCard handCard = ref battle.Cards[index];
            if (FlashKernel.CardType(battle, rules, handCard) != FlashCardType.Attack)
                return false;
        }
        return true;
    }

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("DISTRACTION")]
public sealed class Distraction : FlashCardDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    // CardGenerationCardMirrors.DistractionOnPlay: one distinct unlocked character Skill at upgrade 0,
    // free this turn, added to hand. No choice is recorded.
    public override void OnPlay(ref FlashExec x)
    {
        Span<FlashCard> generated = stackalloc FlashCard[1];
        int count = FlashCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, FlashCardType.Skill, 1,
            generated);
        for (int index = 0; index < count; index++)
        {
            FlashKernel.MakeFreeThisTurn(x.R, ref generated[index]);
            if (!FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Hand))
                return;
        }
    }
}

[FlashContent("DUAL_WIELD")]
public sealed class DualWield : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // CardChoiceSupport.GetSpec: Duplicate, Hand, 1, Attack or Power hand cards.
    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            FlashCardType type = FlashKernel.CardType(x.B, x.R, card);
            if (type is FlashCardType.Attack or FlashCardType.Power)
                options[count++] = index;
        }
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Duplicate, FlashPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        if (chosen == 0)
            return;
        FlashCard original = FlashPiles.At(ref x.B, FlashPile.Hand, selected[0]);
        int copies = (int)FlashCardEffects.Var(x, 0);
        for (int index = 0; index < copies; index++)
        {
            FlashCard clone = FlashKernel.CloneCard(original);
            if (!FlashKernel.AddGeneratedCard(ref x, clone, FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("ENLIGHTENMENT")]
public sealed class Enlightenment : FlashCardDef
{
    // CardPileOnPlaySupport.ReduceHandCosts: every hand card is set to 1, reduce-only; upgraded for
    // the whole combat, otherwise this turn or until played.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCostExpiration expiration = x.Card.Upgrade > 0
            ? FlashCostExpiration.EndOfCombat
            : FlashCostExpiration.EndOfTurnOrWhenPlayed;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            FlashKernel.AddEnergyCostModifier(ref x.B.Cards[index], 1, FlashCostType.Absolute,
                expiration, reduceOnly: true);
        }
    }
}

[FlashContent("ENTRENCH")]
public sealed class Entrench : FlashCardDef
{
    // BespokeCardMirrors.EntrenchOnPlay: gain block equal to the current block with
    // Unpowered | Move and the card as source.
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.B.Creature(FlashLimits.PlayerOwner).Block,
            FlashValueProps.Unpowered | FlashValueProps.Move, fromCard: true);
}

[FlashContent("EXTERMINATE")]
public sealed class Exterminate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), (int)FlashCardEffects.Var(x, 1));
}

[FlashContent("FEEDING_FRENZY")]
public sealed class FeedingFrenzy : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    // CardPowerOnPlaySupport.FeedingFrenzy: Strength first, then the temporary power. The temporary
    // power's native BeforeApplied is not run by combat.Apply, so this is not ApplyTemporaryStrength.
    public override void OnPlay(ref FlashExec x)
    {
        int amount = (int)FlashCardEffects.Var(x, 0);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.StrengthPower>(), amount, FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.FeedingFrenzyPower>(), amount, FlashLimits.PlayerOwner);
    }
}

[FlashContent("HELLO_WORLD")]
public sealed class HelloWorld : FlashCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["HELLO_WORLD_POWER"];

    // CardPowerOnPlaySupport.HelloWorld: combat.Apply<HelloWorldPower>(owner, 1, owner).
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.HelloWorldPower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("METAMORPHOSIS")]
public sealed class Metamorphosis : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAttacks];

    // CardGenerationCardMirrors.MetamorphosisOnPlay: GetUnlockedCharacterAttacksForCombat (with
    // replacement, one NextItem per card), each free this combat, into the draw pile at a random
    // position in generation order.
    public override void OnPlay(ref FlashExec x)
    {
        int count = (int)FlashCardEffects.Var(x, 0);
        if (count > FlashLimits.Cards)
            throw new FlashCapacityException("Metamorphosis.Generated", FlashLimits.Cards);
        Span<FlashCard> generated = stackalloc FlashCard[Math.Max(0, count)];
        int sampled = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.CharacterAttacks, count,
            distinct: false, FlashRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < sampled; index++)
        {
            FlashKernel.MakeFreeThisCombat(x.R, ref generated[index]);
            if (!FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Draw, FlashPilePosition.Random))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("OUTMANEUVER")]
public sealed class Outmaneuver : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    // CardPowerOnPlaySupport.Outmaneuver: combat.Apply<EnergyNextTurnPower>(owner, Energy, owner).
    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(), 0);
}

[FlashContent("PECK")]
public sealed class Peck : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), (int)FlashCardEffects.Var(x, 1));
}

[FlashContent("REBOUND")]
public sealed class Rebound : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<string> AppliedPowers => ["REBOUND_POWER"];

    // Inferred attack; CardEffectSpecRegistry.Rebound applies ReboundPower 1 afterwards.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.ReboundPower>(), 1, FlashLimits.PlayerOwner);
    }
}

[FlashContent("RIP_AND_TEAR")]
public sealed class RipAndTear : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // RandomTargetAttackCardMirrors.RipAndTearOnPlay: two hits on random opponents (each hit rerolls).
    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hitCount: 2);
}

[FlashContent("SQUASH")]
public sealed class Squash : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

    // Inferred attack; CorePowerSupport.ApplyCardPowers applies Vulnerable after the enemy-death pass.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[FlashContent("STACK")]
public sealed class Stack : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "CalculationExtra", "CalculatedBlock"];

    // CalculatedVarSpecRegistry's Stack multiplier is the discard pile count:
    // CalculatedBlock = CalculationBase + CalculationExtra × count; GeneralCardMirrors' block recipe
    // uses CalculatedBlock.Props (Move) with the card as source.
    public override void OnPlay(ref FlashExec x)
    {
        decimal block = FlashCardEffects.Var(x, 0)
            + FlashCardEffects.Var(x, 1) * x.B.DiscardCount;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, block, FlashValueProps.Move, fromCard: true);
    }
}

[FlashContent("TORIC_TOUGHNESS")]
public sealed class ToricToughness : FlashCardDef
{
    // The card creates an instanced power whose rows can be removed in the same listener pass; the
    // root must enable the occurrence-tracked power passes (as the Orbit card does).
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Turns", "Block"];

    // CorePowerSupport.ApplyCardPowers.ToricToughness: gain the Block variable's block, then add an
    // instanced ToricToughnessPower with Turns and store the settled block amount on that instance.
    public override void OnPlay(ref FlashExec x)
    {
        decimal gained = FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner,
            FlashCardEffects.Var(x, 1), FlashValueProps.Move, fromCard: true);
        if (x.NeedsChoice)
            return;
        int turns = (int)FlashCardEffects.Var(x, 0);
        ushort id = FlashRegistry.PowerId<Powers.ToricToughnessPower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, id, turns, FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int index = FlashKernel.FindLastPower(x.B, FlashLimits.PlayerOwner, id);
        if (index < 0)
            return;
        // SimulatedCombatState.AddPowerInstance sets _target = owner (unlike Apply<T>).
        x.B.Powers[index].Target = FlashLimits.PlayerOwner;
        FlashKernel.SetInstanceBlockValue(ref x.B, index, gained);
    }
}

/// <summary>
/// <c>MadScience</c> (TinkerTime event): the saved <c>TinkerTimeType</c> selects Attack (damage),
/// Skill (block) or Power (one of three setup powers), and <c>TinkerTimeRider</c> adds the rider
/// chosen by the event. Existing engine: <c>CardGenerationCardMirrors.MadScienceOnPlay</c> reads
/// both saved properties; the rule table is canonical Attack/No rider, so Flash packs the two saved
/// enum values into <see cref="FlashCard.Private"/> and projects them at admission.
/// </summary>
[FlashContent("MAD_SCIENCE")]
public sealed class MadScience : FlashCardDef
{
    // Native TinkerTime.RiderEffect values.
    public const int RiderNone = 0;
    public const int RiderSapping = 1;
    public const int RiderViolence = 2;
    public const int RiderChoking = 3;
    public const int RiderEnergized = 4;
    public const int RiderWisdom = 5;
    public const int RiderChaos = 6;
    public const int RiderExpertise = 7;
    public const int RiderCurious = 8;
    public const int RiderImprovement = 9;

    public override IReadOnlyList<string> VarNames =>
    [
        "Damage", "Block", "SappingWeak", "SappingVulnerable", "ViolenceHits", "ChokingDamage",
        "EnergizedEnergy", "WisdomCards", "ExpertiseStrength", "ExpertiseDexterity", "CuriousReduction",
    ];

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override bool HasDynamicCardType => true;

    public override int PrivateRider(in FlashCard card) => RiderOf(card);

    public static short Encode(int type, int rider) => checked((short)((byte)type | ((byte)rider << 8)));

    public static FlashCardType TypeOf(in FlashCard card) => (FlashCardType)(card.Private & 0xFF);

    public static int RiderOf(in FlashCard card) => (card.Private >> 8) & 0xFF;

    public override FlashCardType CardType(in FlashBattle battle, FlashRules rules, in FlashCard card)
        => TypeOf(card);

    public override FlashTargetType TargetType(in FlashBattle battle, FlashRules rules, in FlashCard card)
        => TypeOf(card) == FlashCardType.Attack ? FlashTargetType.AnyEnemy : FlashTargetType.Self;

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardType type = TypeOf(x.Card);
        int rider = RiderOf(x.Card);
        switch (type)
        {
            case FlashCardType.Attack:
                FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0),
                    rider == RiderViolence ? (int)FlashCardEffects.Var(x, 4) : 1);
                if (x.NeedsChoice)
                    return;
                ApplyRider(ref x, rider);
                break;
            case FlashCardType.Skill:
                FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, FlashCardEffects.Var(x, 1),
                    FlashValueProps.Move, fromCard: true);
                if (x.NeedsChoice)
                    return;
                ApplyRider(ref x, rider);
                break;
            case FlashCardType.Power:
                ApplyPowerRider(ref x, rider);
                break;
            default:
                throw new FlashUnsupportedException($"MadScience 的 TinkerTimeType {(int)type} 不是可打出的类型。");
        }
    }

    private static void ApplyRider(ref FlashExec x, int rider)
    {
        switch (rider)
        {
            case RiderSapping:
                FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 2);
                if (x.NeedsChoice)
                    return;
                FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 3);
                break;
            case RiderChoking:
                FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.StranglePower>(), 5);
                break;
            case RiderEnergized:
                FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 6));
                break;
            case RiderWisdom:
                FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 7), fromHandDraw: false);
                break;
            case RiderChaos:
            {
                Span<FlashCard> generated = stackalloc FlashCard[1];
                int count = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.CharacterAll, 1,
                    distinct: true, FlashRngStream.CombatCardGeneration, generated);
                if (count == 0)
                    return;
                FlashKernel.MakeFreeThisTurn(x.R, ref generated[0]);
                FlashKernel.AddGeneratedCard(ref x, generated[0], FlashPile.Hand);
                break;
            }
        }
    }

    private static void ApplyPowerRider(ref FlashExec x, int rider)
    {
        switch (rider)
        {
            case RiderExpertise:
                FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.StrengthPower>(), (int)FlashCardEffects.Var(x, 8),
                    FlashLimits.PlayerOwner);
                if (x.NeedsChoice)
                    return;
                FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.DexterityPower>(), (int)FlashCardEffects.Var(x, 9),
                    FlashLimits.PlayerOwner);
                break;
            case RiderCurious:
                FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.CuriousPower>(), (int)FlashCardEffects.Var(x, 10),
                    FlashLimits.PlayerOwner);
                break;
            case RiderImprovement:
            {
                // CardGenerationCardMirrors.MadScienceOnPlay records the growth reward only when the
                // ImprovementPower amount actually increased.
                int applied = FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.ImprovementPower>(), 1, FlashLimits.PlayerOwner);
                if (applied > 0)
                    FlashGrowthRewards.Add(ref x.B, FlashGrowthSource.MadScience);
                break;
            }
        }
    }
}
