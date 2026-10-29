// Encounter generation ported from the native EncounterModel.GenerateMonsters overrides in
// sts2 v0.111.0. The recipes keep the native selection order and RNG draws, but are written
// with Sts2Solver value types instead of mutable model objects.
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Encounters;

[ContentId("AEONGLASS_BOSS")]
public sealed class AeonglassBossEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("AEONGLASS"));
    }
}

[ContentId("AXEBOTS_NORMAL")]
public sealed class AxebotsNormalEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["front"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("AXEBOT", SlotOrNone("front")));
    }
}

[ContentId("BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER")]
public sealed class BattlewornDummyEventV1Encounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("BATTLE_FRIEND_V1"));
    }
}

[ContentId("BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER")]
public sealed class BattlewornDummyEventV2Encounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("BATTLE_FRIEND_V2"));
    }
}

[ContentId("BATTLEWORN_DUMMY_EVENT_V3_ENCOUNTER")]
public sealed class BattlewornDummyEventV3Encounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("BATTLE_FRIEND_V3"));
    }
}

[ContentId("BOWLBUGS_NORMAL")]
public sealed class BowlbugsNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("BOWLBUG_ROCK", SlotOrNone("first")));
        Span<ushort> remaining = stackalloc ushort[3];
        remaining[0] = Monster("BOWLBUG_EGG");
        remaining[1] = Monster("BOWLBUG_SILK");
        remaining[2] = Monster("BOWLBUG_NECTAR");
        for (int i = 0; i < 2; i++)
        {
            int picked = rng.NextItem(3 - i);
            Add(ref count, spawns, Spawn(remaining[picked], SlotOrNone(i == 0 ? "middle" : "last")));
            for (int move = picked; move < 2 - i; move++)
                remaining[move] = remaining[move + 1];
        }
    }
}

[ContentId("BOWLBUGS_WEAK")]
public sealed class BowlbugsWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("BOWLBUG_ROCK", SlotOrNone("odd")));
        ReadOnlySpan<ushort> bugs = [Monster("BOWLBUG_EGG"), Monster("BOWLBUG_NECTAR")];
        Add(ref count, spawns, Spawn(bugs[rng.NextItem(2)], SlotOrNone("even")));
    }
}

[ContentId("BYGONE_EFFIGY_ELITE")]
public sealed class BygoneEffigyEliteEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("BYGONE_EFFIGY"));
    }
}

[ContentId("BYRDONIS_ELITE")]
public sealed class ByrdonisEliteEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("BYRDONIS"));
    }
}

[ContentId("CEREMONIAL_BEAST_BOSS")]
public sealed class CeremonialBeastBossEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("CEREMONIAL_BEAST"));
    }
}

[ContentId("CHOMPERS_NORMAL")]
public sealed class ChompersNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("CHOMPER", Kernel.NoSlot, 0));
        Add(ref count, spawns, Spawn("CHOMPER", Kernel.NoSlot, 1));
    }
}

[ContentId("CONSTRUCT_MENAGERIE_NORMAL")]
public sealed class ConstructMenagerieNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("PUNCH_CONSTRUCT"));
        Add(ref count, spawns, Spawn("CUBEX_CONSTRUCT"));
        Add(ref count, spawns, Spawn("CUBEX_CONSTRUCT"));
    }
}

[ContentId("CORPSE_SLUGS_NORMAL")]
public sealed class CorpseSlugsNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        int start = rng.NextInt(3);
        for (int i = 0; i < 3; i++)
            Add(ref count, spawns, Spawn("CORPSE_SLUG", Kernel.NoSlot, (byte)((start + i) % 3)));
    }
}

[ContentId("CORPSE_SLUGS_WEAK")]
public sealed class CorpseSlugsWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        int start = rng.NextInt(3);
        for (int i = 0; i < 2; i++)
            Add(ref count, spawns, Spawn("CORPSE_SLUG", Kernel.NoSlot, (byte)((start + i) % 3)));
    }
}

