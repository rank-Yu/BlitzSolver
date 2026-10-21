using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Relics;

/// <summary>
/// <c>StrikeDummy</c>: <c>ModifyDamageAdditive</c> adds 3 to powered attacks from a card with the Strike
/// tag (<c>ModifyDamageMirrors</c> falls back to the native relic hook;
/// <c>PredictionCoverage.IsVerifiedNativeRelicHook</c>).
/// </summary>
[FlashContent("STRIKE_DUMMY")]
public sealed class StrikeDummy : FlashRelicDef
{
    // StrikeDummy.cs: DynamicVar("ExtraDamage", 3m).
    public override IReadOnlyList<string> VarNames => ["ExtraDamage"];

    public override decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self,
        in FlashDamageContext damage, decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource || damage.Dealer != FlashLimits.PlayerOwner)
            return 0m;
        return (x.R.Card(damage.CardSource.Id, damage.CardSource.Upgrade).Tags & FlashCardTag.Strike) != 0
            ? x.R.RelicVar(Id, 0)
            : 0m;
    }
}

/// <summary>
/// <c>Anchor</c>: <c>BeforeCombatStart</c> gains 10 unpowered block. That is before the combat root and
/// no hook remains afterwards.
/// </summary>
[FlashContent("ANCHOR")]
public sealed class Anchor : FlashRelicDef;

/// <summary>
/// <c>BagOfMarbles</c>: <c>BeforeSideTurnStart</c> applies Vulnerable only when the player turn number is
/// &lt;= 1 (<c>PrepareRelicBeforeSideTurnStart</c>). Only the player side participates, and the player turn
/// 1 start is before the root; from turn 2 the condition is false.
/// </summary>
[FlashContent("BAG_OF_MARBLES")]
public sealed class BagOfMarbles : FlashRelicDef;

/// <summary>
/// <c>BagOfPreparation</c>: <c>ModifyHandDraw</c> adds 2 only when the turn number is &lt;= 1
/// (<c>PersistentPowerSupport.IsBranchOwnedHandDrawRelic</c> and <c>GetTurnBasedHandDrawContribution</c>).
/// The turn 1 hand draw happens before the root; on turn 2 the condition is false.
/// </summary>
[FlashContent("BAG_OF_PREPARATION")]
public sealed class BagOfPreparation : FlashRelicDef;

/// <summary>
/// <c>BloodVial</c>: <c>AfterPlayerTurnStartLate</c> heals 2 on turn &lt;= 1; the existing engine runs the
/// same heal from <c>ContinueRelicsAfterPlayerTurnStart</c> on turn &lt;= 1. That is before the root and
/// no later turn satisfies it.
/// </summary>
[FlashContent("BLOOD_VIAL")]
public sealed class BloodVial : FlashRelicDef;

/// <summary>
/// <c>BronzeScales</c>: <c>AfterRoomEntered</c> applies Thorns when entering a combat room, i.e. during
/// combat setup. The root already carries the power, and the relic has no later hook.
/// </summary>
[FlashContent("BRONZE_SCALES")]
public sealed class BronzeScales : FlashRelicDef;

/// <summary>
/// <c>FestivePopper</c>: <c>AfterPlayerTurnStart</c> damages all opponents only when the turn number is
/// &lt;= 1 (<c>ContinueRelicsAfterPlayerTurnStart</c>). The turn 1 start is before the root.
/// </summary>
[FlashContent("FESTIVE_POPPER")]
public sealed class FestivePopper : FlashRelicDef;

/// <summary>
/// <c>Gorget</c>: <c>AfterRoomEntered</c> applies Plating when entering a combat room, during combat
/// setup, and has no later hook.
/// </summary>
[FlashContent("GORGET")]
public sealed class Gorget : FlashRelicDef;

/// <summary>
/// <c>Lantern</c>: <c>AfterSideTurnStart</c> gains 1 energy only when the player turn number is &lt;= 1
/// (<c>TriggerRelicsAfterSideTurnStart</c>). The player turn 1 start is before the root.
/// </summary>
[FlashContent("LANTERN")]
public sealed class Lantern : FlashRelicDef;

