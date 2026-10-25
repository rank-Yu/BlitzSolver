using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Relics;

/// <summary>
/// <c>StrikeDummy</c>: <c>ModifyDamageAdditive</c> adds 3 to powered attacks from a card with the Strike
/// tag (<c>ModifyDamageMirrors</c> falls back to the native relic hook;
/// <c>PredictionCoverage.IsVerifiedNativeRelicHook</c>).
/// </summary>
[BlitzContent("STRIKE_DUMMY")]
public sealed class StrikeDummy : BlitzRelicDef
{
    // StrikeDummy.cs: DynamicVar("ExtraDamage", 3m).
    public override IReadOnlyList<string> VarNames => ["ExtraDamage"];

    public override decimal ModifyDamageAdditive(ref BlitzExec x, BlitzListener self,
        in BlitzDamageContext damage, decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource || damage.Dealer != BlitzLimits.PlayerOwner)
            return 0m;
        return (x.R.Card(damage.CardSource.Id, damage.CardSource.Upgrade).Tags & BlitzCardTag.Strike) != 0
            ? x.R.RelicVar(Id, 0)
            : 0m;
    }
}

/// <summary>
/// <c>Anchor</c>: <c>BeforeCombatStart</c> gains 10 unpowered block. That is before the combat root and
/// no hook remains afterwards.
/// </summary>
[BlitzContent("ANCHOR")]
public sealed class Anchor : BlitzRelicDef;

/// <summary>
/// <c>BagOfMarbles</c>: <c>BeforeSideTurnStart</c> applies Vulnerable only when the player turn number is
/// &lt;= 1 (<c>PrepareRelicBeforeSideTurnStart</c>). Only the player side participates, and the player turn
/// 1 start is before the root; from turn 2 the condition is false.
/// </summary>
[BlitzContent("BAG_OF_MARBLES")]
public sealed class BagOfMarbles : BlitzRelicDef;

/// <summary>
/// <c>BagOfPreparation</c>: <c>ModifyHandDraw</c> adds 2 only when the turn number is &lt;= 1
/// (<c>PersistentPowerSupport.IsBranchOwnedHandDrawRelic</c> and <c>GetTurnBasedHandDrawContribution</c>).
/// The turn 1 hand draw happens before the root; on turn 2 the condition is false.
/// </summary>
[BlitzContent("BAG_OF_PREPARATION")]
public sealed class BagOfPreparation : BlitzRelicDef;

/// <summary>
/// <c>BloodVial</c>: <c>AfterPlayerTurnStartLate</c> heals 2 on turn &lt;= 1; the existing engine runs the
/// same heal from <c>ContinueRelicsAfterPlayerTurnStart</c> on turn &lt;= 1. That is before the root and
/// no later turn satisfies it.
/// </summary>
[BlitzContent("BLOOD_VIAL")]
public sealed class BloodVial : BlitzRelicDef;

/// <summary>
/// <c>BronzeScales</c>: <c>AfterRoomEntered</c> applies Thorns when entering a combat room, i.e. during
/// combat setup. The root already carries the power, and the relic has no later hook.
/// </summary>
[BlitzContent("BRONZE_SCALES")]
public sealed class BronzeScales : BlitzRelicDef;

/// <summary>
/// <c>FestivePopper</c>: <c>AfterPlayerTurnStart</c> damages all opponents only when the turn number is
/// &lt;= 1 (<c>ContinueRelicsAfterPlayerTurnStart</c>). The turn 1 start is before the root.
/// </summary>
[BlitzContent("FESTIVE_POPPER")]
public sealed class FestivePopper : BlitzRelicDef;

/// <summary>
/// <c>Gorget</c>: <c>AfterRoomEntered</c> applies Plating when entering a combat room, during combat
/// setup, and has no later hook.
/// </summary>
[BlitzContent("GORGET")]
public sealed class Gorget : BlitzRelicDef;

/// <summary>
/// <c>Lantern</c>: <c>AfterSideTurnStart</c> gains 1 energy only when the player turn number is &lt;= 1
/// (<c>TriggerRelicsAfterSideTurnStart</c>). The player turn 1 start is before the root.
/// </summary>
[BlitzContent("LANTERN")]
public sealed class Lantern : BlitzRelicDef;

/// <summary>
/// <c>OddlySmoothStone</c>: <c>AfterRoomEntered</c> applies Dexterity when entering a combat room, during
/// combat setup, and has no later hook.
/// </summary>
[BlitzContent("ODDLY_SMOOTH_STONE")]
public sealed class OddlySmoothStone : BlitzRelicDef;

