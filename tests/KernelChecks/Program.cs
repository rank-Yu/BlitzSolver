using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BlitzSolver.Engine;
using BlitzSolver.Content.Afflictions;
using BlitzSolver.Content.Cards.Status;
using BlitzSolver.Content.Enchantments;
using BlitzSolver.Content.Powers;
using BlitzSolver.Content.Relics;

// Pure Blitz kernel contracts (K0): no game assembly; BlitzSolver.Core is the only production
// content assembly. Differential equivalence with the existing engine is checked separately by the
// offline harness probe.
BlitzRegistry.RegisterTestAssembly(Assembly.GetExecutingAssembly());
BlitzRegistry.RegisterTestAssembly(Assembly.GetExecutingAssembly());

Check(Unsafe.SizeOf<BlitzCard>() == 92, $"BlitzCard 应为 92 字节，实际 {Unsafe.SizeOf<BlitzCard>()}");
Check(Unsafe.SizeOf<BlitzEnemy>() == 64, $"BlitzEnemy 应为 64 字节，实际 {Unsafe.SizeOf<BlitzEnemy>()}");
Check(Unsafe.SizeOf<BlitzPower>() == 24, $"BlitzPower 应为 24 字节，实际 {Unsafe.SizeOf<BlitzPower>()}");
Check(Unsafe.SizeOf<BlitzOrb>() == 8, $"BlitzOrb 应为 8 字节，实际 {Unsafe.SizeOf<BlitzOrb>()}");
Check(Unsafe.SizeOf<BlitzRngState>() == 40, $"BlitzRngState 应为 40 字节，实际 {Unsafe.SizeOf<BlitzRngState>()}");
Check(Unsafe.SizeOf<BlitzBattle>() == BlitzBattle.ExpectedSize,
    $"BlitzBattle 应为 {BlitzBattle.ExpectedSize} 字节，实际 {Unsafe.SizeOf<BlitzBattle>()}");

CheckRng();
CheckGrowthRewards();
BlitzRules rules = SliceRules();
bool lateRegistrationThrew = false;
try
{
    BlitzRegistry.RegisterTestAssembly(Assembly.GetExecutingAssembly());
}
catch (InvalidOperationException)
{
    lateRegistrationThrew = true;
}
Check(lateRegistrationThrew, "BlitzRegistry 构建后仍接受测试程序集登记。");
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
CheckTwKernel();
CheckDfKernel(rules);
CheckRgKernel(rules);
CheckLrKernel(rules);
CheckTsKernel(rules);
CheckIdKernel(rules);
CheckCxKernel(rules);
CheckDecimalVars(rules);
CheckDx3Kernel(rules);
CheckPrKernel();
CheckEcKernel(rules);
CheckDx5Kernel(rules);
var benchmark = Benchmark(rules);

Console.WriteLine(JsonSerializer.Serialize(new
{
    contracts = "Passed",
    battleBytes = Unsafe.SizeOf<BlitzBattle>(),
    runtime = Environment.Version.ToString(),
    benchmark,
}, new JsonSerializerOptions { WriteIndented = true }));

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

// Reference copy of native MegaRandom + Rng (decompiled sts2 0.111.0) to pin the Blitz port.

static void CheckGrowthRewards()
{
    BlitzBattle battle = default;
    foreach (BlitzGrowthSource source in Enum.GetValues<BlitzGrowthSource>())
        BlitzGrowthRewards.Add(ref battle, source);
    foreach (BlitzGrowthSource source in Enum.GetValues<BlitzGrowthSource>())
        Check(BlitzGrowthRewards.Get(battle, source) == 1, $"成长来源 {source} 应记录一次");
    Check(BlitzGrowthRewards.Get(battle, BlitzGrowthSource.TheHunt) == 1, "同计数器的高 16 位不应串值");
    BlitzGrowthRewards.Set(ref battle, BlitzGrowthSource.MadScience, ushort.MaxValue);
    bool overflowed = false;
    try
    {
        BlitzGrowthRewards.Add(ref battle, BlitzGrowthSource.MadScience);
    }
    catch (BlitzCapacityException)
    {
        overflowed = true;
    }
    Check(overflowed, "成长计数溢出必须显式成为容量边界");
    Check((int)BlitzCounter.GrowthRewardsForbiddenGrimoireMadScience < BlitzLimits.Counters,
        "成长计数器必须位于固定计数器数组内");
}

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

    BlitzRngState blitz = BlitzRngState.FromSerializable(0, s0, s1, s2, s3);
    for (int index = 0; index < 2000; index++)
    {
        int max = 1 + index % 37;
        int expected = (int)(NextDouble() * max);
        Check(blitz.NextInt(max) == expected, $"NextInt 第 {index} 次不一致");
        float total = 0.5f + index % 7;
        float expectedFloat = (float)(NextDouble() * total);
        Check(blitz.NextFloat(total) == expectedFloat, $"NextFloat 第 {index} 次不一致");
        int expectedRange = (int)(NextDouble() * 9) + 3;
        Check(blitz.NextInt(3, 12) == expectedRange, $"NextInt(min,max) 第 {index} 次不一致");
    }
    Check(blitz.Counter == 6000, $"计数器应为 6000，实际 {blitz.Counter}");
}

// Blitz's introsort must permute equal-key cards exactly like List<T>.Sort().
static void CheckIntroSort(BlitzRules rules)
{
    Random random = new(17);
    for (int trial = 0; trial < 4000; trial++)
    {
        int length = 1 + random.Next(80);
        BlitzCard[] cards = new BlitzCard[length];
        List<Keyed> reference = new(length);
        for (int index = 0; index < length; index++)
        {
            cards[index] = new BlitzCard { Id = (ushort)random.Next(3), Upgrade = (byte)random.Next(2), Private = (short)index };
            reference.Add(new Keyed(cards[index]));
        }
        reference.Sort();
        BlitzCard[] original = (BlitzCard[])cards.Clone();
        BlitzCardSort.Sort(cards, rules);
        BlitzCard[] withOrigins = (BlitzCard[])original.Clone();
        int[] origins = new int[length];
        for (int index = 0; index < length; index++)
            origins[index] = index;
        BlitzCardSort.SortWithOrigins(withOrigins, origins, rules);
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
    BlitzBattle battle = default;
    BlitzCard card = new() { Id = 0 };
    for (int index = 0; index < BlitzLimits.Cards; index++)
        BlitzPiles.Append(ref battle, BlitzPile.Draw, card);
    try
    {
        BlitzPiles.Append(ref battle, BlitzPile.Draw, card);
        throw new InvalidOperationException("超出卡牌容量应抛出 BlitzCapacityException");
    }
    catch (BlitzCapacityException)
    {
    }
}

static void CheckPiles()
{
    BlitzBattle battle = default;
    for (short index = 0; index < 6; index++)
        BlitzPiles.Append(ref battle, index < 3 ? BlitzPile.Draw : BlitzPile.Discard, new BlitzCard { Private = index });
    BlitzPiles.DrawTopToHand(ref battle);
    Check(battle.HandCount == 1 && battle.DrawCount == 2 && BlitzPiles.At(ref battle, BlitzPile.Hand, 0).Private == 0, "抽顶牌应只移动段边界");
    BlitzCard removed = BlitzPiles.RemoveAt(ref battle, BlitzPile.Discard, 1);
    Check(removed.Private == 4 && battle.DiscardCount == 2 && battle.Cards[battle.CardCount].Private == 0, "移除后应前移并清零尾部");
    BlitzBattle copy = battle;
    Check(BlitzFingerprint.Equal(copy, battle) && BlitzFingerprint.Hash(copy) == BlitzFingerprint.Hash(battle), "复制后应字节相同");
}

// P16 kernel capabilities: the return-to-hand card bit, X-cost SetToFreeThisTurn, and instanced
// power row addressing.
static void CheckP16Kernel(BlitzRules rules)
{
    BlitzCard card = new() { Id = 0 };
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
    BlitzRegistry.TryCard("WHIRLWIND", out ushort whirlwind);
    BlitzCard xCard = BlitzKernel.CreateCard(rules, whirlwind);
    BlitzKernel.MakeFreeThisTurn(rules, ref xCard);
    Check(xCard.EnergyModifierCount == 1 && xCard.HasTemporaryStarCost,
        "X 费卡的 SetToFreeThisTurn 也应添加 0 费修饰与临时星能费用");

    // Instanced powers share (owner, id); occurrence addresses the exact row.
    BlitzBattle battle = default;
    battle.PowerCount = 2;
    battle.Powers[0] = new BlitzPower { Id = 1, Owner = BlitzLimits.PlayerOwner, Amount = 1 };
    battle.Powers[1] = new BlitzPower { Id = 1, Owner = BlitzLimits.PlayerOwner, Amount = 2 };
    Check(BlitzKernel.FindPowerOccurrence(battle, BlitzLimits.PlayerOwner, 1, 0) == 0
        && BlitzKernel.FindPowerOccurrence(battle, BlitzLimits.PlayerOwner, 1, 1) == 1
        && BlitzKernel.FindLastPower(battle, BlitzLimits.PlayerOwner, 1) == 1,
        "实例 Power 的 occurrence 定位错误");
}

// Forced moves and the synthetic STUNNED node (SimulatedCombatState.MonsterAi.cs:58-128).
static void CheckMonsterForcedMoves(BlitzRules rules)
{
    byte owner = BlitzKernel.EnemyOwner(0);
    BlitzBattle battle = SliceBattle();
    BlitzAction action = default;
    BlitzExec x = new(ref battle, rules, action);
    BlitzKernel.ForceStunnedMove(ref x, owner, -1);
    Check(battle.Enemies[0].Move == 4 && battle.Enemies[0].ForcedMove == 1,
        "STUNNED 缺省后继应取 StateLog 最后一条");
    Check(battle.Enemies[0].LoggedMoves == 1, "强制行动不应写入行动日志");
    battle.Enemies[0].Flags |= BlitzEnemyFlags.PerformedMoveThisRound;
    BlitzKernel.PrepareMonsterMovesForNextRound(ref x);
    Check(battle.Enemies[0].Move == 1 && battle.Enemies[0].ForcedMove == byte.MaxValue,
        "执行 STUNNED 后应推进到动态后继并清空 ForcedMove");
    Check(battle.Enemies[0].LoggedMoves == 2, "推进后的后继应写入行动日志");

    battle = SliceBattle();
    x = new BlitzExec(ref battle, rules, action);
    battle.Enemies[0].Flags |= BlitzEnemyFlags.PerformedMoveThisRound;
    BlitzKernel.ForceStunnedMove(ref x, owner, 2);
    Check((battle.Enemies[0].Flags & BlitzEnemyFlags.ForcedMustPerformAfterMove) != 0,
        "本轮已行动后强制 STUNNED 应记下 must-perform-once 未满足");
    BlitzKernel.PrepareMonsterMovesForNextRound(ref x);
    Check(battle.Enemies[0].Move == 4 && battle.Enemies[0].ForcedMove == 2,
        "本轮已行动后强制 STUNNED 不应推进");
    Check((battle.Enemies[0].Flags & BlitzEnemyFlags.ForcedMustPerformAfterMove) == 0,
        "权威边界上不得残留 ForcedMustPerformAfterMove");
    battle.Enemies[0].Flags |= BlitzEnemyFlags.PerformedMoveThisRound;
    BlitzKernel.PrepareMonsterMovesForNextRound(ref x);
    Check(battle.Enemies[0].Move == 2, "STUNNED 在下一轮执行后应推进到后继");

    battle = SliceBattle();
    x = new BlitzExec(ref battle, rules, action);
    battle.Enemies[0].Flags |= BlitzEnemyFlags.PerformedMoveThisRound;
    BlitzKernel.ForceMonsterMove(ref x, owner, 3);
    BlitzKernel.PrepareMonsterMovesForNextRound(ref x);
    Check(battle.Enemies[0].Move == 1 && battle.Enemies[0].ForcedMove == byte.MaxValue,
        "普通强制行动在本轮已行动后应直接推进");

    battle = SliceBattle();
    x = new BlitzExec(ref battle, rules, action);
    BlitzKernel.ForceStunnedMove(ref x, owner, 2);
    Check(battle.Enemies[0].ForcedMove == 2, "显式后继应写入 ForcedMove");
    Check(battle.Enemies[0].Move == 4, "强制 STUNNED 应指向合成节点");
}

// KM2 kernel contracts: slot names outside the encounter list behave like
// SimulatedCombatState.GetSlotIndex (-1), a fully blocked Damage result exposes the flag the
// Imbalanced stun reads, and an unresolved spawning death keeps IsEnding false.
static void CheckKm2Kernel(BlitzRules rules)
{
    Check(BlitzKernel.SlotIndex(rules, "second") == 1, "已知槽位应返回其下标");
    Check(BlitzKernel.SlotIndex(rules, "middle") == BlitzKernel.NoSlot,
        "EncounterModel.Slots 之外的槽名应映射到 NoSlot");
    Check(BlitzKernel.SlotIndex(rules, null) == BlitzKernel.NoSlot, "null 槽名应映射到 NoSlot");

    BlitzAction action = default;
    BlitzBattle battle = SliceBattle();
    battle.Player.Block = 100;
    BlitzExec x = new(ref battle, rules, action);
    Check(BlitzKernel.Damage(ref x, [BlitzLimits.PlayerOwner], 10m, BlitzValueProps.Move,
            BlitzKernel.EnemyOwner(0), hasCardSource: false, default),
        "完全格挡的 Damage 应返回 true");
    battle = SliceBattle();
    x = new BlitzExec(ref battle, rules, action);
    Check(!BlitzKernel.Damage(ref x, [BlitzLimits.PlayerOwner], 10m, BlitzValueProps.Move,
            BlitzKernel.EnemyOwner(0), hasCardSource: false, default),
        "未完全格挡的 Damage 应返回 false");

    Check(BlitzRegistry.TryPower("INFESTED_POWER", out ushort infested), "InfestedPower 应有 Blitz 定义");
    battle = default;
    battle.Outcome = BlitzOutcome.None;
    battle.EnemyCount = 1;
    battle.RosterCount = 0;
    battle.Enemies[0].Creature.Flags = BlitzCreatureFlags.Present;
    battle.Enemies[0].DeathPhase = BlitzDeathPhase.None;
    battle.PowerCount = 1;
    battle.Powers[0] = new BlitzPower
    {
        Id = infested, Owner = BlitzKernel.EnemyOwner(0), Amount = 1, Applier = BlitzKernel.EnemyOwner(0),
    };
    Check(!BlitzKernel.IsEnding(battle), "未结算的寄生死亡应阻止战斗结束");
    battle.PowerCount = 0;
    battle.Powers[0] = default;
    Check(BlitzKernel.IsEnding(battle), "死亡效果结算完后应允许战斗结束");
}

// Rules for the slice content: Ironclad starter cards and one Nibbit.
// DF: orb kernel contracts. The existing engine's order and values are pinned here: full-queue
// channel evokes the leftmost orb first, Dark evokes its raw accumulation (no ModifyOrbValue), Glass
// decays its private passive by one after a positive passive, Focus modifies passive/evoke values,
// Plasma's passive fires at turn start, and GoldPlatedCables adds one passive trigger to the first orb.
const int TwStunnedNode = 2;

// TW: theft and Waterfall Giant kernel capabilities. The contracts cover the detached stolen-card
// table, the outstanding-resource ledger, escape compaction, the Steam Eruption death rewrite/forced
// phases and the explicit rejection of a Flutter stun whose last move leads to a branch node.
static void CheckTwKernel()
{
    BlitzRules rules = TwRules();
    Check(Unsafe.SizeOf<BlitzEnemy>() == 64, "BlitzEnemy 加入蒸汽阶段后仍应为 64 字节");
    Check(Unsafe.SizeOf<BlitzBattle>() == BlitzBattle.ExpectedSize, "BlitzBattle 尺寸应与 ExpectedSize 一致");

    // Detached table: three live slots (two Nightmare instances plus one stolen card), no eviction;
    // removing the referencing power frees its slot.
    BlitzBattle detached = TwBattle(rules);
    BlitzCard value = new() { Id = 1, BaseCost = 1 };
    int firstSlot = -1;
    for (int index = 0; index < BlitzLimits.DetachedCards; index++)
    {
        int slot = BlitzKernel.AllocateDetachedCard(ref detached, value);
        if (index == 0)
            firstSlot = slot;
    }
    bool overflow = false;
    try
    {
        BlitzKernel.AllocateDetachedCard(ref detached, value);
    }
    catch (BlitzCapacityException)
    {
        overflow = true;
    }
    Check(overflow, "DetachedCards 满后应抛 BlitzCapacityException");
    BlitzExec detachExec = new(ref detached, rules, default);
    BlitzKernel.ApplyPower(ref detachExec, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<SwipePower>(), 1, 0);
    int swipeRow = BlitzKernel.FindPower(detached, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<SwipePower>());
    detached.Powers[swipeRow].Aux0 = firstSlot + 1;
    BlitzKernel.SetPowerAmount(ref detached, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<SwipePower>(), 0);
    Check(detached.DetachedCards[firstSlot].Id == 0, "移除 SwipePower 应释放 detached 槽位");

    // GremlinMerc: min(power amount, gold), the private gold ledger and the outstanding counter.
    BlitzBattle thievery = TwBattle(rules);
    thievery.Gold = 50;
    BlitzExec thiefExec = new(ref thievery, rules, default);
    BlitzKernel.ApplyPowerTargeted(ref thiefExec, BlitzKernel.EnemyOwner(0), BlitzLimits.PlayerOwner,
        BlitzRegistry.PowerId<ThieveryPower>(), 20, BlitzKernel.EnemyOwner(0));
    BlitzKernel.RecordThievery(ref thiefExec, BlitzKernel.EnemyOwner(0));
    Check(thievery.Gold == 30, $"首次偷窃应拿走 20 金币，实际余额 {thievery.Gold}");
    Check(thievery.Counter(BlitzCounter.OutstandingStolenGold) == 20, "未追回金币应为 20");
    int thieveryLedgerRow = BlitzKernel.FindPower(thievery, BlitzKernel.EnemyOwner(0),
        BlitzRegistry.PowerId<ThieveryPower>());
    Check(thieveryLedgerRow >= 0 && thievery.Powers[thieveryLedgerRow].Aux0 == 20,
        "ThieveryPower 的私有金币账本应为 20");
    thievery.Gold = 5;
    BlitzKernel.RecordThievery(ref thiefExec, BlitzKernel.EnemyOwner(0));
    Check(thievery.Gold == 0 && thievery.Counter(BlitzCounter.OutstandingStolenGold) == 25,
        "第二次偷窃应受剩余金币限制并为 5");
    int thieveryRow = BlitzKernel.FindPower(thievery, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<ThieveryPower>());
    thievery.Powers[thieveryRow].Target = BlitzLimits.NoOwner;
    int before = thievery.Counter(BlitzCounter.OutstandingStolenGold);
    BlitzKernel.RecordThievery(ref thiefExec, BlitzKernel.EnemyOwner(0));
    Check(thievery.Counter(BlitzCounter.OutstandingStolenGold) == before, "缺少目标时不应偷窃");

    // RecoverStolenResources: killing the Swipe/Heist owner returns the card and gold; the detached
    // value is dropped with the power.
    BlitzBattle recovered = TwBattle(rules);
    BlitzExec recoverExec = new(ref recovered, rules, default);
    int stolenSlot = BlitzKernel.AllocateDetachedCard(ref recovered, value);
    BlitzKernel.ApplyPowerTargeted(ref recoverExec, BlitzKernel.EnemyOwner(0), BlitzLimits.PlayerOwner,
        BlitzRegistry.PowerId<SwipePower>(), 1, BlitzKernel.EnemyOwner(0));
    int recoverSwipe = BlitzKernel.FindPower(recovered, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<SwipePower>());
    recovered.Powers[recoverSwipe].Aux0 = stolenSlot + 1;
    BlitzKernel.ApplyPowerTargeted(ref recoverExec, BlitzKernel.EnemyOwner(0), BlitzLimits.PlayerOwner,
        BlitzRegistry.PowerId<HeistPower>(), 7, BlitzKernel.EnemyOwner(0));
    recovered.SetCounter(BlitzCounter.OutstandingStolenCards, 1);
    recovered.SetCounter(BlitzCounter.OutstandingStolenGold, 7);
    recovered.Enemies[0].Creature.CurrentHp = 0;
    BlitzKernel.ApplyEnemyDeathPowers(ref recoverExec);
    Check(recovered.Counter(BlitzCounter.OutstandingStolenCards) == 0
        && recovered.Counter(BlitzCounter.OutstandingStolenGold) == 0, "击杀飞贼应追回被偷卡与金币");
    Check(recovered.DetachedCards[stolenSlot].Id == 0, "追回后 detached 槽位应清空");
    Check(BlitzKernel.FindPower(recovered, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<SwipePower>()) < 0,
        "死亡清理应移除 SwipePower");

    // Owner-left-roster cleanup: MinionPower (native keep-on-death) is removed once the creature
    // leaves the roster; FlutterPower keeps its observed pending row (PowerRemainsAfterOwnerLeavesRoster).
    BlitzBattle roster = TwBattle(rules);
    BlitzExec rosterExec = new(ref roster, rules, default);
    BlitzKernel.ApplyPower(ref rosterExec, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<MinionPower>(), 1,
        BlitzKernel.EnemyOwner(0));
    BlitzKernel.ApplyPower(ref rosterExec, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<FlutterPower>(), 1,
        BlitzKernel.EnemyOwner(0));
    roster.Enemies[0].Creature.CurrentHp = 0;
    BlitzKernel.RemoveFromRoster(ref roster, BlitzKernel.EnemyOwner(0));
    BlitzKernel.ApplyEnemyDeathPowers(ref rosterExec);
    Check(BlitzKernel.FindPower(roster, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<MinionPower>()) < 0,
        "离开名单的死者应移除 MinionPower");
    Check(BlitzKernel.FindPower(roster, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<FlutterPower>()) >= 0,
        "离开名单的 Hopper 应保留 FlutterPower 的既有 pending 行");

    // Escape compacts the known-enemy array and remaps later owners, but does not recover loot.
    BlitzBattle escaped = TwBattle(rules);
    escaped.EnemyCount = 2;
    escaped.RosterCount = 2;
    escaped.Roster[0] = 0;
    escaped.Roster[1] = 1;
    escaped.Enemies[1] = escaped.Enemies[0];
    escaped.Enemies[1].CombatId = 2;
    escaped.Enemies[1].Creature = new BlitzCreature { CurrentHp = 40, MaxHp = 40, Flags = BlitzCreatureFlags.Present };
    escaped.Enemies[1].Flags = BlitzEnemyFlags.InCombat;
    BlitzExec escapeExec = new(ref escaped, rules, default);
    int escapeSlot = BlitzKernel.AllocateDetachedCard(ref escaped, value);
    BlitzKernel.ApplyPowerTargeted(ref escapeExec, BlitzKernel.EnemyOwner(0), BlitzLimits.PlayerOwner,
        BlitzRegistry.PowerId<SwipePower>(), 1, BlitzKernel.EnemyOwner(0));
    int escapeSwipe = BlitzKernel.FindPower(escaped, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<SwipePower>());
    escaped.Powers[escapeSwipe].Aux0 = escapeSlot + 1;
    escaped.SetCounter(BlitzCounter.OutstandingStolenCards, 1);
    BlitzKernel.ApplyPower(ref escapeExec, BlitzKernel.EnemyOwner(1), BlitzRegistry.PowerId<StrengthPower>(), 2,
        BlitzKernel.EnemyOwner(1));
    BlitzKernel.EscapeEnemy(ref escapeExec, BlitzKernel.EnemyOwner(0));
    Check(escaped.EnemyCount == 1 && escaped.RosterCount == 1 && escaped.Roster[0] == 0,
        "逃跑应压实已知敌人与名单");
    Check(escaped.DetachedCards[escapeSlot].Id == 0, "逃跑移除 SwipePower 应释放槽位");
    Check(escaped.Counter(BlitzCounter.OutstandingStolenCards) == 1, "逃跑后未追回卡数应保持不变");
    int shifted = BlitzKernel.FindPower(escaped, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<StrengthPower>());
    Check(shifted >= 0 && escaped.Powers[shifted].Owner == BlitzKernel.EnemyOwner(0),
        "后续敌人的 Power owner 应随下标前移");

    // A surviving power that names the escaped creature as applier cannot be projected.
    BlitzBattle referenced = TwBattle(rules);
    referenced.EnemyCount = 2;
    referenced.RosterCount = 2;
    referenced.Roster[1] = 1;
    referenced.Enemies[1] = referenced.Enemies[0];
    referenced.Enemies[1].CombatId = 2;
    referenced.Enemies[1].Flags = BlitzEnemyFlags.InCombat;
    BlitzExec referencedExec = new(ref referenced, rules, default);
    BlitzKernel.ApplyPowerFromMonster(ref referencedExec, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<WeakPower>(), 1,
        BlitzKernel.EnemyOwner(0));
    bool rejected = false;
    try
    {
        BlitzKernel.EscapeEnemy(ref referencedExec, BlitzKernel.EnemyOwner(0));
    }
    catch (BlitzUnsupportedException)
    {
        rejected = true;
    }
    Check(rejected, "逃跑生物仍被 Power 引用时应显式拒绝");

    // Steam Eruption: a dead owner is rewritten to the forced about-to-blow phase, the power is kept by
    // the death cleanup, and the two forced consumptions record then deal the explosion damage.
    BlitzBattle steam = TwBattle(rules);
    BlitzExec steamExec = new(ref steam, rules, default);
    BlitzKernel.ApplyPower(ref steamExec, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<SteamEruptionPower>(), 18,
        BlitzKernel.EnemyOwner(0));
    steam.Enemies[0].Creature.CurrentHp = 0;
    BlitzKernel.ApplyEnemyDeathPowers(ref steamExec);
    Check(steam.Enemies[0].Creature.CurrentHp == BlitzLimits.ValueClamp
        && steam.Enemies[0].Creature.MaxHp == BlitzLimits.ValueClamp, "蒸汽爆发的死亡改写应把 HP/最大 HP 设为钳制值");
    Check(steam.Enemies[0].SteamPhase == BlitzSteamPhase.AboutToBlow, "死亡改写应进入 AboutToBlow 阶段");
    Check(steam.Enemies[0].Move == 0, $"死亡改写应强制 ABOUT_TO_BLOW_MOVE，实际节点 {steam.Enemies[0].Move}");
    Check(BlitzKernel.GetAmount(steam, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<SteamEruptionPower>()) == 18,
        "死亡清理应保留 SteamEruptionPower");
    Check(!BlitzKernel.IsEnding(steam), "有水火巨兽时战斗不应结束");
    Check(BlitzKernel.TryConsumeSteamForcedMove(ref steamExec, BlitzKernel.EnemyOwner(0), out bool explode1, out _)
        && !explode1 && steam.Enemies[0].SteamPhase == BlitzSteamPhase.Explode, "第一次强制行动应进入 Explode 阶段");
    Check(BlitzKernel.GetAmount(steam, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<SteamEruptionPower>()) == 0
        && steam.Enemies[0].Ai[BlitzKernel.SteamEruptionDamageRegister] == 18, "AboutToBlow 应记录爆发伤害并移除 Power");
    Check(BlitzKernel.TryConsumeSteamForcedMove(ref steamExec, BlitzKernel.EnemyOwner(0), out bool explode2, out int damage)
        && explode2 && damage == 18, "第二次强制行动应报告 18 点爆发伤害");

    // Flutter: the last stack resolves the next state of the last logged move. A direct move is
    // accepted; a branch follow-up is an explicit boundary (the assumption contract).
    BlitzBattle flutter = TwBattle(rules);
    flutter.Enemies[0].Move = 0;
    BlitzKernel.LogMonsterMove(ref flutter.Enemies[0], 0);
    BlitzExec flutterExec = new(ref flutter, rules, default);
    BlitzKernel.ApplyPower(ref flutterExec, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<FlutterPower>(), 1,
        BlitzKernel.EnemyOwner(0));
    BlitzKernel.Damage(ref flutterExec, [BlitzKernel.EnemyOwner(0)], 5m, BlitzValueProps.Move, BlitzLimits.PlayerOwner,
        hasCardSource: false, default);
    Check(flutter.Enemies[0].Move == TwStunnedNode && flutter.Enemies[0].ForcedMove == 1,
        "振翅归零应以最近行动的下一状态进入 STUNNED");
    BlitzBattle branch = TwBattle(rules);
    branch.Enemies[0].Move = 3;
    BlitzKernel.LogMonsterMove(ref branch.Enemies[0], 3);
    BlitzExec branchExec = new(ref branch, rules, default);
    BlitzKernel.ApplyPower(ref branchExec, BlitzKernel.EnemyOwner(0), BlitzRegistry.PowerId<FlutterPower>(), 1,
        BlitzKernel.EnemyOwner(0));
    bool branchRejected = false;
    try
    {
        BlitzKernel.Damage(ref branchExec, [BlitzKernel.EnemyOwner(0)], 5m, BlitzValueProps.Move, BlitzLimits.PlayerOwner,
            hasCardSource: false, default);
    }
    catch (BlitzUnsupportedException)
    {
        branchRejected = true;
    }
    Check(branchRejected, "振翅眩晕的后继是分支节点时应显式拒绝");
}

static BlitzBattle TwBattle(BlitzRules rules)
{
    BlitzBattle battle = NewBattle();
    battle.Gold = 0;
    battle.Enemies[0].MonsterId = BlitzRegistry.MonsterId<BlitzSolver.Content.Monsters.Act1.WaterfallGiant>();
    battle.Enemies[0].Rules = 0;
    battle.Enemies[0].Move = 0;
    battle.Enemies[0].ForcedMove = byte.MaxValue;
    return battle;
}

static BlitzRules TwRules()
{
    BlitzRegistry.TryMonster("WATERFALL_GIANT", out ushort waterfall);
    BlitzRulesBuilder builder = new() { RootEnemyCount = 1, HasTheftContent = true };
    BlitzMonsterNode[] nodes =
    [
        new BlitzMonsterNode
        {
            NativeId = "ABOUT_TO_BLOW_MOVE", Kind = BlitzMonsterNodeKind.Move, FollowUp = 1,
            MustPerformOnceBeforeTransitioning = true,
        },
        new BlitzMonsterNode { NativeId = "EXPLODE_MOVE", Kind = BlitzMonsterNodeKind.Move, FollowUp = 1 },
        new BlitzMonsterNode { NativeId = "STUNNED", Kind = BlitzMonsterNodeKind.Move },
        new BlitzMonsterNode { NativeId = "FLUTTER_TEST_MOVE", Kind = BlitzMonsterNodeKind.Move, FollowUp = 4 },
        new BlitzMonsterNode
        {
            NativeId = "TEST_BRANCH", Kind = BlitzMonsterNodeKind.ConditionalBranch, ConditionalTarget = 1,
        },
    ];
    builder.SetMonster(0, new BlitzMonsterRules
    {
        MonsterId = waterfall,
        Nodes = nodes,
        InitialNode = 0,
        PendingInitialMoveNode = 0,
        StunnedNode = TwStunnedNode,
        InitialHpMin = 100,
        InitialHpMax = 100,
        StaticInts = [],
    });
    builder.SetPowerVars(BlitzRegistry.PowerId<FlutterPower>(), [50m]);
    return builder.Build();
}

static void CheckDfKernel(BlitzRules rules)
{
    Check(BlitzRegistry.TryOrb("LIGHTNING_ORB", out ushort lightning), "缺少 LIGHTNING_ORB");
    Check(BlitzRegistry.TryOrb("FROST_ORB", out ushort frost), "缺少 FROST_ORB");
    Check(BlitzRegistry.TryOrb("DARK_ORB", out ushort dark), "缺少 DARK_ORB");
    Check(BlitzRegistry.TryOrb("PLASMA_ORB", out ushort plasma), "缺少 PLASMA_ORB");
    Check(BlitzRegistry.TryOrb("GLASS_ORB", out ushort glass), "缺少 GLASS_ORB");
    Check(BlitzRegistry.TryPower("FOCUS_POWER", out ushort focus), "缺少 FOCUS_POWER");
    Check(BlitzRegistry.TryRelic("GOLD_PLATED_CABLES", out ushort gold), "缺少 GOLD_PLATED_CABLES");
    Check(BlitzRegistry.TryRelic("CRACKED_CORE", out ushort crackedCore), "缺少 CRACKED_CORE");
    Check(BlitzRegistry.TryRelic("RUNIC_CAPACITOR", out ushort runic), "缺少 RUNIC_CAPACITOR");
    Check(BlitzRegistry.TryRelic("SYMBIOTIC_VIRUS", out ushort symbiotic), "缺少 SYMBIOTIC_VIRUS");
    Check(BlitzRegistry.TryRelic("REPTILE_TRINKET", out ushort reptile), "缺少 REPTILE_TRINKET");
    Check(BlitzRegistry.TryPower("THUNDER_POWER", out ushort thunder), "缺少 THUNDER_POWER");
    Check(BlitzRegistry.TryPower("REPTILE_TRINKET_POWER", out ushort reptilePower), "缺少 REPTILE_TRINKET_POWER");
    Check(BlitzRegistry.TryPower("STRENGTH_POWER", out ushort strength), "缺少 STRENGTH_POWER");
    BlitzAction action = default;

    BlitzBattle OrbBattle(int enemyHp = 1000)
    {
        BlitzBattle battle = SliceBattle();
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

    void SetFocus(ref BlitzBattle battle, int amount)
    {
        battle.PowerCount = 1;
        battle.Powers[0] = new BlitzPower { Id = focus, Owner = BlitzLimits.PlayerOwner, Amount = amount };
    }

    // Full queue channel: the leftmost (Dark) orb is evoked first. Dark's evoke uses the raw
    // accumulation, while its passive would have used ModifyOrbValue.
    BlitzBattle battle = OrbBattle();
    SetFocus(ref battle, 2);
    battle.OrbCount = 3;
    battle.Orbs[0] = new BlitzOrb { Id = checked((byte)dark), Aux = 10 };
    battle.Orbs[1] = new BlitzOrb { Id = checked((byte)frost) };
    battle.Orbs[2] = new BlitzOrb { Id = checked((byte)lightning) };
    BlitzExec x = new(ref battle, rules, action);
    Check(BlitzKernel.ChannelOrb(ref x, lightning), "满槽充能应成功");
    Check(battle.OrbCount == 3 && battle.Orbs[0].Id == frost && battle.Orbs[2].Id == lightning,
        "满槽充能应先激发最左侧球");
    Check(battle.Enemies[0].Creature.CurrentHp == 990, "暗球激发不应受集中修正（原始累积 10）");
    Check(battle.Counter(BlitzCounter.LightningChannels) == 1 && battle.Counter(BlitzCounter.OrbChannels) == 1,
        "充能计数错误");

    // Turn-end passives in queue order: Frost 2+Focus=4 block, then two Lightning 3+Focus=5 hits.
    BlitzKernel.TriggerBeforeTurnEndOrbs(ref x);
    Check(battle.Player.Block == 4, $"冰球回合末格挡应为 4，实际 {battle.Player.Block}");
    Check(battle.Enemies[0].Creature.CurrentHp == 980, "闪电球回合末被动伤害错误");

    // GoldPlatedCables adds one trigger to the first orb's passive.
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = gold };
    Check(BlitzHooks.ModifyOrbPassiveTriggerCounts(battle, rules, battle.Orbs[0], 0, 1) == 2,
        "金缆应让首个球的被动触发 +1");
    battle.Player.Block = 0;
    BlitzKernel.TriggerBeforeTurnEndOrbs(ref x);
    Check(battle.Player.Block == 8, $"金缆双次冰球被动应给 8 格挡，实际 {battle.Player.Block}");
    Check(battle.Enemies[0].Creature.CurrentHp == 970, "金缆回合末的闪电被动次数错误");

    // Dark accumulation and raw evoke.
    battle = OrbBattle();
    SetFocus(ref battle, 3);
    battle.OrbCount = 1;
    battle.Orbs[0] = new BlitzOrb { Id = checked((byte)dark), Aux = 10 };
    x = new BlitzExec(ref battle, rules, action);
    BlitzKernel.TriggerOrbPassive(ref x, 0);
    Check(battle.Orbs[0].Aux == 19, $"暗球被动应累积 6+3，实际 {battle.Orbs[0].Aux}");
    BlitzKernel.EvokeOrb(ref x, 0);
    Check(battle.Enemies[0].Creature.CurrentHp == 981, "暗球激发应使用原始累积 19");

    // Glass decays after a positive passive; evoke doubles the modified remaining value.
    battle = OrbBattle();
    SetFocus(ref battle, 1);
    battle.OrbCount = 1;
    battle.Orbs[0] = new BlitzOrb { Id = checked((byte)glass), Aux = 4 };
    x = new BlitzExec(ref battle, rules, action);
    BlitzKernel.TriggerOrbPassive(ref x, 0);
    Check(battle.Orbs[0].Aux == 3 && battle.Enemies[0].Creature.CurrentHp == 995,
        "玻璃球被动应造成 5 并递减私有值");
    BlitzKernel.EvokeOrb(ref x, 0);
    Check(battle.Enemies[0].Creature.CurrentHp == 987, "玻璃球激发应造成 (3+1)*2 = 8");

    // Plasma passive/evoke energy.
    battle = OrbBattle();
    battle.Energy = 0;
    battle.OrbCount = 1;
    battle.Orbs[0] = new BlitzOrb { Id = checked((byte)plasma) };
    x = new BlitzExec(ref battle, rules, action);
    BlitzKernel.TriggerOrbPassive(ref x, 0);
    Check(battle.Energy == 1, "等离子球被动应 +1 能量");
    BlitzKernel.EvokeOrb(ref x, 0);
    Check(battle.Energy == 3, "等离子球激发应 +2 能量");

    // Dualcast semantics: two evocations of the same orb, dequeue only on the last one.
    battle = OrbBattle();
    battle.OrbCount = 2;
    battle.Orbs[0] = new BlitzOrb { Id = checked((byte)lightning) };
    battle.Orbs[1] = new BlitzOrb { Id = checked((byte)lightning) };
    x = new BlitzExec(ref battle, rules, action);
    BlitzKernel.EvokeNext(ref x, repeat: 2);
    Check(battle.OrbCount == 1 && battle.Enemies[0].Creature.CurrentHp == 984,
        "双发激发应激发两次且只出队一次");

    // Capacity 0 refuses a channel for a character with base slots.
    battle = OrbBattle();
    battle.OrbCapacity = 0;
    x = new BlitzExec(ref battle, rules, action);
    Check(!BlitzKernel.ChannelOrb(ref x, lightning) && battle.OrbCount == 0, "零容量不应充能");

    // Slot growth/removal.
    battle = OrbBattle();
    battle.OrbCapacity = 9;
    BlitzKernel.AddOrbSlots(ref battle, 3);
    Check(battle.OrbCapacity == 10, "球槽容量应钳制在 10");
    battle.OrbCapacity = 3;
    battle.OrbCount = 3;
    battle.Orbs[0] = new BlitzOrb { Id = checked((byte)lightning) };
    battle.Orbs[1] = new BlitzOrb { Id = checked((byte)frost) };
    battle.Orbs[2] = new BlitzOrb { Id = checked((byte)dark) };
    BlitzKernel.RemoveOrbSlots(ref battle, 2);
    Check(battle.OrbCapacity == 1 && battle.OrbCount == 1 && battle.Orbs[0].Id == lightning,
        "移除球槽应从队尾删球");

    // CrackedCore channels one Lightning on the first player side turn.
    battle = OrbBattle();
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = crackedCore };
    x = new BlitzExec(ref battle, rules, action);
    BlitzRegistry.Relic(crackedCore).BeforeSideTurnStart(ref x,
        new BlitzListener(BlitzListenerKind.Relic, 0, BlitzLimits.PlayerOwner), BlitzSide.Player);
    Check(battle.OrbCount == 1 && battle.Orbs[0].Id == lightning, "破碎核心应在第一回合充能闪电");

    // RunicCapacitor and SymbioticVirus run in the AfterSideTurnStart relic batch.
    battle = OrbBattle();
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = runic };
    x = new BlitzExec(ref battle, rules, action);
    BlitzRegistry.Relic(runic).AfterSideTurnStart(ref x,
        new BlitzListener(BlitzListenerKind.Relic, 0, BlitzLimits.PlayerOwner), BlitzSide.Player);
    Check(battle.OrbCapacity == 6, "符文电容器应 +3 球槽");

    battle = OrbBattle();
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = symbiotic };
    x = new BlitzExec(ref battle, rules, action);
    BlitzRegistry.Relic(symbiotic).AfterSideTurnStart(ref x,
        new BlitzListener(BlitzListenerKind.Relic, 0, BlitzLimits.PlayerOwner), BlitzSide.Player);
    Check(battle.OrbCount == 1 && battle.Orbs[0].Id == checked((byte)dark), "共生病毒应充能暗球");

    // AfterOrbEvoked: ThunderPower adds its amount to every living evoke target of a Lightning orb.
    battle = OrbBattle();
    battle.PowerCount = 1;
    battle.Powers[0] = new BlitzPower { Id = thunder, Owner = BlitzLimits.PlayerOwner, Amount = 5 };
    battle.OrbCount = 1;
    battle.Orbs[0] = new BlitzOrb { Id = checked((byte)lightning) };
    x = new BlitzExec(ref battle, rules, action);
    BlitzKernel.EvokeOrb(ref x, 0);
    Check(battle.Enemies[0].Creature.CurrentHp == 987, "雷霆应在闪电球激发后追加 5 伤害");

    // AfterPotionUsed: ReptileTrinket grants temporary and permanent Strength.
    battle = OrbBattle();
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = reptile };
    x = new BlitzExec(ref battle, rules, action);
    BlitzHooks.AfterPotionUsed(ref x, 0);
    Check(BlitzKernel.GetAmount(battle, BlitzLimits.PlayerOwner, reptilePower) == 3,
        "爬行动物饰品应赋予 3 层临时力量");
    Check(BlitzKernel.GetAmount(battle, BlitzLimits.PlayerOwner, strength) == 3,
        "临时力量首次施加应同时获得 3 力量");
}

