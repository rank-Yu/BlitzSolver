using System.Reflection;
using System.Runtime.CompilerServices;
using Sts2Solver.Simulation.Combat;
using Sts2Solver.Simulation.Run;

namespace Sts2Solver.KernelChecks;

internal static class RunChecks
{
    private static readonly ulong Seed = 0x0123456789ABCDEFUL;

    private static readonly int[][] ExpectedNamed =
    [
        [979648, 275043, 771652, 410756, 136198, 506982, 806207, 858779],
        [270656, 736300, 228309, 293275, 293434, 788919, 801412, 895242],
        [34306, 767578, 128652, 803194, 826817, 824243, 488545, 374371],
        [606585, 139083, 441516, 880751, 890115, 805475, 694887, 213170],
        [420865, 590153, 739865, 872383, 222738, 763698, 399400, 762178],
        [564324, 961262, 141505, 695398, 227899, 818699, 249660, 419717],
        [191723, 301974, 440461, 419100, 711539, 919888, 818042, 641038],
        [679616, 182395, 540939, 144681, 542455, 281505, 413711, 473194],
        [188276, 831548, 82416, 599140, 984935, 79957, 487202, 72861],
        [561113, 83463, 574712, 298813, 138275, 793194, 592585, 247890],
        [266668, 369639, 356000, 819811, 746633, 675520, 726277, 346099],
        [173075, 949402, 433155, 362427, 667874, 782530, 107073, 203381],
        [325159, 554939, 595719, 26835, 812752, 157625, 797290, 745462],
        [456059, 424390, 406596, 547705, 558766, 841596, 75745, 458211],
        [24190, 924883, 489617, 930186, 697408, 948648, 802470, 300801],
    ];

    internal static void Check()
    {
        Check(Unsafe.SizeOf<RunState>() == RunState.ExpectedSize,
            $"RunState 应为 {RunState.ExpectedSize} 字节，实际 {Unsafe.SizeOf<RunState>()}");
        Check(RunCatalog.CharacterCount == 5 && RunCatalog.ActCount == 4
            && RunCatalog.EncounterCount == 89 && RunCatalog.EventCount == 66
            && RunCatalog.EpochCount == 57,
            "RunCatalog 的原版内容数量与 0.111.0 目录不一致。");
        CheckRng();
        CheckCodec();
        RunCodecChecks.Check(SampleState());
        CheckCapacity();
        RunDecisionChecks.Check();
        CheckView();
        CheckTravel();
        CheckTutorialUnknown();
    }

    private static void CheckRng()
    {
        for (int stream = 0; stream < RunLimits.RngStreams; stream++)
        {
            RngState rng = RunRng.Stream(Seed, (RunRngStream)stream);
            for (int draw = 0; draw < 8; draw++)
            {
                int actual = rng.NextInt(1_000_000);
                Check(actual == ExpectedNamed[stream][draw],
                    $"RNG 流 {RunRng.StreamName((RunRngStream)stream)} 第 {draw} 次抽取 {actual} != {ExpectedNamed[stream][draw]}");
            }
        }
        Check(RunCatalog.TryEvent("NEOW", out ushort neow), "目录缺少 NEOW。");
        RngState eventRng = RunRng.Event(Seed, 0, neow);
        int[] expectedEvent = [746998, 655906, 341367, 360426, 192044, 93520, 483758, 183754];
        for (int draw = 0; draw < expectedEvent.Length; draw++)
            Check(eventRng.NextInt(1_000_000) == expectedEvent[draw], $"事件 RNG 第 {draw} 次抽取不一致。");
        Check(RunCatalog.TryEncounter("CEREMONIAL_BEAST_BOSS", out ushort boss),
            "目录缺少 CEREMONIAL_BEAST_BOSS。");
        RngState encounterRng = RunRng.Encounter(Seed, 37, boss);
        int[] expectedEncounter = [972212, 768317, 319800, 442225, 7264, 991678, 300384, 355307];
        for (int draw = 0; draw < expectedEncounter.Length; draw++)
            Check(encounterRng.NextInt(1_000_000) == expectedEncounter[draw],
                $"遭遇 RNG 第 {draw} 次抽取不一致。");
        RngState mapRng = RunRng.ActMap(Seed, 0);
        int[] expectedMap = [223944, 774104, 543871, 706919, 275087, 400196, 420208, 393776];
        for (int draw = 0; draw < expectedMap.Length; draw++)
            Check(mapRng.NextInt(1_000_000) == expectedMap[draw], $"地图 RNG 第 {draw} 次抽取不一致。");

        RunRngState run = RunRng.NewRun(Seed, Seed + 7);
        for (int stream = 0; stream < RunLimits.RngStreams; stream++)
        {
            bool playerStream = stream is (int)RunRngStream.Rewards
                or (int)RunRngStream.Shops or (int)RunRngStream.Transformations;
            ulong expectedSeed = playerStream ? Seed + 7 : Seed;
            RngState expected = RunRng.Stream(expectedSeed, (RunRngStream)stream);
            Check(RngEqual(expected, run.Streams[stream]),
                $"NewRun 流 {RunRng.StreamName((RunRngStream)stream)} 的种子归属不对。");
        }
    }

