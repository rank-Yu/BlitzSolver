using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Relics;

/// <summary>
/// <c>TheBoot</c>: <c>ModifyHpLostAfterOstyLate</c> raises a powered attack from the owner to another
/// creature to at least <c>DamageMinimum</c> when the truncated amount is between 1 and below it
/// (<c>ModifyHpLostMirrors</c> native fallback). Osty is rejected at admission, so only the owner can
/// deal the attack.
/// </summary>
[FlashContent("THE_BOOT")]
public sealed class TheBoot : FlashRelicDef
{
    // TheBoot.cs: DynamicVar("DamageMinimum", 5m); _damageMinimum and DamageThreshold are constants and
    // the threshold is not read by the hook.
    public override IReadOnlyList<string> VarNames => ["DamageMinimum"];

    public override decimal ModifyHpLostAfterOstyLate(ref FlashExec x, FlashListener self, in FlashHpLossContext loss,
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
[FlashContent("FAKE_STRIKE_DUMMY")]
public sealed class FakeStrikeDummy : FlashRelicDef
{
    // FakeStrikeDummy.cs: DynamicVar("ExtraDamage", 1m).
    public override IReadOnlyList<string> VarNames => ["ExtraDamage"];

    public override decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self, in FlashDamageContext damage,
        decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource)
            return 0m;
        return (x.R.Card(damage.CardSource.Id, damage.CardSource.Upgrade).Tags & FlashCardTag.Strike) != 0
            ? x.R.RelicVar(Id, 0)
            : 0m;
    }
}