/// <summary>
/// <c>RedMask</c>: <c>BeforeSideTurnStart</c> applies Weak only when the player turn number is &lt;= 1
/// (<c>PrepareRelicBeforeSideTurnStart</c>); only the player side participates and the turn 1 start is
/// before the root.
/// </summary>
[BlitzContent("RED_MASK")]
public sealed class RedMask : BlitzRelicDef;

/// <summary>
/// <c>Vajra</c>: <c>AfterRoomEntered</c> applies Strength when entering a combat room, during combat
/// setup, and has no later hook.
/// </summary>
[BlitzContent("VAJRA")]
public sealed class Vajra : BlitzRelicDef;

/// <summary>Native <c>AmethystAubergine</c>: <c>TryModifyRewards</c>, <c>AfterModifyingRewards</c> and <c>IsAllowed</c> only.</summary>
[BlitzContent("AMETHYST_AUBERGINE")]
public sealed class AmethystAubergine : BlitzRelicDef;

/// <summary><c>BookOfFiveRings</c>: <c>AfterCardChangedPiles</c> only fires for <c>PileType.Deck</c> changes; <c>IsAllowed</c> is run-state only.</summary>
[BlitzContent("BOOK_OF_FIVE_RINGS")]
public sealed class BookOfFiveRings : BlitzRelicDef;

/// <summary><c>JuzuBracelet</c>: <c>ModifyUnknownMapPointRoomTypes</c> and <c>IsAllowed</c> only.</summary>
[BlitzContent("JUZU_BRACELET")]
public sealed class JuzuBracelet : BlitzRelicDef;

/// <summary><c>MealTicket</c>: <c>AfterRoomEntered</c> shop heal and <c>IsAllowed</c> only.</summary>
[BlitzContent("MEAL_TICKET")]
public sealed class MealTicket : BlitzRelicDef;

/// <summary><c>PotionBelt</c>: <c>AfterObtained</c> extra potion slots only.</summary>
[BlitzContent("POTION_BELT")]
public sealed class PotionBelt : BlitzRelicDef;

/// <summary><c>RegalPillow</c>: rest-site heal hooks only.</summary>
[BlitzContent("REGAL_PILLOW")]
public sealed class RegalPillow : BlitzRelicDef;

/// <summary><c>Strawberry</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[BlitzContent("STRAWBERRY")]
public sealed class Strawberry : BlitzRelicDef;

/// <summary><c>WarPaint</c>: <c>AfterObtained</c> card upgrades only.</summary>
[BlitzContent("WAR_PAINT")]
public sealed class WarPaint : BlitzRelicDef;

/// <summary><c>Whetstone</c>: <c>AfterObtained</c> attack upgrades only.</summary>
[BlitzContent("WHETSTONE")]
public sealed class Whetstone : BlitzRelicDef;

/// <summary>
/// <c>CentennialPuzzle</c>: the first unblocked damage taken draws its Cards
/// (<c>AfterDamageReceivedMirrors</c>, engine case in <c>SimulatedCombatState</c> mirror).
/// Counter = 1 once used this combat.
/// </summary>
[BlitzContent("CENTENNIAL_PUZZLE")]
public sealed class CentennialPuzzle : BlitzRelicDef
{
    // CentennialPuzzle.cs: CardsVar(3).
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void AfterDamageReceived(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (!BlitzKernel.IsInProgress(x.B) || result.Receiver != BlitzLimits.PlayerOwner
            || result.UnblockedDamage <= 0 || x.B.Relics[self.Index].Counter != 0)
            return;
        x.B.Relics[self.Index].Counter = 1;
        BlitzKernel.Draw(ref x, (int)x.R.RelicVar(Id, 0), fromHandDraw: false);
    }
}

