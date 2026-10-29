using Sts2Solver.Simulation.Run;

namespace Sts2Solver.Import;

/// <summary>Stable rejection codes for native-to-Battle import.</summary>
public static class ImportReason
{
    public const string MultiPlayer = "multi-player";
    public const string RootPhase = "root-phase";
    public const string PendingChoice = "pending-choice";
    public const string UnsupportedCard = ContentReason.UnsupportedCard;
    public const string UnsupportedPower = ContentReason.UnsupportedPower;
    public const string UnsupportedRelic = ContentReason.UnsupportedRelic;
    public const string UnsupportedPotion = ContentReason.UnsupportedPotion;
    public const string UnsupportedOrb = "unsupported-orb";
    public const string UnsupportedMonster = ContentReason.UnsupportedMonster;
    public const string UnsupportedEnchantment = ContentReason.UnsupportedEnchantment;
    public const string UnsupportedAffliction = "unsupported-affliction";
    public const string UnsupportedClosure = ContentReason.UnsupportedClosure;
    public const string MissingMirrorRisk = "missing-mirror-risk";
    public const string Unrepresentable = "unrepresentable-state";
    public const string Capacity = ContentReason.Capacity;
    public const string ThirdPartyContent = "third-party-content";
    public const string MissingPrivateField = "missing-private-field";
    public const string UnclaimedPrivateState = "unclaimed-private-state";
    public const string RulesMismatch = "rules-mismatch";
    public const string KnownEnemyHistory = "known-enemy-history";
    public const string PrefixPlan = "prefix-plan";
    public const string RunDeckHook = "run-deck-hook";
    public const string TestContent = "test-content";
    public const string DeprecatedContent = "deprecated-content";
    public const string UnsupportedPolicy = "unsupported-policy";
    public const string UnsupportedHealingCertificate = "unsupported-healing-certificate";

    // Run-state import (R1b). These codes are stable; new codes are appended.
    public const string UnsupportedRun = "unsupported-run";
    public const string UnsupportedGameMode = "unsupported-game-mode";
    public const string UnsupportedModifier = "unsupported-modifier";
    public const string UnsupportedCharacter = "unsupported-character";
    public const string UnsupportedAct = "unsupported-act";
    public const string UnsupportedEncounter = ContentReason.UnsupportedEncounter;
    public const string UnsupportedEvent = "unsupported-event";
    public const string UnsupportedRoom = "unsupported-room";
    public const string UnsupportedMap = "unsupported-map";
    public const string UnsupportedUnlock = "unsupported-unlock";
    public const string UnsupportedProperty = "unsupported-property";
    public const string UnsupportedPotionSlot = "unsupported-potion-slot";
    public const string UnsupportedRelicState = "unsupported-relic-state";
}

/// <summary>
/// Native state cannot be represented by <see cref="Sts2Solver.Simulation.Combat.Battle"/>. The importer
/// rejects the whole root; it never returns a partially defaulted battle.
/// </summary>
public sealed class UnsupportedImportException(string reason, string detail)
    : Exception($"Sts2Solver 导入拒绝 reason={reason}：{detail}")
{
    /// <summary>Stable machine-readable reason code (see <see cref="ImportReason"/>).</summary>
    public string Reason { get; } = reason;

    public string Detail { get; } = detail;
}
