// R3a feasibility probe. Native game owns every outcome; the deterministic baseline reads only
// visible options. No enemy kills, inventory edits or map jumps are used in natural runs.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Sts2Solver.Ai.Combat;
using Sts2Solver.Autoplay;
using Sts2Solver.Import;
using Sts2Solver.Simulation.Combat;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;

namespace Differential;

internal sealed class RunProbe
{
    private static readonly string[] Characters = ["Ironclad", "Silent", "Regent", "Necrobinder", "Defect"];
    private readonly MainLoopContext _loop = new();
    private readonly Settings _settings;
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private NativeRun _run = null!;
    private Player _player = null!;
    private int _actions, _rooms, _combats, _searches, _researches, _combatSearches;
    private CardSelectorPrefs? _prefs;
    private bool? _nativeVictory;
    private readonly List<NativeRewardSession> _rewardSessions = [];
    private bool _asyncRewardFixture;
    internal RunProbeTelemetry Telemetry { get; }
    private RunProbe(Settings settings)
    {
        _settings = settings;
        Directory.CreateDirectory(settings.Output);
        File.WriteAllText(Path.Combine(settings.Output, "trace.jsonl"), "");
        Telemetry = new(settings.Output);
    }

    private sealed record Settings(string Output = ".local/r3a/probe", string Character = "Ironclad",
        string Seed = "R3A-0", SearchPerformanceTier Tier = SearchPerformanceTier.Medium,
        int MaxRooms = 100, int MaxActions = 10000, int MaxSeconds = 1800, int Seeds = 1,
        int Jobs = 4, bool Child = false, bool Batch = false, string? Fixture = null);

