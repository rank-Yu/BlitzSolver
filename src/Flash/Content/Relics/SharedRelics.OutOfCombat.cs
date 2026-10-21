using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Relics;

// Class 1: no in-combat effect. The native hooks named on each definition only run while picking up
// relics, in shops, at rest sites, on the map or in post-combat rewards; the existing engine has no
// combat registration for them. Every root that holds one of these relics therefore imports an
// empty relic and no hook is dispatched. BurningBlood (post-victory heal) lives in StarterRelics.cs.

/// <summary>Native <c>AmethystAubergine</c>: <c>TryModifyRewards</c>, <c>AfterModifyingRewards</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("AMETHYST_AUBERGINE")]
public sealed class AmethystAubergine : FlashRelicDef;

/// <summary><c>BookOfFiveRings</c>: <c>AfterCardChangedPiles</c> only fires for <c>PileType.Deck</c> changes; <c>IsAllowed</c> is run-state only.</summary>
[FlashContent("BOOK_OF_FIVE_RINGS")]
public sealed class BookOfFiveRings : FlashRelicDef;

/// <summary><c>BowlerHat</c>: <c>ModifyGoldGained</c>/<c>AfterModifyingGoldGained</c> only; the existing engine registers it in <c>GoldGainedMirrors</c>, never as a combat hook.</summary>
[FlashContent("BOWLER_HAT")]
public sealed class BowlerHat : FlashRelicDef;

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

/// <summary><c>EternalFeather</c>: <c>AfterRoomEntered</c> rest-site heal only.</summary>
[FlashContent("ETERNAL_FEATHER")]
public sealed class EternalFeather : FlashRelicDef;

/// <summary><c>FresnelLens</c>: merchant and card-reward creation hooks only.</summary>
[FlashContent("FRESNEL_LENS")]
public sealed class FresnelLens : FlashRelicDef;

/// <summary><c>FrozenEgg</c>: merchant/card-reward upgrade hooks and <c>IsAllowed</c> only.</summary>
[FlashContent("FROZEN_EGG")]
public sealed class FrozenEgg : FlashRelicDef;

/// <summary><c>Girya</c>: <c>AfterRoomEntered</c>, <c>TryModifyRestSiteOptions</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("GIRYA")]
public sealed class Girya : FlashRelicDef;

/// <summary><c>GnarledHammer</c>: <c>AfterObtained</c> card upgrades only.</summary>
[FlashContent("GNARLED_HAMMER")]
public sealed class GnarledHammer : FlashRelicDef;

/// <summary><c>JuzuBracelet</c>: <c>ModifyUnknownMapPointRoomTypes</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("JUZU_BRACELET")]
public sealed class JuzuBracelet : FlashRelicDef;

/// <summary><c>Kifuda</c>: <c>AfterObtained</c> only.</summary>
[FlashContent("KIFUDA")]
public sealed class Kifuda : FlashRelicDef;

/// <summary><c>LastingCandy</c>: <c>BeforeCombatRewardOffered</c>, reward card options and <c>IsAllowed</c> only.</summary>
[FlashContent("LASTING_CANDY")]
public sealed class LastingCandy : FlashRelicDef;

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

/// <summary><c>LoomingFruit</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[FlashContent("LOOMING_FRUIT")]
public sealed class LoomingFruit : FlashRelicDef;

/// <summary><c>LuckyFysh</c>: <c>AfterCardChangedPiles</c> only for deck changes, plus <c>IsAllowed</c>.</summary>
[FlashContent("LUCKY_FYSH")]
public sealed class LuckyFysh : FlashRelicDef;

/// <summary><c>Mango</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[FlashContent("MANGO")]
public sealed class Mango : FlashRelicDef;

/// <summary><c>MealTicket</c>: <c>AfterRoomEntered</c> shop heal and <c>IsAllowed</c> only.</summary>
[FlashContent("MEAL_TICKET")]
public sealed class MealTicket : FlashRelicDef;

/// <summary>
/// <c>MeatOnTheBone</c>: <c>BeforeCombatStart</c> and <c>AfterCurrentHpChanged</c> only maintain the
/// relic status for the after-victory heal in <c>AfterCombatVictoryEarly</c>. The existing engine's
/// combat-hook catalog marks it NotCombatRelevant, so there is no combat dispatch to mirror.
/// </summary>
[FlashContent("MEAT_ON_THE_BONE")]
public sealed class MeatOnTheBone : FlashRelicDef;

