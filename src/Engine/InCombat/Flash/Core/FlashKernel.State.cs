namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// The Flash simulation kernel. Each partial file mirrors one area of the existing engine and
/// names the existing method it follows; the step order inside each method is the existing
/// engine's order.
/// </summary>
[System.Runtime.CompilerServices.SkipLocalsInit]
public static partial class FlashKernel
{
    /// <summary>Mirrors <c>CombatPredictionSimulator.IsInProgress</c>.</summary>
    public static bool IsInProgress(in FlashBattle battle) => battle.Outcome == FlashOutcome.None;

    public static bool IsPendingLoss(in FlashBattle battle) => (battle.Flags & FlashBattleFlags.PendingLoss) != 0;

    /// <summary>Mirrors <c>CombatPredictionSimulator.IsCombatEnding</c>.</summary>
    public static bool IsEnding(in FlashBattle battle)
    {
        if (!IsInProgress(battle))
            return false;
        if (IsPendingLoss(battle))
            return true;
        for (int index = 0; index < battle.RosterCount; index++)
        {
            ref readonly FlashEnemy enemy = ref battle.Enemies[battle.Roster[index]];
            if (enemy.Creature.IsAlive && IsPrimaryEnemy(battle, EnemyOwner(battle.Roster[index])))
                return false;
        }
        // Native Hook.ShouldStopCombatFromEnding iterates the active listeners, which no longer hold a
        // removed dead creature's powers, so the existing engine's equivalent stop is
        // SimulatedCombatState.HasUnresolvedSpawningDeath: an enemy that left the roster, has not
        // completed its death effects, and still holds a power that spawns primary enemies.
        if (HasUnresolvedSpawningDeath(battle))
            return false;
        return true;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.HasUnresolvedSpawningDeath</c> for the death powers the Flash
    /// content implements (<see cref="FlashPowerDef.SpawnsPrimaryEnemyOnDeath"/>).
    /// </summary>
    private static bool HasUnresolvedSpawningDeath(in FlashBattle battle)
    {
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            ref readonly FlashEnemy enemy = ref battle.Enemies[index];
            if ((enemy.Flags & FlashEnemyFlags.InCombat) != 0)
                continue;
            // HasCompletedDeathEffects covers both remaining death phases.
            if (enemy.DeathPhase != FlashDeathPhase.None)
                continue;
            byte owner = EnemyOwner(index);
            for (int power = 0; power < battle.PowerCount; power++)
            {
                ref readonly FlashPower candidate = ref battle.Powers[power];
                if (candidate.Owner == owner && candidate.Amount > 0
                    && FlashRegistry.Power(candidate.Id).SpawnsPrimaryEnemyOnDeath)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.IsPrimaryEnemy</c>: an enemy is secondary while it holds a
    /// positive power whose definition marks its owner secondary.
    /// </summary>
    public static bool IsPrimaryEnemy(in FlashBattle battle, byte owner)
    {
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (power.Owner == owner && power.Amount > 0 && FlashRegistry.Power(power.Id).OwnerIsSecondaryEnemy)
                return false;
        }
        return true;
    }

    public static bool IsOverOrEnding(in FlashBattle battle) => !IsInProgress(battle) || IsEnding(battle);

    /// <summary>Mirrors <c>CombatPredictionSimulator.CheckWinCondition</c>: locks the outcome at a safe point.</summary>
    public static bool CheckWinCondition(ref FlashBattle battle)
    {
        if (!IsInProgress(battle))
            return true;
        bool pendingLoss = IsPendingLoss(battle);
        if (!pendingLoss && !IsEnding(battle))
            return false;
        battle.Outcome = pendingLoss ? FlashOutcome.Defeat : FlashOutcome.Victory;
        battle.Flags &= ~FlashBattleFlags.PendingLoss;
        return true;
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.LoseCombat</c>.</summary>
    public static void LoseCombat(ref FlashBattle battle) => battle.Flags |= FlashBattleFlags.PendingLoss;

    public static bool IsEnemyOwner(byte owner) => owner >= FlashLimits.FirstEnemyOwner && owner != FlashLimits.NoOwner;

    public static byte EnemyOwner(int enemyIndex) => checked((byte)(FlashLimits.FirstEnemyOwner + enemyIndex));

    public static bool InRoster(in FlashBattle battle, byte owner)
    {
        int enemy = owner - FlashLimits.FirstEnemyOwner;
        for (int index = 0; index < battle.RosterCount; index++)
        {
            if (battle.Roster[index] == enemy)
                return true;
        }
        return false;
    }

    /// <summary>Mirrors <c>CombatPredictionState.IsHittable</c>: alive and still in the roster.</summary>
    public static bool IsHittable(in FlashBattle battle, byte owner)
    {
        if (owner == FlashLimits.PlayerOwner)
            return battle.Player.IsAlive;
        if (owner == FlashLimits.OstyOwner)
            return (battle.Flags & FlashBattleFlags.OstyPresent) != 0 && battle.Osty.IsAlive;
        ref readonly FlashEnemy enemy = ref battle.Enemies[owner - FlashLimits.FirstEnemyOwner];
        return enemy.Creature.IsAlive && InRoster(battle, owner);
    }

    /// <summary>Mirrors <c>CombatPredictionState.RemoveCreature</c> for an enemy.</summary>
    public static void RemoveFromRoster(ref FlashBattle battle, byte owner)
    {
        int enemy = owner - FlashLimits.FirstEnemyOwner;
        int count = battle.RosterCount;
        for (int index = 0; index < count; index++)
        {
            if (battle.Roster[index] != enemy)
                continue;
            for (int next = index + 1; next < count; next++)
                battle.Roster[next - 1] = battle.Roster[next];
            battle.Roster[count - 1] = 0;
            battle.RosterCount--;
            battle.Enemies[enemy].Flags &= ~FlashEnemyFlags.InCombat;
            return;
        }
    }

    internal static int Clamp(long value) => (int)Math.Clamp(value, -FlashLimits.ValueClamp, FlashLimits.ValueClamp);
}