    internal static int Run(string[] args)
    {
        Settings s = new();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string Value() => ++i < args.Length ? args[i] : throw new InvalidDataException($"缺少{arg}参数");
            int Number() => int.TryParse(Value(), out int n) && n > 0 ? n : throw new InvalidDataException($"{arg}需要正整数");
            switch (arg)
            {
                case "--out": s = s with { Output = Value() }; break;
                case "--character": s = s with { Character = Value() }; break;
                case "--seed": s = s with { Seed = Value() }; break;
                case "--tier":
                    if (!Enum.TryParse(Value(), true, out SearchPerformanceTier tier) || !Enum.IsDefined(tier))
                        throw new InvalidDataException("搜索档位必须为Low/Medium/High/ExtraHigh");
                    s = s with { Tier = tier }; break;
                case "--max-rooms": s = s with { MaxRooms = Number() }; break;
                case "--max-actions": s = s with { MaxActions = Number() }; break;
                case "--max-seconds": s = s with { MaxSeconds = Number() }; break;
                case "--seeds": s = s with { Seeds = Number() }; break;
                case "--jobs": s = s with { Jobs = Number() }; break;
                case "--batch": s = s with { Batch = true }; break;
                case "--child": s = s with { Child = true }; break;
                case "--fixture": s = s with { Fixture = Value() }; break;
                default: throw new InvalidDataException($"未知run-probe参数{arg}");
            }
        }
        if (!Characters.Contains(s.Character) || s.Jobs > 4 || s.MaxSeconds > 86400 || s.Seeds > 100
            || (s.Batch && (s.Child || s.Fixture != null)))
            throw new InvalidDataException("角色必须为原版五角色，jobs在1..4，batch与child互斥");
        if (s.Fixture != null && s.Fixture is not ("shop-price" or "rooms" or "telemetry" or "unsupported" or "contracts"))
            throw new InvalidDataException("fixture必须为shop-price/rooms/telemetry/unsupported/contracts");
        if (s.Child) return new RunProbe(s).Execute();
        Directory.CreateDirectory(s.Output);
        List<Settings> cases = s.Batch
            ? [.. Characters.SelectMany(c => Enumerable.Range(0, s.Seeds).Select(n => s with
                { Character = c, Seed = $"R3A-{n}", Output = Path.Combine(s.Output, $"{c}-{n}") }))]
            : [s with { Output = Path.Combine(s.Output, s.Character) }];
        Stopwatch wall = Stopwatch.StartNew();
        List<object> results = [];
        int failures = 0;
        Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = s.Jobs }, item =>
        {
            Directory.CreateDirectory(item.Output);
            ProcessStartInfo start = new("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string value in new[] { typeof(RunProbe).Assembly.Location, "--run-probe", "--child", "--character", item.Character,
                "--seed", item.Seed, "--tier", item.Tier.ToString(), "--out", item.Output,
                "--max-rooms", item.MaxRooms.ToString(), "--max-actions", item.MaxActions.ToString(), "--max-seconds", item.MaxSeconds.ToString() })
                start.ArgumentList.Add(value);
            if (item.Fixture != null) { start.ArgumentList.Add("--fixture"); start.ArgumentList.Add(item.Fixture); }
            Stopwatch processWall = Stopwatch.StartNew();
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动子进程");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit((item.MaxSeconds + 60) * 1000);
            if (!exited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            string output = stdout.GetAwaiter().GetResult(), error = stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(item.Output, "stdout.log"), output);
            File.WriteAllText(Path.Combine(item.Output, "stderr.log"), error);
            int code = exited ? process.ExitCode : 4;
            lock (results) results.Add(new { item.Character, item.Seed, exitCode = code, timedOut = !exited, processWallMs = processWall.Elapsed.TotalMilliseconds, item.Output });
            if (code != 0) Interlocked.Increment(ref failures);
            Console.WriteLine($"run-probe character={item.Character} seed={item.Seed} exit={code} out={item.Output}");
        });
        File.WriteAllText(Path.Combine(s.Output, "batch.json"), JsonSerializer.Serialize(new
        { wallMs = wall.Elapsed.TotalMilliseconds, concurrency = s.Jobs, count = cases.Count,
            failures, runsPerHour = cases.Count / wall.Elapsed.TotalHours, results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

    private int Execute()
    {
        string status = "error";
        int code = 1;
        try
        {
            using (Telemetry.Measure("initialization"))
            {
                SynchronizationContext.SetSynchronizationContext(_loop);
                GameBootstrap.ApplyGodotBypasses();
                GameBootstrap.SkipGodotNodeStaticConstructors();
                GameBootstrap.InitializeStaticState();
                RunProbeHooks.Active = this;
                RunProbeHooks.Install();
                TerminalBattleCapture.Install();
                MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitProfileId(1);
                RewardsSet.testSelector = DriveRewards;
                CombatManager.Instance.CombatWon += room =>
                {
                    if (room.Encounter.ShouldGiveRewards) RunProbeHooks.Pending.Add(room.OfferRoomEndRewards());
                };
                UnlockState unlock = new([.. EpochModel.AllEpochIds], [.. ModelDb.AllEncounters.Select(m => m.Id)], 999999999);
                Task<(NativeRun Run, Player Player)> start = RunTestHost.StartNewRunWithUnlocksAsync(
                    ModelDb.AllCharacters.Single(c => c.GetType().Name == _settings.Character), 0, _settings.Seed, unlock);
                Await(start, "新局");
                (_run, _player) = start.Result;
                DrainPending("开局事件初始化");
            }
            Telemetry.Write("configuration", new { _settings.Character, _settings.Seed, tier = _settings.Tier.ToString(),
                search = SearchPerformanceProfile.For(_settings.Tier), widening = "Core WideningSearch deterministic all widths, time insurance recorded only",
                memoryBudgetBytes = OfflineCombatSettings.DefaultMemoryBudgetBytes,
                ascension = 0, unlock = "all", tutorial = false, policy = "visible-first-v1", fixture = _settings.Fixture });
            using IDisposable selector = CardSelectCmd.PushSelector(new VisibleSelector(this));
            if (_run.CurrentRoom is EventRoom) DriveRoom();
            if (_settings.Fixture != null)
            {
                Fixture(_settings.Fixture);
                status = "fixture_complete"; code = 0;
            }
            else
            {
                while (true)
                {
                    Budget();
                    if (_nativeVictory != null || _player.Creature.IsDead || _run.IsGameOver)
                    { status = _nativeVictory == true ? "victory" : "death"; code = 0; break; }
                    if (_rooms >= _settings.MaxRooms) throw new ProbeBudgetException("rooms");
                    IReadOnlyList<MapPoint> points = RunTestHost.GetTravelablePoints(_run);
                    if (points.Count == 0) throw new UnsupportedException("没有合法地图点，且没有原版终局事实");
                    int selected;
                    using (Telemetry.Measure("policy")) selected = 0;
                    Decision("map", points.Select(p => new { column = p.coord.col, row = p.coord.row, type = p.PointType.ToString() }), selected);
                    Advance(() => RunTestHost.EnterMapCoordAsync(points[selected].coord), "地图进点");
                    _rooms++;
                    DriveRoom();
                    if (_run.CurrentRoom?.IsVictoryRoom == true)
                    { Advance(() => RunManager.Instance.WinRun(), "胜利终局"); status = "victory"; code = 0; break; }
                    if (_player.Creature.IsDead || _run.IsGameOver) continue;
                    if (_run.CurrentRoom is CombatRoom boss && boss.RoomType == RoomType.Boss && boss.IsPreFinished)
                    {
                        bool second = _run.Map.SecondBossMapPoint != null && !_run.VisitedMapCoords.Contains(_run.Map.SecondBossMapPoint.coord);
                        if (!second) { Decision("act_transition", new[] { "continue" }, 0); Advance(() => RunManager.Instance.EnterNextAct(), "下一幕"); DriveRoom(); }
                    }
                }
            }
        }
        catch (OperationCanceledException e) { status = "budget"; code = 5; Telemetry.Write("failure", new { error = e.Message }); }
        catch (ProbeBudgetException e) { status = "budget"; code = 5; Telemetry.Write("failure", new { error = e.Message }); }
        catch (TimeoutException e) { status = "timeout"; code = 4; Telemetry.Write("failure", new { error = e.ToString() }); }
        catch (UnsupportedException e) { status = "unsupported"; code = 3; Telemetry.Write("failure", new { error = e.ToString() }); }
        catch (UnsupportedImportException e) { status = "unsupported"; code = 3; Telemetry.Write("failure", new { error = e.ToString() }); }
        catch (Sts2Solver.Simulation.Combat.UnsupportedException e) { status = "unsupported"; code = 3; Telemetry.Write("failure", new { error = e.ToString() }); }
        catch (CapacityException e) { status = "capacity"; code = 3; Telemetry.Write("failure", new { error = e.ToString() }); }
        catch (InvalidDataException e) { status = "invalid"; code = 2; Telemetry.Write("failure", new { error = e.ToString() }); }
        finally
        {
            Telemetry.Finish(status, code, new { _settings.Character, _settings.Seed, tier = _settings.Tier.ToString(),
                rooms = _rooms, actions = _actions, combats = _combats, searches = _searches, researches = _researches,
                nativeAct = _run?.CurrentActIndex, nativeFloor = _run?.ActFloor, nativeHp = _player?.Creature.CurrentHp,
                nativeDead = _player?.Creature.IsDead, nativeGameOver = _run?.IsGameOver,
                terminalImport = "not imported: terminal facts are native; no predicted RunState substituted" });
        }
        return code;
    }

    private void DriveRoom()
    {
        Budget();
        AbstractRoom room = _run.CurrentRoom ?? throw new UnsupportedException("当前房间为空");
        Telemetry.Write("room", new { type = room.RoomType.ToString(), model = room.ModelId?.Entry });
        switch (room)
        {
            case MapRoom: return;
            case CombatRoom combat: Fight(combat); return;
            case EventRoom e:
                while (!e.LocalMutableEvent.IsFinished)
                {
                    Budget();
                    EventModel model = e.LocalMutableEvent;
                    var options = model.CurrentOptions;
                    int index;
                    using (Telemetry.Measure("policy"))
                    {
                        index = Enumerable.Range(0, options.Count).Where(i => !options[i].IsLocked)
                            .OrderByDescending(i => options[i].IsProceed).DefaultIfEmpty(-1).First();
                    }
                    if (index < 0) throw new UnsupportedException($"事件{model.Id}无可用选项");
                    Decision("event", options.Select((o, i) => new { id = i, o.TextKey, o.IsLocked, o.IsProceed,
                        relic = o.Relic?.Id.Entry, title = o.Title.LocEntryKey, description = o.Description.LocEntryKey }), index);
                    using (Telemetry.Measure("native_run"))
                    {
                        // NEventRoom.OptionButtonClicked calls proceed callbacks directly;
                        // ordinary options go through the synchronizer. WasChosen alone is not
                        // a legal-option filter: native cancelable buttons may be re-clicked.
                        if (options[index].IsProceed) Await(options[index].Chosen(), "事件继续 " + model.Id);
                        else RunManager.Instance.EventSynchronizer.ChooseLocalOption(index);
                        DrainPending("事件 " + model.Id);
                        Await(RunManager.Instance.EventSynchronizer.AwaitPendingOptionTasks(), "事件同步 " + model.Id);
                    }
                    if (_run.CurrentRoom is CombatRoom nested) { Fight(nested); }
                    if (_player.Creature.IsDead) return;
                }
                return;
            case MerchantRoom:
                NativeShopSession shop = NativeShopSession.TryCreate(_run, _player) ?? throw new InvalidDataException("商店未初始化");
                while (true)
                {
                    var choices = shop.Choices();
                    var choice = choices.First();
                    Decision("shop", choices.Select(c => new { id = c.Index, kind = c.Kind.ToString(), c.Price,
                        model = c.Card?.Id.Entry ?? c.Relic?.Id.Entry ?? c.Potion?.Id.Entry }), choice.Index);
                    if (choice.Kind == NativeShopChoiceKind.Leave) break;
                    if (choice.Kind == NativeShopChoiceKind.CardRemoval)
                    {
                        shop.EnterRemoval();
                        var cards = shop.RemovalChoices();
                        Decision("shop_removal", cards.Select(Card), 0);
                        Advance(() => shop.RemoveAsync(cards[0]), "删牌");
                    }
                    else Advance(() => shop.BuyAsync(choice), "购买");
                    Budget();
                }
                return;
            case RestSiteRoom rest:
                var available = rest.Options;
                int pick = Enumerable.Range(0, available.Count).Where(i => available[i].IsEnabled)
                    .OrderByDescending(i => _player.Creature.CurrentHp * 2 < _player.Creature.MaxHp
                        ? available[i].OptionId == "HEAL" : available[i].OptionId == "SMITH").DefaultIfEmpty(-1).First();
                if (pick < 0) throw new UnsupportedException("休息点无可用选项");
                Decision("rest", available.Select((o, i) => new { id = i, o.OptionId, o.IsEnabled }), pick);
                Advance(() => RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(pick), "休息点选择");
                return;
            case TreasureRoom treasure:
                Advance(() => treasure.DoNormalRewards(), "宝箱金币");
                var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
                if (relics is { Count: > 0 })
                {
                    Decision("treasure", relics.Select(r => r.Id.Entry), 0);
                    using (Telemetry.Measure("native_run"))
                    {
                        RunManager.Instance.TreasureRoomRelicSynchronizer.PickRelicLocally(0);
                        WaitUntil(() => RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics == null, "宝箱遗物结算");
                    }
                }
                Advance(() => treasure.DoExtraRewardsIfNeeded(), "宝箱额外奖励"); return;
            default: throw new UnsupportedException($"未知房间{room.GetType().Name}");
        }
    }

    private void Fight(CombatRoom room)
    {
        _combats++;
        _combatSearches = 0;
        CombatState combat;
        using (Telemetry.Measure("native_combat"))
            combat = new OfflineCombatWaiter(_loop).WaitForPlayableCombatAsync().GetAwaiter().GetResult();
        Rules rules;
        LiveCombatTracker tracker;
        Battle opening;
        using (Telemetry.Measure("import_check"))
        {
            rules = RulesCompiler.Compile(combat, _player, _run);
            tracker = new(combat);
            opening = tracker.ImportRoot(combat, _player, rules);
            tracker.Observe(opening);
        }
        NativePilotHost host = new(_loop, combat, _player, rules, tracker, opening,
            SearchPolicyCapture.Capture(combat, _player), SearchPerformanceProfile.For(_settings.Tier), OfflineCombatSettings.DefaultMemoryBudgetBytes, MeasureCombat, widening: true, CaptureDifference, CaptureSearchRequest);
        using CancellationTokenSource budget = new(TimeSpan.FromSeconds(Math.Max(1, _settings.MaxSeconds - _wall.Elapsed.TotalSeconds)));
        CombatPilotResult result = CombatPilot.RunAsync(host, $"{_settings.Seed}-{_combats}", budget.Token).GetAwaiter().GetResult();
        if (result.Status == "cancelled" && budget.IsCancellationRequested) throw new ProbeBudgetException("combat-time");

        Telemetry.Write("combat", new { result.Status, result.Outcome, result.Turns, result.PlayerHp, result.PlayerMaxHp, result.Searches, result.ReusedTurns, result.Deviations, result.DeploymentFailures, result.PotionsUsed, result.PauseReason, rounds = result.SearchLog.Select(s => new { s.Turn, s.Reason, s.ExpandedNodes, s.RoundsRun, s.BestRound, rounds = s.Rounds.Select(r => new { r.Width, r.MaxExpandedNodes, r.ExpandedNodes, r.RouteActions, r.Victory }) }) });
        if (result.DeploymentFailures != 0 || result.Deviations != 0 || result.Status is not ("victory" or "defeat"))
            throw new InvalidDataException($"CombatPilot status={result.Status}, failures={JsonSerializer.Serialize(result.FailureLog)}, diff={JsonSerializer.Serialize(result.DeviationLog)}");
        using (Telemetry.Measure("native_run")) DrainPending("战后奖励");
        if (!_player.Creature.IsDead)
            Advance(() => RunManager.Instance.ProceedFromTerminalRewardsScreen(), "战后继续");
        Budget();
    }

    private async Task DriveRewards(RewardsSet set)
    {
        using IDisposable phase = Telemetry.Measure("native_run");
        NativeRewardSession session = NativeRewardSession.AttachAlreadyBegun(set);
        _rewardSessions.Add(session);
        try
        {
        if (_asyncRewardFixture) await Task.Yield();
        while (!session.Left)
        {
            Budget();
            var choices = session.Choices();
            NativeRewardChoice chosen = choices[0];
            if (session.Stage == NativeRewardStage.PotionDiscard) chosen = choices[^1];
            Decision("reward_" + session.Stage, choices.Select(c => new { id = c.Index, kind = c.Kind.ToString(),
                reward = c.Reward?.GetType().Name,
                model = c.Card?.Id.Entry ?? c.Potion?.Id.Entry ?? RewardModel(c.Reward),
                amount = c.Reward is GoldReward gold ? gold.Amount : (int?)null,
                card = c.Card == null ? null : Card(c.Card) }), chosen.Index);
            using (Telemetry.Measure("native_run"))
            {
                switch (session.Stage)
                {
                    case NativeRewardStage.List:
                        if (chosen.Kind == NativeRewardChoiceKind.Skip)
                        {
                            if (set.DisallowSkipping && !set.AllRewardsSuccessfullySelected)
                                throw new UnsupportedException("强制奖励不能跳过");
                            session.Leave();
                        }
                        else await session.TakeRewardAsync(chosen);
                        break;
                    case NativeRewardStage.Card: await session.ChooseCardAsync(chosen); break;
                    case NativeRewardStage.PotionDiscard: await session.ChoosePotionAsync(chosen); break;
                }
            }
        }
        }
        finally { _rewardSessions.Remove(session); }
    }

    private void Decision(string kind, object options, object selected)
    {
        Budget();
        string hash, semanticHash;
        using (Telemetry.Measure("import_check"))
        {
            NativeRewardSession? reward = _rewardSessions.LastOrDefault();
            var imported = RunStateImporter.Import(_run, _player, reward?.Set, reward?.RemainingRewards);
            // Hash the official canonical codec bytes. Keep the old semantic field as an alias.
            byte[] encoded = Sts2Solver.Simulation.Run.RunCodec.Encode(imported);
            hash = Convert.ToHexString(SHA256.HashData(encoded));
            semanticHash = hash;
            if (Environment.GetEnvironmentVariable("STS2_SOLVER_PROBE_CAPTURE_RUNSTATE") == $"{_run.CurrentActIndex}/{_run.ActFloor}")
            {
                using (Telemetry.Measure("logging"))
                {
                    File.WriteAllBytes(Path.Combine(_settings.Output, $"run-{_actions:D5}.bin"), encoded);
                    File.WriteAllText(Path.Combine(_settings.Output, $"run-{_actions:D5}.txt"), Sts2Solver.Simulation.Run.RunDump.Capture(imported));
                }
            }
        }
        Telemetry.Write("decision", new { kind, options, selected, stateHash = hash, semanticStateHash = semanticHash,
            act = _run.CurrentActIndex, floor = _run.ActFloor, hp = _player.Creature.CurrentHp,
            maxHp = _player.Creature.MaxHp, gold = _player.Gold });
        _actions++;
    }

    internal void ObserveBundle(IReadOnlyList<IReadOnlyList<CardModel>> bundles)
        => Decision("card_bundle", bundles.Select(b => b.Select(Card)), 0);

    internal void ObserveEnded(bool victory)
    {
        _nativeVictory = victory;
        Telemetry.Write("native_ended", new { victory });
    }

    internal void ObservePrefs(CardSelectorPrefs prefs)
    {
        _prefs = prefs;
        Telemetry.Write("card_prefs", new { prefs.MinSelect, prefs.MaxSelect, prefs.Cancelable, prefs.RequireManualConfirmation });
    }
    private static string? RewardModel(Reward? reward) => reward switch
    {
        RelicReward relic => relic.Relic?.Id.Entry,
        PotionReward potion => potion.Potion?.Id.Entry,
        _ => null,
    };
    private static object Card(CardModel c) => new { model = c.Id.Entry, upgrade = c.CurrentUpgradeLevel,
        enchantment = c.Enchantment?.Id.Entry, enchantmentAmount = c.Enchantment?.Amount };
    private void CaptureSearchRequest(SearchCase search)
    {
        using (Telemetry.Measure("logging"))
        {
            File.WriteAllBytes(Path.Combine(_settings.Output, "search-request.bin"), SearchCodec.EncodeRequest(search));
            File.WriteAllLines(Path.Combine(_settings.Output, "search-root.txt"), BattleDump.Lines(search.Battles[0]));
            Telemetry.Progress("combat_search", new { _combats, _actions, act = _run.CurrentActIndex,
                floor = _run.ActFloor, width = search.Options.BeamWidth });
        }
    }

    private void CaptureDifference(Battle expected, Battle actual)
    {
        File.WriteAllBytes(Path.Combine(_settings.Output, "predicted-battle.bin"), BattleCodec.Encode(expected));
        File.WriteAllBytes(Path.Combine(_settings.Output, "native-battle.bin"), BattleCodec.Encode(actual));
        File.WriteAllLines(Path.Combine(_settings.Output, "predicted-battle.txt"), BattleDump.Lines(expected));
        File.WriteAllLines(Path.Combine(_settings.Output, "native-battle.txt"), BattleDump.Lines(actual));
    }

    private IDisposable MeasureCombat(string phase)
    {
        Budget();
        Telemetry.Progress(phase, new { _combats, _actions, act = _run.CurrentActIndex, floor = _run.ActFloor });
        if (phase == "combat_search")
        {
            _searches++;
            if (_combatSearches++ > 0) _researches++;
            Decision("combat_search", new { tier = _settings.Tier.ToString(), driver = "CombatPilot", facts = "combat RNG access is the established combat-only exception" }, "search");
        }
        if (phase == "native_combat") _actions++;
        return Telemetry.Measure(phase);
    }

    private void Budget()
    {
        if (_actions >= _settings.MaxActions) throw new ProbeBudgetException("actions");
        if (_wall.Elapsed.TotalSeconds > _settings.MaxSeconds) throw new ProbeBudgetException("time");
    }
    private void Await(Task task, string context) => _loop.RunUntilCompleted(task,
        TimeSpan.FromSeconds(Math.Min(120, Math.Max(1, _settings.MaxSeconds - _wall.Elapsed.TotalSeconds))),
        $"{context} act={_run?.CurrentActIndex} floor={_run?.ActFloor} action={_actions}");
    private void Advance(Func<Task> operation, string context)
    {
        using (Telemetry.Measure("native_run"))
        {
            Telemetry.Progress("native_run", new { context, _actions, act = _run?.CurrentActIndex, floor = _run?.ActFloor });
            Await(operation(), context);
            DrainPending(context);
        }
    }
    private void DrainPending(string context)
    {
        while (RunProbeHooks.Pending.Count > 0)
        {
            Task[] pending = [.. RunProbeHooks.Pending]; RunProbeHooks.Pending.Clear();
            foreach (Task task in pending) Await(task, context);
        }
    }
    private void WaitUntil(Func<bool> condition, string context)
    {
        Stopwatch wait = Stopwatch.StartNew();
        while (!condition())
        {
            Budget();
            if (wait.Elapsed.TotalSeconds > 120) throw new TimeoutException(context);
            _loop.Pump(TimeSpan.FromMilliseconds(5));
        }
    }

    private sealed class VisibleSelector(RunProbe probe) : ICardSelector
    {
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<CardRewardAlternative> alternatives)
        {
            if (alternatives.Count != 0) throw new UnsupportedException("probe卡牌奖励alternative");
            probe.Decision("card_reward", options.Select(o => Card(o.Card)), 0);
            return new CardRewardSelection { card = options[0].Card };
        }
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            List<CardModel> cards = [.. options];
            if (cards.Count < minSelect) throw new InvalidDataException("选牌不足minSelect");
            int count = Math.Max(minSelect, Math.Min(1, maxSelect));
            probe.Decision("cards", new { cards = cards.Select(Card), minSelect, maxSelect,
                cancelable = probe._prefs?.Cancelable, manualConfirmation = probe._prefs?.RequireManualConfirmation },
                Enumerable.Range(0, count));
            return Task.FromResult<IEnumerable<CardModel>>(cards.Take(count).ToArray());
        }
    }

    private void Fixture(string name)
    {
        if (name == "shop-price")
        {
            bool was = TestMode.IsOn;
            var potion = ModelDb.AllPotions.First().ToMutable();
            int before = _player.PlayerRng.Shops.ToSerializable().counter;
            MerchantPotionEntry entry = new(potion, _player);
            int after = _player.PlayerRng.Shops.ToSerializable().counter;
            if (TestMode.IsOn != was || after != before + 1)
                throw new InvalidDataException("正常CalcCost未恢复TestMode或未抽真实Shops价");
            int price = entry.Cost;
            TestMode.IsOn = false;
            try
            {
                entry.CalcCost();
                if (TestMode.IsOn) throw new InvalidDataException("false入口CalcCost未恢复false");
            }
            finally { TestMode.IsOn = was; }
            Advance(() => entry.OnTryPurchaseWrapper(null, ignoreCost: true), "夹具购买药水");
            int errorBefore = _player.PlayerRng.Shops.ToSerializable().counter;
            foreach (bool initial in new[] { true, false })
            {
                TestMode.IsOn = initial;
                try
                {
                    try { entry.CalcCost(); throw new InvalidDataException("空药水CalcCost未抛异常"); }
                    catch (InvalidOperationException) { }
                    if (TestMode.IsOn != initial) throw new InvalidDataException("异常CalcCost未恢复TestMode");
                }
                finally { TestMode.IsOn = was; }
            }
            if (_player.PlayerRng.Shops.ToSerializable().counter != errorBefore)
                throw new InvalidDataException("失败CalcCost不应消费Shops RNG");
            Telemetry.Write("fixture", new { name, price, counterBefore = before, counterAfter = after,
                restoredNormal = true, restoredException = true, falseEntryRestored = true });
            return;
        }
        if (name == "contracts")
        {
            var point = RunTestHost.GetTravelablePoints(_run).First();
            Advance(() => RunTestHost.EnterMapCoordAsync(point.coord), "合同原生战斗");
            CombatState combat = new OfflineCombatWaiter(_loop).WaitForPlayableCombatAsync().GetAwaiter().GetResult();
            Rules rules = RulesCompiler.Compile(combat, _player, _run);
            Battle basis = new LiveCombatTracker(combat).ImportRoot(combat, _player, rules);
            RunProbeContracts.Check(basis);
            RunProbeContracts.CheckEventOptions(ModelDb.Event<Trial>().ToMutable());
            RunProbeContracts.CheckRunCodec(RunStateImporter.Import(_run, _player));
            Telemetry.Write("fixture", new { name, cases = 9 });
            return;
        }
        if (name == "unsupported")
        {
            Advance(() => RelicSelectCmd.FromChooseARelicScreen(_player, [ModelDb.AllRelics.First()]), "真实不支持入口");
            throw new InvalidDataException("不支持的原版RelicSelect入口意外返回");
        }
        if (name == "telemetry")
        {
            _asyncRewardFixture = true;
            Task offer;
            using (Telemetry.Measure("native_combat"))
                offer = RewardsCmd.OfferCustom(_player, [new GoldReward(1, _player)]);
            if (Telemetry.ActivePhase != "native_run") throw new InvalidDataException("悬挂奖励scope被父scope覆盖");
            Await(offer, "异步奖励计时夹具");
            if (Telemetry.ActivePhase != "other") throw new InvalidDataException("奖励scope完成未恢复other");
            DrainPending("异步奖励完成");
            Telemetry.Write("fixture", new { name, nonLifoAsyncReward = true });
            return;
        }
        Telemetry.Write("fixture", new { name, natural = false, setup = "public EnterRoom/EnterNextAct; no natural score" });
        Advance(() => RunManager.Instance.EnterRoom(new EventRoom(ModelDb.Event<Trial>())), "夹具Trial");
        DriveRoom();
        Advance(() => RunManager.Instance.EnterRoom(new EventRoom(ModelDb.Event<SelfHelpBook>())), "夹具选牌事件");
        DriveRoom();
        Advance(() => RunManager.Instance.EnterRoom(new RestSiteRoom()), "夹具休息升级");
        DriveRoom();
        Advance(() => RunManager.Instance.EnterRoom(new MerchantRoom()), "夹具商店");
        NativeShopSession shop = NativeShopSession.TryCreate(_run, _player) ?? throw new InvalidDataException("夹具商店为空");
        var removal = shop.Choices().FirstOrDefault(c => c.Kind == NativeShopChoiceKind.CardRemoval);
        if (removal != null)
        {
            shop.EnterRemoval();
            var cards = shop.RemovalChoices();
            Decision("fixture_removal", cards.Select(Card), 0);
            Advance(() => shop.RemoveAsync(cards[0]), "夹具删牌");
        }
        DriveRoom();
        Advance(() => RunManager.Instance.EnterRoom(new TreasureRoom(_run.CurrentActIndex)), "夹具宝箱");
        DriveRoom();
        // Full potion slots are an explicit fixture; normal runs never inject inventory.
        for (int i = 0; i < _player.MaxPotionCount; i++)
            if (_player.GetPotionAtSlotIndex(i) == null)
                Advance(() => PotionCmd.TryToProcure(ModelDb.AllPotions.First().ToMutable(), _player), "夹具填药水");
        Advance(() => RewardsCmd.OfferCustom(_player, [new PotionReward(ModelDb.AllPotions.First().ToMutable(), _player)]), "夹具满槽药水");
        var rewardSetTask = RewardsCmd.GenerateCustom(_player, [new GoldReward(1, _player)]);
        Await(rewardSetTask, "夹具奖励");
        NativeRewardSession skipped = NativeRewardSession.Attach(rewardSetTask.Result, _player);
        Decision("fixture_reward_skip", skipped.Choices().Select(c => c.Kind.ToString()), "Skip");
        skipped.Leave();
        Advance(() => RunManager.Instance.EnterNextAct(), "夹具幕切换");
        Advance(() => RunManager.Instance.WinRun(), "夹具胜利终局");
        if (_nativeVictory != true) throw new InvalidDataException("WinRun未观察到原版OnEnded(true)");
    }
    private sealed class ProbeBudgetException(string reason) : InvalidOperationException(reason);
}