// MB: contracts for the fixed-option choice protocol (Knowledge Demon's curse), the instanced
// targeted SandpitPower (exact-instance decrement), and EntropicBrew's out-of-combat pool with the
// freeFromEntropic slot mask and generated-use counter.
static void CheckMbKernel(BlitzRules battleRules)
{
    Check(BlitzRegistry.TryPower("SANDPIT_POWER", out ushort sandpit), "缺少 SANDPIT_POWER");
    BlitzBattle battle = NewBattle();
    BlitzAction noChoice = default;
    BlitzExec x = new(ref battle, battleRules, noChoice);
    Check(BlitzKernel.ApplyPowerTargeted(ref x, BlitzLimits.FirstEnemyOwner, BlitzLimits.PlayerOwner,
        sandpit, 4, BlitzLimits.FirstEnemyOwner) == 4, "沙坑首次施加应为 4 层");
    Check(BlitzKernel.ApplyPowerTargeted(ref x, BlitzLimits.FirstEnemyOwner, BlitzLimits.PlayerOwner,
        sandpit, 4, BlitzLimits.FirstEnemyOwner) == 4, "沙坑第二次施加应为独立实例");
    Check(BlitzKernel.GetAmount(battle, BlitzLimits.FirstEnemyOwner, sandpit) == 4,
        "GetAmount 读取第一个沙坑实例应为 4 层");
    int first = BlitzKernel.FindPower(battle, BlitzLimits.FirstEnemyOwner, sandpit);
    int last = BlitzKernel.FindLastPower(battle, BlitzLimits.FirstEnemyOwner, sandpit);
    Check(first != last && battle.Powers[first].Target == BlitzLimits.PlayerOwner
        && battle.Powers[last].Target == BlitzLimits.PlayerOwner,
        "沙坑的两个实例都应记录玩家目标");
    BlitzKernel.IncrementSandpitTargeting(ref x, BlitzLimits.PlayerOwner);
    Check(battle.Powers[first].Amount == 5 && battle.Powers[last].Amount == 4,
        "FranticEscape 应只增加第一个沙坑实例");
    BlitzKernel.SetPowerAmountAt(ref battle, first, 1);
    Check(battle.Powers[first].Amount == 1 && battle.Powers[last].Amount == 4,
        "沙坑计数应只改访问到的实例");
    BlitzKernel.SetPowerAmountAt(ref battle, first, 0);
    Check(battle.PowerCount == 1 && battle.Powers[0].Amount == 4,
        "沙坑归零应只移除该实例");

    // Fixed option list: exactly one bit is selected and the request carries the option values.
    BlitzBattle optionBattle = NewBattle();
    BlitzAction optionAction = BlitzAction.EndTurn().WithChoice(0b10);
    BlitzExec optionExec = new(ref optionBattle, battleRules, optionAction);
    Span<BlitzCard> options = stackalloc BlitzCard[2];
    options[0] = BlitzKernel.CreateCard(battleRules, BlitzRegistry.CardId<Disintegration>());
    options[1] = BlitzKernel.CreateCard(battleRules, BlitzRegistry.CardId<MindRot>());
    Check(BlitzKernel.ChooseOption(ref optionExec, BlitzChoiceEffect.ApplyKnowledgeCurse, options, out int selected),
        "固定选项选择应被动作满足");
    Check(selected == 1 && !optionExec.NeedsChoice, "固定选项应选中第二个选项");
    BlitzExec missing = new(ref optionBattle, battleRules, default);
    Check(!BlitzKernel.ChooseOption(ref missing, BlitzChoiceEffect.ApplyKnowledgeCurse, options, out _),
        "动作没有选择时固定选项应返回 NeedsChoice");
    Check(missing.NeedsChoice && missing.Request.Kind == BlitzChoiceKind.SelectOption
        && missing.Request.OptionCount == 2 && missing.Request.MinCount == 1 && missing.Request.MaxCount == 1,
        "固定选项请求应为 2 项单选");
    BlitzExec invalid = new(ref optionBattle, battleRules, BlitzAction.EndTurn().WithChoice(0b11));
    bool rejected = false;
    try
    {
        BlitzKernel.ChooseOption(ref invalid, BlitzChoiceEffect.ApplyKnowledgeCurse, options, out _);
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }
    Check(rejected, "固定选项选择两个或零个选项应被拒绝");

    // EntropicBrew: out-of-combat pool generation fills an open slot with freeFromEntropic set; using
    // that generated potion records one generated use and clears the slot's free bit.
    BlitzRules rules = MbPotionRules();
    Check(BlitzRegistry.TryPotion("ENTROPIC_BREW", out ushort brew), "缺少 ENTROPIC_BREW");
    Check(BlitzRegistry.TryPotion("FIRE_POTION", out ushort fire), "缺少 FIRE_POTION");
    BlitzBattle potionBattle = NewBattle();
    potionBattle.PotionSlotCount = 1;
    potionBattle.Potions[0] = (ushort)(brew + 1);
    // The pool has a single Common potion; this recorded state's first rarity roll is 0.875.
    potionBattle.RngStream(BlitzRngStream.CombatPotionGeneration) = BlitzRngState.FromSerializable(
        0, 0x161922C645CE50E8, 0xAD760CAFA1697B60, 0x3501FF44902CA50D, 0x417CB9A826D831DF);
    BlitzKernel.Step(ref potionBattle, BlitzAction.UsePotion(0, BlitzLimits.NoOwner), rules, out _);
    Check(potionBattle.Potions[0] == (ushort)(fire + 1) && potionBattle.Potions[0] != brew + 1,
        "EntropicBrew 应把随机战斗外药水放入空槽");
    Check((potionBattle.Counter(BlitzCounter.EntropicFreePotionMask) & 0b1) != 0,
        "EntropicBrew 生成的药水槽应标记为 free");
    int enemyHp = potionBattle.Enemies[0].Creature.CurrentHp;
    BlitzKernel.Step(ref potionBattle, BlitzAction.UsePotion(0, BlitzLimits.FirstEnemyOwner), rules, out _);
    Check(potionBattle.Potions[0] == 0 && potionBattle.Counter(BlitzCounter.GeneratedPotionUseCount) == 1,
        "使用免费药水应记录一次生成药水使用");
    Check(potionBattle.Counter(BlitzCounter.GeneratedPotionStrategicCost) == 0,
        "免费药水不应计入生成药水战略成本");
    Check(potionBattle.Counter(BlitzCounter.EntropicFreePotionMask) == 0, "使用后应清除免费位");
    Check(potionBattle.Enemies[0].Creature.CurrentHp < enemyHp, "免费药水效果应正常结算");
}

static BlitzRules MbPotionRules()
{
    BlitzRulesBuilder builder = new() { BaseMaxEnergy = 3, HasEncounter = true, BaseOrbSlotCount = 0 };
    Check(BlitzRegistry.TryPotion("ENTROPIC_BREW", out ushort brew), "缺少 ENTROPIC_BREW");
    Check(BlitzRegistry.TryPotion("FIRE_POTION", out ushort fire), "缺少 FIRE_POTION");
    builder.SetPotion(brew, new BlitzPotionStats
    {
        Target = BlitzTargetType.Self, Rarity = BlitzPotionRarity.Rare, StrategicCost = 0,
        Vars = [], VarProps = [],
    });
    builder.SetPotion(fire, new BlitzPotionStats
    {
        Target = BlitzTargetType.AnyEnemy, Rarity = BlitzPotionRarity.Common, StrategicCost = 9,
        Vars = [20m], VarProps = [BlitzValueProps.None],
    });
    builder.SetPotionPool([fire]);
    builder.SetOutOfCombatPotionPool([fire]);
    builder.SetRootPotion(0, brew);
    BlitzRules built = builder.Build();
    return built;
}