[ContentId("CUBEX_CONSTRUCT_NORMAL")]
public sealed class CubexConstructNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("CUBEX_CONSTRUCT"));
    }
}

[ContentId("CULTISTS_NORMAL")]
public sealed class CultistsNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("CALCIFIED_CULTIST"));
        Add(ref count, spawns, Spawn("DAMP_CULTIST"));
    }
}

[ContentId("DECIMILLIPEDE_ELITE")]
public sealed class DecimillipedeEliteEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["segment1", "segment2", "segment3"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        int start = rng.NextInt(3);
        Add(ref count, spawns, Spawn("DECIMILLIPEDE_SEGMENT_FRONT", SlotOrNone("segment1"), (byte)start));
        Add(ref count, spawns, Spawn("DECIMILLIPEDE_SEGMENT_MIDDLE", SlotOrNone("segment2"), (byte)((start + 1) % 3)));
        Add(ref count, spawns, Spawn("DECIMILLIPEDE_SEGMENT_BACK", SlotOrNone("segment3"), (byte)((start + 2) % 3)));
    }
}

[ContentId("DENSE_VEGETATION_EVENT_ENCOUNTER")]
public sealed class DenseVegetationEventEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["wriggler1", "wriggler2", "wriggler3", "wriggler4"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        for (int i = 0; i < 4; i++)
            Add(ref count, spawns, Spawn("WRIGGLER", SlotOrNone("wriggler" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }
}

[ContentId("DEVOTED_SCULPTOR_WEAK")]
public sealed class DevotedSculptorWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("DEVOTED_SCULPTOR"));
    }
}

[ContentId("ENTOMANCER_ELITE")]
public sealed class EntomancerEliteEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("ENTOMANCER"));
    }
}

[ContentId("EXOSKELETONS_NORMAL")]
public sealed class ExoskeletonsNormalEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["first", "second", "third", "fourth"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("EXOSKELETON", SlotOrNone("first")));
        Add(ref count, spawns, Spawn("EXOSKELETON", SlotOrNone("second")));
        Add(ref count, spawns, Spawn("EXOSKELETON", SlotOrNone("third")));
        Add(ref count, spawns, Spawn("EXOSKELETON", SlotOrNone("fourth")));
    }
}

[ContentId("EXOSKELETONS_WEAK")]
public sealed class ExoskeletonsWeakEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["first", "second", "third"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("EXOSKELETON", SlotOrNone("first")));
        Add(ref count, spawns, Spawn("EXOSKELETON", SlotOrNone("second")));
        Add(ref count, spawns, Spawn("EXOSKELETON", SlotOrNone("third")));
    }
}

[ContentId("FABRICATOR_NORMAL")]
public sealed class FabricatorNormalEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["bot1", "bot2", "fabricator", "bot3", "bot4"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("FABRICATOR", SlotOrNone("fabricator")));
    }
}

[ContentId("FAKE_MERCHANT_EVENT_ENCOUNTER")]
public sealed class FakeMerchantEventEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["merchant"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("FAKE_MERCHANT_MONSTER", SlotOrNone("merchant")));
    }
}

[ContentId("FLYCONID_NORMAL")]
public sealed class FlyconidNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        ReadOnlySpan<ushort> medium = [Monster("LEAF_SLIME_M"), Monster("TWIG_SLIME_M")];
        Add(ref count, spawns, Spawn(medium[rng.NextItem(2)]));
        Add(ref count, spawns, Spawn("FLYCONID"));
    }
}

[ContentId("FOGMOG_NORMAL")]
public sealed class FogmogNormalEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["illusion", "fogmog"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("FOGMOG", SlotOrNone("fogmog")));
    }
}

[ContentId("FOSSIL_STALKER_NORMAL")]
public sealed class FossilStalkerNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("FOSSIL_STALKER"));
    }
}

[ContentId("FROG_KNIGHT_NORMAL")]
public sealed class FrogKnightNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("FROG_KNIGHT"));
    }
}

