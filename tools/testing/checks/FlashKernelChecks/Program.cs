using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CombatSolver.Engine.InCombat.Flash;
using CombatSolver.Engine.InCombat.Flash.Content.Afflictions;
using CombatSolver.Engine.InCombat.Flash.Content.Cards.Status;
using CombatSolver.Engine.InCombat.Flash.Content.Enchantments;
using CombatSolver.Engine.InCombat.Flash.Content.Powers;
using CombatSolver.Engine.InCombat.Flash.Content.Relics;

// Pure Flash kernel contracts (K0): no game, no CombatSolver assembly. Differential equivalence
// with the existing engine is checked separately by the offline harness probe.

Check(Unsafe.SizeOf<FlashCard>() == 92, $"FlashCard 应为 92 字节，实际 {Unsafe.SizeOf<FlashCard>()}");
Check(Unsafe.SizeOf<FlashEnemy>() == 64, $"FlashEnemy 应为 64 字节，实际 {Unsafe.SizeOf<FlashEnemy>()}");
Check(Unsafe.SizeOf<FlashPower>() == 24, $"FlashPower 应为 24 字节，实际 {Unsafe.SizeOf<FlashPower>()}");
Check(Unsafe.SizeOf<FlashOrb>() == 8, $"FlashOrb 应为 8 字节，实际 {Unsafe.SizeOf<FlashOrb>()}");
Check(Unsafe.SizeOf<FlashRngState>() == 40, $"FlashRngState 应为 40 字节，实际 {Unsafe.SizeOf<FlashRngState>()}");
Check(Unsafe.SizeOf<FlashBattle>() == FlashBattle.ExpectedSize,
    $"FlashBattle 应为 {FlashBattle.ExpectedSize} 字节，实际 {Unsafe.SizeOf<FlashBattle>()}");

CheckRng();
FlashRules rules = SliceRules();
CheckIntroSort(rules);
CheckCapacity();
CheckPiles();
CheckMonsterForcedMoves(rules);
CheckKm2Kernel(rules);
CheckMonsterSpawn(SpawnRules());
CheckGenerationAndAutoPlay();
CheckKg2Hooks(rules);
CheckA3Kernel(rules);
CheckKaAttachments();
CheckP16Kernel(rules);
CheckSkKernel();
CheckLsKernel();
CheckNbKernel();
CheckMbKernel(rules);
CheckDfKernel(rules);
CheckRgKernel(rules);
CheckLrKernel(rules);
CheckIdKernel(rules);
CheckCxKernel(rules);
var benchmark = Benchmark(rules);

Console.WriteLine(JsonSerializer.Serialize(new
{
    contracts = "Passed",
    battleBytes = Unsafe.SizeOf<FlashBattle>(),
    runtime = Environment.Version.ToString(),
    benchmark,
}, new JsonSerializerOptions { WriteIndented = true }));

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

// Reference copy of native MegaRandom + Rng (decompiled sts2 0.111.0) to pin the Flash port.
static void CheckRng()
{
    ulong seed = 0x1234_5678_9ABC_DEF0UL;
    ulong Split(ref ulong x)
    {
        ulong z = x += 11400714819323198485UL;
        z = (z ^ (z >> 30)) * 13787848793156543929UL;
        z = (z ^ (z >> 27)) * 10723151780598845931UL;
        return z ^ (z >> 31);
    }
    ulong s0 = Split(ref seed), s1 = Split(ref seed), s2 = Split(ref seed), s3 = Split(ref seed);
    ulong Next()
    {
        ulong result = BitOperations.RotateLeft(s1 * 5, 7) * 9;
        ulong t = s1 << 17;
        s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3; s2 ^= t; s3 = BitOperations.RotateLeft(s3, 45);
        return result;
    }
    double NextDouble() => (Next() >> 11) * 1.1102230246251565E-16;

    FlashRngState flash = FlashRngState.FromSerializable(0, s0, s1, s2, s3);
    for (int index = 0; index < 2000; index++)
    {
        int max = 1 + index % 37;
        int expected = (int)(NextDouble() * max);
        Check(flash.NextInt(max) == expected, $"NextInt 第 {index} 次不一致");
        float total = 0.5f + index % 7;
        float expectedFloat = (float)(NextDouble() * total);
        Check(flash.NextFloat(total) == expectedFloat, $"NextFloat 第 {index} 次不一致");
        int expectedRange = (int)(NextDouble() * 9) + 3;
        Check(flash.NextInt(3, 12) == expectedRange, $"NextInt(min,max) 第 {index} 次不一致");
    }
    Check(flash.Counter == 6000, $"计数器应为 6000，实际 {flash.Counter}");
}

// Flash's introsort must permute equal-key cards exactly like List<T>.Sort().
static void CheckIntroSort(FlashRules rules)
{
    Random random = new(17);
    for (int trial = 0; trial < 4000; trial++)
    {
        int length = 1 + random.Next(80);
        FlashCard[] cards = new FlashCard[length];
        List<Keyed> reference = new(length);
        for (int index = 0; index < length; index++)
        {
            cards[index] = new FlashCard { Id = (ushort)random.Next(3), Upgrade = (byte)random.Next(2), Private = (short)index };
            reference.Add(new Keyed(cards[index]));
        }
        reference.Sort();
        FlashCard[] original = (FlashCard[])cards.Clone();
        FlashCardSort.Sort(cards, rules);
        FlashCard[] withOrigins = (FlashCard[])original.Clone();
        int[] origins = new int[length];
        for (int index = 0; index < length; index++)
            origins[index] = index;
        FlashCardSort.SortWithOrigins(withOrigins, origins, rules);
        for (int index = 0; index < length; index++)
        {
            Check(cards[index].Private == reference[index].Card.Private, $"排序第 {trial} 组第 {index} 位与 List.Sort 不一致");
            Check(withOrigins[index].Private == cards[index].Private, $"带来源排序第 {trial} 组第 {index} 位与 Sort 不一致");
            Check(original[origins[index]].Private == cards[index].Private, $"来源映射第 {trial} 组第 {index} 位错误");
        }
    }
}

static void CheckCapacity()
{
    FlashBattle battle = default;
    FlashCard card = new() { Id = 0 };
    for (int index = 0; index < FlashLimits.Cards; index++)
        FlashPiles.Append(ref battle, FlashPile.Draw, card);
    try
    {
        FlashPiles.Append(ref battle, FlashPile.Draw, card);
        throw new InvalidOperationException("超出卡牌容量应抛出 FlashCapacityException");
    }
    catch (FlashCapacityException)
    {
    }
}

static void CheckPiles()
{
    FlashBattle battle = default;
    for (short index = 0; index < 6; index++)
        FlashPiles.Append(ref battle, index < 3 ? FlashPile.Draw : FlashPile.Discard, new FlashCard { Private = index });
    FlashPiles.DrawTopToHand(ref battle);
    Check(battle.HandCount == 1 && battle.DrawCount == 2 && FlashPiles.At(ref battle, FlashPile.Hand, 0).Private == 0, "抽顶牌应只移动段边界");
    FlashCard removed = FlashPiles.RemoveAt(ref battle, FlashPile.Discard, 1);
    Check(removed.Private == 4 && battle.DiscardCount == 2 && battle.Cards[battle.CardCount].Private == 0, "移除后应前移并清零尾部");
    FlashBattle copy = battle;
    Check(FlashFingerprint.Equal(copy, battle) && FlashFingerprint.Hash(copy) == FlashFingerprint.Hash(battle), "复制后应字节相同");
}

// P16 kernel capabilities: the return-to-hand card bit, X-cost SetToFreeThisTurn, and instanced
// power row addressing.
static void CheckP16Kernel(FlashRules rules)
{
    FlashCard card = new() { Id = 0 };
    card.ReturnToHandNextTurn = true;
    card.SetTemporaryStarCost(0, clearsAtTurnEnd: true, clearsWhenPlayed: true);
    Check(card.ReturnToHandNextTurn, "临时星能费用不应清除 ReturnToHandNextTurn");
    card.EnchantmentStatus = 3;
    Check(card.ReturnToHandNextTurn && card.EnchantmentStatus == 3, "附魔状态不应清除 ReturnToHandNextTurn");
    card.ClearTemporaryStarCost();
    Check(card.ReturnToHandNextTurn, "费用清理不应清除 ReturnToHandNextTurn");
    card.ReturnToHandNextTurn = false;
    Check(!card.ReturnToHandNextTurn && card.EnchantmentStatus == 3, "清除 ReturnToHandNextTurn 不应影响附魔状态");

    // CardEnergyCost.SetThisTurnOrUntilPlayed adds the modifier for X-cost cards (canonical 0).
    FlashRegistry.TryCard("WHIRLWIND", out ushort whirlwind);
    FlashCard xCard = FlashKernel.CreateCard(rules, whirlwind);
    FlashKernel.MakeFreeThisTurn(rules, ref xCard);
    Check(xCard.EnergyModifierCount == 1 && xCard.HasTemporaryStarCost,
        "X 费卡的 SetToFreeThisTurn 也应添加 0 费修饰与临时星能费用");

    // Instanced powers share (owner, id); occurrence addresses the exact row.
    FlashBattle battle = default;
    battle.PowerCount = 2;
    battle.Powers[0] = new FlashPower { Id = 1, Owner = FlashLimits.PlayerOwner, Amount = 1 };
    battle.Powers[1] = new FlashPower { Id = 1, Owner = FlashLimits.PlayerOwner, Amount = 2 };
    Check(FlashKernel.FindPowerOccurrence(battle, FlashLimits.PlayerOwner, 1, 0) == 0
        && FlashKernel.FindPowerOccurrence(battle, FlashLimits.PlayerOwner, 1, 1) == 1
        && FlashKernel.FindLastPower(battle, FlashLimits.PlayerOwner, 1) == 1,
        "实例 Power 的 occurrence 定位错误");
}

// Forced moves and the synthetic STUNNED node (SimulatedCombatState.MonsterAi.cs:58-128).
static void CheckMonsterForcedMoves(FlashRules rules)
{
    byte owner = FlashKernel.EnemyOwner(0);
    FlashBattle battle = SliceBattle();
    FlashAction action = default;
    FlashExec x = new(ref battle, rules, action);
    FlashKernel.ForceStunnedMove(ref x, owner, -1);
    Check(battle.Enemies[0].Move == 4 && battle.Enemies[0].ForcedMove == 1,
        "STUNNED 缺省后继应取 StateLog 最后一条");
    Check(battle.Enemies[0].LoggedMoves == 1, "强制行动不应写入行动日志");
    battle.Enemies[0].Flags |= FlashEnemyFlags.PerformedMoveThisRound;
    FlashKernel.PrepareMonsterMovesForNextRound(ref x);
    Check(battle.Enemies[0].Move == 1 && battle.Enemies[0].ForcedMove == byte.MaxValue,
        "执行 STUNNED 后应推进到动态后继并清空 ForcedMove");
    Check(battle.Enemies[0].LoggedMoves == 2, "推进后的后继应写入行动日志");

    battle = SliceBattle();
    x = new FlashExec(ref battle, rules, action);
    battle.Enemies[0].Flags |= FlashEnemyFlags.PerformedMoveThisRound;
    FlashKernel.ForceStunnedMove(ref x, owner, 2);
    Check((battle.Enemies[0].Flags & FlashEnemyFlags.ForcedMustPerformAfterMove) != 0,
        "本轮已行动后强制 STUNNED 应记下 must-perform-once 未满足");
    FlashKernel.PrepareMonsterMovesForNextRound(ref x);
    Check(battle.Enemies[0].Move == 4 && battle.Enemies[0].ForcedMove == 2,
        "本轮已行动后强制 STUNNED 不应推进");
    Check((battle.Enemies[0].Flags & FlashEnemyFlags.ForcedMustPerformAfterMove) == 0,
        "权威边界上不得残留 ForcedMustPerformAfterMove");
    battle.Enemies[0].Flags |= FlashEnemyFlags.PerformedMoveThisRound;
    FlashKernel.PrepareMonsterMovesForNextRound(ref x);
    Check(battle.Enemies[0].Move == 2, "STUNNED 在下一轮执行后应推进到后继");

    battle = SliceBattle();
    x = new FlashExec(ref battle, rules, action);
    battle.Enemies[0].Flags |= FlashEnemyFlags.PerformedMoveThisRound;
    FlashKernel.ForceMonsterMove(ref x, owner, 3);
    FlashKernel.PrepareMonsterMovesForNextRound(ref x);
    Check(battle.Enemies[0].Move == 1 && battle.Enemies[0].ForcedMove == byte.MaxValue,
        "普通强制行动在本轮已行动后应直接推进");

    battle = SliceBattle();
    x = new FlashExec(ref battle, rules, action);
    FlashKernel.ForceStunnedMove(ref x, owner, 2);
    Check(battle.Enemies[0].ForcedMove == 2, "显式后继应写入 ForcedMove");
    Check(battle.Enemies[0].Move == 4, "强制 STUNNED 应指向合成节点");
}

// KM2 kernel contracts: slot names outside the encounter list behave like
// SimulatedCombatState.GetSlotIndex (-1), a fully blocked Damage result exposes the flag the
// Imbalanced stun reads, and an unresolved spawning death keeps IsEnding false.
static void CheckKm2Kernel(FlashRules rules)
{
    Check(FlashKernel.SlotIndex(rules, "second") == 1, "已知槽位应返回其下标");
    Check(FlashKernel.SlotIndex(rules, "middle") == FlashKernel.NoSlot,
        "EncounterModel.Slots 之外的槽名应映射到 NoSlot");
    Check(FlashKernel.SlotIndex(rules, null) == FlashKernel.NoSlot, "null 槽名应映射到 NoSlot");

    FlashAction action = default;
    FlashBattle battle = SliceBattle();
    battle.Player.Block = 100;
    FlashExec x = new(ref battle, rules, action);
    Check(FlashKernel.Damage(ref x, [FlashLimits.PlayerOwner], 10m, FlashValueProps.Move,
            FlashKernel.EnemyOwner(0), hasCardSource: false, default),
        "完全格挡的 Damage 应返回 true");
    battle = SliceBattle();
    x = new FlashExec(ref battle, rules, action);
    Check(!FlashKernel.Damage(ref x, [FlashLimits.PlayerOwner], 10m, FlashValueProps.Move,
            FlashKernel.EnemyOwner(0), hasCardSource: false, default),
        "未完全格挡的 Damage 应返回 false");

    Check(FlashRegistry.TryPower("INFESTED_POWER", out ushort infested), "InfestedPower 应有 Flash 定义");
    battle = default;
    battle.Outcome = FlashOutcome.None;
    battle.EnemyCount = 1;
    battle.RosterCount = 0;
    battle.Enemies[0].Creature.Flags = FlashCreatureFlags.Present;
    battle.Enemies[0].DeathPhase = FlashDeathPhase.None;
    battle.PowerCount = 1;
    battle.Powers[0] = new FlashPower
    {
        Id = infested, Owner = FlashKernel.EnemyOwner(0), Amount = 1, Applier = FlashKernel.EnemyOwner(0),
    };
    Check(!FlashKernel.IsEnding(battle), "未结算的寄生死亡应阻止战斗结束");
    battle.PowerCount = 0;
    battle.Powers[0] = default;
    Check(FlashKernel.IsEnding(battle), "死亡效果结算完后应允许战斗结束");
}

// Rules for the slice content: Ironclad starter cards and one Nibbit.
// DF: orb kernel contracts. The existing engine's order and values are pinned here: full-queue
// channel evokes the leftmost orb first, Dark evokes its raw accumulation (no ModifyOrbValue), Glass
// decays its private passive by one after a positive passive, Focus modifies passive/evoke values,
// Plasma's passive fires at turn start, and GoldPlatedCables adds one passive trigger to the first orb.
static void CheckDfKernel(FlashRules rules)
{
    Check(FlashRegistry.TryOrb("LIGHTNING_ORB", out ushort lightning), "缺少 LIGHTNING_ORB");
    Check(FlashRegistry.TryOrb("FROST_ORB", out ushort frost), "缺少 FROST_ORB");
    Check(FlashRegistry.TryOrb("DARK_ORB", out ushort dark), "缺少 DARK_ORB");
    Check(FlashRegistry.TryOrb("PLASMA_ORB", out ushort plasma), "缺少 PLASMA_ORB");
    Check(FlashRegistry.TryOrb("GLASS_ORB", out ushort glass), "缺少 GLASS_ORB");
    Check(FlashRegistry.TryPower("FOCUS_POWER", out ushort focus), "缺少 FOCUS_POWER");
    Check(FlashRegistry.TryRelic("GOLD_PLATED_CABLES", out ushort gold), "缺少 GOLD_PLATED_CABLES");
    Check(FlashRegistry.TryRelic("CRACKED_CORE", out ushort crackedCore), "缺少 CRACKED_CORE");
    Check(FlashRegistry.TryRelic("RUNIC_CAPACITOR", out ushort runic), "缺少 RUNIC_CAPACITOR");
    Check(FlashRegistry.TryRelic("SYMBIOTIC_VIRUS", out ushort symbiotic), "缺少 SYMBIOTIC_VIRUS");
    Check(FlashRegistry.TryRelic("REPTILE_TRINKET", out ushort reptile), "缺少 REPTILE_TRINKET");
    Check(FlashRegistry.TryPower("THUNDER_POWER", out ushort thunder), "缺少 THUNDER_POWER");
    Check(FlashRegistry.TryPower("REPTILE_TRINKET_POWER", out ushort reptilePower), "缺少 REPTILE_TRINKET_POWER");
    Check(FlashRegistry.TryPower("STRENGTH_POWER", out ushort strength), "缺少 STRENGTH_POWER");
    FlashAction action = default;

    FlashBattle OrbBattle(int enemyHp = 1000)
    {
        FlashBattle battle = SliceBattle();
        battle.HandCount = 0;
        battle.DrawCount = 0;
        battle.DiscardCount = 0;
        battle.ExhaustCount = 0;
        battle.PlayCount = 0;
        battle.OrbCount = 0;
        battle.OrbCapacity = 3;
        battle.PowerCount = 0;
        battle.RelicCount = 0;
        battle.Player.Block = 0;
        battle.Enemies[0].Creature.CurrentHp = enemyHp;
        battle.Enemies[0].Creature.MaxHp = enemyHp;
        return battle;
    }

    void SetFocus(ref FlashBattle battle, int amount)
    {
        battle.PowerCount = 1;
        battle.Powers[0] = new FlashPower { Id = focus, Owner = FlashLimits.PlayerOwner, Amount = amount };
    }

    // Full queue channel: the leftmost (Dark) orb is evoked first. Dark's evoke uses the raw
    // accumulation, while its passive would have used ModifyOrbValue.
    FlashBattle battle = OrbBattle();
    SetFocus(ref battle, 2);
    battle.OrbCount = 3;
    battle.Orbs[0] = new FlashOrb { Id = checked((byte)dark), Aux = 10 };
    battle.Orbs[1] = new FlashOrb { Id = checked((byte)frost) };
    battle.Orbs[2] = new FlashOrb { Id = checked((byte)lightning) };
    FlashExec x = new(ref battle, rules, action);
    Check(FlashKernel.ChannelOrb(ref x, lightning), "满槽充能应成功");
    Check(battle.OrbCount == 3 && battle.Orbs[0].Id == frost && battle.Orbs[2].Id == lightning,
        "满槽充能应先激发最左侧球");
    Check(battle.Enemies[0].Creature.CurrentHp == 990, "暗球激发不应受集中修正（原始累积 10）");
    Check(battle.Counter(FlashCounter.LightningChannels) == 1 && battle.Counter(FlashCounter.OrbChannels) == 1,
        "充能计数错误");

    // Turn-end passives in queue order: Frost 2+Focus=4 block, then two Lightning 3+Focus=5 hits.
    FlashKernel.TriggerBeforeTurnEndOrbs(ref x);
    Check(battle.Player.Block == 4, $"冰球回合末格挡应为 4，实际 {battle.Player.Block}");
    Check(battle.Enemies[0].Creature.CurrentHp == 980, "闪电球回合末被动伤害错误");

    // GoldPlatedCables adds one trigger to the first orb's passive.
    battle.RelicCount = 1;
    battle.Relics[0] = new FlashRelic { Id = gold };
    Check(FlashHooks.ModifyOrbPassiveTriggerCounts(battle, rules, battle.Orbs[0], 0, 1) == 2,
        "金缆应让首个球的被动触发 +1");
    battle.Player.Block = 0;
    FlashKernel.TriggerBeforeTurnEndOrbs(ref x);
    Check(battle.Player.Block == 8, $"金缆双次冰球被动应给 8 格挡，实际 {battle.Player.Block}");
    Check(battle.Enemies[0].Creature.CurrentHp == 970, "金缆回合末的闪电被动次数错误");

    // Dark accumulation and raw evoke.
    battle = OrbBattle();
    SetFocus(ref battle, 3);
    battle.OrbCount = 1;
    battle.Orbs[0] = new FlashOrb { Id = checked((byte)dark), Aux = 10 };
    x = new FlashExec(ref battle, rules, action);
    FlashKernel.TriggerOrbPassive(ref x, 0);
    Check(battle.Orbs[0].Aux == 19, $"暗球被动应累积 6+3，实际 {battle.Orbs[0].Aux}");
    FlashKernel.EvokeOrb(ref x, 0);
    Check(battle.Enemies[0].Creature.CurrentHp == 981, "暗球激发应使用原始累积 19");

    // Glass decays after a positive passive; evoke doubles the modified remaining value.
    battle = OrbBattle();
    SetFocus(ref battle, 1);
    battle.OrbCount = 1;
    battle.Orbs[0] = new FlashOrb { Id = checked((byte)glass), Aux = 4 };
    x = new FlashExec(ref battle, rules, action);
    FlashKernel.TriggerOrbPassive(ref x, 0);
    Check(battle.Orbs[0].Aux == 3 && battle.Enemies[0].Creature.CurrentHp == 995,
        "玻璃球被动应造成 5 并递减私有值");
    FlashKernel.EvokeOrb(ref x, 0);
    Check(battle.Enemies[0].Creature.CurrentHp == 987, "玻璃球激发应造成 (3+1)*2 = 8");

    // Plasma passive/evoke energy.
    battle = OrbBattle();
    battle.Energy = 0;
    battle.OrbCount = 1;
    battle.Orbs[0] = new FlashOrb { Id = checked((byte)plasma) };
    x = new FlashExec(ref battle, rules, action);
    FlashKernel.TriggerOrbPassive(ref x, 0);
    Check(battle.Energy == 1, "等离子球被动应 +1 能量");
    FlashKernel.EvokeOrb(ref x, 0);
    Check(battle.Energy == 3, "等离子球激发应 +2 能量");

    // Dualcast semantics: two evocations of the same orb, dequeue only on the last one.
    battle = OrbBattle();
    battle.OrbCount = 2;
    battle.Orbs[0] = new FlashOrb { Id = checked((byte)lightning) };
    battle.Orbs[1] = new FlashOrb { Id = checked((byte)lightning) };
    x = new FlashExec(ref battle, rules, action);
    FlashKernel.EvokeNext(ref x, repeat: 2);
    Check(battle.OrbCount == 1 && battle.Enemies[0].Creature.CurrentHp == 984,
        "双发激发应激发两次且只出队一次");

    // Capacity 0 refuses a channel for a character with base slots.
    battle = OrbBattle();
    battle.OrbCapacity = 0;
    x = new FlashExec(ref battle, rules, action);
    Check(!FlashKernel.ChannelOrb(ref x, lightning) && battle.OrbCount == 0, "零容量不应充能");

    // Slot growth/removal.
    battle = OrbBattle();
    battle.OrbCapacity = 9;
    FlashKernel.AddOrbSlots(ref battle, 3);
    Check(battle.OrbCapacity == 10, "球槽容量应钳制在 10");
    battle.OrbCapacity = 3;
    battle.OrbCount = 3;
    battle.Orbs[0] = new FlashOrb { Id = checked((byte)lightning) };
    battle.Orbs[1] = new FlashOrb { Id = checked((byte)frost) };
    battle.Orbs[2] = new FlashOrb { Id = checked((byte)dark) };
    FlashKernel.RemoveOrbSlots(ref battle, 2);
    Check(battle.OrbCapacity == 1 && battle.OrbCount == 1 && battle.Orbs[0].Id == lightning,
        "移除球槽应从队尾删球");

    // CrackedCore channels one Lightning on the first player side turn.
    battle = OrbBattle();
    battle.RelicCount = 1;
    battle.Relics[0] = new FlashRelic { Id = crackedCore };
    x = new FlashExec(ref battle, rules, action);
    FlashRegistry.Relic(crackedCore).BeforeSideTurnStart(ref x,
        new FlashListener(FlashListenerKind.Relic, 0, FlashLimits.PlayerOwner), FlashSide.Player);
    Check(battle.OrbCount == 1 && battle.Orbs[0].Id == lightning, "破碎核心应在第一回合充能闪电");

    // RunicCapacitor and SymbioticVirus run in the AfterSideTurnStart relic batch.
    battle = OrbBattle();
    battle.RelicCount = 1;
    battle.Relics[0] = new FlashRelic { Id = runic };
    x = new FlashExec(ref battle, rules, action);
    FlashRegistry.Relic(runic).AfterSideTurnStart(ref x,
        new FlashListener(FlashListenerKind.Relic, 0, FlashLimits.PlayerOwner), FlashSide.Player);
    Check(battle.OrbCapacity == 6, "符文电容器应 +3 球槽");

    battle = OrbBattle();
    battle.RelicCount = 1;
    battle.Relics[0] = new FlashRelic { Id = symbiotic };
    x = new FlashExec(ref battle, rules, action);
    FlashRegistry.Relic(symbiotic).AfterSideTurnStart(ref x,
        new FlashListener(FlashListenerKind.Relic, 0, FlashLimits.PlayerOwner), FlashSide.Player);
    Check(battle.OrbCount == 1 && battle.Orbs[0].Id == checked((byte)dark), "共生病毒应充能暗球");

    // AfterOrbEvoked: ThunderPower adds its amount to every living evoke target of a Lightning orb.
    battle = OrbBattle();
    battle.PowerCount = 1;
    battle.Powers[0] = new FlashPower { Id = thunder, Owner = FlashLimits.PlayerOwner, Amount = 5 };
    battle.OrbCount = 1;
    battle.Orbs[0] = new FlashOrb { Id = checked((byte)lightning) };
    x = new FlashExec(ref battle, rules, action);
    FlashKernel.EvokeOrb(ref x, 0);
    Check(battle.Enemies[0].Creature.CurrentHp == 987, "雷霆应在闪电球激发后追加 5 伤害");

    // AfterPotionUsed: ReptileTrinket grants temporary and permanent Strength.
    battle = OrbBattle();
    battle.RelicCount = 1;
    battle.Relics[0] = new FlashRelic { Id = reptile };
    x = new FlashExec(ref battle, rules, action);
    FlashHooks.AfterPotionUsed(ref x, 0);
    Check(FlashKernel.GetAmount(battle, FlashLimits.PlayerOwner, reptilePower) == 3,
        "爬行动物饰品应赋予 3 层临时力量");
    Check(FlashKernel.GetAmount(battle, FlashLimits.PlayerOwner, strength) == 3,
        "临时力量首次施加应同时获得 3 力量");
}

