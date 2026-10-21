using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Relics;

/// <summary>
/// <c>UndyingSigil</c>: powered attacks from a doomed creature against its owner are multiplied by
/// DamageDecrease (<c>ModifyDamageMirrors.HandleUndyingSigil</c>).
/// </summary>
[FlashContent("UNDYING_SIGIL")]
public sealed class UndyingSigil : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["DamageDecrease"];

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self,
        in FlashDamageContext damage, decimal amount)
    {
        if (damage.Dealer == FlashLimits.NoOwner
            || damage.Dealer == FlashLimits.PlayerOwner
            || damage.Target != FlashLimits.PlayerOwner
            || !damage.Props.IsPoweredAttack())
            return 1m;
        ushort doom = FlashRegistry.PowerId<Powers.DoomPower>();
        return x.B.Creature(damage.Dealer).CurrentHp <= FlashKernel.GetAmount(x.B, damage.Dealer, doom)
            ? x.R.RelicVar(Id, 0)
            : 1m;
    }
}

/// <summary>
/// <c>NinjaScroll</c>: adds 3 Shivs to hand at the first <c>BeforeHandDraw</c>, before the captured
/// root; the Shivs are already in the root hand.
/// </summary>
[FlashContent("NINJA_SCROLL")]
public sealed class NinjaScroll : FlashRelicDef
{
}

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
/// <c>RingingTriangle</c>: <c>ShouldFlush</c> declines the owner's hand flush on player turn 1 only
/// (<c>PersistentRelicSupport.ShouldFlush</c> uses the simulated turn number).
/// </summary>
[FlashContent("RINGING_TRIANGLE")]
public sealed class RingingTriangle : FlashRelicDef
{
    public override bool ShouldFlush(ref FlashExec x, FlashListener self) => x.B.PlayerTurn > 1;
}

/// <summary>
/// <c>TheAbacus</c>: <c>AfterShuffle</c> gains its unpowered block when the shuffling player is its
/// owner (<c>AfterShuffleMirrors.HandleTheAbacus</c>).
/// </summary>
[FlashContent("THE_ABACUS")]
public sealed class TheAbacus : FlashRelicDef
{
    // TheAbacus.cs: BlockVar(6m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterShuffle(ref FlashExec x, FlashListener self, byte shuffler)
    {
        if (self.Owner != FlashLimits.PlayerOwner || shuffler != FlashLimits.PlayerOwner)
            return;
        FlashKernel.GainBlock(ref x, self.Owner, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>ChemicalX</c>: <c>ModifyXValue</c> adds its <c>Increase</c> variable to an X value read by its
/// owner's card (<c>CardModel.ResolveEnergyXValue</c>); the native <c>BeforeCardPlayed</c> only flashes.
/// </summary>
[FlashContent("CHEMICAL_X")]
public sealed class ChemicalX : FlashRelicDef
{
    // ChemicalX.cs: DynamicVar("Increase", 2m).
    public override IReadOnlyList<string> VarNames => ["Increase"];

    public override int ModifyXValue(in FlashBattle battle, FlashRules rules, FlashListener self, in FlashCard card,
        int value)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return value;
        return value + (int)rules.RelicVar(Id, 0);
    }
}

/// <summary>
/// <c>GhostSeed</c>: a Basic Strike or Defend that enters combat without Ethereal gains it
/// (<c>SimulatedCombatState.CardEventHistory.AfterCardEnteredCombat</c> /
/// <c>GhostSeedMirrors.AfterCardEnteredCombat</c>).
/// </summary>
[FlashContent("GHOST_SEED")]
public sealed class GhostSeed : FlashRelicDef
{
    public override void AfterCardEnteredCombat(ref FlashExec x, FlashListener self, FlashPile pile, int index,
        in FlashCard card)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        if (stats.Rarity != FlashCardRarity.Basic
            || (stats.Tags & (FlashCardTag.Strike | FlashCardTag.Defend)) == 0)
        {
            return;
        }
        ref FlashCard stored = ref FlashPiles.At(ref x.B, pile, index);
        if ((stored.Keywords & FlashKeywords.Ethereal) == 0)
            stored.Keywords |= FlashKeywords.Ethereal;
    }
}

/// <summary>
/// <c>SlingOfCourage</c>: <c>AfterRoomEntered</c> applies Strength only for an elite room, during combat
/// setup, and has no later hook.
/// </summary>
[FlashContent("SLING_OF_COURAGE")]
public sealed class SlingOfCourage : FlashRelicDef;

/// <summary>
/// <c>Toolbox</c>: <c>BeforeHandDraw</c> offers its colourless card choice only on turn 1
/// (<c>SimulatedCombatState.Relics.ContinueRelicsBeforeHandDraw</c>), i.e. at the player turn start
/// before the play-phase root.
/// </summary>
[FlashContent("TOOLBOX")]
public sealed class Toolbox : FlashRelicDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.ColorlessAll];

    public override bool MayGenerateIdentityContent => true;
}