[ContentId("FUZZY_WURM_CRAWLER_WEAK")]
public sealed class FuzzyWurmCrawlerWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("FUZZY_WURM_CRAWLER"));
    }
}

[ContentId("GLOBE_HEAD_NORMAL")]
public sealed class GlobeHeadNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("GLOBE_HEAD"));
    }
}

[ContentId("GREMLIN_MERC_NORMAL")]
public sealed class GremlinMercNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("GREMLIN_MERC", SlotOrNone("merc")));
    }
}

[ContentId("HAUNTED_SHIP_NORMAL")]
public sealed class HauntedShipNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("HAUNTED_SHIP"));
    }
}

[ContentId("HUNTER_KILLER_NORMAL")]
public sealed class HunterKillerNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("HUNTER_KILLER"));
    }
}

[ContentId("INFESTED_PRISMS_ELITE")]
public sealed class InfestedPrismsEliteEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("INFESTED_PRISM"));
    }
}

[ContentId("INKLETS_NORMAL")]
public sealed class InkletsNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("INKLET"));
        Add(ref count, spawns, Spawn("INKLET", Kernel.NoSlot, 1));
        Add(ref count, spawns, Spawn("INKLET"));
    }
}

[ContentId("KAISER_CRAB_BOSS")]
public sealed class KaiserCrabBossEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["crusher", "rocket"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("CRUSHER", SlotOrNone("crusher")));
        Add(ref count, spawns, Spawn("ROCKET", SlotOrNone("rocket")));
    }
}

[ContentId("KNIGHTS_ELITE")]
public sealed class KnightsEliteEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("FLAIL_KNIGHT", SlotOrNone("first")));
        Add(ref count, spawns, Spawn("SPECTRAL_KNIGHT", SlotOrNone("second")));
        Add(ref count, spawns, Spawn("MAGI_KNIGHT", SlotOrNone("third")));
    }
}

[ContentId("KNOWLEDGE_DEMON_BOSS")]
public sealed class KnowledgeDemonBossEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("KNOWLEDGE_DEMON"));
    }
}

[ContentId("LAGAVULIN_MATRIARCH_BOSS")]
public sealed class LagavulinMatriarchBossEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("LAGAVULIN_MATRIARCH"));
    }
}

[ContentId("LIVING_FOG_NORMAL")]
public sealed class LivingFogNormalEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["bomb1", "bomb2", "bomb3", "bomb4", "bomb5", "livingFog"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("LIVING_FOG", SlotOrNone("livingFog")));
    }
}

[ContentId("LOUSE_PROGENITOR_NORMAL")]
public sealed class LouseProgenitorNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("LOUSE_PROGENITOR"));
    }
}

[ContentId("MAWLER_NORMAL")]
public sealed class MawlerNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("MAWLER"));
    }
}

[ContentId("MECHA_KNIGHT_ELITE")]
public sealed class MechaKnightEliteEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("MECHA_KNIGHT"));
    }
}

[ContentId("MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER")]
public sealed class MysteriousKnightEventEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("MYSTERIOUS_KNIGHT"));
    }
}

[ContentId("MYTES_NORMAL")]
public sealed class MytesNormalEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["first", "second"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("MYTE", SlotOrNone("first")));
        Add(ref count, spawns, Spawn("MYTE", SlotOrNone("second")));
    }
}

[ContentId("NIBBITS_NORMAL")]
public sealed class NibbitsNormalEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["front", "back"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("NIBBIT", SlotOrNone("front"), 1));
        Add(ref count, spawns, Spawn("NIBBIT", SlotOrNone("back"), 2));
    }
}

[ContentId("NIBBITS_WEAK")]
public sealed class NibbitsWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("NIBBIT", Kernel.NoSlot, 3));
    }
}

[ContentId("OVERGROWTH_CRAWLERS")]
public sealed class OvergrowthCrawlersEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("SHRINKER_BEETLE"));
        Add(ref count, spawns, Spawn("FUZZY_WURM_CRAWLER"));
    }
}