// MB: contracts for the fixed-option choice protocol (Knowledge Demon's curse), the instanced
// targeted SandpitPower (exact-instance decrement), and EntropicBrew's out-of-combat pool with the
// freeFromEntropic slot mask and generated-use counter.
static void CheckMbKernel(FlashRules battleRules)
{
    Check(FlashRegistry.TryPower("SANDPIT_POWER", out ushort sandpit), "缺少 SANDPIT_POWER");
    FlashBattle battle = NewBattle();
    FlashAction noChoice = default;
    FlashExec x = new(ref battle, battleRules, noChoice);
    Check(FlashKernel.ApplyPowerTargeted(ref x, FlashLimits.FirstEnemyOwner, FlashLimits.PlayerOwner,
        sandpit, 4, FlashLimits.FirstEnemyOwner) == 4, "沙坑首次施加应为 4 层");
    Check(FlashKernel.ApplyPowerTargeted(ref x, FlashLimits.FirstEnemyOwner, FlashLimits.PlayerOwner,
        sandpit, 4, FlashLimits.FirstEnemyOwner) == 4, "沙坑第二次施加应为独立实例");
    Check(FlashKernel.GetAmount(battle, FlashLimits.FirstEnemyOwner, sandpit) == 4,
        "GetAmount 读取第一个沙坑实例应为 4 层");
    int first = FlashKernel.FindPower(battle, FlashLimits.FirstEnemyOwner, sandpit);
    int last = FlashKernel.FindLastPower(battle, FlashLimits.FirstEnemyOwner, sandpit);
    Check(first != last && battle.Powers[first].Target == FlashLimits.PlayerOwner
        && battle.Powers[last].Target == FlashLimits.PlayerOwner,
        "沙坑的两个实例都应记录玩家目标");
    FlashKernel.IncrementSandpitTargeting(ref x, FlashLimits.PlayerOwner);
    Check(battle.Powers[first].Amount == 5 && battle.Powers[last].Amount == 4,
        "FranticEscape 应只增加第一个沙坑实例");
    FlashKernel.SetPowerAmountAt(ref battle, first, 1);
    Check(battle.Powers[first].Amount == 1 && battle.Powers[last].Amount == 4,
        "沙坑计数应只改访问到的实例");
    FlashKernel.SetPowerAmountAt(ref battle, first, 0);
    Check(battle.PowerCount == 1 && battle.Powers[0].Amount == 4,
        "沙坑归零应只移除该实例");

    // Fixed option list: exactly one bit is selected and the request carries the option values.
    FlashBattle optionBattle = NewBattle();
    FlashAction optionAction = FlashAction.EndTurn().WithChoice(0b10);
    FlashExec optionExec = new(ref optionBattle, battleRules, optionAction);
    Span<FlashCard> options = stackalloc FlashCard[2];
    options[0] = FlashKernel.CreateCard(battleRules, FlashRegistry.CardId<Disintegration>());
    options[1] = FlashKernel.CreateCard(battleRules, FlashRegistry.CardId<MindRot>());
    Check(FlashKernel.ChooseOption(ref optionExec, FlashChoiceEffect.ApplyKnowledgeCurse, options, out int selected),
        "固定选项选择应被动作满足");
    Check(selected == 1 && !optionExec.NeedsChoice, "固定选项应选中第二个选项");
    FlashExec missing = new(ref optionBattle, battleRules, default);
    Check(!FlashKernel.ChooseOption(ref missing, FlashChoiceEffect.ApplyKnowledgeCurse, options, out _),
        "动作没有选择时固定选项应返回 NeedsChoice");
    Check(missing.NeedsChoice && missing.Request.Kind == FlashChoiceKind.SelectOption
        && missing.Request.OptionCount == 2 && missing.Request.MinCount == 1 && missing.Request.MaxCount == 1,
        "固定选项请求应为 2 项单选");
    FlashExec invalid = new(ref optionBattle, battleRules, FlashAction.EndTurn().WithChoice(0b11));
    bool rejected = false;
    try
    {
        FlashKernel.ChooseOption(ref invalid, FlashChoiceEffect.ApplyKnowledgeCurse, options, out _);
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }
    Check(rejected, "固定选项选择两个或零个选项应被拒绝");

    // EntropicBrew: out-of-combat pool generation fills an open slot with freeFromEntropic set; using
    // that generated potion records one generated use and clears the slot's free bit.
    FlashRules rules = MbPotionRules();
    Check(FlashRegistry.TryPotion("ENTROPIC_BREW", out ushort brew), "缺少 ENTROPIC_BREW");
    Check(FlashRegistry.TryPotion("FIRE_POTION", out ushort fire), "缺少 FIRE_POTION");
    FlashBattle potionBattle = NewBattle();
    potionBattle.PotionSlotCount = 1;
    potionBattle.Potions[0] = (ushort)(brew + 1);
    // The pool has a single Common potion; this recorded state's first rarity roll is 0.875.
    potionBattle.RngStream(FlashRngStream.CombatPotionGeneration) = FlashRngState.FromSerializable(
        0, 0x161922C645CE50E8, 0xAD760CAFA1697B60, 0x3501FF44902CA50D, 0x417CB9A826D831DF);
    FlashKernel.Step(ref potionBattle, FlashAction.UsePotion(0, FlashLimits.NoOwner), rules, out _);
    Check(potionBattle.Potions[0] == (ushort)(fire + 1) && potionBattle.Potions[0] != brew + 1,
        "EntropicBrew 应把随机战斗外药水放入空槽");
    Check((potionBattle.Counter(FlashCounter.EntropicFreePotionMask) & 0b1) != 0,
        "EntropicBrew 生成的药水槽应标记为 free");
    int enemyHp = potionBattle.Enemies[0].Creature.CurrentHp;
    FlashKernel.Step(ref potionBattle, FlashAction.UsePotion(0, FlashLimits.FirstEnemyOwner), rules, out _);
    Check(potionBattle.Potions[0] == 0 && potionBattle.Counter(FlashCounter.GeneratedPotionUseCount) == 1,
        "使用免费药水应记录一次生成药水使用");
    Check(potionBattle.Counter(FlashCounter.GeneratedPotionStrategicCost) == 0,
        "免费药水不应计入生成药水战略成本");
    Check(potionBattle.Counter(FlashCounter.EntropicFreePotionMask) == 0, "使用后应清除免费位");
    Check(potionBattle.Enemies[0].Creature.CurrentHp < enemyHp, "免费药水效果应正常结算");
}

static FlashRules MbPotionRules()
{
    FlashRulesBuilder builder = new() { BaseMaxEnergy = 3, HasEncounter = true, BaseOrbSlotCount = 0 };
    Check(FlashRegistry.TryPotion("ENTROPIC_BREW", out ushort brew), "缺少 ENTROPIC_BREW");
    Check(FlashRegistry.TryPotion("FIRE_POTION", out ushort fire), "缺少 FIRE_POTION");
    builder.SetPotion(brew, new FlashPotionStats
    {
        Target = FlashTargetType.Self, Rarity = FlashPotionRarity.Rare, StrategicCost = 0,
        Vars = [], VarProps = [],
    });
    builder.SetPotion(fire, new FlashPotionStats
    {
        Target = FlashTargetType.AnyEnemy, Rarity = FlashPotionRarity.Common, StrategicCost = 9,
        Vars = [20m], VarProps = [FlashValueProps.None],
    });
    builder.SetPotionPool([fire]);
    builder.SetOutOfCombatPotionPool([fire]);
    builder.SetRootPotion(0, brew);
    FlashRules built = builder.Build();
    return built;
}

static FlashRules SliceRules()
{
    FlashRulesBuilder builder = new()
    {
        BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first", "second", "third"], BaseOrbSlotCount = 3,
        // The CX contracts install PerfectFit/SlumberingEssence on hand/discard cards.
        HasShuffleOrderEnchantment = true, HasBeforeFlushEnchantment = true,
    };
    FlashRegistry.TryRelic("CRACKED_CORE", out ushort crackedCore);
    builder.SetRelicVars(crackedCore, [1m]);
    FlashRegistry.TryRelic("RUNIC_CAPACITOR", out ushort runicCapacitor);
    builder.SetRelicVars(runicCapacitor, [3m]);
    FlashRegistry.TryRelic("SYMBIOTIC_VIRUS", out ushort symbioticVirus);
    builder.SetRelicVars(symbioticVirus, [1m]);
    FlashRegistry.TryRelic("REPTILE_TRINKET", out ushort reptile);
    builder.SetRelicVars(reptile, [3m]);
    FlashRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike);
    FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend);
    FlashRegistry.TryCard("BASH", out ushort bash);
    FlashRegistry.TryCard("WHIRLWIND", out ushort whirlwind);
    builder.SetCard(strike, 0, Stats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 1, [6m]));
    builder.SetCard(defend, 0, Stats(FlashCardType.Skill, FlashTargetType.Self, 1, [5m]));
    builder.SetCard(bash, 0, Stats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 2, [8m, 2m]));
    builder.SetCard(whirlwind, 0, Stats(FlashCardType.Attack, FlashTargetType.AllEnemies, 0, [5m], costsX: true));
    FlashRegistry.TryCard("FETCH", out ushort fetch);
    builder.SetCard(fetch, 0, Stats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 0, [3m, 1m]));
    // RG: the forge kernel creates SOVEREIGN_BLADE; the star-cost contract uses a synthetic card.
    FlashRegistry.TryCard("SOVEREIGN_BLADE", out ushort blade);
    builder.SetCard(blade, 0, Stats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 2, [10m]));
    builder.SetCard(blade, 1, Stats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 2, [13m]));
    FlashRegistry.TryCard("RG_TEST_STAR_CARD", out ushort starCard);
    builder.SetCard(starCard, 0, Stats(FlashCardType.Skill, FlashTargetType.Self, 0, [0m], starCost: 2));
    // LR: choice, transform, history-counter and VoidForm cards used by the K0 contracts.
    void LrCard(string id, FlashCardType type, FlashTargetType target, int cost, decimal[] vars,
        int starCost = -1)
    {
        FlashRegistry.TryCard(id, out ushort card);
        builder.SetCard(card, 0, Stats(type, target, cost, vars, starCost: starCost));
        builder.SetCard(card, 1, Stats(type, target, cost, vars, starCost: starCost));
    }
    LrCard("TYRANNY", FlashCardType.Power, FlashTargetType.Self, 1, []);
    LrCard("FOREGONE_CONCLUSION", FlashCardType.Skill, FlashTargetType.Self, 1, [2m]);
    LrCard("CHARGE", FlashCardType.Skill, FlashTargetType.Self, 1, [2m]);
    LrCard("GUARDS", FlashCardType.Skill, FlashTargetType.Self, 2, []);
    LrCard("BEGONE", FlashCardType.Skill, FlashTargetType.Self, 1, []);
    LrCard("HEIRLOOM_HAMMER", FlashCardType.Attack, FlashTargetType.AnyEnemy, 2, [20m, 1m]);
    LrCard("DECISIONS_DECISIONS", FlashCardType.Skill, FlashTargetType.Self, 0, [3m, 3m], starCost: 6);
    LrCard("MINION_DIVE_BOMB", FlashCardType.Attack, FlashTargetType.AnyEnemy, 0, [13m]);
    LrCard("MINION_SACRIFICE", FlashCardType.Skill, FlashTargetType.Self, 0, [7m]);
    LrCard("MINION_STRIKE", FlashCardType.Attack, FlashTargetType.AnyEnemy, 0, [6m, 1m]);
    LrCard("SUPERMASSIVE", FlashCardType.Attack, FlashTargetType.AnyEnemy, 1, [5m, 3m]);
    LrCard("BEAT_INTO_SHAPE", FlashCardType.Attack, FlashTargetType.AnyEnemy, 1, [5m, 5m, 5m]);
    LrCard("KINGLY_KICK", FlashCardType.Attack, FlashTargetType.AnyEnemy, 4, [27m]);
    LrCard("KINGLY_PUNCH", FlashCardType.Attack, FlashTargetType.AnyEnemy, 1, [8m, 4m]);
    LrCard("VOID_FORM", FlashCardType.Power, FlashTargetType.Self, 3, [2m]);

    // MB: Knowledge Demon's fixed curse options only need canonical card values.
    FlashRegistry.TryCard("DISINTEGRATION", out ushort disintegration);
    builder.SetCard(disintegration, 0, Stats(FlashCardType.Status, FlashTargetType.Self, -1, []));
    FlashRegistry.TryCard("MIND_ROT", out ushort mindRot);
    builder.SetCard(mindRot, 0, Stats(FlashCardType.Status, FlashTargetType.Self, -1, []));
    // CX: a 0-cost skill whose OnPlay removes another hand card. The Imbued contract uses it to make a
    // captured card disappear before its auto-play turn, which must be an explicit boundary.
    FlashRegistry.TryCard("CX_TEST_IMBUED_REMOVER", out ushort cxRemover);
    builder.SetCard(cxRemover, 0, Stats(FlashCardType.Skill, FlashTargetType.Self, 0, []));
    FlashRegistry.TryPower("VULNERABLE_POWER", out ushort vulnerable);
    builder.SetPowerVars(vulnerable, [1.5m]);
    FlashRegistry.TryMonster("NIBBIT", out ushort nibbit);
    builder.RootEnemyCount = 1;
    builder.EncounterSlots = ["first", "second", "third"];
    builder.SetMonster(0, new FlashMonsterRules
    {
        MonsterId = nibbit,
        Nodes =
        [
            new FlashMonsterNode { NativeId = "INIT_MOVE", Kind = FlashMonsterNodeKind.ConditionalBranch, ConditionalTarget = 1 },
            new FlashMonsterNode { NativeId = "BUTT_MOVE", Kind = FlashMonsterNodeKind.Move, FollowUp = 2, Attacks = [new(12, 1)] },
            new FlashMonsterNode { NativeId = "SLICE_MOVE", Kind = FlashMonsterNodeKind.Move, FollowUp = 3, Attacks = [new(6, 1)] },
            new FlashMonsterNode { NativeId = "HISS_MOVE", Kind = FlashMonsterNodeKind.Move, FollowUp = 1 },
            new FlashMonsterNode
            {
                NativeId = "STUNNED", Kind = FlashMonsterNodeKind.Move, MustPerformOnceBeforeTransitioning = true,
            },
        ],
        InitialNode = 0,
        PendingInitialMoveNode = 1,
        StunnedNode = 4,
        InitialHpMin = 17,
        InitialHpMax = 21,
        StaticInts = [5, 2],
        InitialAiRegisters = [2, 0],
    });
    return builder.Build();

    static FlashCardStats Stats(FlashCardType type, FlashTargetType target, int cost, decimal[] vars,
        bool costsX = false, int starCost = -1) => new()
    {
        Type = type, Target = target, Rarity = FlashCardRarity.Basic, EnergyCost = cost, CostsX = costsX,
        StarCost = starCost, StarCostsX = false, Keywords = FlashKeywords.None, MaxUpgradeLevel = 1, Vars = vars,
    };
}



