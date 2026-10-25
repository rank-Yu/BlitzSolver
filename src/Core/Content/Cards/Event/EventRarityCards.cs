using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Event;

[BlitzContent("BYRD_SWOOP")]
public sealed class ByrdSwoop : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // Native OnPlay is a plain DamageCmd.Attack; CardOnPlayInferrer infers the attack recipe.
    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("CALTROPS")]
public sealed class Caltrops : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["ThornsPower"];

    // CorePowerSupport.ApplyCardPowers: combat.Apply<ThornsPower>(owner, ThornsPower, owner).
    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.ThornsPower>(), 0);
}

[BlitzContent("CLASH")]
public sealed class Clash : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardIsPlayableMirrors.HandleClash: playable only while every hand card is an Attack.
    public override bool IsPlayable(in BlitzBattle battle, BlitzRules rules, in BlitzCard card)
    {
        for (int index = 0; index < battle.HandCount; index++)
        {
            ref readonly BlitzCard handCard = ref battle.Cards[index];
            if (BlitzKernel.CardType(battle, rules, handCard) != BlitzCardType.Attack)
                return false;
        }
        return true;
    }

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("DISTRACTION")]
public sealed class Distraction : BlitzCardDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAll];

    // CardGenerationCardMirrors.DistractionOnPlay: one distinct unlocked character Skill at upgrade 0,
    // free this turn, added to hand. No choice is recorded.
    public override void OnPlay(ref BlitzExec x)
    {
        Span<BlitzCard> generated = stackalloc BlitzCard[1];
        int count = BlitzCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, BlitzCardType.Skill, 1,
            generated);
        for (int index = 0; index < count; index++)
        {
            BlitzKernel.MakeFreeThisTurn(x.R, ref generated[index]);
            if (!BlitzKernel.AddGeneratedCard(ref x, generated[index], BlitzPile.Hand))
                return;
        }
    }
}

[BlitzContent("DUAL_WIELD")]
public sealed class DualWield : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // CardChoiceSupport.GetSpec: Duplicate, Hand, 1, Attack or Power hand cards.
    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[index];
            BlitzCardType type = BlitzKernel.CardType(x.B, x.R, card);
            if (type is BlitzCardType.Attack or BlitzCardType.Power)
                options[count++] = index;
        }
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Duplicate, BlitzPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        if (chosen == 0)
            return;
        BlitzCard original = BlitzPiles.At(ref x.B, BlitzPile.Hand, selected[0]);
        int copies = (int)BlitzCardEffects.Var(x, 0);
        for (int index = 0; index < copies; index++)
        {
            BlitzCard clone = BlitzKernel.CloneCard(original);
            if (!BlitzKernel.AddGeneratedCard(ref x, clone, BlitzPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("ENLIGHTENMENT")]
public sealed class Enlightenment : BlitzCardDef
{
    // CardPileOnPlaySupport.ReduceHandCosts: every hand card is set to 1, reduce-only; upgraded for
    // the whole combat, otherwise this turn or until played.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCostExpiration expiration = x.Card.Upgrade > 0
            ? BlitzCostExpiration.EndOfCombat
            : BlitzCostExpiration.EndOfTurnOrWhenPlayed;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            BlitzKernel.AddEnergyCostModifier(ref x.B.Cards[index], 1, BlitzCostType.Absolute,
                expiration, reduceOnly: true);
        }
    }
}

[BlitzContent("ENTRENCH")]
public sealed class Entrench : BlitzCardDef
{
    // BespokeCardMirrors.EntrenchOnPlay: gain block equal to the current block with
    // Unpowered | Move and the card as source.
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.B.Creature(BlitzLimits.PlayerOwner).Block,
            BlitzValueProps.Unpowered | BlitzValueProps.Move, fromCard: true);
}

[BlitzContent("EXTERMINATE")]
public sealed class Exterminate : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), (int)BlitzCardEffects.Var(x, 1));
}

[BlitzContent("FEEDING_FRENZY")]
public sealed class FeedingFrenzy : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    // CardPowerOnPlaySupport.FeedingFrenzy: Strength first, then the temporary power. The temporary
    // power's native BeforeApplied is not run by combat.Apply, so this is not ApplyTemporaryStrength.
    public override void OnPlay(ref BlitzExec x)
    {
        int amount = (int)BlitzCardEffects.Var(x, 0);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.StrengthPower>(), amount, BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.FeedingFrenzyPower>(), amount, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("HELLO_WORLD")]
