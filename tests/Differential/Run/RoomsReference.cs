// Independent bounded-room oracle: legacy runtime, TestHost startup and legacy NativePilotHost.
// Native choices are dispatched directly; neither OfflineRunHost nor its policy executes here.
using System.Reflection;
using System.Text.Json;
using Sts2Solver.Autoplay;
using Sts2Solver.Import;
using Sts2Solver.Simulation.Combat;
using Sts2Solver.Simulation.Run;
using Sts2Solver.TestHost;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;

namespace Differential;

internal static class RoomsReference
{
    private static readonly List<object> Checkpoints = [];
    private static Task? _offer;
    private static void TrackOffer(Task __result) => _offer = __result;
    private static object Snapshot(NativeRun run, Player player) => new
    {
        col = run.CurrentMapCoord?.col, row = run.CurrentMapCoord?.row,
        roomType = run.CurrentRoom!.RoomType.ToString(), model = run.CurrentRoom.ModelId?.Entry ?? "",
        runDump = RunDump.Capture(RunStateImporter.Import(run, player)),
        nativeDump = NativeRunDump.Capture(run, player),
        history = JsonSerializer.Serialize(run.MapPointHistory, new JsonSerializerOptions { IncludeFields = true }),
        isGameOver = run.IsGameOver, hp = player.Creature.CurrentHp, actIndex = run.CurrentActIndex,
    };
    private static void Capture()
    {
        NativeRun run = RunManager.Instance.DebugOnlyGetState()!;
        Checkpoints.Add(Snapshot(run, run.Players.Single()));
    }
    internal static int Production(int rooms, string? seed = null)
    {
        new Harmony("Sts2Solver.Differential.RoomsProduction").Patch(
            AccessTools.GetDeclaredConstructors(typeof(Sts2Solver.OfflineHost.RoomSummary)).Single(c => c.IsPublic),
            postfix: new HarmonyMethod(typeof(RoomsReference), nameof(Capture)));
        var result = Sts2Solver.OfflineHost.FirstCombat.Run(new(AssemblyBootstrap.Sts2DataDir,
            Seed: seed ?? "R3B2-DECISION", StopAfter: seed == null ? "rooms" : "act1", MaxRooms: rooms, TimeoutSeconds: 1200));
        Console.WriteLine(JsonSerializer.Serialize(new { result, checkpoints = Checkpoints, finalSnapshot = Snapshot(RunManager.Instance.DebugOnlyGetState()!, RunManager.Instance.DebugOnlyGetState()!.Players.Single()) },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return result.Milestone is "room-budget-reached" or "act1-complete" || result.Status == "Death" ? 0 : 1;
    }
    internal static int Run(int maximum, string seed = "R3B2-DECISION", bool act1 = false)
    {
        MainLoopContext loop = new();
        SynchronizationContext.SetSynchronizationContext(loop);
        void Wait(Task task) => loop.RunUntilCompleted(task, TimeSpan.FromSeconds(600), "rooms-reference");
        GameBootstrap.ApplyGodotBypasses(); GameBootstrap.SkipGodotNodeStaticConstructors(); GameBootstrap.InitializeStaticState();
        foreach (Type type in new[] { typeof(EnchantmentModel), typeof(RelicModel) })
            GameBootstrap.Harmony.Patch(AccessTools.PropertyGetter(type, type == typeof(RelicModel) ? "HoverTipsExcludingRelic" : "HoverTips"),
                prefix: new HarmonyMethod(typeof(RoomsReference), nameof(NoHoverTips)));
        GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(PreloadManager), "LoadRoomEventAssets"),
            prefix: new HarmonyMethod(typeof(RoomsReference), nameof(Completed)));
        foreach (MethodInfo method in typeof(UserDataPathProvider).GetMethods().Where(m => m.Name is "GetProfileDir" or "GetProfileScopedBasePath"))
            GameBootstrap.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(RoomsReference), nameof(ProfilePath)));
        GameBootstrap.Harmony.Unpatch(AccessTools.Method(typeof(CardRewardAlternative), "Generate"),
            AccessTools.Method(typeof(GameBootstrap), "NoCardRewardAlternativesPrefix"));
        GameBootstrap.Harmony.Patch(AccessTools.Constructor(typeof(CardRewardAlternative),
            [typeof(string), typeof(Func<Task>), typeof(MegaCrit.Sts2.Core.Entities.Rewards.PostAlternateCardRewardAction)]),
            transpiler: new HarmonyMethod(AccessTools.Method(typeof(RunRewardShopCompare), "AlternativeHotkeys")));
        GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(RewardsSet), nameof(RewardsSet.Offer)),
            postfix: new HarmonyMethod(typeof(RoomsReference), nameof(TrackOffer)));
        foreach (string loader in new[] { "LoadRoomRestSite", "LoadRoomMerchantAssets", "LoadRoomTreasureAssets" })
            GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(PreloadManager), loader),
                prefix: new HarmonyMethod(typeof(RoomsReference), nameof(Completed)));
        foreach (System.Reflection.MethodInfo method in new[] {
            AccessTools.Method(typeof(MegaCrit.Sts2.Core.Entities.Merchant.MerchantPotionEntry), "CalcCost"),
            AccessTools.Method(typeof(MerchantRoom), nameof(MerchantRoom.Exit)) })
            GameBootstrap.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(RoomsReference), nameof(MerchantBegin)),
                finalizer: new HarmonyMethod(typeof(RoomsReference), nameof(MerchantEnd)));
        SaveManager.Instance.InitProfileId(1); TerminalBattleCapture.Install();
        Task<(NativeRun Run, Player Player)> create = RunTestHost.StartNewRunWithUnlocksAsync(
            ModelDb.AllCharacters.Single(c => c.GetType().Name == "Ironclad"), 0, seed,
            new UnlockState([new NeowEpoch().Id], [], 1));
        Wait(create); (NativeRun run, Player player) = create.Result;
        using IDisposable selector = CardSelectCmd.UseSelector(new Selector());
        EventRoom neow = (EventRoom)run.CurrentRoom!;
        RunManager.Instance.EventSynchronizer.ChooseLocalOption(neow.LocalMutableEvent.CurrentOptions.ToList().IndexOf(neow.LocalMutableEvent.CurrentOptions.FirstOrDefault(o => !o.IsLocked && o.Relic is LavaRock) ?? neow.LocalMutableEvent.CurrentOptions.First(o => !o.IsLocked)));
        Wait(RunManager.Instance.EventSynchronizer.AwaitPendingOptionTasks());
        List<Sts2Solver.OfflineHost.CombatSummary> combats = [];
        RewardsSet.testSelector = async set =>
        {
            foreach (Reward reward in set.Rewards) await RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
        };
        try
        {
            for (int n = 0; n < maximum; n++)
            {
                var point = RunTestHost.GetTravelablePoints(run).OrderBy(p => p.coord.col).ThenBy(p => p.coord.row).First();
                Wait(RunManager.Instance.EnterMapCoord(point.coord));
                if (run.CurrentRoom is CombatRoom room)
                {
                    var ready = new OfflineCombatWaiter(loop).WaitForPlayableCombatAsync(); Wait(ready);
                    var combat = ready.Result;
                    Rules rules = RulesCompiler.Compile(combat, player, run);
                    LiveCombatTracker tracker = new(combat); Battle opening = tracker.ImportRoot(combat, player, rules); tracker.Observe(opening);
                    NativePilotHost host = new(loop, combat, player, rules, tracker, opening, SearchPolicyCapture.Capture(combat, player),
                        SearchPerformanceProfile.For(SearchPerformanceTier.Low), OfflineCombatSettings.DefaultMemoryBudgetBytes);
                    Task<CombatPilotResult> fight = CombatPilot.RunAsync(host, "rooms-reference-" + n, default); Wait(fight);
                    CombatPilotResult result = fight.Result;
                    Wait(RunManager.Instance.ActionExecutor.FinishedExecutingActions());
                    Battle terminal = TerminalBattleCapture.Value ?? throw new InvalidDataException("reference-terminal-missing");
                    combats.Add(new(result.Status, result.Outcome, result.Turns, host.Coverage.Actions.Sum(a => a.Count), result.Searches,
                        result.SearchLog.Sum(s => (long)s.ExpandedNodes), result.Deviations, result.DeploymentFailures, null,
                        Hash(string.Join("\n", BattleDump.Lines(opening))),
                        Hash(string.Join("\n", BattleDump.Lines(terminal))),
                        TerminalBattleCapture.Path.ToString(), terminal.Player.CurrentHp, terminal.Player.MaxHp, terminal.Gold));
                    if (result.Outcome is not ("victory" or "defeat") || result.Deviations != 0 || result.DeploymentFailures != 0)
                        throw new InvalidDataException("rooms-reference-combat");
                    if (result.Outcome == "defeat") break;
                    _offer = null; Wait(room.OfferRoomEndRewards()); Wait(_offer!);
                    if (room.RoomType != RoomType.Boss) Wait(RunManager.Instance.ProceedFromTerminalRewardsScreen());
                }
                else if (run.CurrentRoom is EventRoom eventRoom)
                {
                    EventModel model = eventRoom.LocalMutableEvent;
                    while (!model.IsFinished && !run.IsGameOver)
                    {
                        EventOption[] legal = model.CurrentOptions.Where(o => !o.IsLocked).ToArray();
                        EventOption option = legal.FirstOrDefault(o => o.IsProceed || o.TextKey.Contains("LEAVE", StringComparison.OrdinalIgnoreCase)
                            || o.TextKey.Contains("GIVE_UP", StringComparison.OrdinalIgnoreCase) || o.TextKey.Contains("GIVEUP", StringComparison.OrdinalIgnoreCase)) ?? legal.Last();
                        if (option.IsProceed) Wait(option.Chosen());
                        else
                        {
                            RunManager.Instance.EventSynchronizer.ChooseLocalOption(model.CurrentOptions.ToList().IndexOf(option));
                            Wait(RunManager.Instance.EventSynchronizer.AwaitPendingOptionTasks());
                        }
                    }
                }
                else if (run.CurrentRoom is TreasureRoom treasure)
                {
                    Wait(treasure.DoNormalRewards()); Wait(treasure.DoExtraRewardsIfNeeded());
                    var sync = RunManager.Instance.TreasureRoomRelicSynchronizer;
                    if (sync.CurrentRelics is not { Count: > 0 }) sync.CompleteWithNoRelics();
                    else
                    {
                        List<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult>? awards = null;
                        void Award(List<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult> results) => awards = results;
                        sync.RelicsAwarded += Award;
                        try { sync.PickRelicLocally(0); Wait(RunManager.Instance.ActionExecutor.FinishedExecutingActions()); }
                        finally { sync.RelicsAwarded -= Award; }
                        foreach (var award in awards ?? throw new InvalidDataException("reference-award-missing"))
                        {
                            RelicModel mutable = award.relic.ToMutable();
                            if (award.type != MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResultType.Skipped)
                                Wait(RelicCmd.Obtain(mutable, award.player!));
                            foreach (var p in run.Players)
                                if (!ReferenceEquals(p, award.player)) p.RelicGrabBag.MoveToFallback(award.relic);
                        }
                    }
                    Wait(RunManager.Instance.ProceedFromTerminalRewardsScreen());
                }
                else if (run.CurrentRoom is RestSiteRoom rest)
                {
                    while (rest.Options.Count != 0)
                    {
                        int index = rest.Options.ToList().FindIndex(o => o.IsEnabled);
                        Wait(RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(index));
                    }
                    Wait(RunManager.Instance.RestSiteSynchronizer.AfterAllRestSitesCompleted());
                }
                else if (run.CurrentRoom is MerchantRoom)
                {
                    var shop = NativeShopSession.TryCreate(run, player) ?? throw new InvalidDataException("reference-shop-missing");
                    var purchase = shop.Choices().FirstOrDefault(c => c.Kind is NativeShopChoiceKind.Card or NativeShopChoiceKind.Relic or NativeShopChoiceKind.Potion);
                    if (purchase != null) Wait(shop.BuyAsync(purchase));
                }
                else throw new InvalidDataException("rooms-reference-scope:" + run.CurrentRoom?.RoomType);
                Wait(RunManager.Instance.ActionExecutor.FinishedExecutingActions());
                if (run.IsGameOver && player.Creature.IsDead) break;
                Checkpoints.Add(Snapshot(run, player));
                if (run.CurrentRoom!.RoomType == RoomType.Boss) break;
            }
        }
        finally { RewardsSet.testSelector = null; }
        Console.WriteLine(JsonSerializer.Serialize(new { checkpoints = Checkpoints, combats, finalSnapshot = Snapshot(run, player), status = run.IsGameOver && player.Creature.IsDead ? "Death" : act1 && run.CurrentRoom!.RoomType == RoomType.Boss ? "Paused" : "Bounded" },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return 0;
    }
    private sealed class Selector : ICardSelector
    {
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> cards, IReadOnlyList<CardRewardAlternative> alternatives)
            => new() { card = cards[0].Card };
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards, int min, int max)
            => Task.FromResult(cards.Take(Math.Max(1, min)));
    }
    private static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void MerchantBegin(out bool __state) { __state = TestMode.IsOn; TestMode.IsOn = false; }
    private static void MerchantEnd(bool __state) => TestMode.IsOn = __state;
    private static bool NoHoverTips(ref IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> __result) { __result = []; return false; }
    private static bool Completed(ref Task __result) { __result = Task.CompletedTask; return false; }
    private static bool ProfilePath(int profileId, ref string __result) { __result = $"profile{profileId}"; return false; }
}