/// <summary>
/// <c>OddlySmoothStone</c>: <c>AfterRoomEntered</c> applies Dexterity when entering a combat room, during
/// combat setup, and has no later hook.
/// </summary>
[FlashContent("ODDLY_SMOOTH_STONE")]
public sealed class OddlySmoothStone : FlashRelicDef;

/// <summary>
/// <c>RedMask</c>: <c>BeforeSideTurnStart</c> applies Weak only when the player turn number is &lt;= 1
/// (<c>PrepareRelicBeforeSideTurnStart</c>); only the player side participates and the turn 1 start is
/// before the root.
/// </summary>
[FlashContent("RED_MASK")]
public sealed class RedMask : FlashRelicDef;

/// <summary>
/// <c>Vajra</c>: <c>AfterRoomEntered</c> applies Strength when entering a combat room, during combat
/// setup, and has no later hook.
/// </summary>
[FlashContent("VAJRA")]
public sealed class Vajra : FlashRelicDef;

/// <summary>Native <c>AmethystAubergine</c>: <c>TryModifyRewards</c>, <c>AfterModifyingRewards</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("AMETHYST_AUBERGINE")]
public sealed class AmethystAubergine : FlashRelicDef;

/// <summary><c>BookOfFiveRings</c>: <c>AfterCardChangedPiles</c> only fires for <c>PileType.Deck</c> changes; <c>IsAllowed</c> is run-state only.</summary>
[FlashContent("BOOK_OF_FIVE_RINGS")]
public sealed class BookOfFiveRings : FlashRelicDef;

/// <summary><c>JuzuBracelet</c>: <c>ModifyUnknownMapPointRoomTypes</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("JUZU_BRACELET")]
public sealed class JuzuBracelet : FlashRelicDef;

/// <summary><c>MealTicket</c>: <c>AfterRoomEntered</c> shop heal and <c>IsAllowed</c> only.</summary>
[FlashContent("MEAL_TICKET")]
public sealed class MealTicket : FlashRelicDef;

/// <summary><c>PotionBelt</c>: <c>AfterObtained</c> extra potion slots only.</summary>
[FlashContent("POTION_BELT")]
public sealed class PotionBelt : FlashRelicDef;

/// <summary><c>RegalPillow</c>: rest-site heal hooks only.</summary>
[FlashContent("REGAL_PILLOW")]
public sealed class RegalPillow : FlashRelicDef;

/// <summary><c>Strawberry</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[FlashContent("STRAWBERRY")]
public sealed class Strawberry : FlashRelicDef;

/// <summary><c>WarPaint</c>: <c>AfterObtained</c> card upgrades only.</summary>
[FlashContent("WAR_PAINT")]
public sealed class WarPaint : FlashRelicDef;

/// <summary><c>Whetstone</c>: <c>AfterObtained</c> attack upgrades only.</summary>
[FlashContent("WHETSTONE")]
public sealed class Whetstone : FlashRelicDef;

/// <summary>
/// <c>CentennialPuzzle</c>: the first unblocked damage taken draws its Cards
/// (<c>AfterDamageReceivedMirrors</c>, engine case in <c>SimulatedCombatState</c> mirror).
/// Counter = 1 once used this combat.
/// </summary>
[FlashContent("CENTENNIAL_PUZZLE")]
public sealed class CentennialPuzzle : FlashRelicDef
{
    // CentennialPuzzle.cs: CardsVar(3).
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (!FlashKernel.IsInProgress(x.B) || result.Receiver != FlashLimits.PlayerOwner
            || result.UnblockedDamage <= 0 || x.B.Relics[self.Index].Counter != 0)
            return;
        x.B.Relics[self.Index].Counter = 1;
        FlashKernel.Draw(ref x, (int)x.R.RelicVar(Id, 0), fromHandDraw: false);
    }
}

