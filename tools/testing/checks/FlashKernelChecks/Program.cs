using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CombatSolver.Engine.InCombat.Flash;
using CombatSolver.Engine.InCombat.Flash.Content.Afflictions;
using CombatSolver.Engine.InCombat.Flash.Content.Enchantments;
using CombatSolver.Engine.InCombat.Flash.Content.Powers;
using CombatSolver.Engine.InCombat.Flash.Content.Relics;

// Pure Flash kernel contracts (K0): no game, no CombatSolver assembly. Differential equivalence
// with the existing engine is checked separately by the offline harness probe.

Check(Unsafe.SizeOf<FlashCard>() == 92, $"FlashCard 应为 92 字节，实际 {Unsafe.SizeOf<FlashCard>()}");
Check(Unsafe.SizeOf<FlashEnemy>() == 64, $"FlashEnemy 应为 64 字节，实际 {Unsafe.SizeOf<FlashEnemy>()}");
Check(Unsafe.SizeOf<FlashPower>() == 24, $"FlashPower 应为 24 字节，实际 {Unsafe.SizeOf<FlashPower>()}");
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
static FlashRules SliceRules()
{
    FlashRulesBuilder builder = new() { BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first", "second", "third"] };
    FlashRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike);
    FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend);
    FlashRegistry.TryCard("BASH", out ushort bash);
    FlashRegistry.TryCard("WHIRLWIND", out ushort whirlwind);
    builder.SetCard(strike, 0, Stats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 1, [6m]));
    builder.SetCard(defend, 0, Stats(FlashCardType.Skill, FlashTargetType.Self, 1, [5m]));
    builder.SetCard(bash, 0, Stats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 2, [8m, 2m]));
    builder.SetCard(whirlwind, 0, Stats(FlashCardType.Attack, FlashTargetType.AllEnemies, 0, [5m], costsX: true));
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
        bool costsX = false) => new()
    {
        Type = type, Target = target, Rarity = FlashCardRarity.Basic, EnergyCost = cost, CostsX = costsX,
        StarCost = -1, StarCostsX = false, Keywords = FlashKeywords.None, MaxUpgradeLevel = 1, Vars = vars,
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
        locationModifiers, out int locationCount, out FlashPilePosition locationPosition);
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
    public override void AfterCardGeneratedForCombat(ref FlashExec x, FlashListener self, int position)
    {
        bool matches = (uint)position < (uint)x.B.CardCount
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