/// <summary><c>MembershipCard</c>: <c>ModifyMerchantPrice</c> only.</summary>
[FlashContent("MEMBERSHIP_CARD")]
public sealed class MembershipCard : FlashRelicDef;

/// <summary><c>MiniatureTent</c>: <c>ShouldDisableRemainingRestSiteOptions</c> only.</summary>
[FlashContent("MINIATURE_TENT")]
public sealed class MiniatureTent : FlashRelicDef;

/// <summary><c>MoltenEgg</c>: merchant/card-reward upgrade hooks and <c>IsAllowed</c> only.</summary>
[FlashContent("MOLTEN_EGG")]
public sealed class MoltenEgg : FlashRelicDef;

/// <summary><c>OldCoin</c>: <c>AfterObtained</c> gold gain and <c>IsAllowed</c> only.</summary>
[FlashContent("OLD_COIN")]
public sealed class OldCoin : FlashRelicDef;

/// <summary><c>Orrery</c>: <c>AfterObtained</c> card rewards only.</summary>
[FlashContent("ORRERY")]
public sealed class Orrery : FlashRelicDef;

/// <summary><c>Pear</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[FlashContent("PEAR")]
public sealed class Pear : FlashRelicDef;

/// <summary><c>Planisphere</c>: <c>AfterRoomEntered</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("PLANISPHERE")]
public sealed class Planisphere : FlashRelicDef;

/// <summary><c>PotionBelt</c>: <c>AfterObtained</c> extra potion slots only.</summary>
[FlashContent("POTION_BELT")]
public sealed class PotionBelt : FlashRelicDef;

/// <summary><c>PrayerWheel</c>: <c>TryModifyRewards</c> only.</summary>
[FlashContent("PRAYER_WHEEL")]
public sealed class PrayerWheel : FlashRelicDef;

/// <summary><c>PunchDagger</c>: <c>AfterObtained</c> only.</summary>
[FlashContent("PUNCH_DAGGER")]
public sealed class PunchDagger : FlashRelicDef;

/// <summary><c>RegalPillow</c>: rest-site heal hooks only.</summary>
[FlashContent("REGAL_PILLOW")]
public sealed class RegalPillow : FlashRelicDef;

/// <summary><c>RoyalStamp</c>: <c>AfterObtained</c> only.</summary>
[FlashContent("ROYAL_STAMP")]
public sealed class RoyalStamp : FlashRelicDef;

/// <summary><c>Shovel</c>: <c>TryModifyRestSiteOptions</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("SHOVEL")]
public sealed class Shovel : FlashRelicDef;

/// <summary><c>Strawberry</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[FlashContent("STRAWBERRY")]
public sealed class Strawberry : FlashRelicDef;

/// <summary><c>TheCourier</c>: <c>ModifyMerchantPrice</c> and <c>ShouldRefillMerchantEntry</c> only.</summary>
[FlashContent("THE_COURIER")]
public sealed class TheCourier : FlashRelicDef;

/// <summary><c>TinyMailbox</c>: rest-site heal reward hooks only.</summary>
[FlashContent("TINY_MAILBOX")]
public sealed class TinyMailbox : FlashRelicDef;

/// <summary><c>ToxicEgg</c>: merchant/card-reward upgrade hooks and <c>IsAllowed</c> only.</summary>
[FlashContent("TOXIC_EGG")]
public sealed class ToxicEgg : FlashRelicDef;

/// <summary><c>WarPaint</c>: <c>AfterObtained</c> card upgrades only.</summary>
[FlashContent("WAR_PAINT")]
public sealed class WarPaint : FlashRelicDef;

/// <summary><c>Whetstone</c>: <c>AfterObtained</c> attack upgrades only.</summary>
[FlashContent("WHETSTONE")]
public sealed class Whetstone : FlashRelicDef;

/// <summary><c>WhiteBeastStatue</c>: <c>ShouldForcePotionReward</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("WHITE_BEAST_STATUE")]
public sealed class WhiteBeastStatue : FlashRelicDef;

/// <summary><c>WhiteStar</c>: <c>TryModifyRewards</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("WHITE_STAR")]
public sealed class WhiteStar : FlashRelicDef;

/// <summary><c>WingCharm</c>: <c>TryModifyCardRewardOptionsLate</c> only.</summary>
[FlashContent("WING_CHARM")]
public sealed class WingCharm : FlashRelicDef;
