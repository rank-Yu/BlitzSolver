// Run content id catalog. Generated from the sts2 0.111.0 ModelDb catalog dump; only vanilla,
// non-deprecated, non-test content is listed. The array order is the id order and is append-only
// within a game version: adding content re-sorts ids, so RunCodec.FormatVersion must be bumped.
// See docs/run-state-layout.md.
using System;
using System.Collections.Generic;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Simulation.Run;

/// <summary>Kind of a model reference carried by run state.</summary>
public enum RunRefKind : byte
{
    None = 0,
    Character,
    Act,
    Encounter,
    Event,
    Card,
    Relic,
    Potion,
    Monster,
    Enchantment,
    Affliction,
    Orb,
}

/// <summary>A model reference: kind plus the id in the matching content table.</summary>
public readonly record struct RunRef(RunRefKind Kind, ushort Id)
{
    public static readonly RunRef None = new(RunRefKind.None, 0);

    public bool IsNone => Kind == RunRefKind.None;
}

/// <summary>
/// Native model-id to dense <c>ushort</c> ids for the run-level content that the combat
/// <see cref="Registry"/> does not own (characters, acts, encounters, events and unlock epochs).
/// </summary>
public static class RunCatalog
{
    public const int CharacterCount = 5;
    public const int ActCount = 4;
    public const int EncounterCount = 89;
    public const int EventCount = 66;
    public const int EpochCount = 57;

