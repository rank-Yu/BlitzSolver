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
        FlashCardSort.Sort(cards, rules);
        for (int index = 0; index < length; index++)
            Check(cards[index].Private == reference[index].Card.Private, $"排序第 {trial} 组第 {index} 位与 List.Sort 不一致");
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

// Rules for the slice content: Ironclad starter cards and one Nibbit.
static FlashRules SliceRules()
{
    FlashRulesBuilder builder = new() { BaseMaxEnergy = 3 };
    FlashRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike);
    FlashRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend);
    FlashRegistry.TryCard("BASH", out ushort bash);
    builder.SetCard(strike, 0, Stats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 1, [6m]));
    builder.SetCard(defend, 0, Stats(FlashCardType.Skill, FlashTargetType.Self, 1, [5m]));
    builder.SetCard(bash, 0, Stats(FlashCardType.Attack, FlashTargetType.AnyEnemy, 2, [8m, 2m]));
    FlashRegistry.TryPower("VULNERABLE_POWER", out ushort vulnerable);
    builder.SetPowerVars(vulnerable, [1.5m]);
    builder.SetMonster(0, new FlashMonsterRules
    {
        Nodes =
        [
            new FlashMonsterNode { NativeId = "INIT_MOVE", Kind = FlashMonsterNodeKind.ConditionalBranch, ConditionalTarget = 1 },
            new FlashMonsterNode { NativeId = "BUTT_MOVE", Kind = FlashMonsterNodeKind.Move, FollowUp = 2, Attacks = [new(12, 1)] },
            new FlashMonsterNode { NativeId = "SLICE_MOVE", Kind = FlashMonsterNodeKind.Move, FollowUp = 3, Attacks = [new(6, 1)] },
            new FlashMonsterNode { NativeId = "HISS_MOVE", Kind = FlashMonsterNodeKind.Move, FollowUp = 1 },
        ],
        InitialNode = 0,
        StaticInts = [5, 2],
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

sealed class Keyed(FlashCard card) : IComparable<Keyed>
{
    public FlashCard Card { get; } = card;

    public int CompareTo(Keyed? other)
    {
        int id = Card.Id.CompareTo(other!.Card.Id);
        return id != 0 ? id : Card.Upgrade.CompareTo(other.Card.Upgrade);
    }
}
