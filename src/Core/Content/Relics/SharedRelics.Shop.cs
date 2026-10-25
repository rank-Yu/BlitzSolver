using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Relics;

/// <summary>
/// <c>UndyingSigil</c>: powered attacks from a doomed creature against its owner are multiplied by
/// DamageDecrease (<c>ModifyDamageMirrors.HandleUndyingSigil</c>).
/// </summary>
[BlitzContent("UNDYING_SIGIL")]
public sealed class UndyingSigil : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["DamageDecrease"];

    public override decimal ModifyDamageMultiplicative(ref BlitzExec x, BlitzListener self,
        in BlitzDamageContext damage, decimal amount)
    {
        if (damage.Dealer == BlitzLimits.NoOwner
            || damage.Dealer == BlitzLimits.PlayerOwner
            || damage.Target != BlitzLimits.PlayerOwner
            || !damage.Props.IsPoweredAttack())
            return 1m;
        ushort doom = BlitzRegistry.PowerId<Powers.DoomPower>();
        return x.B.Creature(damage.Dealer).CurrentHp <= BlitzKernel.GetAmount(x.B, damage.Dealer, doom)
            ? x.R.RelicVar(Id, 0)
            : 1m;
    }
}

/// <summary>
/// <c>NinjaScroll</c>: adds 3 Shivs to hand at the first <c>BeforeHandDraw</c>, before the captured
/// root; the Shivs are already in the root hand.
/// </summary>
[BlitzContent("NINJA_SCROLL")]
public sealed class NinjaScroll : BlitzRelicDef
{
}

/// <summary>
/// <c>Bread</c>: <c>ModifyMaxEnergy</c> adds 1 on every turn except turn 1
/// (<c>PersistentPowerSupport.GetTurnBasedMaxEnergyContribution</c>). The native
/// <c>AfterSideTurnStart</c> lose-2-energy case is turn 1 only and belongs to the combat-start turn,
/// before the root.
/// </summary>
[BlitzContent("BREAD")]
public sealed class Bread : BlitzRelicDef
{
    // Bread.cs: EnergyVar("GainEnergy", 1).
    public override IReadOnlyList<string> VarNames => ["GainEnergy"];

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => x.B.PlayerTurn == 1 ? amount : amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>ScreamingFlagon</c>: <c>BeforeSideTurnEnd</c>, with an empty hand, deals 20 unpowered damage to
/// every hittable enemy (<c>BeforeSideTurnEndMirrors.HandleScreamingFlagon</c>).
/// </summary>
[BlitzContent("SCREAMING_FLAGON")]
public sealed class ScreamingFlagon : BlitzRelicDef
{
    // ScreamingFlagon.cs: DamageVar(20m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void BeforeSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        if (!BlitzKernel.Participates(participants, self.Owner) || x.B.HandCount != 0)
            return;
        BlitzCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered,
            BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Brimstone</c>: <c>AfterSideTurnStart</c> on every player turn applies 2 Strength to the player and
/// 1 Strength to every living opponent (<c>TriggerRelicsAfterSideTurnStart</c>).
/// </summary>
[BlitzContent("BRIMSTONE")]
public sealed class Brimstone : BlitzRelicDef
{
    // Brimstone.cs: PowerVar<StrengthPower>("SelfStrength", 2m), ("EnemyStrength", 1m).
    public override IReadOnlyList<string> VarNames => ["SelfStrength", "EnemyStrength"];

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player)
            return;
        ushort strength = BlitzRegistry.PowerId<StrengthPower>();
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, strength, (int)x.R.RelicVar(Id, 0),
            BlitzLimits.PlayerOwner);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (!BlitzKernel.IsHittable(x.B, owner))
                continue;
            BlitzKernel.ApplyPower(ref x, owner, strength, (int)x.R.RelicVar(Id, 1), BlitzLimits.NoOwner);
        }
    }
}

/// <summary>
/// <c>RingingTriangle</c>: <c>ShouldFlush</c> declines the owner's hand flush on player turn 1 only
/// (<c>PersistentRelicSupport.ShouldFlush</c> uses the simulated turn number).
/// </summary>
[BlitzContent("RINGING_TRIANGLE")]
public sealed class RingingTriangle : BlitzRelicDef
{
    public override bool ShouldFlush(ref BlitzExec x, BlitzListener self) => x.B.PlayerTurn > 1;
}

