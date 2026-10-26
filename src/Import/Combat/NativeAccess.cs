// Reads native models and private fields for the importer. This is the only layer in BlitzSolver
// allowed to depend on the game model types and their non-public state.
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Unlocks;

using Creature = MegaCrit.Sts2.Core.Entities.Creatures.Creature;

namespace BlitzSolver.Import;

/// <summary>Central native read layer. All failures are explicit, never defaulted.</summary>
internal static class NativeAccess
{
    private static readonly Assembly GameAssembly = typeof(AbstractModel).Assembly;
    private static readonly FieldInfo NightmareDataField = RequiredField(typeof(NightmarePower), "_internalData");
    private static readonly FieldInfo PaleBlueDotDataField = RequiredField(typeof(PaleBlueDotPower), "_internalData");
    private static readonly FieldInfo DampenDataField = RequiredField(typeof(DampenPower), "_internalData");
    private static readonly FieldInfo TenderCardsPlayedField = RequiredField(typeof(TenderPower), "_cardsPlayedThisTurn");
    private static readonly FieldInfo NemesisField = RequiredField(typeof(NemesisPower), "_shouldApplyIntangible");
    private static readonly FieldInfo RuinedHelmetUsedField = RequiredField(typeof(RuinedHelmet), "_usedThisCombat");
    private static readonly FieldInfo ThrowingAxeUsedField = RequiredField(typeof(ThrowingAxe), "_usedThisCombat");
    private static readonly FieldInfo UnsettlingLampField = RequiredField(typeof(UnsettlingLamp), "_isFinishedTriggering");

    public static Assembly OriginalAssembly => GameAssembly;

    /// <summary>Read-only next-creature id used by the native state dump in TestHost.</summary>
    public static uint NextCreatureId(MegaCrit.Sts2.Core.Combat.CombatState state)
        => state._nextCreatureId;

    /// <summary>Read-only unlock counts used by the native state dump in TestHost.</summary>
    public static int UnlockedEpochCount(UnlockState state) => state._unlockedEpochIds.Count;

    public static int EncountersSeenCount(UnlockState state) => state._encountersSeen.Count;

    /// <summary>Whether the model's type belongs to the audited game assembly.</summary>
    public static bool IsOriginal(AbstractModel model)
        => ReferenceEquals(model.GetType().Assembly, GameAssembly);

    /// <summary>Rejects models loaded from any assembly other than the game's <c>sts2</c>.</summary>
    public static void RequireOriginal(AbstractModel model)
    {
        Assembly assembly = model.GetType().Assembly;
        if (!ReferenceEquals(assembly, GameAssembly))
            throw new UnsupportedImportException(ImportReason.ThirdPartyContent,
                $"{model.GetType().FullName} 来自第三方程序集 {assembly.GetName().Name}");
    }

    public static void RequireOriginalType(Type type)
    {
        Assembly assembly = type.Assembly;
        if (!ReferenceEquals(assembly, GameAssembly))
            throw new UnsupportedImportException(ImportReason.ThirdPartyContent,
                $"{type.FullName} 来自第三方程序集 {assembly.GetName().Name}");
    }

    public static T Data<T>(AbstractModel model, FieldInfo field, string name)
        where T : class
        => field.GetValue(model) as T
            ?? throw new UnsupportedImportException(ImportReason.MissingPrivateField,
                $"{model.GetType().Name}.{name} 缺失");

    /// <summary>Reads <c>PowerModel._internalData</c> as the requested nested data type.</summary>
    public static T PowerData<T>(PowerModel power, string name)
        where T : class
    {
        object? data = RequiredField(typeof(PowerModel), "_internalData").GetValue(power);
        return data as T
            ?? throw new UnsupportedImportException(ImportReason.MissingPrivateField,
                $"{power.GetType().Name}._internalData({name}) 缺失或类型不符");
    }

    public static CardModel? NightmareSelection(NightmarePower power)
    {
        object data = Data<object>(power, NightmareDataField, "_internalData");
        object? selected = ReadMember(data, "selectedCard");
        return selected as CardModel;
    }

    public static bool PaleBlueDotActivated(PaleBlueDotPower power)
    {
        object data = Data<object>(power, PaleBlueDotDataField, "_internalData");
        return ReadMember(data, "alreadyActivatedThisTurn") is true;
    }

    public static (HashSet<Creature> Casters, Dictionary<CardModel, int> OriginalUpgrades) DampenData(DampenPower power)
    {
        object data = Data<object>(power, DampenDataField, "_internalData");
        HashSet<Creature> casters = ReadMember(data, "casters") as HashSet<Creature>
            ?? throw new UnsupportedImportException(ImportReason.MissingPrivateField, "DampenPower.casters 缺失");
        Dictionary<CardModel, int> upgrades = ReadMember(data, "downgradedCardsToOldUpgradeLevels")
            as Dictionary<CardModel, int>
            ?? throw new UnsupportedImportException(ImportReason.MissingPrivateField,
                "DampenPower.downgradedCardsToOldUpgradeLevels 缺失");
        return (casters, upgrades);
    }

    public static int TenderCardsPlayed(TenderPower power)
        => TenderCardsPlayedField.GetValue(power) is int value
            ? value
            : throw new UnsupportedImportException(ImportReason.MissingPrivateField, "TenderPower._cardsPlayedThisTurn 缺失");

    public static bool NemesisShouldApplyIntangible(NemesisPower power)
        => NemesisField.GetValue(power) is bool value
            ? value
            : throw new UnsupportedImportException(ImportReason.MissingPrivateField, "NemesisPower._shouldApplyIntangible 缺失");

