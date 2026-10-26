namespace BlitzSolver.Simulation.Combat;

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
    /// listeners are not implemented; an enchantment that needs one is rejected.
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
            if ((enchantment.HooksExt & HookMaskExt.ModifyShuffleOrder) != 0)
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

    /// <summary><c>Hook.AfterRoomEntered</c> for the combat room, over the player's relics.</summary>
    public static void AfterRoomEntered(ref Battle battle, Rules rules, ICombatStartContent content,
        bool elite, bool boss)
    {
        PlayerAction action = default;
        Exec x = new(ref battle, rules, in action)
        {
            CombatStart = true,
            SetupContent = content,
        };
        for (int index = 0; index < battle.RelicCount; index++)
        {
            ref readonly Relic relic = ref battle.Relics[index];
            if ((relic.Flags & RelicFlags.Melted) != 0)
                continue;
            RelicDef def = Registry.Relic(relic.Id);
            Listener self = new(ListenerKind.Relic, checked((byte)index), Limits.PlayerOwner);
            def.AfterRoomEntered(ref x, self, elite, boss);
            if (x.NeedsChoice)
                throw new UnsupportedException($"遗物 {def.NativeId} 的房间进入效果请求了选择。");
        }
    }

    /// <summary><c>Hook.BeforeCombatStart</c> over relics then powers, in native listener order.</summary>
    public static void BeforeCombatStart(ref Battle battle, Rules rules, ICombatStartContent content,
        bool boss)
        => ListenerCombatStart(ref battle, rules, content, late: false, boss);

    /// <summary><c>Hook.BeforeCombatStartLate</c> over the same listeners.</summary>
    public static void BeforeCombatStartLate(ref Battle battle, Rules rules,
        ICombatStartContent content, bool boss)
        => ListenerCombatStart(ref battle, rules, content, late: true, boss);

    private static void ListenerCombatStart(ref Battle battle, Rules rules,
        ICombatStartContent content, bool late, bool boss)
    {
        PlayerAction action = default;
        Exec x = new(ref battle, rules, in action)
        {
            CombatStart = true,
            SetupContent = content,
            FirstRoomIsBoss = boss,
        };
        for (int index = 0; index < battle.RelicCount; index++)
        {
            ref readonly Relic relic = ref battle.Relics[index];
            if ((relic.Flags & RelicFlags.Melted) != 0)
                continue;
            RelicDef def = Registry.Relic(relic.Id);
            Listener self = new(ListenerKind.Relic, checked((byte)index), Limits.PlayerOwner);
            if (late)
                def.BeforeCombatStartLate(ref x, self);
            else
                def.BeforeCombatStart(ref x, self);
            if (x.NeedsChoice)
                throw new UnsupportedException($"遗物 {def.NativeId} 的开战效果请求了选择。");
        }
        for (int index = 0; index < battle.PowerCount; index++)
        {
            PowerDef def = Registry.Power(battle.Powers[index].Id);
            Listener self = new(ListenerKind.Power, checked((byte)index), battle.Powers[index].Owner);
            if (late)
                def.BeforeCombatStartLate(ref x, self);
            else
                def.BeforeCombatStart(ref x, self);
            if (x.NeedsChoice)
                throw new UnsupportedException($"Power {def.NativeId} 的开战效果请求了选择。");
        }
    }

    /// <summary>
    /// Runs the native first player turn and requires it to reach the play phase without a choice.
    /// </summary>
    public static void StartFirstTurn(ref Battle battle, Rules rules, ICombatStartContent content)
    {
        PlayerAction action = default;
        Exec x = new(ref battle, rules, in action)
        {
            CombatStart = true,
            SetupContent = content,
        };
        Kernel.StartFirstPlayerTurn(ref x);
        if (x.NeedsChoice)
            throw new UnsupportedException("第一回合的起手效果请求了选择。");
    }
}