public sealed class HelloWorld : BlitzCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["HELLO_WORLD_POWER"];

    // CardPowerOnPlaySupport.HelloWorld: combat.Apply<HelloWorldPower>(owner, 1, owner).
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.HelloWorldPower>(), 1, BlitzLimits.PlayerOwner);
}

[BlitzContent("METAMORPHOSIS")]
public sealed class Metamorphosis : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAttacks];

    // CardGenerationCardMirrors.MetamorphosisOnPlay: GetUnlockedCharacterAttacksForCombat (with
    // replacement, one NextItem per card), each free this combat, into the draw pile at a random
    // position in generation order.
    public override void OnPlay(ref BlitzExec x)
    {
        int count = (int)BlitzCardEffects.Var(x, 0);
        if (count > BlitzLimits.Cards)
            throw new BlitzCapacityException("Metamorphosis.Generated", BlitzLimits.Cards);
        Span<BlitzCard> generated = stackalloc BlitzCard[Math.Max(0, count)];
        int sampled = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.CharacterAttacks, count,
            distinct: false, BlitzRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < sampled; index++)
        {
            BlitzKernel.MakeFreeThisCombat(x.R, ref generated[index]);
            if (!BlitzKernel.AddGeneratedCard(ref x, generated[index], BlitzPile.Draw, BlitzPilePosition.Random))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("OUTMANEUVER")]
public sealed class Outmaneuver : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    // CardPowerOnPlaySupport.Outmaneuver: combat.Apply<EnergyNextTurnPower>(owner, Energy, owner).
    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.EnergyNextTurnPower>(), 0);
}

[BlitzContent("PECK")]
public sealed class Peck : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), (int)BlitzCardEffects.Var(x, 1));
}

[BlitzContent("REBOUND")]
public sealed class Rebound : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<string> AppliedPowers => ["REBOUND_POWER"];

    // Inferred attack; CardEffectSpecRegistry.Rebound applies ReboundPower 1 afterwards.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.ReboundPower>(), 1, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("RIP_AND_TEAR")]
public sealed class RipAndTear : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // RandomTargetAttackCardMirrors.RipAndTearOnPlay: two hits on random opponents (each hit rerolls).
    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), hitCount: 2);
}

[BlitzContent("SQUASH")]
public sealed class Squash : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

    // Inferred attack; CorePowerSupport.ApplyCardPowers applies Vulnerable after the enemy-death pass.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[BlitzContent("STACK")]
public sealed class Stack : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "CalculationExtra", "CalculatedBlock"];

    // CalculatedVarSpecRegistry's Stack multiplier is the discard pile count:
    // CalculatedBlock = CalculationBase + CalculationExtra × count; GeneralCardMirrors' block recipe
    // uses CalculatedBlock.Props (Move) with the card as source.
    public override void OnPlay(ref BlitzExec x)
    {
        decimal block = BlitzCardEffects.Var(x, 0)
            + BlitzCardEffects.Var(x, 1) * x.B.DiscardCount;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, block, BlitzValueProps.Move, fromCard: true);
    }
}

[BlitzContent("TORIC_TOUGHNESS")]
public sealed class ToricToughness : BlitzCardDef
{
    // The card creates an instanced power whose rows can be removed in the same listener pass; the
    // root must enable the occurrence-tracked power passes (as the Orbit card does).
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Turns", "Block"];

    // CorePowerSupport.ApplyCardPowers.ToricToughness: gain the Block variable's block, then add an
    // instanced ToricToughnessPower with Turns and store the settled block amount on that instance.
    public override void OnPlay(ref BlitzExec x)
    {
        decimal gained = BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner,
            BlitzCardEffects.Var(x, 1), BlitzValueProps.Move, fromCard: true);
        if (x.NeedsChoice)
            return;
        int turns = (int)BlitzCardEffects.Var(x, 0);
        ushort id = BlitzRegistry.PowerId<Powers.ToricToughnessPower>();
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, id, turns, BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int index = BlitzKernel.FindLastPower(x.B, BlitzLimits.PlayerOwner, id);
        if (index < 0)
            return;
        // SimulatedCombatState.AddPowerInstance sets _target = owner (unlike Apply<T>).
        x.B.Powers[index].Target = BlitzLimits.PlayerOwner;
        BlitzKernel.SetInstanceBlockValue(ref x.B, index, gained);
    }
}