// ID: card-instance ledgers and instance powers. Monologue's instance Strength, SwordSage's per-blade
// applied replay bonus, Nightmare's detached selection, Orbit's energy remainder and the
// UnsettlingLamp latch are the stable-boundary contract; Fetch's played bit and Vambrace's trigger are
// per-card/per-play values and are asserted through their kernel setters.
static void CheckIdKernel(FlashRules rules)
{
    Check(FlashRegistry.TryCard("SOVEREIGN_BLADE", out ushort blade), "缺少 SOVEREIGN_BLADE 定义");
    Check(FlashRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike), "缺少 STRIKE_IRONCLAD 定义");
    Check(FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD 定义");
    Check(FlashRegistry.TryCard("FETCH", out ushort fetch), "缺少 FETCH 定义");
    Check(FlashRegistry.TryPower("MONOLOGUE_POWER", out ushort monologue), "缺少 MONOLOGUE_POWER");
    Check(FlashRegistry.TryPower("STRENGTH_POWER", out ushort strength), "缺少 STRENGTH_POWER");
    Check(FlashRegistry.TryPower("SWORD_SAGE_POWER", out ushort swordSage), "缺少 SWORD_SAGE_POWER");
    Check(FlashRegistry.TryPower("NIGHTMARE_POWER", out ushort nightmare), "缺少 NIGHTMARE_POWER");
    Check(FlashRegistry.TryPower("ORBIT_POWER", out ushort orbit), "缺少 ORBIT_POWER");
    Check(FlashRegistry.TryPower("DRAW_CARDS_NEXT_TURN_POWER", out ushort drawNext), "缺少 DRAW_CARDS_NEXT_TURN_POWER");
    Check(FlashRegistry.TryPower("WEAK_POWER", out ushort weak), "缺少 WEAK_POWER");
    Check(FlashRegistry.TryPower("VULNERABLE_POWER", out ushort vulnerable), "缺少 VULNERABLE_POWER");
    Check(FlashRegistry.TryRelic("UNSETTLING_LAMP", out ushort lamp), "缺少 UNSETTLING_LAMP");

    // Monologue: two instances keep separate Strength/StrengthApplied rows; a play applies both and the
    // side turn end removes each and reverts its applied Strength.
    FlashBattle monologueBattle = NewBattle();
    FlashPiles.Append(ref monologueBattle, FlashPile.Hand, FlashKernel.CreateCard(rules, strike));
    FlashExec monologueExec = new(ref monologueBattle, rules, default);
    FlashKernel.ApplyPower(ref monologueExec, FlashLimits.PlayerOwner, monologue, 1, FlashLimits.PlayerOwner,
        FlashLimits.PlayerOwner);
    FlashKernel.ApplyPower(ref monologueExec, FlashLimits.PlayerOwner, monologue, 1, FlashLimits.PlayerOwner,
        FlashLimits.PlayerOwner);
    Check(monologueBattle.PowerCount == 2, "Monologue 应有两个实例行");
    monologueBattle.Powers[0].Aux0 = 3;
    monologueBattle.Powers[1].Aux0 = 5;
    FlashKernel.ManualPlay(ref monologueExec, 0, FlashKernel.EnemyOwner(0));
    Check(FlashKernel.GetAmount(monologueBattle, FlashLimits.PlayerOwner, strength) == 8,
        "Monologue 应在出牌后施加两个实例的 Strength");
    Check(monologueBattle.Powers[0].Aux1 == 3 && monologueBattle.Powers[1].Aux1 == 5,
        "Monologue 的 StrengthApplied 应按实例累计");
    // EndTurnPowerSupport.Batch048 visits each instance; removing the first shifts the second to row 0.
    FlashPowerDef monologueDef = FlashRegistry.Power(monologue);
    monologueDef.EndTurnBatch048(ref monologueExec, new FlashListener(FlashListenerKind.Power, 0,
        FlashLimits.PlayerOwner), FlashSide.Player, participates: true);
    monologueDef.EndTurnBatch048(ref monologueExec, new FlashListener(FlashListenerKind.Power, 0,
        FlashLimits.PlayerOwner), FlashSide.Player, participates: true);
    Check(FlashKernel.GetAmount(monologueBattle, FlashLimits.PlayerOwner, monologue) == 0
          && FlashKernel.GetAmount(monologueBattle, FlashLimits.PlayerOwner, strength) == 0,
        "Monologue 应在玩家回合末移除并归还 Strength");

    // SwordSage: the per-blade applied bonus is a card value; normalization follows the power amount.
    FlashBattle sageBattle = NewBattle();
    FlashCard sageBlade = FlashKernel.CreateCard(rules, blade);
    FlashPiles.Append(ref sageBattle, FlashPile.Hand, sageBlade);
    FlashExec sageExec = new(ref sageBattle, rules, default);
    FlashKernel.ApplyPower(ref sageExec, FlashLimits.PlayerOwner, swordSage, 2, FlashLimits.PlayerOwner);
    FlashKernel.NormalizeCardAfflictions(ref sageExec);
    Check(sageBattle.Cards[0].ReplayCount == 2 && sageBattle.Cards[0].Private == 2,
        $"SwordSage 应把重放加到 2 并记录已应用值，实际 {sageBattle.Cards[0].ReplayCount}/{sageBattle.Cards[0].Private}");
    FlashKernel.SetPowerAmount(ref sageBattle, FlashLimits.PlayerOwner, swordSage, 0);
    FlashKernel.NormalizeCardAfflictions(ref sageExec);
    Check(sageBattle.Cards[0].ReplayCount == 0 && sageBattle.Cards[0].Private == 0,
        "SwordSage 移除应回退已应用的重放");
    FlashCard sageClone = FlashKernel.CloneCard(sageBattle.Cards[0]);
    Check(sageClone.Private == FlashLimits.UninitializedLedger,
        "克隆卡应重置实例账本与 Fetch 位");

    // Nightmare: the selected card lives in DetachedCards; BeforeHandDraw clones it Amount times,
    // removes the instance and frees the slot.
    FlashBattle nightmareBattle = NewBattle();
    FlashExec nightmareExec = new(ref nightmareBattle, rules, default);
    FlashKernel.ApplyPower(ref nightmareExec, FlashLimits.PlayerOwner, nightmare, 3, FlashLimits.PlayerOwner,
        FlashLimits.PlayerOwner);
    FlashCard selected = FlashKernel.CreateCard(rules, strike);
    int slot = FlashKernel.AllocateDetachedCard(ref nightmareBattle, selected);
    nightmareBattle.Powers[0].Aux0 = slot + 1;
    FlashKernel.BeforeHandDraw(ref nightmareExec);
    Check(nightmareBattle.HandCount == 3, $"Nightmare 应生成 3 张选定卡的克隆，实际手牌 {nightmareBattle.HandCount}");
    Check(FlashKernel.GetAmount(nightmareBattle, FlashLimits.PlayerOwner, nightmare) == 0,
        "Nightmare 结算后应移除实例");
    Check(nightmareBattle.DetachedCards[slot].Id == 0, "Nightmare 实例移除应释放 detached 槽位");
    for (int index = 0; index < nightmareBattle.HandCount; index++)
    {
        Check(nightmareBattle.Cards[index].Id == strike && nightmareBattle.Cards[index].IsClone,
            "Nightmare 生成的卡应是选定卡的克隆");
    }

    // Orbit: spent energy accumulates in Aux0; each full 4 grants Amount energy.
    FlashBattle orbitBattle = NewBattle();
    orbitBattle.Energy = 0;
    FlashExec orbitExec = new(ref orbitBattle, rules, default);
    FlashKernel.ApplyPower(ref orbitExec, FlashLimits.PlayerOwner, orbit, 2, FlashLimits.PlayerOwner,
        FlashLimits.PlayerOwner);
    FlashCard energyCard = FlashKernel.CreateCard(rules, strike);
    FlashHooks.AfterEnergySpent(ref orbitExec, energyCard, 3);
    Check(orbitBattle.Powers[0].Aux0 == 3 && orbitBattle.Energy == 0,
        $"Orbit 3 点能量应只留下余数 3，实际余数 {orbitBattle.Powers[0].Aux0} 能量 {orbitBattle.Energy}");
    FlashHooks.AfterEnergySpent(ref orbitExec, energyCard, 1);
    Check(orbitBattle.Powers[0].Aux0 == 0 && orbitBattle.Energy == 2,
        $"Orbit 满 4 点应给 2 能量，实际余数 {orbitBattle.Powers[0].Aux0} 能量 {orbitBattle.Energy}");

    // PaleBlueDot: the fifth completed play latches and grants next-turn draws once.
    FlashBattle dotBattle = NewBattle();
    FlashPiles.Append(ref dotBattle, FlashPile.Hand, FlashKernel.CreateCard(rules, strike));
    dotBattle.SetCounter(FlashCounter.CardsPlayedThisTurn, 4);
    FlashExec dotExec = new(ref dotBattle, rules, default);
    FlashKernel.ApplyPower(ref dotExec, FlashLimits.PlayerOwner,
        FlashRegistry.PowerId<PaleBlueDotPower>(), 2, FlashLimits.PlayerOwner);
    FlashKernel.ManualPlay(ref dotExec, 0, FlashKernel.EnemyOwner(0));
    Check(dotBattle.Powers[0].Aux0 == 1, "PaleBlueDot 第五次出牌应锁存");
    Check(FlashKernel.GetAmount(dotBattle, FlashLimits.PlayerOwner, drawNext) == 2,
        "PaleBlueDot 应给出 2 层下回合抽牌");

    // UnsettlingLamp: the first card-sourced debuff is doubled once; the internal temporary type is
    // exempt; completing the card power scope latches and clears the transient trigger.
    FlashBattle lampBattle = NewBattle();
    lampBattle.Flags |= FlashBattleFlags.PlayerHooksActive;
    lampBattle.RelicCount = 1;
    lampBattle.Relics[0] = new FlashRelic { Id = lamp };
    FlashExec lampExec = new(ref lampBattle, rules, default);
    lampExec.CardPowerSourceActive = true;
    lampExec.CardPowerSourceSerial = ++lampExec.CardPowerSourceSerialCounter;
    int weakApplied = FlashKernel.ApplyPower(ref lampExec, FlashKernel.EnemyOwner(0), weak, 1,
        FlashLimits.PlayerOwner);
    Check(weakApplied == 2, $"UnsettlingLamp 应把首个减益翻倍，实际 {weakApplied}");
    int vulnerableApplied = FlashKernel.ApplyPower(ref lampExec, FlashKernel.EnemyOwner(0), vulnerable, 1,
        FlashLimits.PlayerOwner);
    Check(vulnerableApplied == 2, $"同一张牌的第二个减益也应翻倍，实际 {vulnerableApplied}");
    FlashKernel.CompleteCardPowerSource(ref lampExec);
    Check(lampBattle.Relics[0].Counter == 1 && lampExec.LampTriggerSerial == 0,
        "UnsettlingLamp 完成卡牌 Power 作用域后应锁存并清空触发");
    lampExec.CardPowerSourceSerial = ++lampExec.CardPowerSourceSerialCounter;
    int weakAgain = FlashKernel.ApplyPower(ref lampExec, FlashKernel.EnemyOwner(0), weak, 1,
        FlashLimits.PlayerOwner);
    Check(weakAgain == 1, $"已锁存的 UnsettlingLamp 不应再翻倍，实际 {weakAgain}");

    // Fetch: completing a play sets the per-card bit; a clone starts clear.
    FlashBattle fetchBattle = NewBattle();
    FlashPiles.Append(ref fetchBattle, FlashPile.Hand, FlashKernel.CreateCard(rules, fetch));
    FlashExec fetchExec = new(ref fetchBattle, rules, default);
    FlashKernel.ManualPlay(ref fetchExec, 0, FlashKernel.EnemyOwner(0));
    int fetchPosition = FlashPiles.Start(fetchBattle, FlashPile.Discard);
    Check(fetchBattle.DiscardCount == 1 && fetchBattle.Cards[fetchPosition].Id == fetch,
        "Fetch 应完成并进入弃牌堆");
    Check((fetchBattle.Cards[fetchPosition].Private & FlashKernel.FetchPlayedBit) != 0,
        "Fetch 完成一次出牌后应置本回合已打出位");
    Check((FlashKernel.CloneCard(fetchBattle.Cards[fetchPosition]).Private & FlashKernel.FetchPlayedBit) == 0,
        "Fetch 的克隆不应继承已打出位");

    // Vambrace: the first card-sourced block of the combat is doubled; the relic latches on the
    // matching AfterCardPlayed and never doubles again.
    Check(FlashRegistry.TryRelic("VAMBRACE", out ushort vambrace), "缺少 VAMBRACE");
    FlashBattle vambraceBattle = NewBattle();
    vambraceBattle.Flags |= FlashBattleFlags.PlayerHooksActive;
    vambraceBattle.RelicCount = 1;
    vambraceBattle.Relics[0] = new FlashRelic { Id = vambrace };
    FlashExec vambraceExec = new(ref vambraceBattle, rules, default);
    vambraceExec.Card = FlashKernel.CreateCard(rules, defend);
    decimal doubled = FlashKernel.GainBlock(ref vambraceExec, FlashLimits.PlayerOwner, 5m, FlashValueProps.Move,
        fromCard: true);
    Check(doubled == 10m && vambraceBattle.Player.Block == 10,
        $"Vambrace 应把首次格挡翻倍，实际 {doubled}/{vambraceBattle.Player.Block}");
    Check(vambraceExec.VambraceTriggerActive, "Vambrace 应记录触发卡");
}

// LS: contracts for the remaining Silent/Necrobinder/Defect kernel capabilities: the power
// amount-given pass, EchoingSlash attack waves, KnifeTrap's duplicate-Shiv auto-play and the
// drawn-card position assumption, Misery's temporary-power boundary, MomentumStrike's live play slot
// and HelicalDart's temporary Dexterity.
static void CheckLsKernel()
{
    FlashRules rules = LsRules();
    Check(FlashRegistry.TryCard("ECHOING_SLASH", out ushort echoing), "缺少 ECHOING_SLASH");
    Check(FlashRegistry.TryCard("KNIFE_TRAP", out ushort knifeTrap), "缺少 KNIFE_TRAP");
    Check(FlashRegistry.TryCard("SHIV", out ushort shiv), "缺少 SHIV");
    Check(FlashRegistry.TryCard("MOMENTUM_STRIKE", out ushort momentum), "缺少 MOMENTUM_STRIKE");
    Check(FlashRegistry.TryCard("MISERY", out ushort misery), "缺少 MISERY");
    Check(FlashRegistry.TryPower("POISON_POWER", out ushort poison), "缺少 POISON_POWER");
    Check(FlashRegistry.TryPower("PIERCING_WAIL_POWER", out ushort piercing), "缺少 PIERCING_WAIL_POWER");
    Check(FlashRegistry.TryPower("DEXTERITY_POWER", out ushort dexterity), "缺少 DEXTERITY_POWER");
    Check(FlashRegistry.TryPower("HELICAL_DART_POWER", out ushort helicalPower), "缺少 HELICAL_DART_POWER");
    Check(FlashRegistry.TryRelic("SNECKO_SKULL", out ushort skull), "缺少 SNECKO_SKULL");
    Check(FlashRegistry.TryRelic("HELICAL_DART", out ushort dart), "缺少 HELICAL_DART");

    // ModifyPowerAmountGiven: SneckoSkull adds 1 to Poison the player gives.
    FlashBattle battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    battle.RelicCount = 1;
    battle.Relics[0] = new FlashRelic { Id = skull };
    FlashExec x = new(ref battle, rules, default);
    int applied = FlashKernel.ApplyPower(ref x, 2, poison, 2, FlashLimits.PlayerOwner);
    Check(applied == 3, $"SneckoSkull 应把毒 2 变成 3，实际 {applied}");
    Check(FlashKernel.GetAmount(battle, 2, poison) == 3, "SneckoSkull 后的毒层数应为 3");

    // EchoingSlash: the first wave kills one enemy, which adds a second wave.
    battle = NewBattle();
    battle.EnemyCount = 2;
    battle.RosterCount = 2;
    battle.Roster[1] = 1;
    battle.Enemies[0].Creature.CurrentHp = 5;
    battle.Enemies[0].Creature.MaxHp = 5;
    battle.Enemies[1].Creature.CurrentHp = 100;
    battle.Enemies[1].Creature.MaxHp = 100;
    battle.Enemies[1].Flags = FlashEnemyFlags.InCombat;
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, echoing));
    x = new(ref battle, rules, default);
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, FlashLimits.NoOwner), rules, out _);
    Check(battle.Enemies[0].Creature.IsDead, "EchoingSlash 第一波应击杀低血量敌人");
    Check(battle.Enemies[1].Creature.CurrentHp == 80,
        $"EchoingSlash 击杀后应追加一波，实际敌人生命 {battle.Enemies[1].Creature.CurrentHp}");

    // KnifeTrap: two byte-equal Shivs in the exhaust pile are both auto-played (duplicate copies
    // must not make the second search pick the first again).
    battle = NewBattle();
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, knifeTrap));
    FlashPiles.Append(ref battle, FlashPile.Exhaust, FlashKernel.CreateCard(rules, shiv));
    FlashPiles.Append(ref battle, FlashPile.Exhaust, FlashKernel.CreateCard(rules, shiv));
    x = new(ref battle, rules, default);
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, 2), rules, out _);
    Check(battle.Enemies[0].Creature.CurrentHp == 92,
        $"KnifeTrap 应自动打出两张 Shiv（12 伤害），实际生命 {battle.Enemies[0].Creature.CurrentHp}");
    Check(battle.ExhaustCount == 2, "两张 Shiv 打出后应回到消耗堆");

    // Upgraded KnifeTrap upgrades every exhausted Shiv before playing it.
    battle = NewBattle();
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, knifeTrap, 1));
    FlashPiles.Append(ref battle, FlashPile.Exhaust, FlashKernel.CreateCard(rules, shiv));
    FlashPiles.Append(ref battle, FlashPile.Exhaust, FlashKernel.CreateCard(rules, shiv));
    x = new(ref battle, rules, default);
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, 2), rules, out _);
    for (int index = 0; index < battle.ExhaustCount; index++)
    {
        FlashCard card = battle.Cards[FlashPiles.Start(battle, FlashPile.Exhaust) + index];
        Check(card.Id == shiv && card.Upgrade == 1, "升级版 KnifeTrap 应把每张 Shiv 升级");
    }

    // Drawn-card discard positions are an explicit assumption: a hook that moved the drawn card
    // leaves a different byte-equal copy at the slot, which must be rejected rather than discarded.
    battle = NewBattle();
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, shiv));
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, echoing));
    Span<FlashCard> drawn = stackalloc FlashCard[1];
    Span<int> positions = stackalloc int[1];
    drawn[0] = FlashKernel.CreateCard(rules, knifeTrap);
    positions[0] = 0;
    Span<int> selected = stackalloc int[1];
    selected[0] = 0;
    x = new(ref battle, rules, default);
    bool positionUnsupported = false;
    try
    {
        FlashCardEffects.DiscardDrawn(ref x, drawn, positions, selected);
    }
    catch (FlashUnsupportedException)
    {
        positionUnsupported = true;
    }
    Check(positionUnsupported, "抓牌位置不匹配时应抛 FlashUnsupportedException");

    // Burst adds one replay to the next Skill; the block card counter and block gain both run twice.
    battle = NewBattle();
    battle.Energy = 9;
    Check(FlashRegistry.TryCard("BURST", out ushort burstCard), "缺少 BURST");
    Check(FlashRegistry.TryCard("MIRAGE", out ushort mirageCard), "缺少 MIRAGE");
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, burstCard));
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, mirageCard));
    x = new(ref battle, rules, default);
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, FlashLimits.NoOwner), rules, out _);
    Check(FlashKernel.GetAmount(battle, 0, FlashRegistry.PowerId<BurstPower>()) == 1,
        $"Burst 应施加 1 层 BurstPower，实际 {FlashKernel.GetAmount(battle, 0, FlashRegistry.PowerId<BurstPower>())}");
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, FlashLimits.NoOwner), rules, out _);
    Check(battle.Counter(FlashCounter.BlockCardsPlayedThisTurn) == 2,
        $"Burst 应重放 Mirage 两次，格挡牌计数实际 {battle.Counter(FlashCounter.BlockCardsPlayedThisTurn)}");

    battle = NewBattle();
    battle.Energy = 9;
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, burstCard));
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, mirageCard));
    x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, 0, dexterity, 2, 0);
    FlashKernel.ApplyPower(ref x, 0, FlashRegistry.PowerId<ShadowmeldPower>(), 1, 0);
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, FlashLimits.NoOwner), rules, out _);
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, FlashLimits.NoOwner), rules, out _);
    Check(battle.Player.Block == 16, $"Burst+Mirage(2,dex,shadowmeld) 应为 16 格挡，实际 {battle.Player.Block}");

    // Misery clones a temporary debuff as its own power row (the native ApplyClonedPower skips the
    // temporary BeforeApplied) and leaves the internally applied stat alone when it is not among the
    // cloned debuffs (PiercingWail's Strength is a buff).
    battle = NewBattle();
    battle.Energy = 9;
    battle.EnemyCount = 2;
    battle.RosterCount = 2;
    battle.Roster[1] = 1;
    battle.Enemies[1].Creature.CurrentHp = 100;
    battle.Enemies[1].Creature.MaxHp = 100;
    battle.Enemies[1].Flags = FlashEnemyFlags.InCombat;
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, misery));
    x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, 2, piercing, 1, FlashLimits.PlayerOwner);
    x = new(ref battle, rules, default);
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, 2), rules, out _);
    Check(FlashKernel.GetAmount(battle, 3, piercing) == 1, "Misery 应把临时减益克隆到其他敌人");
    Check(FlashKernel.GetAmount(battle, 3, dexterity) == 0, "PiercingWail 的内部力量是增益，不应被合并");

    // MomentumStrike sets its own cost to 0 this combat; PlayPosition is valid during OnPlay.
    battle = NewBattle();
    battle.Energy = 9;
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, momentum));
    x = new(ref battle, rules, default);
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, 2), rules, out _);
    bool found = false;
    for (int index = 0; index < battle.DiscardCount; index++)
    {
        FlashCard card = battle.Cards[FlashPiles.Start(battle, FlashPile.Discard) + index];
        if (card.Id != momentum)
            continue;
        found = true;
        Check(card.EnergyModifierCount == 1, "MomentumStrike 应留下一条费用修饰");
        Check(FlashKernel.EnergyCostValueWithModifiers(battle, rules, card, FlashPile.Discard) == 0,
            "MomentumStrike 本场费用应为 0");
    }
    Check(found, "MomentumStrike 应进入弃牌堆");

    // HelicalDart: a Shiv play applies 1 temporary Dexterity.
    battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    battle.RelicCount = 1;
    battle.Relics[0] = new FlashRelic { Id = dart };
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, shiv));
    x = new(ref battle, rules, default);
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, 2), rules, out _);
    Check(FlashKernel.GetAmount(battle, 0, dexterity) == 1, "HelicalDart 应给 1 敏捷");
    Check(FlashKernel.GetAmount(battle, 0, helicalPower) == 1, "HelicalDart 应施加临时敏捷层");
}

static FlashRules LsRules()
{
    FlashRulesBuilder builder = new() { BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first"], RootEnemyCount = 0 };
    FlashRegistry.TryCard("ECHOING_SLASH", out ushort echoing);
    FlashRegistry.TryCard("KNIFE_TRAP", out ushort knifeTrap);
    FlashRegistry.TryCard("SHIV", out ushort shiv);
    FlashRegistry.TryCard("MOMENTUM_STRIKE", out ushort momentum);
    FlashRegistry.TryCard("MISERY", out ushort misery);
    FlashRegistry.TryRelic("SNECKO_SKULL", out ushort skull);
    FlashRegistry.TryRelic("HELICAL_DART", out ushort dart);
    builder.SetCard(echoing, 0, LsStats(FlashCardType.Attack, FlashTargetType.AllEnemies, 1, [10m]));
    builder.SetCard(knifeTrap, 0, LsStats(FlashCardType.Skill, FlashTargetType.AnyEnemy, 1));
    builder.SetCard(knifeTrap, 1, LsStats(FlashCardType.Skill, FlashTargetType.AnyEnemy, 1));
    builder.SetCard(shiv, 0, LsStats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 0, [4m],
        FlashKeywords.Exhaust, FlashCardTag.Shiv));
    builder.SetCard(shiv, 1, LsStats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 0, [6m],
        FlashKeywords.Exhaust, FlashCardTag.Shiv));
    builder.SetCard(momentum, 0, LsStats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 1, [11m]));
    builder.SetCard(misery, 0, LsStats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 0, [7m]));
    FlashRegistry.TryCard("BURST", out ushort burst);
    FlashRegistry.TryCard("MIRAGE", out ushort mirage);
    builder.SetCard(burst, 0, LsStats(FlashCardType.Skill, FlashTargetType.Self, 1, [1m]));
    builder.SetCard(mirage, 0, LsStats(FlashCardType.Skill, FlashTargetType.Self, 1, [2m, 1m]));
    builder.SetRelicVars(skull, [1m]);
    builder.SetRelicVars(dart, [1m]);
    return builder.Build();

    static FlashCardStats LsStats(FlashCardType type, FlashTargetType target, int cost, decimal[]? vars = null,
        FlashKeywords keywords = FlashKeywords.None, FlashCardTag tags = FlashCardTag.None) => new()
    {
        Type = type, Target = target, Rarity = FlashCardRarity.Basic, EnergyCost = cost, CostsX = false,
        StarCost = -1, StarCostsX = false, Keywords = keywords, Tags = tags, MaxUpgradeLevel = 1,
        Vars = vars ?? [],
    };
}