[ContentId("OVICOPTER_NORMAL")]
public sealed class OvicopterNormalEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["egg1", "egg2", "egg3", "egg4", "egg5", "ovicopter"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("OVICOPTER", SlotOrNone("ovicopter")));
    }
}

[ContentId("OWL_MAGISTRATE_NORMAL")]
public sealed class OwlMagistrateNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("OWL_MAGISTRATE"));
    }
}

[ContentId("PHANTASMAL_GARDENERS_ELITE")]
public sealed class PhantasmalGardenersEliteEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["first", "second", "third", "fourth"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("PHANTASMAL_GARDENER", SlotOrNone("first")));
        Add(ref count, spawns, Spawn("PHANTASMAL_GARDENER", SlotOrNone("second")));
        Add(ref count, spawns, Spawn("PHANTASMAL_GARDENER", SlotOrNone("third")));
        Add(ref count, spawns, Spawn("PHANTASMAL_GARDENER", SlotOrNone("fourth")));
    }
}

[ContentId("PHROG_PARASITE_ELITE")]
public sealed class PhrogParasiteEliteEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["phrog", "wriggler1", "wriggler2", "wriggler3", "wriggler4"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("PHROG_PARASITE", SlotOrNone("phrog")));
    }
}

[ContentId("PUNCH_CONSTRUCT_NORMAL")]
public sealed class PunchConstructNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("PUNCH_CONSTRUCT"));
    }
}

[ContentId("PUNCH_OFF_EVENT_ENCOUNTER")]
public sealed class PunchOffEventEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("PUNCH_CONSTRUCT", Kernel.NoSlot, 1, rng.NextInt(2, 10)));
        Add(ref count, spawns, Spawn("PUNCH_CONSTRUCT", Kernel.NoSlot, 0, rng.NextInt(2, 10)));
    }
}

[ContentId("QUEEN_BOSS")]
public sealed class QueenBossEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["amalgam", "queen"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("TORCH_HEAD_AMALGAM", SlotOrNone("amalgam")));
        Add(ref count, spawns, Spawn("QUEEN", SlotOrNone("queen")));
    }
}

[ContentId("RUBY_RAIDERS_NORMAL")]
public sealed class RubyRaidersNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Span<ushort> raiders =
        [
            Monster("AXE_RUBY_RAIDER"), Monster("ASSASSIN_RUBY_RAIDER"), Monster("BRUTE_RUBY_RAIDER"),
            Monster("CROSSBOW_RUBY_RAIDER"), Monster("TRACKER_RUBY_RAIDER"),
        ];
        for (int i = 0; i < 3; i++)
        {
            int picked = rng.NextItem(5 - i);
            Add(ref count, spawns, Spawn(raiders[picked]));
            for (int move = picked; move < 4 - i; move++)
                raiders[move] = raiders[move + 1];
        }
    }
}

[ContentId("SCROLLS_OF_BITING_NORMAL")]
public sealed class ScrollsOfBitingNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        int start = rng.NextInt(3);
        for (int i = 0; i < 3; i++)
            Add(ref count, spawns, Spawn("SCROLL_OF_BITING", Kernel.NoSlot, (byte)((start + i) % 3)));
        Add(ref count, spawns, Spawn("SCROLL_OF_BITING", Kernel.NoSlot, 2));
    }
}

[ContentId("SCROLLS_OF_BITING_WEAK")]
public sealed class ScrollsOfBitingWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        int start = rng.NextInt(3);
        for (int i = 0; i < 3; i++)
            Add(ref count, spawns, Spawn("SCROLL_OF_BITING", Kernel.NoSlot, (byte)((start + i) % 3)));
    }
}

[ContentId("SEAPUNK_NORMAL")]
public sealed class SeapunkNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("CALCIFIED_CULTIST"));
        Add(ref count, spawns, Spawn("SEAPUNK"));
    }
}

[ContentId("SEAPUNK_WEAK")]
public sealed class SeapunkWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("SEAPUNK"));
    }
}

[ContentId("SEWER_CLAM_NORMAL")]
public sealed class SewerClamNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("SEWER_CLAM"));
    }
}