/// <summary><c>Cauldron</c>: <c>AfterObtained</c> potion rewards only.</summary>
[FlashContent("CAULDRON")]
public sealed class Cauldron : FlashRelicDef;

/// <summary><c>DingyRug</c>: <c>ModifyCardRewardCreationOptions</c> only.</summary>
[FlashContent("DINGY_RUG")]
public sealed class DingyRug : FlashRelicDef;

/// <summary><c>DollysMirror</c>: <c>AfterObtained</c> card duplication only.</summary>
[FlashContent("DOLLYS_MIRROR")]
public sealed class DollysMirror : FlashRelicDef;

/// <summary><c>DragonFruit</c>: <c>AfterGoldGained</c> only; the existing engine mirrors it in <c>GoldGainedMirrors</c>, outside combat.</summary>
[FlashContent("DRAGON_FRUIT")]
public sealed class DragonFruit : FlashRelicDef;

/// <summary><c>GnarledHammer</c>: <c>AfterObtained</c> card upgrades only.</summary>
[FlashContent("GNARLED_HAMMER")]
public sealed class GnarledHammer : FlashRelicDef;

/// <summary><c>Kifuda</c>: <c>AfterObtained</c> only.</summary>
[FlashContent("KIFUDA")]
public sealed class Kifuda : FlashRelicDef;

/// <summary>
/// <c>LavaLamp</c>: <c>AfterDamageReceived</c> only records <c>TookDamageThisCombat</c>, consumed by
/// <c>TryModifyCardRewardOptionsLate</c> after combat. The existing engine registers it with
/// <c>AfterDamageReceivedMirrors.RegisterIgnored&lt;LavaLamp&gt;()</c>, so it has no combat effect.
/// </summary>
[FlashContent("LAVA_LAMP")]
public sealed class LavaLamp : FlashRelicDef;

/// <summary><c>LeesWaffle</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[FlashContent("LEES_WAFFLE")]
public sealed class LeesWaffle : FlashRelicDef;

/// <summary><c>MembershipCard</c>: <c>ModifyMerchantPrice</c> only.</summary>
[FlashContent("MEMBERSHIP_CARD")]
public sealed class MembershipCard : FlashRelicDef;

/// <summary><c>MiniatureTent</c>: <c>ShouldDisableRemainingRestSiteOptions</c> only.</summary>
[FlashContent("MINIATURE_TENT")]
public sealed class MiniatureTent : FlashRelicDef;

/// <summary><c>Orrery</c>: <c>AfterObtained</c> card rewards only.</summary>
[FlashContent("ORRERY")]
public sealed class Orrery : FlashRelicDef;

/// <summary><c>PunchDagger</c>: <c>AfterObtained</c> only.</summary>
[FlashContent("PUNCH_DAGGER")]
public sealed class PunchDagger : FlashRelicDef;

/// <summary><c>RoyalStamp</c>: <c>AfterObtained</c> only.</summary>
[FlashContent("ROYAL_STAMP")]
public sealed class RoyalStamp : FlashRelicDef;

/// <summary><c>WingCharm</c>: <c>TryModifyCardRewardOptionsLate</c> only.</summary>
[FlashContent("WING_CHARM")]
public sealed class WingCharm : FlashRelicDef;

/// <summary>
/// <c>BurningSticks</c>: the first Skill exhausted in the combat puts a clone into the hand
/// (<c>AfterCardExhaustedMirrors.HandleBurningSticks</c>).
/// Counter = 1 once used this combat.
/// </summary>
[FlashContent("BURNING_STICKS")]
public sealed class BurningSticks : FlashRelicDef
{
    public override void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card, bool causedByEthereal)
    {
        if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Skill || x.B.Relics[self.Index].Counter != 0)
            return;
        if (!FlashKernel.AddGeneratedCard(ref x, FlashKernel.CloneCard(card), FlashPile.Hand))
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
[FlashContent("BELT_BUCKLE")]
public sealed class BeltBuckle : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    public override void AfterPotionUsed(ref FlashExec x, FlashListener self, ushort potionId)
    {
        if (x.B.Relics[self.Index].Counter != 0 || HasAvailablePotion(x.B))
            return;
        x.B.Relics[self.Index].Counter = 1;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.DexterityPower>(), (int)x.R.RelicVar(Id, 0), FlashLimits.PlayerOwner);
    }

    private static bool HasAvailablePotion(in FlashBattle battle)
    {
        for (int slot = 0; slot < battle.PotionSlotCount; slot++)
        {
            if (battle.Potions[slot] != 0)
                return true;
        }
        return false;
    }
}