    public static bool RuinedHelmetUsed(RuinedHelmet relic)
        => RuinedHelmetUsedField.GetValue(relic) is bool value
            ? value
            : throw new UnsupportedImportException(ImportReason.MissingPrivateField, "RuinedHelmet._usedThisCombat 缺失");

    public static bool ThrowingAxeUsed(ThrowingAxe relic)
        => ThrowingAxeUsedField.GetValue(relic) is bool value
            ? value
            : throw new UnsupportedImportException(ImportReason.MissingPrivateField, "ThrowingAxe._usedThisCombat 缺失");

    public static bool UnsettlingLampFinished(UnsettlingLamp relic)
        => UnsettlingLampField.GetValue(relic) is bool value
            ? value
            : throw new UnsupportedImportException(ImportReason.MissingPrivateField,
                "UnsettlingLamp._isFinishedTriggering 缺失");

    /// <summary>
    /// The native model field/property read by <c>SimulatedCombatState.GetMonsterInt</c>. Root
    /// branch dictionaries are absent; the model value is what the first read returns.
    /// </summary>
    public static int MonsterInt(MonsterModel monster, string name)
    {
        if (monster.GetType().Name == "WaterfallGiant" && name is "PressureGunBonus" or "SteamEruptionDamage")
            return 0;
        if (monster.GetType().Name == "Fabricator" && name == "fabricator_last_spawn")
        {
            object? spawned = ReadMember(monster, "_lastSpawned");
            return (spawned as MonsterModel)?.Id.Entry switch
            {
                "GUARDBOT" => 1,
                "NOISEBOT" => 2,
                "ZAPBOT" => 3,
                "STABBOT" => 4,
                _ => 0,
            };
        }
        object? value = ReadMember(monster, name)
            ?? throw new UnsupportedImportException(ImportReason.MissingPrivateField,
                $"{monster.GetType().Name}.{name} 不存在");
        return value switch
        {
            bool boolean => boolean ? 1 : 0,
            int integer => integer,
            IConvertible convertible => convertible.ToInt32(null),
            _ => throw new UnsupportedImportException(ImportReason.MissingPrivateField,
                $"{monster.GetType().Name}.{name} 不是整数"),
        };
    }

    public static string CurrentMoveId(MonsterModel monster)
        => monster.NextMove?.Id
            ?? throw new UnsupportedImportException(ImportReason.MissingPrivateField,
                $"{monster.Id.Entry} 没有当前行动");

    public static IReadOnlyList<string> StateLog(MonsterModel monster)
    {
        MonsterMoveStateMachine machine = monster.MoveStateMachine
            ?? throw new UnsupportedImportException(ImportReason.MissingPrivateField,
                $"{monster.Id.Entry} 没有行动状态机");
        return machine.StateLog.Select(state => state.Id).ToArray();
    }

    public static string? MoveFollowUpId(MonsterModel monster, string moveId)
    {
        MonsterMoveStateMachine machine = monster.MoveStateMachine
            ?? throw new UnsupportedImportException(ImportReason.MissingPrivateField,
                $"{monster.Id.Entry} 没有行动状态机");
        // The synthetic STUNNED and REVIVE_MOVE states are created by Creature.StunInternal and
        // IllusionPower.AfterDeath and are only installed as NextMove; they are never registered in
        // States or StateLog. Their dynamic follow-up lives on that MoveState object, so read the
        // current move first and fall back to the registered state table for every earlier move.
        if (string.Equals(monster.NextMove.Id, moveId, StringComparison.Ordinal))
            return monster.NextMove.FollowUpState?.Id ?? monster.NextMove.FollowUpStateId;
        return machine.States.TryGetValue(moveId, out MonsterState? state) && state is MoveState move
            ? move.FollowUpState?.Id ?? move.FollowUpStateId
            : null;
    }

    public static decimal OrbPrivateValue(OrbModel orb)
    {
        Type type = orb.GetType();
        string? name = type.Name switch
        {
            "DarkOrb" => "_evokeVal",
            "GlassOrb" => "_passiveVal",
            _ => null,
        };
        if (name == null)
            return 0m;
        FieldInfo field = RequiredField(type, name);
        object? value = field.GetValue(orb);
        return value is decimal decimalValue
            ? decimalValue
            : throw new UnsupportedImportException(ImportReason.MissingPrivateField,
                $"{type.Name}.{name} 不是 decimal");
    }

    public static bool HasNonVanillaModels(Player player, CombatStateView view)
    {
        foreach (CardModel card in player.Deck.Cards)
            if (!ReferenceEquals(card.GetType().Assembly, GameAssembly))
                return true;
        foreach (RelicModel relic in player.Relics)
            if (!ReferenceEquals(relic.GetType().Assembly, GameAssembly))
                return true;
        foreach (PotionModel? potion in player.PotionSlots)
            if (potion != null && !ReferenceEquals(potion.GetType().Assembly, GameAssembly))
                return true;
        foreach (Creature creature in view.Creatures)
        {
            foreach (PowerModel power in creature.Powers)
                if (!ReferenceEquals(power.GetType().Assembly, GameAssembly))
                    return true;
            if (creature.Monster != null && !ReferenceEquals(creature.Monster.GetType().Assembly, GameAssembly))
                return true;
        }
        return false;
    }

    /// <summary>The creature collections the importer reads, kept free of engine types.</summary>
    public readonly record struct CombatStateView(
        IReadOnlyList<Creature> Creatures,
        IReadOnlyList<Creature> Enemies,
        IReadOnlyList<Creature> Allies);

    private static object? ReadMember(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target)
            ?? target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);

    private static FieldInfo RequiredField(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        throw new UnsupportedImportException(ImportReason.MissingPrivateField,
            $"找不到字段 {type.FullName}.{name}");
    }
}