    private static void CheckCodec()
    {
        RunState state = SampleState();
        byte[] first = RunCodec.Encode(state);
        RunState decoded = RunCodec.Decode(first);
        byte[] second = RunCodec.Encode(decoded);
        Check(first.AsSpan().SequenceEqual(second), "RunState 编码往返不是逐字节相同。");
        Check(RunCodec.Hash(state) == RunCodec.Hash(decoded), "RunState 往返哈希不同。");
        RunState different = state;
        different.ActFloor++;
        Check(RunCodec.Hash(state) != RunCodec.Hash(different), "RunState 哈希未覆盖 ActFloor。");

        byte[] badMagic = (byte[])first.Clone();
        badMagic[0] ^= 0xFF;
        Check(Throws<InvalidDataException>(() => RunCodec.Decode(badMagic)), "坏魔数未被拒绝。");
        byte[] badVersion = (byte[])first.Clone();
        badVersion[4] ^= 0xFF;
        Check(Throws<InvalidDataException>(() => RunCodec.Decode(badVersion)), "坏版本未被拒绝。");
        Check(Throws<InvalidDataException>(() => RunCodec.Decode(first[..^1])),
            "截断的 RunState 未被拒绝。");
        RunState overCount = state;
        overCount.Deck.Count = RunLimits.DeckCards + 1;
        Check(Throws<InvalidDataException>(() => RunCodec.Encode(overCount)),
            "牌组越界的 RunState 未被拒绝。");

        RunState rewarded = SampleState();
        RunReward gold = default;
        gold.Type = RunRewardType.Gold;
        gold.Amount = 17;
        gold.AmountMax = 17;
        gold.Flags = RunRewardFlags.Populated;
        rewarded.AddReward(gold);
        RunReward card = default;
        card.Type = RunRewardType.Card;
        card.OptionStart = rewarded.ReserveRewardCards(2);
        card.OptionCount = 2;
        card.CardCount = 2;
        card.Flags = RunRewardFlags.Populated;
        for (int option = 0; option < 2; option++)
        {
            RunCard optionCard = default;
            optionCard.Id = 0;
            optionCard.Upgrade = checked((byte)option);
            rewarded.SetRewardCard(card.OptionStart + option, optionCard);
        }
        rewarded.AddReward(card);
        byte[] rewardBytes = RunCodec.Encode(rewarded);
        RunState rewardDecoded = RunCodec.Decode(rewardBytes);
        Check(rewardBytes.AsSpan().SequenceEqual(RunCodec.Encode(rewardDecoded)),
            "含奖励的 RunState 编码往返不是逐字节相同。");
        Check(rewardDecoded.Rewards.Count == 2
            && rewardDecoded.Rewards.Items[0].Amount == 17
            && rewardDecoded.Rewards.Items[1].OptionCount == 2,
            "奖励负载没有在编码中保留。");
    }