/// <summary>
/// <c>TheAbacus</c>: <c>AfterShuffle</c> gains its unpowered block when the shuffling player is its
/// owner (<c>AfterShuffleMirrors.HandleTheAbacus</c>).
/// </summary>
[BlitzContent("THE_ABACUS")]
public sealed class TheAbacus : BlitzRelicDef
{
    // TheAbacus.cs: BlockVar(6m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterShuffle(ref BlitzExec x, BlitzListener self, byte shuffler)
    {
        if (self.Owner != BlitzLimits.PlayerOwner || shuffler != BlitzLimits.PlayerOwner)
            return;
        BlitzKernel.GainBlock(ref x, self.Owner, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>ChemicalX</c>: <c>ModifyXValue</c> adds its <c>Increase</c> variable to an X value read by its
/// owner's card (<c>CardModel.ResolveEnergyXValue</c>); the native <c>BeforeCardPlayed</c> only flashes.
/// </summary>
[BlitzContent("CHEMICAL_X")]
public sealed class ChemicalX : BlitzRelicDef
{
    // ChemicalX.cs: DynamicVar("Increase", 2m).
    public override IReadOnlyList<string> VarNames => ["Increase"];

    public override int ModifyXValue(in BlitzBattle battle, BlitzRules rules, BlitzListener self, in BlitzCard card,
        int value)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return value;
        return value + (int)rules.RelicVar(Id, 0);
    }
}

/// <summary>
/// <c>GhostSeed</c>: a Basic Strike or Defend that enters combat without Ethereal gains it
/// (<c>SimulatedCombatState.CardEventHistory.AfterCardEnteredCombat</c> /
/// <c>GhostSeedMirrors.AfterCardEnteredCombat</c>).
/// </summary>
[BlitzContent("GHOST_SEED")]
public sealed class GhostSeed : BlitzRelicDef
{
    public override void AfterCardEnteredCombat(ref BlitzExec x, BlitzListener self, BlitzPile pile, int index,
        in BlitzCard card)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
        if (stats.Rarity != BlitzCardRarity.Basic
            || (stats.Tags & (BlitzCardTag.Strike | BlitzCardTag.Defend)) == 0)
        {
            return;
        }
        ref BlitzCard stored = ref BlitzPiles.At(ref x.B, pile, index);
        if ((stored.Keywords & BlitzKeywords.Ethereal) == 0)
            stored.Keywords |= BlitzKeywords.Ethereal;
    }
}

/// <summary>
/// <c>SlingOfCourage</c>: <c>AfterRoomEntered</c> applies Strength only for an elite room, during combat
/// setup, and has no later hook.
/// </summary>
[BlitzContent("SLING_OF_COURAGE")]
public sealed class SlingOfCourage : BlitzRelicDef;

/// <summary>
/// <c>Toolbox</c>: <c>BeforeHandDraw</c> offers its colourless card choice only on turn 1
/// (<c>SimulatedCombatState.Relics.ContinueRelicsBeforeHandDraw</c>), i.e. at the player turn start
/// before the play-phase root.
/// </summary>
[BlitzContent("TOOLBOX")]
public sealed class Toolbox : BlitzRelicDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.ColorlessAll];

    public override bool MayGenerateIdentityContent => true;
}

/// <summary><c>Cauldron</c>: <c>AfterObtained</c> potion rewards only.</summary>
[BlitzContent("CAULDRON")]
public sealed class Cauldron : BlitzRelicDef;

/// <summary><c>DingyRug</c>: <c>ModifyCardRewardCreationOptions</c> only.</summary>
[BlitzContent("DINGY_RUG")]
public sealed class DingyRug : BlitzRelicDef;

/// <summary><c>DollysMirror</c>: <c>AfterObtained</c> card duplication only.</summary>
[BlitzContent("DOLLYS_MIRROR")]
public sealed class DollysMirror : BlitzRelicDef;

/// <summary><c>DragonFruit</c>: <c>AfterGoldGained</c> only; the existing engine mirrors it in <c>GoldGainedMirrors</c>, outside combat.</summary>
[BlitzContent("DRAGON_FRUIT")]
public sealed class DragonFruit : BlitzRelicDef;