static BlitzRules SliceRules()
{
    BlitzRulesBuilder builder = new()
    {
        BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first", "second", "third"], BaseOrbSlotCount = 3,
        // The CX contracts install PerfectFit/SlumberingEssence on hand/discard cards.
        HasShuffleOrderEnchantment = true, HasBeforeFlushEnchantment = true,
    };
    BlitzRegistry.TryRelic("CRACKED_CORE", out ushort crackedCore);
    builder.SetRelicVars(crackedCore, [1m]);
    BlitzRegistry.TryRelic("RUNIC_CAPACITOR", out ushort runicCapacitor);
    builder.SetRelicVars(runicCapacitor, [3m]);
    BlitzRegistry.TryRelic("SYMBIOTIC_VIRUS", out ushort symbioticVirus);
    builder.SetRelicVars(symbioticVirus, [1m]);
    BlitzRegistry.TryRelic("REPTILE_TRINKET", out ushort reptile);
    builder.SetRelicVars(reptile, [3m]);
    BlitzRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike);
    BlitzRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend);
    BlitzRegistry.TryCard("BASH", out ushort bash);
    BlitzRegistry.TryCard("WHIRLWIND", out ushort whirlwind);
    BlitzRegistry.TryCard("THRASH", out ushort thrash);
    builder.SetCard(strike, 0, Stats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 1, [6m]));
    builder.SetCard(thrash, 0, Stats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 1, [4m]));
    builder.SetCard(thrash, 1, Stats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 1, [6m]));
    builder.HasDecimalCardVars = true;
    builder.SetCard(defend, 0, Stats(BlitzCardType.Skill, BlitzTargetType.Self, 1, [5m]));
    builder.SetCard(bash, 0, Stats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 2, [8m, 2m]));
    builder.SetCard(whirlwind, 0, Stats(BlitzCardType.Attack, BlitzTargetType.AllEnemies, 0, [5m], costsX: true));
    BlitzRegistry.TryCard("FETCH", out ushort fetch);
    builder.SetCard(fetch, 0, Stats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 0, [3m, 1m]));
    // DX3: Dampen's level-0 rules and the Rampage accumulator restore.
    BlitzRegistry.TryCard("RAMPAGE", out ushort rampage);
    builder.SetCard(rampage, 0, Stats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 1, [10m, 5m]));
    builder.SetCard(rampage, 1, Stats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 1, [10m, 8m]));
    // RG: the forge kernel creates SOVEREIGN_BLADE; the star-cost contract uses a synthetic card.
    BlitzRegistry.TryCard("SOVEREIGN_BLADE", out ushort blade);
    builder.SetCard(blade, 0, Stats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 2, [10m]));
    builder.SetCard(blade, 1, Stats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 2, [13m]));
    BlitzRegistry.TryCard("RG_TEST_STAR_CARD", out ushort starCard);
    builder.SetCard(starCard, 0, Stats(BlitzCardType.Skill, BlitzTargetType.Self, 0, [0m], starCost: 2));
    // LR: choice, transform, history-counter and VoidForm cards used by the K0 contracts.
    void LrCard(string id, BlitzCardType type, BlitzTargetType target, int cost, decimal[] vars,
        int starCost = -1)
    {
        BlitzRegistry.TryCard(id, out ushort card);
        builder.SetCard(card, 0, Stats(type, target, cost, vars, starCost: starCost));
        builder.SetCard(card, 1, Stats(type, target, cost, vars, starCost: starCost));
    }
    LrCard("TYRANNY", BlitzCardType.Power, BlitzTargetType.Self, 1, []);
    LrCard("FOREGONE_CONCLUSION", BlitzCardType.Skill, BlitzTargetType.Self, 1, [2m]);
    LrCard("CHARGE", BlitzCardType.Skill, BlitzTargetType.Self, 1, [2m]);
    LrCard("GUARDS", BlitzCardType.Skill, BlitzTargetType.Self, 2, []);
    LrCard("BEGONE", BlitzCardType.Skill, BlitzTargetType.Self, 1, []);
    LrCard("HEIRLOOM_HAMMER", BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 2, [20m, 1m]);
    LrCard("DECISIONS_DECISIONS", BlitzCardType.Skill, BlitzTargetType.Self, 0, [3m, 3m], starCost: 6);
    LrCard("MINION_DIVE_BOMB", BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 0, [13m]);
    LrCard("MINION_SACRIFICE", BlitzCardType.Skill, BlitzTargetType.Self, 0, [7m]);
    LrCard("MINION_STRIKE", BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 0, [6m, 1m]);
    LrCard("SUPERMASSIVE", BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 1, [5m, 3m]);
    LrCard("BEAT_INTO_SHAPE", BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 1, [5m, 5m, 5m]);
    LrCard("KINGLY_KICK", BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 4, [27m]);
    LrCard("KINGLY_PUNCH", BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 1, [8m, 4m]);
    LrCard("VOID_FORM", BlitzCardType.Power, BlitzTargetType.Self, 3, [2m]);
    // TS: the two turn-start choice sources used by the turn-setup fixtures. ToastyMittens exhausts
    // one hand card then gains Strength; ChoicesParadox offers a generated character card option set.
    BlitzRegistry.TryRelic("TOASTY_MITTENS", out ushort toastyMittens);
    builder.SetRelicVars(toastyMittens, [2m]);
    BlitzRegistry.TryRelic("CHOICES_PARADOX", out ushort choicesParadox);
    builder.SetRelicVars(choicesParadox, [3m]);
    builder.SetCardPool(BlitzCardPools.CharacterAll, [strike, defend, bash]);

    // MB: Knowledge Demon's fixed curse options only need canonical card values.
    BlitzRegistry.TryCard("DISINTEGRATION", out ushort disintegration);
    builder.SetCard(disintegration, 0, Stats(BlitzCardType.Status, BlitzTargetType.Self, -1, []));
    BlitzRegistry.TryCard("MIND_ROT", out ushort mindRot);
    builder.SetCard(mindRot, 0, Stats(BlitzCardType.Status, BlitzTargetType.Self, -1, []));
    // CX: a 0-cost skill whose OnPlay removes another hand card. The Imbued contract uses it to make a
    // captured card disappear before its auto-play turn, which must be an explicit boundary.
    BlitzRegistry.TryCard("CX_TEST_IMBUED_REMOVER", out ushort cxRemover);
    builder.SetCard(cxRemover, 0, Stats(BlitzCardType.Skill, BlitzTargetType.Self, 0, []));
    BlitzRegistry.TryPower("VULNERABLE_POWER", out ushort vulnerable);
    builder.SetPowerVars(vulnerable, [1.5m]);
    BlitzRegistry.TryMonster("NIBBIT", out ushort nibbit);
    builder.RootEnemyCount = 1;
    builder.EncounterSlots = ["first", "second", "third"];
    builder.SetMonster(0, new BlitzMonsterRules
    {
        MonsterId = nibbit,
        Nodes =
        [
            new BlitzMonsterNode { NativeId = "INIT_MOVE", Kind = BlitzMonsterNodeKind.ConditionalBranch, ConditionalTarget = 1 },
            new BlitzMonsterNode { NativeId = "BUTT_MOVE", Kind = BlitzMonsterNodeKind.Move, FollowUp = 2, Attacks = [new(12, 1)] },
            new BlitzMonsterNode { NativeId = "SLICE_MOVE", Kind = BlitzMonsterNodeKind.Move, FollowUp = 3, Attacks = [new(6, 1)] },
            new BlitzMonsterNode { NativeId = "HISS_MOVE", Kind = BlitzMonsterNodeKind.Move, FollowUp = 1 },
            new BlitzMonsterNode
            {
                NativeId = "STUNNED", Kind = BlitzMonsterNodeKind.Move, MustPerformOnceBeforeTransitioning = true,
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

    static BlitzCardStats Stats(BlitzCardType type, BlitzTargetType target, int cost, decimal[] vars,
        bool costsX = false, int starCost = -1) => new()
    {
        Type = type, Target = target, Rarity = BlitzCardRarity.Basic, EnergyCost = cost, CostsX = costsX,
        StarCost = starCost, StarCostsX = false, Keywords = BlitzKeywords.None, MaxUpgradeLevel = 1, Vars = vars,
    };
}



// ID: card-instance ledgers and instance powers. Monologue's instance Strength, SwordSage's per-blade
// applied replay bonus, Nightmare's detached selection, Orbit's energy remainder and the
// UnsettlingLamp latch are the stable-boundary contract; Fetch's played bit and Vambrace's trigger are
// per-card/per-play values and are asserted through their kernel setters.
static void CheckIdKernel(BlitzRules rules)
{
    Check(BlitzRegistry.TryCard("SOVEREIGN_BLADE", out ushort blade), "缺少 SOVEREIGN_BLADE 定义");
    Check(BlitzRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike), "缺少 STRIKE_IRONCLAD 定义");
    Check(BlitzRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD 定义");
    Check(BlitzRegistry.TryCard("FETCH", out ushort fetch), "缺少 FETCH 定义");
    Check(BlitzRegistry.TryPower("MONOLOGUE_POWER", out ushort monologue), "缺少 MONOLOGUE_POWER");
    Check(BlitzRegistry.TryPower("STRENGTH_POWER", out ushort strength), "缺少 STRENGTH_POWER");
    Check(BlitzRegistry.TryPower("SWORD_SAGE_POWER", out ushort swordSage), "缺少 SWORD_SAGE_POWER");
    Check(BlitzRegistry.TryPower("NIGHTMARE_POWER", out ushort nightmare), "缺少 NIGHTMARE_POWER");
    Check(BlitzRegistry.TryPower("ORBIT_POWER", out ushort orbit), "缺少 ORBIT_POWER");
    Check(BlitzRegistry.TryPower("DRAW_CARDS_NEXT_TURN_POWER", out ushort drawNext), "缺少 DRAW_CARDS_NEXT_TURN_POWER");
    Check(BlitzRegistry.TryPower("WEAK_POWER", out ushort weak), "缺少 WEAK_POWER");
    Check(BlitzRegistry.TryPower("VULNERABLE_POWER", out ushort vulnerable), "缺少 VULNERABLE_POWER");
    Check(BlitzRegistry.TryRelic("UNSETTLING_LAMP", out ushort lamp), "缺少 UNSETTLING_LAMP");

    // Monologue: two instances keep separate Strength/StrengthApplied rows; a play applies both and the
    // side turn end removes each and reverts its applied Strength.
    BlitzBattle monologueBattle = NewBattle();
    BlitzPiles.Append(ref monologueBattle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, strike));
    BlitzExec monologueExec = new(ref monologueBattle, rules, default);
    BlitzKernel.ApplyPower(ref monologueExec, BlitzLimits.PlayerOwner, monologue, 1, BlitzLimits.PlayerOwner,
        BlitzLimits.PlayerOwner);
    BlitzKernel.ApplyPower(ref monologueExec, BlitzLimits.PlayerOwner, monologue, 1, BlitzLimits.PlayerOwner,
        BlitzLimits.PlayerOwner);
    Check(monologueBattle.PowerCount == 2, "Monologue 应有两个实例行");
    monologueBattle.Powers[0].Aux0 = 3;
    monologueBattle.Powers[1].Aux0 = 5;
    BlitzKernel.ManualPlay(ref monologueExec, 0, BlitzKernel.EnemyOwner(0));
    Check(BlitzKernel.GetAmount(monologueBattle, BlitzLimits.PlayerOwner, strength) == 8,
        "Monologue 应在出牌后施加两个实例的 Strength");
    Check(monologueBattle.Powers[0].Aux1 == 3 && monologueBattle.Powers[1].Aux1 == 5,
        "Monologue 的 StrengthApplied 应按实例累计");
    // EndTurnPowerSupport.Batch048 visits each instance; removing the first shifts the second to row 0.
    BlitzPowerDef monologueDef = BlitzRegistry.Power(monologue);
    monologueDef.EndTurnBatch048(ref monologueExec, new BlitzListener(BlitzListenerKind.Power, 0,
        BlitzLimits.PlayerOwner), BlitzSide.Player, participates: true);
    monologueDef.EndTurnBatch048(ref monologueExec, new BlitzListener(BlitzListenerKind.Power, 0,
        BlitzLimits.PlayerOwner), BlitzSide.Player, participates: true);
    Check(BlitzKernel.GetAmount(monologueBattle, BlitzLimits.PlayerOwner, monologue) == 0
          && BlitzKernel.GetAmount(monologueBattle, BlitzLimits.PlayerOwner, strength) == 0,
        "Monologue 应在玩家回合末移除并归还 Strength");

    // SwordSage: the per-blade applied bonus is a card value; normalization follows the power amount.
    BlitzBattle sageBattle = NewBattle();
    BlitzCard sageBlade = BlitzKernel.CreateCard(rules, blade);
    BlitzPiles.Append(ref sageBattle, BlitzPile.Hand, sageBlade);
    BlitzExec sageExec = new(ref sageBattle, rules, default);
    BlitzKernel.ApplyPower(ref sageExec, BlitzLimits.PlayerOwner, swordSage, 2, BlitzLimits.PlayerOwner);
    BlitzKernel.NormalizeCardAfflictions(ref sageExec);
    Check(sageBattle.Cards[0].ReplayCount == 2 && sageBattle.Cards[0].Private == 2,
        $"SwordSage 应把重放加到 2 并记录已应用值，实际 {sageBattle.Cards[0].ReplayCount}/{sageBattle.Cards[0].Private}");
    BlitzKernel.SetPowerAmount(ref sageBattle, BlitzLimits.PlayerOwner, swordSage, 0);
    BlitzKernel.NormalizeCardAfflictions(ref sageExec);
    Check(sageBattle.Cards[0].ReplayCount == 0 && sageBattle.Cards[0].Private == 0,
        "SwordSage 移除应回退已应用的重放");
    BlitzCard sageClone = BlitzKernel.CloneCard(sageBattle.Cards[0]);
    Check(sageClone.Private == BlitzLimits.UninitializedLedger,
        "克隆卡应重置实例账本与 Fetch 位");

    // Nightmare: the selected card lives in DetachedCards; BeforeHandDraw clones it Amount times,
    // removes the instance and frees the slot.
    BlitzBattle nightmareBattle = NewBattle();
    BlitzExec nightmareExec = new(ref nightmareBattle, rules, default);
    BlitzKernel.ApplyPower(ref nightmareExec, BlitzLimits.PlayerOwner, nightmare, 3, BlitzLimits.PlayerOwner,
        BlitzLimits.PlayerOwner);
    BlitzCard selected = BlitzKernel.CreateCard(rules, strike);
    int slot = BlitzKernel.AllocateDetachedCard(ref nightmareBattle, selected);
    nightmareBattle.Powers[0].Aux0 = slot + 1;
    BlitzKernel.BeforeHandDraw(ref nightmareExec);
    Check(nightmareBattle.HandCount == 3, $"Nightmare 应生成 3 张选定卡的克隆，实际手牌 {nightmareBattle.HandCount}");
    Check(BlitzKernel.GetAmount(nightmareBattle, BlitzLimits.PlayerOwner, nightmare) == 0,
        "Nightmare 结算后应移除实例");
    Check(nightmareBattle.DetachedCards[slot].Id == 0, "Nightmare 实例移除应释放 detached 槽位");
    for (int index = 0; index < nightmareBattle.HandCount; index++)
    {
        Check(nightmareBattle.Cards[index].Id == strike && nightmareBattle.Cards[index].IsClone,
            "Nightmare 生成的卡应是选定卡的克隆");
    }

    // Orbit: spent energy accumulates in Aux0; each full 4 grants Amount energy.
    BlitzBattle orbitBattle = NewBattle();
    orbitBattle.Energy = 0;
    BlitzExec orbitExec = new(ref orbitBattle, rules, default);
    BlitzKernel.ApplyPower(ref orbitExec, BlitzLimits.PlayerOwner, orbit, 2, BlitzLimits.PlayerOwner,
        BlitzLimits.PlayerOwner);
    BlitzCard energyCard = BlitzKernel.CreateCard(rules, strike);
    BlitzHooks.AfterEnergySpent(ref orbitExec, energyCard, 3);
    Check(orbitBattle.Powers[0].Aux0 == 3 && orbitBattle.Energy == 0,
        $"Orbit 3 点能量应只留下余数 3，实际余数 {orbitBattle.Powers[0].Aux0} 能量 {orbitBattle.Energy}");
    BlitzHooks.AfterEnergySpent(ref orbitExec, energyCard, 1);
    Check(orbitBattle.Powers[0].Aux0 == 0 && orbitBattle.Energy == 2,
        $"Orbit 满 4 点应给 2 能量，实际余数 {orbitBattle.Powers[0].Aux0} 能量 {orbitBattle.Energy}");

    // PaleBlueDot: the fifth completed play latches and grants next-turn draws once.
    BlitzBattle dotBattle = NewBattle();
    BlitzPiles.Append(ref dotBattle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, strike));
    dotBattle.SetCounter(BlitzCounter.CardsPlayedThisTurn, 4);
    BlitzExec dotExec = new(ref dotBattle, rules, default);
    BlitzKernel.ApplyPower(ref dotExec, BlitzLimits.PlayerOwner,
        BlitzRegistry.PowerId<PaleBlueDotPower>(), 2, BlitzLimits.PlayerOwner);
    BlitzKernel.ManualPlay(ref dotExec, 0, BlitzKernel.EnemyOwner(0));
    Check(dotBattle.Powers[0].Aux0 == 1, "PaleBlueDot 第五次出牌应锁存");
    Check(BlitzKernel.GetAmount(dotBattle, BlitzLimits.PlayerOwner, drawNext) == 2,
        "PaleBlueDot 应给出 2 层下回合抽牌");

    // UnsettlingLamp: the first card-sourced debuff is doubled once; the internal temporary type is
    // exempt; completing the card power scope latches and clears the transient trigger.
    BlitzBattle lampBattle = NewBattle();
    lampBattle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    lampBattle.RelicCount = 1;
    lampBattle.Relics[0] = new BlitzRelic { Id = lamp };
    BlitzExec lampExec = new(ref lampBattle, rules, default);
    lampExec.CardPowerSourceActive = true;
    lampExec.CardPowerSourceSerial = ++lampExec.CardPowerSourceSerialCounter;
    int weakApplied = BlitzKernel.ApplyPower(ref lampExec, BlitzKernel.EnemyOwner(0), weak, 1,
        BlitzLimits.PlayerOwner);
    Check(weakApplied == 2, $"UnsettlingLamp 应把首个减益翻倍，实际 {weakApplied}");
    int vulnerableApplied = BlitzKernel.ApplyPower(ref lampExec, BlitzKernel.EnemyOwner(0), vulnerable, 1,
        BlitzLimits.PlayerOwner);
    Check(vulnerableApplied == 2, $"同一张牌的第二个减益也应翻倍，实际 {vulnerableApplied}");
    BlitzKernel.CompleteCardPowerSource(ref lampExec);
    Check(lampBattle.Relics[0].Counter == 1 && lampExec.LampTriggerSerial == 0,
        "UnsettlingLamp 完成卡牌 Power 作用域后应锁存并清空触发");
    lampExec.CardPowerSourceSerial = ++lampExec.CardPowerSourceSerialCounter;
    int weakAgain = BlitzKernel.ApplyPower(ref lampExec, BlitzKernel.EnemyOwner(0), weak, 1,
        BlitzLimits.PlayerOwner);
    Check(weakAgain == 1, $"已锁存的 UnsettlingLamp 不应再翻倍，实际 {weakAgain}");

    // Fetch: completing a play sets the per-card bit; a clone starts clear.
    BlitzBattle fetchBattle = NewBattle();
    BlitzPiles.Append(ref fetchBattle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, fetch));
    BlitzExec fetchExec = new(ref fetchBattle, rules, default);
    BlitzKernel.ManualPlay(ref fetchExec, 0, BlitzKernel.EnemyOwner(0));
    int fetchPosition = BlitzPiles.Start(fetchBattle, BlitzPile.Discard);
    Check(fetchBattle.DiscardCount == 1 && fetchBattle.Cards[fetchPosition].Id == fetch,
        "Fetch 应完成并进入弃牌堆");
    Check((fetchBattle.Cards[fetchPosition].Private & BlitzKernel.FetchPlayedBit) != 0,
        "Fetch 完成一次出牌后应置本回合已打出位");
    Check((BlitzKernel.CloneCard(fetchBattle.Cards[fetchPosition]).Private & BlitzKernel.FetchPlayedBit) == 0,
        "Fetch 的克隆不应继承已打出位");

    // Vambrace: the first card-sourced block of the combat is doubled; the relic latches on the
    // matching AfterCardPlayed and never doubles again.
    Check(BlitzRegistry.TryRelic("VAMBRACE", out ushort vambrace), "缺少 VAMBRACE");
    BlitzBattle vambraceBattle = NewBattle();
    vambraceBattle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    vambraceBattle.RelicCount = 1;
    vambraceBattle.Relics[0] = new BlitzRelic { Id = vambrace };
    BlitzExec vambraceExec = new(ref vambraceBattle, rules, default);
    vambraceExec.Card = BlitzKernel.CreateCard(rules, defend);
    decimal doubled = BlitzKernel.GainBlock(ref vambraceExec, BlitzLimits.PlayerOwner, 5m, BlitzValueProps.Move,
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
    BlitzRules rules = LsRules();
    Check(BlitzRegistry.TryCard("ECHOING_SLASH", out ushort echoing), "缺少 ECHOING_SLASH");
    Check(BlitzRegistry.TryCard("KNIFE_TRAP", out ushort knifeTrap), "缺少 KNIFE_TRAP");
    Check(BlitzRegistry.TryCard("SHIV", out ushort shiv), "缺少 SHIV");
    Check(BlitzRegistry.TryCard("MOMENTUM_STRIKE", out ushort momentum), "缺少 MOMENTUM_STRIKE");
    Check(BlitzRegistry.TryCard("MISERY", out ushort misery), "缺少 MISERY");
    Check(BlitzRegistry.TryPower("POISON_POWER", out ushort poison), "缺少 POISON_POWER");
    Check(BlitzRegistry.TryPower("PIERCING_WAIL_POWER", out ushort piercing), "缺少 PIERCING_WAIL_POWER");
    Check(BlitzRegistry.TryPower("DEXTERITY_POWER", out ushort dexterity), "缺少 DEXTERITY_POWER");
    Check(BlitzRegistry.TryPower("HELICAL_DART_POWER", out ushort helicalPower), "缺少 HELICAL_DART_POWER");
    Check(BlitzRegistry.TryRelic("SNECKO_SKULL", out ushort skull), "缺少 SNECKO_SKULL");
    Check(BlitzRegistry.TryRelic("HELICAL_DART", out ushort dart), "缺少 HELICAL_DART");

    // ModifyPowerAmountGiven: SneckoSkull adds 1 to Poison the player gives.
    BlitzBattle battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = skull };
    BlitzExec x = new(ref battle, rules, default);
    int applied = BlitzKernel.ApplyPower(ref x, 2, poison, 2, BlitzLimits.PlayerOwner);
    Check(applied == 3, $"SneckoSkull 应把毒 2 变成 3，实际 {applied}");
    Check(BlitzKernel.GetAmount(battle, 2, poison) == 3, "SneckoSkull 后的毒层数应为 3");

    // EchoingSlash: the first wave kills one enemy, which adds a second wave.
    battle = NewBattle();
    battle.EnemyCount = 2;
    battle.RosterCount = 2;
    battle.Roster[1] = 1;
    battle.Enemies[0].Creature.CurrentHp = 5;
    battle.Enemies[0].Creature.MaxHp = 5;
    battle.Enemies[1].Creature.CurrentHp = 100;
    battle.Enemies[1].Creature.MaxHp = 100;
    battle.Enemies[1].Flags = BlitzEnemyFlags.InCombat;
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, echoing));
    x = new(ref battle, rules, default);
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, BlitzLimits.NoOwner), rules, out _);
    Check(battle.Enemies[0].Creature.IsDead, "EchoingSlash 第一波应击杀低血量敌人");
    Check(battle.Enemies[1].Creature.CurrentHp == 80,
        $"EchoingSlash 击杀后应追加一波，实际敌人生命 {battle.Enemies[1].Creature.CurrentHp}");

    // KnifeTrap: two byte-equal Shivs in the exhaust pile are both auto-played (duplicate copies
    // must not make the second search pick the first again).
    battle = NewBattle();
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, knifeTrap));
    BlitzPiles.Append(ref battle, BlitzPile.Exhaust, BlitzKernel.CreateCard(rules, shiv));
    BlitzPiles.Append(ref battle, BlitzPile.Exhaust, BlitzKernel.CreateCard(rules, shiv));
    x = new(ref battle, rules, default);
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, 2), rules, out _);
    Check(battle.Enemies[0].Creature.CurrentHp == 92,
        $"KnifeTrap 应自动打出两张 Shiv（12 伤害），实际生命 {battle.Enemies[0].Creature.CurrentHp}");
    Check(battle.ExhaustCount == 2, "两张 Shiv 打出后应回到消耗堆");

    // Upgraded KnifeTrap upgrades every exhausted Shiv before playing it.
    battle = NewBattle();
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, knifeTrap, 1));
    BlitzPiles.Append(ref battle, BlitzPile.Exhaust, BlitzKernel.CreateCard(rules, shiv));
    BlitzPiles.Append(ref battle, BlitzPile.Exhaust, BlitzKernel.CreateCard(rules, shiv));
    x = new(ref battle, rules, default);
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, 2), rules, out _);
    for (int index = 0; index < battle.ExhaustCount; index++)
    {
        BlitzCard card = battle.Cards[BlitzPiles.Start(battle, BlitzPile.Exhaust) + index];
        Check(card.Id == shiv && card.Upgrade == 1, "升级版 KnifeTrap 应把每张 Shiv 升级");
    }

    // Drawn-card discard positions are an explicit assumption: a hook that moved the drawn card
    // leaves a different byte-equal copy at the slot, which must be rejected rather than discarded.
    battle = NewBattle();
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, shiv));
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, echoing));
    Span<BlitzCard> drawn = stackalloc BlitzCard[1];
    Span<int> positions = stackalloc int[1];
    drawn[0] = BlitzKernel.CreateCard(rules, knifeTrap);
    positions[0] = 0;
    Span<int> selected = stackalloc int[1];
    selected[0] = 0;
    x = new(ref battle, rules, default);
    bool positionUnsupported = false;
    try
    {
        BlitzCardEffects.DiscardDrawn(ref x, drawn, positions, selected);
    }
    catch (BlitzUnsupportedException)
    {
        positionUnsupported = true;
    }
    Check(positionUnsupported, "抓牌位置不匹配时应抛 BlitzUnsupportedException");

    // Burst adds one replay to the next Skill; the block card counter and block gain both run twice.
    battle = NewBattle();
    battle.Energy = 9;
    Check(BlitzRegistry.TryCard("BURST", out ushort burstCard), "缺少 BURST");
    Check(BlitzRegistry.TryCard("MIRAGE", out ushort mirageCard), "缺少 MIRAGE");
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, burstCard));
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, mirageCard));
    x = new(ref battle, rules, default);
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, BlitzLimits.NoOwner), rules, out _);
    Check(BlitzKernel.GetAmount(battle, 0, BlitzRegistry.PowerId<BurstPower>()) == 1,
        $"Burst 应施加 1 层 BurstPower，实际 {BlitzKernel.GetAmount(battle, 0, BlitzRegistry.PowerId<BurstPower>())}");
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, BlitzLimits.NoOwner), rules, out _);
    Check(battle.Counter(BlitzCounter.BlockCardsPlayedThisTurn) == 2,
        $"Burst 应重放 Mirage 两次，格挡牌计数实际 {battle.Counter(BlitzCounter.BlockCardsPlayedThisTurn)}");

    battle = NewBattle();
    battle.Energy = 9;
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, burstCard));
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, mirageCard));
    x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, 0, dexterity, 2, 0);
    BlitzKernel.ApplyPower(ref x, 0, BlitzRegistry.PowerId<ShadowmeldPower>(), 1, 0);
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, BlitzLimits.NoOwner), rules, out _);
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, BlitzLimits.NoOwner), rules, out _);
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
    battle.Enemies[1].Flags = BlitzEnemyFlags.InCombat;
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, misery));
    x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, 2, piercing, 1, BlitzLimits.PlayerOwner);
    x = new(ref battle, rules, default);
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, 2), rules, out _);
    Check(BlitzKernel.GetAmount(battle, 3, piercing) == 1, "Misery 应把临时减益克隆到其他敌人");
    Check(BlitzKernel.GetAmount(battle, 3, dexterity) == 0, "PiercingWail 的内部力量是增益，不应被合并");

    // MomentumStrike sets its own cost to 0 this combat; PlayPosition is valid during OnPlay.
    battle = NewBattle();
    battle.Energy = 9;
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, momentum));
    x = new(ref battle, rules, default);
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, 2), rules, out _);
    bool found = false;
    for (int index = 0; index < battle.DiscardCount; index++)
    {
        BlitzCard card = battle.Cards[BlitzPiles.Start(battle, BlitzPile.Discard) + index];
        if (card.Id != momentum)
            continue;
        found = true;
        Check(card.EnergyModifierCount == 1, "MomentumStrike 应留下一条费用修饰");
        Check(BlitzKernel.EnergyCostValueWithModifiers(battle, rules, card, BlitzPile.Discard) == 0,
            "MomentumStrike 本场费用应为 0");
    }
    Check(found, "MomentumStrike 应进入弃牌堆");

    // HelicalDart: a Shiv play applies 1 temporary Dexterity.
    battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = dart };
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, shiv));
    x = new(ref battle, rules, default);
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, 2), rules, out _);
    Check(BlitzKernel.GetAmount(battle, 0, dexterity) == 1, "HelicalDart 应给 1 敏捷");
    Check(BlitzKernel.GetAmount(battle, 0, helicalPower) == 1, "HelicalDart 应施加临时敏捷层");
}

static BlitzRules LsRules()
{
    BlitzRulesBuilder builder = new() { BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first"], RootEnemyCount = 0 };
    BlitzRegistry.TryCard("ECHOING_SLASH", out ushort echoing);
    BlitzRegistry.TryCard("KNIFE_TRAP", out ushort knifeTrap);
    BlitzRegistry.TryCard("SHIV", out ushort shiv);
    BlitzRegistry.TryCard("MOMENTUM_STRIKE", out ushort momentum);
    BlitzRegistry.TryCard("MISERY", out ushort misery);
    BlitzRegistry.TryRelic("SNECKO_SKULL", out ushort skull);
    BlitzRegistry.TryRelic("HELICAL_DART", out ushort dart);
    builder.SetCard(echoing, 0, LsStats(BlitzCardType.Attack, BlitzTargetType.AllEnemies, 1, [10m]));
    builder.SetCard(knifeTrap, 0, LsStats(BlitzCardType.Skill, BlitzTargetType.AnyEnemy, 1));
    builder.SetCard(knifeTrap, 1, LsStats(BlitzCardType.Skill, BlitzTargetType.AnyEnemy, 1));
    builder.SetCard(shiv, 0, LsStats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 0, [4m],
        BlitzKeywords.Exhaust, BlitzCardTag.Shiv));
    builder.SetCard(shiv, 1, LsStats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 0, [6m],
        BlitzKeywords.Exhaust, BlitzCardTag.Shiv));
    builder.SetCard(momentum, 0, LsStats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 1, [11m]));
    builder.SetCard(misery, 0, LsStats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 0, [7m]));
    BlitzRegistry.TryCard("BURST", out ushort burst);
    BlitzRegistry.TryCard("MIRAGE", out ushort mirage);
    builder.SetCard(burst, 0, LsStats(BlitzCardType.Skill, BlitzTargetType.Self, 1, [1m]));
    builder.SetCard(mirage, 0, LsStats(BlitzCardType.Skill, BlitzTargetType.Self, 1, [2m, 1m]));
    builder.SetRelicVars(skull, [1m]);
    builder.SetRelicVars(dart, [1m]);
    return builder.Build();

    static BlitzCardStats LsStats(BlitzCardType type, BlitzTargetType target, int cost, decimal[]? vars = null,
        BlitzKeywords keywords = BlitzKeywords.None, BlitzCardTag tags = BlitzCardTag.None) => new()
    {
        Type = type, Target = target, Rarity = BlitzCardRarity.Basic, EnergyCost = cost, CostsX = false,
        StarCost = -1, StarCostsX = false, Keywords = keywords, Tags = tags, MaxUpgradeLevel = 1,
        Vars = vars ?? [],
    };
}