// NB: contracts for the Necrobinder kernel capabilities. Osty's summon/revive ledger, the
// DieForYou redirect and spill-over, the Osty attack dealer, Doom's side-turn-end kill, the
// RightHandHand/Flatten/Flast cost-modifier hooks, EtherealPlays and the UnceasingTop special case.
static void CheckNbKernel()
{
    FlashRules rules = NbRules();
    Check(FlashRegistry.TryCard("STRIKE_NECROBINDER", out ushort strike), "缺少 STRIKE_NECROBINDER");
    Check(FlashRegistry.TryCard("DEFEND_NECROBINDER", out ushort defend), "缺少 DEFEND_NECROBINDER");
    Check(FlashRegistry.TryCard("BODYGUARD", out ushort bodyguard), "缺少 BODYGUARD");
    Check(FlashRegistry.TryCard("SOUL", out ushort soul), "缺少 SOUL");
    Check(FlashRegistry.TryCard("PARSE", out ushort parse), "缺少 PARSE");
    Check(FlashRegistry.TryCard("FLATTEN", out ushort flatten), "缺少 FLATTEN");
    Check(FlashRegistry.TryCard("HIGH_FIVE", out ushort highFive), "缺少 HIGH_FIVE");
    Check(FlashRegistry.TryCard("RIGHT_HAND_HAND", out ushort rightHandHand), "缺少 RIGHT_HAND_HAND");
    Check(FlashRegistry.TryPower("DIE_FOR_YOU_POWER", out ushort dieForYou), "缺少 DIE_FOR_YOU_POWER");
    Check(FlashRegistry.TryPower("DOOM_POWER", out ushort doom), "缺少 DOOM_POWER");
    Check(FlashRegistry.TryPower("OBLIVION_POWER", out ushort oblivion), "缺少 OBLIVION_POWER");
    Check(FlashRegistry.TryRelic("UNCEASING_TOP", out ushort unceasingTop), "缺少 UNCEASING_TOP");

    // Summon: the first call creates the pet at the requested HP, applies DieForYouPower 1 and consumes
    // one CombatId; a later summon raises both HP values; a dead pet is revived with the new amount.
    FlashBattle battle = NewBattle();
    battle.NextCombatId = 7;
    FlashExec x = new(ref battle, rules, default);
    FlashKernel.SummonOsty(ref x, 5);
    Check((battle.Flags & FlashBattleFlags.OstyPresent) != 0, "召唤后 Osty 应存在");
    Check(battle.Osty.CurrentHp == 5 && battle.Osty.MaxHp == 5, "首次召唤 HP 应为 5/5");
    Check(battle.NextCombatId == 8, "创建 Osty 应消耗一个 CombatId");
    Check(FlashKernel.GetAmount(battle, FlashLimits.OstyOwner, dieForYou) == 1, "首次召唤应获得 1 层 DieForYouPower");
    x = new(ref battle, rules, default);
    FlashKernel.SummonOsty(ref x, 3);
    Check(battle.Osty.CurrentHp == 8 && battle.Osty.MaxHp == 8, "存活 Osty 召唤应提高最大生命并治疗");
    battle.Osty.CurrentHp = 0;
    x = new(ref battle, rules, default);
    FlashKernel.SummonOsty(ref x, 4);
    Check(battle.Osty.CurrentHp == 4 && battle.Osty.MaxHp == 4, "死亡 Osty 召唤应按新值复活");
    x = new(ref battle, rules, default);
    FlashKernel.HealOsty(ref x, 99);
    Check(battle.Osty.CurrentHp == 4, "治疗 Osty 不得超过最大生命");

    // Redirect: powered attack damage above Osty's HP spills over to the player; unpowered damage and
    // a dead pet do not redirect.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    FlashKernel.SummonOsty(ref x, 8);
    battle.Player.CurrentHp = 50;
    Span<byte> player = stackalloc byte[1];
    player[0] = FlashLimits.PlayerOwner;
    x = new(ref battle, rules, default);
    FlashKernel.Damage(ref x, player, 20m, FlashValueProps.Move, FlashLimits.NoOwner, false, default);
    Check(battle.Osty.CurrentHp == 0, "Osty 应吸收其全部 HP 的伤害");
    Check(battle.Player.CurrentHp == 38, "Osty 死亡后的溢出伤害应打给玩家");
    Check((battle.Player.Flags & FlashCreatureFlags.LostHpThisTurn) != 0, "溢出伤害应标记玩家本回合失血");
    battle = NewBattle();
    x = new(ref battle, rules, default);
    FlashKernel.SummonOsty(ref x, 8);
    battle.Player.CurrentHp = 50;
    x = new(ref battle, rules, default);
    FlashKernel.Damage(ref x, player, 20m, FlashValueProps.Move | FlashValueProps.Unpowered, FlashLimits.NoOwner,
        false, default);
    Check(battle.Osty.CurrentHp == 8 && battle.Player.CurrentHp == 30, "Unpowered 伤害不得重定向到 Osty");

    // Osty attack: the pet is the dealer, so its attack history is recorded and the player's is not.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    FlashKernel.SummonOsty(ref x, 8);
    x.Card = FlashKernel.CreateCard(rules, flatten);
    byte enemy = FlashKernel.EnemyOwner(0);
    x = new(ref battle, rules, default);
    FlashKernel.CardAttack(ref x, 6m, 1, FlashAttackTargeting.Single, enemy, FlashValueProps.Move,
        FlashLimits.OstyOwner);
    Check(battle.Enemies[0].Creature.CurrentHp == 94, "Osty 攻击应伤害目标");
    Check(battle.Counter(FlashCounter.OstyAttacksThisTurn) == 1, "Osty 攻击应计入 Osty 本回合次数");
    Check(battle.Counter(FlashCounter.PlayerAttacksThisTurn) == 0, "Osty 攻击不得计入玩家攻击次数");

    // Assumption contract: a single-target attack without a target owner is unsupported, not an
    // index crash. K0 pins the FlashUnsupportedException.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    bool missingTargetThrew = false;
    try
    {
        FlashKernel.CardAttack(ref x, 6m, 1, FlashAttackTargeting.Single, FlashLimits.NoOwner);
    }
    catch (FlashUnsupportedException)
    {
        missingTargetThrew = true;
    }
    Check(missingTargetThrew, "单体攻击缺少目标应抛 FlashUnsupportedException");

    // Doom: the enemy-side turn-end hook kills every doomed participant together.
    battle = NewBattle();
    battle.Enemies[0].Creature.CurrentHp = 20;
    x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, enemy, doom, 25, FlashLimits.PlayerOwner);
    int doomIndex = FlashKernel.FindPower(battle, enemy, doom);
    Check(doomIndex >= 0, "Doom 应已施加");
    x = new(ref battle, rules, default);
    FlashListener doomListener = new(FlashListenerKind.Power, checked((byte)doomIndex), enemy);
    FlashRegistry.Power(doom).BeforeSideTurnEnd(ref x, doomListener, FlashSide.Enemy,
        FlashKernel.ParticipantMask(enemy));
    Check(battle.Enemies[0].Creature.IsDead, "Doom 达到生命值应在敌方回合末击杀");
    Check(battle.RosterCount == 0, "被 Doom 击杀的敌人应离开名单");

    // RightHandHand: any play whose energy value reaches its Energy variable returns the copy from the
    // discard pile to hand.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    FlashKernel.SummonOsty(ref x, 8);
    battle.Energy = 3;
    battle.Phase = FlashPlayerPhase.Play;
    battle.Side = FlashSide.Player;
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, highFive));
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, defend));
    FlashPiles.Append(ref battle, FlashPile.Discard, FlashKernel.CreateCard(rules, rightHandHand));
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, FlashLimits.NoOwner), rules, out _);
    Check(battle.HandCount == 2, "费用 >= RightHandHand 的 Energy 的打出应把弃牌堆的副本收回手牌");

    // Flatten: after an Osty attack every Flatten copy in a combat pile gets an absolute zero
    // EndOfTurn cost modifier.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    FlashKernel.SummonOsty(ref x, 8);
    battle.Energy = 3;
    battle.Phase = FlashPlayerPhase.Play;
    battle.Side = FlashSide.Player;
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, flatten));
    FlashPiles.Append(ref battle, FlashPile.Discard, FlashKernel.CreateCard(rules, flatten));
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, FlashKernel.EnemyOwner(0)), rules, out _);
    Check(battle.DiscardCount == 2, "Flatten 应打出并进入弃牌堆");
    int discardStart = FlashPiles.Start(battle, FlashPile.Discard);
    Check(battle.Cards[discardStart].EnergyModifierCount == 1
        && battle.Cards[discardStart + 1].EnergyModifierCount == 1,
        "Osty 攻击后每张 Flatten 应各获得一个零费修饰");

    // EtherealPlays and Oblivion: playing an Ethereal Soul counts the play and applies the Oblivion
    // amount as Doom to its owner.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, enemy, oblivion, 5, FlashLimits.PlayerOwner);
    x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, dieForYou, 1, FlashLimits.NoOwner);
    battle.Energy = 1;
    battle.Phase = FlashPlayerPhase.Play;
    battle.Side = FlashSide.Player;
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, parse));
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, FlashLimits.NoOwner), rules, out _);
    Check(battle.Counter(FlashCounter.EtherealPlays) == 1, "打出 Ethereal 牌应计入 EtherealPlays");
    Check(FlashKernel.GetAmount(battle, enemy, doom) == 5, "Oblivion 应在卡牌打出后施加等量 Doom");

    // UnceasingTop: playing the last hand card with an empty hand and a non-empty draw pile draws one.
    battle = NewBattle();
    battle.Phase = FlashPlayerPhase.Play;
    battle.Side = FlashSide.Player;
    battle.Energy = 1;
    battle.RelicCount = 1;
    battle.Relics[0] = new FlashRelic { Id = unceasingTop };
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, defend));
    FlashPiles.Append(ref battle, FlashPile.Draw, FlashKernel.CreateCard(rules, strike));
    FlashKernel.Step(ref battle, FlashAction.PlayCard(0, FlashLimits.NoOwner), rules, out _);
    Check(battle.HandCount == 1, "手牌打空后 UnceasingTop 应抽一张");
    Check(battle.Cards[0].Id == strike, "UnceasingTop 应抽到抽牌堆顶");
}

static FlashRules NbRules()
{
    FlashRulesBuilder builder = new() { BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first"], RootEnemyCount = 1 };
    FlashRegistry.TryCard("STRIKE_NECROBINDER", out ushort strike);
    FlashRegistry.TryCard("DEFEND_NECROBINDER", out ushort defend);
    FlashRegistry.TryCard("BODYGUARD", out ushort bodyguard);
    FlashRegistry.TryCard("SOUL", out ushort soul);
    FlashRegistry.TryCard("PARSE", out ushort parse);
    FlashRegistry.TryCard("FLATTEN", out ushort flatten);
    FlashRegistry.TryCard("HIGH_FIVE", out ushort highFive);
    FlashRegistry.TryCard("RIGHT_HAND_HAND", out ushort rightHandHand);
    builder.SetCard(strike, 0, NbStats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 1, [6m]));
    builder.SetCard(strike, 1, NbStats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 1, [9m]));
    builder.SetCard(defend, 0, NbStats(FlashCardType.Skill, FlashTargetType.Self, 1, [5m]));
    builder.SetCard(defend, 1, NbStats(FlashCardType.Skill, FlashTargetType.Self, 1, [8m]));
    builder.SetCard(bodyguard, 0, NbStats(FlashCardType.Skill, FlashTargetType.Self, 1, [5m]));
    builder.SetCard(soul, 0, NbStats(FlashCardType.Skill, FlashTargetType.Self, 0, [2m], keywords: FlashKeywords.Exhaust));
    builder.SetCard(parse, 0, NbStats(FlashCardType.Skill, FlashTargetType.Self, 1, [3m], keywords: FlashKeywords.Ethereal));
    builder.SetCard(flatten, 0, NbStats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 2, [12m]));
    builder.SetCard(flatten, 1, NbStats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 2, [15m]));
    builder.SetCard(highFive, 0, NbStats(FlashCardType.Attack, FlashTargetType.AllEnemies, 2, [11m, 2m]));
    builder.SetCard(rightHandHand, 0, NbStats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 0, [4m, 2m]));
    return builder.Build();

    static FlashCardStats NbStats(FlashCardType type, FlashTargetType target, int cost, decimal[] vars,
        FlashKeywords keywords = FlashKeywords.None) => new()
    {
        Type = type, Target = target, Rarity = FlashCardRarity.Basic, EnergyCost = cost, CostsX = false,
        StarCost = -1, StarCostsX = false, Keywords = keywords, MaxUpgradeLevel = 1, Vars = vars,
    };
}

static FlashBattle SliceBattle()
{
    FlashRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike);
    FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend);
    FlashRegistry.TryCard("BASH", out ushort bash);
    FlashRegistry.TryMonster("NIBBIT", out ushort nibbit);
    FlashBattle battle = new()
    {
        Round = 1, PlayerTurn = 1, Energy = 3, Side = FlashSide.Player, Phase = FlashPlayerPhase.Play,
        Flags = FlashBattleFlags.PlayerHooksActive, MaxHandSize = 10, EnemyCount = 1, RosterCount = 1,
        Player = new FlashCreature { CurrentHp = 80, MaxHp = 80, Flags = FlashCreatureFlags.Present },
    };
    battle.Enemies[0] = new FlashEnemy
    {
        Creature = new FlashCreature { CurrentHp = 44, MaxHp = 44, Flags = FlashCreatureFlags.Present },
        MonsterId = nibbit, CombatId = 1, Move = 1, ForcedMove = byte.MaxValue, Flags = FlashEnemyFlags.InCombat,
    };
    FlashKernel.LogMonsterMove(ref battle.Enemies[0], 1);
    battle.RngStream(FlashRngStream.Shuffle) = FlashRngState.FromSerializable(0, 1, 2, 3, 4);
    battle.RngStream(FlashRngStream.MonsterAi) = FlashRngState.FromSerializable(0, 5, 6, 7, 8);
    ushort[] deck = [strike, defend, defend, strike, bash, strike, defend, defend, strike, strike];
    for (int index = 0; index < deck.Length; index++)
    {
        FlashCard card = new() { Id = deck[index], BaseCost = (sbyte)(deck[index] == bash ? 2 : 1), BaseStarCost = -1, Flags = FlashCardFlags.DeckCard };
        FlashPiles.Append(ref battle, index < 5 ? FlashPile.Hand : FlashPile.Draw, card);
    }
    return battle;
}

