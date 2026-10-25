using BlitzSolver.Content.Afflictions;
using BlitzSolver.Content.Monsters.Act1;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// <c>PlowPower</c> (Ceremonial Beast's STAMP_MOVE): when the owner takes unblocked damage and its
/// current HP drops to this power's amount or below, it loses Strength (permanent and temporary),
/// is stunned into BEAST_CRY_MOVE and removes the power. Mirrors
/// <c>AfterDamageReceivedMirrors.HandlePlowPower</c>; the stun is the existing engine's
/// <c>ForceStunnedMove</c> (not the native <c>CreatureCmd.Stun</c> overload).
/// </summary>
[BlitzContent("PLOW_POWER")]
public sealed class PlowPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterDamageReceived(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || result.UnblockedDamage <= 0)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (x.B.Creature(self.Owner).CurrentHp > amount)
            return;
        ushort strength = BlitzRegistry.TryPower("STRENGTH_POWER", out ushort strengthId)
            ? strengthId
            : ushort.MaxValue;
        for (int index = x.B.PowerCount - 1; index >= 0; index--)
        {
            ref readonly BlitzPower power = ref x.B.Powers[index];
            if (power.Owner != self.Owner)
                continue;
            // AfterDamageReceivedMirrors.HandlePlowPower removes every TemporaryStrengthPower
            // subclass (Mangle, SetupStrike, PiercingWail, ...) plus StrengthPower itself. The
            // temporary family has no single native id; the definition records the stat it feeds.
            BlitzPowerDef def = BlitzRegistry.Power(power.Id);
            bool temporaryStrength = def.IsTemporaryPower
                && def.InternallyAppliedPowerId == "STRENGTH_POWER";
            if (power.Id == strength || temporaryStrength)
                BlitzKernel.SetPowerAmount(ref x.B, self.Owner, power.Id, 0);
        }
        int nextNode = -1;
        if (x.B.Enemies[self.Owner - BlitzLimits.FirstEnemyOwner].MonsterId
            == BlitzRegistry.MonsterId<CeremonialBeast>())
        {
            nextNode = BlitzKernel.MonsterRules(ref x, self.Owner).NodeIndex("BEAST_CRY_MOVE");
        }
        BlitzKernel.ForceStunnedMove(ref x, self.Owner, nextNode);
        BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}

/// <summary>
/// <c>RingingPower</c> (Ceremonial Beast's BEAST_CRY_MOVE): afflicts every card with Ringing, and a
/// Ringing card may not be played as the second or later card of the same turn
/// (<c>ShouldPlayMirrors.HandleRingingPower</c>). Removed at the player's turn end
/// (<c>CorePowerSupport.TriggerPlayerRegularSideTurnEndEffects</c>), after which the normalization
/// clears the afflictions.
/// </summary>
[BlitzContent("RINGING_POWER")]
public sealed class RingingPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override bool ShouldPlay(in BlitzBattle battle, BlitzRules rules, BlitzListener self, in BlitzCard card)
    {
        if (card.AfflictionId != BlitzRegistry.AfflictionId<RingingAffliction>() + 1)
            return true;
        return battle.Counter(BlitzCounter.CardPlaySeriesStartedThisTurn) == 0;
    }

    public override void PlayerSideTurnEnd(ref BlitzExec x, BlitzListener self)
        => BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
}

/// <summary>
/// <c>TangledPower</c> (Vine Shambler's GRASPING_VINES_MOVE): afflicts Attacks with Entangled and adds
/// the power's amount to their energy cost (<c>TryModifyEnergyCostInCombat</c>). Removed at the
/// player's turn end; the normalization clears Entangled afterwards.
/// </summary>
[BlitzContent("TANGLED_POWER")]
public sealed class TangledPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombat(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzCard card, BlitzPile pile, decimal amount)
    {
        if (card.AfflictionId != BlitzRegistry.AfflictionId<EntangledAffliction>() + 1)
            return amount;
        return amount + battle.Powers[self.Index].Amount;
    }

    public override void PlayerSideTurnEnd(ref BlitzExec x, BlitzListener self)
        => BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
}

/// <summary>
/// <c>SmoggyPower</c> (Living Fog's ADVANCED_GAS_MOVE): Skills are unplayable while Smog-afflicted;
/// after the player plays a Skill every other Skill card gets Smog, and the player's turn end clears
/// them. Mirrors <c>ShouldPlayMirrors</c>, <c>AfterCardPlayedMirrors.HandleSmoggyPower</c> and
/// <c>EndTurnPowerSupport.TriggerRegular</c>'s SmoggyPower case.
/// </summary>
[BlitzContent("SMOGGY_POWER")]
public sealed class SmoggyPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override bool ShouldPlay(in BlitzBattle battle, BlitzRules rules, BlitzListener self, in BlitzCard card)
        => card.AfflictionId != BlitzRegistry.AfflictionId<SmogAffliction>() + 1;

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (play.Position < 0 || play.Position >= x.B.CardCount)
            return;
        ref readonly BlitzCard played = ref x.B.Cards[play.Position];
        BlitzCardStats stats = x.R.Card(played.Id, played.Upgrade);
        if (stats.Type != BlitzCardType.Skill)
            return;
        ushort smog = BlitzRegistry.AfflictionId<SmogAffliction>();
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[position];
            if (card.AfflictionId != 0 || BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Skill)
                continue;
            BlitzKernel.ApplyAffliction(ref x, position, smog, 1);
            if (x.NeedsChoice)
                return;
        }
    }

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
            BlitzKernel.ClearAfflictions(ref x.B, BlitzRegistry.AfflictionId<SmogAffliction>());
    }
}

/// <summary>
/// <c>VitalSparkPower</c> (Infested Prism's PULSATE_MOVE and entrance): Skills carry Tainted. Playing
/// one applies TaintedPower to its owner with the Vital Spark amount; Tainted layers are kept in sync
/// by the normalization pass. The native <c>AfterPowerAmountChanged</c> body is not mirrored by the
/// existing engine, which normalizes instead.
/// </summary>
[BlitzContent("VITAL_SPARK_POWER")]
public sealed class VitalSparkPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (play.Position < 0 || play.Position >= x.B.CardCount
            || x.B.Cards[play.Position].AfflictionId != BlitzRegistry.AfflictionId<TaintedAffliction>() + 1)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<TaintedPower>(),
            x.B.Powers[self.Index].Amount, BlitzLimits.NoOwner);
    }
}

/// <summary>
/// <c>TaintedPower</c>: adds its amount to the owner's powered attacks and is removed at the end of
/// the enemy side. Mirrors <c>TaintedPower.ModifyDamageAdditive</c> and the EndTurnPowerSupport case.
/// </summary>
[BlitzContent("TAINTED_POWER")]
public sealed class TaintedPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyDamageAdditive(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage,
        decimal amount)
    {
        if (damage.Target != self.Owner || !damage.Props.IsPoweredAttack())
            return 0m;
        return x.B.Powers[self.Index].Amount;
    }

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (side == BlitzSide.Enemy)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}

/// <summary>
/// <c>HexPower</c>: afflicts all player cards with Hexed; when its applier dies the power is removed
/// and the normalization clears Hexed. Mirrors <c>CorePowerSupport.ApplyEnemyDeathPowers</c>'s
/// <c>RemoveHexPower</c>.
/// </summary>
[BlitzContent("HEX_POWER")]
public sealed class HexPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void ApplierDied(ref BlitzExec x, BlitzListener self)
        => BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
}
