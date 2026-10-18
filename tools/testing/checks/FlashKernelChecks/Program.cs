using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CombatSolver.Engine.InCombat.Flash;

// Pure Flash kernel contracts (K0): no game, no CombatSolver assembly. Differential equivalence
// with the existing engine is checked separately by the offline harness probe.

Check(Unsafe.SizeOf<FlashCard>() == 32, $"FlashCard 应为 32 字节，实际 {Unsafe.SizeOf<FlashCard>()}");
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
CheckMonsterSpawn(SpawnRules());
CheckGenerationAndAutoPlay();
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

// Rules for the slice content: Ironclad starter cards and one Nibbit.
static FlashRules SliceRules()
{
    FlashRulesBuilder builder = new() { BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first", "second", "third"] };
    FlashRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike);
    FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend);
    FlashRegistry.TryCard("BASH", out ushort bash);
    builder.SetCard(strike, 0, Stats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 1, [6m]));
    builder.SetCard(defend, 0, Stats(FlashCardType.Skill, FlashTargetType.Self, 1, [5m]));
    builder.SetCard(bash, 0, Stats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 2, [8m, 2m]));
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

    static FlashCardStats Stats(FlashCardType type, FlashTargetType target, int cost, decimal[] vars) => new()
    {
        Type = type, Target = target, Rarity = FlashCardRarity.Basic, EnergyCost = cost, CostsX = false,
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

sealed class Keyed(FlashCard card) : IComparable<Keyed>
{
    public FlashCard Card { get; } = card;

    public int CompareTo(Keyed? other)
    {
        int id = Card.Id.CompareTo(other!.Card.Id);
        return id != 0 ? id : Card.Upgrade.CompareTo(other.Card.Upgrade);
    }
}
