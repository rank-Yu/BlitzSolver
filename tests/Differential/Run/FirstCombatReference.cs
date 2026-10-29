// Independent native oracle for the standalone first-combat adapter. This deliberately uses the
// legacy Differential runtime and TestHost startup, not OfflineHost runtime/combat implementations.
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlitzSolver.Autoplay;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;
using ProductionCapture = BlitzSolver.OfflineHost.Combat.TerminalBattleCapture;
using ProductionProjection = BlitzSolver.OfflineHost.Combat.TerminalBattleProjection;
using ProductionPath = BlitzSolver.OfflineHost.Combat.TerminalCapturePath;

namespace Differential;

internal static class FirstCombatReference
{
    internal static int Run(string[] args)
    {
        bool nextRoom = args.Contains("--next-room");
        string[] seeds = args.Where(a => a != "--next-room").ToArray();
        if (seeds.Length > 1) throw new InvalidDataException("reference accepts at most one seed and --next-room");
        string seed = seeds.Length == 1 ? seeds[0] : "R3B2-DECISION";
        MainLoopContext loop = new();
        SynchronizationContext.SetSynchronizationContext(loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();
        foreach (Type type in new[] { typeof(EnchantmentModel), typeof(RelicModel) })
            GameBootstrap.Harmony.Patch(AccessTools.PropertyGetter(type, type == typeof(RelicModel) ? "HoverTipsExcludingRelic" : "HoverTips"), prefix: new HarmonyMethod(typeof(FirstCombatReference), nameof(NoHoverTips)));
        GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(PreloadManager), "LoadRoomEventAssets"), prefix: new HarmonyMethod(typeof(FirstCombatReference), nameof(Completed)));
        foreach (MethodInfo method in typeof(UserDataPathProvider).GetMethods().Where(m => m.Name is "GetProfileDir" or "GetProfileScopedBasePath"))
            GameBootstrap.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(FirstCombatReference), nameof(ProfilePath)));
        SaveManager.Instance.InitProfileId(1);
        TerminalBattleCapture.Install();
        Task<(NativeRun Run, Player Player)> create = RunTestHost.StartNewRunWithUnlocksAsync(
            ModelDb.AllCharacters.Single(c => c.GetType().Name == "Ironclad"), 0, seed,
            new UnlockState([new NeowEpoch().Id], [], 1));
        loop.RunUntilCompleted(create, TimeSpan.FromSeconds(300), "reference-new-run");
        (NativeRun run, Player player) = create.GetAwaiter().GetResult();
        object starting = Summary(run, player);
        string nativeStarting = NativeSnapshot(run, player);
        EventRoom neow = run.CurrentRoom as EventRoom ?? throw new InvalidDataException("reference-neow");
        int neowIndex = neow.LocalMutableEvent.CurrentOptions.ToList().FindIndex(o => o.Relic is LavaRock);
        RunManager.Instance.EventSynchronizer.ChooseLocalOption(neowIndex);
        loop.RunUntilCompleted(RunManager.Instance.EventSynchronizer.AwaitPendingOptionTasks(),
            TimeSpan.FromSeconds(300), "reference-neow-choice");
        loop.RunUntilCompleted(RunManager.Instance.EnterMapCoord(RunTestHost.GetTravelablePoints(run)[0].coord),
            TimeSpan.FromSeconds(300), "reference-map-choice");
        var combat = new OfflineCombatWaiter(loop).WaitForPlayableCombatAsync().GetAwaiter().GetResult();
        Rules rules = RulesCompiler.Compile(combat, player, run);
        LiveCombatTracker tracker = new(combat);
        Battle opening = tracker.ImportRoot(combat, player, rules);
        tracker.Observe(opening);
        NativePilotHost host = new(loop, combat, player, rules, tracker, opening,
            SearchPolicyCapture.Capture(combat, player), SearchPerformanceProfile.For(SearchPerformanceTier.Low),
            OfflineCombatSettings.DefaultMemoryBudgetBytes, widening: true);
        CombatPilotResult result = CombatPilot.RunAsync(host, "first-combat-reference", default).GetAwaiter().GetResult();
        loop.RunUntilCompleted(RunManager.Instance.ActionExecutor.FinishedExecutingActions(),
            TimeSpan.FromSeconds(300), "reference-post-combat-queue");
        Battle? terminal = TerminalBattleCapture.Value;
        object afterCombat = Summary(run, player);
        string nativeAfterCombat = NativeSnapshot(run, player);
        object? afterRewards = null, next = null;
        string? nativeAfterRewards = null, nativeNextRoom = null;
        ReferenceSelector selector = new();
        if (nextRoom && result.Status == "victory")
        {
            GameBootstrap.Harmony.Unpatch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative), "Generate"),
                AccessTools.Method(typeof(GameBootstrap), "NoCardRewardAlternativesPrefix"));
            GameBootstrap.Harmony.Patch(AccessTools.Constructor(typeof(MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative),
                [typeof(string), typeof(Func<Task>), typeof(MegaCrit.Sts2.Core.Entities.Rewards.PostAlternateCardRewardAction)]),
                transpiler: new HarmonyMethod(AccessTools.Method(typeof(RunRewardShopCompare), "AlternativeHotkeys")));
            GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Rewards.RewardsSet), "Offer"),
                postfix: new HarmonyMethod(typeof(FirstCombatReference), nameof(TrackReferenceOffer)));
            using IDisposable scope = MegaCrit.Sts2.Core.Commands.CardSelectCmd.UseSelector(selector);
            MegaCrit.Sts2.Core.Rewards.RewardsSet.testSelector = async set =>
            {
                foreach (var reward in set.Rewards) await RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
            };
            try
            {
                loop.RunUntilCompleted(((CombatRoom)run.CurrentRoom!).OfferRoomEndRewards(), TimeSpan.FromSeconds(60), "reference-offer-generate");
                loop.RunUntilCompleted(_referenceOffer ?? throw new InvalidDataException("reference-offer-missing"), TimeSpan.FromSeconds(60), "reference-raw-offer");
                loop.RunUntilCompleted(RunManager.Instance.ProceedFromTerminalRewardsScreen(), TimeSpan.FromSeconds(60), "reference-proceed");
                afterRewards = Summary(run, player);
                nativeAfterRewards = NativeSnapshot(run, player);
                loop.RunUntilCompleted(RunManager.Instance.EnterMapCoord(RunTestHost.GetTravelablePoints(run)[0].coord), TimeSpan.FromSeconds(60), "reference-next-map");
                loop.RunUntilCompleted(RunManager.Instance.ActionExecutor.FinishedExecutingActions(), TimeSpan.FromSeconds(60), "reference-next-queue");
                if (run.CurrentRoom is CombatRoom) new OfflineCombatWaiter(loop).WaitForPlayableCombatAsync().GetAwaiter().GetResult();
                var coord = run.CurrentMapCoord!.Value;
                next = new { roomType = run.CurrentRoom!.RoomType.ToString(), model = run.CurrentRoom.ModelId?.Entry ?? "",
                    col = coord.col, row = coord.row, state = Summary(run, player) };
                nativeNextRoom = NativeSnapshot(run, player);
            }
            finally { MegaCrit.Sts2.Core.Rewards.RewardsSet.testSelector = null; }
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            seed, character = "Ironclad", ascension = 0, unlockProfile = "neow-epoch-v1",
            searchTier = "Low", memoryBudgetBytes = OfflineCombatSettings.DefaultMemoryBudgetBytes,
            starting, afterCombat, afterRewards, nextRoom = next, selector.Candidates,
            nativeStarting, nativeAfterCombat, nativeAfterRewards, nativeNextRoom,
            combat = new
            {
                status = result.Status, outcome = result.Outcome, turns = result.Turns,
                actions = host.Coverage.Actions.Sum(a => a.Count), searches = result.Searches, expandedNodes = result.SearchLog.Sum(s => (long)s.ExpandedNodes),
                deviations = result.Deviations, deploymentFailures = result.DeploymentFailures,
                openingSha256 = Hash(string.Join("\n", BattleDump.Lines(opening))),
                terminalSha256 = terminal is { } t ? Hash(string.Join("\n", BattleDump.Lines(t))) : null,
                terminalPath = TerminalBattleCapture.Path.ToString(),
                terminalHp = terminal?.Player.CurrentHp, terminalMaxHp = terminal?.Player.MaxHp,
                terminalGold = terminal?.Gold,
            }
        }));
        return result.Status == "victory" && result.Deviations == 0 && result.DeploymentFailures == 0 && terminal != null ? 0 : 1;
    }

    private static Task? _referenceOffer;
    private static void TrackReferenceOffer(Task __result) => _referenceOffer = __result;
    private sealed class ReferenceSelector : MegaCrit.Sts2.Core.TestSupport.ICardSelector
    {
        public List<string[]> Candidates { get; } = [];
        public MegaCrit.Sts2.Core.TestSupport.CardRewardSelection GetSelectedCardReward(
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult> cards,
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives)
        {
            Candidates.Add(cards.Select(c => c.Card.Id.Entry).ToArray());
            return new() { card = cards[0].Card };
        }
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards, int min, int max)
            => throw new InvalidDataException("reference unexpected selector");
    }
    private static string NativeSnapshot(NativeRun run, Player player)
        => Hash(NativeRunDump.Capture(run, player) + "\nhistory=" +
            JsonSerializer.Serialize(run.MapPointHistory, new JsonSerializerOptions { IncludeFields = true }));
    private static readonly List<string> NativeSnapshots = [];
    private static void CaptureNative(NativeRun run, Player player) => NativeSnapshots.Add(NativeSnapshot(run, player));
    internal static int ProductionPostCombat()
    {
        new Harmony("BlitzSolver.Differential.ProductionPostCombat").Patch(
            AccessTools.Method(typeof(BlitzSolver.OfflineHost.Runtime.FirstCombatSession), "Summarize"),
            postfix: new HarmonyMethod(typeof(FirstCombatReference), nameof(CaptureNative)));
        var result = BlitzSolver.OfflineHost.FirstCombat.Run(new(AssemblyBootstrap.Sts2DataDir, StopAfter: "next-room"));
        Console.WriteLine(JsonSerializer.Serialize(new { result, nativeSnapshots = NativeSnapshots },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return result.Milestone == "next-room-entered" ? 0 : 1;
    }

    private static object Summary(NativeRun run, Player player) => new
    {
        sha256 = Hash(RunDump.Capture(RunStateImporter.Import(run, player))),
        hp = (int)player.Creature.CurrentHp, maxHp = (int)player.Creature.MaxHp, gold = player.Gold,
        deck = player.Deck.Cards.Select(c => c.Id.Entry + "+" + c.CurrentUpgradeLevel).ToArray(),
        relics = player.Relics.Select(r => r.Id.Entry).ToArray(),
        potions = player.PotionSlots.Select(p => p?.Id.Entry ?? "").ToArray(),
    };
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static bool NoHoverTips(ref IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> __result) { __result = []; return false; }
    private static bool Completed(ref Task __result) { __result = Task.CompletedTask; return false; }
    private static bool ProfilePath(int profileId, ref string __result) { __result = $"profile{profileId}"; return false; }

    private static CancellationTokenSource? _combatCancellation;
    private static int _selectorScopes;
    private static int _subscriptions;
    internal static int CancelInCombat()
    {
        using CancellationTokenSource cancellation = new();
        _combatCancellation = cancellation;
        Harmony harmony = new("BlitzSolver.Differential.CombatCancellation");
        harmony.Patch(AccessTools.Method(typeof(BlitzSolver.OfflineHost.Combat.NativeActionExecutor), "Execute"),
            postfix: new HarmonyMethod(typeof(FirstCombatReference), nameof(CancelAfterDeployment)));
        harmony.Patch(AccessTools.Method(typeof(BlitzSolver.OfflineHost.Combat.NativeChoiceSelector), "Activate"),
            postfix: new HarmonyMethod(typeof(FirstCombatReference), nameof(TrackSelector)));
        EventInfo actionEvent = typeof(MegaCrit.Sts2.Core.GameActions.ActionExecutor).GetEvent("BeforeActionExecuted")
            ?? throw new InvalidDataException("action event missing");
        harmony.Patch(actionEvent.AddMethod!, postfix: new HarmonyMethod(typeof(FirstCombatReference), nameof(SubscriptionAdded)));
        harmony.Patch(actionEvent.RemoveMethod!, postfix: new HarmonyMethod(typeof(FirstCombatReference), nameof(SubscriptionRemoved)));
        BlitzSolver.OfflineHost.FirstCombatResult result = BlitzSolver.OfflineHost.FirstCombat.Run(
            new(AssemblyBootstrap.Sts2DataDir), cancellation.Token);
        bool singleRunRejected = false;
        try { BlitzSolver.OfflineHost.FirstCombat.Run(new(AssemblyBootstrap.Sts2DataDir)); }
        catch (InvalidOperationException error) when (error.Message.Contains("One native run", StringComparison.Ordinal))
        { singleRunRejected = true; }
        bool passed = result.Status == "Cancelled" && result.Milestone == null
            && result.Combat is { Actions: >= 1 } && _selectorScopes == 0 && _subscriptions == 0
            && singleRunRejected && (result.FinalState == null || result.FinalState.Hp == (int)RunManager.Instance.DebugOnlyGetState()!.Players.Single().Creature.CurrentHp);
        Console.WriteLine(JsonSerializer.Serialize(new { passed, selectorScopes = _selectorScopes,
            subscriptions = _subscriptions, singleRunRejected, result }));
        return passed ? 0 : 1;
    }
    private static void CancelAfterDeployment() => _combatCancellation!.Cancel();
    private static void SubscriptionAdded() => _subscriptions++;
    private static void SubscriptionRemoved() => _subscriptions--;
    private static void TrackSelector(ref IDisposable __result)
    { _selectorScopes++; __result = new TrackedScope(__result); }
    private sealed class TrackedScope(IDisposable inner) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { inner.Dispose(); }
            finally { _selectorScopes--; }
        }
    }

    private static string? _negativeMode;
    internal static int Negative(string mode)
    {
        if (mode is not ("hp" or "missing")) throw new InvalidDataException("negative mode");
        _negativeMode = mode;
        new Harmony("BlitzSolver.Differential.FirstCombatNegative").Patch(
            AccessTools.Method(typeof(ProductionCapture), "Capture"),
            postfix: new HarmonyMethod(typeof(FirstCombatReference), nameof(TamperCapturedTerminal)));
        BlitzSolver.OfflineHost.FirstCombatResult result = BlitzSolver.OfflineHost.FirstCombat.Run(
            new(AssemblyBootstrap.Sts2DataDir));
        bool rejected = result.Milestone == null && result.Status == "Failed"
            && result.Combat is { DeploymentFailures: > 0 }
            && (result.Detail?.Contains(mode == "hp" ? "player.hp" : "terminal_missing", StringComparison.Ordinal) ?? false);
        Console.WriteLine(JsonSerializer.Serialize(new { mode, rejected, result }));
        return rejected ? 0 : 1;
    }
    private static void TamperCapturedTerminal()
    {
        FieldInfo value = AccessTools.Field(typeof(ProductionCapture), "_value");
        if (_negativeMode == "missing")
        {
            value.SetValue(null, null);
            AccessTools.Field(typeof(ProductionCapture), "_path").SetValue(null, ProductionPath.None);
        }
        else if (value.GetValue(null) is Battle battle)
        {
            battle.Player.CurrentHp++;
            value.SetValue(null, (Battle?)battle);
        }
    }

    private static int _compiledCombats;
    internal static int LaterCombatFailure()
    {
        new Harmony("BlitzSolver.Differential.LaterCombatFailure").Patch(
            AccessTools.Method(typeof(RulesCompiler), nameof(RulesCompiler.Compile)),
            prefix: new HarmonyMethod(typeof(FirstCombatReference), nameof(FailLaterCompile)));
        var result = BlitzSolver.OfflineHost.FirstCombat.Run(new(AssemblyBootstrap.Sts2DataDir, StopAfter: "rooms", MaxRooms: 4));
        bool rejected = result.Status == "Failed" && result.Detail == "fixture-second-combat-root"
            && result.Rooms is { Length: 1 } && result.Combats is { Length: 1 }
            && result.FinalState == null && ProductionCapture.Value == null && ProductionCapture.Path == ProductionPath.None;
        Console.WriteLine(JsonSerializer.Serialize(new { rejected, result }));
        return rejected ? 0 : 1;
    }
    private static void FailLaterCompile()
    {
        if (++_compiledCombats == 2) throw new InvalidDataException("fixture-second-combat-root");
    }

    internal static int Contracts()
    {
        int checks = 0;
        void Check(bool valid, string message)
        {
            if (!valid) throw new InvalidDataException(message);
            checks++;
        }
        ProductionCapture.Begin(() => null);
        bool rejected = false;
        try { ProductionCapture.Compare(default); }
        catch (BlitzSolver.OfflineHost.Combat.OfflineDeploymentException error) when (error.Reason == "terminal_missing") { rejected = true; }
        Check(rejected, "missing terminal capture must reject completion");
        Battle native = default;
        native.Player.CurrentHp = 50; native.Player.MaxHp = 80; native.Outcome = Outcome.Victory; native.Gold = 99;
        ProductionCapture.Begin(() => native);
        // Invoke our own observer callback; no game state or production behavior is altered.
        typeof(ProductionCapture).GetMethod("VictoryPrefix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        Check(ProductionCapture.Compare(native) == null, "matching captured terminal");
        Battle predicted = native;
        predicted.Player.CurrentHp++;
        Check(ProductionCapture.Compare(predicted)?.Contains("player.hp", StringComparison.Ordinal) == true, "HP mismatch must reject");
        predicted = native; predicted.Gold++;
        Check(ProductionCapture.Compare(predicted)?.Contains("gold", StringComparison.Ordinal) == true, "gold mismatch must reject");
        predicted = native; predicted.Outcome = Outcome.Defeat;
        Check(ProductionCapture.Compare(predicted)?.Contains("outcome", StringComparison.Ordinal) == true, "outcome mismatch must reject");
        predicted = native; predicted.Outcome = Outcome.EventDefeat;
        Check(ProductionCapture.Compare(predicted)?.Contains("event-defeat", StringComparison.Ordinal) == true, "unsupported event terminal must reject");
        predicted = native; predicted.PotionSlotCount = 1;
        Check(ProductionCapture.Compare(predicted)?.Contains("potions.count", StringComparison.Ordinal) == true, "potion mismatch must reject");
        predicted = native; predicted.RelicCount = 1;
        Check(ProductionCapture.Compare(predicted)?.Contains("relics.count", StringComparison.Ordinal) == true, "relic mismatch must reject");
        predicted = native; predicted.RngStream(RngStream.Shuffle).Counter++;
        Check(ProductionProjection.Compare(predicted, native, ProductionPath.Victory)?.Contains("rng.", StringComparison.Ordinal) == true, "RNG mismatch must reject");
        ProductionCapture.Begin(() => throw new InvalidDataException("capture-original-failure"));
        typeof(ProductionCapture).GetMethod("VictoryPrefix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        rejected = false;
        try { ProductionCapture.RequireValue(); }
        catch (InvalidDataException error) when (error.Message == "capture-original-failure") { rejected = true; }
        Check(rejected, "capture failure must preserve original reason");
        ProductionCapture.Clear();
        Check(ProductionCapture.Value == null && ProductionCapture.Path == ProductionPath.None, "new combat clears prior terminal evidence before any action");
        ProductionCapture.End();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        BlitzSolver.OfflineHost.Runtime.MainLoopContext loop = new();
        rejected = false;
        try { loop.RunUntilCompleted(Task.CompletedTask, TimeSpan.FromSeconds(1), "cancelled-drain", cancellation.Token); }
        catch (OperationCanceledException) { rejected = true; }
        Check(rejected, "message pump cancellation before drain");
        using CancellationTokenSource pendingCancellation = new();
        pendingCancellation.CancelAfter(TimeSpan.FromMilliseconds(10));
        rejected = false;
        try { loop.RunUntilCompleted(new TaskCompletionSource().Task, TimeSpan.FromSeconds(1), "pending-cancel", pendingCancellation.Token); }
        catch (OperationCanceledException) { rejected = true; }
        Check(rejected, "message pump cancels pending native wait");
        foreach (BlitzSolver.OfflineHost.FirstCombatOptions invalid in new[]
        {
            new BlitzSolver.OfflineHost.FirstCombatOptions(AssemblyBootstrap.Sts2DataDir, TimeoutSeconds: 0),
            new BlitzSolver.OfflineHost.FirstCombatOptions(AssemblyBootstrap.Sts2DataDir, UnlockProfile: "unknown"),
            new BlitzSolver.OfflineHost.FirstCombatOptions(AssemblyBootstrap.Sts2DataDir, SearchTier: "unknown"),
            new BlitzSolver.OfflineHost.FirstCombatOptions(AssemblyBootstrap.Sts2DataDir, MemoryBudgetBytes: 0),
            new BlitzSolver.OfflineHost.FirstCombatOptions(AssemblyBootstrap.Sts2DataDir, StopAfter: "unknown"),
            new BlitzSolver.OfflineHost.FirstCombatOptions(AssemblyBootstrap.Sts2DataDir, MaxRooms: 0),
            new BlitzSolver.OfflineHost.FirstCombatOptions(AssemblyBootstrap.Sts2DataDir, MaximumSteps: 0),
            new BlitzSolver.OfflineHost.FirstCombatOptions(AssemblyBootstrap.Sts2DataDir, MaximumDecisions: 0),
        })
        {
            rejected = false;
            try { BlitzSolver.OfflineHost.FirstCombat.Run(invalid); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "public facade rejects invalid configuration before native startup");
        }
        Console.WriteLine($"FIRST-COMBAT-CONTRACTS checks={checks} status=same");
        return 0;
    }
}