[ContentId("SHRINKER_BEETLE_WEAK")]
public sealed class ShrinkerBeetleWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("SHRINKER_BEETLE"));
    }
}

[ContentId("SKULKING_COLONY_ELITE")]
public sealed class SkulkingColonyEliteEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("SKULKING_COLONY"));
    }
}

[ContentId("SLIMED_BERSERKER_NORMAL")]
public sealed class SlimedBerserkerNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("SLIMED_BERSERKER"));
    }
}

[ContentId("SLIMES_NORMAL")]
public sealed class SlimesNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        bool leafFirst = rng.NextBool();
        Add(ref count, spawns, Spawn("TWIG_SLIME_M"));
        Add(ref count, spawns, Spawn("LEAF_SLIME_M"));
        Add(ref count, spawns, Spawn(leafFirst ? "LEAF_SLIME_S" : "TWIG_SLIME_S"));
        Add(ref count, spawns, Spawn(leafFirst ? "TWIG_SLIME_S" : "LEAF_SLIME_S"));
    }
}

[ContentId("SLIMES_WEAK")]
public sealed class SlimesWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        ReadOnlySpan<ushort> small = [Monster("LEAF_SLIME_S"), Monster("TWIG_SLIME_S")];
        ReadOnlySpan<ushort> medium = [Monster("LEAF_SLIME_M"), Monster("TWIG_SLIME_M")];
        int first = rng.NextItem(2);
        _ = rng.NextItem(1);
        int second = 1 - first;
        Add(ref count, spawns, Spawn(small[first]));
        Add(ref count, spawns, Spawn(medium[rng.NextItem(2)]));
        Add(ref count, spawns, Spawn(small[second]));
    }
}

[ContentId("SLITHERING_STRANGLER_NORMAL")]
public sealed class SlitheringStranglerNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        switch (rng.NextItem(3))
        {
            case 0:
                Add(ref count, spawns, Spawn("SNAPPING_JAXFRUIT"));
                break;
            case 1:
            {
                ReadOnlySpan<ushort> medium = [Monster("LEAF_SLIME_M"), Monster("TWIG_SLIME_M")];
                Add(ref count, spawns, Spawn(medium[rng.NextItem(2)]));
                break;
            }
            default:
            {
                ReadOnlySpan<ushort> small = [Monster("LEAF_SLIME_S"), Monster("TWIG_SLIME_S")];
                Add(ref count, spawns, Spawn(small[rng.NextItem(2)]));
                Add(ref count, spawns, Spawn(small[rng.NextItem(2)]));
                break;
            }
        }
        Add(ref count, spawns, Spawn("SLITHERING_STRANGLER"));
    }
}

[ContentId("SLUDGE_SPINNER_WEAK")]
public sealed class SludgeSpinnerWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("SLUDGE_SPINNER"));
    }
}

[ContentId("SLUMBERING_BEETLE_NORMAL")]
public sealed class SlumberingBeetleNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("BOWLBUG_ROCK", SlotOrNone("first")));
        Add(ref count, spawns, Spawn("BOWLBUG_SILK", SlotOrNone("second")));
        Add(ref count, spawns, Spawn("SLUMBERING_BEETLE", SlotOrNone("third")));
    }
}

[ContentId("SNAPPING_JAXFRUIT_NORMAL")]
public sealed class SnappingJaxfruitNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("SNAPPING_JAXFRUIT"));
        Add(ref count, spawns, Spawn("FLYCONID"));
    }
}

[ContentId("SOUL_FYSH_BOSS")]
public sealed class SoulFyshBossEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("SOUL_FYSH"));
    }
}

[ContentId("SOUL_NEXUS_ELITE")]
public sealed class SoulNexusEliteEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("SOUL_NEXUS"));
    }
}

[ContentId("SPINY_TOAD_NORMAL")]
public sealed class SpinyToadNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("SPINY_TOAD"));
    }
}

[ContentId("TERROR_EEL_ELITE")]
public sealed class TerrorEelEliteEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("TERROR_EEL"));
    }
}

