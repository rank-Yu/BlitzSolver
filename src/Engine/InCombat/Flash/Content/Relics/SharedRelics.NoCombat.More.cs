namespace CombatSolver.Engine.InCombat.Flash.Content.Relics;

// Class 1 extension for relic batch R2: relics whose native hooks run only outside combat, while picking
// up relics, in shops, at rest sites, on the map or in post-combat rewards, or whose only combat hook
// fires before the play-phase root (combat start / player turn 1 start / turn-1 BeforeHandDraw) and whose
// result is imported. The existing engine has no combat registration for these, so the definition only
// lets roots holding them pass admission. Each comment names the native hook.

// --- rewards, shops, rest sites, map and pickup effects --------------------------------
[FlashContent("ALCHEMICAL_COFFER")] public sealed class AlchemicalCoffer : FlashRelicDef; // AfterObtained / TryModifyRewards
[FlashContent("ARCANE_SCROLL")] public sealed class ArcaneScroll : FlashRelicDef; // AfterObtained
[FlashContent("ARCHAIC_TOOTH")] public sealed class ArchaicTooth : FlashRelicDef; // AfterCloned / AfterObtained
[FlashContent("ASTROLABE")] public sealed class Astrolabe : FlashRelicDef; // AfterObtained
[FlashContent("BEAUTIFUL_BRACELET")] public sealed class BeautifulBracelet : FlashRelicDef; // AfterObtained
[FlashContent("BLACK_BLOOD")] public sealed class BlackBlood : FlashRelicDef; // AfterCombatVictory
[FlashContent("BLACK_STAR")] public sealed class BlackStar : FlashRelicDef; // TryModifyRewards
[FlashContent("CALLING_BELL")] public sealed class CallingBell : FlashRelicDef; // AfterObtained
[FlashContent("CHOSEN_CHEESE")] public sealed class ChosenCheese : FlashRelicDef; // AfterCombatEnd
[FlashContent("CIRCLET")] public sealed class Circlet : FlashRelicDef; // IsStackable
[FlashContent("CLAWS")] public sealed class Claws : FlashRelicDef; // AfterObtained
[FlashContent("CURSED_PEARL")] public sealed class CursedPearl : FlashRelicDef; // AfterObtained
[FlashContent("DISTINGUISHED_CAPE")] public sealed class DistinguishedCape : FlashRelicDef; // AfterObtained
[FlashContent("DOWSING_ROD")] public sealed class DowsingRod : FlashRelicDef; // AfterObtained / TryModifyRewards
[FlashContent("DREAM_CATCHER")] public sealed class DreamCatcher : FlashRelicDef; // TryModifyRestSiteHealRewards
[FlashContent("DRIFTWOOD")] public sealed class Driftwood : FlashRelicDef; // TryModifyRewardsLate
[FlashContent("DUSTY_TOME")] public sealed class DustyTome : FlashRelicDef; // AfterObtained
[FlashContent("ELECTRIC_SHRYMP")] public sealed class ElectricShrymp : FlashRelicDef; // AfterObtained
[FlashContent("EMBER_TEA")] public sealed class EmberTea : FlashRelicDef; // AfterRoomEntered Strength
[FlashContent("EMPTY_CAGE")] public sealed class EmptyCage : FlashRelicDef; // AfterObtained
[FlashContent("FAKE_LEES_WAFFLE")] public sealed class FakeLeesWaffle : FlashRelicDef; // AfterObtained
[FlashContent("FAKE_MANGO")] public sealed class FakeMango : FlashRelicDef; // AfterObtained
[FlashContent("FAKE_MERCHANTS_RUG")] public sealed class FakeMerchantsRug : FlashRelicDef; // no hooks
[FlashContent("FISHING_ROD")] public sealed class FishingRod : FlashRelicDef; // AfterCombatEnd
[FlashContent("FRAGRANT_MUSHROOM")] public sealed class FragrantMushroom : FlashRelicDef; // AfterObtained
[FlashContent("GLASS_EYE")] public sealed class GlassEye : FlashRelicDef; // AfterObtained
[FlashContent("GLITTER")] public sealed class Glitter : FlashRelicDef; // TryModifyCardRewardOptionsLate
[FlashContent("GOLDEN_COMPASS")] public sealed class GoldenCompass : FlashRelicDef; // AfterObtained / map
[FlashContent("GOLDEN_PEARL")] public sealed class GoldenPearl : FlashRelicDef; // AfterObtained
[FlashContent("HEFTY_TABLET")] public sealed class HeftyTablet : FlashRelicDef; // AfterObtained
[FlashContent("JEWELRY_BOX")] public sealed class JewelryBox : FlashRelicDef; // AfterObtained
[FlashContent("KALEIDOSCOPE")] public sealed class Kaleidoscope : FlashRelicDef; // AfterObtained / IsAllowedAtNeow
[FlashContent("LARGE_CAPSULE")] public sealed class LargeCapsule : FlashRelicDef; // AfterObtained
[FlashContent("LAVA_ROCK")] public sealed class LavaRock : FlashRelicDef; // TryModifyRewards
[FlashContent("LEAD_PAPERWEIGHT")] public sealed class LeadPaperweight : FlashRelicDef; // AfterObtained
[FlashContent("LEAFY_POULTICE")] public sealed class LeafyPoultice : FlashRelicDef; // AfterObtained
[FlashContent("LORDS_PARASOL")] public sealed class LordsParasol : FlashRelicDef; // AfterRoomEntered (merchant)
[FlashContent("LOST_COFFER")] public sealed class LostCoffer : FlashRelicDef; // AfterObtained
[FlashContent("MASSIVE_SCROLL")] public sealed class MassiveScroll : FlashRelicDef; // IsAllowed / AfterObtained
[FlashContent("MAW_BANK")] public sealed class MawBank : FlashRelicDef; // AfterItemPurchased
[FlashContent("MEAT_CLEAVER")] public sealed class MeatCleaver : FlashRelicDef; // TryModifyRestSiteOptions
[FlashContent("NEOWS_BONES")] public sealed class NeowsBones : FlashRelicDef; // AfterObtained
[FlashContent("NEOWS_SACRIFICE")] public sealed class NeowsSacrifice : FlashRelicDef; // AfterObtained
[FlashContent("NEOWS_TALISMAN")] public sealed class NeowsTalisman : FlashRelicDef; // AfterObtained
[FlashContent("NEOWS_TORMENT")] public sealed class NeowsTorment : FlashRelicDef; // AfterObtained
[FlashContent("NEW_LEAF")] public sealed class NewLeaf : FlashRelicDef; // AfterObtained
[FlashContent("NUTRITIOUS_OYSTER")] public sealed class NutritiousOyster : FlashRelicDef; // AfterObtained
[FlashContent("NUTRITIOUS_SOUP")] public sealed class NutritiousSoup : FlashRelicDef; // AfterObtained
[FlashContent("PAELS_CLAW")] public sealed class PaelsClaw : FlashRelicDef; // AfterObtained (deck enchantment)
[FlashContent("PAELS_GROWTH")] public sealed class PaelsGrowth : FlashRelicDef; // AfterObtained / rest site
[FlashContent("PAELS_HORN")] public sealed class PaelsHorn : FlashRelicDef; // AfterObtained
[FlashContent("PAELS_TOOTH")] public sealed class PaelsTooth : FlashRelicDef; // rewards / AfterObtained
[FlashContent("PAELS_WING")] public sealed class PaelsWing : FlashRelicDef; // TryModifyCardRewardAlternatives
[FlashContent("PANDORAS_BOX")] public sealed class PandorasBox : FlashRelicDef; // AfterObtained
[FlashContent("PHIAL_HOLSTER")] public sealed class PhialHolster : FlashRelicDef; // AfterObtained
[FlashContent("POMANDER")] public sealed class Pomander : FlashRelicDef; // AfterObtained
[FlashContent("PRECARIOUS_SHEARS")] public sealed class PrecariousShears : FlashRelicDef; // AfterObtained
[FlashContent("PRECISE_SCISSORS")] public sealed class PreciseScissors : FlashRelicDef; // AfterObtained
[FlashContent("PRESERVED_FOG")] public sealed class PreservedFog : FlashRelicDef; // AfterObtained
[FlashContent("SAND_CASTLE")] public sealed class SandCastle : FlashRelicDef; // AfterObtained
[FlashContent("SCROLL_BOXES")] public sealed class ScrollBoxes : FlashRelicDef; // AfterObtained / IsAllowedAtNeow
[FlashContent("SEA_GLASS")] public sealed class SeaGlass : FlashRelicDef; // AfterObtained
[FlashContent("SERE_TALON")] public sealed class SereTalon : FlashRelicDef; // AfterObtained
[FlashContent("SIGNET_RING")] public sealed class SignetRing : FlashRelicDef; // AfterObtained
[FlashContent("SILKEN_TRESS")] public sealed class SilkenTress : FlashRelicDef; // TryModifyCardRewardOptionsLate
[FlashContent("SILVER_CRUCIBLE")] public sealed class SilverCrucible : FlashRelicDef; // rewards / IsAllowed
[FlashContent("SMALL_CAPSULE")] public sealed class SmallCapsule : FlashRelicDef; // AfterObtained
[FlashContent("STONE_HUMIDIFIER")] public sealed class StoneHumidifier : FlashRelicDef; // AfterRestSiteHeal
[FlashContent("STORYBOOK")] public sealed class Storybook : FlashRelicDef; // AfterObtained
[FlashContent("SWORD_OF_STONE")] public sealed class SwordOfStone : FlashRelicDef; // AfterCombatVictory
[FlashContent("TANXS_WHISTLE")] public sealed class TanxsWhistle : FlashRelicDef; // AfterObtained
[FlashContent("TOUCH_OF_OROBAS")] public sealed class TouchOfOrobas : FlashRelicDef; // AfterCloned / AfterObtained
[FlashContent("TOY_BOX")] public sealed class ToyBox : FlashRelicDef; // AfterObtained / AfterCombatEnd
[FlashContent("TRI_BOOMERANG")] public sealed class TriBoomerang : FlashRelicDef; // AfterObtained
[FlashContent("WAR_HAMMER")] public sealed class WarHammer : FlashRelicDef; // AfterCombatVictory
[FlashContent("WINGED_BOOTS")] public sealed class WingedBoots : FlashRelicDef; // map / IsAllowed
[FlashContent("WONGO_CUSTOMER_APPRECIATION_BADGE")] public sealed class WongoCustomerAppreciationBadge : FlashRelicDef; // no hooks
[FlashContent("WONGOS_MYSTERY_TICKET")] public sealed class WongosMysteryTicket : FlashRelicDef; // TryModifyRewards / AfterCombatEnd
[FlashContent("YUMMY_COOKIE")] public sealed class YummyCookie : FlashRelicDef; // AfterObtained

// --- combat-start / player turn-1 effects imported with the root ------------------------
// The native hook fires before the play-phase root; the resulting block, power, hand or
// counter is part of the imported FlashBattle.
[FlashContent("BIG_MUSHROOM")] public sealed class BigMushroom : FlashRelicDef; // ModifyHandDraw turn 1 only
[FlashContent("BOOMING_CONCH")] public sealed class BoomingConch : FlashRelicDef; // turn 1 elite draw / energy
[FlashContent("FAKE_ANCHOR")] public sealed class FakeAnchor : FlashRelicDef; // BeforeCombatStart block
[FlashContent("FAKE_BLOOD_VIAL")] public sealed class FakeBloodVial : FlashRelicDef; // turn <= 1 AfterPlayerTurnStartLate heal
[FlashContent("JEWELED_MASK")] public sealed class JeweledMask : FlashRelicDef; // turn 1 BeforeHandDraw
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