    private static readonly string[] Characters =
    [
        "DEFECT", // 0
        "IRONCLAD", // 1
        "NECROBINDER", // 2
        "REGENT", // 3
        "SILENT", // 4
    ];
    private static readonly string[] Acts =
    [
        "GLORY", // 0
        "HIVE", // 1
        "OVERGROWTH", // 2
        "UNDERDOCKS", // 3
    ];
    private static readonly string[] Encounters =
    [
        "AEONGLASS_BOSS", // 0
        "AXEBOTS_NORMAL", // 1
        "BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER", // 2
        "BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER", // 3
        "BATTLEWORN_DUMMY_EVENT_V3_ENCOUNTER", // 4
        "BOWLBUGS_NORMAL", // 5
        "BOWLBUGS_WEAK", // 6
        "BYGONE_EFFIGY_ELITE", // 7
        "BYRDONIS_ELITE", // 8
        "CEREMONIAL_BEAST_BOSS", // 9
        "CHOMPERS_NORMAL", // 10
        "CONSTRUCT_MENAGERIE_NORMAL", // 11
        "CORPSE_SLUGS_NORMAL", // 12
        "CORPSE_SLUGS_WEAK", // 13
        "CUBEX_CONSTRUCT_NORMAL", // 14
        "CULTISTS_NORMAL", // 15
        "DECIMILLIPEDE_ELITE", // 16
        "DENSE_VEGETATION_EVENT_ENCOUNTER", // 17
        "DEVOTED_SCULPTOR_WEAK", // 18
        "ENTOMANCER_ELITE", // 19
        "EXOSKELETONS_NORMAL", // 20
        "EXOSKELETONS_WEAK", // 21
        "FABRICATOR_NORMAL", // 22
        "FAKE_MERCHANT_EVENT_ENCOUNTER", // 23
        "FLYCONID_NORMAL", // 24
        "FOGMOG_NORMAL", // 25
        "FOSSIL_STALKER_NORMAL", // 26
        "FROG_KNIGHT_NORMAL", // 27
        "FUZZY_WURM_CRAWLER_WEAK", // 28
        "GLOBE_HEAD_NORMAL", // 29
        "GREMLIN_MERC_NORMAL", // 30
        "HAUNTED_SHIP_NORMAL", // 31
        "HUNTER_KILLER_NORMAL", // 32
        "INFESTED_PRISMS_ELITE", // 33
        "INKLETS_NORMAL", // 34
        "KAISER_CRAB_BOSS", // 35
        "KNIGHTS_ELITE", // 36
        "KNOWLEDGE_DEMON_BOSS", // 37
        "LAGAVULIN_MATRIARCH_BOSS", // 38
        "LIVING_FOG_NORMAL", // 39
        "LOUSE_PROGENITOR_NORMAL", // 40
        "MAWLER_NORMAL", // 41
        "MECHA_KNIGHT_ELITE", // 42
        "MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER", // 43
        "MYTES_NORMAL", // 44
        "NIBBITS_NORMAL", // 45
        "NIBBITS_WEAK", // 46
        "OVERGROWTH_CRAWLERS", // 47
        "OVICOPTER_NORMAL", // 48
        "OWL_MAGISTRATE_NORMAL", // 49
        "PHANTASMAL_GARDENERS_ELITE", // 50
        "PHROG_PARASITE_ELITE", // 51
        "PUNCH_CONSTRUCT_NORMAL", // 52
        "PUNCH_OFF_EVENT_ENCOUNTER", // 53
        "QUEEN_BOSS", // 54
        "RUBY_RAIDERS_NORMAL", // 55
        "SCROLLS_OF_BITING_NORMAL", // 56
        "SCROLLS_OF_BITING_WEAK", // 57
        "SEAPUNK_NORMAL", // 58
        "SEAPUNK_WEAK", // 59
        "SEWER_CLAM_NORMAL", // 60
        "SHRINKER_BEETLE_WEAK", // 61
        "SKULKING_COLONY_ELITE", // 62
        "SLIMED_BERSERKER_NORMAL", // 63
        "SLIMES_NORMAL", // 64
        "SLIMES_WEAK", // 65
        "SLITHERING_STRANGLER_NORMAL", // 66
        "SLUDGE_SPINNER_WEAK", // 67
        "SLUMBERING_BEETLE_NORMAL", // 68
        "SNAPPING_JAXFRUIT_NORMAL", // 69
        "SOUL_FYSH_BOSS", // 70
        "SOUL_NEXUS_ELITE", // 71
        "SPINY_TOAD_NORMAL", // 72
        "TERROR_EEL_ELITE", // 73
        "TEST_SUBJECT_BOSS", // 74
        "THE_ARCHITECT_EVENT_ENCOUNTER", // 75
        "THE_INSATIABLE_BOSS", // 76
        "THE_KIN_BOSS", // 77
        "THE_LOST_AND_FORGOTTEN_NORMAL", // 78
        "THE_OBSCURA_NORMAL", // 79
        "THIEVING_HOPPER_WEAK", // 80
        "TOADPOLES_WEAK", // 81
        "TUNNELER_NORMAL", // 82
        "TUNNELER_WEAK", // 83
        "TURRET_OPERATOR_WEAK", // 84
        "TWO_TAILED_RATS_NORMAL", // 85
        "VANTOM_BOSS", // 86
        "VINE_SHAMBLER_NORMAL", // 87
        "WATERFALL_GIANT_BOSS", // 88
    ];
    private static readonly string[] Events =
    [
        "ABYSSAL_BATHS", // 0
        "AMALGAMATOR", // 1
        "AROMA_OF_CHAOS", // 2
        "BATTLEWORN_DUMMY", // 3
        "BRAIN_LEECH", // 4
        "BUGSLAYER", // 5
        "BYRDONIS_NEST", // 6
        "COLORFUL_PHILOSOPHERS", // 7
        "COLOSSAL_FLOWER", // 8
        "CRYSTAL_SPHERE", // 9
        "DARV", // 10
        "DENSE_VEGETATION", // 11
        "DOLL_ROOM", // 12
        "DOORS_OF_LIGHT_AND_DARK", // 13
        "DROWNING_BEACON", // 14
        "ENDLESS_CONVEYOR", // 15
        "FAKE_MERCHANT", // 16
        "FIELD_OF_MAN_SIZED_HOLES", // 17
        "GRAVE_OF_THE_FORGOTTEN", // 18
        "HUNGRY_FOR_MUSHROOMS", // 19
        "INFESTED_AUTOMATON", // 20
        "JUNGLE_MAZE_ADVENTURE", // 21
        "LOST_WISP", // 22
        "LUMINOUS_CHOIR", // 23
        "MORPHIC_GROVE", // 24
        "NEOW", // 25
        "NONUPEIPE", // 26
        "OROBAS", // 27
        "PAEL", // 28
        "POTION_COURIER", // 29
        "PUNCH_OFF", // 30
        "RANWID_THE_ELDER", // 31
        "REFLECTIONS", // 32
        "RELIC_TRADER", // 33
        "ROOM_FULL_OF_CHEESE", // 34
        "ROUND_TEA_PARTY", // 35
        "SAPPHIRE_SEED", // 36
        "SELF_HELP_BOOK", // 37
        "SLIPPERY_BRIDGE", // 38
        "SPIRALING_WHIRLPOOL", // 39
        "SPIRIT_GRAFTER", // 40
        "STONE_OF_ALL_TIME", // 41
        "SUNKEN_STATUE", // 42
        "SUNKEN_TREASURY", // 43
        "SYMBIOTE", // 44
        "TABLET_OF_TRUTH", // 45
        "TANX", // 46
        "TEA_MASTER", // 47
        "TEZCATARA", // 48
        "THE_ARCHITECT", // 49
        "THE_FUTURE_OF_POTIONS", // 50
        "THE_LANTERN_KEY", // 51
        "THE_LEGENDS_WERE_TRUE", // 52
        "THIS_OR_THAT", // 53
        "TINKER_TIME", // 54
        "TRASH_HEAP", // 55
        "TRIAL", // 56
        "UNREST_SITE", // 57
        "VAKUU", // 58
        "WAR_HISTORIAN_REPY", // 59
        "WATERLOGGED_SCRIPTORIUM", // 60
        "WELCOME_TO_WONGOS", // 61
        "WELLSPRING", // 62
        "WHISPERING_HOLLOW", // 63
        "WOOD_CARVINGS", // 64
        "ZEN_WEAVER", // 65
    ];
    private static readonly string[] Epochs =
    [
        "ACT2_B_EPOCH", // 0
        "ACT3_B_EPOCH", // 1
        "COLORLESS1_EPOCH", // 2
        "COLORLESS2_EPOCH", // 3
        "COLORLESS3_EPOCH", // 4
        "COLORLESS4_EPOCH", // 5
        "COLORLESS5_EPOCH", // 6
        "CUSTOM_AND_SEEDS_EPOCH", // 7
        "DAILY_RUN_EPOCH", // 8
        "DARV_EPOCH", // 9
        "DEFECT1_EPOCH", // 10
        "DEFECT2_EPOCH", // 11
        "DEFECT3_EPOCH", // 12
        "DEFECT4_EPOCH", // 13
        "DEFECT5_EPOCH", // 14
        "DEFECT6_EPOCH", // 15
        "DEFECT7_EPOCH", // 16
        "EVENT1_EPOCH", // 17
        "EVENT2_EPOCH", // 18
        "EVENT3_EPOCH", // 19
        "IRONCLAD2_EPOCH", // 20
        "IRONCLAD3_EPOCH", // 21
        "IRONCLAD4_EPOCH", // 22
        "IRONCLAD5_EPOCH", // 23
        "IRONCLAD6_EPOCH", // 24
        "IRONCLAD7_EPOCH", // 25
        "NECROBINDER1_EPOCH", // 26
        "NECROBINDER2_EPOCH", // 27
        "NECROBINDER3_EPOCH", // 28
        "NECROBINDER4_EPOCH", // 29
        "NECROBINDER5_EPOCH", // 30
        "NECROBINDER6_EPOCH", // 31
        "NECROBINDER7_EPOCH", // 32
        "NEOW_EPOCH", // 33
        "OROBAS_EPOCH", // 34
        "POTION1_EPOCH", // 35
        "POTION2_EPOCH", // 36
        "REGENT1_EPOCH", // 37
        "REGENT2_EPOCH", // 38
        "REGENT3_EPOCH", // 39
        "REGENT4_EPOCH", // 40
        "REGENT5_EPOCH", // 41
        "REGENT6_EPOCH", // 42
        "REGENT7_EPOCH", // 43
        "RELIC1_EPOCH", // 44
        "RELIC2_EPOCH", // 45
        "RELIC3_EPOCH", // 46
        "RELIC4_EPOCH", // 47
        "RELIC5_EPOCH", // 48
        "SILENT1_EPOCH", // 49
        "SILENT2_EPOCH", // 50
        "SILENT3_EPOCH", // 51
        "SILENT4_EPOCH", // 52
        "SILENT5_EPOCH", // 53
        "SILENT6_EPOCH", // 54
        "SILENT7_EPOCH", // 55
        "UNDERDOCKS_EPOCH", // 56
    ];

