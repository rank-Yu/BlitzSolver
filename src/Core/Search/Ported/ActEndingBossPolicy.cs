// Ported from CombatSolver src/Search/ActEndingBossPolicy.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

internal enum BossHpRelief
{
    None,
    ActClearHeal,
    RunEnding,
}

internal readonly record struct PostCombatRelicHealProfile(
    int UnconditionalHeal,
    int WoundedHeal,
    int WoundedHpPercent)
{
    public static PostCombatRelicHealProfile None => default;

    public bool HasAnyHeal => UnconditionalHeal > 0 || WoundedHeal > 0;

    public int HealFor(int finalHp, int finalMaxHp)
    {
        int capacity = Math.Max(0, UnconditionalHeal);
        if (WoundedHeal > 0 && finalHp <= WoundedThreshold(finalMaxHp))
            capacity += WoundedHeal;
        return ClampToHeadroom(capacity, finalHp, finalMaxHp);
    }

    public int MonotoneHealFor(int finalHp, int finalMaxHp)
        => ClampToHeadroom(Math.Max(0, UnconditionalHeal), finalHp, finalMaxHp);

    private int WoundedThreshold(int finalMaxHp)
        => Math.Max(0, finalMaxHp) * Math.Max(0, WoundedHpPercent) / 100;

    private static int ClampToHeadroom(int capacity, int finalHp, int finalMaxHp)
        => Math.Clamp(capacity, 0, Math.Max(0, finalMaxHp - finalHp));
}

internal static class ActEndingBossPolicy
{
    public static int RawHpRequiredForPersistentValue(
        int persistentHpValue,
        BossHpRelief bossHpRelief)
    {
        if (persistentHpValue <= 0)
            return 0;
        return bossHpRelief switch
        {
            BossHpRelief.ActClearHeal => persistentHpValue * 5,
            BossHpRelief.RunEnding => int.MaxValue / 4,
            _ => persistentHpValue,
        };
    }

    public static int PersistentValueOfRecoveredHp(int recoveredHp, BossHpRelief bossHpRelief)
        => recoveredHp <= 0
            ? 0
            : recoveredHp / RawHpRequiredForPersistentValue(1, bossHpRelief);

    public static int StrategicHpDeficit(
        int cumulativeHpLost,
        int maxHpDeficit,
        int recoveredHp,
        BossHpRelief bossHpRelief,
        int deathSaveHpRestored = 0)
        => cumulativeHpLost
            + maxHpDeficit
            - PersistentValueOfRecoveredHp(recoveredHp - deathSaveHpRestored, bossHpRelief)
            + DeathSavePremium(deathSaveHpRestored);

    public static int RankedPostCombatRelicHeal(
        PostCombatRelicHealProfile profile,
        bool completeVictory,
        int finalHp,
        int finalMaxHp)
        => completeVictory && finalHp > 0
            ? profile.MonotoneHealFor(finalHp, finalMaxHp)
            : 0;

    public static int DeathSavePremium(int deathSaveHpRestored)
        => Math.Max(0, deathSaveHpRestored)
            * SolverWeights.DeathSavePremiumPercent
            / 100;

    public static int DeathSaveBeamCost(int deathSaveHpRestored)
    {
        int restored = Math.Max(0, deathSaveHpRestored);
        return restored + DeathSavePremium(restored);
    }
}