// NB: contracts for the Necrobinder kernel capabilities. Osty's summon/revive ledger, the
// DieForYou redirect and spill-over, the Osty attack dealer, Doom's side-turn-end kill, the
// RightHandHand/Flatten/Flast cost-modifier hooks, EtherealPlays and the UnceasingTop special case.
static void CheckNbKernel()
{
    BlitzRules rules = NbRules();
    Check(BlitzRegistry.TryCard("STRIKE_NECROBINDER", out ushort strike), "缺少 STRIKE_NECROBINDER");
    Check(BlitzRegistry.TryCard("DEFEND_NECROBINDER", out ushort defend), "缺少 DEFEND_NECROBINDER");
    Check(BlitzRegistry.TryCard("BODYGUARD", out ushort bodyguard), "缺少 BODYGUARD");
    Check(BlitzRegistry.TryCard("SOUL", out ushort soul), "缺少 SOUL");
    Check(BlitzRegistry.TryCard("PARSE", out ushort parse), "缺少 PARSE");
    Check(BlitzRegistry.TryCard("FLATTEN", out ushort flatten), "缺少 FLATTEN");
    Check(BlitzRegistry.TryCard("HIGH_FIVE", out ushort highFive), "缺少 HIGH_FIVE");
    Check(BlitzRegistry.TryCard("RIGHT_HAND_HAND", out ushort rightHandHand), "缺少 RIGHT_HAND_HAND");
    Check(BlitzRegistry.TryPower("DIE_FOR_YOU_POWER", out ushort dieForYou), "缺少 DIE_FOR_YOU_POWER");
    Check(BlitzRegistry.TryPower("DOOM_POWER", out ushort doom), "缺少 DOOM_POWER");
    Check(BlitzRegistry.TryPower("OBLIVION_POWER", out ushort oblivion), "缺少 OBLIVION_POWER");
    Check(BlitzRegistry.TryRelic("UNCEASING_TOP", out ushort unceasingTop), "缺少 UNCEASING_TOP");

    // Summon: the first call creates the pet at the requested HP, applies DieForYouPower 1 and consumes
    // one CombatId; a later summon raises both HP values; a dead pet is revived with the new amount.
    BlitzBattle battle = NewBattle();
    battle.NextCombatId = 7;
    BlitzExec x = new(ref battle, rules, default);
    BlitzKernel.SummonOsty(ref x, 5);
    Check((battle.Flags & BlitzBattleFlags.OstyPresent) != 0, "召唤后 Osty 应存在");
    Check(battle.Osty.CurrentHp == 5 && battle.Osty.MaxHp == 5, "首次召唤 HP 应为 5/5");
    Check(battle.NextCombatId == 8, "创建 Osty 应消耗一个 CombatId");
    Check(BlitzKernel.GetAmount(battle, BlitzLimits.OstyOwner, dieForYou) == 1, "首次召唤应获得 1 层 DieForYouPower");
    x = new(ref battle, rules, default);
    BlitzKernel.SummonOsty(ref x, 3);
    Check(battle.Osty.CurrentHp == 8 && battle.Osty.MaxHp == 8, "存活 Osty 召唤应提高最大生命并治疗");
    battle.Osty.CurrentHp = 0;
    x = new(ref battle, rules, default);
    BlitzKernel.SummonOsty(ref x, 4);
    Check(battle.Osty.CurrentHp == 4 && battle.Osty.MaxHp == 4, "死亡 Osty 召唤应按新值复活");
    x = new(ref battle, rules, default);
    BlitzKernel.HealOsty(ref x, 99);
    Check(battle.Osty.CurrentHp == 4, "治疗 Osty 不得超过最大生命");

    // Redirect: powered attack damage above Osty's HP spills over to the player; unpowered damage and
    // a dead pet do not redirect.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    BlitzKernel.SummonOsty(ref x, 8);
    battle.Player.CurrentHp = 50;
    Span<byte> player = stackalloc byte[1];
    player[0] = BlitzLimits.PlayerOwner;
    x = new(ref battle, rules, default);
    BlitzKernel.Damage(ref x, player, 20m, BlitzValueProps.Move, BlitzLimits.NoOwner, false, default);
    Check(battle.Osty.CurrentHp == 0, "Osty 应吸收其全部 HP 的伤害");
    Check(battle.Player.CurrentHp == 38, "Osty 死亡后的溢出伤害应打给玩家");
    Check((battle.Player.Flags & BlitzCreatureFlags.LostHpThisTurn) != 0, "溢出伤害应标记玩家本回合失血");
    battle = NewBattle();
    x = new(ref battle, rules, default);
    BlitzKernel.SummonOsty(ref x, 8);
    battle.Player.CurrentHp = 50;
    x = new(ref battle, rules, default);
    BlitzKernel.Damage(ref x, player, 20m, BlitzValueProps.Move | BlitzValueProps.Unpowered, BlitzLimits.NoOwner,
        false, default);
    Check(battle.Osty.CurrentHp == 8 && battle.Player.CurrentHp == 30, "Unpowered 伤害不得重定向到 Osty");

    // Osty attack: the pet is the dealer, so its attack history is recorded and the player's is not.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    BlitzKernel.SummonOsty(ref x, 8);
    x.Card = BlitzKernel.CreateCard(rules, flatten);
    byte enemy = BlitzKernel.EnemyOwner(0);
    x = new(ref battle, rules, default);
    BlitzKernel.CardAttack(ref x, 6m, 1, BlitzAttackTargeting.Single, enemy, BlitzValueProps.Move,
        BlitzLimits.OstyOwner);
    Check(battle.Enemies[0].Creature.CurrentHp == 94, "Osty 攻击应伤害目标");
    Check(battle.Counter(BlitzCounter.OstyAttacksThisTurn) == 1, "Osty 攻击应计入 Osty 本回合次数");
    Check(battle.Counter(BlitzCounter.PlayerAttacksThisTurn) == 0, "Osty 攻击不得计入玩家攻击次数");

    // Assumption contract: a single-target attack without a target owner is unsupported, not an
    // index crash. K0 pins the BlitzUnsupportedException.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    bool missingTargetThrew = false;
    try
    {
        BlitzKernel.CardAttack(ref x, 6m, 1, BlitzAttackTargeting.Single, BlitzLimits.NoOwner);
    }
    catch (BlitzUnsupportedException)
    {
        missingTargetThrew = true;
    }
    Check(missingTargetThrew, "单体攻击缺少目标应抛 BlitzUnsupportedException");

    // Doom: the enemy-side turn-end hook kills every doomed participant together.
    battle = NewBattle();
    battle.Enemies[0].Creature.CurrentHp = 20;
    x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, enemy, doom, 25, BlitzLimits.PlayerOwner);
    int doomIndex = BlitzKernel.FindPower(battle, enemy, doom);
    Check(doomIndex >= 0, "Doom 应已施加");
    x = new(ref battle, rules, default);
    BlitzListener doomListener = new(BlitzListenerKind.Power, checked((byte)doomIndex), enemy);
    BlitzRegistry.Power(doom).BeforeSideTurnEnd(ref x, doomListener, BlitzSide.Enemy,
        BlitzKernel.ParticipantMask(enemy));
    Check(battle.Enemies[0].Creature.IsDead, "Doom 达到生命值应在敌方回合末击杀");
    Check(battle.RosterCount == 0, "被 Doom 击杀的敌人应离开名单");

    // RightHandHand: any play whose energy value reaches its Energy variable returns the copy from the
    // discard pile to hand.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    BlitzKernel.SummonOsty(ref x, 8);
    battle.Energy = 3;
    battle.Phase = BlitzPlayerPhase.Play;
    battle.Side = BlitzSide.Player;
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, highFive));
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, defend));
    BlitzPiles.Append(ref battle, BlitzPile.Discard, BlitzKernel.CreateCard(rules, rightHandHand));
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, BlitzLimits.NoOwner), rules, out _);
    Check(battle.HandCount == 2, "费用 >= RightHandHand 的 Energy 的打出应把弃牌堆的副本收回手牌");

    // Flatten: after an Osty attack every Flatten copy in a combat pile gets an absolute zero
    // EndOfTurn cost modifier.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    BlitzKernel.SummonOsty(ref x, 8);
    battle.Energy = 3;
    battle.Phase = BlitzPlayerPhase.Play;
    battle.Side = BlitzSide.Player;
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, flatten));
    BlitzPiles.Append(ref battle, BlitzPile.Discard, BlitzKernel.CreateCard(rules, flatten));
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, BlitzKernel.EnemyOwner(0)), rules, out _);
    Check(battle.DiscardCount == 2, "Flatten 应打出并进入弃牌堆");
    int discardStart = BlitzPiles.Start(battle, BlitzPile.Discard);
    Check(battle.Cards[discardStart].EnergyModifierCount == 1
        && battle.Cards[discardStart + 1].EnergyModifierCount == 1,
        "Osty 攻击后每张 Flatten 应各获得一个零费修饰");

    // EtherealPlays and Oblivion: playing an Ethereal Soul counts the play and applies the Oblivion
    // amount as Doom to its owner.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, enemy, oblivion, 5, BlitzLimits.PlayerOwner);
    x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, dieForYou, 1, BlitzLimits.NoOwner);
    battle.Energy = 1;
    battle.Phase = BlitzPlayerPhase.Play;
    battle.Side = BlitzSide.Player;
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, parse));
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, BlitzLimits.NoOwner), rules, out _);
    Check(battle.Counter(BlitzCounter.EtherealPlays) == 1, "打出 Ethereal 牌应计入 EtherealPlays");
    Check(BlitzKernel.GetAmount(battle, enemy, doom) == 5, "Oblivion 应在卡牌打出后施加等量 Doom");

    // UnceasingTop: playing the last hand card with an empty hand and a non-empty draw pile draws one.
    battle = NewBattle();
    battle.Phase = BlitzPlayerPhase.Play;
    battle.Side = BlitzSide.Player;
    battle.Energy = 1;
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = unceasingTop };
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, defend));
    BlitzPiles.Append(ref battle, BlitzPile.Draw, BlitzKernel.CreateCard(rules, strike));
    BlitzKernel.Step(ref battle, BlitzAction.PlayCard(0, BlitzLimits.NoOwner), rules, out _);
    Check(battle.HandCount == 1, "手牌打空后 UnceasingTop 应抽一张");
    Check(battle.Cards[0].Id == strike, "UnceasingTop 应抽到抽牌堆顶");
}

static BlitzRules NbRules()
{
    BlitzRulesBuilder builder = new() { BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first"], RootEnemyCount = 1 };
    BlitzRegistry.TryCard("STRIKE_NECROBINDER", out ushort strike);
    BlitzRegistry.TryCard("DEFEND_NECROBINDER", out ushort defend);
    BlitzRegistry.TryCard("BODYGUARD", out ushort bodyguard);
    BlitzRegistry.TryCard("SOUL", out ushort soul);
    BlitzRegistry.TryCard("PARSE", out ushort parse);
    BlitzRegistry.TryCard("FLATTEN", out ushort flatten);
    BlitzRegistry.TryCard("HIGH_FIVE", out ushort highFive);
    BlitzRegistry.TryCard("RIGHT_HAND_HAND", out ushort rightHandHand);
    builder.SetCard(strike, 0, NbStats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 1, [6m]));
    builder.SetCard(strike, 1, NbStats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 1, [9m]));
    builder.SetCard(defend, 0, NbStats(BlitzCardType.Skill, BlitzTargetType.Self, 1, [5m]));
    builder.SetCard(defend, 1, NbStats(BlitzCardType.Skill, BlitzTargetType.Self, 1, [8m]));
    builder.SetCard(bodyguard, 0, NbStats(BlitzCardType.Skill, BlitzTargetType.Self, 1, [5m]));
    builder.SetCard(soul, 0, NbStats(BlitzCardType.Skill, BlitzTargetType.Self, 0, [2m], keywords: BlitzKeywords.Exhaust));
    builder.SetCard(parse, 0, NbStats(BlitzCardType.Skill, BlitzTargetType.Self, 1, [3m], keywords: BlitzKeywords.Ethereal));
    builder.SetCard(flatten, 0, NbStats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 2, [12m]));
    builder.SetCard(flatten, 1, NbStats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 2, [15m]));
    builder.SetCard(highFive, 0, NbStats(BlitzCardType.Attack, BlitzTargetType.AllEnemies, 2, [11m, 2m]));
    builder.SetCard(rightHandHand, 0, NbStats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 0, [4m, 2m]));
    return builder.Build();

    static BlitzCardStats NbStats(BlitzCardType type, BlitzTargetType target, int cost, decimal[] vars,
        BlitzKeywords keywords = BlitzKeywords.None) => new()
    {
        Type = type, Target = target, Rarity = BlitzCardRarity.Basic, EnergyCost = cost, CostsX = false,
        StarCost = -1, StarCostsX = false, Keywords = keywords, MaxUpgradeLevel = 1, Vars = vars,
    };
}

static BlitzBattle SliceBattle()
{
    BlitzRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike);
    BlitzRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend);
    BlitzRegistry.TryCard("BASH", out ushort bash);
    BlitzRegistry.TryMonster("NIBBIT", out ushort nibbit);
    BlitzBattle battle = new()
    {
        Round = 1, PlayerTurn = 1, Energy = 3, Side = BlitzSide.Player, Phase = BlitzPlayerPhase.Play,
        Flags = BlitzBattleFlags.PlayerHooksActive, MaxHandSize = 10, EnemyCount = 1, RosterCount = 1,
        Player = new BlitzCreature { CurrentHp = 80, MaxHp = 80, Flags = BlitzCreatureFlags.Present },
    };
    battle.Enemies[0] = new BlitzEnemy
    {
        Creature = new BlitzCreature { CurrentHp = 44, MaxHp = 44, Flags = BlitzCreatureFlags.Present },
        MonsterId = nibbit, CombatId = 1, Move = 1, ForcedMove = byte.MaxValue, Flags = BlitzEnemyFlags.InCombat,
    };
    BlitzKernel.LogMonsterMove(ref battle.Enemies[0], 1);
    battle.RngStream(BlitzRngStream.Shuffle) = BlitzRngState.FromSerializable(0, 1, 2, 3, 4);
    battle.RngStream(BlitzRngStream.MonsterAi) = BlitzRngState.FromSerializable(0, 5, 6, 7, 8);
    ushort[] deck = [strike, defend, defend, strike, bash, strike, defend, defend, strike, strike];
    for (int index = 0; index < deck.Length; index++)
    {
        BlitzCard card = new() { Id = deck[index], BaseCost = (sbyte)(deck[index] == bash ? 2 : 1), BaseStarCost = -1, Flags = BlitzCardFlags.DeckCard };
        BlitzPiles.Append(ref battle, index < 5 ? BlitzPile.Hand : BlitzPile.Draw, card);
    }
    return battle;
}

// CX: the four previously missing enchantments. Imbued's turn-1 auto-play (and its explicit boundary
// when a captured card disappears), Slither's draw-time random cost, SlumberingEssence's BeforeFlush
// cost modifier and PerfectFit's ModifyShuffleOrder placement.
static void CheckCxKernel(BlitzRules rules)
{
    Check(BlitzRegistry.TryEnchantment("IMBUED", out ushort imbued), "缺少 IMBUED");
    Check(BlitzRegistry.TryEnchantment("SLITHER", out ushort slither), "缺少 SLITHER");
    Check(BlitzRegistry.TryEnchantment("SLUMBERING_ESSENCE", out ushort slumbering), "缺少 SLUMBERING_ESSENCE");
    Check(BlitzRegistry.TryEnchantment("PERFECT_FIT", out ushort perfectFit), "缺少 PERFECT_FIT");
    Check(BlitzRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD");
    Check(BlitzRegistry.TryCard("CX_TEST_IMBUED_REMOVER", out ushort remover), "缺少 CX_TEST_IMBUED_REMOVER");

    static BlitzCard Enchanted(BlitzRules rules, ushort card, ushort enchantment)
    {
        BlitzCard value = BlitzKernel.CreateCard(rules, card);
        value.EnchantmentId = (byte)(enchantment + 1);
        return value;
    }

    // Imbued: every enchanted card auto-plays on player turn 1, in AllCards order, without paying energy.
    BlitzBattle imbuedBattle = NewBattle();
    imbuedBattle.PlayerTurn = 1;
    imbuedBattle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzPiles.Append(ref imbuedBattle, BlitzPile.Hand, Enchanted(rules, defend, imbued));
    BlitzPiles.Append(ref imbuedBattle, BlitzPile.Hand, Enchanted(rules, defend, imbued));
    BlitzExec imbuedExec = new(ref imbuedBattle, rules, default);
    BlitzKernel.AutoPrePlay(ref imbuedExec);
    Check(imbuedBattle.HandCount == 0 && imbuedBattle.DiscardCount == 2,
        $"Imbued 应在第一回合自动打出两张牌，实际手牌 {imbuedBattle.HandCount} 弃牌 {imbuedBattle.DiscardCount}");
    Check(imbuedBattle.Energy == 3 && imbuedBattle.Counter(BlitzCounter.CardsPlayedThisTurn) == 2,
        "Imbued 自动打出不应支付能量");
    Check(imbuedBattle.Counter(BlitzCounter.EnergySpentThisTurn) == 0, "Imbued 自动打出不应记录能量消耗");

    // Turn 2 (and later) must not auto-play Imbued.
    imbuedBattle.PlayerTurn = 2;
    BlitzPiles.Append(ref imbuedBattle, BlitzPile.Hand, Enchanted(rules, defend, imbued));
    BlitzKernel.AutoPrePlay(ref imbuedExec);
    Check(imbuedBattle.HandCount == 1, "Imbued 只应在第一回合自动打出");

    // Boundary: the remover plays first and takes the other captured card out of combat. The card count
    // no longer matches the tracked move, so the kernel must throw BlitzUnsupportedException (not replay
    // a guessed card and not silently skip it).
    BlitzBattle boundaryBattle = NewBattle();
    boundaryBattle.PlayerTurn = 1;
    boundaryBattle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzPiles.Append(ref boundaryBattle, BlitzPile.Hand, Enchanted(rules, remover, imbued));
    BlitzPiles.Append(ref boundaryBattle, BlitzPile.Hand, Enchanted(rules, defend, imbued));
    BlitzExec boundaryExec = new(ref boundaryBattle, rules, default);
    bool boundaryThrown = false;
    try
    {
        BlitzKernel.AutoPrePlay(ref boundaryExec);
    }
    catch (BlitzUnsupportedException)
    {
        boundaryThrown = true;
    }
    Check(boundaryThrown, "Imbued 捕获卡消失时应抛 BlitzUnsupportedException");

    // Slither: drawing the card into hand spends one CombatEnergyCosts draw and sets an absolute
    // this-combat cost.
    BlitzBattle slitherBattle = NewBattle();
    slitherBattle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzPiles.Append(ref slitherBattle, BlitzPile.Draw, Enchanted(rules, defend, slither));
    BlitzRngState expected = slitherBattle.RngStream(BlitzRngStream.CombatEnergyCosts);
    int counterBefore = expected.Counter;
    int expectedCost = expected.NextInt(4);
    BlitzExec slitherExec = new(ref slitherBattle, rules, default);
    BlitzKernel.Draw(ref slitherExec, 1, fromHandDraw: false);
    Check(slitherBattle.HandCount == 1, "Slither 测试应抽到一张手牌");
    ref BlitzCard drawn = ref slitherBattle.Cards[0];
    Check(drawn.EnchantmentId == (byte)(slither + 1), "抽到的应是 Slither 卡");
    Check(drawn.EnergyModifierCount == 1
        && drawn.EnergyModifiers[0].Type == BlitzCostType.Absolute
        && drawn.EnergyModifiers[0].Expiration == BlitzCostExpiration.EndOfCombat
        && drawn.EnergyModifiers[0].Amount == expectedCost,
        $"Slither 应写入绝对值 {expectedCost} 的本场费用修饰，实际 {drawn.EnergyModifierCount}");
    Check(slitherBattle.RngStream(BlitzRngStream.CombatEnergyCosts).Counter == counterBefore + 1,
        "Slither 应只消耗一次 CombatEnergyCosts");

    // SlumberingEssence: BeforeFlush adds a WhenPlayed -1 relative modifier before the flush.
    BlitzBattle slumberBattle = NewBattle();
    BlitzPiles.Append(ref slumberBattle, BlitzPile.Hand, Enchanted(rules, defend, slumbering));
    BlitzExec slumberExec = new(ref slumberBattle, rules, default);
    BlitzKernel.BeforeFlush(ref slumberExec);
    ref BlitzCard slumberCard = ref slumberBattle.Cards[0];
    Check(slumberCard.EnergyModifierCount == 1
        && slumberCard.EnergyModifiers[0].Type == BlitzCostType.Relative
        && slumberCard.EnergyModifiers[0].Expiration == BlitzCostExpiration.WhenPlayed
        && slumberCard.EnergyModifiers[0].Amount == -1,
        "SlumberingEssence BeforeFlush 应写入 WhenPlayed -1 修饰");
    BlitzKernel.BeforeFlush(ref slumberExec);
    Check(slumberCard.EnergyModifierCount == 2, "每次 BeforeFlush 都应追加一条 SlumberingEssence 修饰");

    // PerfectFit: after the non-initial shuffle the enchanted card is the top of the new draw pile.
    BlitzBattle shuffleBattle = NewBattle();
    shuffleBattle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzCard perfect = Enchanted(rules, defend, perfectFit);
    BlitzPiles.Append(ref shuffleBattle, BlitzPile.Discard, Enchanted(rules, defend, imbued));
    BlitzPiles.Append(ref shuffleBattle, BlitzPile.Discard, perfect);
    BlitzPiles.Append(ref shuffleBattle, BlitzPile.Draw, Enchanted(rules, defend, slither));
    BlitzExec shuffleExec = new(ref shuffleBattle, rules, default);
    BlitzKernel.Shuffle(ref shuffleExec);
    Check(shuffleBattle.DrawCount == 3 && shuffleBattle.DiscardCount == 0, "洗牌后三张牌都应进入抽牌堆");
    Check(BlitzFingerprint.Equal(shuffleBattle.Cards[shuffleBattle.HandCount], perfect),
        "PerfectFit 应把附魔卡放在洗牌后抽牌堆顶");
}

// SK: contracts for the Silent kernel capabilities. Discard pipeline + AfterCardDiscarded + Sly
// auto-play, the poison trigger, the dynamic Shiv target, the card-play pair snapshot and the
// next-turn draw/energy terms.
static void CheckSkKernel()
{
    BlitzRules rules = SilentRules();
    Check(BlitzRegistry.TryCard("KS_DISCARD_TARGET", out ushort targetCard), "缺少 KS_DISCARD_TARGET");
    Check(BlitzRegistry.TryCard("KS_SLY_CARD", out ushort slyCard), "缺少 KS_SLY_CARD");
    Check(BlitzRegistry.TryCard("SHIV", out ushort shiv), "缺少 SHIV");
    Check(BlitzRegistry.TryPower("POISON_POWER", out ushort poison), "缺少 POISON_POWER");
    Check(BlitzRegistry.TryPower("ACCELERANT_POWER", out ushort accelerant), "缺少 ACCELERANT_POWER");
    Check(BlitzRegistry.TryPower("FAN_OF_KNIVES_POWER", out ushort fan), "缺少 FAN_OF_KNIVES_POWER");
    Check(BlitzRegistry.TryPower("AFTERIMAGE_POWER", out ushort afterimage), "缺少 AFTERIMAGE_POWER");
    Check(BlitzRegistry.TryPower("DRAW_CARDS_NEXT_TURN_POWER", out ushort drawNext), "缺少 DRAW_CARDS_NEXT_TURN_POWER");
    Check(BlitzRegistry.TryPower("ENERGY_NEXT_TURN_POWER", out ushort energyNext), "缺少 ENERGY_NEXT_TURN_POWER");
    Check(BlitzRegistry.TryRelic("KS_TEST_DISCARD_RELIC", out ushort discardRelic), "缺少 KS_TEST_DISCARD_RELIC");
    Check(BlitzRegistry.TryRelic("KS_TEST_SLY_RELIC", out ushort slyRelic), "缺少 KS_TEST_SLY_RELIC");

    // Discard: the card moves, the counter grows and AfterCardDiscarded reaches the relic listener.
    BlitzBattle battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    battle.RelicCount = 2;
    battle.Relics[0] = new BlitzRelic { Id = discardRelic };
    battle.Relics[1] = new BlitzRelic { Id = slyRelic };
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, targetCard));
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, slyCard));
    BlitzExec x = new(ref battle, rules, default);
    KsTestLog.Clear();
    BlitzKernel.Discard(ref x, BlitzPile.Hand, 1);
    Check(battle.Counter(BlitzCounter.CardsDiscardedThisTurn) == 1, "弃牌计数应为 1");
    Check(KsTestLog.Lines.Contains("discarded"), "AfterCardDiscarded 未派发");
    Check(KsTestLog.Lines.Contains("sly-play"), "Sly 弃牌未自动打出");

    // Discard-and-draw with a Sly card: the draw may shuffle the Sly card out of the discard pile;
    // the auto-play locates it anywhere in the battle instead of assuming the discard pile.
    battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    battle.RelicCount = 2;
    battle.Relics[0] = new BlitzRelic { Id = discardRelic };
    battle.Relics[1] = new BlitzRelic { Id = slyRelic };
    // The draw pile starts empty, so Draw shuffles the discard (containing the Sly card) into the
    // draw pile before the auto-play locates it.
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, targetCard));
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, slyCard));
    battle.RngStream(BlitzRngStream.Shuffle) = BlitzRngState.FromSerializable(0, 1, 2, 3, 4);
    x = new(ref battle, rules, default);
    KsTestLog.Clear();
    Span<int> selected = stackalloc int[2];
    selected[0] = 0;
    selected[1] = 1;
    BlitzKernel.DiscardAndDraw(ref x, BlitzPile.Hand, selected, 1);
    Check(battle.Counter(BlitzCounter.CardsDiscardedThisTurn) == 2, "批量弃牌计数应为 2");
    Check(KsTestLog.Lines.Count(line => line == "discarded") == 2, "批量弃牌应派发两次 AfterCardDiscarded");
    Check(KsTestLog.Lines.Contains("sly-play"), "批量弃牌后 Sly 未自动打出");

    // Two byte-equal Sly cards are not interchangeable. The discarded copy must auto-play; taking
    // the copy still in hand would consume both and leave the hand empty (DX4).
    battle = NewBattle();
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, slyCard));
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, slyCard));
    x = new(ref battle, rules, default);
    KsTestLog.Clear();
    Span<int> duplicateSelection = stackalloc int[1];
    duplicateSelection[0] = 1;
    BlitzKernel.DiscardAndDraw(ref x, BlitzPile.Hand, duplicateSelection, 0);
    Check(battle.HandCount == 1 && BlitzPiles.At(ref battle, BlitzPile.Hand, 0).Id == slyCard,
        "两个相同 Sly 牌时应只自动打出被弃置的那一张，手里保留另一张");
    Check(battle.DiscardCount == 1 && BlitzPiles.At(ref battle, BlitzPile.Discard, 0).Id == slyCard,
        "自动打出结束后被弃置的 Sly 牌应在弃牌堆");
    Check(KsTestLog.Lines.Count(line => line == "sly-play") == 1, "两个相同 Sly 牌时只应自动打出一次");
    Check(x.TransientMarkBase == 0, "Sly 瞬态身份范围应在结算后释放");

    // The identity range is a capacity-bounded transient resource: exhausting it is an explicit
    // BlitzCapacityException, not a silent reuse of another batch's marks.
    battle = NewBattle();
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, slyCard));
    x = new(ref battle, rules, default) { TransientMarkBase = 200 };
    bool markCapacity = false;
    try
    {
        Span<int> one = stackalloc int[1];
        one[0] = 0;
        BlitzKernel.DiscardAndDraw(ref x, BlitzPile.Hand, one, 0);
    }
    catch (BlitzCapacityException)
    {
        markCapacity = true;
    }
    Check(markCapacity, "Sly 瞬态身份范围耗尽时应抛 BlitzCapacityException");

    // A hook that removes the discarded card leaves no copy carrying its mark; the auto-play target
    // is unrepresentable and must fail explicitly instead of playing a byte-equal stranger.
    battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = discardRelic };
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, slyCard));
    x = new(ref battle, rules, default);
    KsTestDiscardRelic.RemoveDiscardedCard = true;
    bool batchUnsupported = false;
    try
    {
        Span<int> one = stackalloc int[1];
        one[0] = 0;
        BlitzKernel.DiscardAndDraw(ref x, BlitzPile.Hand, one, 0);
    }
    catch (BlitzUnsupportedException)
    {
        batchUnsupported = true;
    }
    finally
    {
        KsTestDiscardRelic.RemoveDiscardedCard = false;
    }
    Check(batchUnsupported, "批量弃牌的 Sly 目标失去瞬态标记时应抛 BlitzUnsupportedException");

    // An AfterCardDiscarded listener that moves the card out of every pile makes the Sly auto-play
    // target unrepresentable: the kernel must surface a BlitzUnsupportedException, not guess a slot.
    battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = discardRelic };
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, slyCard));
    x = new(ref battle, rules, default);
    KsTestLog.Clear();
    KsTestDiscardRelic.RemoveDiscardedCard = true;
    bool unsupported = false;
    try
    {
        BlitzKernel.Discard(ref x, BlitzPile.Hand, 0);
    }
    catch (BlitzUnsupportedException)
    {
        unsupported = true;
    }
    finally
    {
        KsTestDiscardRelic.RemoveDiscardedCard = false;
    }
    Check(unsupported, "Sly 目标离开所有牌堆时应抛 BlitzUnsupportedException");

    // Poison: min(amount, 1 + opponents' Accelerant) hits of the current amount, decrementing while
    // alive (CorePowerSupport.TriggerPoison).
    battle = NewBattle();
    x = new(ref battle, rules, default);
    byte enemy = BlitzKernel.EnemyOwner(0);
    BlitzKernel.ApplyPower(ref x, enemy, poison, 3, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    Span<byte> enemies = stackalloc byte[1];
    enemies[0] = enemy;
    BlitzKernel.TriggerPoison(ref x, enemies);
    // TriggerCount = min(amount, 1 + opponents' Accelerant): no Accelerant means one hit.
    Check(battle.Enemies[0].Creature.CurrentHp == 97, $"毒应造成 3 点伤害，实际血量 {battle.Enemies[0].Creature.CurrentHp}");
    Check(BlitzKernel.GetAmount(battle, enemy, poison) == 2, "毒应递减到 2");

    battle = NewBattle();
    x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, enemy, poison, 3, BlitzLimits.PlayerOwner);
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, accelerant, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    BlitzKernel.TriggerPoison(ref x, enemies);
    Check(battle.Enemies[0].Creature.CurrentHp == 95, $"加速毒应触发 2 次，实际血量 {battle.Enemies[0].Creature.CurrentHp}");
    Check(BlitzKernel.GetAmount(battle, enemy, poison) == 1, "加速毒应剩余 1 层");

    // Shiv's dynamic target: all enemies only while the player holds FanOfKnivesPower.
    BlitzCard shivCard = BlitzKernel.CreateCard(rules, shiv);
    Check(BlitzRegistry.Card(shiv).TargetType(battle, rules, shivCard) == BlitzTargetType.AnyEnemy,
        "无飞刀扇时 Shiv 应单体");
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, fan, 1, BlitzLimits.PlayerOwner);
    Check(BlitzRegistry.Card(shiv).TargetType(battle, rules, shivCard) == BlitzTargetType.AllEnemies,
        "飞刀扇在场时 Shiv 应打全体");

    // Card-play pair: BeforeCardPlayed snapshots the Afterimage amount for AfterCardPlayed.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, afterimage, 2, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    int afterimageIndex = BlitzKernel.FindPower(battle, BlitzLimits.PlayerOwner, afterimage);
    Check(afterimageIndex >= 0, "Afterimage 未施加");
    BlitzListenerDef afterimageDef = BlitzRegistry.Power(afterimage);
    BlitzListener self = new(BlitzListenerKind.Power, (byte)afterimageIndex, BlitzLimits.PlayerOwner);
    BlitzCardPlayInfo play = new(BlitzKernel.CreateCard(rules, targetCard), BlitzLimits.NoOwner, 0, 1, BlitzPile.Discard);
    afterimageDef.BeforeCardPlayed(ref x, self, play);
    Check(x.TryTakeCardPlayPair(BlitzCardPlayPairKind.AfterimageBlock, BlitzLimits.PlayerOwner, afterimage,
        out int pairAmount) && pairAmount == 2, "Afterimage 快照金额错误");
    Check(!x.TryTakeCardPlayPair(BlitzCardPlayPairKind.AfterimageBlock, BlitzLimits.PlayerOwner, afterimage,
        out _), "Afterimage 快照只应消费一次");

    // Next-turn draw/energy: DrawCardsNextTurn adds only its snapshotted amount; EnergyNextTurn is
    // consumed by the turn-start reset.
    battle = NewBattle();
    x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, drawNext, 2, BlitzLimits.PlayerOwner);
    int drawIndex = BlitzKernel.FindPower(battle, BlitzLimits.PlayerOwner, drawNext);
    battle.Powers[drawIndex].AmountOnTurnStart = 2;
    Check(BlitzKernel.ModifiedHandDraw(ref x, 5) == 7, "下回合抽牌应加 2");
    battle.Powers[drawIndex].AmountOnTurnStart = 0;
    Check(BlitzKernel.ModifiedHandDraw(ref x, 5) == 5, "未快照的下回合抽牌不应由监听者加");

    battle = NewBattle();
    x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, energyNext, 2, BlitzLimits.PlayerOwner);
    Check(BlitzKernel.ConsumeEnergyNextTurn(ref battle) == 2, "下回合能量应为 2");
    Check(BlitzKernel.GetAmount(battle, BlitzLimits.PlayerOwner, energyNext) == 0, "下回合能量应被消费");
}