/// <summary>
/// <c>MadScience</c> (TinkerTime event): the saved <c>TinkerTimeType</c> selects Attack (damage),
/// Skill (block) or Power (one of three setup powers), and <c>TinkerTimeRider</c> adds the rider
/// chosen by the event. Existing engine: <c>CardGenerationCardMirrors.MadScienceOnPlay</c> reads
/// both saved properties; the rule table is canonical Attack/No rider, so Blitz packs the two saved
/// enum values into <see cref="BlitzCard.Private"/> and projects them at admission.
/// </summary>
[BlitzContent("MAD_SCIENCE")]
public sealed class MadScience : BlitzCardDef
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

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAll];

    public override bool HasDynamicCardType => true;

    public override int PrivateRider(in BlitzCard card) => RiderOf(card);

    public static short Encode(int type, int rider) => checked((short)((byte)type | ((byte)rider << 8)));

    public static BlitzCardType TypeOf(in BlitzCard card) => (BlitzCardType)(card.Private & 0xFF);

    public static int RiderOf(in BlitzCard card) => (card.Private >> 8) & 0xFF;

    public override BlitzCardType CardType(in BlitzBattle battle, BlitzRules rules, in BlitzCard card)
        => TypeOf(card);

    public override BlitzTargetType TargetType(in BlitzBattle battle, BlitzRules rules, in BlitzCard card)
        => TypeOf(card) == BlitzCardType.Attack ? BlitzTargetType.AnyEnemy : BlitzTargetType.Self;

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardType type = TypeOf(x.Card);
        int rider = RiderOf(x.Card);
        switch (type)
        {
            case BlitzCardType.Attack:
                BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0),
                    rider == RiderViolence ? (int)BlitzCardEffects.Var(x, 4) : 1);
                if (x.NeedsChoice)
                    return;
                ApplyRider(ref x, rider);
                break;
            case BlitzCardType.Skill:
                BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, BlitzCardEffects.Var(x, 1),
                    BlitzValueProps.Move, fromCard: true);
                if (x.NeedsChoice)
                    return;
                ApplyRider(ref x, rider);
                break;
            case BlitzCardType.Power:
                ApplyPowerRider(ref x, rider);
                break;
            default:
                throw new BlitzUnsupportedException($"MadScience 的 TinkerTimeType {(int)type} 不是可打出的类型。");
        }
    }

    private static void ApplyRider(ref BlitzExec x, int rider)
    {
        switch (rider)
        {
            case RiderSapping:
                BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.WeakPower>(), 2);
                if (x.NeedsChoice)
                    return;
                BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.VulnerablePower>(), 3);
                break;
            case RiderChoking:
                BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.StranglePower>(), 5);
                break;
            case RiderEnergized:
                BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 6));
                break;
            case RiderWisdom:
                BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 7), fromHandDraw: false);
                break;
            case RiderChaos:
            {
                Span<BlitzCard> generated = stackalloc BlitzCard[1];
                int count = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.CharacterAll, 1,
                    distinct: true, BlitzRngStream.CombatCardGeneration, generated);
                if (count == 0)
                    return;
                BlitzKernel.MakeFreeThisTurn(x.R, ref generated[0]);
                BlitzKernel.AddGeneratedCard(ref x, generated[0], BlitzPile.Hand);
                break;
            }
        }
    }

    private static void ApplyPowerRider(ref BlitzExec x, int rider)
    {
        switch (rider)
        {
            case RiderExpertise:
                BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.StrengthPower>(), (int)BlitzCardEffects.Var(x, 8),
                    BlitzLimits.PlayerOwner);
                if (x.NeedsChoice)
                    return;
                BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.DexterityPower>(), (int)BlitzCardEffects.Var(x, 9),
                    BlitzLimits.PlayerOwner);
                break;
            case RiderCurious:
                BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.CuriousPower>(), (int)BlitzCardEffects.Var(x, 10),
                    BlitzLimits.PlayerOwner);
                break;
            case RiderImprovement:
            {
                // CardGenerationCardMirrors.MadScienceOnPlay records the growth reward only when the
                // ImprovementPower amount actually increased.
                int applied = BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.ImprovementPower>(), 1, BlitzLimits.PlayerOwner);
                if (applied > 0)
                    BlitzGrowthRewards.Add(ref x.B, BlitzGrowthSource.MadScience);
                break;
            }
        }
    }
}