/// <summary><c>GnarledHammer</c>: <c>AfterObtained</c> card upgrades only.</summary>
[BlitzContent("GNARLED_HAMMER")]
public sealed class GnarledHammer : BlitzRelicDef;

/// <summary><c>Kifuda</c>: <c>AfterObtained</c> only.</summary>
[BlitzContent("KIFUDA")]
public sealed class Kifuda : BlitzRelicDef;

/// <summary>
/// <c>LavaLamp</c>: <c>AfterDamageReceived</c> only records <c>TookDamageThisCombat</c>, consumed by
/// <c>TryModifyCardRewardOptionsLate</c> after combat. The existing engine registers it with
/// <c>AfterDamageReceivedMirrors.RegisterIgnored&lt;LavaLamp&gt;()</c>, so it has no combat effect.
/// </summary>
[BlitzContent("LAVA_LAMP")]
public sealed class LavaLamp : BlitzRelicDef;

/// <summary><c>LeesWaffle</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[BlitzContent("LEES_WAFFLE")]
public sealed class LeesWaffle : BlitzRelicDef;

/// <summary><c>MembershipCard</c>: <c>ModifyMerchantPrice</c> only.</summary>
[BlitzContent("MEMBERSHIP_CARD")]
public sealed class MembershipCard : BlitzRelicDef;

/// <summary><c>MiniatureTent</c>: <c>ShouldDisableRemainingRestSiteOptions</c> only.</summary>
[BlitzContent("MINIATURE_TENT")]
public sealed class MiniatureTent : BlitzRelicDef;

/// <summary><c>Orrery</c>: <c>AfterObtained</c> card rewards only.</summary>
[BlitzContent("ORRERY")]
public sealed class Orrery : BlitzRelicDef;

/// <summary><c>PunchDagger</c>: <c>AfterObtained</c> only.</summary>
[BlitzContent("PUNCH_DAGGER")]
public sealed class PunchDagger : BlitzRelicDef;

/// <summary><c>RoyalStamp</c>: <c>AfterObtained</c> only.</summary>
[BlitzContent("ROYAL_STAMP")]
public sealed class RoyalStamp : BlitzRelicDef;

/// <summary><c>WingCharm</c>: <c>TryModifyCardRewardOptionsLate</c> only.</summary>
[BlitzContent("WING_CHARM")]
public sealed class WingCharm : BlitzRelicDef;

/// <summary>
/// <c>BurningSticks</c>: the first Skill exhausted in the combat puts a clone into the hand
/// (<c>AfterCardExhaustedMirrors.HandleBurningSticks</c>).
/// Counter = 1 once used this combat.
/// </summary>
[BlitzContent("BURNING_STICKS")]
public sealed class BurningSticks : BlitzRelicDef
{
    public override void AfterCardExhausted(ref BlitzExec x, BlitzListener self, in BlitzCard card, bool causedByEthereal)
    {
        if (BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Skill || x.B.Relics[self.Index].Counter != 0)
            return;
        if (!BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CloneCard(card), BlitzPile.Hand))
            return;
        x.B.Relics[self.Index].Counter = 1;
    }
}

/// <summary>
/// <c>BeltBuckle</c>: applies 2 Dexterity when the owner has no potions. The pre-combat application
/// is already in the captured root; <c>AfterPotionUsed</c> reapplies it once when the last potion is
/// used (<c>SimulatedCombatState.TriggerRelicsAfterPotionUsed</c>, gated by the relic's
/// StatefulRelicState.Current flag).
/// </summary>
[BlitzContent("BELT_BUCKLE")]
public sealed class BeltBuckle : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    public override void AfterPotionUsed(ref BlitzExec x, BlitzListener self, ushort potionId)
    {
        if (x.B.Relics[self.Index].Counter != 0 || HasAvailablePotion(x.B))
            return;
        x.B.Relics[self.Index].Counter = 1;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.DexterityPower>(), (int)x.R.RelicVar(Id, 0), BlitzLimits.PlayerOwner);
    }

    private static bool HasAvailablePotion(in BlitzBattle battle)
    {
        for (int slot = 0; slot < battle.PotionSlotCount; slot++)
        {
            if (battle.Potions[slot] != 0)
                return true;
        }
        return false;
    }
}