static BlitzRules SilentRules()
{
    BlitzRulesBuilder builder = new() { BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first"], RootEnemyCount = 0 };
    BlitzRegistry.TryCard("KS_DISCARD_TARGET", out ushort targetCard);
    BlitzRegistry.TryCard("KS_SLY_CARD", out ushort slyCard);
    BlitzRegistry.TryCard("SHIV", out ushort shiv);
    builder.SetCard(targetCard, 0, SilentStats(BlitzCardType.Skill, BlitzTargetType.Self, 0));
    builder.SetCard(slyCard, 0, SilentStats(BlitzCardType.Skill, BlitzTargetType.Self, 0, BlitzKeywords.Sly));
    builder.SetCard(shiv, 0, SilentStats(BlitzCardType.Attack, BlitzTargetType.AnyEnemy, 0));
    return builder.Build();

    static BlitzCardStats SilentStats(BlitzCardType type, BlitzTargetType target, int cost,
        BlitzKeywords keywords = BlitzKeywords.None) => new()
    {
        Type = type, Target = target, Rarity = BlitzCardRarity.Basic, EnergyCost = cost, CostsX = false,
        StarCost = -1, StarCostsX = false, Keywords = keywords, MaxUpgradeLevel = 1, Vars = [],
    };
}



// Beam-shaped workload: copy parent into a preallocated slot, step in place, hash. Reports the
// mean per transition; zero managed allocation is asserted on the measured loop.
static object Benchmark(BlitzRules rules)
{
    const int width = 64;
    BlitzBattle root = SliceBattle();
    BlitzBattle[] parents = new BlitzBattle[width];
    BlitzBattle[] children = new BlitzBattle[width * 16];
    BlitzAction[] actions = new BlitzAction[BlitzKernel.MaxLegalActions];
    Random random = new(3);
    for (int index = 0; index < width; index++)
    {
        BlitzBattle battle = root;
        int depth = random.Next(12);
        for (int step = 0; step < depth && BlitzKernel.IsInProgress(battle); step++)
        {
            int count = BlitzKernel.LegalActions(battle, rules, actions);
            BlitzKernel.Step(ref battle, actions[random.Next(count)], rules, out _);
        }
        parents[index] = BlitzKernel.IsInProgress(battle) ? battle : root;
    }

    long transitions = 0;
    ulong sink = 0;
    // mode 0: copy+step+hash, 1: copy only, 2: copy+hash, 3: copy+step
    void Round(int mode)
    {
        int slot = 0;
        for (int parent = 0; parent < width; parent++)
        {
            int count = BlitzKernel.LegalActions(parents[parent], rules, actions);
            for (int action = 0; action < count && slot < children.Length; action++)
            {
                children[slot] = parents[parent];
                if (mode is 0 or 3)
                    BlitzKernel.Step(ref children[slot], actions[action], rules, out _);
                if (mode is 0 or 2)
                    sink += BlitzFingerprint.Hash(children[slot]);
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
    Check(BlitzRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD 定义");
    Check(BlitzRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike), "缺少 STRIKE_IRONCLAD 定义");
    BlitzRulesBuilder builder = new()
    {
        BaseMaxEnergy = 3,
        PlayerSlotIndex = 0,
        HasEncounter = true,
        EncounterSlots = ["a"],
        RootEnemyCount = 0,
    };
    builder.SetCard(defend, 0, new BlitzCardStats
    {
        Type = BlitzCardType.Skill, Target = BlitzTargetType.Self, Rarity = BlitzCardRarity.Basic,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false, Keywords = BlitzKeywords.None,
        MaxUpgradeLevel = 1, Vars = [6m],
    });
    builder.SetCard(strike, 0, new BlitzCardStats
    {
        Type = BlitzCardType.Attack, Target = BlitzTargetType.AnyEnemy, Rarity = BlitzCardRarity.Basic,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false, Keywords = BlitzKeywords.None,
        MaxUpgradeLevel = 1, Vars = [6m],
    });
    builder.SetCard(strike, 1, new BlitzCardStats
    {
        Type = BlitzCardType.Attack, Target = BlitzTargetType.AnyAlly, Rarity = BlitzCardRarity.Basic,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false, Keywords = BlitzKeywords.None,
        MaxUpgradeLevel = 1, Vars = [6m],
    });
    builder.SetCardPool("test.pool", [strike, defend, strike, defend]);
    BlitzRules rules = builder.Build();

    // Distinct selection mirrors TakeRandom/UnstableShuffle: n-1 Shuffle draws over the whole list,
    // then the first count entries of the shuffled order. With-replacement selection draws each card.
    BlitzBattle battle = NewBattle();
    battle.RngStream(BlitzRngStream.Shuffle) = BlitzRngState.FromSerializable(1, 11, 22, 33, 44);
    Span<BlitzCard> selected = stackalloc BlitzCard[4];
    BlitzRngState reference = battle.RngStream(BlitzRngStream.Shuffle);
    int distinctCount = BlitzKernel.SampleCardPool(ref battle, rules, "test.pool", 1, distinct: true,
        BlitzRngStream.Shuffle, selected);
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
    Check(battle.RngStream(BlitzRngStream.Shuffle).Counter == 4, "distinct 取样应消耗 3 次 Shuffle");
    Check(battle.RngStream(BlitzRngStream.Shuffle).Counter == reference.Counter,
        "distinct 取样参考计数器不一致");

    BlitzBattle replacement = NewBattle();
    replacement.RngStream(BlitzRngStream.Shuffle) = BlitzRngState.FromSerializable(1, 11, 22, 33, 44);
    Span<BlitzCard> repeated = stackalloc BlitzCard[3];
    int repeatedCount = BlitzKernel.SampleCardPool(ref replacement, rules, "test.pool", 3, distinct: false,
        BlitzRngStream.Shuffle, repeated);
    BlitzRngState replacementReference = BlitzRngState.FromSerializable(1, 11, 22, 33, 44);
    Check(repeatedCount == 3, "有放回取样数量应为 3");
    for (int index = 0; index < 3; index++)
    {
        // The same NextInt draw indexes the frozen pool [strike, defend, strike, defend].
        int pick = replacementReference.NextInt(0, 4);
        Check(repeated[index].Id == (pick % 2 == 0 ? strike : defend), $"有放回第 {index} 张与 NextInt 不一致");
    }
    Check(replacement.RngStream(BlitzRngStream.Shuffle).Counter == 4, "有放回取样应每张消耗一次 Shuffle");

    // Unplayable / no-target auto-play goes to the result pile without playing.
    BlitzBattle unplayable = NewBattle();
    BlitzCard blocked = BlitzKernel.CreateCard(rules, defend);
    blocked.Keywords |= BlitzKeywords.Unplayable;
    BlitzPiles.Append(ref unplayable, BlitzPile.Hand, blocked);
    BlitzExec x = new(ref unplayable, rules, BlitzAction.PlayCard(0, BlitzLimits.NoOwner));
    Check(!BlitzKernel.AutoPlay(ref x, BlitzPile.Hand, 0), "不可打出的自动打出应返回 false");
    Check(unplayable.HandCount == 0 && unplayable.DiscardCount == 1, "不可打出的自动打出应进入弃牌堆");
    Check(!x.NeedsChoice, "不可打出的自动打出不应请求选择");

    BlitzBattle targetless = NewBattle();
    BlitzPiles.Append(ref targetless, BlitzPile.Hand, BlitzKernel.CreateCard(rules, strike, 1));
    BlitzExec y = new(ref targetless, rules, BlitzAction.PlayCard(0, BlitzLimits.NoOwner));
    Check(!BlitzKernel.AutoPlay(ref y, BlitzPile.Hand, 0), "无目标的自动打出应返回 false");
    Check(targetless.HandCount == 0 && targetless.DiscardCount == 1, "无目标的自动打出应进入弃牌堆");

    // Exhaust-keyword and force-exhaust cards land in the exhaust pile; a played force-exhaust card
    // clears ExhaustOnNextPlay, while one moved without playing keeps it (native behavior).
    BlitzBattle exhaustKeyword = NewBattle();
    BlitzCard howl = BlitzKernel.CreateCard(rules, defend);
    howl.Keywords |= BlitzKeywords.Unplayable | BlitzKeywords.Exhaust;
    BlitzPiles.Append(ref exhaustKeyword, BlitzPile.Hand, howl);
    BlitzExec z = new(ref exhaustKeyword, rules, BlitzAction.PlayCard(0, BlitzLimits.NoOwner));
    _ = BlitzKernel.AutoPlay(ref z, BlitzPile.Hand, 0);
    Check(exhaustKeyword.ExhaustCount == 1 && exhaustKeyword.DiscardCount == 0, "消耗关键字的自动打出应进入消耗堆");

    BlitzBattle played = NewBattle();
    BlitzPiles.Append(ref played, BlitzPile.Draw, BlitzKernel.CreateCard(rules, defend));
    BlitzExec w = new(ref played, rules, BlitzAction.PlayCard(0, BlitzLimits.NoOwner));
    BlitzKernel.AutoPlayFromDrawPile(ref w, 1, forceExhaust: true);
    Check(played.ExhaustCount == 1 && played.DiscardCount == 0, "强制消耗的自动打出应进入消耗堆");
    Check(played.Player.Block == 6, "自动打出的防御应获得格挡");
    Check((BlitzPiles.At(ref played, BlitzPile.Exhaust, 0).Flags & BlitzCardFlags.ExhaustOnNextPlay) == 0,
        "打出的强制消耗标记应被清除");

    BlitzBattle notPlayed = NewBattle();
    BlitzPiles.Append(ref notPlayed, BlitzPile.Draw, BlitzKernel.CreateCard(rules, strike, 1));
    BlitzExec v = new(ref notPlayed, rules, BlitzAction.PlayCard(0, BlitzLimits.NoOwner));
    BlitzKernel.AutoPlayFromDrawPile(ref v, 1, forceExhaust: true);
    Check(notPlayed.ExhaustCount == 1, "无目标的强制消耗自动打出应进入消耗堆");
    Check((BlitzPiles.At(ref notPlayed, BlitzPile.Exhaust, 0).Flags & BlitzCardFlags.ExhaustOnNextPlay) != 0,
        "未打出的强制消耗标记应保留");
}

// KB: decimal card variables (Thrash) use an exact value table keyed from BlitzCard.Private. The
// contracts pin canonical slot order, clone sharing, upgrade/downgrade retention, orphan removal,
// capacity, the fractional-delta boundary on a card that did not opt in, and the transient
// BeatDown mark's clone semantics.
static void CheckDecimalVars(BlitzRules rules)
{
    Check(BlitzRegistry.TryCard("THRASH", out ushort thrash), "缺少 THRASH 定义");
    BlitzBattle battle = NewBattle();
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, thrash));
    BlitzDecimalVars.Assign(ref battle, ref battle.Cards[0], 0.75m);
    Check(battle.DecimalVarCount == 1 && battle.Cards[0].Private == 1,
        $"Thrash 的小数变量应占用第一个槽位，实际 count={battle.DecimalVarCount} private={battle.Cards[0].Private}");
    Check(BlitzDecimalVars.Delta(in battle, in battle.Cards[0], 0) == 0.75m, "小数变量应精确保存 0.75");

    // A gameplay clone inherits the value; updating one card must not change the other.
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CloneCard(battle.Cards[0]));
    Check(BlitzDecimalVars.Delta(in battle, in battle.Cards[1], 0) == 0.75m, "克隆应继承小数变量");
    BlitzDecimalVars.Assign(ref battle, ref battle.Cards[0], 1.5m);
    Check(BlitzDecimalVars.Delta(in battle, in battle.Cards[0], 0) == 1.5m, "更新后原卡应为 1.5");
    Check(BlitzDecimalVars.Delta(in battle, in battle.Cards[1], 0) == 0.75m, "共享槽位的克隆不应被改写");
    Check(battle.DecimalVarCount == 2, "两个不同值应占两个槽位");

    // Upgrade keeps the delta; Downgrade resets Var0 and keeps the private ExtraDamage equivalent.
    BlitzKernel.UpgradeCard(rules, ref battle.Cards[0]);
    Check(BlitzDecimalVars.Delta(in battle, in battle.Cards[0], 0) == 1.5m, "升级不应改变小数变量偏移");
    Check(battle.Cards[0].Var0 == 0, "小数变量卡不应同时写 Var0");
    BlitzExec x = new(ref battle, rules, default);
    BlitzKernel.DowngradeCard(ref x, 0);
    Check(BlitzDecimalVars.Delta(in battle, in battle.Cards[0], 0) == 1.5m,
        "降级后应保留 ExtraDamage 等价的小数偏移");
    Check(battle.Cards[0].Var0 == 0, "降级不应把小数偏移写入 Var0");

    // An orphaned value disappears during canonicalization.
    BlitzPiles.RemoveAt(ref battle, BlitzPile.Hand, 1);
    BlitzDecimalVars.Canonicalize(ref battle);
    Check(battle.DecimalVarCount == 1, $"移除引用后应只留一个槽位，实际 {battle.DecimalVarCount}");

    // Fractional deltas on a card that did not opt into the table remain an explicit boundary.
    BlitzBattle strikeBattle = NewBattle();
    BlitzRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike);
    BlitzPiles.Append(ref strikeBattle, BlitzPile.Play, BlitzKernel.CreateCard(rules, strike));
    BlitzExec strikeExec = new(ref strikeBattle, rules, default)
    {
        Card = strikeBattle.Cards[0],
        Stats = rules.Card(strike, 0),
        PlayPosition = 0,
    };
    bool fractionalRejected = false;
    try
    {
        BlitzCardEffects.AddCurrentCardVar(ref strikeExec, 0, 0.5m);
    }
    catch (BlitzUnsupportedException)
    {
        fractionalRejected = true;
    }
    Check(fractionalRejected, "未声明小数变量的卡收到小数偏移时应抛 BlitzUnsupportedException");

    // Every distinct live value fits; the next one throws BlitzCapacityException.
    BlitzBattle capacity = NewBattle();
    for (int index = 0; index < BlitzLimits.DecimalCardVars; index++)
    {
        BlitzPiles.Append(ref capacity, BlitzPile.Hand, BlitzKernel.CreateCard(rules, thrash));
        BlitzDecimalVars.Assign(ref capacity, ref capacity.Cards[index], 0.25m * (index + 1));
    }
    Check(capacity.DecimalVarCount == BlitzLimits.DecimalCardVars, "全部小数变量槽位应被占满");
    BlitzPiles.Append(ref capacity, BlitzPile.Hand, BlitzKernel.CreateCard(rules, thrash));
    bool capacityRejected = false;
    try
    {
        BlitzDecimalVars.Assign(ref capacity, ref capacity.Cards[BlitzLimits.DecimalCardVars], 99m);
    }
    catch (BlitzCapacityException)
    {
        capacityRejected = true;
    }
    Check(capacityRejected, "超出容量的小数变量应抛 BlitzCapacityException");

    BlitzCard marked = BlitzKernel.CreateCard(rules, thrash);
    marked.TransientMark = 5;
    Check(BlitzKernel.CloneCard(marked).TransientMark == 0, "克隆应清除 BeatDown 瞬态标记");
}

// DX3: contracts for the rules/identity fixes. The spawn contract uses the state-machine
// constructor's logged initial MoveState (InitialMoveLogged), the card contract pins the play-pile
// position of AddCurrentCardVar and Dampen's accumulator restore.
static void CheckDx3Kernel(BlitzRules rules)
{
    Check(BlitzRegistry.TryMonster("NIBBIT", out ushort nibbit), "缺少 NIBBIT");
    BlitzRulesBuilder builder = new() { RootEnemyCount = 1, HasEncounter = true, EncounterSlots = ["first", "second", "third"] };
    builder.SetMonster(0, SpawnMonsterRules(nibbit));
    builder.AddSpawnableMonster(SpawnMonsterRules(nibbit, initialMoveLogged: true));
    BlitzRules spawnRules = builder.Build();
    BlitzBattle battle = SpawnBattle();
    battle.Side = BlitzSide.Enemy;
    BlitzExec spawnExec = new(ref battle, spawnRules, default);
    int rulesId = spawnRules.SpawnRulesId(nibbit);
    byte owner = BlitzKernel.SpawnMonster(ref spawnExec, BlitzLimits.PlayerOwner, rulesId, 1, minion: false);
    Check((battle.Enemy(owner).Flags & BlitzEnemyFlags.NeedsInitialRoll) == 0,
        "InitialMoveLogged 的敌方召唤物应直接采用状态机构造器记录的初始行动");
    Check(battle.Enemy(owner).Move == 0 && battle.Enemy(owner).LoggedMoves == 1,
        $"InitialMoveLogged 召唤物应已记录初始行动，实际 move={battle.Enemy(owner).Move} logged={battle.Enemy(owner).LoggedMoves}");

    // AddCurrentCardVar must address BlitzExec.PlayPosition, not the top of the play pile: a nested
    // auto-play leaves its card above the outer card until its tail moves it.
    Check(BlitzRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike), "缺少 STRIKE_IRONCLAD");
    BlitzBattle pileBattle = NewBattle();
    BlitzPiles.Append(ref pileBattle, BlitzPile.Play, BlitzKernel.CreateCard(rules, strike));
    BlitzPiles.Append(ref pileBattle, BlitzPile.Play, BlitzKernel.CreateCard(rules, strike));
    int playStart = BlitzPiles.Start(pileBattle, BlitzPile.Play);
    BlitzExec pileExec = new(ref pileBattle, rules, default);
    pileExec.Card = BlitzPiles.At(ref pileBattle, BlitzPile.Play, 0);
    pileExec.PlayPosition = 0;
    BlitzCardEffects.AddCurrentCardVar(ref pileExec, 0, 5);
    Check(BlitzPiles.At(ref pileBattle, BlitzPile.Play, 0).Var0 == 5
        && BlitzPiles.At(ref pileBattle, BlitzPile.Play, 1).Var0 == 0,
        "AddCurrentCardVar 应写到当前出牌位置，而不是打出堆顶");

    // Dampen downgrade: the level-0 value is read and the accumulated Rampage var is restored.
    Check(BlitzRegistry.TryCard("RAMPAGE", out ushort rampage), "缺少 RAMPAGE");
    BlitzBattle rampBattle = NewBattle();
    BlitzPiles.Append(ref rampBattle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, rampage, 1));
    int rampageIndex = BlitzPiles.Start(rampBattle, BlitzPile.Hand);
    rampBattle.Cards[rampageIndex].Var0 = 10;
    BlitzExec rampExec = new(ref rampBattle, rules, default);
    rampExec.Card = rampBattle.Cards[rampageIndex];
    rampExec.PlayPosition = rampageIndex;
    BlitzKernel.DowngradeCard(ref rampExec, rampageIndex);
    Check(rampBattle.Cards[rampageIndex].Upgrade == 0
        && rampBattle.Cards[rampageIndex].DampenOriginalUpgrade == 2
        && rampBattle.Cards[rampageIndex].Var0 == 10,
        "Dampen 降级后应使用 0 级规则并恢复 Rampage 的累计 Var0");

    // The pending amount marker is transient and cleared at the stable boundary.
    BlitzBattle flagBattle = NewBattle();
    Check(BlitzRegistry.TryPower("STRENGTH_POWER", out ushort strength), "缺少 STRENGTH_POWER");
    flagBattle.PowerCount = 1;
    flagBattle.Powers[0] = new BlitzPower
    {
        Id = strength, Owner = BlitzLimits.PlayerOwner, Amount = 1,
        Flags = BlitzPowerFlags.PendingAmountState,
    };
    BlitzKernel.ClearPendingPowerAmountStates(ref flagBattle);
    Check((flagBattle.Powers[0].Flags & BlitzPowerFlags.PendingAmountState) == 0,
        "PendingAmountState 应在稳定边界清除");
}

// DX5: the Pen Nib latch names the CardPlay whose damage is doubled, the HistoryCourse cross-turn
// slot clones the previous turn's last attack, and the Battleworn Dummy's last tick is EventDefeat.
static void CheckDx5Kernel(BlitzRules rules)
{
    // Pen Nib: a Sly card auto-played from the tenth attack's discard choice is a separate CardPlay;
    // it must not inherit the doubling (the native latch is AttackToDouble == CardSource.Original).
    Check(BlitzRegistry.TryRelic("PEN_NIB", out ushort penNib), "缺少 PEN_NIB");
    BlitzRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike);
    BlitzBattle nibBattle = NewBattle();
    nibBattle.RelicCount = 1;
    nibBattle.Relics[0] = new BlitzRelic { Id = penNib, Counter = 9 };
    BlitzExec nibExec = new(ref nibBattle, rules, default);
    nibExec.PlayIdentity = 7;
    BlitzCard nibCard = BlitzKernel.CreateCard(rules, strike);
    nibExec.Card = nibCard;
    BlitzCardPlayInfo nibPlay = new(nibCard, BlitzLimits.NoOwner, 0, 1, BlitzPile.Discard, 0);
    PenNib nibDef = (PenNib)BlitzRegistry.Relic(penNib);
    BlitzListener nibSelf = new(BlitzListenerKind.Relic, 0, BlitzLimits.PlayerOwner);
    nibDef.BeforeCardPlayed(ref nibExec, nibSelf, nibPlay);
    Check(nibBattle.Relics[0].Counter == 0 && nibBattle.Relics[0].Aux == 7,
        "Pen Nib 第十次攻击应锁存触发它的 CardPlay 身份");
    Check(nibDef.ModifyDamageMultiplicative(ref nibExec, nibSelf,
        new BlitzDamageContext(BlitzLimits.NoOwner, BlitzLimits.PlayerOwner, BlitzValueProps.Move,
            true, nibCard), 6m) == 2m,
        "Pen Nib 应加倍触发它的那张攻击");
    nibExec.PlayIdentity = 8;
    BlitzCard slyCard = BlitzKernel.CreateCard(rules, strike);
    slyCard.Upgrade = 1;
    Check(nibDef.ModifyDamageMultiplicative(ref nibExec, nibSelf,
        new BlitzDamageContext(BlitzLimits.NoOwner, BlitzLimits.PlayerOwner, BlitzValueProps.Move,
            true, slyCard), 6m) == 1m,
        "Pen Nib 的加倍不能泄漏到同一次选择里嵌套自动打出的牌");
    nibExec.LastPlayIdentity = 8;
    nibDef.AfterCardPlayed(ref nibExec, nibSelf, nibPlay);
    Check(nibBattle.Relics[0].Aux == 7, "嵌套打出的 AfterCardPlayed 不应清除外层的 Pen Nib 锁存");
    nibExec.PlayIdentity = 7;
    nibExec.LastPlayIdentity = 7;
    nibDef.AfterCardPlayed(ref nibExec, nibSelf, nibPlay);
    Check(nibBattle.Relics[0].Aux == 0, "触发卡自己的 AfterCardPlayed 应清除 Pen Nib 锁存");

    // HistoryCourse: the turn-start AutoPrePlay clones the previous turn's last attack, auto-plays it,
    // removes the dupe from combat and leaves the slot empty for the new turn.
    Check(BlitzRegistry.TryRelic("HISTORY_COURSE", out ushort historyCourse), "缺少 HISTORY_COURSE");
    BlitzRulesBuilder historyBuilder = new()
    {
        BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first"], RootEnemyCount = 1,
        HasHistoryCourse = true,
    };
    historyBuilder.SetCard(strike, 0, new BlitzCardStats
    {
        Type = BlitzCardType.Attack, Target = BlitzTargetType.AnyEnemy, Rarity = BlitzCardRarity.Basic,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false, Keywords = BlitzKeywords.None,
        MaxUpgradeLevel = 1, Vars = [6m],
    });
    BlitzRules historyRules = historyBuilder.Build();
    BlitzBattle historyBattle = NewBattle();
    historyBattle.PlayerTurn = 2;
    historyBattle.Side = BlitzSide.Player;
    historyBattle.Phase = BlitzPlayerPhase.AutoPrePlay;
    historyBattle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    historyBattle.RelicCount = 1;
    historyBattle.Relics[0] = new BlitzRelic { Id = historyCourse };
    BlitzCard previous = BlitzKernel.CreateCard(historyRules, strike);
    historyBattle.HistoryCourseCard = previous;
    historyBattle.Flags |= BlitzBattleFlags.HistoryCourseCardPresent;
    BlitzExec historyExec = new(ref historyBattle, historyRules, BlitzAction.EndTurn());
    BlitzKernel.AutoPrePlay(ref historyExec);
    Check(!historyExec.NeedsChoice, "HistoryCourse 的复制品自动打出不应请求选择");
    Check(historyBattle.Counter(BlitzCounter.FinishedCardPlays) == 1,
        $"HistoryCourse 应自动打出复制品一次（实际完成 {historyBattle.Counter(BlitzCounter.FinishedCardPlays)} 次）");
    Check(historyBattle.CardCount == 0, "HistoryCourse 的复制品是 dupe，打出后应离开战斗牌堆");
    Check((historyBattle.Flags & BlitzBattleFlags.HistoryCourseCardPresent) == 0
        && BlitzFingerprint.Equal(historyBattle.HistoryCourseCard, default),
        "HistoryCourse 的跨回合槽在 AutoPrePlay 消费后应清空");

    // Battleworn Dummy: the amount-1 branch is the event defeat; it leaves Amount at 1 (the existing
    // engine returns before any decrement/removal) and does not touch a normal combat outcome.
    Check(BlitzRegistry.TryPower("BATTLEWORN_DUMMY_TIME_LIMIT_POWER", out ushort dummyLimit),
        "缺少 BATTLEWORN_DUMMY_TIME_LIMIT_POWER");
    BlitzBattle dummyBattle = NewBattle();
    dummyBattle.PlayerTurn = 1;
    dummyBattle.Side = BlitzSide.Enemy;
    dummyBattle.PowerCount = 1;
    dummyBattle.Powers[0] = new BlitzPower
    {
        Id = dummyLimit, Owner = BlitzLimits.FirstEnemyOwner, Applier = BlitzLimits.FirstEnemyOwner, Amount = 1,
    };
    BlitzExec dummyExec = new(ref dummyBattle, rules, BlitzAction.EndTurn());
    BlitzListener dummySelf = new(BlitzListenerKind.Power, 0, BlitzLimits.FirstEnemyOwner);
    BattlewornDummyTimeLimitPower dummyDef = (BattlewornDummyTimeLimitPower)BlitzRegistry.Power(dummyLimit);
    dummyDef.EndTurnRegular(ref dummyExec, dummySelf, BlitzSide.Enemy, participates: true);
    Check(dummyBattle.Outcome == BlitzOutcome.EventDefeat && dummyBattle.Powers[0].Amount == 1,
        "战损假人最后一层应记 EventDefeat 且保留 Amount 1（现有引擎在逃跑前返回）");
    BlitzBattle tickBattle = NewBattle();
    tickBattle.PlayerTurn = 1;
    tickBattle.Side = BlitzSide.Enemy;
    tickBattle.PowerCount = 1;
    tickBattle.Powers[0] = new BlitzPower
    {
        Id = dummyLimit, Owner = BlitzLimits.FirstEnemyOwner, Applier = BlitzLimits.FirstEnemyOwner, Amount = 2,
    };
    BlitzExec tickExec = new(ref tickBattle, rules, BlitzAction.EndTurn());
    dummyDef.EndTurnRegular(ref tickExec, dummySelf, BlitzSide.Enemy, participates: true);
    Check(tickBattle.Outcome == BlitzOutcome.None && tickBattle.Powers[0].Amount == 1,
        "战损假人非最后一层应只递减一层");
}

