namespace Sts2Solver.Simulation.Combat;

/// <summary>
/// Opening-combat semantics that run before the first player turn: the initial deck shuffle, the
/// room-entered and combat-start listener passes, and the first-turn setup. These are the native
/// <c>PopulateCombatState</c>, <c>Hook.AfterRoomEntered</c>, <c>Hook.BeforeCombatStart(Late)</c> and
/// <c>CombatManager.StartTurn</c> steps that happen before the captured play-phase root.
/// </summary>
public static class CombatStart
{
    /// <summary>
    /// Native <c>CardPile.RandomizeOrderInternal</c> for the initial draw pile: an unstable
    /// Fisher-Yates shuffle over the cloned deck order. Initial-shuffle <c>ModifyShuffleOrder</c>
    /// listeners are not implemented; an enchantment that needs one is rejected. The initial
    /// shuffle does not fire <c>AfterShuffle</c> (native calls only <c>ModifyShuffleOrder</c>), so
    /// BiiigHug is unaffected.
    /// </summary>
    public static void ShuffleInitialDrawPile(ref Battle battle)
    {
        int start = Piles.Start(battle, Pile.Draw);
        int count = battle.DrawCount;
        for (int index = 0; index < count; index++)
        {
            ref readonly Card card = ref battle.Cards[start + index];
            if (card.EnchantmentId == 0)
                continue;
            EnchantmentDef enchantment = Registry.Enchantment((ushort)(card.EnchantmentId - 1));
            if ((enchantment.HooksExt & HookMaskExt.ModifyShuffleOrder) == 0)
                continue;
            // PerfectFit.ModifyShuffleOrder starts with `if (isInitialShuffle) return;`; the
            // initial shuffle is exactly the no-op branch. Every other ModifyShuffleOrder
            // enchantment is still rejected until the initial-shuffle dispatch is implemented.
            if (enchantment.NativeId == "PERFECT_FIT")
                continue;
            throw new UnsupportedException(
                $"开战洗牌的 ModifyShuffleOrder 尚未实现：{enchantment.NativeId}。");
        }
        ref RngState rng = ref battle.RngStream(RngStream.Shuffle);
        for (int index = count - 1; index > 0; index--)
        {
            int swap = rng.NextInt(index + 1);
            (battle.Cards[start + index], battle.Cards[start + swap]) =
                (battle.Cards[start + swap], battle.Cards[start + index]);
        }
    }

    /// <summary>
    /// <c>Hook.AfterRoomEntered</c> for the combat room, over the player's relics. The caller owns
    /// the opening <see cref="Exec"/> so a choice requested here consumes the same sequence as the
    /// later combat-start and first-turn passes.
    /// </summary>
    public static void AfterRoomEntered(ref Exec x, bool elite, bool boss)
    {
        x.FirstRoomIsElite = elite;
        x.FirstRoomIsBoss = boss;
        for (int index = 0; index < x.B.RelicCount; index++)
        {
            ref readonly Relic relic = ref x.B.Relics[index];
            if ((relic.Flags & RelicFlags.Melted) != 0)
                continue;
            RelicDef def = Registry.Relic(relic.Id);
            Listener self = new(ListenerKind.Relic, checked((byte)index), Limits.PlayerOwner);
            def.AfterRoomEntered(ref x, self, elite, boss);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary><c>Hook.BeforeCombatStart</c> over relics then powers, in native listener order.</summary>
    public static void BeforeCombatStart(ref Exec x, bool boss)
        => ListenerCombatStart(ref x, late: false, boss);

    /// <summary><c>Hook.BeforeCombatStartLate</c> over the same listeners.</summary>
    public static void BeforeCombatStartLate(ref Exec x, bool boss)
        => ListenerCombatStart(ref x, late: true, boss);

    private static void ListenerCombatStart(ref Exec x, bool late, bool boss)
    {
        x.FirstRoomIsBoss = boss;
        for (int index = 0; index < x.B.RelicCount; index++)
        {
            ref readonly Relic relic = ref x.B.Relics[index];
            if ((relic.Flags & RelicFlags.Melted) != 0)
                continue;
            RelicDef def = Registry.Relic(relic.Id);
            Listener self = new(ListenerKind.Relic, checked((byte)index), Limits.PlayerOwner);
            if (late)
                def.BeforeCombatStartLate(ref x, self);
            else
                def.BeforeCombatStart(ref x, self);
            if (x.NeedsChoice)
                return;
        }
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            PowerDef def = Registry.Power(x.B.Powers[index].Id);
            Listener self = new(ListenerKind.Power, checked((byte)index), x.B.Powers[index].Owner);
            if (late)
                def.BeforeCombatStartLate(ref x, self);
            else
                def.BeforeCombatStart(ref x, self);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>Runs the native first player turn on the caller's opening execution context.</summary>
    public static void StartFirstTurn(ref Exec x) => Kernel.StartFirstPlayerTurn(ref x);
}