/// <summary>
/// <c>HappyFlower</c>: <c>AfterSideTurnStart</c> advances its cycle and grants Energy on wrap
/// (<c>SimulatedCombatState.RelicTurnStart.TriggerRelicsAfterSideTurnStart</c>).
/// Counter = turns seen in the current cycle.
/// </summary>
[FlashContent("HAPPY_FLOWER")]
public sealed class HappyFlower : FlashRelicDef
{
    // HappyFlower.cs: EnergyVar(1), DynamicVar("Turns", 3m).
    public override IReadOnlyList<string> VarNames => ["Energy", "Turns"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        int turns = (int)x.R.RelicVar(Id, 1);
        relic.Counter = (relic.Counter + 1) % turns;
        if (relic.Counter == 0)
            FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>Pendulum</c>: <c>BeforeHandDraw</c> advances its cycle, and <c>ModifyHandDraw</c> adds its
/// Cards on the turn the cycle wraps (<c>SimulatedCombatState.Relics.PrepareRelicsBeforeHandDraw</c>
/// and <c>PersistentPowerSupport.GetStatefulRelicHandDrawContribution</c>).
/// Counter = turns seen in the current cycle.
/// </summary>
[FlashContent("PENDULUM")]
public sealed class Pendulum : FlashRelicDef
{
    // Pendulum.cs: CardsVar(1), DynamicVar("Turns", 3m).
    public override IReadOnlyList<string> VarNames => ["Cards", "Turns"];

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        int turns = (int)x.R.RelicVar(Id, 1);
        relic.Counter = (relic.Counter + 1) % turns;
        return relic.Counter == 0 ? amount + x.R.RelicVar(Id, 0) : amount;
    }
}

/// <summary>
/// <c>VenerableTeaSet</c>: <c>AfterEnergyReset</c> grants its Energy once when the pre-combat tea is
/// pending (<c>SimulatedCombatState.TriggerRelicsAfterEnergyReset</c>). Counter = 1 while pending.
/// </summary>
[FlashContent("VENERABLE_TEA_SET")]
public sealed class VenerableTeaSet : FlashRelicDef
{
    // VenerableTeaSet.cs: EnergyVar(2).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterEnergyReset(ref FlashExec x, FlashListener self)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter == 0)
            return;
        FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
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
[FlashContent("RED_SKULL")]
public sealed class RedSkull : FlashRelicDef
{
    // RedSkull.cs: DynamicVar("HpThreshold", 50m), PowerVar<StrengthPower>(3m).
    public override IReadOnlyList<string> VarNames => ["HpThreshold", "StrengthPower"];

    public override void AfterCurrentHpChanged(ref FlashExec x, FlashListener self, byte creature, decimal delta)
    {
        decimal threshold = x.B.Player.MaxHp * (x.R.RelicVar(Id, 0) / 100m);
        bool shouldApply = x.B.Player.CurrentHp <= threshold;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (shouldApply == (relic.Counter != 0))
            return;
        int strength = (int)x.R.RelicVar(Id, 1);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<StrengthPower>(),
            shouldApply ? strength : -strength, FlashLimits.PlayerOwner);
        relic.Counter = shouldApply ? 1 : 0;
    }
}

/// <summary>
/// <c>BoneFlute</c>: after an attack by the owner's Osty, gain its Block variable
/// (<c>AfterAttackMirrors.HandleBoneFlute</c>; the BlockVar is Unpowered).
/// </summary>
[FlashContent("BONE_FLUTE")]
public sealed class BoneFlute : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterAttack(ref FlashExec x, FlashListener self, byte attacker)
    {
        if (attacker != FlashLimits.OstyOwner)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            fromCard: false);
    }
}

/// <summary>
/// <c>DataDisk</c>: applies Focus 1 on entering a combat room (<c>AfterRoomEntered</c>), which is
/// before the captured root; the Focus power is already in the root.
/// </summary>
[FlashContent("DATA_DISK")]
public sealed class DataDisk : FlashRelicDef
{
}

/// <summary>
/// <c>SneckoSkull</c>: <c>ModifyPowerAmountGivenAdditive</c> adds 1 to every Poison amount the owner
/// gives (<c>PersistentRelicSupport.ModifyPowerAmountGiven</c>). The after-hook is cosmetic.
/// </summary>
[FlashContent("SNECKO_SKULL")]
public sealed class SneckoSkull : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override decimal ModifyPowerAmountGiven(in FlashBattle battle, FlashRules rules, FlashListener self,
        ushort powerId, byte giver, byte target, decimal amount)
    {
        if (giver != FlashLimits.PlayerOwner)
            return 0m;
        if (!FlashRegistry.TryPower("POISON_POWER", out ushort poison) || powerId != poison)
            return 0m;
        return rules.RelicVar(Id, 0);
    }
}
