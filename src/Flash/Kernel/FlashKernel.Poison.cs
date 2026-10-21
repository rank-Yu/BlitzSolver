namespace CombatSolver.Flash.Kernel;

public static partial class FlashKernel
{
    private static readonly Lazy<ushort> PoisonPowerId = new(() => PowerIdOrMax("POISON_POWER"));
    private static readonly Lazy<ushort> AccelerantPowerId = new(() => PowerIdOrMax("ACCELERANT_POWER"));

    /// <summary>
    /// Mirrors <c>CorePowerSupport.TriggerPoison</c> (native <c>PoisonPower.Trigger</c>): each
    /// creature with poison takes <c>min(amount, 1 + opponents' Accelerant)</c> unblockable unpowered
    /// hits of its current amount, decrementing once per hit while it survives. The damage has no
    /// dealer and no card source. Returns false when a choice interrupted the loop.
    /// </summary>
    internal static bool TriggerPoison(ref FlashExec x, scoped ReadOnlySpan<byte> creatures)
    {
        ushort poison = PoisonPowerId.Value;
        if (poison == ushort.MaxValue)
        {
            // No PoisonPower definition is registered, so no poisoned creature can exist.
            return true;
        }
        ushort accelerant = AccelerantPowerId.Value;
        Span<byte> target = stackalloc byte[1];
        for (int index = 0; index < creatures.Length; index++)
        {
            byte creature = creatures[index];
            int amount = GetAmount(x.B, creature, poison);
            if (amount <= 0 || x.B.Creature(creature).IsDead)
                continue;
            int accelerated = 1;
            if (accelerant != ushort.MaxValue)
            {
                if (IsPlayerSide(creature))
                {
                    for (int position = 0; position < x.B.RosterCount; position++)
                    {
                        byte enemy = EnemyOwner(x.B.Roster[position]);
                        if (x.B.Creature(enemy).IsAlive)
                            accelerated += GetAmount(x.B, enemy, accelerant);
                    }
                }
                else
                {
                    if (x.B.Player.IsAlive)
                        accelerated += GetAmount(x.B, FlashLimits.PlayerOwner, accelerant);
                    if ((x.B.Flags & FlashBattleFlags.OstyPresent) != 0 && x.B.Osty.IsAlive)
                        accelerated += GetAmount(x.B, FlashLimits.OstyOwner, accelerant);
                }
            }
            int iterations = Math.Min(amount, accelerated);
            target[0] = creature;
            for (int trigger = 0; trigger < iterations; trigger++)
            {
                int current = GetAmount(x.B, creature, poison);
                Damage(ref x, target, current, FlashValueProps.Unblockable | FlashValueProps.Unpowered,
                    FlashLimits.NoOwner, hasCardSource: false, default);
                if (x.NeedsChoice)
                    return false;
                if (x.B.Creature(creature).IsDead)
                    break;
                SetPowerAmount(ref x.B, creature, poison, current - 1);
            }
        }
        return true;
    }
}