// CX: the four previously missing enchantments. Imbued's turn-1 auto-play (and its explicit boundary
// when a captured card disappears), Slither's draw-time random cost, SlumberingEssence's BeforeFlush
// cost modifier and PerfectFit's ModifyShuffleOrder placement.
static void CheckCxKernel(FlashRules rules)
{
    Check(FlashRegistry.TryEnchantment("IMBUED", out ushort imbued), "缺少 IMBUED");
    Check(FlashRegistry.TryEnchantment("SLITHER", out ushort slither), "缺少 SLITHER");
    Check(FlashRegistry.TryEnchantment("SLUMBERING_ESSENCE", out ushort slumbering), "缺少 SLUMBERING_ESSENCE");
    Check(FlashRegistry.TryEnchantment("PERFECT_FIT", out ushort perfectFit), "缺少 PERFECT_FIT");
    Check(FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD");
    Check(FlashRegistry.TryCard("CX_TEST_IMBUED_REMOVER", out ushort remover), "缺少 CX_TEST_IMBUED_REMOVER");

    static FlashCard Enchanted(FlashRules rules, ushort card, ushort enchantment)
    {
        FlashCard value = FlashKernel.CreateCard(rules, card);
        value.EnchantmentId = (byte)(enchantment + 1);
        return value;
    }

    // Imbued: every enchanted card auto-plays on player turn 1, in AllCards order, without paying energy.
    FlashBattle imbuedBattle = NewBattle();
    imbuedBattle.PlayerTurn = 1;
    imbuedBattle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashPiles.Append(ref imbuedBattle, FlashPile.Hand, Enchanted(rules, defend, imbued));
    FlashPiles.Append(ref imbuedBattle, FlashPile.Hand, Enchanted(rules, defend, imbued));
    FlashExec imbuedExec = new(ref imbuedBattle, rules, default);
    FlashKernel.AutoPrePlay(ref imbuedExec);
    Check(imbuedBattle.HandCount == 0 && imbuedBattle.DiscardCount == 2,
        $"Imbued 应在第一回合自动打出两张牌，实际手牌 {imbuedBattle.HandCount} 弃牌 {imbuedBattle.DiscardCount}");
    Check(imbuedBattle.Energy == 3 && imbuedBattle.Counter(FlashCounter.CardsPlayedThisTurn) == 2,
        "Imbued 自动打出不应支付能量");
    Check(imbuedBattle.Counter(FlashCounter.EnergySpentThisTurn) == 0, "Imbued 自动打出不应记录能量消耗");

    // Turn 2 (and later) must not auto-play Imbued.
    imbuedBattle.PlayerTurn = 2;
    FlashPiles.Append(ref imbuedBattle, FlashPile.Hand, Enchanted(rules, defend, imbued));
    FlashKernel.AutoPrePlay(ref imbuedExec);
    Check(imbuedBattle.HandCount == 1, "Imbued 只应在第一回合自动打出");

    // Boundary: the remover plays first and takes the other captured card out of combat. The card count
    // no longer matches the tracked move, so the kernel must throw FlashUnsupportedException (not replay
    // a guessed card and not silently skip it).
    FlashBattle boundaryBattle = NewBattle();
    boundaryBattle.PlayerTurn = 1;
    boundaryBattle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashPiles.Append(ref boundaryBattle, FlashPile.Hand, Enchanted(rules, remover, imbued));
    FlashPiles.Append(ref boundaryBattle, FlashPile.Hand, Enchanted(rules, defend, imbued));
    FlashExec boundaryExec = new(ref boundaryBattle, rules, default);
    bool boundaryThrown = false;
    try
    {
        FlashKernel.AutoPrePlay(ref boundaryExec);
    }
    catch (FlashUnsupportedException)
    {
        boundaryThrown = true;
    }
    Check(boundaryThrown, "Imbued 捕获卡消失时应抛 FlashUnsupportedException");

    // Slither: drawing the card into hand spends one CombatEnergyCosts draw and sets an absolute
    // this-combat cost.
    FlashBattle slitherBattle = NewBattle();
    slitherBattle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashPiles.Append(ref slitherBattle, FlashPile.Draw, Enchanted(rules, defend, slither));
    FlashRngState expected = slitherBattle.RngStream(FlashRngStream.CombatEnergyCosts);
    int counterBefore = expected.Counter;
    int expectedCost = expected.NextInt(4);
    FlashExec slitherExec = new(ref slitherBattle, rules, default);
    FlashKernel.Draw(ref slitherExec, 1, fromHandDraw: false);
    Check(slitherBattle.HandCount == 1, "Slither 测试应抽到一张手牌");
    ref FlashCard drawn = ref slitherBattle.Cards[0];
    Check(drawn.EnchantmentId == (byte)(slither + 1), "抽到的应是 Slither 卡");
    Check(drawn.EnergyModifierCount == 1
        && drawn.EnergyModifiers[0].Type == FlashCostType.Absolute
        && drawn.EnergyModifiers[0].Expiration == FlashCostExpiration.EndOfCombat
        && drawn.EnergyModifiers[0].Amount == expectedCost,
        $"Slither 应写入绝对值 {expectedCost} 的本场费用修饰，实际 {drawn.EnergyModifierCount}");
    Check(slitherBattle.RngStream(FlashRngStream.CombatEnergyCosts).Counter == counterBefore + 1,
        "Slither 应只消耗一次 CombatEnergyCosts");

    // SlumberingEssence: BeforeFlush adds a WhenPlayed -1 relative modifier before the flush.
    FlashBattle slumberBattle = NewBattle();
    FlashPiles.Append(ref slumberBattle, FlashPile.Hand, Enchanted(rules, defend, slumbering));
    FlashExec slumberExec = new(ref slumberBattle, rules, default);
    FlashKernel.BeforeFlush(ref slumberExec);
    ref FlashCard slumberCard = ref slumberBattle.Cards[0];
    Check(slumberCard.EnergyModifierCount == 1
        && slumberCard.EnergyModifiers[0].Type == FlashCostType.Relative
        && slumberCard.EnergyModifiers[0].Expiration == FlashCostExpiration.WhenPlayed
        && slumberCard.EnergyModifiers[0].Amount == -1,
        "SlumberingEssence BeforeFlush 应写入 WhenPlayed -1 修饰");
    FlashKernel.BeforeFlush(ref slumberExec);
    Check(slumberCard.EnergyModifierCount == 2, "每次 BeforeFlush 都应追加一条 SlumberingEssence 修饰");

    // PerfectFit: after the non-initial shuffle the enchanted card is the top of the new draw pile.
    FlashBattle shuffleBattle = NewBattle();
    shuffleBattle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashCard perfect = Enchanted(rules, defend, perfectFit);
    FlashPiles.Append(ref shuffleBattle, FlashPile.Discard, Enchanted(rules, defend, imbued));
    FlashPiles.Append(ref shuffleBattle, FlashPile.Discard, perfect);
    FlashPiles.Append(ref shuffleBattle, FlashPile.Draw, Enchanted(rules, defend, slither));
    FlashExec shuffleExec = new(ref shuffleBattle, rules, default);
    FlashKernel.Shuffle(ref shuffleExec);
    Check(shuffleBattle.DrawCount == 3 && shuffleBattle.DiscardCount == 0, "洗牌后三张牌都应进入抽牌堆");
    Check(FlashFingerprint.Equal(shuffleBattle.Cards[shuffleBattle.HandCount], perfect),
        "PerfectFit 应把附魔卡放在洗牌后抽牌堆顶");
}

// SK: contracts for the Silent kernel capabilities. Discard pipeline + AfterCardDiscarded + Sly
// auto-play, the poison trigger, the dynamic Shiv target, the card-play pair snapshot and the
// next-turn draw/energy terms.
static void CheckSkKernel()
{
    FlashRules rules = SilentRules();
    Check(FlashRegistry.TryCard("KS_DISCARD_TARGET", out ushort targetCard), "缺少 KS_DISCARD_TARGET");
    Check(FlashRegistry.TryCard("KS_SLY_CARD", out ushort slyCard), "缺少 KS_SLY_CARD");
    Check(FlashRegistry.TryCard("SHIV", out ushort shiv), "缺少 SHIV");
    Check(FlashRegistry.TryPower("POISON_POWER", out ushort poison), "缺少 POISON_POWER");
    Check(FlashRegistry.TryPower("ACCELERANT_POWER", out ushort accelerant), "缺少 ACCELERANT_POWER");
    Check(FlashRegistry.TryPower("FAN_OF_KNIVES_POWER", out ushort fan), "缺少 FAN_OF_KNIVES_POWER");
    Check(FlashRegistry.TryPower("AFTERIMAGE_POWER", out ushort afterimage), "缺少 AFTERIMAGE_POWER");
    Check(FlashRegistry.TryPower("DRAW_CARDS_NEXT_TURN_POWER", out ushort drawNext), "缺少 DRAW_CARDS_NEXT_TURN_POWER");
    Check(FlashRegistry.TryPower("ENERGY_NEXT_TURN_POWER", out ushort energyNext), "缺少 ENERGY_NEXT_TURN_POWER");
    Check(FlashRegistry.TryRelic("KS_TEST_DISCARD_RELIC", out ushort discardRelic), "缺少 KS_TEST_DISCARD_RELIC");
    Check(FlashRegistry.TryRelic("KS_TEST_SLY_RELIC", out ushort slyRelic), "缺少 KS_TEST_SLY_RELIC");

    // Discard: the card moves, the counter grows and AfterCardDiscarded reaches the relic listener.
    FlashBattle battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    battle.RelicCount = 2;
    battle.Relics[0] = new FlashRelic { Id = discardRelic };
    battle.Relics[1] = new FlashRelic { Id = slyRelic };
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, targetCard));
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, slyCard));
    FlashExec x = new(ref battle, rules, default);
    KsTestLog.Clear();
    FlashKernel.Discard(ref x, FlashPile.Hand, 1);
    Check(battle.Counter(FlashCounter.CardsDiscardedThisTurn) == 1, "弃牌计数应为 1");
    Check(KsTestLog.Lines.Contains("discarded"), "AfterCardDiscarded 未派发");
    Check(KsTestLog.Lines.Contains("sly-play"), "Sly 弃牌未自动打出");

    // Discard-and-draw with a Sly card: the draw may shuffle the Sly card out of the discard pile;
    // the auto-play locates it anywhere in the battle instead of assuming the discard pile.
    battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    battle.RelicCount = 2;
    battle.Relics[0] = new FlashRelic { Id = discardRelic };
    battle.Relics[1] = new FlashRelic { Id = slyRelic };
    // The draw pile starts empty, so Draw shuffles the discard (containing the Sly card) into the
    // draw pile before the auto-play locates it.
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, targetCard));
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, slyCard));
    battle.RngStream(FlashRngStream.Shuffle) = FlashRngState.FromSerializable(0, 1, 2, 3, 4);
    x = new(ref battle, rules, default);
    KsTestLog.Clear();
    Span<int> selected = stackalloc int[2];
    selected[0] = 0;
    selected[1] = 1;
    FlashKernel.DiscardAndDraw(ref x, FlashPile.Hand, selected, 1);
    Check(battle.Counter(FlashCounter.CardsDiscardedThisTurn) == 2, "批量弃牌计数应为 2");
    Check(KsTestLog.Lines.Count(line => line == "discarded") == 2, "批量弃牌应派发两次 AfterCardDiscarded");
    Check(KsTestLog.Lines.Contains("sly-play"), "批量弃牌后 Sly 未自动打出");

    // An AfterCardDiscarded listener that moves the card out of every pile makes the Sly auto-play
    // target unrepresentable: the kernel must surface a FlashUnsupportedException, not guess a slot.
    battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    battle.RelicCount = 1;
    battle.Relics[0] = new FlashRelic { Id = discardRelic };
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, slyCard));
    x = new(ref battle, rules, default);
    KsTestLog.Clear();
    KsTestDiscardRelic.RemoveDiscardedCard = true;
    bool unsupported = false;
    try
    {
        FlashKernel.Discard(ref x, FlashPile.Hand, 0);
    }
    catch (FlashUnsupportedException)
    {
        unsupported = true;
    }
    finally
    {
        KsTestDiscardRelic.RemoveDiscardedCard = false;
    }
    Check(unsupported, "Sly 目标离开所有牌堆时应抛 FlashUnsupportedException");

    // Poison: min(amount, 1 + opponents' Accelerant) hits of the current amount, decrementing while
    // alive (CorePowerSupport.TriggerPoison).
    battle = NewBattle();
    x = new(ref battle, rules, default);
    byte enemy = FlashKernel.EnemyOwner(0);
    FlashKernel.ApplyPower(ref x, enemy, poison, 3, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    Span<byte> enemies = stackalloc byte[1];
    enemies[0] = enemy;
    FlashKernel.TriggerPoison(ref x, enemies);
    // TriggerCount = min(amount, 1 + opponents' Accelerant): no Accelerant means one hit.
    Check(battle.Enemies[0].Creature.CurrentHp == 97, $"毒应造成 3 点伤害，实际血量 {battle.Enemies[0].Creature.CurrentHp}");
    Check(FlashKernel.GetAmount(battle, enemy, poison) == 2, "毒应递减到 2");

    battle = NewBattle();
    x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, enemy, poison, 3, FlashLimits.PlayerOwner);
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, accelerant, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    FlashKernel.TriggerPoison(ref x, enemies);
    Check(battle.Enemies[0].Creature.CurrentHp == 95, $"加速毒应触发 2 次，实际血量 {battle.Enemies[0].Creature.CurrentHp}");
    Check(FlashKernel.GetAmount(battle, enemy, poison) == 1, "加速毒应剩余 1 层");

    // Shiv's dynamic target: all enemies only while the player holds FanOfKnivesPower.
    FlashCard shivCard = FlashKernel.CreateCard(rules, shiv);
    Check(FlashRegistry.Card(shiv).TargetType(battle, rules, shivCard) == FlashTargetType.AnyEnemy,
        "无飞刀扇时 Shiv 应单体");
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, fan, 1, FlashLimits.PlayerOwner);
    Check(FlashRegistry.Card(shiv).TargetType(battle, rules, shivCard) == FlashTargetType.AllEnemies,
        "飞刀扇在场时 Shiv 应打全体");

    // Card-play pair: BeforeCardPlayed snapshots the Afterimage amount for AfterCardPlayed.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, afterimage, 2, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    int afterimageIndex = FlashKernel.FindPower(battle, FlashLimits.PlayerOwner, afterimage);
    Check(afterimageIndex >= 0, "Afterimage 未施加");
    FlashListenerDef afterimageDef = FlashRegistry.Power(afterimage);
    FlashListener self = new(FlashListenerKind.Power, (byte)afterimageIndex, FlashLimits.PlayerOwner);
    FlashCardPlayInfo play = new(FlashKernel.CreateCard(rules, targetCard), FlashLimits.NoOwner, 0, 1, FlashPile.Discard);
    afterimageDef.BeforeCardPlayed(ref x, self, play);
    Check(x.TryTakeCardPlayPair(FlashCardPlayPairKind.AfterimageBlock, FlashLimits.PlayerOwner, afterimage,
        out int pairAmount) && pairAmount == 2, "Afterimage 快照金额错误");
    Check(!x.TryTakeCardPlayPair(FlashCardPlayPairKind.AfterimageBlock, FlashLimits.PlayerOwner, afterimage,
        out _), "Afterimage 快照只应消费一次");

    // Next-turn draw/energy: DrawCardsNextTurn adds only its snapshotted amount; EnergyNextTurn is
    // consumed by the turn-start reset.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, drawNext, 2, FlashLimits.PlayerOwner);
    int drawIndex = FlashKernel.FindPower(battle, FlashLimits.PlayerOwner, drawNext);
    battle.Powers[drawIndex].AmountOnTurnStart = 2;
    Check(FlashKernel.ModifiedHandDraw(ref x, 5) == 7, "下回合抽牌应加 2");
    battle.Powers[drawIndex].AmountOnTurnStart = 0;
    Check(FlashKernel.ModifiedHandDraw(ref x, 5) == 5, "未快照的下回合抽牌不应由监听者加");

    battle = NewBattle();
    x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, energyNext, 2, FlashLimits.PlayerOwner);
    Check(FlashKernel.ConsumeEnergyNextTurn(ref battle) == 2, "下回合能量应为 2");
    Check(FlashKernel.GetAmount(battle, FlashLimits.PlayerOwner, energyNext) == 0, "下回合能量应被消费");
}

static FlashRules SilentRules()
{
    FlashRulesBuilder builder = new() { BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first"], RootEnemyCount = 0 };
    FlashRegistry.TryCard("KS_DISCARD_TARGET", out ushort targetCard);
    FlashRegistry.TryCard("KS_SLY_CARD", out ushort slyCard);
    FlashRegistry.TryCard("SHIV", out ushort shiv);
    builder.SetCard(targetCard, 0, SilentStats(FlashCardType.Skill, FlashTargetType.Self, 0));
    builder.SetCard(slyCard, 0, SilentStats(FlashCardType.Skill, FlashTargetType.Self, 0, FlashKeywords.Sly));
    builder.SetCard(shiv, 0, SilentStats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 0));
    return builder.Build();

    static FlashCardStats SilentStats(FlashCardType type, FlashTargetType target, int cost,
        FlashKeywords keywords = FlashKeywords.None) => new()
    {
        Type = type, Target = target, Rarity = FlashCardRarity.Basic, EnergyCost = cost, CostsX = false,
        StarCost = -1, StarCostsX = false, Keywords = keywords, MaxUpgradeLevel = 1, Vars = [],
    };
}



// Beam-shaped workload: copy parent into a preallocated slot, step in place, hash. Reports the
// mean per transition; zero managed allocation is asserted on the measured loop.
static object Benchmark(FlashRules rules)
{
    const int width = 64;
    FlashBattle root = SliceBattle();
    FlashBattle[] parents = new FlashBattle[width];
    FlashBattle[] children = new FlashBattle[width * 16];
    FlashAction[] actions = new FlashAction[FlashKernel.MaxLegalActions];
    Random random = new(3);
    for (int index = 0; index < width; index++)
    {
        FlashBattle battle = root;
        int depth = random.Next(12);
        for (int step = 0; step < depth && FlashKernel.IsInProgress(battle); step++)
        {
            int count = FlashKernel.LegalActions(battle, rules, actions);
            FlashKernel.Step(ref battle, actions[random.Next(count)], rules, out _);
        }
        parents[index] = FlashKernel.IsInProgress(battle) ? battle : root;
    }

    long transitions = 0;
    ulong sink = 0;
    // mode 0: copy+step+hash, 1: copy only, 2: copy+hash, 3: copy+step
    void Round(int mode)
    {
        int slot = 0;
        for (int parent = 0; parent < width; parent++)
        {
            int count = FlashKernel.LegalActions(parents[parent], rules, actions);
            for (int action = 0; action < count && slot < children.Length; action++)
            {
                children[slot] = parents[parent];
                if (mode is 0 or 3)
                    FlashKernel.Step(ref children[slot], actions[action], rules, out _);
                if (mode is 0 or 2)
                    sink += FlashFingerprint.Hash(children[slot]);
                slot++;
                transitions++;
            }
        }
    }
    Dictionary<string, double> breakdown = [];
    long allocated = 0;
    foreach ((string name, int mode) in new[] { ("copyStepHash", 0), ("copyOnly", 1), ("copyHash", 2), ("copyStep", 3) })
    {
        for (int warm = 0; warm < 200; warm++)
            Round(mode);
        transitions = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch watch = Stopwatch.StartNew();
        for (int round = 0; round < 1000; round++)
            Round(mode);
        watch.Stop();
        if (mode == 0)
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        breakdown[name] = watch.Elapsed.TotalNanoseconds / transitions;
    }
    return new
    {
        transitionsPerMode = transitions,
        nanosecondsPerTransition = breakdown,
        allocatedBytes = allocated,
        sink,
    };
}

// Spawn contract: CombatId allocation, the native initial-HP distribution, encounter-slot roster
// insertion, per-side initial roll and the synthetic revive move. Mirrors
// SimulatedCombatState.CreatePredictedMonster/AddPredictedMonster/PreparePredictedMonster.
// KG1 contracts: generation-pool sampling order and the auto-play result-pile rules.
static void CheckGenerationAndAutoPlay()
{
    Check(FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD 定义");
    Check(FlashRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike), "缺少 STRIKE_IRONCLAD 定义");
    FlashRulesBuilder builder = new()
    {
        BaseMaxEnergy = 3,
        PlayerSlotIndex = 0,
        HasEncounter = true,
        EncounterSlots = ["a"],
        RootEnemyCount = 0,
    };
    builder.SetCard(defend, 0, new FlashCardStats
    {
        Type = FlashCardType.Skill, Target = FlashTargetType.Self, Rarity = FlashCardRarity.Basic,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false, Keywords = FlashKeywords.None,
        MaxUpgradeLevel = 1, Vars = [6m],
    });
    builder.SetCard(strike, 0, new FlashCardStats
    {
        Type = FlashCardType.Attack, Target = FlashTargetType.AnyEnemy, Rarity = FlashCardRarity.Basic,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false, Keywords = FlashKeywords.None,
        MaxUpgradeLevel = 1, Vars = [6m],
    });
    builder.SetCard(strike, 1, new FlashCardStats
    {
        Type = FlashCardType.Attack, Target = FlashTargetType.AnyAlly, Rarity = FlashCardRarity.Basic,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false, Keywords = FlashKeywords.None,
        MaxUpgradeLevel = 1, Vars = [6m],
    });
    builder.SetCardPool("test.pool", [strike, defend, strike, defend]);
    FlashRules rules = builder.Build();

    // Distinct selection mirrors TakeRandom/UnstableShuffle: n-1 Shuffle draws over the whole list,
    // then the first count entries of the shuffled order. With-replacement selection draws each card.
    FlashBattle battle = NewBattle();
    battle.RngStream(FlashRngStream.Shuffle) = FlashRngState.FromSerializable(1, 11, 22, 33, 44);
    Span<FlashCard> selected = stackalloc FlashCard[4];
    FlashRngState reference = battle.RngStream(FlashRngStream.Shuffle);
    int distinctCount = FlashKernel.SampleCardPool(ref battle, rules, "test.pool", 1, distinct: true,
        FlashRngStream.Shuffle, selected);
    Check(distinctCount == 1, "distinct 取样数量应为 1");
    Span<ushort> expected = stackalloc ushort[4];
    for (int index = 0; index < 4; index++)
        expected[index] = index % 2 == 0 ? strike : defend;
    for (int n = 4; n > 1;)
    {
        n--;
        int k = reference.NextInt(n + 1);
        (expected[k], expected[n]) = (expected[n], expected[k]);
    }
    Check(selected[0].Id == expected[0], "distinct 取样顺序与 UnstableShuffle 不一致");
    Check(battle.RngStream(FlashRngStream.Shuffle).Counter == 4, "distinct 取样应消耗 3 次 Shuffle");
    Check(battle.RngStream(FlashRngStream.Shuffle).Counter == reference.Counter,
        "distinct 取样参考计数器不一致");

    FlashBattle replacement = NewBattle();
    replacement.RngStream(FlashRngStream.Shuffle) = FlashRngState.FromSerializable(1, 11, 22, 33, 44);
    Span<FlashCard> repeated = stackalloc FlashCard[3];
    int repeatedCount = FlashKernel.SampleCardPool(ref replacement, rules, "test.pool", 3, distinct: false,
        FlashRngStream.Shuffle, repeated);
    FlashRngState replacementReference = FlashRngState.FromSerializable(1, 11, 22, 33, 44);
    Check(repeatedCount == 3, "有放回取样数量应为 3");
    for (int index = 0; index < 3; index++)
    {
        // The same NextInt draw indexes the frozen pool [strike, defend, strike, defend].
        int pick = replacementReference.NextInt(0, 4);
        Check(repeated[index].Id == (pick % 2 == 0 ? strike : defend), $"有放回第 {index} 张与 NextInt 不一致");
    }
    Check(replacement.RngStream(FlashRngStream.Shuffle).Counter == 4, "有放回取样应每张消耗一次 Shuffle");

    // Unplayable / no-target auto-play goes to the result pile without playing.
    FlashBattle unplayable = NewBattle();
    FlashCard blocked = FlashKernel.CreateCard(rules, defend);
    blocked.Keywords |= FlashKeywords.Unplayable;
    FlashPiles.Append(ref unplayable, FlashPile.Hand, blocked);
    FlashExec x = new(ref unplayable, rules, FlashAction.PlayCard(0, FlashLimits.NoOwner));
    Check(!FlashKernel.AutoPlay(ref x, FlashPile.Hand, 0), "不可打出的自动打出应返回 false");
    Check(unplayable.HandCount == 0 && unplayable.DiscardCount == 1, "不可打出的自动打出应进入弃牌堆");
    Check(!x.NeedsChoice, "不可打出的自动打出不应请求选择");

    FlashBattle targetless = NewBattle();
    FlashPiles.Append(ref targetless, FlashPile.Hand, FlashKernel.CreateCard(rules, strike, 1));
    FlashExec y = new(ref targetless, rules, FlashAction.PlayCard(0, FlashLimits.NoOwner));
    Check(!FlashKernel.AutoPlay(ref y, FlashPile.Hand, 0), "无目标的自动打出应返回 false");
    Check(targetless.HandCount == 0 && targetless.DiscardCount == 1, "无目标的自动打出应进入弃牌堆");

    // Exhaust-keyword and force-exhaust cards land in the exhaust pile; a played force-exhaust card
    // clears ExhaustOnNextPlay, while one moved without playing keeps it (native behavior).
    FlashBattle exhaustKeyword = NewBattle();
    FlashCard howl = FlashKernel.CreateCard(rules, defend);
    howl.Keywords |= FlashKeywords.Unplayable | FlashKeywords.Exhaust;
    FlashPiles.Append(ref exhaustKeyword, FlashPile.Hand, howl);
    FlashExec z = new(ref exhaustKeyword, rules, FlashAction.PlayCard(0, FlashLimits.NoOwner));
    _ = FlashKernel.AutoPlay(ref z, FlashPile.Hand, 0);
    Check(exhaustKeyword.ExhaustCount == 1 && exhaustKeyword.DiscardCount == 0, "消耗关键字的自动打出应进入消耗堆");

    FlashBattle played = NewBattle();
    FlashPiles.Append(ref played, FlashPile.Draw, FlashKernel.CreateCard(rules, defend));
    FlashExec w = new(ref played, rules, FlashAction.PlayCard(0, FlashLimits.NoOwner));
    FlashKernel.AutoPlayFromDrawPile(ref w, 1, forceExhaust: true);
    Check(played.ExhaustCount == 1 && played.DiscardCount == 0, "强制消耗的自动打出应进入消耗堆");
    Check(played.Player.Block == 6, "自动打出的防御应获得格挡");
    Check((FlashPiles.At(ref played, FlashPile.Exhaust, 0).Flags & FlashCardFlags.ExhaustOnNextPlay) == 0,
        "打出的强制消耗标记应被清除");

    FlashBattle notPlayed = NewBattle();
    FlashPiles.Append(ref notPlayed, FlashPile.Draw, FlashKernel.CreateCard(rules, strike, 1));
    FlashExec v = new(ref notPlayed, rules, FlashAction.PlayCard(0, FlashLimits.NoOwner));
    FlashKernel.AutoPlayFromDrawPile(ref v, 1, forceExhaust: true);
    Check(notPlayed.ExhaustCount == 1, "无目标的强制消耗自动打出应进入消耗堆");
    Check((FlashPiles.At(ref notPlayed, FlashPile.Exhaust, 0).Flags & FlashCardFlags.ExhaustOnNextPlay) != 0,
        "未打出的强制消耗标记应保留");
}

static FlashBattle NewBattle()
{
    FlashBattle battle = default;
    battle.Player = new FlashCreature
    {
        CurrentHp = 50,
        MaxHp = 50,
        Flags = FlashCreatureFlags.Present,
    };
    battle.MaxHandSize = 10;
    battle.Energy = 3;
    battle.PotionSlotCount = 0;
    // One alive primary enemy keeps IsEnding false; targetless tests use an AnyAlly card instead.
    battle.EnemyCount = 1;
    battle.RosterCount = 1;
    battle.Roster[0] = 0;
    battle.Enemies[0].Creature = new FlashCreature
    {
        CurrentHp = 100,
        MaxHp = 100,
        Flags = FlashCreatureFlags.Present,
    };
    battle.Enemies[0].Flags = FlashEnemyFlags.InCombat;
    return battle;
}