    private static void CheckCapacity()
    {
        RunState state = default;
        state.Deck.Count = RunLimits.DeckCards;
        ExpectCapacity("Deck", () => state.AddCard(default));
        state = default;
        state.Relics.Count = RunLimits.Relics;
        ExpectCapacity("Relics", () => state.AddRelic(default));
        state = default;
        ExpectCapacity("PotionSlots", () => state.SetPotion(RunLimits.PotionSlots, 0));
        state = default;
        state.Acts.Count = RunLimits.Acts;
        ExpectCapacity("Acts", () => state.AddAct(default));
        state = default;
        state.Map.NodeCount = RunLimits.MapPoints;
        ExpectCapacity("Map", () => state.AddMapPoint(default));
        state = default;
        state.Map.StartCount = RunLimits.MapStarts;
        ExpectCapacity("MapStarts", () => state.AddMapStart(default));
        state = default;
        state.Visited.CoordCount = RunLimits.VisitedCoords;
        ExpectCapacity("VisitedCoords", () => state.AddVisitedCoord(default));
        state = default;
        state.History.Count = RunLimits.MapHistory;
        ExpectCapacity("MapHistory", () => state.AddHistory(default));
        state = default;
        state.Rooms.Count = RunLimits.RoomStack;
        ExpectCapacity("RoomStack", () => state.AddRoom(default));
        state = default;
        state.Rng.EventCount = RunLimits.EventRngs;
        ExpectCapacity("EventRngs", () => state.AddEventRng(default));
        state = default;
        state.Rewards.Count = RunLimits.Rewards;
        ExpectCapacity("Rewards", () => state.AddReward(default));
        state = default;
        ExpectCapacity("RewardCardOptions", () => state.ReserveRewardCards(
            RunLimits.RewardCardOptions + 1));
        RunAct act = default;
        act.EventCount = RunLimits.ActEvents;
        ExpectCapacity("ActEvents", () => RunState.AddActEvent(ref act, 0));
        act = default;
        act.NormalCount = RunLimits.ActNormalEncounters;
        ExpectCapacity("ActNormalEncounters", () => RunState.AddActNormalEncounter(ref act, 0));
        act = default;
        act.EliteCount = RunLimits.ActEliteEncounters;
        ExpectCapacity("ActEliteEncounters", () => RunState.AddActEliteEncounter(ref act, 0));

        RunPropertyBag bag = default;
        bag.Length = RunLimits.CardPropertyBytes;
        ExpectCapacity("RunPropertyBag", () => RunProperties.AddInt(ref bag, 1, 1));
        bag = default;
        ExpectCapacity("RunProperties.String", () =>
            RunProperties.AddString(ref bag, 1, new byte[RunLimits.PropertyStringBytes + 1]));
        RunCard[] tooManyCards = new RunCard[RunLimits.PropertyCards + 1];
        bag = default;
        ExpectCapacity("RunProperties.CardArray", () =>
            RunProperties.AddCardArray(ref bag, 1, tooManyCards));
        int[] tooManyInts = new int[RunLimits.PropertyInts + 1];
        bag = default;
        ExpectCapacity("RunProperties.IntArray", () =>
            RunProperties.AddIntArray(ref bag, 1, tooManyInts));
    }

