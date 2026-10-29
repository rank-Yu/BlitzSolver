using System.Security.Cryptography;
using System.Text;
using Sts2Solver.Ai.Run;
using Sts2Solver.Autoplay;
using Sts2Solver.Import;
using Sts2Solver.OfflineHost.Combat;
using Sts2Solver.OfflineHost.Run;
using Sts2Solver.Simulation.Combat;
using Sts2Solver.Simulation.Run;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;

namespace Sts2Solver.OfflineHost.Runtime;

internal static class FirstCombatSession
{
    internal static FirstCombatResult Run(FirstCombatOptions options, CancellationToken external)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(external, timeout.Token);
        CancellationToken token = lifetime.Token;
        MainLoopContext loop = new();
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(loop);
        StateSummary? starting = null, after = null, afterRewards = null, finalState = null;
        List<RoomSummary> rooms = [];
        List<CombatSummary> combats = [];
        bool multi = options.StopAfter is "rooms" or "act1";
        bool bossCompleted = false;
        NextRoomSummary? nextRoom = null;
        CombatSummary? summary = null;
        SmokePolicy policy = new(multi, () => finalState = null);
        FirstCombatResult Result(string status, string? milestone, string? detail)
        {
            if (status is "Failed" or "Cancelled" or "Timeout") finalState = null;
            return new(status, milestone, detail, options, options.StopAfter == "act1" ? "act1-smoke-v1" : multi ? "rooms-smoke-v1" : options.StopAfter == "next-room" ? "next-room-smoke-v1" : "first-combat-smoke-v1",
                [.. policy.Choices], starting, after, summary, afterRewards, nextRoom,
                multi ? [.. rooms] : null, multi ? [.. combats] : null, finalState);
        }
        try
        {
            token.ThrowIfCancellationRequested();
            GameBootstrap.ApplyGodotBypasses();
            GameBootstrap.SkipGodotNodeStaticConstructors();
            GameBootstrap.InitializeStaticState();
            TerminalBattleCapture.Install();
            Task<(NativeRun Run, Player Player)> create = NewRunFactory.Create(options, token);
            loop.RunUntilCompleted(create, TimeSpan.FromSeconds(options.TimeoutSeconds), "new-run", token);
            (NativeRun run, Player player) = create.GetAwaiter().GetResult();
            starting = Summarize(run, player);
            OfflineRunHost? runHost = null;
            CombatRoom? rewardRoom = null;
            AbstractRoom? completedRoom = null, shopRoom = null;
            bool shopClosed = false;
            OfflineRunBoundary Travel()
            {
                AbstractRoom current = run.CurrentRoom ?? throw new InvalidDataException("room-missing");
                if (multi && current is not EventRoom { LocalMutableEvent.Id.Entry: "NEOW" }
                    && run.CurrentMapCoord is { } coord && !ReferenceEquals(completedRoom, current))
                {
                    // Neow occupies row zero; it is not a completed map-node room.
                    completedRoom = current;
                    finalState = Summarize(run, player);
                    rooms.Add(new(current.RoomType.ToString(), current.ModelId?.Entry ?? "", coord.col, coord.row, finalState));
                }
                if (options.StopAfter == "rooms" && rooms.Count >= options.MaxRooms)
                    return new(OfflineBoundaryKind.Paused, Detail: "room-budget-reached");
                if (bossCompleted)
                    return new(OfflineBoundaryKind.Paused, Detail: options.StopAfter == "act1" ? "act1-complete" : "scope-act-end");
                return new(OfflineBoundaryKind.Map, TravelEnabled: () => !CombatManager.Instance.IsInProgress
                    && RunManager.Instance.ActionExecutor.FinishedExecutingActions().IsCompleted);
            }
            async ValueTask<OfflineRunBoundary> Boundary(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                finalState = Summarize(run, player);
                if (player.Creature.IsDead || run.IsGameOver)
                {
                    await RunManager.Instance.ActionExecutor.FinishedExecutingActions().WaitAsync(ct);
                    if (run.IsGameOver && player.Creature.IsDead)
                    {
                        finalState = Summarize(run, player);
                        return new(OfflineBoundaryKind.Terminal, Terminal: RunStopReason.Death, Detail: "native-run-death");
                    }
                    throw new InvalidDataException("native-run-death-unconfirmed");
                }
                if (multi && (run.CurrentActIndex != 0 || run.CurrentRoomCount != 1))
                    return new(OfflineBoundaryKind.Paused, Detail: "scope-act-or-nested-room");
                if (rewardRoom != null)
                {
                    if (!ReferenceEquals(run.CurrentRoom, rewardRoom))
                    {
                        await runHost!.ConfirmRewardsRoomExitAsync(ct);
                        await RunManager.Instance.ActionExecutor.FinishedExecutingActions().WaitAsync(ct);
                        if (run.CurrentRoom is CombatRoom) WaitPlayable(loop, run, player, ct);
                        if (!multi)
                        {
                            var coord = run.CurrentMapCoord ?? throw new InvalidDataException("next-room-coordinate-missing");
                            AbstractRoom next = run.CurrentRoom ?? throw new InvalidDataException("next-room-missing");
                            nextRoom = new(next.RoomType.ToString(), next.ModelId?.Entry ?? "",
                                coord.col, coord.row, Summarize(run, player));
                            return new(OfflineBoundaryKind.Paused, Detail: "next-room-entered");
                        }
                        rewardRoom = null;
                    }
                    else
                    {
                        if (rewardRoom.RoomType == RoomType.Boss)
                        {
                            if (!runHost!.RewardsSetCompleted) return new(OfflineBoundaryKind.Rewards);
                            if (run.CurrentActIndex != 0 || !rewardRoom.IsPreFinished || CombatManager.Instance.IsInProgress
                                || combats.LastOrDefault() is not { Outcome: "victory", Deviations: 0, DeploymentFailures: 0 })
                                throw new InvalidDataException("act1-boss-completion-unconfirmed");
                            if (run.Map.SecondBossMapPoint != null && run.CurrentMapCoord == run.Map.BossMapPoint.coord)
                                throw new Sts2Solver.Simulation.Combat.UnsupportedException("act1-second-boss-boundary");
                            await runHost.SettleBossRewardsAsync(ct);
                            afterRewards ??= Summarize(run, player);
                            bossCompleted = true;
                            return Travel();
                        }
                        if (!runHost!.RewardsProceedConfirmed) return new(OfflineBoundaryKind.Rewards);
                        afterRewards ??= Summarize(run, player);
                        return Travel();
                    }
                }
                switch (run.CurrentRoom)
                {
                    case CombatRoom room:
                        if (room.RoomType is not (RoomType.Monster or RoomType.Elite or RoomType.Boss))
                            return new(OfflineBoundaryKind.Paused, Detail: "scope-room:" + room.RoomType);
                        WaitPlayable(loop, run, player, ct);
                        return new(OfflineBoundaryKind.Combat);
                    case EventRoom room:
                        if (!room.LocalMutableEvent.IsFinished)
                            return new(OfflineBoundaryKind.Event, Event: room.LocalMutableEvent);
                        return Travel();
                    case RestSiteRoom room:
                        if (room.Options.Count != 0) return new(OfflineBoundaryKind.Rest, Rest: room);
                        await RunManager.Instance.RestSiteSynchronizer.AfterAllRestSitesCompleted().WaitAsync(ct);
                        return Travel();
                    case TreasureRoom room:
                        if (runHost!.TreasureProceedConfirmed(room)) return Travel();
                        return new(OfflineBoundaryKind.Treasure, Treasure: room);
                    case MerchantRoom room:
                        if (!ReferenceEquals(shopRoom, room)) { shopRoom = room; shopClosed = false; policy.BeginShop(); }
                        if (shopClosed) return Travel();
                        return new(OfflineBoundaryKind.Shop, Shop: room,
                            ShopActive: () => !shopClosed && ReferenceEquals(run.CurrentRoom, room), CloseShop: () => shopClosed = true);
                    default:
                        return new(OfflineBoundaryKind.Paused, Detail: "scope-room:" + run.CurrentRoom?.GetType().Name);
                }
            }
            async ValueTask<RunCombatResult> Combat(CancellationToken ct)
            {
                finalState = null; // No previous-room state may masquerade as this combat terminal final state.
                TerminalBattleCapture.Clear();
                CombatState combat = WaitPlayable(loop, run, player, ct);
                Rules rules = RulesCompiler.Compile(combat, player, run);
                LiveCombatTracker tracker = new(combat);
                Battle opening = tracker.ImportRoot(combat, player, rules);
                tracker.Observe(opening);
                NativePilotHost host = new(loop, combat, player, rules, tracker, opening,
                    SearchPolicyCapture.Capture(combat, player),
                    SearchPerformanceProfile.For(SearchPerformanceProfile.ParseTier(options.SearchTier)), options.MemoryBudgetBytes,
                    widening: true);
                CombatPilotResult result = await CombatPilot.RunAsync(host, "first-combat", ct);
                finalState = Summarize(run, player);
                Battle? terminal = TerminalBattleCapture.Value;
                CombatSummary currentSummary = new(result.Status, result.Outcome, result.Turns, host.Actions, result.Searches,
                    result.SearchLog.Sum(s => (long)s.ExpandedNodes), result.Deviations, result.DeploymentFailures,
                    result.FailureLog.FirstOrDefault()?.Detail ?? result.PauseReason,
                    Hash(string.Join("\n", BattleDump.Lines(opening))),
                    terminal is { } t ? Hash(string.Join("\n", BattleDump.Lines(t))) : null,
                    TerminalBattleCapture.Path.ToString(), terminal?.Player.CurrentHp,
                    terminal?.Player.MaxHp, terminal?.Gold);
                summary ??= currentSummary;
                combats.Add(currentSummary);
                if (result.DeploymentFailures != 0 || result.Deviations != 0 || result.Status == "failed")
                    return new(RunStopReason.Failed, currentSummary.Failure ?? "combat-deviation");
                if (result.Status == "cancelled") return new(RunStopReason.Cancelled, "combat-cancelled");
                if (result.Outcome is "victory" or "defeat")
                {
                    if (terminal == null || CombatManager.Instance.IsInProgress)
                        return new(RunStopReason.Failed, "native-terminal-unconfirmed");
                    loop.RunUntilCompleted(RunManager.Instance.ActionExecutor.FinishedExecutingActions(),
                        TimeSpan.FromSeconds(options.TimeoutSeconds), "post-combat-queue", ct);
                    finalState = Summarize(run, player);
                    after ??= finalState;
                    if (result.Outcome == "victory" && options.StopAfter is "next-room" or "rooms" or "act1")
                    {
                        rewardRoom = run.CurrentRoom as CombatRoom ?? throw new InvalidDataException("reward-room-missing");
                        await runHost!.OfferCombatRewardsAsync(rewardRoom, ct);
                        return new();
                    }
                    return result.Outcome == "victory"
                        ? new(RunStopReason.Paused, "first-combat-complete")
                        : new(RunStopReason.Death, "combat-death");
                }
                return new(RunStopReason.Failed, result.PauseReason ?? "combat-incomplete");
            }
            runHost = new(run, player, Boundary, Combat);
            try
            {
                Task<RunPilotResult> task = RunPilot.RunAsync(runHost, policy, options.MaximumDecisions, options.MaximumSteps, token);
                loop.RunUntilCompleted(task, TimeSpan.FromSeconds(options.TimeoutSeconds), "run-pilot", token);
                RunPilotResult result = task.GetAwaiter().GetResult();
                if (timeout.IsCancellationRequested) return Result("Timeout", null, "overall-timeout");
                return Result(result.Reason.ToString(), result.Detail is "first-combat-complete" or "next-room-entered" or "room-budget-reached" or "act1-complete" ? result.Detail : null, result.Detail);
            }
            finally
            {
                runHost.InvalidatePending();
                loop.RunUntilCompleted(runHost.DisposeAsync().AsTask(), TimeSpan.FromSeconds(5), "host-dispose");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return Result(timeout.IsCancellationRequested ? "Timeout" : "Cancelled", null, timeout.IsCancellationRequested ? "overall-timeout" : "external-cancellation"); }
        catch (TimeoutException error) { return Result("Timeout", null, error.Message); }
        catch (UnsupportedImportException error) { return Result("Failed", null, error.Message); }
        catch (Sts2Solver.Simulation.Combat.UnsupportedException error) { return Result("Failed", null, error.Message); }
        catch (CapacityException error) { return Result("Failed", null, error.Message); }
        catch (InvalidDataException error) { return Result("Failed", null, error.Message); }
        catch (OfflineDeploymentException error) { return Result("Failed", null, error.Message); }
        finally { TerminalBattleCapture.End(); SynchronizationContext.SetSynchronizationContext(previous); }
    }
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static StateSummary Summarize(NativeRun run, Player player)
        => new(Hash(RunDump.Capture(RunStateImporter.Import(run, player))), (int)player.Creature.CurrentHp,
            (int)player.Creature.MaxHp, player.Gold,
            player.Deck.Cards.Select(c => c.Id.Entry + "+" + c.CurrentUpgradeLevel).ToArray(),
            player.Relics.Select(r => r.Id.Entry).ToArray(),
            player.PotionSlots.Select(p => p?.Id.Entry ?? "").ToArray());
    private static CombatState WaitPlayable(MainLoopContext loop, NativeRun run, Player player, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), run))
                throw new InvalidDataException("playable-run-changed");
            CombatState? state = CombatManager.Instance.DebugOnlyGetState();
            if (state != null && CombatManager.Instance.IsInProgress && state.CurrentSide == CombatSide.Player
                && ReferenceEquals(MegaCrit.Sts2.Core.Context.LocalContext.GetMe(state), player)
                && player.PlayerCombatState?.Phase == PlayerTurnPhase.Play
                && RunManager.Instance.ActionExecutor.FinishedExecutingActions().IsCompleted)
                return state;
            loop.Pump(TimeSpan.FromMilliseconds(5));
        }
    }
    private sealed class SmokePolicy(bool multi, Action dispatching) : IRunPolicy
    {
        internal List<VisibleChoice> Choices { get; } = [];
        private bool _shopped;
        internal void BeginShop() => _shopped = false;
        private static string Key(in RunDecisionRequest request, RunDecisionSlice slice)
        { ReadOnlySpan<byte> keys = request.Keys; return Encoding.UTF8.GetString(keys.Slice(slice.Start, slice.Count)); }
        public ValueTask<RunPilotAnswer> DecideAsync(RunView view, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            RunDecisionRequest request = view.Request;
            if (!multi && request.Kind == RunDecision.Event
                && (!RunCatalog.TryEvent("NEOW", out ushort neow) || request.Source != new RunRef(RunRefKind.Event, neow)))
                throw new Sts2Solver.Simulation.Combat.UnsupportedException("smoke-policy-event-outside-neow");
            string[] names = new string[request.OptionCount];
            List<int> legal = [];
            int chosen = -1;
            for (int i = 0; i < names.Length; i++)
            {
                RunDecisionOption option = request.Options[i];
                names[i] = option.Kind == RunDecisionOptionKind.Reward ? "Reward:" + option.RewardType
                    : option.Kind == RunDecisionOptionKind.Card ? "Card:" + Registry.Card(option.Card.Id).NativeId
                    : option.Model.Kind == RunRefKind.Relic ? Registry.Relic(option.Model.Id).NativeId
                    : option.Kind == RunDecisionOptionKind.MapPoint
                        ? $"MapPoint:{option.Coord.Col},{option.Coord.Row}:{option.PointType}"
                        : option.Kind + ":" + option.OptionId;
                if (multi && option.NativeKey.Count != 0) names[i] += ":" + Key(request, option.NativeKey);
                if ((option.Flags & (RunDecisionOptionFlags.Disabled | RunDecisionOptionFlags.Locked)) == 0)
                {
                    legal.Add(i);
                    if (chosen < 0 || names[i].Split(':')[0] == "LAVA_ROCK") chosen = i;
                }
            }
            if (multi && request.Kind == RunDecision.Event
                && (!RunCatalog.TryEvent("NEOW", out ushort neowEvent) || request.Source != new RunRef(RunRefKind.Event, neowEvent)))
            {
                chosen = legal.LastOrDefault(-1);
                foreach (int i in legal)
                {
                    string key = Key(request, request.Options[i].NativeKey);
                    if (request.Options[i].Kind == RunDecisionOptionKind.Leave || key.Contains("LEAVE", StringComparison.OrdinalIgnoreCase)
                        || key.Contains("GIVE_UP", StringComparison.OrdinalIgnoreCase) || key.Contains("GIVEUP", StringComparison.OrdinalIgnoreCase)) { chosen = i; break; }
                }
            }
            if (multi && request.Kind == RunDecision.MapPoint)
                chosen = legal.OrderBy(i => request.Options[i].Coord.Col).ThenBy(i => request.Options[i].Coord.Row).FirstOrDefault(-1);
            if (multi && request.Kind == RunDecision.Shop)
            {
                chosen = legal.FirstOrDefault(i => request.Options[i].Kind == RunDecisionOptionKind.Leave, -1);
                if (!_shopped)
                {
                    int purchase = legal.FirstOrDefault(i => request.Options[i].Kind == RunDecisionOptionKind.ShopEntry, -1);
                    if (purchase >= 0) { chosen = purchase; _shopped = true; }
                }
            }
            if (request.Kind is not (RunDecision.Event or RunDecision.MapPoint or RunDecision.CardSelect
                or RunDecision.CombatReward or RunDecision.CardReward or RunDecision.PotionDiscard or RunDecision.RestSite or RunDecision.Shop or RunDecision.Treasure or RunDecision.RelicSelect))
                throw new InvalidDataException("smoke-policy-unsupported-decision:" + request.Kind);
            int count = Math.Max(1, (int)request.MinSelect);
            if (count > request.MaxSelect || count > legal.Count)
                throw new InvalidDataException("smoke-policy-no-legal-selection");
            uint[] ids = new uint[count];
            string[] selected = new string[count];
            legal.Remove(chosen);
            legal.Insert(0, chosen);
            for (int i = 0; i < count; i++)
            {
                int index = legal[i];
                ids[i] = request.Options[index].OptionId; selected[i] = names[index];
            }
            Choices.Add(new(request.Kind.ToString(), names, selected));
            dispatching(); // A partially executed native command has no confirmed final snapshot.
            return ValueTask.FromResult(new RunPilotAnswer(RunDecisionProtocol.Select(request, ids), true));
        }
    }
}