static void CheckMonsterSpawn(FlashRules rules)
{
    // HashSet<int> enumeration order is the ascending insertion order the native NextItem sees.
    for (int trial = 1; trial <= 40; trial++)
    {
        int minimum = 17, maximum = 21;
        HashSet<int> available = Enumerable.Range(minimum, maximum - minimum + 1).ToHashSet();
        List<int> excluded = [];
        for (int hp = minimum; hp <= maximum; hp++)
        {
            if ((hp + trial) % 4 == 0)
                excluded.Add(hp);
        }
        available.ExceptWith(excluded);
        List<int> ordered = [.. available];
        Check(ordered.SequenceEqual(ordered.OrderBy(value => value)),
            $"HashSet 枚举顺序应升序（trial={trial}）");
    }

    // Player-turn spawn: CombatId, HP draw, roster order and the immediate initial roll.
    FlashBattle battle = SpawnBattle();
    FlashRngState nicheBefore = battle.RngStream(FlashRngStream.Niche);
    int expectedHp = ExpectedHp(ref nicheBefore, 17, 21, [44]);
    FlashExec x = new(ref battle, rules, default);
    byte spawned = FlashKernel.SpawnMonster(ref x, FlashKernel.EnemyOwner(0), 1, slotIndex: 2, minion: false);
    Check(spawned == FlashKernel.EnemyOwner(1), "生成应返回新 owner");
    Check(battle.EnemyCount == 2 && battle.RosterCount == 2, "生成后已知敌人数与名单数应各加一");
    Check(battle.NextCombatId == 8 && battle.Enemies[1].CombatId == 7, "CombatId 应从 NextCombatId 分配并递增");
    Check(battle.Enemies[1].Creature.CurrentHp == expectedHp && battle.Enemies[1].Creature.MaxHp == expectedHp,
        $"生成生命应为排除现有 MaxHp 后的 Niche 取值（期望 {expectedHp}，实际 {battle.Enemies[1].Creature.CurrentHp}）");
    Check(SameRng(battle.RngStream(FlashRngStream.Niche), nicheBefore), "生成只应消耗一次 Niche 抽样");
    Check(battle.Enemies[1].Rules == 1 && battle.Enemies[1].Slot == 2, "生成敌人应记录规则编号与槽位下标");
    Check(battle.Roster[0] == 0 && battle.Roster[1] == 1, "新槽位在名单末尾时直接追加");
    Check(battle.Enemies[1].Move == 0 && battle.Enemies[1].LoggedMoves == 1
        && (battle.Enemies[1].Flags & FlashEnemyFlags.NeedsInitialRoll) == 0,
        "玩家回合内生成应立即掷出初始行动并写日志");
    Check(battle.Enemies[1].Ai[0] == 2 && battle.Enemies[1].Ai[1] == 0, "生成应写入编译的初始 AI 寄存器");

    // Stable slot insertion: a same-slot enemy goes after the existing one, before a later slot.
    FlashKernel.SpawnMonster(ref x, FlashKernel.EnemyOwner(0), 1, slotIndex: 1, minion: false);
    Check(battle.RosterCount == 3 && battle.Roster[0] == 0 && battle.Roster[1] == 2 && battle.Roster[2] == 1,
        "同槽位生成应稳定插在原槽位敌人之后");

    // A null slot name sorts as -1: before named slots, after earlier nulls.
    FlashBattle nullSlot = SpawnBattle();
    nullSlot.Enemies[1] = nullSlot.Enemies[0];
    nullSlot.Enemies[1].Slot = (byte)FlashKernel.NoSlot;
    nullSlot.Enemies[1].CombatId = 2;
    nullSlot.EnemyCount = 2;
    nullSlot.RosterCount = 2;
    nullSlot.Roster[0] = 1;
    nullSlot.Roster[1] = 0;
    x = new FlashExec(ref nullSlot, rules, default);
    FlashKernel.SpawnMonster(ref x, FlashKernel.EnemyOwner(0), 1, FlashKernel.NoSlot, minion: false);
    Check(nullSlot.RosterCount == 3 && nullSlot.Roster[0] == 1 && nullSlot.Roster[1] == 2 && nullSlot.Roster[2] == 0,
        "无槽名生成应排在有名字的槽位之前，并保持既有空槽顺序");

    // NextFreeSlot / LastFreeSlot: null without an encounter, "" when full, otherwise the first/last.
    Check(FlashKernel.NextFreeSlot(battle, rules) == "first" && FlashKernel.LastFreeSlot(battle, rules) == "first",
        "只剩 first 时两者都应返回 first");
    Check(FlashKernel.NextFreeSlot(battle, rules) == "first" && FlashKernel.LastFreeSlot(battle, rules) == "first",
        "槽位查询不应改变状态");
    FlashRules noEncounter = NoEncounterRules();
    Check(FlashKernel.NextFreeSlot(battle, noEncounter) == null && FlashKernel.LastFreeSlot(battle, noEncounter) == null,
        "无遭遇时槽位查询应返回 null");

    // Exhausted slots: NextFreeSlot returns "", LastFreeSlot returns null.
    FlashBattle full = SpawnBattle();
    full.RosterCount = 3;
    full.EnemyCount = 3;
    for (int index = 0; index < 3; index++)
    {
        full.Enemies[index] = full.Enemies[0];
        full.Enemies[index].Slot = (byte)index;
        full.Roster[index] = (byte)index;
    }
    Check(FlashKernel.NextFreeSlot(full, rules) == "" && FlashKernel.LastFreeSlot(full, rules) == null,
        "无空槽时 NextFreeSlot 应为空串、LastFreeSlot 应为 null");

    // HP exclusion follows the roster's current MaxHp, including earlier spawns; an exhausted range
    // falls back to NextInt(min, max + 1).
    battle = SpawnBattle();
    battle.Enemies[0].Creature.MaxHp = 17;
    x = new FlashExec(ref battle, rules, default);
    nicheBefore = battle.RngStream(FlashRngStream.Niche);
    expectedHp = ExpectedHp(ref nicheBefore, 17, 21, [17]);
    FlashKernel.SpawnMonster(ref x, FlashKernel.EnemyOwner(0), 1, slotIndex: 0, minion: false);
    Check(battle.Enemies[1].Creature.MaxHp == expectedHp, "生命排除应使用名单敌人的当前 MaxHp");
    battle.Enemies[0].Creature.MaxHp = 18;
    nicheBefore = battle.RngStream(FlashRngStream.Niche);
    expectedHp = ExpectedHp(ref nicheBefore, 17, 21, [18, battle.Enemies[1].Creature.MaxHp]);
    FlashKernel.SpawnMonster(ref x, FlashKernel.EnemyOwner(0), 1, slotIndex: 2, minion: false);
    Check(battle.Enemies[2].Creature.MaxHp == expectedHp, "生命排除应包含同回合先前生成的敌人");

    battle = SpawnBattle();
    for (int index = 0; index < 5; index++)
    {
        battle.Enemies[index] = battle.Enemies[0];
        battle.Enemies[index].Creature.MaxHp = 17 + index;
        battle.Enemies[index].CombatId = (uint)(index + 1);
        battle.Enemies[index].Slot = (byte)(index % 3);
        battle.Roster[index] = (byte)index;
    }
    battle.EnemyCount = 5;
    battle.RosterCount = 5;
    x = new FlashExec(ref battle, rules, default);
    nicheBefore = battle.RngStream(FlashRngStream.Niche);
    int expectedFallback = nicheBefore.NextInt(17, 22);
    FlashKernel.SpawnMonster(ref x, FlashKernel.EnemyOwner(0), 1, slotIndex: 0, minion: false);
    Check(battle.Enemies[5].Creature.MaxHp == expectedFallback,
        $"可取值耗尽时应使用 NextInt(min, max+1)（期望 {expectedFallback}）");

    // Enemy-turn spawn: pending initial roll until the end-of-enemy-turn pass.
    battle = SpawnBattle();
    battle.Side = FlashSide.Enemy;
    battle.Enemies[0].Flags |= FlashEnemyFlags.PerformedMoveThisRound;
    x = new FlashExec(ref battle, rules, default);
    FlashKernel.SpawnMonster(ref x, FlashKernel.EnemyOwner(0), 1, slotIndex: 2, minion: false);
    Check((battle.Enemies[1].Flags & FlashEnemyFlags.NeedsInitialRoll) != 0, "敌人回合内生成应保留待掷标记");
    Check(battle.Enemies[1].Move == 0 && battle.Enemies[1].LoggedMoves == 0, "待掷时行动节点应为占位行动且不写日志");
    FlashKernel.PrepareMonsterMovesForNextRound(ref x);
    Check((battle.Enemies[1].Flags & FlashEnemyFlags.NeedsInitialRoll) == 0
        && battle.Enemies[1].Move == 0 && battle.Enemies[1].LoggedMoves == 1,
        "敌人回合结束时应对待掷敌人掷出初始行动");

    // Illusion revive: forced synthetic node, dynamic follow-up, heal to max and advancement.
    battle = SpawnBattle();
    x = new FlashExec(ref battle, rules, default);
    byte rat = FlashKernel.EnemyOwner(0);
    FlashKernel.LogMonsterMove(ref battle.Enemies[0], 0);
    battle.Enemies[0].Creature.CurrentHp = 0;
    FlashKernel.BeginIllusionRevive(ref x, rat);
    Check(battle.Enemies[0].DeathPhase == FlashDeathPhase.Reviving
        && battle.Enemies[0].Move == 2 && battle.Enemies[0].ForcedMove == 0,
        "幻象复活应写复活节点与最近行动后继");
    Check(FlashKernel.CanPerformMonsterMove(battle.Enemies[0]), "复活中的敌人应可执行行动");
    FlashKernel.ExecuteMonsterMove(ref x, rat);
    Check(battle.Enemies[0].Creature.CurrentHp == 44 && battle.Enemies[0].DeathPhase == FlashDeathPhase.None,
        "REVIVE_MOVE 应回满生命并清除复活阶段");
    battle.Enemies[0].Flags |= FlashEnemyFlags.PerformedMoveThisRound;
    FlashKernel.PrepareMonsterMovesForNextRound(ref x);
    Check(battle.Enemies[0].Move == 0 && battle.Enemies[0].ForcedMove == byte.MaxValue,
        "复活行动执行后应推进到动态后继并清空 ForcedMove");
}

// A rules table with a root entry and one spawnable entry, both for NIBBIT, plus the synthetic
// revive node so the revive contract can force it.
static FlashRules SpawnRules()
{
    FlashRegistry.TryMonster("NIBBIT", out ushort nibbit);
    FlashRulesBuilder builder = new() { RootEnemyCount = 1, HasEncounter = true, EncounterSlots = ["first", "second", "third"] };
    builder.SetMonster(0, SpawnMonsterRules(nibbit));
    builder.AddSpawnableMonster(SpawnMonsterRules(nibbit));
    return builder.Build();
}

static FlashRules NoEncounterRules()
{
    FlashRegistry.TryMonster("NIBBIT", out ushort nibbit);
    FlashRulesBuilder builder = new();
    builder.SetMonster(0, SpawnMonsterRules(nibbit));
    return builder.Build();
}

static FlashMonsterRules SpawnMonsterRules(ushort nibbit) => new()
{
    MonsterId = nibbit,
    Nodes =
    [
        new FlashMonsterNode { NativeId = "BUTT_MOVE", Kind = FlashMonsterNodeKind.Move, FollowUp = 0, Attacks = [new(12, 1)] },
        new FlashMonsterNode { NativeId = "STUNNED", Kind = FlashMonsterNodeKind.Move, MustPerformOnceBeforeTransitioning = true },
        new FlashMonsterNode { NativeId = "REVIVE_MOVE", Kind = FlashMonsterNodeKind.Move, MustPerformOnceBeforeTransitioning = true },
    ],
    InitialNode = 0,
    PendingInitialMoveNode = 0,
    StunnedNode = 1,
    ReviveNode = 2,
    InitialHpMin = 17,
    InitialHpMax = 21,
    StaticInts = [],
    InitialAiRegisters = [2, 0],
};

static bool SameRng(in FlashRngState left, in FlashRngState right)
    => left.Counter == right.Counter && left.S0 == right.S0 && left.S1 == right.S1
        && left.S2 == right.S2 && left.S3 == right.S3;

static FlashBattle SpawnBattle()
{
    FlashRegistry.TryMonster("NIBBIT", out ushort nibbit);
    FlashBattle battle = new()
    {
        Round = 1, PlayerTurn = 1, Energy = 3, Side = FlashSide.Player, Phase = FlashPlayerPhase.Play,
        Flags = FlashBattleFlags.PlayerHooksActive, MaxHandSize = 10,
        EnemyCount = 1, RosterCount = 1, NextCombatId = 7,
        Player = new FlashCreature { CurrentHp = 80, MaxHp = 80, Flags = FlashCreatureFlags.Present },
    };
    battle.Enemies[0] = new FlashEnemy
    {
        Creature = new FlashCreature { CurrentHp = 44, MaxHp = 44, Flags = FlashCreatureFlags.Present },
        MonsterId = nibbit, Rules = 0, Slot = 1, CombatId = 1, Move = 0, ForcedMove = byte.MaxValue,
        Flags = FlashEnemyFlags.InCombat,
    };
    battle.Roster[0] = 0;
    battle.RngStream(FlashRngStream.Shuffle) = FlashRngState.FromSerializable(0, 1, 2, 3, 4);
    battle.RngStream(FlashRngStream.MonsterAi) = FlashRngState.FromSerializable(0, 5, 6, 7, 8);
    battle.RngStream(FlashRngStream.Niche) = FlashRngState.FromSerializable(0, 9, 10, 11, 12);
    return battle;
}

// Reference CreatePredictedMonster HP draw: the eligible values are the ascending HashSet
// enumeration order; an empty set uses NextInt(min, max + 1).
static int ExpectedHp(ref FlashRngState niche, int minimum, int maximum, IReadOnlyList<int> rosterMaxHp)
{
    HashSet<int> available = Enumerable.Range(minimum, maximum - minimum + 1).ToHashSet();
    available.ExceptWith(rosterMaxHp);
    if (available.Count == 0)
        return niche.NextInt(minimum, maximum + 1);
    int index = niche.NextInt(0, available.Count);
    return available.ElementAt(index);
}


// KG2: contracts for the new dispatch points. The helper definitions below are registered only in
// this pure-kernel executable; they log call order and prove gate, listener scope and multi-pass order.
static void CheckA3Kernel(FlashRules rules)
{
    Check(FlashRegistry.TryPower("A3_TEST_RAMPART_POWER", out ushort rampart), "缺少 A3 Rampart 测试 Power");
    Check(FlashRegistry.TryRelic("A3_TEST_GENERATED_RELIC", out ushort generatedRelic), "缺少 A3 生成遗物");
    Check(FlashRegistry.TryPower("A3_TEST_ENDING_POWER", out ushort ending), "缺少 A3 终局测试 Power");
    Check(FlashRegistry.TryPower("HEX_POWER", out ushort hex), "缺少 HEX_POWER");
    Check(FlashRegistry.TryAffliction("HEXED", out ushort hexed), "缺少 HEXED");
    Check(FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD");

    // PersistentPowerSupport.TriggerRampart: the player-side turn start calls the Rampart hook.
    FlashBattle battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashExec x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, rampart, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    Kg2TestLog.Clear();
    Span<byte> player = stackalloc byte[1];
    player[0] = FlashLimits.PlayerOwner;
    FlashKernel.TriggerSideTurnStart(ref x, FlashSide.Player, player, decrementPlating: false);
    Check(Kg2TestLog.Lines.Contains("rampart"), "A3 Rampart 派发未生效");

    // AfterCardGeneratedForCombat receives the absolute card position.
    FlashBattle generated = NewBattle();
    generated.Flags |= FlashBattleFlags.PlayerHooksActive;
    generated.RelicCount = 1;
    generated.Relics[0] = new FlashRelic { Id = generatedRelic };
    FlashExec g = new(ref generated, rules, default);
    FlashPiles.Append(ref generated, FlashPile.Hand, FlashKernel.CreateCard(rules,
        defend));
    Kg2TestLog.Clear();
    Check(FlashKernel.AddGeneratedCard(ref g,
        FlashKernel.CreateCard(rules, defend), FlashPile.Discard),
        "A3 生成卡加入失败");
    Check(Kg2TestLog.Lines.Contains("generated-ok"), "A3 AfterCardGeneratedForCombat 位置错误");

    // HexPower.TryModifyKeywordsInCombat gives a Hexed card Ethereal without storing it.
    FlashBattle keywords = NewBattle();
    FlashExec k = new(ref keywords, rules, default);
    FlashCard hexedCard = FlashKernel.CreateCard(rules, defend);
    hexedCard.AfflictionId = (byte)(hexed + 1);
    Check(!FlashKernel.HasKeyword(keywords, hexedCard, FlashKeywords.Ethereal), "A3 无 Hex 时不应有 Ethereal");
    FlashKernel.ApplyPower(ref k, FlashLimits.PlayerOwner, hex, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref k);
    Check(FlashKernel.HasKeyword(keywords, hexedCard, FlashKeywords.Ethereal), "A3 Hex 未授予 Ethereal");

    // ShouldStopCombatFromEnding: a positive flag keeps the ending check false.
    FlashBattle endingBattle = NewBattle();
    endingBattle.EnemyCount = 1;
    endingBattle.RosterCount = 1;
    endingBattle.Roster[0] = 0;
    ref FlashEnemy enemy = ref endingBattle.Enemies[0];
    enemy.Creature.Flags = FlashCreatureFlags.Present;
    enemy.Creature.CurrentHp = 0;
    enemy.Flags = FlashEnemyFlags.InCombat;
    endingBattle.PowerCount = 1;
    endingBattle.Powers[0] = new FlashPower { Id = ending, Owner = FlashLimits.FirstEnemyOwner, Amount = 1 };
    Check(!FlashKernel.IsEnding(endingBattle), "A3 PreventsCombatEnding 未阻止终局");
    endingBattle.Powers[0].Amount = 0;
    Check(FlashKernel.IsEnding(endingBattle), "A3 无阻止 Power 时终局应成立");
}

static void CheckKg2Hooks(FlashRules rules)
{
    Check(FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD 定义");
    Check(FlashRegistry.TryPower("KG2_TEST_COST_POWER", out ushort costPower), "缺少 KG2 费用 Power");
    Check(FlashRegistry.TryPower("KG2_TEST_PLAY_COUNT_POWER", out ushort countPower), "缺少 KG2 出牌次数 Power");
    Check(FlashRegistry.TryPower("KG2_TEST_RESULT_LOCATION_POWER", out ushort locationPower), "缺少 KG2 去向 Power");
    Check(FlashRegistry.TryPower("KG2_TEST_POWER_CHANGE_POWER", out ushort changePower), "缺少 KG2 Power 数量 Power");
    Check(FlashRegistry.TryPower("KG2_TEST_DAMAGE_SOURCE_POWER", out ushort damagePower), "缺少 KG2 伤害来源 Power");
    Check(FlashRegistry.TryRelic("KG2_TEST_COST_RELIC", out ushort costRelic), "缺少 KG2 费用遗物");
    Check(FlashRegistry.TryRelic("KG2_TEST_X_VALUE_RELIC", out ushort xRelic), "缺少 KG2 X 值遗物");
    Check(FlashRegistry.TryRelic("KG2_TEST_RESET_RELIC", out ushort resetRelic), "缺少 KG2 能量重置遗物");
    Check(FlashRegistry.TryRelic("KG2_TEST_RECEIVED_RELIC", out ushort receivedRelic), "缺少 KG2 Power 数量接收遗物");
    Check(FlashRegistry.TryRelic("KG2_TEST_SHUFFLE_RELIC", out ushort shuffleRelic), "缺少 KG2 洗牌遗物");
    Check(FlashRegistry.TryRelic("KG2_TEST_ENTERED_RELIC", out ushort enteredRelic), "缺少 KG2 进战斗遗物");
    Check(FlashRegistry.TryRelic("KG2_TEST_POWER_CHANGE_RELIC", out ushort changeRelic), "缺少 KG2 Power 数量遗物");
    Check(FlashRegistry.TryPower("VULNERABLE_POWER", out ushort VulnerableId), "缺少 VULNERABLE_POWER");
    Check(FlashRegistry.TryPower("STRENGTH_POWER", out ushort StrengthId), "缺少 STRENGTH_POWER");

    FlashBattle battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashCard card = FlashKernel.CreateCard(rules, defend);
    FlashPiles.Append(ref battle, FlashPile.Hand, card);
    battle.RelicCount = 6;
    battle.Relics[0] = new FlashRelic { Id = costRelic };
    battle.Relics[1] = new FlashRelic { Id = xRelic };
    battle.Relics[2] = new FlashRelic { Id = resetRelic };
    battle.Relics[3] = new FlashRelic { Id = receivedRelic };
    battle.Relics[4] = new FlashRelic { Id = shuffleRelic };
    battle.Relics[5] = new FlashRelic { Id = enteredRelic };
    FlashExec x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, costPower, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);

    // Normal pass order: power then relic; late pass: power. Multiplicative late makes the order visible.
    Kg2TestLog.Clear();
    int cost = FlashKernel.EnergyCostWithModifiers(battle, rules, card, FlashPile.Hand);
    Check(cost == 10, $"KG2 费用顺序应为 10，实际 {cost} log={string.Join(",", Kg2TestLog.Lines)}");
    Check(string.Join(",", Kg2TestLog.Lines) == "cost-normal-power,cost-normal-relic,cost-late-power",
        $"KG2 费用监听顺序错误：{string.Join(",", Kg2TestLog.Lines)}");
    battle.Outcome = FlashOutcome.Victory;
    int endedCost = FlashKernel.EnergyCostWithModifiers(battle, rules, card, FlashPile.Hand);
    Check(endedCost == 1 && Kg2TestLog.Lines.Count == 3, "KG2 费用 Hook 应在战斗结束时跳过");
    battle.Outcome = FlashOutcome.None;

    // Result location: the changed power is recorded and its after hook runs.
    Kg2TestLog.Clear();
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, locationPower, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    Span<FlashListener> locationModifiers = stackalloc FlashListener[FlashHooks.MaxListenerCount];
    FlashPile location = FlashHooks.ModifyCardPlayResultLocation(ref x, card, false, 0, FlashPile.Discard,
        FlashPilePosition.Bottom, locationModifiers, out int locationCount, out FlashPilePosition locationPosition);
    Check(location == FlashPile.Draw && locationPosition == FlashPilePosition.Top && locationCount == 1,
        "KG2 去向 Hook 未生效（含 Draw/Top 位置）");
    FlashHooks.AfterModifyingCardPlayResultLocation(ref x, card, location, locationModifiers[..locationCount]);
    Check(Kg2TestLog.Lines.Contains("location-after"), "KG2 去向 After Hook 未派发");

    // Play count: changed listener recorded, after only for changed listeners.
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, countPower, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    Kg2TestLog.Clear();
    Span<FlashListener> countModifiers = stackalloc FlashListener[FlashHooks.MaxListenerCount];
    int playCount = FlashHooks.ModifyCardPlayCount(ref x, card, FlashLimits.NoOwner, 1, countModifiers,
        out int countModifierCount);
    Check(playCount == 2 && countModifierCount == 1, "KG2 出牌次数 Hook 未生效");
    FlashHooks.AfterModifyingCardPlayCount(ref x, card, countModifiers[..countModifierCount]);
    Check(Kg2TestLog.Lines.Contains("count-after"), "KG2 出牌次数 After Hook 未派发");

    // AfterPowerAmountChanged is power-only; the relic override must not be visited.
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, changePower, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    // The relic hook is registered on a relic, not on a power and must not be visited by the
    // power-only AfterPowerAmountChanged dispatch.
    battle.RelicCount++;
    battle.Relics[battle.RelicCount - 1] = new FlashRelic { Id = changeRelic };
    Kg2TestLog.Clear();
    FlashKernel.ApplyPower(ref x, FlashLimits.FirstEnemyOwner, VulnerableId, 1,
        FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    Check(Kg2TestLog.Lines.Contains("power-changed"), "KG2 AfterPowerAmountChanged 未派发");
    Check(Kg2TestLog.Lines.Contains("power-changed-target"), "KG2 AfterPowerAmountChanged 未传目标");
    Check(!Kg2TestLog.Lines.Contains("power-changed-relic"), "KG2 AfterPowerAmountChanged 不应派发遗物");

    // AfterShuffle, ModifyXValue, ShouldPlayerResetEnergy, AfterCardEnteredCombat.
    Kg2TestLog.Clear();
    FlashPiles.Append(ref battle, FlashPile.Discard, FlashKernel.CreateCard(rules, defend));
    battle.DrawCount = 0;
    FlashKernel.Shuffle(ref x);
    Check(Kg2TestLog.Lines.Contains("shuffle"), "KG2 AfterShuffle 未派发");
    Check(FlashHooks.ModifyXValue(battle, rules, card, 5) == 6, "KG2 ModifyXValue 未生效");
    Check(!FlashHooks.ShouldPlayerResetEnergy(battle, rules, FlashLimits.PlayerOwner), "KG2 能量重置判定未生效");
    Kg2TestLog.Clear();
    Check(FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(rules, defend), FlashPile.Hand),
        "KG2 生成卡加入失败");
    Check(Kg2TestLog.Lines.Contains("entered"), "KG2 遗物 AfterCardEnteredCombat 未派发");

    // Received power amount: the doubling pass runs before the Artifact check and the after records it.
    Kg2TestLog.Clear();
    int applied = FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
        StrengthId, 2, FlashLimits.PlayerOwner);
    Check(applied == 4, $"KG2 接收 Power 数量应为 4，实际 {applied}");
    Check(Kg2TestLog.Lines.Contains("received-after"), "KG2 AfterModifyingPowerAmountReceived 未派发");

    // AfterDamageReceived carries the card source.
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, damagePower, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    Kg2TestLog.Clear();
    Span<byte> enemy = stackalloc byte[1];
    enemy[0] = FlashLimits.FirstEnemyOwner;
    FlashKernel.Damage(ref x, enemy, 1, FlashValueProps.Move, FlashLimits.PlayerOwner, true, card);
    Check(Kg2TestLog.Lines.Contains("damage-card"), $"KG2 AfterDamageReceived 缺少卡牌来源 log={string.Join(",", Kg2TestLog.Lines)} outcome={battle.Outcome} flags={battle.Flags} hooks={FlashRegistry.Power(damagePower).Hooks}");
    Kg2TestLog.Clear();
    FlashKernel.Damage(ref x, enemy, 1, FlashValueProps.Move, FlashLimits.PlayerOwner, false, default);
    Check(Kg2TestLog.Lines.Contains("damage-none"), $"KG2 AfterDamageReceived 无来源分支错误 log={string.Join(",", Kg2TestLog.Lines)}");
}