/// <summary>
/// <c>ForgottenSoul</c>: <c>AfterCardExhausted</c> deals its Damage to one random hittable enemy
/// (<c>AfterCardExhaustedMirrors.HandleForgottenSoul</c>).
/// </summary>
[FlashContent("FORGOTTEN_SOUL")]
public sealed class ForgottenSoul : FlashRelicDef
{
    // ForgottenSoul.cs: DamageVar(1m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card,
        bool causedByEthereal)
        => FlashCardEffects.DamageRandomEnemy(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>HandDrill</c>: <c>AfterBlockBroken</c> applies Vulnerable when the owner breaks a non-player
/// creature's block (<c>AfterBlockBrokenMirrors.HandleHandDrill</c>). Osty is rejected at admission.
/// </summary>
[FlashContent("HAND_DRILL")]
public sealed class HandDrill : FlashRelicDef
{
    // HandDrill.cs: PowerVar&lt;VulnerablePower&gt;(2m).
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    public override void AfterBlockBroken(ref FlashExec x, FlashListener self, byte target, byte breaker)
    {
        if (breaker != self.Owner || target == FlashLimits.PlayerOwner)
            return;
        FlashKernel.ApplyPower(ref x, target, FlashRegistry.PowerId<VulnerablePower>(),
            (int)x.R.RelicVar(Id, 0), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>DaughterOfTheWind</c>: <c>AfterCardPlayed</c>, after an Attack, gains its unpowered block
/// (<c>AfterCardPlayedMirrors.HandleDaughterOfTheWind</c>).
/// </summary>
[FlashContent("DAUGHTER_OF_THE_WIND")]
public sealed class DaughterOfTheWind : FlashRelicDef
{
    // DaughterOfTheWind.cs: BlockVar(1m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Attack)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>LostWisp</c>: <c>AfterCardPlayed</c>, after a Power, deals its unpowered damage to every hittable
/// enemy (<c>AfterCardPlayedMirrors.HandleLostWisp</c>).
/// </summary>
[FlashContent("LOST_WISP")]
public sealed class LostWisp : FlashRelicDef
{
    // LostWisp.cs: DamageVar(8m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (!FlashKernel.IsInProgress(x.B)
            || FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Power)
            return;
        FlashCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>MrStruggles</c>: <c>AfterPlayerTurnStart</c> deals unpowered damage equal to the player turn
/// number to every hittable enemy (existing-engine relic batch, <c>ContinueRelicsAfterPlayerTurnStart</c>).
/// </summary>
[FlashContent("MR_STRUGGLES")]
public sealed class MrStruggles : FlashRelicDef
{
    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
        => FlashCardEffects.DamageAllEnemies(ref x, x.B.PlayerTurn, FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>FakeHappyFlower</c>: <c>AfterSideTurnStart</c> advances the cycle and grants Energy on wrap
/// (<c>TriggerRelicsAfterSideTurnStart</c>). Counter = turns seen (the engine's
/// <c>StatefulRelicState.Current</c>).
/// </summary>
[FlashContent("FAKE_HAPPY_FLOWER")]
public sealed class FakeHappyFlower : FlashRelicDef
{
    // FakeHappyFlower.cs: EnergyVar(1), DynamicVar("Turns", 5m).
    public override IReadOnlyList<string> VarNames => ["Energy", "Turns"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter = (relic.Counter + 1) % (int)x.R.RelicVar(Id, 1);
        if (relic.Counter == 0)
            FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>FakeVenerableTeaSet</c>: <c>AfterEnergyReset</c> grants Energy once while the pending flag is
/// set (<c>TriggerRelicsAfterEnergyReset</c>). Counter = the engine's
/// <c>StatefulRelicState.Current</c> (GainEnergyInNextCombat).
/// </summary>
[FlashContent("FAKE_VENERABLE_TEA_SET")]
public sealed class FakeVenerableTeaSet : FlashRelicDef
{
    // FakeVenerableTeaSet.cs: EnergyVar(1).
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
/// <c>FakeOrichalcum</c>: <c>BeforeSideTurnEndVeryEarly</c> latches when the player has no block;
/// <c>BeforeSideTurnEnd</c> spends the latch for its Block; <c>BeforeSideTurnStart</c> clears it
/// (<c>OrichalcumMirrors</c>). Counter = the latched trigger, projected from the same
/// <c>OrichalcumMirrors</c> store entry as Orichalcum.
/// </summary>
[FlashContent("FAKE_ORICHALCUM")]
public sealed class FakeOrichalcum : FlashRelicDef
{
    // FakeOrichalcum.cs: BlockVar(3m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void BeforeSideTurnEndVeryEarly(ref FlashExec x, FlashListener self, FlashSide side,
        ushort participants)
    {
        if (FlashKernel.Participates(participants, self.Owner) && x.B.Creature(self.Owner).Block <= 0)
            x.B.Relics[self.Index].Counter = 1;
    }

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter == 0)
            return;
        relic.Counter = 0;
        FlashKernel.GainBlock(ref x, self.Owner, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered, fromCard: false);
    }

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>PollinousCore</c>: <c>BeforeHandDraw</c> advances TurnsSeen and <c>ModifyHandDraw</c> adds Cards
/// once the cycle completes, then <c>CompleteRelicHandDraw</c> resets it
/// (<c>SimulatedCombatState.Relics.PrepareRelicsBeforeHandDraw</c>,
/// <c>GetStatefulRelicHandDrawContribution</c>, <c>CompleteRelicHandDraw</c>).
/// <see cref="FlashListenerDef.ModifyHandDraw"/> is evaluated exactly once per turn-start hand draw, so
/// the increment, contribution and reset happen in that one method. Counter = TurnsSeen
/// (<c>StatefulRelicState.Current</c>).
/// </summary>
[FlashContent("POLLINOUS_CORE")]
public sealed class PollinousCore : FlashRelicDef
{
    // PollinousCore.cs: CardsVar(2), DynamicVar("Turns", 4m).
    public override IReadOnlyList<string> VarNames => ["Cards", "Turns"];

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (relic.Counter < (int)x.R.RelicVar(Id, 1))
            return amount;
        relic.Counter = 0;
        return amount + x.R.RelicVar(Id, 0);
    }
}

/// <summary><c>FresnelLens</c>: merchant and card-reward creation hooks only.</summary>
[FlashContent("FRESNEL_LENS")]
public sealed class FresnelLens : FlashRelicDef;

/// <summary>
/// <c>HistoryCourse</c>: at the player turn start's AutoPrePlay it clones the last attack played on the
/// previous player turn and auto-plays it (<c>SimulatedCombatState.TriggerScheduledAutoPlays</c> →
/// <c>ContinueHistoryCourse</c>). The clone itself lives in the kernel's AutoPrePlay stage; the relic
/// has no other combat hook. <see cref="FlashRules.HasHistoryCourse"/> gates the per-turn card slot.
/// </summary>
[FlashContent("HISTORY_COURSE")]
public sealed class HistoryCourseRelic : FlashRelicDef
{
}

/// <summary>
/// <c>BingBong</c>: duplicates a card added to the owner's out-of-combat Deck pile
/// (<c>AfterCardChangedPiles</c> with <c>PileType.Deck</c>). Combat piles never produce a Deck-pile
/// event, so it has no hook inside the search window.
/// </summary>
[FlashContent("BING_BONG")]
public sealed class BingBong : FlashRelicDef
{
}

/// <summary>
/// <c>DarkstonePeriapt</c>: gains MaxHp when a Curse is added to the owner's Deck pile
/// (<c>AfterCardChangedPiles</c> with <c>PileType.Deck</c>); deck-only, inert in combat.
/// </summary>
[FlashContent("DARKSTONE_PERIAPT")]
public sealed class DarkstonePeriapt : FlashRelicDef
{
}

/// <summary>
/// <c>FakeSneckoEye</c>: applies Confused 1 at <c>BeforeCombatStart</c>; the power is in the captured
/// root, so the relic itself has no in-combat hook.
/// </summary>
[FlashContent("FAKE_SNECKO_EYE")]
public sealed class FakeSneckoEye : FlashRelicDef
{
}

[FlashContent("CHOSEN_CHEESE")] public sealed class ChosenCheese : FlashRelicDef; // AfterCombatEnd

[FlashContent("DREAM_CATCHER")] public sealed class DreamCatcher : FlashRelicDef; // TryModifyRestSiteHealRewards

[FlashContent("EMBER_TEA")] public sealed class EmberTea : FlashRelicDef; // AfterRoomEntered Strength

[FlashContent("FAKE_LEES_WAFFLE")] public sealed class FakeLeesWaffle : FlashRelicDef; // AfterObtained

[FlashContent("FAKE_MANGO")] public sealed class FakeMango : FlashRelicDef; // AfterObtained

[FlashContent("FAKE_MERCHANTS_RUG")] public sealed class FakeMerchantsRug : FlashRelicDef; // no hooks

[FlashContent("FRAGRANT_MUSHROOM")] public sealed class FragrantMushroom : FlashRelicDef; // AfterObtained

[FlashContent("MAW_BANK")] public sealed class MawBank : FlashRelicDef; // AfterItemPurchased

[FlashContent("SWORD_OF_STONE")] public sealed class SwordOfStone : FlashRelicDef; // AfterCombatVictory

[FlashContent("WONGO_CUSTOMER_APPRECIATION_BADGE")] public sealed class WongoCustomerAppreciationBadge : FlashRelicDef; // no hooks

[FlashContent("WONGOS_MYSTERY_TICKET")] public sealed class WongosMysteryTicket : FlashRelicDef; // TryModifyRewards / AfterCombatEnd

// --- combat-start / player turn-1 effects imported with the root ------------------------
// The native hook fires before the play-phase root; the resulting block, power, hand or
// counter is part of the imported FlashBattle.
[FlashContent("BIG_MUSHROOM")] public sealed class BigMushroom : FlashRelicDef; // ModifyHandDraw turn 1 only

[FlashContent("FAKE_ANCHOR")] public sealed class FakeAnchor : FlashRelicDef; // BeforeCombatStart block

[FlashContent("FAKE_BLOOD_VIAL")] public sealed class FakeBloodVial : FlashRelicDef; // turn <= 1 AfterPlayerTurnStartLate heal

[FlashContent("ROYAL_POISON")] public sealed class RoyalPoison : FlashRelicDef; // turn <= 1 AfterPlayerTurnStart damage

[FlashContent("SWORD_OF_JADE")] public sealed class SwordOfJade : FlashRelicDef; // AfterRoomEntered Strength

[FlashContent("TEA_OF_DISCOURTESY")] public sealed class TeaOfDiscourtesy : FlashRelicDef; // BeforeCombatStart Dazed

// --- state projected without a later combat effect --------------------------------------
/// <summary>
/// <c>BoneTea</c>: upgrades the turn-1 hand and spends a combat at the player turn start before the
/// play-phase root; no later hook. Counter = CombatsLeft, projected from
/// <c>StatefulRelicState.Current</c>.
/// </summary>
[FlashContent("BONE_TEA")]
public sealed class BoneTea : FlashRelicDef;
