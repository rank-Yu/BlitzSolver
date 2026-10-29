// Lifecycle fixtures execute original commands and compare an independent original selector.
// Fixture event options are test-only; no production state injection is used.
using System.Text.Json;
using Sts2Solver.Ai.Run;
using Sts2Solver.Autoplay;
using Sts2Solver.Import;
using Sts2Solver.OfflineHost.Run;
using Sts2Solver.Simulation.Run;
using Sts2Solver.TestHost;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;
using Bootstrap = Sts2Solver.OfflineHost.Runtime.GameBootstrap;

namespace Differential;

internal static class EventSelectionHostChecks
{
    private static MainLoopContext _loop = null!;
    private static int _checks, _cases;
    private static void Wait(Task task) => _loop.RunUntilCompleted(task, TimeSpan.FromSeconds(60), "event-selection");
    private static T Get<T>(Task<T> task) { Wait(task); return task.GetAwaiter().GetResult(); }
    private static void Check(bool valid, string reason) { _checks++; if (!valid) throw new InvalidDataException("event-selection:" + reason); }
    private static string State(NativeRun run, Player player) => NativeRunDump.Capture(run, player)
        + RunDump.Capture(RunStateImporter.Import(run, player))
        + JsonSerializer.Serialize(run.MapPointHistory, new JsonSerializerOptions { IncludeFields = true });
    private static RunPilotAnswer Pick(RunView view, int index = 0, bool confirmed = true)
        => new(RunDecisionProtocol.Select(view.Request, [view.Request.Options[index].OptionId]), confirmed);
    private static (NativeRun Run, Player Player, EventModel Model) Start(bool book = false)
    {
        if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false);
        var created = RunTestHost.StartNewRunBeforeChoicesAsync(ModelDb.AllCharacters.Single(c => c.GetType().Name == "Ironclad"),
            0, "R3B8-EVENT", UnlockState.none);
        var (run, player) = Get(created);
        Wait(RunManager.Instance.EnterRoomDebug(RoomType.Event,
            model: book ? ModelDb.Event<SelfHelpBook>() : ModelDb.Event<ThisOrThat>(), showTransition: false));
        return (run, player, ((EventRoom)run.CurrentRoom!).LocalMutableEvent);
    }
    internal static int Run()
    {
        _loop = new(); SynchronizationContext.SetSynchronizationContext(_loop);
        Bootstrap.ApplyGodotBypasses(); Bootstrap.SkipGodotNodeStaticConstructors(); Bootstrap.InitializeStaticState();
        Book();
        foreach (string mode in new[] { "choose-required", "choose-skip", "choose-empty", "remove", "transform", "transform-auto",
            "enchant-player", "enchant-then-generic", "enchant-filter", "enchant-list", "enchant-auto", "multi", "manual" }) Command(mode);
        Faults();
        Console.WriteLine($"EVENTSELECTION status=pass cases={_cases} checks={_checks}");
        return 0;
    }
    private static void Book()
    {
        var (run, player, model) = Start(book: true);
        var host = new OfflineRunHost(run, player, _ => ValueTask.FromResult(model.IsFinished
            ? new OfflineRunBoundary(OfflineBoundaryKind.Paused, Detail: "book-complete")
            : new OfflineRunBoundary(OfflineBoundaryKind.Event, Event: model)));
        int children = 0;
        var result = Get(RunPilot.RunAsync(host, new Policy((view, _) =>
        {
            if (view.Request.Kind == RunDecision.CardSelect)
            {
                children++;
                Check(view.Request.SelectionPurpose == RunSelectionPurpose.Enchant
                    && view.Request.SelectionPreviewKnowledge == RunDecisionKnowledge.Unknown, "book purpose and unknown preview");
            }
            return ValueTask.FromResult(Pick(view, view.Request.Kind == RunDecision.CardSelect ? 1 : 0));
        })));
        Check(result.Reason == RunStopReason.Paused && children == 1 && result.Decisions == 2
            && player.Deck.Cards.Count(c => c.Enchantment is Sharp { Amount: 2 }) == 1, "real SelfHelpBook child/Sharp/history");
        string actual = State(run, player); Wait(host.DisposeAsync().AsTask());
        var (dr, dp, dm) = Start(book: true);
        using (CardSelectCmd.UseSelector(new Selector("book")))
        {
            RunManager.Instance.EventSynchronizer.ChooseLocalOption(0);
            Wait(RunManager.Instance.EventSynchronizer.AwaitPendingOptionTasks());
        }
        Check(actual == State(dr, dp), "SelfHelpBook direct native selector/full dump/history/RNG");
        _cases++;
    }
    private static async Task Effect(string mode, NativeRun run, Player player)
    {
        CardSelectorPrefs prefs = new(CardSelectorPrefs.EnchantSelectionPrompt, mode == "multi" ? 2 : 1)
            { RequireManualConfirmation = mode == "manual" || mode.EndsWith("-auto", StringComparison.Ordinal), Cancelable = true };
        IEnumerable<CardModel> selected;
        if (mode.StartsWith("choose-", StringComparison.Ordinal))
        {
            var cards = mode == "choose-empty" ? Array.Empty<CardModel>()
                : new[] { run.CreateCard(player.Deck.Cards[0].CanonicalInstance, player), run.CreateCard(player.Deck.Cards[0].CanonicalInstance, player) };
            CardModel? card = await CardSelectCmd.FromChooseACardScreen(new BlockingPlayerChoiceContext(), cards, player, mode == "choose-skip");
            if (card != null) await CardPileCmd.Add(card, PileType.Deck);
            return;
        }
        if (mode == "remove")
        {
            selected = await CardSelectCmd.FromDeckForRemoval(player, new(CardSelectorPrefs.RemoveSelectionPrompt, 1));
            await CardPileCmd.RemoveFromDeck(selected.ToArray(), showPreview: false); return;
        }
        if (mode.StartsWith("transform", StringComparison.Ordinal))
        {
            var tp = new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt,
                mode == "transform-auto" ? player.Deck.Cards.Count : 1) { RequireManualConfirmation = mode != "transform-auto" };
            selected = await CardSelectCmd.FromDeckForTransformation(player, tp);
            foreach (var card in selected) await CardCmd.TransformToRandom(card, player.PlayerRng.Transformations);
            return;
        }
        EnchantmentModel enchantment = ModelDb.Enchantment<Sharp>();
        if (mode is "enchant-player" or "enchant-then-generic") selected = await CardSelectCmd.FromDeckForEnchantment(player, enchantment, 2, prefs);
        else if (mode == "enchant-filter") selected = await CardSelectCmd.FromDeckForEnchantment(player, enchantment, 2, c => c!.Type == CardType.Attack, prefs);
        else if (mode is "enchant-list" or "enchant-auto")
        {
            CardModel[] cards = player.Deck.Cards.Where(enchantment.CanEnchant).Reverse().ToArray();
            if (mode == "enchant-auto") cards = cards.Take(1).ToArray();
            selected = await CardSelectCmd.FromDeckForEnchantment(cards, enchantment, 2, prefs);
        }
        else selected = await CardSelectCmd.FromDeckGeneric(player, prefs);
        foreach (CardModel card in selected) CardCmd.Enchant<Sharp>(card, 2);
        if (mode == "enchant-then-generic")
            await CardSelectCmd.FromDeckGeneric(player, new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1));
    }
    private static void Fixture(EventModel model, Func<Task> effect)
    {
        var options = (List<EventOption>)model.CurrentOptions; options.Clear();
        options.Add(new EventOption(model, effect, "FIXTURE.SELECTION"));
    }
    private static void Command(string mode)
    {
        Console.WriteLine("EVENTSELECTION case=" + mode);
        var (run, player, model) = Start();
        Fixture(model, () => Effect(mode, run, player));
        var host = new OfflineRunHost(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Event, Event: model)));
        var boundary = Get(host.WaitForBoundaryAsync(default).AsTask());
        int children = 0;
        Wait(host.ExecuteAsync(Pick(boundary.View), (view, _) =>
        {
            children++;
            RunDecisionRequest request = view.Request;
            if (mode == "enchant-then-generic")
                Check(request.SelectionPurpose == (children == 1 ? RunSelectionPurpose.Enchant : RunSelectionPurpose.None),
                    "outer enchantment context cannot pollute a later unrelated selection");
            if (mode == "choose-required")
            {
                Check(request.MinSelect == 1 && request.Cancelable == 0 && request.SelectionPurpose == RunSelectionPurpose.None, "required choose rejects empty");
                bool rejected = false;
                try { RunDecisionProtocol.Select(request, []); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "required choose cannot skip despite raw callback min zero");
            }
            if (mode == "choose-skip")
            {
                Check(request.MinSelect == 0 && request.MaxSelect == 1, "choose canSkip visible bounds");
                return ValueTask.FromResult(new RunPilotAnswer(RunDecisionProtocol.Select(request, [])));
            }
            if (mode == "enchant-list") Check(request.Options[0].Card.Id == RunDecisionImporter.CardSelect(player,
                player.Deck.Cards.Where(ModelDb.Enchantment<Sharp>().CanEnchant).ToArray(), new(CardSelectorPrefs.EnchantSelectionPrompt, 1),
                NativeCardSelectionKind.DeckEnchant, request.Token, RunSelectionPurpose.Enchant, ModelDb.Enchantment<Sharp>()).Request.Options[0].Card.Id,
                "enchantment list preserves native deck ordering");
            uint[] ids = Enumerable.Range(0, (int)request.MinSelect).Select(i => request.Options[i + (request.OptionCount > request.MinSelect ? 1 : 0)].OptionId).ToArray();
            return ValueTask.FromResult(new RunPilotAnswer(RunDecisionProtocol.Select(request, ids), true));
        }, default).AsTask());
        Check(children == (mode is "choose-empty" or "enchant-auto" or "transform-auto" ? 0 : mode == "enchant-then-generic" ? 2 : 1), "original auto-selection callback count " + mode);
        string actual = State(run, player);
        Wait(host.DisposeAsync().AsTask());
        var (dr, dp, dm) = Start();
        Fixture(dm, () => Effect(mode, dr, dp));
        using (CardSelectCmd.UseSelector(new Selector(mode)))
        {
            RunManager.Instance.EventSynchronizer.ChooseLocalOption(0);
            Wait(RunManager.Instance.EventSynchronizer.AwaitPendingOptionTasks());
        }
        Check(actual == State(dr, dp), "original effect/full dump/history/RNG " + mode);
        _cases++;
    }
    private static void Faults()
    {
        foreach (string mode in new[] { "cancel", "stale", "unconfirmed", "failure" })
        {
            var (run, player, model) = Start();
            Fixture(model, () => Effect(mode == "unconfirmed" ? "manual" : "enchant-filter", run, player));
            var host = new OfflineRunHost(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Event, Event: model)));
            var boundary = Get(host.WaitForBoundaryAsync(default).AsTask());
            using CancellationTokenSource cancellation = new();
            TaskCompletionSource<RunPilotAnswer> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RunView child = default;
            Task executing = host.ExecuteAsync(Pick(boundary.View), (view, _) => { child = view; return new(answer.Task); }, cancellation.Token).AsTask();
            Check(!executing.IsCompleted && child.Request.SelectionPurpose == (mode == "unconfirmed" ? RunSelectionPurpose.None : RunSelectionPurpose.Enchant), "actual awaited enchant child " + mode);
            if (mode == "cancel") cancellation.Cancel();
            if (mode == "stale") CardCmd.Enchant<Sharp>(player.Deck.Cards[0], 1);
            if (mode == "failure") answer.SetException(new InvalidDataException("fixture-child-failure"));
            else answer.SetResult(Pick(child, confirmed: mode != "unconfirmed"));
            bool rejected = false;
            try { Wait(executing); }
            catch (OperationCanceledException) when (mode == "cancel") { rejected = true; }
            catch (InvalidDataException) when (mode is "stale" or "failure" or "unconfirmed") { rejected = true; }
            catch (Sts2Solver.Simulation.Combat.UnsupportedException) when (mode == "stale") { rejected = true; }
            Check(rejected && CardSelectCmd.Selector == null, "failure/cancellation restore selector " + mode);
            Check(!player.Deck.Cards.Any(c => c.Enchantment is Sharp { Amount: 2 }), "late/rejected answer has no effect " + mode);
            Wait(host.DisposeAsync().AsTask());
            _cases++;
        }
    }
    private sealed class Policy(Func<RunView, CancellationToken, ValueTask<RunPilotAnswer>> callback) : IRunPolicy
    { public ValueTask<RunPilotAnswer> DecideAsync(RunView view, CancellationToken token) => callback(view, token); }
    private sealed class Selector(string mode) : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards, int min, int max)
            => Task.FromResult(mode == "choose-skip" ? Enumerable.Empty<CardModel>() : cards.Skip(cards.Count() > Math.Max(1, min) ? 1 : 0).Take(Math.Max(1, min)));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> cards,
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives) => throw new InvalidDataException("unexpected-reward");
    }
}
