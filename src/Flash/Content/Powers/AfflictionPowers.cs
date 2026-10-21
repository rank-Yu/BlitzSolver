using CombatSolver.Flash.Content.Afflictions;
using CombatSolver.Flash.Content.Monsters.Act1;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Powers;

// Powers behind the vanilla card afflictions and the first-act encounters that use them. The
// existing engine implements these as mirrors/compensations; the original decompiled models are
// referenced only where the existing engine changed a condition or ordering.

/// <summary>
/// <c>PlowPower</c> (Ceremonial Beast's STAMP_MOVE): when the owner takes unblocked damage and its
/// current HP drops to this power's amount or below, it loses Strength (permanent and temporary),
/// is stunned into BEAST_CRY_MOVE and removes the power. Mirrors
/// <c>AfterDamageReceivedMirrors.HandlePlowPower</c>; the stun is the existing engine's
/// <c>ForceStunnedMove</c> (not the native <c>CreatureCmd.Stun</c> overload).
/// </summary>
[FlashContent("PLOW_POWER")]
public sealed class PlowPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || result.UnblockedDamage <= 0)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (x.B.Creature(self.Owner).CurrentHp > amount)
            return;
        ushort temporaryStrength = FlashRegistry.TryPower("TEMPORARY_STRENGTH_POWER", out ushort temporaryId)
            ? temporaryId
            : ushort.MaxValue;
        ushort strength = FlashRegistry.TryPower("STRENGTH_POWER", out ushort strengthId)
            ? strengthId
            : ushort.MaxValue;
        for (int index = x.B.PowerCount - 1; index >= 0; index--)
        {
            ref readonly FlashPower power = ref x.B.Powers[index];
            if (power.Owner == self.Owner && (power.Id == temporaryStrength || power.Id == strength))
                FlashKernel.SetPowerAmount(ref x.B, self.Owner, power.Id, 0);
        }
        int nextNode = -1;
        if (x.B.Enemies[self.Owner - FlashLimits.FirstEnemyOwner].MonsterId
            == FlashRegistry.MonsterId<CeremonialBeast>())
        {
            nextNode = FlashKernel.MonsterRules(ref x, self.Owner).NodeIndex("BEAST_CRY_MOVE");
        }
        FlashKernel.ForceStunnedMove(ref x, self.Owner, nextNode);
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}

/// <summary>
/// <c>RingingPower</c> (Ceremonial Beast's BEAST_CRY_MOVE): afflicts every card with Ringing, and a
/// Ringing card may not be played as the second or later card of the same turn
/// (<c>ShouldPlayMirrors.HandleRingingPower</c>). Removed at the player's turn end
/// (<c>CorePowerSupport.TriggerPlayerRegularSideTurnEndEffects</c>), after which the normalization
/// clears the afflictions.
/// </summary>
[FlashContent("RINGING_POWER")]
public sealed class RingingPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override bool ShouldPlay(in FlashBattle battle, FlashRules rules, FlashListener self, in FlashCard card)
    {
        if (card.AfflictionId != FlashRegistry.AfflictionId<RingingAffliction>() + 1)
            return true;
        return battle.Counter(FlashCounter.CardPlaySeriesStartedThisTurn) == 0;
    }

    public override void PlayerSideTurnEnd(ref FlashExec x, FlashListener self)
        => FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
}

/// <summary>
/// <c>TangledPower</c> (Vine Shambler's GRASPING_VINES_MOVE): afflicts Attacks with Entangled and adds
/// the power's amount to their energy cost (<c>TryModifyEnergyCostInCombat</c>). Removed at the
/// player's turn end; the normalization clears Entangled afterwards.
/// </summary>
[FlashContent("TANGLED_POWER")]
public sealed class TangledPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombat(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount)
    {
        if (card.AfflictionId != FlashRegistry.AfflictionId<EntangledAffliction>() + 1)
            return amount;
        return amount + battle.Powers[self.Index].Amount;
    }

    public override void PlayerSideTurnEnd(ref FlashExec x, FlashListener self)
        => FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
}

/// <summary>
/// <c>SmoggyPower</c> (Living Fog's ADVANCED_GAS_MOVE): Skills are unplayable while Smog-afflicted;
/// after the player plays a Skill every other Skill card gets Smog, and the player's turn end clears
/// them. Mirrors <c>ShouldPlayMirrors</c>, <c>AfterCardPlayedMirrors.HandleSmoggyPower</c> and
/// <c>EndTurnPowerSupport.TriggerRegular</c>'s SmoggyPower case.
/// </summary>
[FlashContent("SMOGGY_POWER")]
public sealed class SmoggyPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override bool ShouldPlay(in FlashBattle battle, FlashRules rules, FlashListener self, in FlashCard card)
        => card.AfflictionId != FlashRegistry.AfflictionId<SmogAffliction>() + 1;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (play.Position < 0 || play.Position >= x.B.CardCount)
            return;
        ref readonly FlashCard played = ref x.B.Cards[play.Position];
        FlashCardStats stats = x.R.Card(played.Id, played.Upgrade);
        if (stats.Type != FlashCardType.Skill)
            return;
        ushort smog = FlashRegistry.AfflictionId<SmogAffliction>();
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref readonly FlashCard card = ref x.B.Cards[position];
            if (card.AfflictionId != 0 || FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Skill)
                continue;
            FlashKernel.ApplyAffliction(ref x, position, smog, 1);
            if (x.NeedsChoice)
                return;
        }
    }

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.ClearAfflictions(ref x.B, FlashRegistry.AfflictionId<SmogAffliction>());
    }
}

/// <summary>
/// <c>VitalSparkPower</c> (Infested Prism's PULSATE_MOVE and entrance): Skills carry Tainted. Playing
/// one applies TaintedPower to its owner with the Vital Spark amount; Tainted layers are kept in sync
/// by the normalization pass. The native <c>AfterPowerAmountChanged</c> body is not mirrored by the
/// existing engine, which normalizes instead.
/// </summary>
[FlashContent("VITAL_SPARK_POWER")]
public sealed class VitalSparkPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (play.Position < 0 || play.Position >= x.B.CardCount
            || x.B.Cards[play.Position].AfflictionId != FlashRegistry.AfflictionId<TaintedAffliction>() + 1)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<TaintedPower>(),
            x.B.Powers[self.Index].Amount, FlashLimits.NoOwner);
    }
}

/// <summary>
/// <c>TaintedPower</c>: adds its amount to the owner's powered attacks and is removed at the end of
/// the enemy side. Mirrors <c>TaintedPower.ModifyDamageAdditive</c> and the EndTurnPowerSupport case.
/// </summary>
[FlashContent("TAINTED_POWER")]
public sealed class TaintedPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self, in FlashDamageContext damage,
        decimal amount)
    {
        if (damage.Target != self.Owner || !damage.Props.IsPoweredAttack())
            return 0m;
        return x.B.Powers[self.Index].Amount;
    }

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (side == FlashSide.Enemy)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}

/// <summary>
/// <c>HexPower</c>: afflicts all player cards with Hexed; when its applier dies the power is removed
/// and the normalization clears Hexed. Mirrors <c>CorePowerSupport.ApplyEnemyDeathPowers</c>'s
/// <c>RemoveHexPower</c>.
/// </summary>
[FlashContent("HEX_POWER")]
public sealed class HexPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void ApplierDied(ref FlashExec x, FlashListener self)
        => FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
}
