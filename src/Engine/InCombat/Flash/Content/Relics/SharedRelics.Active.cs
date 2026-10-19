using CombatSolver.Engine.InCombat.Flash.Content.Powers;

namespace CombatSolver.Engine.InCombat.Flash.Content.Relics;

// Class 3: combat effects that the existing engine implements through a hook or a turn-phase relic
// batch and that need no per-relic private state. The engine location is named on each definition.
// Numeric values come from the per-root relic rule table (FlashRelicDef.VarNames + x.R.RelicVar);
// the names are the native DynamicVars keys. A few turn numbers are code constants and stay here
// with the decompiled source named.

/// <summary>
/// <c>Bread</c>: <c>ModifyMaxEnergy</c> adds 1 on every turn except turn 1
/// (<c>PersistentPowerSupport.GetTurnBasedMaxEnergyContribution</c>). The native
/// <c>AfterSideTurnStart</c> lose-2-energy case is turn 1 only and belongs to the combat-start turn,
/// before the root.
/// </summary>
[FlashContent("BREAD")]
public sealed class Bread : FlashRelicDef
{
    // Bread.cs: EnergyVar("GainEnergy", 1).
    public override IReadOnlyList<string> VarNames => ["GainEnergy"];

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => x.B.PlayerTurn == 1 ? amount : amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>Candelabra</c>: <c>AfterSideTurnStart</c>, player turn 2, gains 2 energy
/// (<c>SimulatedCombatState.RelicTurnStart.TriggerRelicsAfterSideTurnStart</c>). Turn 2 is reached after
/// the root through <c>AdvanceRound</c>.
/// </summary>
[FlashContent("CANDELABRA")]
public sealed class Candelabra : FlashRelicDef
{
    // Candelabra.cs: EnergyVar(2), _energyTurn = 2.
    private const int EnergyTurn = 2;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side == FlashSide.Player && x.B.PlayerTurn == EnergyTurn)
            FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>Chandelier</c>: <c>AfterSideTurnStart</c>, player turn 3, gains 3 energy
/// (<c>TriggerRelicsAfterSideTurnStart</c>).
/// </summary>
[FlashContent("CHANDELIER")]
public sealed class Chandelier : FlashRelicDef
{
    // Chandelier.cs: EnergyVar(3), _energyTurn = 3.
    private const int EnergyTurn = 3;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side == FlashSide.Player && x.B.PlayerTurn == EnergyTurn)
            FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>CaptainsWheel</c>: <c>AfterBlockCleared</c>, player turn 3, gains 18 unpowered block
/// (<c>SimulatedCombatState.ReactiveRelics.TriggerRelicsAfterBlockCleared</c>).
/// </summary>
[FlashContent("CAPTAINS_WHEEL")]
public sealed class CaptainsWheel : FlashRelicDef
{
    // CaptainsWheel.cs: BlockVar(18m, ValueProp.Unpowered); block turn 3 is a code constant.
    private const int BlockTurn = 3;

    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterBlockCleared(ref FlashExec x, FlashListener self)
    {
        if (x.B.PlayerTurn == BlockTurn)
            FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
                FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>HornCleat</c>: <c>AfterBlockCleared</c>, player turn 2, gains 14 unpowered block
/// (<c>TriggerRelicsAfterBlockCleared</c>).
/// </summary>
[FlashContent("HORN_CLEAT")]
public sealed class HornCleat : FlashRelicDef
{
    // HornCleat.cs: BlockVar(14m, ValueProp.Unpowered); block turn 2 is a code constant.
    private const int BlockTurn = 2;

    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterBlockCleared(ref FlashExec x, FlashListener self)
    {
        if (x.B.PlayerTurn == BlockTurn)
            FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
                FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>SparklingRouge</c>: <c>AfterBlockCleared</c>, player turn 3, applies 1 Strength and 1 Dexterity
/// (<c>TriggerRelicsAfterBlockCleared</c>).
/// </summary>
[FlashContent("SPARKLING_ROUGE")]
public sealed class SparklingRouge : FlashRelicDef
{
    // SparklingRouge.cs: PowerVar<StrengthPower>(1m), PowerVar<DexterityPower>(1m); block turn 3 is
    // a code constant.
    private const int BlockTurn = 3;

    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    public override void AfterBlockCleared(ref FlashExec x, FlashListener self)
    {
        if (x.B.PlayerTurn != BlockTurn)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<StrengthPower>(),
            (int)x.R.RelicVar(Id, 0), FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<DexterityPower>(),
            (int)x.R.RelicVar(Id, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>CloakClasp</c>: <c>BeforeSideTurnEnd</c> hook (normal pass) gains 1 unpowered block per card in hand
/// (<c>BeforeSideTurnEndMirrors.HandleCloakClasp</c>).
/// </summary>
[FlashContent("CLOAK_CLASP")]
public sealed class CloakClasp : FlashRelicDef
{
    // CloakClasp.cs: BlockVar(1m, ValueProp.Unpowered), multiplied by the hand count.
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (!FlashKernel.Participates(participants, self.Owner) || x.B.HandCount == 0)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.B.HandCount * x.R.RelicVar(Id, 0),
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>ScreamingFlagon</c>: <c>BeforeSideTurnEnd</c>, with an empty hand, deals 20 unpowered damage to
/// every hittable enemy (<c>BeforeSideTurnEndMirrors.HandleScreamingFlagon</c>).
/// </summary>
[FlashContent("SCREAMING_FLAGON")]
public sealed class ScreamingFlagon : FlashRelicDef
{
    // ScreamingFlagon.cs: DamageVar(20m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (!FlashKernel.Participates(participants, self.Owner) || x.B.HandCount != 0)
            return;
        FlashCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>StoneCalendar</c>: <c>BeforeSideTurnEnd</c> on player turn 7 deals 52 unpowered damage to every
/// hittable enemy (<c>BeforeSideTurnEndMirrors.HandleStoneCalendar</c>). The existing engine reads the
/// turn number, not a private counter, so no imported state is needed.
/// </summary>
[FlashContent("STONE_CALENDAR")]
public sealed class StoneCalendar : FlashRelicDef
{
    // StoneCalendar.cs: DamageVar(52m, ValueProp.Unpowered), DynamicVar("DamageTurn", 7m).
    public override IReadOnlyList<string> VarNames => ["Damage", "DamageTurn"];

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (!FlashKernel.Participates(participants, self.Owner)
            || x.B.PlayerTurn != (int)x.R.RelicVar(Id, 1))
            return;
        FlashCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>RippleBasin</c>: <c>BeforeSideTurnEnd</c>, if no attack card was played this turn, gains 4 block
/// (<c>BeforeSideTurnEndMirrors.HandleRippleBasin</c>; Flash's
/// <see cref="FlashCounter.AttacksPlayedThisTurn"/> is the branch-local counterpart of
/// <c>GetAttacksPlayedThisTurn</c>).
/// </summary>
[FlashContent("RIPPLE_BASIN")]
public sealed class RippleBasin : FlashRelicDef
{
    // RippleBasin.cs: BlockVar(4m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (!FlashKernel.Participates(participants, self.Owner) || x.B.Counter(FlashCounter.AttacksPlayedThisTurn) != 0)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>MercuryHourglass</c>: <c>AfterPlayerTurnStart</c> deals 3 unpowered damage to every hittable enemy
/// on every player turn (<c>ContinueRelicsAfterPlayerTurnStart</c>).
/// </summary>
[FlashContent("MERCURY_HOURGLASS")]
public sealed class MercuryHourglass : FlashRelicDef
{
    // MercuryHourglass.cs: DamageVar(3m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
        => FlashCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>GamePiece</c>: <c>AfterCardPlayed</c>, after a Power card, draws 1
/// (<c>AfterCardPlayedMirrors.HandleGamePiece</c>).
/// </summary>
[FlashContent("GAME_PIECE")]
public sealed class GamePiece : FlashRelicDef
{
    // GamePiece.cs: CardsVar(1).
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Power)
            return;
        FlashKernel.Draw(ref x, (int)x.R.RelicVar(Id, 0), fromHandDraw: false);
    }
}

/// <summary>
/// <c>RazorTooth</c>: <c>AfterCardPlayed</c> upgrades the played Attack or Skill when it is still
/// upgradable (<c>AfterCardPlayedMirrors.HandleRazorTooth</c>).
/// </summary>
[FlashContent("RAZOR_TOOTH")]
public sealed class RazorTooth : FlashRelicDef
{
    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        FlashCardStats stats = x.R.Card(play.Card.Id, play.Card.Upgrade);
        if (stats.Type is not (FlashCardType.Attack or FlashCardType.Skill))
            return;
        if (play.Card.Upgrade >= stats.MaxUpgradeLevel)
            return;
        FlashCardEffects.UpgradeCurrentCard(ref x);
    }
}

/// <summary>
/// <c>IntimidatingHelmet</c>: <c>BeforeCardPlayed</c>, when the play's energy value is at least 2, gains
/// 4 unpowered block (<c>BeforeCardPlayedMirrors.HandleIntimidatingHelmet</c>; Flash's
/// <see cref="FlashExec.XValue"/> carries the same <c>CardPlay.Resources.EnergyValue</c>).
/// </summary>
[FlashContent("INTIMIDATING_HELMET")]
public sealed class IntimidatingHelmet : FlashRelicDef
{
    // IntimidatingHelmet.cs: BlockVar(4m, ValueProp.Unpowered), EnergyVar(2).
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.XValue < (int)x.R.RelicVar(Id, 1))
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            FlashValueProps.Unpowered, fromCard: false);
    }
}

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
/// <c>MiniatureCannon</c>: <c>ModifyDamageAdditive</c> adds 3 to powered attacks from an upgraded card
/// (<c>ModifyDamageMirrors</c> native fallback; <c>PredictionCoverage.IsVerifiedNativeRelicHook</c>).
/// </summary>
[FlashContent("MINIATURE_CANNON")]
public sealed class MiniatureCannon : FlashRelicDef
{
    // MiniatureCannon.cs: DynamicVar("ExtraDamage", 3m).
    public override IReadOnlyList<string> VarNames => ["ExtraDamage"];

    public override decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self,
        in FlashDamageContext damage, decimal amount)
    {
        // Native condition: reject only when neither the dealer is the owner's creature nor the
        // card source belongs to the owner. An Osty attack has the pet as dealer but the player's
        // card as source, so it still gains the bonus.
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource)
            return 0m;
        return damage.CardSource.Upgrade > 0 ? x.R.RelicVar(Id, 0) : 0m;
    }
}

/// <summary>
/// <c>Brimstone</c>: <c>AfterSideTurnStart</c> on every player turn applies 2 Strength to the player and
/// 1 Strength to every living opponent (<c>TriggerRelicsAfterSideTurnStart</c>).
/// </summary>
[FlashContent("BRIMSTONE")]
public sealed class Brimstone : FlashRelicDef
{
    // Brimstone.cs: PowerVar<StrengthPower>("SelfStrength", 2m), ("EnemyStrength", 1m).
    public override IReadOnlyList<string> VarNames => ["SelfStrength", "EnemyStrength"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player)
            return;
        ushort strength = FlashRegistry.PowerId<StrengthPower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, strength, (int)x.R.RelicVar(Id, 0),
            FlashLimits.PlayerOwner);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyPower(ref x, owner, strength, (int)x.R.RelicVar(Id, 1), FlashLimits.NoOwner);
        }
    }
}

/// <summary>
/// <c>CharonsAshes</c>: <c>AfterCardExhausted</c> deals 3 unpowered damage to every hittable enemy
/// (<c>AfterCardExhaustedMirrors.HandleCharonsAshes</c>).
/// </summary>
[FlashContent("CHARONS_ASHES")]
public sealed class CharonsAshes : FlashRelicDef
{
    // CharonsAshes.cs: DamageVar(3m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card, bool causedByEthereal)
        => FlashCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>ParryingShield</c>: <c>AfterSideTurnEnd</c>, when the player has at least 10 block, deals 6 unpowered
/// damage to one random hittable enemy (<c>SimulatedCombatState.ReactiveRelics.TriggerRelicsAfterSideTurnEnd</c>).
/// </summary>
[FlashContent("PARRYING_SHIELD")]
public sealed class ParryingShield : FlashRelicDef
{
    // ParryingShield.cs: BlockVar(10m, ValueProp.Unpowered), DamageVar(6m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block", "Damage"];

    public override void AfterSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player || x.B.Creature(FlashLimits.PlayerOwner).Block < x.R.RelicVar(Id, 0))
            return;
        FlashCardEffects.DamageRandomEnemy(ref x, x.R.RelicVar(Id, 1), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>SturdyClamp</c>: <c>ShouldClearBlock</c> declines the player's block clear and
/// <c>BlockAfterPreventingClear</c> keeps at most 10
/// (<c>PersistentRelicSupport.ShouldClearBlock</c>/<c>BlockAfterPreventingClear</c>).
/// </summary>
[FlashContent("STURDY_CLAMP")]
public sealed class SturdyClamp : FlashRelicDef
{
    // SturdyClamp.cs: BlockVar(10m, ValueProp.Unpowered); the engine reads DynamicVars.Block.
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override bool ShouldClearBlock(ref FlashExec x, FlashListener self, byte owner)
        => owner != FlashLimits.PlayerOwner;

    public override int BlockAfterPreventingClear(ref FlashExec x, FlashListener self, byte owner, int block)
        => owner == FlashLimits.PlayerOwner ? (int)Math.Min(block, x.R.RelicVar(Id, 0)) : block;
}