[ContentId("TEST_SUBJECT_BOSS")]
public sealed class TestSubjectBossEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("TEST_SUBJECT"));
    }
}

[ContentId("THE_ARCHITECT_EVENT_ENCOUNTER")]
public sealed class TheArchitectEventEncounter : EncounterDef
{
    // The native event dialogue ends the room before the player's first action; the Architect's
    // 9999-HP NOTHING_MOVE loop is never a searchable combat.
    public override bool EndsByEventScript => true;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("ARCHITECT"));
    }
}

[ContentId("THE_INSATIABLE_BOSS")]
public sealed class TheInsatiableBossEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("THE_INSATIABLE"));
    }
}

[ContentId("THE_KIN_BOSS")]
public sealed class TheKinBossEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["slot1", "slot2", "leaderSlot"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("KIN_FOLLOWER", SlotOrNone("slot1"), 1));
        Add(ref count, spawns, Spawn("KIN_FOLLOWER", SlotOrNone("slot2"), 0));
        Add(ref count, spawns, Spawn("KIN_PRIEST", SlotOrNone("leaderSlot")));
    }
}

[ContentId("THE_LOST_AND_FORGOTTEN_NORMAL")]
public sealed class TheLostAndForgottenNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("THE_LOST"));
        Add(ref count, spawns, Spawn("THE_FORGOTTEN"));
    }
}

[ContentId("THE_OBSCURA_NORMAL")]
public sealed class TheObscuraNormalEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["illusion", "obscura"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("THE_OBSCURA", SlotOrNone("obscura")));
    }
}

[ContentId("THIEVING_HOPPER_WEAK")]
public sealed class ThievingHopperWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("THIEVING_HOPPER"));
    }
}

[ContentId("TOADPOLES_WEAK")]
public sealed class ToadpolesWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("TOADPOLE", Kernel.NoSlot, 1));
        Add(ref count, spawns, Spawn("TOADPOLE", Kernel.NoSlot, 2));
    }
}

[ContentId("TUNNELER_NORMAL")]
public sealed class TunnelerNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("CHOMPER", Kernel.NoSlot, 1));
        Add(ref count, spawns, Spawn("TUNNELER"));
    }
}

[ContentId("TUNNELER_WEAK")]
public sealed class TunnelerWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("TUNNELER"));
    }
}

[ContentId("TURRET_OPERATOR_WEAK")]
public sealed class TurretOperatorWeakEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("LIVING_SHIELD"));
        Add(ref count, spawns, Spawn("TURRET_OPERATOR"));
    }
}

[ContentId("TWO_TAILED_RATS_NORMAL")]
public sealed class TwoTailedRatsNormalEncounter : EncounterDef
{
    private static readonly string[] SlotNames = ["first", "second", "third", "fourth", "fifth"];
    public override IReadOnlyList<string> Slots => SlotNames;

    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        int start = rng.NextInt(3);
        Add(ref count, spawns, Spawn("TWO_TAILED_RAT", SlotOrNone("third"), (byte)start));
        Add(ref count, spawns, Spawn("TWO_TAILED_RAT", SlotOrNone("fourth"), (byte)((start + 1) % 3)));
        Add(ref count, spawns, Spawn("TWO_TAILED_RAT", SlotOrNone("fifth"), (byte)((start + 2) % 3)));
    }
}

[ContentId("VANTOM_BOSS")]
public sealed class VantomBossEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("VANTOM"));
    }
}

[ContentId("VINE_SHAMBLER_NORMAL")]
public sealed class VineShamblerNormalEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("VINE_SHAMBLER"));
    }
}

[ContentId("WATERFALL_GIANT_BOSS")]
public sealed class WaterfallGiantBossEncounter : EncounterDef
{
    public override void Generate(ref EncounterRng rng, Span<EncounterSpawn> spawns, out int count)
    {
        count = 0;
        Add(ref count, spawns, Spawn("WATERFALL_GIANT"));
    }
}