// PR batch: the extra-turn pipeline (PaelsEye) must keep RoundNumber unchanged, advance the player
// turn, exhaust the triggering hand and consume only the relic's once-per-combat latch.
static void CheckPrKernel()
{
    BlitzRegistry.TryRelic("PAELS_EYE", out ushort paelsEye);
    BlitzRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike);
    BlitzRulesBuilder builder = new()
    {
        BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first"], RootEnemyCount = 1,
    };
    builder.SetCard(strike, 0, new BlitzCardStats
    {
        Type = BlitzCardType.Attack, Target = BlitzTargetType.AnyEnemy, Rarity = BlitzCardRarity.Basic,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false, Keywords = BlitzKeywords.None,
        MaxUpgradeLevel = 1, Vars = [6m],
    });
    BlitzRules rules = builder.Build();
    BlitzBattle battle = NewBattle();
    battle.Round = 1;
    battle.PlayerTurn = 1;
    battle.Side = BlitzSide.Player;
    battle.Phase = BlitzPlayerPhase.Play;
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    battle.MaxHandSize = 10;
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = paelsEye, Aux = 1 };
    battle.Cards[0] = BlitzKernel.CreateCard(rules, strike);
    battle.HandCount = 1;
    BlitzExec x = new(ref battle, rules, BlitzAction.EndTurn());
    BlitzKernel.AdvanceRound(ref x);
    Check(!x.NeedsChoice, "PaelsEye 的额外回合不应产生待处理选择");
    Check(battle.Round == 1, $"额外回合不应增加 RoundNumber（实际 {battle.Round}）");
    Check(battle.PlayerTurn == 2, $"额外回合应推进玩家回合数（实际 {battle.PlayerTurn}）");
    Check(battle.HandCount == 0, $"PaelsEye 应在回合末消耗整手牌（剩余 {battle.HandCount}）");
    Check(battle.Relics[0].Counter == 1, "PaelsEye 的 AfterTakingExtraTurn 应置为已用");
    Check(battle.Side == BlitzSide.Player, "额外回合应直接开始新的玩家回合");

    // PaelsLegion doubles a card's block while the cooldown is down; the root flag replaces the
    // identity-content gate that used to skip the changed-block listener pass entirely.
    BlitzRegistry.TryRelic("PAELS_LEGION", out ushort paelsLegion);
    BlitzRulesBuilder blockBuilder = new()
    {
        BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first"], RootEnemyCount = 1,
        HasPaelsLegion = true,
    };
    blockBuilder.SetRelicVars(paelsLegion, [2m]);
    BlitzRules blockRules = blockBuilder.Build();
    BlitzBattle blockBattle = NewBattle();
    blockBattle.Round = 1;
    blockBattle.PlayerTurn = 1;
    blockBattle.Side = BlitzSide.Player;
    blockBattle.Phase = BlitzPlayerPhase.Play;
    blockBattle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    blockBattle.RelicCount = 1;
    blockBattle.Relics[0] = new BlitzRelic { Id = paelsLegion };
    BlitzExec blockExec = new(ref blockBattle, blockRules, BlitzAction.EndTurn());
    decimal doubled = BlitzKernel.GainBlock(ref blockExec, BlitzLimits.PlayerOwner, 5m,
        BlitzValueProps.Move, fromCard: true);
    Check(doubled == 10m, $"PaelsLegion 冷却结束时卡牌格挡应翻倍（实际 {doubled}）");
    blockBattle.Relics[0].Counter = 2;
    decimal normal = BlitzKernel.GainBlock(ref blockExec, BlitzLimits.PlayerOwner, 5m,
        BlitzValueProps.Move, fromCard: true);
    Check(normal == 5m, $"PaelsLegion 冷却中不应加倍卡牌格挡（实际 {normal}）");

    // FurCoat freezes the marked-coordinate check into the rules: a spawned enemy starts at 1 HP
    // but keeps its rolled max HP (MonsterSpawnSupport.ApplyCreatureAddedRelics).
    BlitzRegistry.TryMonster("NIBBIT", out ushort nibbit);
    BlitzRulesBuilder coatBuilder = new()
    {
        RootEnemyCount = 1, HasEncounter = true, EncounterSlots = ["first", "second", "third"],
        FurCoatSpawnHpOne = true,
    };
    coatBuilder.SetMonster(0, SpawnMonsterRules(nibbit));
    coatBuilder.AddSpawnableMonster(SpawnMonsterRules(nibbit));
    BlitzRules coatRules = coatBuilder.Build();
    BlitzBattle coatBattle = SpawnBattle();
    BlitzExec coatExec = new(ref coatBattle, coatRules, default);
    byte spawnedOwner = BlitzKernel.SpawnMonster(ref coatExec, BlitzKernel.EnemyOwner(0), 1, slotIndex: 2, minion: false);
    Check(coatBattle.Creature(spawnedOwner).CurrentHp == 1,
        $"FurCoat 命中的生成敌人当前生命应为 1（实际 {coatBattle.Creature(spawnedOwner).CurrentHp}）");
    Check(coatBattle.Creature(spawnedOwner).MaxHp is >= 17 and <= 21,
        $"FurCoat 不应改写生成敌人的最大生命（实际 {coatBattle.Creature(spawnedOwner).MaxHp}）");

    // Bookmark: a retained, non-X card with a positive local cost gets an until-played -1 after the
    // flush (SimulatedCombatState.TriggerBookmarkAfterFlush).
    BlitzRegistry.TryRelic("BOOKMARK", out ushort bookmark);
    BlitzRegistry.TryCard("GOLD_AXE", out ushort goldAxe);
    BlitzRulesBuilder bookmarkBuilder = new()
    {
        BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["first"], RootEnemyCount = 1,
    };
    bookmarkBuilder.SetCard(goldAxe, 0, new BlitzCardStats
    {
        Type = BlitzCardType.Attack, Target = BlitzTargetType.AnyEnemy, Rarity = BlitzCardRarity.Rare,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false,
        Keywords = BlitzKeywords.Retain, MaxUpgradeLevel = 0, Vars = [],
    });
    BlitzRules bookmarkRules = bookmarkBuilder.Build();
    BlitzBattle bookmarkBattle = NewBattle();
    bookmarkBattle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    bookmarkBattle.RelicCount = 1;
    bookmarkBattle.Relics[0] = new BlitzRelic { Id = bookmark };
    bookmarkBattle.Cards[0] = BlitzKernel.CreateCard(bookmarkRules, goldAxe);
    bookmarkBattle.HandCount = 1;
    BlitzExec bookmarkExec = new(ref bookmarkBattle, bookmarkRules, BlitzAction.EndTurn());
    BlitzRegistry.Relic(bookmark).AfterFlush(ref bookmarkExec,
        new BlitzListener(BlitzListenerKind.Relic, 0, BlitzLimits.PlayerOwner));
    Check(bookmarkBattle.Cards[0].EnergyModifierCount == 1,
        $"Bookmark 应给保留牌追加一条费用修饰（实际 {bookmarkBattle.Cards[0].EnergyModifierCount}）");
    Check(bookmarkBattle.Cards[0].EnergyModifiers[0].Type == BlitzCostType.Relative
        && bookmarkBattle.Cards[0].EnergyModifiers[0].Amount == -1
        && bookmarkBattle.Cards[0].EnergyModifiers[0].Expiration == BlitzCostExpiration.WhenPlayed,
        "Bookmark 的费用修饰应为直到打出的 -1");
}

// EC: contracts for the event-card kernel helpers. SetInstanceBlockValue backs ToricToughnessPower's
// block amount (an integer in the settled card block; a fractional value is an explicit boundary), and
// MakeFreeThisCombat mirrors CardModel.SetToFreeThisCombat's energy and star-cost modifiers.
static void CheckEcKernel(BlitzRules rules)
{
    Check(BlitzRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike), "缺少 STRIKE_IRONCLAD 定义");
    Check(BlitzRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD 定义");
    BlitzCard free = BlitzKernel.CreateCard(rules, defend);
    BlitzKernel.MakeFreeThisCombat(rules, ref free);
    Check(free.EnergyModifierCount == 1, "SetToFreeThisCombat 应添加一个费用修饰");
    BlitzCostModifier modifier = free.EnergyModifiers[0];
    Check(modifier.Type == BlitzCostType.Absolute && modifier.Amount == 0
        && modifier.Expiration == BlitzCostExpiration.EndOfCombat && !modifier.IsReduceOnly,
        "SetToFreeThisCombat 应是本场战斗的绝对 0 费修饰");
    Check(free.HasTemporaryStarCost && free.TemporaryStarCost == 0
        && !free.TemporaryStarCostClearsAtTurnEnd && !free.TemporaryStarCostClearsWhenPlayed,
        "SetToFreeThisCombat 的临时星能费用不在回合末或打出时清除");

    BlitzBattle battle = NewBattle();
    battle.PowerCount = 1;
    battle.Powers[0] = new BlitzPower
    {
        Id = BlitzRegistry.PowerId<ToricToughnessPower>(),
        Owner = BlitzLimits.PlayerOwner,
        Amount = 2,
    };
    BlitzKernel.SetInstanceBlockValue(ref battle, 0, 7m);
    Check(battle.Powers[0].Aux0 == 7, "实例格挡值应写入 Aux0");
    bool rejected = false;
    try
    {
        BlitzKernel.SetInstanceBlockValue(ref battle, 0, 7.5m);
    }
    catch (BlitzUnsupportedException)
    {
        rejected = true;
    }
    Check(rejected, "小数格挡量应抛 BlitzUnsupportedException");
}

static BlitzBattle NewBattle()
{
    BlitzBattle battle = default;
    battle.Player = new BlitzCreature
    {
        CurrentHp = 50,
        MaxHp = 50,
        Flags = BlitzCreatureFlags.Present,
    };
    battle.MaxHandSize = 10;
    battle.Energy = 3;
    battle.PotionSlotCount = 0;
    // One alive primary enemy keeps IsEnding false; targetless tests use an AnyAlly card instead.
    battle.EnemyCount = 1;
    battle.RosterCount = 1;
    battle.Roster[0] = 0;
    battle.Enemies[0].Creature = new BlitzCreature
    {
        CurrentHp = 100,
        MaxHp = 100,
        Flags = BlitzCreatureFlags.Present,
    };
    battle.Enemies[0].Flags = BlitzEnemyFlags.InCombat;
    return battle;
}

static void CheckMonsterSpawn(BlitzRules rules)
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
    BlitzBattle battle = SpawnBattle();
    BlitzRngState nicheBefore = battle.RngStream(BlitzRngStream.Niche);
    int expectedHp = ExpectedHp(ref nicheBefore, 17, 21, [44]);
    BlitzExec x = new(ref battle, rules, default);
    byte spawned = BlitzKernel.SpawnMonster(ref x, BlitzKernel.EnemyOwner(0), 1, slotIndex: 2, minion: false);
    Check(spawned == BlitzKernel.EnemyOwner(1), "生成应返回新 owner");
    Check(battle.EnemyCount == 2 && battle.RosterCount == 2, "生成后已知敌人数与名单数应各加一");
    Check(battle.NextCombatId == 8 && battle.Enemies[1].CombatId == 7, "CombatId 应从 NextCombatId 分配并递增");
    Check(battle.Enemies[1].Creature.CurrentHp == expectedHp && battle.Enemies[1].Creature.MaxHp == expectedHp,
        $"生成生命应为排除现有 MaxHp 后的 Niche 取值（期望 {expectedHp}，实际 {battle.Enemies[1].Creature.CurrentHp}）");
    Check(SameRng(battle.RngStream(BlitzRngStream.Niche), nicheBefore), "生成只应消耗一次 Niche 抽样");
    Check(battle.Enemies[1].Rules == 1 && battle.Enemies[1].Slot == 2, "生成敌人应记录规则编号与槽位下标");
    Check(battle.Roster[0] == 0 && battle.Roster[1] == 1, "新槽位在名单末尾时直接追加");
    Check(battle.Enemies[1].Move == 0 && battle.Enemies[1].LoggedMoves == 1
        && (battle.Enemies[1].Flags & BlitzEnemyFlags.NeedsInitialRoll) == 0,
        "玩家回合内生成应立即掷出初始行动并写日志");
    Check(battle.Enemies[1].Ai[0] == 2 && battle.Enemies[1].Ai[1] == 0, "生成应写入编译的初始 AI 寄存器");

    // Stable slot insertion: a same-slot enemy goes after the existing one, before a later slot.
    BlitzKernel.SpawnMonster(ref x, BlitzKernel.EnemyOwner(0), 1, slotIndex: 1, minion: false);
    Check(battle.RosterCount == 3 && battle.Roster[0] == 0 && battle.Roster[1] == 2 && battle.Roster[2] == 1,
        "同槽位生成应稳定插在原槽位敌人之后");

    // A null slot name sorts as -1: before named slots, after earlier nulls.
    BlitzBattle nullSlot = SpawnBattle();
    nullSlot.Enemies[1] = nullSlot.Enemies[0];
    nullSlot.Enemies[1].Slot = (byte)BlitzKernel.NoSlot;
    nullSlot.Enemies[1].CombatId = 2;
    nullSlot.EnemyCount = 2;
    nullSlot.RosterCount = 2;
    nullSlot.Roster[0] = 1;
    nullSlot.Roster[1] = 0;
    x = new BlitzExec(ref nullSlot, rules, default);
    BlitzKernel.SpawnMonster(ref x, BlitzKernel.EnemyOwner(0), 1, BlitzKernel.NoSlot, minion: false);
    Check(nullSlot.RosterCount == 3 && nullSlot.Roster[0] == 1 && nullSlot.Roster[1] == 2 && nullSlot.Roster[2] == 0,
        "无槽名生成应排在有名字的槽位之前，并保持既有空槽顺序");

    // NextFreeSlot / LastFreeSlot: null without an encounter, "" when full, otherwise the first/last.
    Check(BlitzKernel.NextFreeSlot(battle, rules) == "first" && BlitzKernel.LastFreeSlot(battle, rules) == "first",
        "只剩 first 时两者都应返回 first");
    Check(BlitzKernel.NextFreeSlot(battle, rules) == "first" && BlitzKernel.LastFreeSlot(battle, rules) == "first",
        "槽位查询不应改变状态");
    BlitzRules noEncounter = NoEncounterRules();
    Check(BlitzKernel.NextFreeSlot(battle, noEncounter) == null && BlitzKernel.LastFreeSlot(battle, noEncounter) == null,
        "无遭遇时槽位查询应返回 null");

    // Exhausted slots: NextFreeSlot returns "", LastFreeSlot returns null.
    BlitzBattle full = SpawnBattle();
    full.RosterCount = 3;
    full.EnemyCount = 3;
    for (int index = 0; index < 3; index++)
    {
        full.Enemies[index] = full.Enemies[0];
        full.Enemies[index].Slot = (byte)index;
        full.Roster[index] = (byte)index;
    }
    Check(BlitzKernel.NextFreeSlot(full, rules) == "" && BlitzKernel.LastFreeSlot(full, rules) == null,
        "无空槽时 NextFreeSlot 应为空串、LastFreeSlot 应为 null");

    // HP exclusion follows the roster's current MaxHp, including earlier spawns; an exhausted range
    // falls back to NextInt(min, max + 1).
    battle = SpawnBattle();
    battle.Enemies[0].Creature.MaxHp = 17;
    x = new BlitzExec(ref battle, rules, default);
    nicheBefore = battle.RngStream(BlitzRngStream.Niche);
    expectedHp = ExpectedHp(ref nicheBefore, 17, 21, [17]);
    BlitzKernel.SpawnMonster(ref x, BlitzKernel.EnemyOwner(0), 1, slotIndex: 0, minion: false);
    Check(battle.Enemies[1].Creature.MaxHp == expectedHp, "生命排除应使用名单敌人的当前 MaxHp");
    battle.Enemies[0].Creature.MaxHp = 18;
    nicheBefore = battle.RngStream(BlitzRngStream.Niche);
    expectedHp = ExpectedHp(ref nicheBefore, 17, 21, [18, battle.Enemies[1].Creature.MaxHp]);
    BlitzKernel.SpawnMonster(ref x, BlitzKernel.EnemyOwner(0), 1, slotIndex: 2, minion: false);
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
    x = new BlitzExec(ref battle, rules, default);
    nicheBefore = battle.RngStream(BlitzRngStream.Niche);
    int expectedFallback = nicheBefore.NextInt(17, 22);
    BlitzKernel.SpawnMonster(ref x, BlitzKernel.EnemyOwner(0), 1, slotIndex: 0, minion: false);
    Check(battle.Enemies[5].Creature.MaxHp == expectedFallback,
        $"可取值耗尽时应使用 NextInt(min, max+1)（期望 {expectedFallback}）");

    // Enemy-turn spawn: pending initial roll until the end-of-enemy-turn pass.
    battle = SpawnBattle();
    battle.Side = BlitzSide.Enemy;
    battle.Enemies[0].Flags |= BlitzEnemyFlags.PerformedMoveThisRound;
    x = new BlitzExec(ref battle, rules, default);
    BlitzKernel.SpawnMonster(ref x, BlitzKernel.EnemyOwner(0), 1, slotIndex: 2, minion: false);
    Check((battle.Enemies[1].Flags & BlitzEnemyFlags.NeedsInitialRoll) != 0, "敌人回合内生成应保留待掷标记");
    Check(battle.Enemies[1].Move == 0 && battle.Enemies[1].LoggedMoves == 0, "待掷时行动节点应为占位行动且不写日志");
    BlitzKernel.PrepareMonsterMovesForNextRound(ref x);
    Check((battle.Enemies[1].Flags & BlitzEnemyFlags.NeedsInitialRoll) == 0
        && battle.Enemies[1].Move == 0 && battle.Enemies[1].LoggedMoves == 1,
        "敌人回合结束时应对待掷敌人掷出初始行动");

    // Illusion revive: forced synthetic node, dynamic follow-up, heal to max and advancement.
    battle = SpawnBattle();
    x = new BlitzExec(ref battle, rules, default);
    byte rat = BlitzKernel.EnemyOwner(0);
    BlitzKernel.LogMonsterMove(ref battle.Enemies[0], 0);
    battle.Enemies[0].Creature.CurrentHp = 0;
    BlitzKernel.BeginIllusionRevive(ref x, rat);
    Check(battle.Enemies[0].DeathPhase == BlitzDeathPhase.Reviving
        && battle.Enemies[0].Move == 2 && battle.Enemies[0].ForcedMove == 0,
        "幻象复活应写复活节点与最近行动后继");
    Check(BlitzKernel.CanPerformMonsterMove(battle.Enemies[0]), "复活中的敌人应可执行行动");
    BlitzKernel.ExecuteMonsterMove(ref x, rat);
    Check(battle.Enemies[0].Creature.CurrentHp == 44 && battle.Enemies[0].DeathPhase == BlitzDeathPhase.None,
        "REVIVE_MOVE 应回满生命并清除复活阶段");
    battle.Enemies[0].Flags |= BlitzEnemyFlags.PerformedMoveThisRound;
    BlitzKernel.PrepareMonsterMovesForNextRound(ref x);
    Check(battle.Enemies[0].Move == 0 && battle.Enemies[0].ForcedMove == byte.MaxValue,
        "复活行动执行后应推进到动态后继并清空 ForcedMove");
}

// A rules table with a root entry and one spawnable entry, both for NIBBIT, plus the synthetic
// revive node so the revive contract can force it.
static BlitzRules SpawnRules()
{
    BlitzRegistry.TryMonster("NIBBIT", out ushort nibbit);
    BlitzRulesBuilder builder = new() { RootEnemyCount = 1, HasEncounter = true, EncounterSlots = ["first", "second", "third"] };
    builder.SetMonster(0, SpawnMonsterRules(nibbit));
    builder.AddSpawnableMonster(SpawnMonsterRules(nibbit));
    return builder.Build();
}

static BlitzRules NoEncounterRules()
{
    BlitzRegistry.TryMonster("NIBBIT", out ushort nibbit);
    BlitzRulesBuilder builder = new();
    builder.SetMonster(0, SpawnMonsterRules(nibbit));
    return builder.Build();
}

static BlitzMonsterRules SpawnMonsterRules(ushort nibbit, bool initialMoveLogged = false) => new()
{
    MonsterId = nibbit,
    Nodes =
    [
        new BlitzMonsterNode { NativeId = "BUTT_MOVE", Kind = BlitzMonsterNodeKind.Move, FollowUp = 0, Attacks = [new(12, 1)] },
        new BlitzMonsterNode { NativeId = "STUNNED", Kind = BlitzMonsterNodeKind.Move, MustPerformOnceBeforeTransitioning = true },
        new BlitzMonsterNode { NativeId = "REVIVE_MOVE", Kind = BlitzMonsterNodeKind.Move, MustPerformOnceBeforeTransitioning = true },
    ],
    InitialNode = 0,
    PendingInitialMoveNode = 0,
    StunnedNode = 1,
    ReviveNode = 2,
    InitialHpMin = 17,
    InitialHpMax = 21,
    StaticInts = [],
    InitialAiRegisters = [2, 0],
    InitialMoveLogged = initialMoveLogged,
};

static bool SameRng(in BlitzRngState left, in BlitzRngState right)
    => left.Counter == right.Counter && left.S0 == right.S0 && left.S1 == right.S1
        && left.S2 == right.S2 && left.S3 == right.S3;

static BlitzBattle SpawnBattle()
{
    BlitzRegistry.TryMonster("NIBBIT", out ushort nibbit);
    BlitzBattle battle = new()
    {
        Round = 1, PlayerTurn = 1, Energy = 3, Side = BlitzSide.Player, Phase = BlitzPlayerPhase.Play,
        Flags = BlitzBattleFlags.PlayerHooksActive, MaxHandSize = 10,
        EnemyCount = 1, RosterCount = 1, NextCombatId = 7,
        Player = new BlitzCreature { CurrentHp = 80, MaxHp = 80, Flags = BlitzCreatureFlags.Present },
    };
    battle.Enemies[0] = new BlitzEnemy
    {
        Creature = new BlitzCreature { CurrentHp = 44, MaxHp = 44, Flags = BlitzCreatureFlags.Present },
        MonsterId = nibbit, Rules = 0, Slot = 1, CombatId = 1, Move = 0, ForcedMove = byte.MaxValue,
        Flags = BlitzEnemyFlags.InCombat,
    };
    battle.Roster[0] = 0;
    battle.RngStream(BlitzRngStream.Shuffle) = BlitzRngState.FromSerializable(0, 1, 2, 3, 4);
    battle.RngStream(BlitzRngStream.MonsterAi) = BlitzRngState.FromSerializable(0, 5, 6, 7, 8);
    battle.RngStream(BlitzRngStream.Niche) = BlitzRngState.FromSerializable(0, 9, 10, 11, 12);
    return battle;
}