// KA: card afflictions/enchantments, their modification hooks and the enchantment relic.
static void CheckKaAttachments()
{
    FlashRules rules = KaRules();
    Check(FlashRegistry.TryAffliction("TAINTED", out ushort tainted), "缺少 TAINTED 定义");
    Check(FlashRegistry.TryAffliction("ENTANGLED", out ushort entangled), "缺少 ENTANGLED 定义");
    Check(FlashRegistry.TryAffliction("RINGING", out ushort ringing), "缺少 RINGING 定义");
    Check(FlashRegistry.TryAffliction("SMOG", out ushort smog), "缺少 SMOG 定义");
    Check(FlashRegistry.TryEnchantment("SHARP", out ushort sharp), "缺少 SHARP 定义");
    Check(FlashRegistry.TryEnchantment("GLAM", out _), "缺少 GLAM 定义");
    Check(FlashRegistry.TryEnchantment("SPIRAL", out _), "缺少 SPIRAL 定义");
    Check(FlashRegistry.TryPower("VITAL_SPARK_POWER", out ushort vitalSpark), "缺少 VITAL_SPARK_POWER");
    Check(FlashRegistry.TryPower("SMOGGY_POWER", out ushort smoggy), "缺少 SMOGGY_POWER");
    Check(FlashRegistry.TryPower("TANGLED_POWER", out ushort tangled), "缺少 TANGLED_POWER");
    Check(FlashRegistry.TryPower("RINGING_POWER", out ushort ringingPower), "缺少 RINGING_POWER");
    Check(FlashRegistry.TryPower("TAINTED_POWER", out ushort taintedPower), "缺少 TAINTED_POWER");
    Check(FlashRegistry.TryRelic("MYSTIC_LIGHTER", out ushort lighter), "缺少 MYSTIC_LIGHTER");

    Check(FlashRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike), "缺少 STRIKE_IRONCLAD");
    Check(FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD");
    FlashBattle battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, strike));
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, defend));
    FlashExec x = new(ref battle, rules, default);
    int attackPosition = 0;
    int skillPosition = 1;

    // CardCmd.Afflict: Tainted is Skill-only and stacks; Entangled is non-stackable.
    FlashKernel.ApplyAffliction(ref x, skillPosition, tainted, 2);
    Check(battle.Cards[skillPosition].AfflictionId == tainted + 1
        && battle.Cards[skillPosition].AfflictionAmount == 2, "Tainted 应施加到技能牌");
    FlashKernel.ApplyAffliction(ref x, skillPosition, tainted, 3);
    Check(battle.Cards[skillPosition].AfflictionAmount == 5, "可叠加苦难应累加层数");
    FlashKernel.ApplyAffliction(ref x, attackPosition, tainted, 1);
    Check(battle.Cards[attackPosition].AfflictionId == 0, "Tainted 不应施加到攻击牌");
    FlashKernel.ApplyAffliction(ref x, attackPosition, entangled, 1);
    FlashKernel.ApplyAffliction(ref x, attackPosition, entangled, 1);
    Check(battle.Cards[attackPosition].AfflictionId == entangled + 1
        && battle.Cards[attackPosition].AfflictionAmount == 1, "不可叠加苦难只应保留首次层数");
    FlashKernel.ClearAfflictions(ref battle, entangled);
    Check(battle.Cards[attackPosition].AfflictionId == 0, "ClearAfflictions 应清除同类苦难");

    // Vital Spark one-time entry affliction, amount-change rewrite and removal clearing.
    battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, defend));
    x = new(ref battle, rules, default);
    byte enemyOwner = FlashKernel.EnemyOwner(0);
    FlashKernel.ApplyPower(ref x, enemyOwner, vitalSpark, 2, enemyOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    FlashKernel.NormalizeCardAfflictions(ref x);
    Check(battle.Cards[0].AfflictionId == tainted + 1 && battle.Cards[0].AfflictionAmount == 2,
        "新进场技能牌应获得 Vital Spark 的 Tainted");
    FlashKernel.SetPowerAmount(ref battle, enemyOwner, vitalSpark, 3);
    FlashKernel.NormalizeCardAfflictions(ref x);
    Check(battle.Cards[0].AfflictionAmount == 3, "Vital Spark 数量变化应同步 Tainted 层数");
    FlashKernel.SetPowerAmount(ref battle, enemyOwner, vitalSpark, 0);
    FlashKernel.NormalizeCardAfflictions(ref x);
    Check(battle.Cards[0].AfflictionId == 0, "Life Spark 移除后应清除 Tainted");

    // SmoggyPower: a Skill played this side turn makes newly entered Skills unplayable.
    battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, defend));
    x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, smoggy, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    battle.SetCounter(FlashCounter.SkillPlayedThisSideTurn, 1);
    FlashKernel.NormalizeCardAfflictions(ref x);
    Check(battle.Cards[0].AfflictionId == smog + 1, "SmoggyPower 应给进场技能牌施加 Smog");
    Check(!FlashKernel.CanPlayCard(battle, rules, 0), "Smog 牌应不可打出");

    // TangledPower cost and RingingPower second-card restriction.
    battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, strike));
    x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, tangled, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    FlashKernel.ApplyAffliction(ref x, 0, entangled, 1);
    Check(FlashKernel.EnergyCostWithModifiers(battle, rules, battle.Cards[0], FlashPile.Hand) == 2,
        "TangledPower 应给 Entangled 牌加 1 费");
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, ringingPower, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    FlashKernel.ClearAfflictions(ref battle, entangled);
    FlashKernel.ApplyAffliction(ref x, 0, ringing, 1);
    battle.SetCounter(FlashCounter.CardPlaySeriesStartedThisTurn, 0);
    Check(FlashKernel.CanPlayCard(battle, rules, 0), "Ringing 牌应能作为本回合第一张打出");
    battle.SetCounter(FlashCounter.CardPlaySeriesStartedThisTurn, 1);
    Check(!FlashKernel.CanPlayCard(battle, rules, 0), "Ringing 牌不应作为本回合第二张打出");

    // TaintedPower adds to the player's powered attacks.
    battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, taintedPower, 2, FlashLimits.NoOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    decimal taintedDamage = FlashHooks.ModifyDamage(ref x, FlashLimits.PlayerOwner, enemyOwner, 6m,
        FlashValueProps.Move, hasCardSource: false, default);
    Check(taintedDamage == 8m, $"TaintedPower 应加 2 伤害，实际 {taintedDamage}");

    // Enchantment damage/block modifiers and play count.
    battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashPiles.Append(ref battle, FlashPile.Hand, FlashKernel.CreateCard(rules, strike));
    x = new(ref battle, rules, default);
    ref FlashCard enchanted = ref battle.Cards[0];
    enchanted.EnchantmentId = checked((byte)(sharp + 1));
    enchanted.EnchantmentAmount = 3;
    decimal sharpDamage = FlashHooks.ModifyDamage(ref x, enemyOwner, FlashLimits.PlayerOwner, 6m,
        FlashValueProps.Move, hasCardSource: true, enchanted);
    Check(sharpDamage == 9m, $"Sharp 应加 3 伤害，实际 {sharpDamage}");
    battle.RelicCount = 1;
    battle.Relics[0] = new FlashRelic { Id = lighter };
    decimal lighterDamage = FlashHooks.ModifyDamage(ref x, enemyOwner, FlashLimits.PlayerOwner, 6m,
        FlashValueProps.Move, hasCardSource: true, enchanted);
    Check(lighterDamage == 18m, $"MysticLighter 应给附魔攻击加 9 伤害，实际 {lighterDamage}");

    FlashCard glam = FlashKernel.CreateCard(rules, strike);
    glam.EnchantmentId = checked((byte)(FlashRegistry.EnchantmentId<Glam>() + 1));
    Check(FlashKernel.EnchantedReplayCount(glam) == 1, "Glam 首次应加 1 次重放");
    glam.EnchantmentPrivate = 1;
    Check(FlashKernel.EnchantedReplayCount(glam) == 0, "Glam 已用后不应再加重放");
    FlashCard spiral = FlashKernel.CreateCard(rules, strike);
    spiral.EnchantmentId = checked((byte)(FlashRegistry.EnchantmentId<Spiral>() + 1));
    Check(FlashKernel.EnchantedReplayCount(spiral) == 1, "Spiral 应加 1 次重放");
}

static FlashRules KaRules()
{
    FlashRulesBuilder builder = new() { BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["a"], RootEnemyCount = 0 };
    FlashRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike);
    FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend);
    FlashRegistry.TryRelic("MYSTIC_LIGHTER", out ushort lighter);
    builder.SetCard(strike, 0, new FlashCardStats
    {
        Type = FlashCardType.Attack, Target = FlashTargetType.AnyEnemy, Rarity = FlashCardRarity.Basic,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false, Keywords = FlashKeywords.None,
        MaxUpgradeLevel = 1, Vars = [6m],
    });
    builder.SetCard(defend, 0, new FlashCardStats
    {
        Type = FlashCardType.Skill, Target = FlashTargetType.Self, Rarity = FlashCardRarity.Basic,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false, Keywords = FlashKeywords.None,
        MaxUpgradeLevel = 1, Vars = [5m],
    });
    builder.SetRelicVars(lighter, [9m]);
    return builder.Build();
}

// RG: star and forge kernel contracts. The helper definitions below are registered only in this
// pure-kernel executable; the star relic/power log the dispatch order and the paid amount.
static void CheckRgKernel(FlashRules rules)
{
    Check(FlashRegistry.TryCard("SOVEREIGN_BLADE", out ushort blade), "缺少 SOVEREIGN_BLADE 定义");
    Check(FlashRegistry.TryCard("RG_TEST_STAR_CARD", out ushort starCard), "缺少 RG_TEST_STAR_CARD 定义");
    Check(FlashRegistry.TryPower("RG_TEST_STAR_POWER", out ushort starPower), "缺少 RG_TEST_STAR_POWER");
    Check(FlashRegistry.TryRelic("RG_TEST_STAR_RELIC", out ushort starRelic), "缺少 RG_TEST_STAR_RELIC");

    // Star cost: the hook only sees non-negative costs, runs in listener order and is gated by combat
    // ending; the temporary cost replaces the base; X star cost returns every current star without
    // dispatching the hook.
    FlashBattle battle = NewBattle();
    battle.Flags |= FlashBattleFlags.PlayerHooksActive;
    battle.RelicCount = 1;
    battle.Relics[0] = new FlashRelic { Id = starRelic };
    FlashCard card = FlashKernel.CreateCard(rules, starCard);
    Kg2TestLog.Clear();
    Check(FlashKernel.StarCostWithModifiers(battle, rules, card, FlashPile.Hand) == 0,
        "RG 星能费用 Hook 未生效（应把 2 降到 -2 后钳制到 0）");
    Check(Kg2TestLog.Lines.Count == 1 && Kg2TestLog.Lines[0] == "star-cost:2",
        $"RG 星能费用 Hook 应只收到一次 2，实际 {string.Join(",", Kg2TestLog.Lines)}");
    card.SetTemporaryStarCost(5, clearsAtTurnEnd: true, clearsWhenPlayed: true);
    Kg2TestLog.Clear();
    Check(FlashKernel.StarCostWithModifiers(battle, rules, card, FlashPile.Hand) == 1
        && Kg2TestLog.Lines.Count == 1 && Kg2TestLog.Lines[0] == "star-cost:5",
        "RG 临时星能费用应替换基础费用");
    card.Flags |= FlashCardFlags.StarCostsX;
    battle.Stars = 3;
    Kg2TestLog.Clear();
    Check(FlashKernel.StarCostWithModifiers(battle, rules, card, FlashPile.Hand) == 3
        && Kg2TestLog.Lines.Count == 0, "RG X 星能费用应返回当前星能且不派发 Hook");
    card.Flags &= ~FlashCardFlags.StarCostsX;
    battle.Outcome = FlashOutcome.Victory;
    Kg2TestLog.Clear();
    Check(FlashKernel.StarCostWithModifiers(battle, rules, card, FlashPile.Hand) == 5
        && Kg2TestLog.Lines.Count == 0, "RG 战斗结束时星能费用 Hook 应跳过");
    battle.Outcome = FlashOutcome.None;
    card.ClearTemporaryStarCost();
    card.BaseStarCost = -1;
    Kg2TestLog.Clear();
    Check(FlashKernel.StarCostWithModifiers(battle, rules, card, FlashPile.Hand) == 0
        && Kg2TestLog.Lines.Count == 0, "RG 负星能费用不应派发 Hook");

    // AfterStarsSpent: the existing engine runs the relics before the powers, not in listener order.
    FlashExec x = new(ref battle, rules, default);
    FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, starPower, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref x);
    Kg2TestLog.Clear();
    FlashHooks.AfterStarsSpent(ref x, 4);
    Check(string.Join(",", Kg2TestLog.Lines) == "stars-spent-relic:4,stars-spent-power:4",
        $"RG 星能消耗顺序错误：{string.Join(",", Kg2TestLog.Lines)}");

    // GainStars: clamp, then AfterStarsGained; skipped while the combat is ending.
    battle.Stars = FlashLimits.ValueClamp - 2;
    Kg2TestLog.Clear();
    Check(FlashKernel.GainStars(ref x, 5), "RG 星能获得不应产生待处理选择");
    Check(battle.Stars == FlashLimits.ValueClamp, "RG 星能获得应钳制到上限");
    Check(Kg2TestLog.Lines.Contains("stars-gained:5"), "RG 星能获得 Hook 未派发");
    battle.Stars = 0;
    battle.Enemies[0].Creature.CurrentHp = 0;
    Check(FlashKernel.IsEnding(battle), "RG 终局测试前提不成立");
    Kg2TestLog.Clear();
    Check(FlashKernel.GainStars(ref x, 5) && battle.Stars == 0 && Kg2TestLog.Lines.Count == 0,
        "RG 战斗结束时星能获得应跳过");
    battle.Enemies[0].Creature.CurrentHp = 100;

    // Star payment: ManualPlay deducts the star cost once and dispatches AfterStarsSpent with the paid
    // amount (K0 contract for the "the count changed by exactly the paid amount" assumption). No
    // cost-modifying relic here, so the base cost 2 is paid.
    FlashBattle paid = NewBattle();
    paid.Flags |= FlashBattleFlags.PlayerHooksActive;
    FlashPiles.Append(ref paid, FlashPile.Hand, FlashKernel.CreateCard(rules, starCard));
    FlashExec pay = new(ref paid, rules, default);
    FlashKernel.ApplyPower(ref pay, FlashLimits.PlayerOwner, starPower, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref pay);
    paid.Stars = 5;
    Kg2TestLog.Clear();
    FlashKernel.ManualPlay(ref pay, 0, FlashLimits.NoOwner);
    Check(paid.Stars == 3, $"RG 出牌应恰好支付 2 星能，实际星能 {paid.Stars}");
    Check(string.Join(",", Kg2TestLog.Lines) == "stars-spent-power:2",
        $"RG 出牌应只派发一次金额 2 的星能消耗 Hook：{string.Join(",", Kg2TestLog.Lines)}");

    // Forge: grows every Sovereign Blade in every pile; creates one canonical blade only when no
    // unexhausted blade exists; the freshly created blade receives the same forge amount.
    FlashBattle forgeBattle = NewBattle();
    FlashExec forge = new(ref forgeBattle, rules, default);
    FlashCard bladeValue = FlashKernel.CreateCard(rules, blade);
    FlashPiles.Append(ref forgeBattle, FlashPile.Hand, bladeValue);
    FlashPiles.Append(ref forgeBattle, FlashPile.Discard, bladeValue);
    Check(FlashKernel.Forge(ref forge, 3), "RG 锻造失败");
    Check(forgeBattle.CardCount == 2, "RG 已有未消耗君王之刃时不应再生成");
    Check(forgeBattle.Cards[0].Var0 == 3 && forgeBattle.Cards[1].Var0 == 3,
        "RG 锻造应增加所有君王之刃的伤害变量");

    FlashBattle exhaustedOnly = NewBattle();
    FlashExec forge2 = new(ref exhaustedOnly, rules, default);
    FlashPiles.Append(ref exhaustedOnly, FlashPile.Exhaust, bladeValue);
    Check(FlashKernel.Forge(ref forge2, 4), "RG 消耗堆君王之刃的锻造失败");
    Check(exhaustedOnly.HandCount == 1 && exhaustedOnly.ExhaustCount == 1,
        "RG 只有消耗堆君王之刃时应生成一张新牌");
    Check(exhaustedOnly.Cards[0].Var0 == 4 && exhaustedOnly.Cards[1].Var0 == 4,
        "RG 新生成的君王之刃也应获得本次锻造量");

    // StarsGainedThisTurn: positive gains accumulate (Radiate's CalculatedHits), non-positive gains do
    // not, and the player side turn start resets it (BeginSideTurn's ResetTurnCounter).
    FlashBattle turn = SliceBattle();
    FlashExec turnExec = new(ref turn, rules, default);
    turn.Stars = 10;
    Check(FlashKernel.GainStars(ref turnExec, 4), "RG 星能获得不应产生待处理选择");
    Check(turn.Counter(FlashCounter.StarsGainedThisTurn) == 4,
        $"RG 本回合获得星能应为 4，实际 {turn.Counter(FlashCounter.StarsGainedThisTurn)}");
    Check(FlashKernel.GainStars(ref turnExec, -2), "RG 负星能获得不应产生待处理选择");
    Check(turn.Counter(FlashCounter.StarsGainedThisTurn) == 4,
        "RG 负星能获得不应计入本回合获得星能");
    FlashKernel.Step(ref turn, FlashAction.EndTurn(), rules, out _);
    Check(turn.Counter(FlashCounter.StarsGainedThisTurn) == 0,
        $"RG 玩家回合开始应清零本回合获得星能，实际 {turn.Counter(FlashCounter.StarsGainedThisTurn)}");

    // The blade targets every enemy while the owner holds SeekingEdgePower.
    Check(FlashRegistry.TryPower("SEEKING_EDGE_POWER", out ushort seekingEdge), "缺少 SEEKING_EDGE_POWER");
    FlashBattle targetBattle = NewBattle();
    FlashExec target = new(ref targetBattle, rules, default);
    FlashCard bladeCard = FlashKernel.CreateCard(rules, blade);
    Check(FlashRegistry.Card(blade).TargetType(targetBattle, rules, bladeCard) == FlashTargetType.AnyEnemy,
        "RG 君王之刃默认目标应为单体");
    FlashKernel.ApplyPower(ref target, FlashLimits.PlayerOwner, seekingEdge, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref target);
    Check(FlashRegistry.Card(blade).TargetType(targetBattle, rules, bladeCard) == FlashTargetType.AllEnemies,
        "RG 寻求锋芒下君王之刃目标应为全体");
}


