using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Relics;

/// <summary>
/// <c>TheBoot</c>: <c>ModifyHpLostAfterOstyLate</c> raises a powered attack from the owner to another
/// creature to at least <c>DamageMinimum</c> when the truncated amount is between 1 and below it
/// (<c>ModifyHpLostMirrors</c> native fallback). Osty is rejected at admission, so only the owner can
/// deal the attack.
/// </summary>
[BlitzContent("THE_BOOT")]
public sealed class TheBoot : BlitzRelicDef
{
    // TheBoot.cs: DynamicVar("DamageMinimum", 5m); _damageMinimum and DamageThreshold are constants and
    // the threshold is not read by the hook.
    public override IReadOnlyList<string> VarNames => ["DamageMinimum"];

    public override decimal ModifyHpLostAfterOstyLate(ref BlitzExec x, BlitzListener self, in BlitzHpLossContext loss,
        decimal amount)
    {
        if (loss.Dealer != self.Owner || loss.Target == self.Owner)
            return amount;
        if (!loss.Props.IsPoweredAttack() || amount < 1m)
            return amount;
        decimal minimum = x.R.RelicVar(Id, 0);
        return amount >= minimum ? amount : minimum;
    }
}

/// <summary>
/// <c>FakeStrikeDummy</c>: <c>ModifyDamageAdditive</c> adds <c>ExtraDamage</c> to a powered attack whose
/// card has the Strike tag (<c>ModifyDamageMirrors</c> native fallback).
/// </summary>
[BlitzContent("FAKE_STRIKE_DUMMY")]
public sealed class FakeStrikeDummy : BlitzRelicDef
{
    // FakeStrikeDummy.cs: DynamicVar("ExtraDamage", 1m).
    public override IReadOnlyList<string> VarNames => ["ExtraDamage"];

    public override decimal ModifyDamageAdditive(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage,
        decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource)
            return 0m;
        return (x.R.Card(damage.CardSource.Id, damage.CardSource.Upgrade).Tags & BlitzCardTag.Strike) != 0
            ? x.R.RelicVar(Id, 0)
            : 0m;
    }
}