/// <summary>
/// <c>HappyFlower</c>: <c>AfterSideTurnStart</c> advances its cycle and grants Energy on wrap
/// (<c>SimulatedCombatState.RelicTurnStart.TriggerRelicsAfterSideTurnStart</c>).
/// Counter = turns seen in the current cycle.
/// </summary>
[BlitzContent("HAPPY_FLOWER")]
public sealed class HappyFlower : BlitzRelicDef
{
    // HappyFlower.cs: EnergyVar(1), DynamicVar("Turns", 3m).
    public override IReadOnlyList<string> VarNames => ["Energy", "Turns"];

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        int turns = (int)x.R.RelicVar(Id, 1);
        relic.Counter = (relic.Counter + 1) % turns;
        if (relic.Counter == 0)
            BlitzKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>Pendulum</c>: <c>BeforeHandDraw</c> advances its cycle, and <c>ModifyHandDraw</c> adds its
/// Cards on the turn the cycle wraps (<c>SimulatedCombatState.Relics.PrepareRelicsBeforeHandDraw</c>
/// and <c>PersistentPowerSupport.GetStatefulRelicHandDrawContribution</c>).
/// Counter = turns seen in the current cycle.
/// </summary>
[BlitzContent("PENDULUM")]
public sealed class Pendulum : BlitzRelicDef
{
    // Pendulum.cs: CardsVar(1), DynamicVar("Turns", 3m).
    public override IReadOnlyList<string> VarNames => ["Cards", "Turns"];

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        int turns = (int)x.R.RelicVar(Id, 1);
        relic.Counter = (relic.Counter + 1) % turns;
        return relic.Counter == 0 ? amount + x.R.RelicVar(Id, 0) : amount;
    }
}

/// <summary>
/// <c>VenerableTeaSet</c>: <c>AfterEnergyReset</c> grants its Energy once when the pre-combat tea is
/// pending (<c>SimulatedCombatState.TriggerRelicsAfterEnergyReset</c>). Counter = 1 while pending.
/// </summary>
[BlitzContent("VENERABLE_TEA_SET")]
public sealed class VenerableTeaSet : BlitzRelicDef
{
    // VenerableTeaSet.cs: EnergyVar(2).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterEnergyReset(ref BlitzExec x, BlitzListener self)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter == 0)
            return;
        BlitzKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
        relic.Counter = 0;
    }
}

/// <summary>
/// <c>RedSkull</c>: whenever any creature's current HP changes, the relic keeps Strength applied
/// exactly while its owner is at or below the HP threshold
/// (<c>AfterCurrentHpChangedMirrors.HandleRedSkull</c>; the native room-entry application happens
/// before the root and is imported). Counter = the engine's stateful StrengthApplied flag (0/1),
/// projected by <c>ClaimStatefulRelic</c>.
/// </summary>
[BlitzContent("RED_SKULL")]
public sealed class RedSkull : BlitzRelicDef
{
    // RedSkull.cs: DynamicVar("HpThreshold", 50m), PowerVar<StrengthPower>(3m).
    public override IReadOnlyList<string> VarNames => ["HpThreshold", "StrengthPower"];

    public override void AfterCurrentHpChanged(ref BlitzExec x, BlitzListener self, byte creature, decimal delta)
    {
        decimal threshold = x.B.Player.MaxHp * (x.R.RelicVar(Id, 0) / 100m);
        bool shouldApply = x.B.Player.CurrentHp <= threshold;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (shouldApply == (relic.Counter != 0))
            return;
        int strength = (int)x.R.RelicVar(Id, 1);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<StrengthPower>(),
            shouldApply ? strength : -strength, BlitzLimits.PlayerOwner);
        relic.Counter = shouldApply ? 1 : 0;
    }
}

/// <summary>
/// <c>BoneFlute</c>: after an attack by the owner's Osty, gain its Block variable
/// (<c>AfterAttackMirrors.HandleBoneFlute</c>; the BlockVar is Unpowered).
/// </summary>
[BlitzContent("BONE_FLUTE")]
public sealed class BoneFlute : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterAttack(ref BlitzExec x, BlitzListener self, byte attacker)
    {
        if (attacker != BlitzLimits.OstyOwner)
            return;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered,
            fromCard: false);
    }
}

/// <summary>
/// <c>DataDisk</c>: applies Focus 1 on entering a combat room (<c>AfterRoomEntered</c>), which is
/// before the captured root; the Focus power is already in the root.
/// </summary>
[BlitzContent("DATA_DISK")]
public sealed class DataDisk : BlitzRelicDef
{
}

/// <summary>
/// <c>SneckoSkull</c>: <c>ModifyPowerAmountGivenAdditive</c> adds 1 to every Poison amount the owner
/// gives (<c>PersistentRelicSupport.ModifyPowerAmountGiven</c>). The after-hook is cosmetic.
/// </summary>
[BlitzContent("SNECKO_SKULL")]
public sealed class SneckoSkull : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override decimal ModifyPowerAmountGiven(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        ushort powerId, byte giver, byte target, decimal amount)
    {
        if (giver != BlitzLimits.PlayerOwner)
            return 0m;
        if (!BlitzRegistry.TryPower("POISON_POWER", out ushort poison) || powerId != poison)
            return 0m;
        return rules.RelicVar(Id, 0);
    }
}