    private static readonly Dictionary<string, ushort> CharacterIds = Build(Characters);
    private static readonly Dictionary<string, ushort> ActIds = Build(Acts);
    private static readonly Dictionary<string, ushort> EncounterIds = Build(Encounters);
    private static readonly Dictionary<string, ushort> EventIds = Build(Events);
    private static readonly Dictionary<string, ushort> EpochIds = Build(Epochs);

    private static Dictionary<string, ushort> Build(string[] names)
    {
        Dictionary<string, ushort> ids = new(names.Length, StringComparer.Ordinal);
        for (int index = 0; index < names.Length; index++)
            ids.Add(names[index], (ushort)index);
        return ids;
    }

    public static bool TryCharacter(string nativeId, out ushort id) => CharacterIds.TryGetValue(nativeId, out id);
    public static bool TryAct(string nativeId, out ushort id) => ActIds.TryGetValue(nativeId, out id);
    public static bool TryEncounter(string nativeId, out ushort id) => EncounterIds.TryGetValue(nativeId, out id);
    public static bool TryEvent(string nativeId, out ushort id) => EventIds.TryGetValue(nativeId, out id);
    public static bool TryEpoch(string nativeId, out ushort id) => EpochIds.TryGetValue(nativeId, out id);

    /// <summary>Native id of a catalog entry, for dumps and diagnostics.</summary>
    public static string CharacterName(ushort id) => Name(Characters, id, "character");
    public static string ActName(ushort id) => Name(Acts, id, "act");
    public static string EncounterName(ushort id) => Name(Encounters, id, "encounter");
    public static string EventName(ushort id) => Name(Events, id, "event");
    public static string EpochName(ushort id) => Name(Epochs, id, "epoch");

    private static string Name(string[] names, ushort id, string what)
    {
        if (id >= names.Length)
            throw new ArgumentOutOfRangeException(nameof(id), id, $"{what} id 超出 {names.Length}。");
        return names[id];
    }

    public static bool IsKnown(RunRef reference) => reference.Kind switch
    {
        RunRefKind.None => true,
        RunRefKind.Character => reference.Id < CharacterCount,
        RunRefKind.Act => reference.Id < ActCount,
        RunRefKind.Encounter => reference.Id < EncounterCount,
        RunRefKind.Event => reference.Id < EventCount,
        RunRefKind.Card => reference.Id < Registry.CardCount,
        RunRefKind.Relic => reference.Id < Registry.RelicCount,
        RunRefKind.Potion => reference.Id < Registry.PotionCount,
        RunRefKind.Monster => reference.Id < Registry.MonsterCount,
        RunRefKind.Enchantment => reference.Id < Registry.EnchantmentCount,
        RunRefKind.Affliction => reference.Id < Registry.AfflictionCount,
        RunRefKind.Orb => reference.Id < Registry.OrbCount,
        _ => false,
    };
}