/// <summary>
/// <c>ForgottenSoul</c>: <c>AfterCardExhausted</c> deals its Damage to one random hittable enemy
/// (<c>AfterCardExhaustedMirrors.HandleForgottenSoul</c>).
/// </summary>
[BlitzContent("FORGOTTEN_SOUL")]
public sealed class ForgottenSoul : BlitzRelicDef
{
    // ForgottenSoul.cs: DamageVar(1m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterCardExhausted(ref BlitzExec x, BlitzListener self, in BlitzCard card,
        bool causedByEthereal)
        => BlitzCardEffects.DamageRandomEnemy(ref x, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered,
            BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>HandDrill</c>: <c>AfterBlockBroken</c> applies Vulnerable when the owner breaks a non-player
/// creature's block (<c>AfterBlockBrokenMirrors.HandleHandDrill</c>). Osty is rejected at admission.
/// </summary>
[BlitzContent("HAND_DRILL")]
public sealed class HandDrill : BlitzRelicDef
{
    // HandDrill.cs: PowerVar&lt;VulnerablePower&gt;(2m).
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    public override void AfterBlockBroken(ref BlitzExec x, BlitzListener self, byte target, byte breaker)
    {
        if (breaker != self.Owner || target == BlitzLimits.PlayerOwner)
            return;
        BlitzKernel.ApplyPower(ref x, target, BlitzRegistry.PowerId<VulnerablePower>(),
            (int)x.R.RelicVar(Id, 0), BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>DaughterOfTheWind</c>: <c>AfterCardPlayed</c>, after an Attack, gains its unpowered block
/// (<c>AfterCardPlayedMirrors.HandleDaughterOfTheWind</c>).
/// </summary>
[BlitzContent("DAUGHTER_OF_THE_WIND")]
public sealed class DaughterOfTheWind : BlitzRelicDef
{
    // DaughterOfTheWind.cs: BlockVar(1m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Attack)
            return;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>LostWisp</c>: <c>AfterCardPlayed</c>, after a Power, deals its unpowered damage to every hittable
/// enemy (<c>AfterCardPlayedMirrors.HandleLostWisp</c>).
/// </summary>
[BlitzContent("LOST_WISP")]
public sealed class LostWisp : BlitzRelicDef
{
    // LostWisp.cs: DamageVar(8m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (!BlitzKernel.IsInProgress(x.B)
            || BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Power)
            return;
        BlitzCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered,
            BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>MrStruggles</c>: <c>AfterPlayerTurnStart</c> deals unpowered damage equal to the player turn
/// number to every hittable enemy (existing-engine relic batch, <c>ContinueRelicsAfterPlayerTurnStart</c>).
/// </summary>
[BlitzContent("MR_STRUGGLES")]
public sealed class MrStruggles : BlitzRelicDef
{
    public override void AfterPlayerTurnStart(ref BlitzExec x, BlitzListener self)
        => BlitzCardEffects.DamageAllEnemies(ref x, x.B.PlayerTurn, BlitzValueProps.Unpowered,
            BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>FakeHappyFlower</c>: <c>AfterSideTurnStart</c> advances the cycle and grants Energy on wrap
/// (<c>TriggerRelicsAfterSideTurnStart</c>). Counter = turns seen (the engine's
/// <c>StatefulRelicState.Current</c>).
/// </summary>
[BlitzContent("FAKE_HAPPY_FLOWER")]
public sealed class FakeHappyFlower : BlitzRelicDef
{
    // FakeHappyFlower.cs: EnergyVar(1), DynamicVar("Turns", 5m).
    public override IReadOnlyList<string> VarNames => ["Energy", "Turns"];

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter = (relic.Counter + 1) % (int)x.R.RelicVar(Id, 1);
        if (relic.Counter == 0)
            BlitzKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>FakeVenerableTeaSet</c>: <c>AfterEnergyReset</c> grants Energy once while the pending flag is
/// set (<c>TriggerRelicsAfterEnergyReset</c>). Counter = the engine's
/// <c>StatefulRelicState.Current</c> (GainEnergyInNextCombat).
/// </summary>
[BlitzContent("FAKE_VENERABLE_TEA_SET")]
public sealed class FakeVenerableTeaSet : BlitzRelicDef
{
    // FakeVenerableTeaSet.cs: EnergyVar(1).
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
/// <c>FakeOrichalcum</c>: <c>BeforeSideTurnEndVeryEarly</c> latches when the player has no block;
/// <c>BeforeSideTurnEnd</c> spends the latch for its Block; <c>BeforeSideTurnStart</c> clears it
/// (<c>OrichalcumMirrors</c>). Counter = the latched trigger, projected from the same
/// <c>OrichalcumMirrors</c> store entry as Orichalcum.
/// </summary>
[BlitzContent("FAKE_ORICHALCUM")]
public sealed class FakeOrichalcum : BlitzRelicDef
{
    // FakeOrichalcum.cs: BlockVar(3m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void BeforeSideTurnEndVeryEarly(ref BlitzExec x, BlitzListener self, BlitzSide side,
        ushort participants)
    {
        if (BlitzKernel.Participates(participants, self.Owner) && x.B.Creature(self.Owner).Block <= 0)
            x.B.Relics[self.Index].Counter = 1;
    }

    public override void BeforeSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter == 0)
            return;
        relic.Counter = 0;
        BlitzKernel.GainBlock(ref x, self.Owner, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered, fromCard: false);
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>PollinousCore</c>: <c>BeforeHandDraw</c> advances TurnsSeen and <c>ModifyHandDraw</c> adds Cards
/// once the cycle completes, then <c>CompleteRelicHandDraw</c> resets it
/// (<c>SimulatedCombatState.Relics.PrepareRelicsBeforeHandDraw</c>,
/// <c>GetStatefulRelicHandDrawContribution</c>, <c>CompleteRelicHandDraw</c>).
/// <see cref="BlitzListenerDef.ModifyHandDraw"/> is evaluated exactly once per turn-start hand draw, so
/// the increment, contribution and reset happen in that one method. Counter = TurnsSeen
/// (<c>StatefulRelicState.Current</c>).
/// </summary>
[BlitzContent("POLLINOUS_CORE")]
public sealed class PollinousCore : BlitzRelicDef
{
    // PollinousCore.cs: CardsVar(2), DynamicVar("Turns", 4m).
    public override IReadOnlyList<string> VarNames => ["Cards", "Turns"];

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (relic.Counter < (int)x.R.RelicVar(Id, 1))
            return amount;
        relic.Counter = 0;
        return amount + x.R.RelicVar(Id, 0);
    }
}

/// <summary><c>FresnelLens</c>: merchant and card-reward creation hooks only.</summary>
[BlitzContent("FRESNEL_LENS")]
public sealed class FresnelLens : BlitzRelicDef;

/// <summary>
/// <c>HistoryCourse</c>: at the player turn start's AutoPrePlay it clones the last attack played on the
/// previous player turn and auto-plays it (<c>SimulatedCombatState.TriggerScheduledAutoPlays</c> →
/// <c>ContinueHistoryCourse</c>). The clone itself lives in the kernel's AutoPrePlay stage; the relic
/// has no other combat hook. <see cref="BlitzRules.HasHistoryCourse"/> gates the per-turn card slot.
/// </summary>
[BlitzContent("HISTORY_COURSE")]
public sealed class HistoryCourseRelic : BlitzRelicDef
{
}

/// <summary>
/// <c>BingBong</c>: duplicates a card added to the owner's out-of-combat Deck pile
/// (<c>AfterCardChangedPiles</c> with <c>PileType.Deck</c>). Combat piles never produce a Deck-pile
/// event, so it has no hook inside the search window.
/// </summary>
[BlitzContent("BING_BONG")]
public sealed class BingBong : BlitzRelicDef
{
}

/// <summary>
/// <c>DarkstonePeriapt</c>: gains MaxHp when a Curse is added to the owner's Deck pile
/// (<c>AfterCardChangedPiles</c> with <c>PileType.Deck</c>); deck-only, inert in combat.
/// </summary>
[BlitzContent("DARKSTONE_PERIAPT")]
public sealed class DarkstonePeriapt : BlitzRelicDef
{
}

/// <summary>
/// <c>FakeSneckoEye</c>: applies Confused 1 at <c>BeforeCombatStart</c>; the power is in the captured
/// root, so the relic itself has no in-combat hook.
/// </summary>
[BlitzContent("FAKE_SNECKO_EYE")]
public sealed class FakeSneckoEye : BlitzRelicDef
{
}

[BlitzContent("CHOSEN_CHEESE")] public sealed class ChosenCheese : BlitzRelicDef; // AfterCombatEnd

[BlitzContent("DREAM_CATCHER")] public sealed class DreamCatcher : BlitzRelicDef; // TryModifyRestSiteHealRewards

[BlitzContent("EMBER_TEA")] public sealed class EmberTea : BlitzRelicDef; // AfterRoomEntered Strength

[BlitzContent("FAKE_LEES_WAFFLE")] public sealed class FakeLeesWaffle : BlitzRelicDef; // AfterObtained

[BlitzContent("FAKE_MANGO")] public sealed class FakeMango : BlitzRelicDef; // AfterObtained

[BlitzContent("FAKE_MERCHANTS_RUG")] public sealed class FakeMerchantsRug : BlitzRelicDef; // no hooks

[BlitzContent("FRAGRANT_MUSHROOM")] public sealed class FragrantMushroom : BlitzRelicDef; // AfterObtained

[BlitzContent("MAW_BANK")] public sealed class MawBank : BlitzRelicDef; // AfterItemPurchased

[BlitzContent("SWORD_OF_STONE")] public sealed class SwordOfStone : BlitzRelicDef; // AfterCombatVictory

[BlitzContent("WONGO_CUSTOMER_APPRECIATION_BADGE")] public sealed class WongoCustomerAppreciationBadge : BlitzRelicDef; // no hooks

[BlitzContent("WONGOS_MYSTERY_TICKET")] public sealed class WongosMysteryTicket : BlitzRelicDef; // TryModifyRewards / AfterCombatEnd

// --- combat-start / player turn-1 effects imported with the root ------------------------
// The native hook fires before the play-phase root; the resulting block, power, hand or
// counter is part of the imported BlitzBattle.
[BlitzContent("BIG_MUSHROOM")] public sealed class BigMushroom : BlitzRelicDef; // ModifyHandDraw turn 1 only

[BlitzContent("FAKE_ANCHOR")] public sealed class FakeAnchor : BlitzRelicDef; // BeforeCombatStart block

[BlitzContent("FAKE_BLOOD_VIAL")] public sealed class FakeBloodVial : BlitzRelicDef; // turn <= 1 AfterPlayerTurnStartLate heal

[BlitzContent("ROYAL_POISON")] public sealed class RoyalPoison : BlitzRelicDef; // turn <= 1 AfterPlayerTurnStart damage

[BlitzContent("SWORD_OF_JADE")] public sealed class SwordOfJade : BlitzRelicDef; // AfterRoomEntered Strength

[BlitzContent("TEA_OF_DISCOURTESY")] public sealed class TeaOfDiscourtesy : BlitzRelicDef; // BeforeCombatStart Dazed

// --- state projected without a later combat effect --------------------------------------
/// <summary>
/// <c>BoneTea</c>: upgrades the turn-1 hand and spends a combat at the player turn start before the
/// play-phase root; no later hook. Counter = CombatsLeft, projected from
/// <c>StatefulRelicState.Current</c>.
/// </summary>
[BlitzContent("BONE_TEA")]
public sealed class BoneTea : BlitzRelicDef;