// LR: new kernel capabilities. Turn-start choices, the fixed transform, the generated-card and
// per-target powered-hit counters, and VoidForm's free-play window / forced turn end.
static void CheckLrKernel(FlashRules rules)
{
    Check(FlashRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike), "缺少 STRIKE_IRONCLAD");
    Check(FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD");
    Check(FlashRegistry.TryCard("SOVEREIGN_BLADE", out ushort blade), "缺少 SOVEREIGN_BLADE");
    Check(FlashRegistry.TryCard("RG_TEST_STAR_CARD", out ushort starCard), "缺少 RG_TEST_STAR_CARD");
    Check(FlashRegistry.TryCard("MINION_STRIKE", out ushort minionStrike), "缺少 MINION_STRIKE");
    Check(FlashRegistry.TryPower("VOID_FORM_POWER", out ushort voidForm), "缺少 VOID_FORM_POWER");
    Check(FlashRegistry.TryPower("TYRANNY_POWER", out ushort tyranny), "缺少 TYRANNY_POWER");
    Check(FlashRegistry.TryPower("FOREGONE_CONCLUSION_POWER", out ushort foregone), "缺少 FOREGONE_CONCLUSION_POWER");

    // Generated cards: AddGeneratedCard records History.CardGenerated before insertion, so a card
    // refused while the combat is ending still counts (HeirloomHammer clones on a killing blow).
    FlashBattle generated = NewBattle();
    FlashExec generate = new(ref generated, rules, default);
    Check(FlashKernel.AddGeneratedCard(ref generate, FlashKernel.CreateCard(rules, defend), FlashPile.Hand),
        "LR 生成卡应加入手牌");
    Check(generated.Counter(FlashCounter.CardsGeneratedThisCombat) == 1,
        "LR 生成卡应计数 1");
    generated.Enemies[0].Creature.CurrentHp = 0;
    Check(FlashKernel.IsEnding(generated), "LR 终局测试前提不成立");
    int handBefore = generated.HandCount;
    Check(FlashKernel.AddGeneratedCard(ref generate, FlashKernel.CreateCard(rules, defend), FlashPile.Hand),
        "LR 战斗结束时生成卡按现有引擎继续执行而不中止");
    Check(generated.HandCount == handBefore, "LR 战斗结束时生成卡不应加入牌堆");
    Check(generated.Counter(FlashCounter.CardsGeneratedThisCombat) == 2,
        "LR 战斗结束时被拒绝的生成卡仍应计入生成数");

    // The fixed transform replaces each selected card in place and keeps equal copies distinct by
    // occurrence (CardChoiceResolution.TransformCards).
    FlashBattle transformed = NewBattle();
    FlashExec transform = new(ref transformed, rules, default);
    FlashCard defendA = FlashKernel.CreateCard(rules, defend);
    defendA.Flags |= FlashCardFlags.PowerAfflictionChecked;
    FlashCard strikeA = FlashKernel.CreateCard(rules, strike);
    strikeA.Flags |= FlashCardFlags.PowerAfflictionChecked;
    FlashCard defendB = FlashKernel.CreateCard(rules, defend);
    defendB.Flags |= FlashCardFlags.PowerAfflictionChecked;
    FlashPiles.Append(ref transformed, FlashPile.Hand, defendA);
    FlashPiles.Append(ref transformed, FlashPile.Hand, strikeA);
    FlashPiles.Append(ref transformed, FlashPile.Hand, defendB);
    Span<int> selection = stackalloc int[2];
    selection[0] = 0;
    selection[1] = 2;
    FlashKernel.TransformSelectedTo(ref transform, FlashPile.Hand, selection, minionStrike, upgrade: true);
    Check(transformed.HandCount == 3, "LR 变换不应改变手牌数");
    Check(transformed.Cards[0].Id == minionStrike && transformed.Cards[0].Upgrade == 1
        && transformed.Cards[2].Id == minionStrike && transformed.Cards[2].Upgrade == 1,
        "LR 变换应原位替换两张防御并升到 1 级");
    Check(transformed.Cards[1].Id == strike, "LR 未选中的卡不应改变");
    Check(transformed.Counter(FlashCounter.CardsGeneratedThisCombat) == 2,
        "LR 每次固定变换应计入一次生成");

    // Powered attack hits: recorded for the player's powered attacks even when fully blocked,
    // ignored for unpowered damage, and an overflowing byte is an explicit boundary.
    FlashBattle hits = NewBattle();
    FlashExec hit = new(ref hits, rules, default);
    Span<byte> target = stackalloc byte[1];
    target[0] = FlashKernel.EnemyOwner(0);
    FlashKernel.Damage(ref hit, target, 6m, FlashValueProps.Move, FlashLimits.PlayerOwner, false, default);
    Check(FlashKernel.PoweredAttackHitsByPlayer(hits, target[0]) == 1, "LR 强化攻击命中应计数");
    hits.Enemies[0].Creature.Block = 100;
    Check(FlashKernel.Damage(ref hit, target, 6m, FlashValueProps.Move, FlashLimits.PlayerOwner, false, default),
        "LR 被完全格挡的强化攻击应报告完全格挡");
    Check(FlashKernel.PoweredAttackHitsByPlayer(hits, target[0]) == 2,
        "LR 被完全格挡的强化攻击仍应计数");
    FlashKernel.Damage(ref hit, target, 6m,
        FlashValueProps.Move | FlashValueProps.Unpowered, FlashLimits.PlayerOwner, false, default);
    Check(FlashKernel.PoweredAttackHitsByPlayer(hits, target[0]) == 2,
        "LR 非强化伤害不应计数");
    hits.Enemies[0].PoweredAttackHitsThisTurn = byte.MaxValue;
    bool overflowed = false;
    try
    {
        FlashKernel.Damage(ref hit, target, 1m, FlashValueProps.Move, FlashLimits.PlayerOwner, false, default);
    }
    catch (FlashUnsupportedException)
    {
        overflowed = true;
    }
    Check(overflowed, "LR 强化攻击命中数溢出应抛 FlashUnsupportedException");
    FlashBattle reset = SliceBattle();
    reset.Enemies[0].PoweredAttackHitsThisTurn = 3;
    FlashKernel.Step(ref reset, FlashAction.EndTurn(), rules, out _);
    Check(reset.Enemies[0].PoweredAttackHitsThisTurn == 0,
        "LR 玩家回合开始应清零强化攻击命中数");

    // Action choice capacity: WithChoice refuses the 33rd selection explicitly.
    FlashAction choices = FlashAction.EndTurn();
    for (int index = 0; index < FlashLimits.MaxChoices; index++)
        choices = choices.WithChoice(1UL << (index & 63));
    bool capacity = false;
    try
    {
        choices.WithChoice(1);
    }
    catch (FlashCapacityException)
    {
        capacity = true;
    }
    Check(capacity, "LR 动作选择超出容量应抛 FlashCapacityException");

    // VoidForm's free-play window: the late energy/star hooks zero the cost while fewer than Amount
    // non-auto last-in-series plays happened this turn; the counter resets at the side turn start.
    FlashBattle voidBattle = SliceBattle();
    FlashExec voidExec = new(ref voidBattle, rules, default);
    FlashKernel.ApplyPower(ref voidExec, FlashLimits.PlayerOwner, voidForm, 2, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref voidExec);
    int voidIndex = FlashKernel.FindPower(voidBattle, FlashLimits.PlayerOwner, voidForm);
    Check(voidIndex >= 0, "LR VoidFormPower 未施加");
    FlashListener voidSelf = new(FlashListenerKind.Power, checked((byte)voidIndex), FlashLimits.PlayerOwner);
    FlashPowerDef voidDef = FlashRegistry.Power(voidForm);
    FlashCard strikeCard = FlashKernel.CreateCard(rules, strike);
    Check(voidDef.TryModifyEnergyCostInCombatLate(voidBattle, rules, voidSelf, strikeCard, FlashPile.Hand, 3m) == 0m,
        "LR VoidForm 首次出牌的能量费用应为 0");
    Check(voidDef.TryModifyStarCost(voidBattle, rules, voidSelf,
            FlashKernel.CreateCard(rules, starCard), FlashPile.Hand, 2m) == 0m,
        "LR VoidForm 首次出牌的星能费用应为 0");
    Check(voidDef.TryModifyEnergyCostInCombatLate(voidBattle, rules, voidSelf, strikeCard, FlashPile.Draw, 3m) == 3m,
        "LR VoidForm 只应影响手牌与打出区");
    FlashExec voidPlay = new(ref voidBattle, rules, default);
    voidDef.AfterCardPlayed(ref voidPlay, voidSelf,
        new FlashCardPlayInfo(strikeCard, FlashLimits.NoOwner, 0, 1, FlashPile.Discard));
    Check(voidBattle.Powers[voidIndex].Aux0 == 1, "LR VoidForm 非自动最后一段出牌应计数");
    voidPlay.CurrentPlayIsAutoPlay = true;
    voidDef.AfterCardPlayed(ref voidPlay, voidSelf,
        new FlashCardPlayInfo(strikeCard, FlashLimits.NoOwner, 0, 1, FlashPile.Discard));
    Check(voidBattle.Powers[voidIndex].Aux0 == 1, "LR VoidForm 自动打出不应计数");
    voidPlay.CurrentPlayIsAutoPlay = false;
    voidDef.AfterCardPlayed(ref voidPlay, voidSelf,
        new FlashCardPlayInfo(strikeCard, FlashLimits.NoOwner, 0, 2, FlashPile.Discard));
    Check(voidBattle.Powers[voidIndex].Aux0 == 1, "LR VoidForm 非最后一段出牌不应计数");
    Check(voidDef.TryModifyEnergyCostInCombatLate(voidBattle, rules, voidSelf, strikeCard, FlashPile.Hand, 3m) == 0m,
        "LR VoidForm 第二次出牌仍在免费窗口内");
    voidPlay.CurrentPlayIsAutoPlay = false;
    voidDef.AfterCardPlayed(ref voidPlay, voidSelf,
        new FlashCardPlayInfo(strikeCard, FlashLimits.NoOwner, 1, 2, FlashPile.Discard));
    Check(voidBattle.Powers[voidIndex].Aux0 == 2, "LR VoidForm 最后一段出牌应计数");
    Check(voidDef.TryModifyEnergyCostInCombatLate(voidBattle, rules, voidSelf, strikeCard, FlashPile.Hand, 3m) == 3m,
        "LR VoidForm 免费窗口用尽后应恢复费用");
    voidDef.BeforeSideTurnStart(ref voidPlay, voidSelf, participates: true);
    Check(voidBattle.Powers[voidIndex].Aux0 == 0, "LR VoidForm 侧回合开始应清零出牌计数");
    FlashBattle request = SliceBattle();
    FlashKernel.RequestPlayerTurnEnd(ref request);
    Check((request.Flags & FlashBattleFlags.PlayerTurnEndRequested) != 0,
        "LR VoidForm 的结束回合请求应置位");

    // TyrannyPower's turn-start exhaust choice is a PlayerTurnStart choice on EndTurn; replaying the
    // action with the selection exhausts the chosen hand card and the power also increases the draw.
    FlashBattle tyrannyBattle = SliceBattle();
    FlashExec tyrannyExec = new(ref tyrannyBattle, rules, default);
    FlashKernel.ApplyPower(ref tyrannyExec, FlashLimits.PlayerOwner, tyranny, 1, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref tyrannyExec);
    FlashBattle tyrannyParent = tyrannyBattle;
    FlashAction endTurn = FlashAction.EndTurn();
    Check(FlashKernel.Step(ref tyrannyBattle, endTurn, rules, out FlashChoiceRequest tyrannyRequest)
        == FlashStepResult.NeedsChoice, "LR 暴政应在回合开始请求选牌");
    Check(tyrannyRequest.Timing == FlashChoiceTiming.PlayerTurnStart
        && tyrannyRequest.Effect == FlashChoiceEffect.Exhaust
        && tyrannyRequest.Source == FlashPile.Hand
        && tyrannyRequest.MinCount == 1 && tyrannyRequest.MaxCount == 1,
        $"LR 暴政选择规格错误 timing={tyrannyRequest.Timing} effect={tyrannyRequest.Effect} "
        + $"source={tyrannyRequest.Source} count={tyrannyRequest.MinCount}..{tyrannyRequest.MaxCount}");
    FlashBattle tyrannyReplay = tyrannyParent;
    Check(FlashKernel.Step(ref tyrannyReplay, endTurn.WithChoice(1UL), rules, out _)
        == FlashStepResult.Completed, "LR 暴政选择后回合应完成");
    Check(tyrannyReplay.ExhaustCount == 1, $"LR 暴政应消耗 1 张选中的手牌，实际 {tyrannyReplay.ExhaustCount}");

    // ForegoneConclusionPower: its BeforeHandDraw choice moves draw cards to hand and removes the
    // power; the empty-draw branch shuffles first and asks nothing.
    FlashBattle foregoneBattle = SliceBattle();
    FlashExec foregoneExec = new(ref foregoneBattle, rules, default);
    FlashKernel.ApplyPower(ref foregoneExec, FlashLimits.PlayerOwner, foregone, 2, FlashLimits.PlayerOwner);
    FlashKernel.ResolvePowerAmountChanges(ref foregoneExec);
    FlashBattle foregoneParent = foregoneBattle;
    FlashAction foregoneEnd = FlashAction.EndTurn();
    Check(FlashKernel.Step(ref foregoneBattle, foregoneEnd, rules, out FlashChoiceRequest foregoneRequest)
        == FlashStepResult.NeedsChoice, "LR 先见之明应在摸牌前请求选牌");
    Check(foregoneRequest.Timing == FlashChoiceTiming.PlayerTurnStart
        && foregoneRequest.Effect == FlashChoiceEffect.MoveToHand
        && foregoneRequest.Source == FlashPile.Draw
        && foregoneRequest.MinCount == 2 && foregoneRequest.MaxCount == 2,
        $"LR 先见之明选择规格错误 timing={foregoneRequest.Timing} effect={foregoneRequest.Effect} "
        + $"source={foregoneRequest.Source} count={foregoneRequest.MinCount}..{foregoneRequest.MaxCount}");
    FlashBattle foregoneReplay = foregoneParent;
    Check(FlashKernel.Step(ref foregoneReplay, foregoneEnd.WithChoice(0b11UL), rules, out _)
        == FlashStepResult.Completed, "LR 先见之明选择后回合应完成");
    Check(FlashKernel.GetAmount(foregoneReplay, FlashLimits.PlayerOwner, foregone) == 0,
        "LR 先见之明结算后应移除 Power");
    Check(foregoneReplay.HandCount == 7,
        $"LR 先见之明应先移入手牌再抽 5 张，实际手牌 {foregoneReplay.HandCount}");
}

static class Kg2TestLog
{
    public static List<string> Lines { get; } = [];

    public static void Clear() => Lines.Clear();

    public static void Add(string line) => Lines.Add(line);
}

[FlashContent("KG2_TEST_COST_POWER")]
public sealed class Kg2TestCostPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombat(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount)
    {
        Kg2TestLog.Add("cost-normal-power");
        return amount + 1m;
    }

    public override decimal TryModifyEnergyCostInCombatLate(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount)
    {
        Kg2TestLog.Add("cost-late-power");
        return amount * 2m;
    }
}

[FlashContent("KG2_TEST_COST_RELIC")]
public sealed class Kg2TestCostRelic : FlashRelicDef
{
    public override decimal TryModifyEnergyCostInCombat(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount)
    {
        Kg2TestLog.Add("cost-normal-relic");
        return amount + 3m;
    }
}

[FlashContent("KG2_TEST_RESULT_LOCATION_POWER")]
public sealed class Kg2TestResultLocationPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override FlashPile ModifyCardPlayResultLocation(ref FlashExec x, FlashListener self, in FlashCard card,
        bool isAutoPlay, int energyValue, FlashPile location, out FlashPilePosition position)
    {
        position = FlashPilePosition.Top;
        return FlashPile.Draw;
    }

    public override void AfterModifyingCardPlayResultLocation(ref FlashExec x, FlashListener self, in FlashCard card,
        FlashPile location) => Kg2TestLog.Add("location-after");
}

[FlashContent("KG2_TEST_PLAY_COUNT_POWER")]
public sealed class Kg2TestPlayCountPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override int ModifyCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card, byte target,
        int playCount) => playCount + 1;

    public override void AfterModifyingCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card)
        => Kg2TestLog.Add("count-after");
}

[FlashContent("KG2_TEST_POWER_CHANGE_POWER")]
public sealed class Kg2TestPowerChangePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterPowerAmountChanged(ref FlashExec x, FlashListener self, ushort powerId, int delta,
        byte applier, byte target)
    {
        Kg2TestLog.Add("power-changed");
        if (target == FlashLimits.FirstEnemyOwner)
            Kg2TestLog.Add("power-changed-target");
    }
}

[FlashContent("KG2_TEST_POWER_CHANGE_RELIC")]
public sealed class Kg2TestPowerChangeRelic : FlashRelicDef
{
    public override void AfterPowerAmountChanged(ref FlashExec x, FlashListener self, ushort powerId, int delta,
        byte applier, byte target) => Kg2TestLog.Add("power-changed-relic");
}

[FlashContent("A3_TEST_RAMPART_POWER")]
public sealed class A3TestRampartPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void Rampart(ref FlashExec x, FlashListener self) => Kg2TestLog.Add("rampart");
}

[FlashContent("A3_TEST_GENERATED_RELIC")]
public sealed class A3TestGeneratedRelic : FlashRelicDef
{
    public override void AfterCardGeneratedForCombat(ref FlashExec x, FlashListener self, int position,
        byte creator)
    {
        bool matches = creator == FlashLimits.PlayerOwner
            && (uint)position < (uint)x.B.CardCount
            && FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort expected)
            && x.B.Cards[position].Id == expected;
        Kg2TestLog.Add(matches ? "generated-ok" : "generated-wrong-position");
    }
}

[FlashContent("A3_TEST_ENDING_POWER")]
public sealed class A3TestEndingPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool PreventsCombatEnding => true;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath => false;
}

[FlashContent("KG2_TEST_X_VALUE_RELIC")]
public sealed class Kg2TestXValueRelic : FlashRelicDef
{
    public override int ModifyXValue(in FlashBattle battle, FlashRules rules, FlashListener self, in FlashCard card,
        int value) => value + 1;
}

[FlashContent("KG2_TEST_RESET_RELIC")]
public sealed class Kg2TestResetRelic : FlashRelicDef
{
    public override bool ShouldPlayerResetEnergy(in FlashBattle battle, FlashRules rules, FlashListener self,
        byte player) => false;
}

[FlashContent("KG2_TEST_RECEIVED_RELIC")]
public sealed class Kg2TestReceivedRelic : FlashRelicDef
{
    public override bool TryModifyPowerAmountReceived(in FlashBattle battle, FlashRules rules, FlashListener self,
        ushort powerId, byte target, int amount, out int modified)
    {
        modified = amount * 2;
        return amount > 0;
    }

    public override void AfterModifyingPowerAmountReceived(ref FlashExec x, FlashListener self, ushort powerId,
        byte target) => Kg2TestLog.Add("received-after");
}

[FlashContent("KG2_TEST_SHUFFLE_RELIC")]
public sealed class Kg2TestShuffleRelic : FlashRelicDef
{
    public override void AfterShuffle(ref FlashExec x, FlashListener self, byte shuffler)
        => Kg2TestLog.Add("shuffle");
}

[FlashContent("KG2_TEST_ENTERED_RELIC")]
public sealed class Kg2TestEnteredRelic : FlashRelicDef
{
    public override void AfterCardEnteredCombat(ref FlashExec x, FlashListener self, FlashPile pile, int index,
        in FlashCard card) => Kg2TestLog.Add("entered");
}

[FlashContent("KG2_TEST_DAMAGE_SOURCE_POWER")]
public sealed class Kg2TestDamageSourcePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
        => Kg2TestLog.Add(result.HasCardSource ? "damage-card" : "damage-none");
}

sealed class Keyed(FlashCard card) : IComparable<Keyed>
{
    public FlashCard Card { get; } = card;

    public int CompareTo(Keyed? other)
    {
        int id = Card.Id.CompareTo(other!.Card.Id);
        return id != 0 ? id : Card.Upgrade.CompareTo(other.Card.Upgrade);
    }
}

static class KsTestLog
{
    public static List<string> Lines { get; } = [];

    public static void Clear() => Lines.Clear();
}

[FlashContent("KS_DISCARD_TARGET")]
public sealed class KsDiscardTarget : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

[FlashContent("KS_SLY_CARD")]
public sealed class KsSlyCard : FlashCardDef
{
    public override void OnPlay(ref FlashExec x) => KsTestLog.Lines.Add("sly-play");
}

[FlashContent("KS_TEST_DISCARD_RELIC")]
public sealed class KsTestDiscardRelic : FlashRelicDef
{
    public static bool RemoveDiscardedCard { get; set; }

    public override void AfterCardDiscarded(ref FlashExec x, FlashListener self, in FlashCard card)
    {
        KsTestLog.Lines.Add("discarded");
        if (!RemoveDiscardedCard)
            return;
        for (int index = 0; index < x.B.DiscardCount; index++)
        {
            if (!FlashFingerprint.Equal(FlashPiles.At(ref x.B, FlashPile.Discard, index), card))
                continue;
            // Leave every pile: removing without inserting models the unrepresentable target.
            FlashPiles.RemoveAt(ref x.B, FlashPile.Discard, index);
            return;
        }
    }
}

[FlashContent("KS_TEST_SLY_RELIC")]
public sealed class KsTestSlyRelic : FlashRelicDef;

[FlashContent("RG_TEST_STAR_CARD")]
public sealed class RgTestStarCard : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

[FlashContent("RG_TEST_STAR_POWER")]
public sealed class RgTestStarPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterStarsSpent(ref FlashExec x, FlashListener self, int amount)
        => Kg2TestLog.Add($"stars-spent-power:{amount}");

    public override void AfterStarsGained(ref FlashExec x, FlashListener self, int amount)
        => Kg2TestLog.Add($"stars-gained:{amount}");
}

[FlashContent("RG_TEST_STAR_RELIC")]
public sealed class RgTestStarRelic : FlashRelicDef
{
    public override decimal TryModifyStarCost(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount)
    {
        Kg2TestLog.Add($"star-cost:{(int)amount}");
        return amount - 4m;
    }

    public override void AfterStarsSpent(ref FlashExec x, FlashListener self, int amount)
        => Kg2TestLog.Add($"stars-spent-relic:{amount}");
}

[FlashContent("CX_TEST_IMBUED_REMOVER")]
public sealed class CxTestImbuedRemover : FlashCardDef
{
    /// <summary>Removes the first other Imbued card from the hand, making its pending auto-play unlocatable.</summary>
    public override void OnPlay(ref FlashExec x)
    {
        FlashRegistry.TryEnchantment(FlashKernel.ImbuedEnchantmentId, out ushort imbued);
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (x.B.Cards[index].EnchantmentId != (byte)(imbued + 1))
                continue;
            FlashPiles.RemoveAt(ref x.B, FlashPile.Hand, index);
            return;
        }
    }
}