    private static void CheckView()
    {
        Check(Unsafe.SizeOf<RunView>() == 20912,
            $"RunView 应为 20912 字节，实际 {Unsafe.SizeOf<RunView>()}");
        RunState state = SampleState();
        RunView view = RunView.From(state);
        Check(view.Player.CurrentHp == state.Player.CurrentHp, "RunView HP 不一致。");
        Check(view.PlayerOdds.CardRarity == state.PlayerOdds.CardRarity
            && view.PlayerOdds.PotionReward == state.PlayerOdds.PotionReward,
            "RunView 卡牌稀有度/药水掉落保底不一致。");
        Check(view.Odds.Monster == state.Odds.Monster
            && view.Odds.Elite == state.Odds.Elite
            && view.Odds.Treasure == state.Odds.Treasure
            && view.Odds.Shop == state.Odds.Shop,
            "RunView 未知点房间保底不一致。");
        Check(view.Deck.Count == state.Deck.Count, "RunView 牌组数量不一致。");
        Check(view.Relics.Count == state.Relics.Count, "RunView 遗物数量不一致。");
        Check(view.Map.NodeCount == state.Map.NodeCount, "RunView 地图节点数不一致。");
        RunAct current = state.Acts.Items[state.ActIndex];
        RunAct future = state.Acts.Items[state.ActIndex + 1];
        Check(view.Act == current.Id, "RunView 当前幕 id 不一致。");
        Check(view.Boss == current.BossId, "RunView 当前幕 Boss 不一致。");
        Check(view.HasSecondBoss == current.HasSecondBoss
            && view.SecondBoss == current.SecondBossId, "RunView 当前幕第二 Boss 不一致。");
        Check(view.HasAncient == current.HasAncient && view.Ancient == current.AncientId,
            "RunView 当前幕先古之民不一致。");
        Check(view.Act != future.Id && view.Boss != future.BossId
            && view.SecondBoss != future.SecondBossId && view.Ancient != future.AncientId,
            "RunView 暴露了后续幕的 Boss/先古/幕 id。");
        Type[] forbidden =
        [
            typeof(RunState), typeof(RunRngState), typeof(RunRng), typeof(RunRelicBag),
            typeof(RunAct), typeof(RunUnlockState), typeof(RunHistory), typeof(RunPropertyBag),
        ];
        foreach (FieldInfo field in typeof(RunView).GetFields(BindingFlags.Instance
                     | BindingFlags.Public | BindingFlags.NonPublic))
        {
            Check(!forbidden.Contains(field.FieldType),
                $"RunView 暴露了不可见类型 {field.FieldType}。");
        }
    }

    private static void CheckTravel()
    {
        RunState state = TravelState();
        state.AddVisitedCoord(new RunCoord(3, 0));
        Check(Collect(state) == "1,1;5,1", "起点后的合法点应为两个子点，按坐标排序。");
        state.AddVisitedCoord(new RunCoord(1, 1));
        Check(Collect(state) == "2,2", "普通点的合法点应为其子点。");

        state.AddVisitedCoord(new RunCoord(2, 2));
        Check(Collect(state) == "3,3", "最后一行之后应只有 Boss。");

        state.AddVisitedCoord(new RunCoord(3, 3));
        Check(Collect(state) == "3,4", "A10 的 Boss 之后应只有第二 Boss。");

        state.AddVisitedCoord(new RunCoord(3, 4));
        Check(Collect(state) == "", "第二 Boss 之后没有合法点。");
    }

    private static string Collect(in RunState state)
    {
        Span<RunCoord> points = stackalloc RunCoord[RunLimits.DecisionOptions];
        int count = RunTravel.Collect(in state, points);
        return string.Join(';', points[..count].ToArray().Select(static point => point.ToString()));
    }

    private static RunState TravelState()
    {
        RunState state = default;
        state.GameMode = RunGameMode.Standard;
        state.Player.CurrentHp = 70;
        state.Map.Width = 7;
        state.Map.Height = 3;
        state.Map.Starting = new RunCoord(3, 0);
        state.Map.Boss = new RunCoord(3, 3);
        state.Map.HasSecondBoss = 1;
        state.Map.SecondBoss = new RunCoord(3, 4);
        AddMap(ref state, new RunCoord(3, 0), RunMapPointType.Ancient, [new(1, 1), new(5, 1)]);
        AddMap(ref state, new RunCoord(1, 1), RunMapPointType.Shop, [new(2, 2)]);
        AddMap(ref state, new RunCoord(5, 1), RunMapPointType.Unknown, []);
        AddMap(ref state, new RunCoord(2, 2), RunMapPointType.Monster, []);
        AddMap(ref state, new RunCoord(3, 3), RunMapPointType.Boss, []);
        AddMap(ref state, new RunCoord(3, 4), RunMapPointType.Boss, []);
        return state;
    }

    private static void AddMap(ref RunState state, RunCoord coord, RunMapPointType type,
        RunCoord[] children)
    {
        RunMapPoint point = default;
        point.Coord = coord;
        point.Type = type;
        point.ChildCount = checked((byte)children.Length);
        for (int index = 0; index < children.Length; index++)
            point.Children[index] = children[index];
        state.AddMapPoint(point);
    }