// Reference CreatePredictedMonster HP draw: the eligible values are the ascending HashSet
// enumeration order; an empty set uses NextInt(min, max + 1).
static int ExpectedHp(ref BlitzRngState niche, int minimum, int maximum, IReadOnlyList<int> rosterMaxHp)
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
static void CheckA3Kernel(BlitzRules rules)
{
    Check(BlitzRegistry.TryPower("A3_TEST_RAMPART_POWER", out ushort rampart), "缺少 A3 Rampart 测试 Power");
    Check(BlitzRegistry.TryRelic("A3_TEST_GENERATED_RELIC", out ushort generatedRelic), "缺少 A3 生成遗物");
    Check(BlitzRegistry.TryPower("A3_TEST_ENDING_POWER", out ushort ending), "缺少 A3 终局测试 Power");
    Check(BlitzRegistry.TryPower("HEX_POWER", out ushort hex), "缺少 HEX_POWER");
    Check(BlitzRegistry.TryAffliction("HEXED", out ushort hexed), "缺少 HEXED");
    Check(BlitzRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD");

    // PersistentPowerSupport.TriggerRampart: the player-side turn start calls the Rampart hook.
    BlitzBattle battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzExec x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, rampart, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    Kg2TestLog.Clear();
    Span<byte> player = stackalloc byte[1];
    player[0] = BlitzLimits.PlayerOwner;
    BlitzKernel.TriggerSideTurnStart(ref x, BlitzSide.Player, player, decrementPlating: false);
    Check(Kg2TestLog.Lines.Contains("rampart"), "A3 Rampart 派发未生效");

    // AfterCardGeneratedForCombat receives the absolute card position.
    BlitzBattle generated = NewBattle();
    generated.Flags |= BlitzBattleFlags.PlayerHooksActive;
    generated.RelicCount = 1;
    generated.Relics[0] = new BlitzRelic { Id = generatedRelic };
    BlitzExec g = new(ref generated, rules, default);
    BlitzPiles.Append(ref generated, BlitzPile.Hand, BlitzKernel.CreateCard(rules,
        defend));
    Kg2TestLog.Clear();
    Check(BlitzKernel.AddGeneratedCard(ref g,
        BlitzKernel.CreateCard(rules, defend), BlitzPile.Discard),
        "A3 生成卡加入失败");
    Check(Kg2TestLog.Lines.Contains("generated-ok"), "A3 AfterCardGeneratedForCombat 位置错误");

    // HexPower.TryModifyKeywordsInCombat gives a Hexed card Ethereal without storing it.
    BlitzBattle keywords = NewBattle();
    BlitzExec k = new(ref keywords, rules, default);
    BlitzCard hexedCard = BlitzKernel.CreateCard(rules, defend);
    hexedCard.AfflictionId = (byte)(hexed + 1);
    Check(!BlitzKernel.HasKeyword(keywords, hexedCard, BlitzKeywords.Ethereal), "A3 无 Hex 时不应有 Ethereal");
    BlitzKernel.ApplyPower(ref k, BlitzLimits.PlayerOwner, hex, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref k);
    Check(BlitzKernel.HasKeyword(keywords, hexedCard, BlitzKeywords.Ethereal), "A3 Hex 未授予 Ethereal");

    // ShouldStopCombatFromEnding: a positive flag keeps the ending check false.
    BlitzBattle endingBattle = NewBattle();
    endingBattle.EnemyCount = 1;
    endingBattle.RosterCount = 1;
    endingBattle.Roster[0] = 0;
    ref BlitzEnemy enemy = ref endingBattle.Enemies[0];
    enemy.Creature.Flags = BlitzCreatureFlags.Present;
    enemy.Creature.CurrentHp = 0;
    enemy.Flags = BlitzEnemyFlags.InCombat;
    endingBattle.PowerCount = 1;
    endingBattle.Powers[0] = new BlitzPower { Id = ending, Owner = BlitzLimits.FirstEnemyOwner, Amount = 1 };
    Check(!BlitzKernel.IsEnding(endingBattle), "A3 PreventsCombatEnding 未阻止终局");
    endingBattle.Powers[0].Amount = 0;
    Check(BlitzKernel.IsEnding(endingBattle), "A3 无阻止 Power 时终局应成立");
}

static void CheckKg2Hooks(BlitzRules rules)
{
    Check(BlitzRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD 定义");
    Check(BlitzRegistry.TryPower("KG2_TEST_COST_POWER", out ushort costPower), "缺少 KG2 费用 Power");
    Check(BlitzRegistry.TryPower("KG2_TEST_PLAY_COUNT_POWER", out ushort countPower), "缺少 KG2 出牌次数 Power");
    Check(BlitzRegistry.TryPower("KG2_TEST_RESULT_LOCATION_POWER", out ushort locationPower), "缺少 KG2 去向 Power");
    Check(BlitzRegistry.TryPower("KG2_TEST_POWER_CHANGE_POWER", out ushort changePower), "缺少 KG2 Power 数量 Power");
    Check(BlitzRegistry.TryPower("KG2_TEST_DAMAGE_SOURCE_POWER", out ushort damagePower), "缺少 KG2 伤害来源 Power");
    Check(BlitzRegistry.TryRelic("KG2_TEST_COST_RELIC", out ushort costRelic), "缺少 KG2 费用遗物");
    Check(BlitzRegistry.TryRelic("KG2_TEST_X_VALUE_RELIC", out ushort xRelic), "缺少 KG2 X 值遗物");
    Check(BlitzRegistry.TryRelic("KG2_TEST_RESET_RELIC", out ushort resetRelic), "缺少 KG2 能量重置遗物");
    Check(BlitzRegistry.TryRelic("KG2_TEST_RECEIVED_RELIC", out ushort receivedRelic), "缺少 KG2 Power 数量接收遗物");
    Check(BlitzRegistry.TryRelic("KG2_TEST_SHUFFLE_RELIC", out ushort shuffleRelic), "缺少 KG2 洗牌遗物");
    Check(BlitzRegistry.TryRelic("KG2_TEST_ENTERED_RELIC", out ushort enteredRelic), "缺少 KG2 进战斗遗物");
    Check(BlitzRegistry.TryRelic("KG2_TEST_POWER_CHANGE_RELIC", out ushort changeRelic), "缺少 KG2 Power 数量遗物");
    Check(BlitzRegistry.TryPower("VULNERABLE_POWER", out ushort VulnerableId), "缺少 VULNERABLE_POWER");
    Check(BlitzRegistry.TryPower("STRENGTH_POWER", out ushort StrengthId), "缺少 STRENGTH_POWER");

    BlitzBattle battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzCard card = BlitzKernel.CreateCard(rules, defend);
    BlitzPiles.Append(ref battle, BlitzPile.Hand, card);
    battle.RelicCount = 6;
    battle.Relics[0] = new BlitzRelic { Id = costRelic };
    battle.Relics[1] = new BlitzRelic { Id = xRelic };
    battle.Relics[2] = new BlitzRelic { Id = resetRelic };
    battle.Relics[3] = new BlitzRelic { Id = receivedRelic };
    battle.Relics[4] = new BlitzRelic { Id = shuffleRelic };
    battle.Relics[5] = new BlitzRelic { Id = enteredRelic };
    BlitzExec x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, costPower, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);

    // Normal pass order: power then relic; late pass: power. Multiplicative late makes the order visible.
    Kg2TestLog.Clear();
    int cost = BlitzKernel.EnergyCostWithModifiers(battle, rules, card, BlitzPile.Hand);
    Check(cost == 10, $"KG2 费用顺序应为 10，实际 {cost} log={string.Join(",", Kg2TestLog.Lines)}");
    Check(string.Join(",", Kg2TestLog.Lines) == "cost-normal-power,cost-normal-relic,cost-late-power",
        $"KG2 费用监听顺序错误：{string.Join(",", Kg2TestLog.Lines)}");
    battle.Outcome = BlitzOutcome.Victory;
    int endedCost = BlitzKernel.EnergyCostWithModifiers(battle, rules, card, BlitzPile.Hand);
    Check(endedCost == 1 && Kg2TestLog.Lines.Count == 3, "KG2 费用 Hook 应在战斗结束时跳过");
    battle.Outcome = BlitzOutcome.None;

    // Result location: the changed power is recorded and its after hook runs.
    Kg2TestLog.Clear();
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, locationPower, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    Span<BlitzListener> locationModifiers = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
    BlitzPile location = BlitzHooks.ModifyCardPlayResultLocation(ref x, card, false, 0, BlitzPile.Discard,
        BlitzPilePosition.Bottom, locationModifiers, out int locationCount, out BlitzPilePosition locationPosition);
    Check(location == BlitzPile.Draw && locationPosition == BlitzPilePosition.Top && locationCount == 1,
        "KG2 去向 Hook 未生效（含 Draw/Top 位置）");
    BlitzHooks.AfterModifyingCardPlayResultLocation(ref x, card, location, locationModifiers[..locationCount]);
    Check(Kg2TestLog.Lines.Contains("location-after"), "KG2 去向 After Hook 未派发");

    // Play count: changed listener recorded, after only for changed listeners.
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, countPower, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    Kg2TestLog.Clear();
    Span<BlitzListener> countModifiers = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
    int playCount = BlitzHooks.ModifyCardPlayCount(ref x, card, BlitzLimits.NoOwner, 1, countModifiers,
        out int countModifierCount);
    Check(playCount == 2 && countModifierCount == 1, "KG2 出牌次数 Hook 未生效");
    BlitzHooks.AfterModifyingCardPlayCount(ref x, card, countModifiers[..countModifierCount]);
    Check(Kg2TestLog.Lines.Contains("count-after"), "KG2 出牌次数 After Hook 未派发");

    // AfterPowerAmountChanged is power-only; the relic override must not be visited.
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, changePower, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    // The relic hook is registered on a relic, not on a power and must not be visited by the
    // power-only AfterPowerAmountChanged dispatch.
    battle.RelicCount++;
    battle.Relics[battle.RelicCount - 1] = new BlitzRelic { Id = changeRelic };
    Kg2TestLog.Clear();
    BlitzKernel.ApplyPower(ref x, BlitzLimits.FirstEnemyOwner, VulnerableId, 1,
        BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    Check(Kg2TestLog.Lines.Contains("power-changed"), "KG2 AfterPowerAmountChanged 未派发");
    Check(Kg2TestLog.Lines.Contains("power-changed-target"), "KG2 AfterPowerAmountChanged 未传目标");
    Check(!Kg2TestLog.Lines.Contains("power-changed-relic"), "KG2 AfterPowerAmountChanged 不应派发遗物");

    // AfterShuffle, ModifyXValue, ShouldPlayerResetEnergy, AfterCardEnteredCombat.
    Kg2TestLog.Clear();
    BlitzPiles.Append(ref battle, BlitzPile.Discard, BlitzKernel.CreateCard(rules, defend));
    battle.DrawCount = 0;
    BlitzKernel.Shuffle(ref x);
    Check(Kg2TestLog.Lines.Contains("shuffle"), "KG2 AfterShuffle 未派发");
    Check(BlitzHooks.ModifyXValue(battle, rules, card, 5) == 6, "KG2 ModifyXValue 未生效");
    Check(!BlitzHooks.ShouldPlayerResetEnergy(battle, rules, BlitzLimits.PlayerOwner), "KG2 能量重置判定未生效");
    Kg2TestLog.Clear();
    Check(BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CreateCard(rules, defend), BlitzPile.Hand),
        "KG2 生成卡加入失败");
    Check(Kg2TestLog.Lines.Contains("entered"), "KG2 遗物 AfterCardEnteredCombat 未派发");

    // Received power amount: the doubling pass runs before the Artifact check and the after records it.
    Kg2TestLog.Clear();
    int applied = BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
        StrengthId, 2, BlitzLimits.PlayerOwner);
    Check(applied == 4, $"KG2 接收 Power 数量应为 4，实际 {applied}");
    Check(Kg2TestLog.Lines.Contains("received-after"), "KG2 AfterModifyingPowerAmountReceived 未派发");

    // AfterDamageReceived carries the card source.
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, damagePower, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    Kg2TestLog.Clear();
    Span<byte> enemy = stackalloc byte[1];
    enemy[0] = BlitzLimits.FirstEnemyOwner;
    BlitzKernel.Damage(ref x, enemy, 1, BlitzValueProps.Move, BlitzLimits.PlayerOwner, true, card);
    Check(Kg2TestLog.Lines.Contains("damage-card"), $"KG2 AfterDamageReceived 缺少卡牌来源 log={string.Join(",", Kg2TestLog.Lines)} outcome={battle.Outcome} flags={battle.Flags} hooks={BlitzRegistry.Power(damagePower).Hooks}");
    Kg2TestLog.Clear();
    BlitzKernel.Damage(ref x, enemy, 1, BlitzValueProps.Move, BlitzLimits.PlayerOwner, false, default);
    Check(Kg2TestLog.Lines.Contains("damage-none"), $"KG2 AfterDamageReceived 无来源分支错误 log={string.Join(",", Kg2TestLog.Lines)}");
}

// KA: card afflictions/enchantments, their modification hooks and the enchantment relic.
static void CheckKaAttachments()
{
    BlitzRules rules = KaRules();
    Check(BlitzRegistry.TryAffliction("TAINTED", out ushort tainted), "缺少 TAINTED 定义");
    Check(BlitzRegistry.TryAffliction("ENTANGLED", out ushort entangled), "缺少 ENTANGLED 定义");
    Check(BlitzRegistry.TryAffliction("RINGING", out ushort ringing), "缺少 RINGING 定义");
    Check(BlitzRegistry.TryAffliction("SMOG", out ushort smog), "缺少 SMOG 定义");
    Check(BlitzRegistry.TryEnchantment("SHARP", out ushort sharp), "缺少 SHARP 定义");
    Check(BlitzRegistry.TryEnchantment("GLAM", out _), "缺少 GLAM 定义");
    Check(BlitzRegistry.TryEnchantment("SPIRAL", out _), "缺少 SPIRAL 定义");
    Check(BlitzRegistry.TryPower("VITAL_SPARK_POWER", out ushort vitalSpark), "缺少 VITAL_SPARK_POWER");
    Check(BlitzRegistry.TryPower("SMOGGY_POWER", out ushort smoggy), "缺少 SMOGGY_POWER");
    Check(BlitzRegistry.TryPower("TANGLED_POWER", out ushort tangled), "缺少 TANGLED_POWER");
    Check(BlitzRegistry.TryPower("RINGING_POWER", out ushort ringingPower), "缺少 RINGING_POWER");
    Check(BlitzRegistry.TryPower("TAINTED_POWER", out ushort taintedPower), "缺少 TAINTED_POWER");
    Check(BlitzRegistry.TryRelic("MYSTIC_LIGHTER", out ushort lighter), "缺少 MYSTIC_LIGHTER");

    Check(BlitzRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike), "缺少 STRIKE_IRONCLAD");
    Check(BlitzRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD");
    BlitzBattle battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, strike));
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, defend));
    BlitzExec x = new(ref battle, rules, default);
    int attackPosition = 0;
    int skillPosition = 1;

    // CardCmd.Afflict: Tainted is Skill-only and stacks; Entangled is non-stackable.
    BlitzKernel.ApplyAffliction(ref x, skillPosition, tainted, 2);
    Check(battle.Cards[skillPosition].AfflictionId == tainted + 1
        && battle.Cards[skillPosition].AfflictionAmount == 2, "Tainted 应施加到技能牌");
    BlitzKernel.ApplyAffliction(ref x, skillPosition, tainted, 3);
    Check(battle.Cards[skillPosition].AfflictionAmount == 5, "可叠加苦难应累加层数");
    BlitzKernel.ApplyAffliction(ref x, attackPosition, tainted, 1);
    Check(battle.Cards[attackPosition].AfflictionId == 0, "Tainted 不应施加到攻击牌");
    BlitzKernel.ApplyAffliction(ref x, attackPosition, entangled, 1);
    BlitzKernel.ApplyAffliction(ref x, attackPosition, entangled, 1);
    Check(battle.Cards[attackPosition].AfflictionId == entangled + 1
        && battle.Cards[attackPosition].AfflictionAmount == 1, "不可叠加苦难只应保留首次层数");
    BlitzKernel.ClearAfflictions(ref battle, entangled);
    Check(battle.Cards[attackPosition].AfflictionId == 0, "ClearAfflictions 应清除同类苦难");

    // Vital Spark one-time entry affliction, amount-change rewrite and removal clearing.
    battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, defend));
    x = new(ref battle, rules, default);
    byte enemyOwner = BlitzKernel.EnemyOwner(0);
    BlitzKernel.ApplyPower(ref x, enemyOwner, vitalSpark, 2, enemyOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    BlitzKernel.NormalizeCardAfflictions(ref x);
    Check(battle.Cards[0].AfflictionId == tainted + 1 && battle.Cards[0].AfflictionAmount == 2,
        "新进场技能牌应获得 Vital Spark 的 Tainted");
    BlitzKernel.SetPowerAmount(ref battle, enemyOwner, vitalSpark, 3);
    BlitzKernel.NormalizeCardAfflictions(ref x);
    Check(battle.Cards[0].AfflictionAmount == 3, "Vital Spark 数量变化应同步 Tainted 层数");
    BlitzKernel.SetPowerAmount(ref battle, enemyOwner, vitalSpark, 0);
    BlitzKernel.NormalizeCardAfflictions(ref x);
    Check(battle.Cards[0].AfflictionId == 0, "Life Spark 移除后应清除 Tainted");

    // SmoggyPower: a Skill played this side turn makes newly entered Skills unplayable.
    battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, defend));
    x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, smoggy, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    battle.SetCounter(BlitzCounter.SkillPlayedThisSideTurn, 1);
    BlitzKernel.NormalizeCardAfflictions(ref x);
    Check(battle.Cards[0].AfflictionId == smog + 1, "SmoggyPower 应给进场技能牌施加 Smog");
    Check(!BlitzKernel.CanPlayCard(battle, rules, 0), "Smog 牌应不可打出");

    // TangledPower cost and RingingPower second-card restriction.
    battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, strike));
    x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, tangled, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    BlitzKernel.ApplyAffliction(ref x, 0, entangled, 1);
    Check(BlitzKernel.EnergyCostWithModifiers(battle, rules, battle.Cards[0], BlitzPile.Hand) == 2,
        "TangledPower 应给 Entangled 牌加 1 费");
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, ringingPower, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    BlitzKernel.ClearAfflictions(ref battle, entangled);
    BlitzKernel.ApplyAffliction(ref x, 0, ringing, 1);
    battle.SetCounter(BlitzCounter.CardPlaySeriesStartedThisTurn, 0);
    Check(BlitzKernel.CanPlayCard(battle, rules, 0), "Ringing 牌应能作为本回合第一张打出");
    battle.SetCounter(BlitzCounter.CardPlaySeriesStartedThisTurn, 1);
    Check(!BlitzKernel.CanPlayCard(battle, rules, 0), "Ringing 牌不应作为本回合第二张打出");

    // TaintedPower adds to the player's powered attacks.
    battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, taintedPower, 2, BlitzLimits.NoOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    decimal taintedDamage = BlitzHooks.ModifyDamage(ref x, BlitzLimits.PlayerOwner, enemyOwner, 6m,
        BlitzValueProps.Move, hasCardSource: false, default);
    Check(taintedDamage == 8m, $"TaintedPower 应加 2 伤害，实际 {taintedDamage}");

    // Enchantment damage/block modifiers and play count.
    battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzPiles.Append(ref battle, BlitzPile.Hand, BlitzKernel.CreateCard(rules, strike));
    x = new(ref battle, rules, default);
    ref BlitzCard enchanted = ref battle.Cards[0];
    enchanted.EnchantmentId = checked((byte)(sharp + 1));
    enchanted.EnchantmentAmount = 3;
    decimal sharpDamage = BlitzHooks.ModifyDamage(ref x, enemyOwner, BlitzLimits.PlayerOwner, 6m,
        BlitzValueProps.Move, hasCardSource: true, enchanted);
    Check(sharpDamage == 9m, $"Sharp 应加 3 伤害，实际 {sharpDamage}");
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = lighter };
    decimal lighterDamage = BlitzHooks.ModifyDamage(ref x, enemyOwner, BlitzLimits.PlayerOwner, 6m,
        BlitzValueProps.Move, hasCardSource: true, enchanted);
    Check(lighterDamage == 18m, $"MysticLighter 应给附魔攻击加 9 伤害，实际 {lighterDamage}");

    BlitzCard glam = BlitzKernel.CreateCard(rules, strike);
    glam.EnchantmentId = checked((byte)(BlitzRegistry.EnchantmentId<Glam>() + 1));
    Check(BlitzKernel.EnchantedReplayCount(glam) == 1, "Glam 首次应加 1 次重放");
    glam.EnchantmentPrivate = 1;
    Check(BlitzKernel.EnchantedReplayCount(glam) == 0, "Glam 已用后不应再加重放");
    BlitzCard spiral = BlitzKernel.CreateCard(rules, strike);
    spiral.EnchantmentId = checked((byte)(BlitzRegistry.EnchantmentId<Spiral>() + 1));
    Check(BlitzKernel.EnchantedReplayCount(spiral) == 1, "Spiral 应加 1 次重放");
}

static BlitzRules KaRules()
{
    BlitzRulesBuilder builder = new() { BaseMaxEnergy = 3, HasEncounter = true, EncounterSlots = ["a"], RootEnemyCount = 0 };
    BlitzRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike);
    BlitzRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend);
    BlitzRegistry.TryRelic("MYSTIC_LIGHTER", out ushort lighter);
    builder.SetCard(strike, 0, new BlitzCardStats
    {
        Type = BlitzCardType.Attack, Target = BlitzTargetType.AnyEnemy, Rarity = BlitzCardRarity.Basic,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false, Keywords = BlitzKeywords.None,
        MaxUpgradeLevel = 1, Vars = [6m],
    });
    builder.SetCard(defend, 0, new BlitzCardStats
    {
        Type = BlitzCardType.Skill, Target = BlitzTargetType.Self, Rarity = BlitzCardRarity.Basic,
        EnergyCost = 1, CostsX = false, StarCost = -1, StarCostsX = false, Keywords = BlitzKeywords.None,
        MaxUpgradeLevel = 1, Vars = [5m],
    });
    builder.SetRelicVars(lighter, [9m]);
    return builder.Build();
}

// RG: star and forge kernel contracts. The helper definitions below are registered only in this
// pure-kernel executable; the star relic/power log the dispatch order and the paid amount.
static void CheckRgKernel(BlitzRules rules)
{
    Check(BlitzRegistry.TryCard("SOVEREIGN_BLADE", out ushort blade), "缺少 SOVEREIGN_BLADE 定义");
    Check(BlitzRegistry.TryCard("RG_TEST_STAR_CARD", out ushort starCard), "缺少 RG_TEST_STAR_CARD 定义");
    Check(BlitzRegistry.TryPower("RG_TEST_STAR_POWER", out ushort starPower), "缺少 RG_TEST_STAR_POWER");
    Check(BlitzRegistry.TryRelic("RG_TEST_STAR_RELIC", out ushort starRelic), "缺少 RG_TEST_STAR_RELIC");

    // Star cost: the hook only sees non-negative costs, runs in listener order and is gated by combat
    // ending; the temporary cost replaces the base; X star cost returns every current star without
    // dispatching the hook.
    BlitzBattle battle = NewBattle();
    battle.Flags |= BlitzBattleFlags.PlayerHooksActive;
    battle.RelicCount = 1;
    battle.Relics[0] = new BlitzRelic { Id = starRelic };
    BlitzCard card = BlitzKernel.CreateCard(rules, starCard);
    Kg2TestLog.Clear();
    Check(BlitzKernel.StarCostWithModifiers(battle, rules, card, BlitzPile.Hand) == 0,
        "RG 星能费用 Hook 未生效（应把 2 降到 -2 后钳制到 0）");
    Check(Kg2TestLog.Lines.Count == 1 && Kg2TestLog.Lines[0] == "star-cost:2",
        $"RG 星能费用 Hook 应只收到一次 2，实际 {string.Join(",", Kg2TestLog.Lines)}");
    card.SetTemporaryStarCost(5, clearsAtTurnEnd: true, clearsWhenPlayed: true);
    Kg2TestLog.Clear();
    Check(BlitzKernel.StarCostWithModifiers(battle, rules, card, BlitzPile.Hand) == 1
        && Kg2TestLog.Lines.Count == 1 && Kg2TestLog.Lines[0] == "star-cost:5",
        "RG 临时星能费用应替换基础费用");
    card.Flags |= BlitzCardFlags.StarCostsX;
    battle.Stars = 3;
    Kg2TestLog.Clear();
    Check(BlitzKernel.StarCostWithModifiers(battle, rules, card, BlitzPile.Hand) == 3
        && Kg2TestLog.Lines.Count == 0, "RG X 星能费用应返回当前星能且不派发 Hook");
    card.Flags &= ~BlitzCardFlags.StarCostsX;
    battle.Outcome = BlitzOutcome.Victory;
    Kg2TestLog.Clear();
    Check(BlitzKernel.StarCostWithModifiers(battle, rules, card, BlitzPile.Hand) == 5
        && Kg2TestLog.Lines.Count == 0, "RG 战斗结束时星能费用 Hook 应跳过");
    battle.Outcome = BlitzOutcome.None;
    card.ClearTemporaryStarCost();
    card.BaseStarCost = -1;
    Kg2TestLog.Clear();
    Check(BlitzKernel.StarCostWithModifiers(battle, rules, card, BlitzPile.Hand) == 0
        && Kg2TestLog.Lines.Count == 0, "RG 负星能费用不应派发 Hook");

    // AfterStarsSpent: the existing engine runs the relics before the powers, not in listener order.
    BlitzExec x = new(ref battle, rules, default);
    BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, starPower, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref x);
    Kg2TestLog.Clear();
    BlitzHooks.AfterStarsSpent(ref x, 4);
    Check(string.Join(",", Kg2TestLog.Lines) == "stars-spent-relic:4,stars-spent-power:4",
        $"RG 星能消耗顺序错误：{string.Join(",", Kg2TestLog.Lines)}");

    // GainStars: clamp, then AfterStarsGained; skipped while the combat is ending.
    battle.Stars = BlitzLimits.ValueClamp - 2;
    Kg2TestLog.Clear();
    Check(BlitzKernel.GainStars(ref x, 5), "RG 星能获得不应产生待处理选择");
    Check(battle.Stars == BlitzLimits.ValueClamp, "RG 星能获得应钳制到上限");
    Check(Kg2TestLog.Lines.Contains("stars-gained:5"), "RG 星能获得 Hook 未派发");
    battle.Stars = 0;
    battle.Enemies[0].Creature.CurrentHp = 0;
    Check(BlitzKernel.IsEnding(battle), "RG 终局测试前提不成立");
    Kg2TestLog.Clear();
    Check(BlitzKernel.GainStars(ref x, 5) && battle.Stars == 0 && Kg2TestLog.Lines.Count == 0,
        "RG 战斗结束时星能获得应跳过");
    battle.Enemies[0].Creature.CurrentHp = 100;

    // Star payment: ManualPlay deducts the star cost once and dispatches AfterStarsSpent with the paid
    // amount (K0 contract for the "the count changed by exactly the paid amount" assumption). No
    // cost-modifying relic here, so the base cost 2 is paid.
    BlitzBattle paid = NewBattle();
    paid.Flags |= BlitzBattleFlags.PlayerHooksActive;
    BlitzPiles.Append(ref paid, BlitzPile.Hand, BlitzKernel.CreateCard(rules, starCard));
    BlitzExec pay = new(ref paid, rules, default);
    BlitzKernel.ApplyPower(ref pay, BlitzLimits.PlayerOwner, starPower, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref pay);
    paid.Stars = 5;
    Kg2TestLog.Clear();
    BlitzKernel.ManualPlay(ref pay, 0, BlitzLimits.NoOwner);
    Check(paid.Stars == 3, $"RG 出牌应恰好支付 2 星能，实际星能 {paid.Stars}");
    Check(string.Join(",", Kg2TestLog.Lines) == "stars-spent-power:2",
        $"RG 出牌应只派发一次金额 2 的星能消耗 Hook：{string.Join(",", Kg2TestLog.Lines)}");

    // Forge: grows every Sovereign Blade in every pile; creates one canonical blade only when no
    // unexhausted blade exists; the freshly created blade receives the same forge amount.
    BlitzBattle forgeBattle = NewBattle();
    BlitzExec forge = new(ref forgeBattle, rules, default);
    BlitzCard bladeValue = BlitzKernel.CreateCard(rules, blade);
    BlitzPiles.Append(ref forgeBattle, BlitzPile.Hand, bladeValue);
    BlitzPiles.Append(ref forgeBattle, BlitzPile.Discard, bladeValue);
    Check(BlitzKernel.Forge(ref forge, 3), "RG 锻造失败");
    Check(forgeBattle.CardCount == 2, "RG 已有未消耗君王之刃时不应再生成");
    Check(forgeBattle.Cards[0].Var0 == 3 && forgeBattle.Cards[1].Var0 == 3,
        "RG 锻造应增加所有君王之刃的伤害变量");

    BlitzBattle exhaustedOnly = NewBattle();
    BlitzExec forge2 = new(ref exhaustedOnly, rules, default);
    BlitzPiles.Append(ref exhaustedOnly, BlitzPile.Exhaust, bladeValue);
    Check(BlitzKernel.Forge(ref forge2, 4), "RG 消耗堆君王之刃的锻造失败");
    Check(exhaustedOnly.HandCount == 1 && exhaustedOnly.ExhaustCount == 1,
        "RG 只有消耗堆君王之刃时应生成一张新牌");
    Check(exhaustedOnly.Cards[0].Var0 == 4 && exhaustedOnly.Cards[1].Var0 == 4,
        "RG 新生成的君王之刃也应获得本次锻造量");

    // StarsGainedThisTurn: positive gains accumulate (Radiate's CalculatedHits), non-positive gains do
    // not, and the player side turn start resets it (BeginSideTurn's ResetTurnCounter).
    BlitzBattle turn = SliceBattle();
    BlitzExec turnExec = new(ref turn, rules, default);
    turn.Stars = 10;
    Check(BlitzKernel.GainStars(ref turnExec, 4), "RG 星能获得不应产生待处理选择");
    Check(turn.Counter(BlitzCounter.StarsGainedThisTurn) == 4,
        $"RG 本回合获得星能应为 4，实际 {turn.Counter(BlitzCounter.StarsGainedThisTurn)}");
    Check(BlitzKernel.GainStars(ref turnExec, -2), "RG 负星能获得不应产生待处理选择");
    Check(turn.Counter(BlitzCounter.StarsGainedThisTurn) == 4,
        "RG 负星能获得不应计入本回合获得星能");
    BlitzKernel.Step(ref turn, BlitzAction.EndTurn(), rules, out _);
    Check(turn.Counter(BlitzCounter.StarsGainedThisTurn) == 0,
        $"RG 玩家回合开始应清零本回合获得星能，实际 {turn.Counter(BlitzCounter.StarsGainedThisTurn)}");

    // The blade targets every enemy while the owner holds SeekingEdgePower.
    Check(BlitzRegistry.TryPower("SEEKING_EDGE_POWER", out ushort seekingEdge), "缺少 SEEKING_EDGE_POWER");
    BlitzBattle targetBattle = NewBattle();
    BlitzExec target = new(ref targetBattle, rules, default);
    BlitzCard bladeCard = BlitzKernel.CreateCard(rules, blade);
    Check(BlitzRegistry.Card(blade).TargetType(targetBattle, rules, bladeCard) == BlitzTargetType.AnyEnemy,
        "RG 君王之刃默认目标应为单体");
    BlitzKernel.ApplyPower(ref target, BlitzLimits.PlayerOwner, seekingEdge, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref target);
    Check(BlitzRegistry.Card(blade).TargetType(targetBattle, rules, bladeCard) == BlitzTargetType.AllEnemies,
        "RG 寻求锋芒下君王之刃目标应为全体");
}


