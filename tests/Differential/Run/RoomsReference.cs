// Independent bounded-room oracle: legacy runtime, TestHost startup and legacy NativePilotHost.
// Native choices are dispatched directly; neither OfflineRunHost nor its policy executes here.
using System.Reflection;
using System.Text.Json;
using BlitzSolver.Autoplay;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
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
    };
    private static void Capture()
    {
        NativeRun run = RunManager.Instance.DebugOnlyGetState()!;
        Checkpoints.Add(Snapshot(run, run.Players.Single()));
    }
    internal static int Production(int rooms)
    {
        new Harmony("BlitzSolver.Differential.RoomsProduction").Patch(
            AccessTools.GetDeclaredConstructors(typeof(BlitzSolver.OfflineHost.RoomSummary)).Single(c => c.IsPublic),
            postfix: new HarmonyMethod(typeof(RoomsReference), nameof(Capture)));
        var result = BlitzSolver.OfflineHost.FirstCombat.Run(new(AssemblyBootstrap.Sts2DataDir,
            StopAfter: "rooms", MaxRooms: rooms, TimeoutSeconds: 600));
        Console.WriteLine(JsonSerializer.Serialize(new { result, checkpoints = Checkpoints },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return result.Milestone == "room-budget-reached" ? 0 : 1;
    }
    internal static int Run(int maximum)
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
        SaveManager.Instance.InitProfileId(1); TerminalBattleCapture.Install();
        Task<(NativeRun Run, Player Player)> create = RunTestHost.StartNewRunWithUnlocksAsync(
            ModelDb.AllCharacters.Single(c => c.GetType().Name == "Ironclad"), 0, "R3B2-DECISION",
            new UnlockState([new NeowEpoch().Id], [], 1));
        Wait(create); (NativeRun run, Player player) = create.Result;
        EventRoom neow = (EventRoom)run.CurrentRoom!;
        RunManager.Instance.EventSynchronizer.ChooseLocalOption(neow.LocalMutableEvent.CurrentOptions.ToList().FindIndex(o => o.Relic is LavaRock));
        Wait(RunManager.Instance.EventSynchronizer.AwaitPendingOptionTasks());
        List<BlitzSolver.OfflineHost.CombatSummary> combats = [];
        using IDisposable selector = CardSelectCmd.UseSelector(new Selector());
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
                        SearchPerformanceProfile.For(SearchPerformanceTier.Low), OfflineCombatSettings.DefaultMemoryBudgetBytes, widening: true);
                    Task<CombatPilotResult> fight = CombatPilot.RunAsync(host, "rooms-reference-" + n, default); Wait(fight);
                    CombatPilotResult result = fight.Result;
                    Wait(RunManager.Instance.ActionExecutor.FinishedExecutingActions());
                    Battle terminal = TerminalBattleCapture.Value ?? throw new InvalidDataException("reference-terminal-missing");
                    combats.Add(new(result.Status, result.Outcome, result.Turns, host.Coverage.Actions.Sum(a => a.Count), result.Searches,
                        result.SearchLog.Sum(s => (long)s.ExpandedNodes), result.Deviations, result.DeploymentFailures, null,
                        Hash(string.Join("\n", BattleDump.Lines(opening))),
                        Hash(string.Join("\n", BattleDump.Lines(terminal))),
                        TerminalBattleCapture.Path.ToString(), terminal.Player.CurrentHp, terminal.Player.MaxHp, terminal.Gold));
                    if (result.Outcome != "victory" || result.Deviations != 0 || result.DeploymentFailures != 0)
                        throw new InvalidDataException("rooms-reference-combat");
                    _offer = null; Wait(room.OfferRoomEndRewards()); Wait(_offer!);
                    Wait(RunManager.Instance.ProceedFromTerminalRewardsScreen());
                }
                else if (run.CurrentRoom is EventRoom eventRoom)
                {
                    EventModel model = eventRoom.LocalMutableEvent;
                    while (!model.IsFinished)
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
                else throw new InvalidDataException("rooms-reference-scope:" + run.CurrentRoom?.RoomType);
                Checkpoints.Add(Snapshot(run, player));
            }
        }
        finally { RewardsSet.testSelector = null; }
        Console.WriteLine(JsonSerializer.Serialize(new { checkpoints = Checkpoints, combats },
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
    private static bool NoHoverTips(ref IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> __result) { __result = []; return false; }
    private static bool Completed(ref Task __result) { __result = Task.CompletedTask; return false; }
    private static bool ProfilePath(int profileId, ref string __result) { __result = $"profile{profileId}"; return false; }
}