    private static void CheckTutorialUnknown()
    {
        RunState state = default;
        state.Unlock.NumberOfRuns = 0;
        Check(RunUnknownPoint.TryGetRoomTypeForTutorial(in state, RunMapPointType.Unassigned,
            out RunRoomType roomType) && roomType == RunRoomType.Event,
            "首次跑局的教学未知点应为事件。");
        RunHistoryEntry history = default;
        history.MapPointType = RunMapPointType.Unassigned;
        state.AddHistory(history);
        Check(!RunUnknownPoint.TryGetRoomTypeForTutorial(in state, RunMapPointType.Unassigned,
            out _), "历史里已有 Unassigned 时教学未知点不应再生效。");
        state = default;
        state.Unlock.NumberOfRuns = 1;
        Check(!RunUnknownPoint.TryGetRoomTypeForTutorial(in state, RunMapPointType.Unassigned,
            out _), "非首次跑局不应触发教学未知点。");
        state.Unlock.NumberOfRuns = 0;
        Check(!RunUnknownPoint.TryGetRoomTypeForTutorial(in state, RunMapPointType.Unknown,
            out _), "普通 Unknown 点不应走教学分支。");
    }

    private static RunState SampleState()
    {
        RunState state = default;
        state.GameMode = RunGameMode.Standard;
        state.Ascension = 7;
        state.ActIndex = 0;
        state.ActFloor = 3;
        state.Decision = RunDecision.MapPoint;
        state.Odds = new RunOdds { Monster = 0.1f, Elite = -1f, Treasure = 0.02f, Shop = 0.03f };
        state.PlayerOdds = new RunPlayerOdds { CardRarity = -0.05f, PotionReward = 0.4f };
        state.Player.Character = 0;
        state.Player.CurrentHp = 70;
        state.Player.MaxHp = 80;
        state.Player.MaxEnergy = 3;
        state.Player.Gold = 99;
        state.Player.BaseOrbSlots = 0;
        state.Player.Potions.Count = 3;
        state.Unlock.NumberOfRuns = 5;
        state.Rng = RunRng.NewRun(123, 123);
        RunCard card = default;
        card.Id = 0;
        card.FloorAddedToDeck = 1;
        state.AddCard(card);
        RunRelic relic = default;
        relic.Id = 0;
        relic.FloorAddedToDeck = 1;
        state.AddRelic(relic);
        RunAct act = default;
        act.Id = 0;
        act.BossId = 0;
        act.HasAncient = 1;
        act.AncientId = 0;
        act.HasSecondBoss = 1;
        act.SecondBossId = 1;
        state.AddAct(act);
        RunAct future = default;
        future.Id = 1;
        future.BossId = 2;
        future.HasAncient = 1;
        future.AncientId = 1;
        future.HasSecondBoss = 1;
        future.SecondBossId = 3;
        state.AddAct(future);
        state.Map.Width = 7;
        state.Map.Height = 2;
        state.Map.Starting = new RunCoord(3, 0);
        state.Map.Boss = new RunCoord(3, 1);
        state.Map.NodeCount = 2;
        state.Map.Nodes[0] = new RunMapPoint
        {
            Coord = new RunCoord(3, 0),
            Type = RunMapPointType.Ancient,
            CanBeModified = 0,
        };
        state.Map.Nodes[1] = new RunMapPoint
        {
            Coord = new RunCoord(3, 1),
            Type = RunMapPointType.Boss,
            CanBeModified = 0,
        };
        state.Map.StartCount = 1;
        state.Map.Starts[0] = new RunCoord(3, 0);
        return state;
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }

    private static void ExpectCapacity(string region, Action action)
    {
        try
        {
            action();
        }
        catch (CapacityException error)
        {
            Check(error.Region.Contains(region, StringComparison.Ordinal),
                $"容量溢出区域 {error.Region} 预期包含 {region}。");
            return;
        }
        throw new InvalidOperationException($"容量区域 {region} 未抛出 CapacityException。");
    }

    private static bool RngEqual(in RngState left, in RngState right)
        => left.Counter == right.Counter && left.S0 == right.S0 && left.S1 == right.S1
            && left.S2 == right.S2 && left.S3 == right.S3;

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