// LR: new kernel capabilities. Turn-start choices, the fixed transform, the generated-card and
// per-target powered-hit counters, and VoidForm's free-play window / forced turn end.
static void CheckLrKernel(BlitzRules rules)
{
    Check(BlitzRegistry.TryCard("STRIKE_IRONCLAD", out ushort strike), "缺少 STRIKE_IRONCLAD");
    Check(BlitzRegistry.TryCard("DEFEND_IRONCLAD", out ushort defend), "缺少 DEFEND_IRONCLAD");
    Check(BlitzRegistry.TryCard("SOVEREIGN_BLADE", out ushort blade), "缺少 SOVEREIGN_BLADE");
    Check(BlitzRegistry.TryCard("RG_TEST_STAR_CARD", out ushort starCard), "缺少 RG_TEST_STAR_CARD");
    Check(BlitzRegistry.TryCard("MINION_STRIKE", out ushort minionStrike), "缺少 MINION_STRIKE");
    Check(BlitzRegistry.TryPower("VOID_FORM_POWER", out ushort voidForm), "缺少 VOID_FORM_POWER");
    Check(BlitzRegistry.TryPower("TYRANNY_POWER", out ushort tyranny), "缺少 TYRANNY_POWER");
    Check(BlitzRegistry.TryPower("FOREGONE_CONCLUSION_POWER", out ushort foregone), "缺少 FOREGONE_CONCLUSION_POWER");

    // Generated cards: AddGeneratedCard records History.CardGenerated before insertion, so a card
    // refused while the combat is ending still counts (HeirloomHammer clones on a killing blow).
    BlitzBattle generated = NewBattle();
    BlitzExec generate = new(ref generated, rules, default);
    Check(BlitzKernel.AddGeneratedCard(ref generate, BlitzKernel.CreateCard(rules, defend), BlitzPile.Hand),
        "LR 生成卡应加入手牌");
    Check(generated.Counter(BlitzCounter.CardsGeneratedThisCombat) == 1,
        "LR 生成卡应计数 1");
    generated.Enemies[0].Creature.CurrentHp = 0;
    Check(BlitzKernel.IsEnding(generated), "LR 终局测试前提不成立");
    int handBefore = generated.HandCount;
    Check(BlitzKernel.AddGeneratedCard(ref generate, BlitzKernel.CreateCard(rules, defend), BlitzPile.Hand),
        "LR 战斗结束时生成卡按现有引擎继续执行而不中止");
    Check(generated.HandCount == handBefore, "LR 战斗结束时生成卡不应加入牌堆");
    Check(generated.Counter(BlitzCounter.CardsGeneratedThisCombat) == 2,
        "LR 战斗结束时被拒绝的生成卡仍应计入生成数");

    // The fixed transform replaces each selected card in place and keeps equal copies distinct by
    // occurrence (CardChoiceResolution.TransformCards).
    BlitzBattle transformed = NewBattle();
    BlitzExec transform = new(ref transformed, rules, default);
    BlitzCard defendA = BlitzKernel.CreateCard(rules, defend);
    defendA.Flags |= BlitzCardFlags.PowerAfflictionChecked;
    BlitzCard strikeA = BlitzKernel.CreateCard(rules, strike);
    strikeA.Flags |= BlitzCardFlags.PowerAfflictionChecked;
    BlitzCard defendB = BlitzKernel.CreateCard(rules, defend);
    defendB.Flags |= BlitzCardFlags.PowerAfflictionChecked;
    BlitzPiles.Append(ref transformed, BlitzPile.Hand, defendA);
    BlitzPiles.Append(ref transformed, BlitzPile.Hand, strikeA);
    BlitzPiles.Append(ref transformed, BlitzPile.Hand, defendB);
    Span<int> selection = stackalloc int[2];
    selection[0] = 0;
    selection[1] = 2;
    BlitzKernel.TransformSelectedTo(ref transform, BlitzPile.Hand, selection, minionStrike, upgrade: true);
    Check(transformed.HandCount == 3, "LR 变换不应改变手牌数");
    Check(transformed.Cards[0].Id == minionStrike && transformed.Cards[0].Upgrade == 1
        && transformed.Cards[2].Id == minionStrike && transformed.Cards[2].Upgrade == 1,
        "LR 变换应原位替换两张防御并升到 1 级");
    Check(transformed.Cards[1].Id == strike, "LR 未选中的卡不应改变");
    Check(transformed.Counter(BlitzCounter.CardsGeneratedThisCombat) == 2,
        "LR 每次固定变换应计入一次生成");

    // Powered attack hits: recorded for the player's powered attacks even when fully blocked,
    // ignored for unpowered damage, and an overflowing byte is an explicit boundary.
    BlitzBattle hits = NewBattle();
    BlitzExec hit = new(ref hits, rules, default);
    Span<byte> target = stackalloc byte[1];
    target[0] = BlitzKernel.EnemyOwner(0);
    BlitzKernel.Damage(ref hit, target, 6m, BlitzValueProps.Move, BlitzLimits.PlayerOwner, false, default);
    Check(BlitzKernel.PoweredAttackHitsByPlayer(hits, target[0]) == 1, "LR 强化攻击命中应计数");
    hits.Enemies[0].Creature.Block = 100;
    Check(BlitzKernel.Damage(ref hit, target, 6m, BlitzValueProps.Move, BlitzLimits.PlayerOwner, false, default),
        "LR 被完全格挡的强化攻击应报告完全格挡");
    Check(BlitzKernel.PoweredAttackHitsByPlayer(hits, target[0]) == 2,
        "LR 被完全格挡的强化攻击仍应计数");
    BlitzKernel.Damage(ref hit, target, 6m,
        BlitzValueProps.Move | BlitzValueProps.Unpowered, BlitzLimits.PlayerOwner, false, default);
    Check(BlitzKernel.PoweredAttackHitsByPlayer(hits, target[0]) == 2,
        "LR 非强化伤害不应计数");
    hits.Enemies[0].PoweredAttackHitsThisTurn = byte.MaxValue;
    bool overflowed = false;
    try
    {
        BlitzKernel.Damage(ref hit, target, 1m, BlitzValueProps.Move, BlitzLimits.PlayerOwner, false, default);
    }
    catch (BlitzUnsupportedException)
    {
        overflowed = true;
    }
    Check(overflowed, "LR 强化攻击命中数溢出应抛 BlitzUnsupportedException");
    BlitzBattle reset = SliceBattle();
    reset.Enemies[0].PoweredAttackHitsThisTurn = 3;
    BlitzKernel.Step(ref reset, BlitzAction.EndTurn(), rules, out _);
    Check(reset.Enemies[0].PoweredAttackHitsThisTurn == 0,
        "LR 玩家回合开始应清零强化攻击命中数");

    // Action choice capacity: WithChoice refuses the 33rd selection explicitly.
    BlitzAction choices = BlitzAction.EndTurn();
    for (int index = 0; index < BlitzLimits.MaxChoices; index++)
        choices = choices.WithChoice(1UL << (index & 63));
    bool capacity = false;
    try
    {
        choices.WithChoice(1);
    }
    catch (BlitzCapacityException)
    {
        capacity = true;
    }
    Check(capacity, "LR 动作选择超出容量应抛 BlitzCapacityException");

    // VoidForm's free-play window: the late energy/star hooks zero the cost while fewer than Amount
    // non-auto last-in-series plays happened this turn; the counter resets at the side turn start.
    BlitzBattle voidBattle = SliceBattle();
    BlitzExec voidExec = new(ref voidBattle, rules, default);
    BlitzKernel.ApplyPower(ref voidExec, BlitzLimits.PlayerOwner, voidForm, 2, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref voidExec);
    int voidIndex = BlitzKernel.FindPower(voidBattle, BlitzLimits.PlayerOwner, voidForm);
    Check(voidIndex >= 0, "LR VoidFormPower 未施加");
    BlitzListener voidSelf = new(BlitzListenerKind.Power, checked((byte)voidIndex), BlitzLimits.PlayerOwner);
    BlitzPowerDef voidDef = BlitzRegistry.Power(voidForm);
    BlitzCard strikeCard = BlitzKernel.CreateCard(rules, strike);
    Check(voidDef.TryModifyEnergyCostInCombatLate(voidBattle, rules, voidSelf, strikeCard, BlitzPile.Hand, 3m) == 0m,
        "LR VoidForm 首次出牌的能量费用应为 0");
    Check(voidDef.TryModifyStarCost(voidBattle, rules, voidSelf,
            BlitzKernel.CreateCard(rules, starCard), BlitzPile.Hand, 2m) == 0m,
        "LR VoidForm 首次出牌的星能费用应为 0");
    Check(voidDef.TryModifyEnergyCostInCombatLate(voidBattle, rules, voidSelf, strikeCard, BlitzPile.Draw, 3m) == 3m,
        "LR VoidForm 只应影响手牌与打出区");
    BlitzExec voidPlay = new(ref voidBattle, rules, default);
    voidDef.AfterCardPlayed(ref voidPlay, voidSelf,
        new BlitzCardPlayInfo(strikeCard, BlitzLimits.NoOwner, 0, 1, BlitzPile.Discard));
    Check(voidBattle.Powers[voidIndex].Aux0 == 1, "LR VoidForm 非自动最后一段出牌应计数");
    voidPlay.CurrentPlayIsAutoPlay = true;
    voidDef.AfterCardPlayed(ref voidPlay, voidSelf,
        new BlitzCardPlayInfo(strikeCard, BlitzLimits.NoOwner, 0, 1, BlitzPile.Discard));
    Check(voidBattle.Powers[voidIndex].Aux0 == 1, "LR VoidForm 自动打出不应计数");
    voidPlay.CurrentPlayIsAutoPlay = false;
    voidDef.AfterCardPlayed(ref voidPlay, voidSelf,
        new BlitzCardPlayInfo(strikeCard, BlitzLimits.NoOwner, 0, 2, BlitzPile.Discard));
    Check(voidBattle.Powers[voidIndex].Aux0 == 1, "LR VoidForm 非最后一段出牌不应计数");
    Check(voidDef.TryModifyEnergyCostInCombatLate(voidBattle, rules, voidSelf, strikeCard, BlitzPile.Hand, 3m) == 0m,
        "LR VoidForm 第二次出牌仍在免费窗口内");
    voidPlay.CurrentPlayIsAutoPlay = false;
    voidDef.AfterCardPlayed(ref voidPlay, voidSelf,
        new BlitzCardPlayInfo(strikeCard, BlitzLimits.NoOwner, 1, 2, BlitzPile.Discard));
    Check(voidBattle.Powers[voidIndex].Aux0 == 2, "LR VoidForm 最后一段出牌应计数");
    Check(voidDef.TryModifyEnergyCostInCombatLate(voidBattle, rules, voidSelf, strikeCard, BlitzPile.Hand, 3m) == 3m,
        "LR VoidForm 免费窗口用尽后应恢复费用");
    voidDef.BeforeSideTurnStart(ref voidPlay, voidSelf, participates: true);
    Check(voidBattle.Powers[voidIndex].Aux0 == 0, "LR VoidForm 侧回合开始应清零出牌计数");
    BlitzBattle request = SliceBattle();
    BlitzKernel.RequestPlayerTurnEnd(ref request);
    Check((request.Flags & BlitzBattleFlags.PlayerTurnEndRequested) != 0,
        "LR VoidForm 的结束回合请求应置位");

    // TyrannyPower's turn-start exhaust choice is a PlayerTurnStart choice on EndTurn; replaying the
    // action with the selection exhausts the chosen hand card and the power also increases the draw.
    BlitzBattle tyrannyBattle = SliceBattle();
    BlitzExec tyrannyExec = new(ref tyrannyBattle, rules, default);
    BlitzKernel.ApplyPower(ref tyrannyExec, BlitzLimits.PlayerOwner, tyranny, 1, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref tyrannyExec);
    BlitzBattle tyrannyParent = tyrannyBattle;
    BlitzAction endTurn = BlitzAction.EndTurn();
    Check(BlitzKernel.Step(ref tyrannyBattle, endTurn, rules, out BlitzChoiceRequest tyrannyRequest)
        == BlitzStepResult.NeedsChoice, "LR 暴政应在回合开始请求选牌");
    Check(tyrannyRequest.Timing == BlitzChoiceTiming.PlayerTurnStart
        && tyrannyRequest.Effect == BlitzChoiceEffect.Exhaust
        && tyrannyRequest.Source == BlitzPile.Hand
        && tyrannyRequest.MinCount == 1 && tyrannyRequest.MaxCount == 1,
        $"LR 暴政选择规格错误 timing={tyrannyRequest.Timing} effect={tyrannyRequest.Effect} "
        + $"source={tyrannyRequest.Source} count={tyrannyRequest.MinCount}..{tyrannyRequest.MaxCount}");
    BlitzBattle tyrannyReplay = tyrannyParent;
    Check(BlitzKernel.Step(ref tyrannyReplay, endTurn.WithChoice(1UL), rules, out _)
        == BlitzStepResult.Completed, "LR 暴政选择后回合应完成");
    Check(tyrannyReplay.ExhaustCount == 1, $"LR 暴政应消耗 1 张选中的手牌，实际 {tyrannyReplay.ExhaustCount}");

    // ForegoneConclusionPower: its BeforeHandDraw choice moves draw cards to hand and removes the
    // power; the empty-draw branch shuffles first and asks nothing.
    BlitzBattle foregoneBattle = SliceBattle();
    BlitzExec foregoneExec = new(ref foregoneBattle, rules, default);
    BlitzKernel.ApplyPower(ref foregoneExec, BlitzLimits.PlayerOwner, foregone, 2, BlitzLimits.PlayerOwner);
    BlitzKernel.ResolvePowerAmountChanges(ref foregoneExec);
    BlitzBattle foregoneParent = foregoneBattle;
    BlitzAction foregoneEnd = BlitzAction.EndTurn();
    Check(BlitzKernel.Step(ref foregoneBattle, foregoneEnd, rules, out BlitzChoiceRequest foregoneRequest)
        == BlitzStepResult.NeedsChoice, "LR 先见之明应在摸牌前请求选牌");
    Check(foregoneRequest.Timing == BlitzChoiceTiming.PlayerTurnStart
        && foregoneRequest.Effect == BlitzChoiceEffect.MoveToHand
        && foregoneRequest.Source == BlitzPile.Draw
        && foregoneRequest.MinCount == 2 && foregoneRequest.MaxCount == 2,
        $"LR 先见之明选择规格错误 timing={foregoneRequest.Timing} effect={foregoneRequest.Effect} "
        + $"source={foregoneRequest.Source} count={foregoneRequest.MinCount}..{foregoneRequest.MaxCount}");
    BlitzBattle foregoneReplay = foregoneParent;
    Check(BlitzKernel.Step(ref foregoneReplay, foregoneEnd.WithChoice(0b11UL), rules, out _)
        == BlitzStepResult.Completed, "LR 先见之明选择后回合应完成");
    Check(BlitzKernel.GetAmount(foregoneReplay, BlitzLimits.PlayerOwner, foregone) == 0,
        "LR 先见之明结算后应移除 Power");
    Check(foregoneReplay.HandCount == 7,
        $"LR 先见之明应先移入手牌再抽 5 张，实际手牌 {foregoneReplay.HandCount}");
}

/// <summary>
/// TS: the two turn-start choice shapes the turn-setup fixtures rely on. The bridge replays the
/// setup page through the existing engine, and the kernel uses the same shapes for later turns:
/// <c>ToastyMittens</c> (exhaust one hand card, then gain Strength) and <c>ChoicesParadox</c>
/// (a generated-card fixed option set). Both choices are stamped <c>PlayerTurnStart</c>.
/// </summary>
static void CheckTsKernel(BlitzRules rules)
{
    Check(BlitzRegistry.TryRelic("TOASTY_MITTENS", out ushort toasty), "缺少 TOASTY_MITTENS");
    Check(BlitzRegistry.TryRelic("CHOICES_PARADOX", out ushort paradox), "缺少 CHOICES_PARADOX");
    Check(BlitzRegistry.TryPower("STRENGTH_POWER", out ushort strength), "缺少 STRENGTH_POWER");
    BlitzRelicDef toastyDef = BlitzRegistry.Relic(toasty);
    BlitzRelicDef paradoxDef = BlitzRegistry.Relic(paradox);
    BlitzListener self = new(BlitzListenerKind.Relic, 0, BlitzLimits.PlayerOwner);

    // ToastyMittens: the request is an Exhaust choice over the hand, stamped PlayerTurnStart.
    BlitzBattle toastyBattle = SliceBattle();
    toastyBattle.RelicCount = 1;
    toastyBattle.Relics[0] = new BlitzRelic { Id = toasty };
    int toastyHand = toastyBattle.HandCount;
    BlitzExec toastyProbe = new(ref toastyBattle, rules, default);
    toastyProbe.ChoiceTiming = BlitzChoiceTiming.PlayerTurnStart;
    toastyDef.AfterPlayerTurnStart(ref toastyProbe, self);
    Check(toastyProbe.NeedsChoice, "TS 烤面包手套应在回合开始请求选牌");
    Check(toastyProbe.Request.Kind == BlitzChoiceKind.SelectCards
        && toastyProbe.Request.Effect == BlitzChoiceEffect.Exhaust
        && toastyProbe.Request.Source == BlitzPile.Hand
        && toastyProbe.Request.Timing == BlitzChoiceTiming.PlayerTurnStart
        && toastyProbe.Request.MinCount == 1 && toastyProbe.Request.MaxCount == 1
        && toastyProbe.Request.OptionCount == toastyHand,
        $"TS 烤面包手套选择规格错误 kind={toastyProbe.Request.Kind} effect={toastyProbe.Request.Effect} "
        + $"source={toastyProbe.Request.Source} timing={toastyProbe.Request.Timing} "
        + $"count={toastyProbe.Request.MinCount}..{toastyProbe.Request.MaxCount} "
        + $"options={toastyProbe.Request.OptionCount} hand={toastyHand}");
    BlitzBattle toastyReplayBattle = toastyBattle;
    BlitzExec toastyReplay = new(ref toastyReplayBattle, rules, BlitzAction.EndTurn().WithChoice(0b1UL));
    toastyReplay.ChoiceTiming = BlitzChoiceTiming.PlayerTurnStart;
    toastyDef.AfterPlayerTurnStart(ref toastyReplay, self);
    Check(!toastyReplay.NeedsChoice && toastyReplayBattle.ExhaustCount == 1,
        $"TS 烤面包手套应消耗 1 张选中的手牌，实际消耗 {toastyReplayBattle.ExhaustCount}");
    Check(BlitzKernel.GetAmount(toastyReplayBattle, BlitzLimits.PlayerOwner, strength) == 2,
        $"TS 烤面包手套应施加 2 力量，实际 {BlitzKernel.GetAmount(toastyReplayBattle, BlitzLimits.PlayerOwner, strength)}");

    // ChoicesParadox: a generated-card option set (no pile), one selected card enters the hand and
    // carries Retain. The kernel samples CharacterAll in option order with CombatCardGeneration.
    BlitzBattle paradoxBattle = SliceBattle();
    paradoxBattle.RelicCount = 1;
    paradoxBattle.Relics[0] = new BlitzRelic { Id = paradox };
    paradoxBattle.RngStream(BlitzRngStream.CombatCardGeneration) =
        BlitzRngState.FromSerializable(0, 1, 2, 3, 4);
    int paradoxHand = paradoxBattle.HandCount;
    BlitzExec paradoxProbe = new(ref paradoxBattle, rules, default);
    paradoxProbe.ChoiceTiming = BlitzChoiceTiming.PlayerTurnStart;
    paradoxDef.AfterPlayerTurnStart(ref paradoxProbe, self);
    Check(paradoxProbe.NeedsChoice, "TS 抉择悖论应在回合开始请求固定选项");
    Check(paradoxProbe.Request.Kind == BlitzChoiceKind.SelectOption
        && paradoxProbe.Request.Effect == BlitzChoiceEffect.GenerateToHand
        && paradoxProbe.Request.OptionsAreGenerated
        && paradoxProbe.Request.SourceIsNone
        && paradoxProbe.Request.Timing == BlitzChoiceTiming.PlayerTurnStart
        && paradoxProbe.Request.OptionCount == 3,
        $"TS 抉择悖论选择规格错误 kind={paradoxProbe.Request.Kind} effect={paradoxProbe.Request.Effect} "
        + $"generated={paradoxProbe.Request.OptionsAreGenerated} timing={paradoxProbe.Request.Timing} "
        + $"options={paradoxProbe.Request.OptionCount}");
    BlitzBattle paradoxReplayBattle = paradoxBattle;
    BlitzExec paradoxReplay = new(ref paradoxReplayBattle, rules, BlitzAction.EndTurn().WithChoice(0b010UL));
    paradoxReplay.ChoiceTiming = BlitzChoiceTiming.PlayerTurnStart;
    paradoxDef.AfterPlayerTurnStart(ref paradoxReplay, self);
    Check(!paradoxReplay.NeedsChoice, "TS 抉择悖论选定后不应再请求选择");
    Check(paradoxReplayBattle.HandCount == paradoxHand + 1,
        $"TS 抉择悖论应把选中的生成卡加入手牌，实际 {paradoxReplayBattle.HandCount}");
    ref BlitzCard generatedCard = ref paradoxReplayBattle.Cards[
        BlitzPiles.Start(paradoxReplayBattle, BlitzPile.Hand) + paradoxReplayBattle.HandCount - 1];
    Check((generatedCard.Keywords & BlitzKeywords.Retain) != 0,
        "TS 抉择悖论生成的卡应带保留关键字");
}

static class Kg2TestLog
{
    public static List<string> Lines { get; } = [];

    public static void Clear() => Lines.Clear();

    public static void Add(string line) => Lines.Add(line);
}

[BlitzContent("KG2_TEST_COST_POWER")]
public sealed class Kg2TestCostPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombat(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzCard card, BlitzPile pile, decimal amount)
    {
        Kg2TestLog.Add("cost-normal-power");
        return amount + 1m;
    }

    public override decimal TryModifyEnergyCostInCombatLate(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzCard card, BlitzPile pile, decimal amount)
    {
        Kg2TestLog.Add("cost-late-power");
        return amount * 2m;
    }
}

[BlitzContent("KG2_TEST_COST_RELIC")]
public sealed class Kg2TestCostRelic : BlitzRelicDef
{
    public override decimal TryModifyEnergyCostInCombat(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzCard card, BlitzPile pile, decimal amount)
    {
        Kg2TestLog.Add("cost-normal-relic");
        return amount + 3m;
    }
}

[BlitzContent("KG2_TEST_RESULT_LOCATION_POWER")]
public sealed class Kg2TestResultLocationPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override BlitzPile ModifyCardPlayResultLocation(ref BlitzExec x, BlitzListener self, in BlitzCard card,
        bool isAutoPlay, int energyValue, BlitzPile location, out BlitzPilePosition position)
    {
        position = BlitzPilePosition.Top;
        return BlitzPile.Draw;
    }

    public override void AfterModifyingCardPlayResultLocation(ref BlitzExec x, BlitzListener self, in BlitzCard card,
        BlitzPile location) => Kg2TestLog.Add("location-after");
}

[BlitzContent("KG2_TEST_PLAY_COUNT_POWER")]
public sealed class Kg2TestPlayCountPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override int ModifyCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card, byte target,
        int playCount) => playCount + 1;

    public override void AfterModifyingCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card)
        => Kg2TestLog.Add("count-after");
}

[BlitzContent("KG2_TEST_POWER_CHANGE_POWER")]
public sealed class Kg2TestPowerChangePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterPowerAmountChanged(ref BlitzExec x, BlitzListener self, ushort powerId, int delta,
        byte applier, byte target)
    {
        Kg2TestLog.Add("power-changed");
        if (target == BlitzLimits.FirstEnemyOwner)
            Kg2TestLog.Add("power-changed-target");
    }
}

[BlitzContent("KG2_TEST_POWER_CHANGE_RELIC")]
public sealed class Kg2TestPowerChangeRelic : BlitzRelicDef
{
    public override void AfterPowerAmountChanged(ref BlitzExec x, BlitzListener self, ushort powerId, int delta,
        byte applier, byte target) => Kg2TestLog.Add("power-changed-relic");
}

[BlitzContent("A3_TEST_RAMPART_POWER")]
public sealed class A3TestRampartPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void Rampart(ref BlitzExec x, BlitzListener self) => Kg2TestLog.Add("rampart");
}

[BlitzContent("A3_TEST_GENERATED_RELIC")]
public sealed class A3TestGeneratedRelic : BlitzRelicDef
{
    public override void AfterCardGeneratedForCombat(ref BlitzExec x, BlitzListener self, int position,
        byte creator)
    {
        bool matches = creator == BlitzLimits.PlayerOwner
            && (uint)position < (uint)x.B.CardCount
            && BlitzRegistry.TryCard("DEFEND_IRONCLAD", out ushort expected)
            && x.B.Cards[position].Id == expected;
        Kg2TestLog.Add(matches ? "generated-ok" : "generated-wrong-position");
    }
}

[BlitzContent("A3_TEST_ENDING_POWER")]
public sealed class A3TestEndingPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool PreventsCombatEnding => true;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath => false;
}

[BlitzContent("KG2_TEST_X_VALUE_RELIC")]
public sealed class Kg2TestXValueRelic : BlitzRelicDef
{
    public override int ModifyXValue(in BlitzBattle battle, BlitzRules rules, BlitzListener self, in BlitzCard card,
        int value) => value + 1;
}

[BlitzContent("KG2_TEST_RESET_RELIC")]
public sealed class Kg2TestResetRelic : BlitzRelicDef
{
    public override bool ShouldPlayerResetEnergy(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        byte player) => false;
}

[BlitzContent("KG2_TEST_RECEIVED_RELIC")]
public sealed class Kg2TestReceivedRelic : BlitzRelicDef
{
    public override bool TryModifyPowerAmountReceived(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        ushort powerId, byte target, int amount, out int modified)
    {
        modified = amount * 2;
        return amount > 0;
    }

    public override void AfterModifyingPowerAmountReceived(ref BlitzExec x, BlitzListener self, ushort powerId,
        byte target) => Kg2TestLog.Add("received-after");
}

[BlitzContent("KG2_TEST_SHUFFLE_RELIC")]
public sealed class Kg2TestShuffleRelic : BlitzRelicDef
{
    public override void AfterShuffle(ref BlitzExec x, BlitzListener self, byte shuffler)
        => Kg2TestLog.Add("shuffle");
}

[BlitzContent("KG2_TEST_ENTERED_RELIC")]
public sealed class Kg2TestEnteredRelic : BlitzRelicDef
{
    public override void AfterCardEnteredCombat(ref BlitzExec x, BlitzListener self, BlitzPile pile, int index,
        in BlitzCard card) => Kg2TestLog.Add("entered");
}

[BlitzContent("KG2_TEST_DAMAGE_SOURCE_POWER")]
public sealed class Kg2TestDamageSourcePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterDamageReceived(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
        => Kg2TestLog.Add(result.HasCardSource ? "damage-card" : "damage-none");
}

sealed class Keyed(BlitzCard card) : IComparable<Keyed>
{
    public BlitzCard Card { get; } = card;

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

[BlitzContent("KS_DISCARD_TARGET")]
public sealed class KsDiscardTarget : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

[BlitzContent("KS_SLY_CARD")]
public sealed class KsSlyCard : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x) => KsTestLog.Lines.Add("sly-play");
}

[BlitzContent("KS_TEST_DISCARD_RELIC")]
public sealed class KsTestDiscardRelic : BlitzRelicDef
{
    public static bool RemoveDiscardedCard { get; set; }

    public override void AfterCardDiscarded(ref BlitzExec x, BlitzListener self, in BlitzCard card)
    {
        KsTestLog.Lines.Add("discarded");
        if (!RemoveDiscardedCard)
            return;
        for (int index = 0; index < x.B.DiscardCount; index++)
        {
            if (!BlitzFingerprint.Equal(BlitzPiles.At(ref x.B, BlitzPile.Discard, index), card))
                continue;
            // Leave every pile: removing without inserting models the unrepresentable target.
            BlitzPiles.RemoveAt(ref x.B, BlitzPile.Discard, index);
            return;
        }
    }
}

[BlitzContent("KS_TEST_SLY_RELIC")]
public sealed class KsTestSlyRelic : BlitzRelicDef;

[BlitzContent("RG_TEST_STAR_CARD")]
public sealed class RgTestStarCard : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

[BlitzContent("RG_TEST_STAR_POWER")]
public sealed class RgTestStarPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterStarsSpent(ref BlitzExec x, BlitzListener self, int amount)
        => Kg2TestLog.Add($"stars-spent-power:{amount}");

    public override void AfterStarsGained(ref BlitzExec x, BlitzListener self, int amount)
        => Kg2TestLog.Add($"stars-gained:{amount}");
}

[BlitzContent("RG_TEST_STAR_RELIC")]
public sealed class RgTestStarRelic : BlitzRelicDef
{
    public override decimal TryModifyStarCost(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzCard card, BlitzPile pile, decimal amount)
    {
        Kg2TestLog.Add($"star-cost:{(int)amount}");
        return amount - 4m;
    }

    public override void AfterStarsSpent(ref BlitzExec x, BlitzListener self, int amount)
        => Kg2TestLog.Add($"stars-spent-relic:{amount}");
}

[BlitzContent("CX_TEST_IMBUED_REMOVER")]
public sealed class CxTestImbuedRemover : BlitzCardDef
{
    /// <summary>Removes the first other Imbued card from the hand, making its pending auto-play unlocatable.</summary>
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzRegistry.TryEnchantment(BlitzKernel.ImbuedEnchantmentId, out ushort imbued);
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (x.B.Cards[index].EnchantmentId != (byte)(imbued + 1))
                continue;
            BlitzPiles.RemoveAt(ref x.B, BlitzPile.Hand, index);
            return;
        }
    }
}
