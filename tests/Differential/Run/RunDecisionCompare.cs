// Original model imports and native round trips. Directed debug fixtures and natural startup are distinct.
using System.Text;
using Sts2Solver.Import;
using Sts2Solver.Simulation.Combat;
using Sts2Solver.Simulation.Run;
using Sts2Solver.TestHost;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;

namespace Differential;

internal static class RunDecisionCompare
{
    private static MainLoopContext _loop = null!;
    private static int _checks;
    private static int _cases;

    public static int Run(string[] args)
    {
        if (args.Length != 0) throw new InvalidDataException("--run-decision-compare accepts no arguments");
        _loop = new(); SynchronizationContext.SetSynchronizationContext(_loop);
        GameBootstrap.ApplyGodotBypasses(); GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState(); NativeDecisionTestHooks.Install();
        Case("natural", "neow-map-roundtrip", Natural);
        Case("fixture", "map-hook-and-staleness", MapFixtures);
        Case("fixture", "event-this-or-that", ThisOrThatRoundtrip);
        Case("fixture", "event-real-page-transition", PageRoundtrip);
        Case("fixture", "event-lock-proceed-repeat-and-stale", EventFixtures);
        Case("fixture", "event-multiselect-native-effect", FieldRoundtrip);
        Case("fixture", "rest-heal-native-roundtrip", RestRoundtrip);
        Case("fixture", "rest-smith-nested-and-cancel", SmithRoundtrip);
        Case("fixture", "rest-disabled-and-replacement", RestFixtures);
        Case("fixture", "card-identity-visible-payload-and-stale", CardFixtures);
        Case("fixture", "card-empty-cancel-confirm-and-auto", SelectionBoundaries);
        Case("fixture", "unknown-capacity-and-native-exception", NegativeFixtures);
        Console.WriteLine($"RUNDECISION status=pass cases={_cases} checks={_checks}");
        return 0;
    }
    private static void Case(string kind, string name, Action test)
    {
        Console.WriteLine($"RUNDECISION kind={kind} case={name} status=start");
        try { test(); _cases++; Console.WriteLine($"RUNDECISION kind={kind} case={name} status=pass checks={_checks}"); }
        finally { if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false); }
    }
    private static void Check(bool success, string message)
    {
        _checks++; if (!success) throw new InvalidDataException("decision-check: " + message);
    }
    private static void Wait(Task task) => _loop.RunUntilCompleted(task, TimeSpan.FromSeconds(90), "native decision");
    private static (NativeRun Run, Player Player) Start(bool neow = false, bool natural = false)
    {
        if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false);
        UnlockState unlock = neow ? new UnlockState([new NeowEpoch().Id], [], 1) : UnlockState.none;
        CharacterModel character = ModelDb.AllCharacters.First(c => c.GetType().Name == "Ironclad");
        Task<(NativeRun Run, Player Player)> start = natural
            ? RunTestHost.StartNewRunWithUnlocksAsync(character, 0, "R3B2-DECISION", unlock)
            : RunTestHost.StartNewRunBeforeChoicesAsync(character, 0, "R3B2-DECISION", unlock);
        Wait(start); return start.GetAwaiter().GetResult();
    }
    private static (NativeRun Run, Player Player, EventModel Event) Event<T>() where T : EventModel
    {
        (NativeRun run, Player player) = Start();
        Wait(RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<T>(), showTransition: false));
        EventModel model = ((EventRoom)run.CurrentRoom!).LocalMutableEvent;
        Check(ReferenceEquals(model.Owner, player) && model.CurrentOptions.Count > 0, "real initialized event pending");
        return (run, player, model);
    }
    private static (NativeRun Run, Player Player, RestSiteRoom Room) Rest()
    {
        (NativeRun run, Player player) = Start();
        Wait(RunManager.Instance.EnterRoomDebug(RoomType.RestSite, showTransition: false));
        return (run, player, (RestSiteRoom)run.CurrentRoom!);
    }
    private static string State(NativeRun run, Player player, EventModel? model = null)
    {
        string text = NativeRunDump.Capture(run, player);
        if (model == null) return text;
        MegaCrit.Sts2.Core.Saves.SerializableRng rng = model.Rng.ToSerializable();
        return text + $"\nevent.page={model.Description?.LocEntryKey}\nevent.finished={model.IsFinished}\nevent.rng={rng.counter},{rng.state0},{rng.state1},{rng.state2},{rng.state3}\n"
            + string.Join('\n', model.CurrentOptions.Select(o => $"{o.TextKey}|{o.IsLocked}|{o.IsProceed}|{o.WasChosen}|{o.Relic?.Id.Entry}"));
    }
    private static RunAction Pick(RunDecisionRequest request, params int[] indices)
        => RunDecisionProtocol.Select(request, indices.Select(i => request.Options[i].OptionId).ToArray());
    private static string Key(RunDecisionRequest request, RunDecisionSlice key)
    {
        byte[] bytes = new byte[key.Count];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = request.Keys[key.Start + i];
        return Encoding.UTF8.GetString(bytes);
    }
    private static void Reject(Action action, string reason)
    {
        _checks++;
        try { action(); }
        catch (InvalidDataException e) when (e.Message.Contains(reason, StringComparison.Ordinal)) { return; }
        catch (Sts2Solver.Simulation.Combat.UnsupportedException e) when (e.Message.Contains(reason, StringComparison.Ordinal)) { return; }
        catch (UnsupportedImportException e) when (e.Message.Contains(reason, StringComparison.Ordinal)) { return; }
        throw new InvalidDataException("expected rejection: " + reason);
    }
    private static void RejectSubmit(NativeDecisionTestSession session, RunAction action, string reason,
        NativeRun run, Player player, EventModel? model = null, bool confirmed = true)
    {
        string before = State(run, player, model); int calls = session.NativeCalls;
        Reject(() => Wait(session.Submit(action, confirmed)), reason);
        Check(calls == session.NativeCalls && before == State(run, player, model), "rejection has no native effects: " + reason);
    }
    private static void Natural()
    {
        (NativeRun run, Player player) = Start(neow: true, natural: true);
        NativeDecisionTestSession session = new(); bool enabled = true;
        Check(run.CurrentRoom is EventRoom && run.VisitedMapCoords.Count == 1, "native EnterAct automatically enters Neow");
        EventModel model = ((EventRoom)run.CurrentRoom!).LocalMutableEvent;
        Check(model is Neow, "actual natural Neow model");
        session.Event(model); RunDecisionRequest request = session.Request;
        Check(request.Source.Kind == RunRefKind.Event && request.Options[0].PayloadKind == RunDecisionPayloadKind.Model, "visible Neow relic binding");
        string before = State(run, player, model);
        RunDecisionImporter.Event(model, new(991, 407)).ValidateCurrent();
        Check(before == State(run, player, model), "event import preserves native state and RNG");
        Console.WriteLine("RUNDECISION neow-relics=" + string.Join(",", model.CurrentOptions.Select(o => o.Relic?.Id.Entry)));
        int naturalChoice = Enumerable.Range(0, model.CurrentOptions.Count).Single(i => model.CurrentOptions[i].Relic is LavaRock);
        RunAction chosen = Pick(request, naturalChoice); Wait(session.Submit(chosen));
        Check(model.IsFinished, "Neow finishes through native synchronizer");
        RejectSubmit(session, chosen, "host-no-pending", run, player, model);
        Reject(() => RunDecisionImporter.Event(model, new(991, 408)), "no pending");
        // UI finishing PROCEED is outside CurrentOptions; the host has now reached map travel.
        session.Map(run, () => enabled); RunDecisionRequest after = session.Request;
        Check(after.OptionCount == RunTestHost.GetTravelablePoints(run).Count && after.Token.Revision > chosen.Token.Revision,
            "finished Neow to fresh native map request");
        MapCoord destination = RunTestHost.GetTravelablePoints(run)[0].coord;
        RunAction travel = Pick(after, 0);
        Wait(session.Submit(travel));
        if (run.CurrentRoom is CombatRoom) Wait(new OfflineCombatWaiter(_loop).WaitForPlayableCombatAsync());
        Check(run.VisitedMapCoords[^1] == destination, "native map action enters actual next room");
        _ = RunStateImporter.Import(run, player);
        RejectSubmit(session, travel, "host-no-pending", run, player);
        string actual = State(run, player);
        (NativeRun direct, Player dp) = Start(neow: true, natural: true);
        EventModel dm = ((EventRoom)direct.CurrentRoom!).LocalMutableEvent;
        Wait(NativeDecisionTestHooks.ChooseEvent(dm, dm.CurrentOptions.Single(o => o.Relic is LavaRock)));
        Wait(RunManager.Instance.EnterMapCoord(RunTestHost.GetTravelablePoints(direct)[0].coord));
        if (direct.CurrentRoom is CombatRoom) Wait(new OfflineCombatWaiter(_loop).WaitForPlayableCombatAsync());
        Check(actual == State(direct, dp), "map and Neow agree with independent direct native calls");
    }
    private static void MapFixtures()
    {
        (NativeRun run, Player player) = Start(); bool enabled = true;
        NativeDecisionTestSession session = new(); session.Map(run, () => enabled); RunDecisionRequest request = session.Request;
        enabled = false; RejectSubmit(session, Pick(request, 0), "not enabled", run, player); enabled = true;
        RejectSubmit(session, Pick(request, 0), "host-no-pending", run, player);
        NativeDecisionSnapshot<MapPoint> stale = RunDecisionImporter.Map(run, new(901, 301), () => true);
        run.AddVisitedMapCoord(run.Map.StartingMapPoint.coord); // Directed boundary fixture.
        Reject(stale.ValidateCurrent, "native-stale");
        player.AddRelicInternal(ModelDb.Relic<WingedBoots>().ToMutable());
        NativeDecisionSnapshot<MapPoint> free = RunDecisionImporter.Map(run, new(901, 302), () => true);
        MapPoint[] expected = MapTravel.GetTravelablePointsFrom(run, run.Map.StartingMapPoint).ToArray();
        Check(free.Request.OptionCount == expected.Length, "free travel original Hook legal set");
        for (int i = 0; i < expected.Length; i++) Check(ReferenceEquals(free.Resolve(Pick(free.Request, i))[0], expected[i]), "map exact native instance");
        string before = State(run, player); free.ValidateCurrent(); Check(before == State(run, player), "map read only including RNG");
        Start();
        Reject(free.ValidateCurrent, "active native run");
    }
    private static void ThisOrThatRoundtrip()
    {
        (NativeRun run, Player player, EventModel model) = Event<ThisOrThat>();
        string before = State(run, player, model); NativeDecisionTestSession session = new(); session.Event(model);
        RunDecisionRequest request = session.Request; RunDecisionOption row = request.Options[0];
        Check(Key(request, row.NativeKey) == model.CurrentOptions[0].TextKey, "event exact native key");
        Check(request.Facts[row.Costs.Start].Amount == model.DynamicVars.HpLoss.IntValue
            && request.Facts[row.Effects.Start].Amount == model.DynamicVars.Gold.IntValue
            && row.CostsKnowledge == RunDecisionKnowledge.Complete && row.EffectsKnowledge == RunDecisionKnowledge.Complete, "plain facts from native vars");
        Check(request.Options[1].EffectsKnowledge == RunDecisionKnowledge.Unknown, "unknown effect remains unknown");
        Check(before == State(run, player, model), "typed event import read only");
        RunAction wrong = Pick(request, 0); wrong.Token = new(request.Token.SessionId, request.Token.Revision + 1);
        RejectSubmit(session, wrong, "action-token", run, player, model);
        RunAction invalid = Pick(request, 0); invalid.OptionIds[0] = uint.MaxValue;
        RejectSubmit(session, invalid, "action-option", run, player, model);
        Wait(session.Submit(Pick(request, 0))); string actual = State(run, player, model);
        (NativeRun direct, Player dp, EventModel dm) = Event<ThisOrThat>();
        Wait(NativeDecisionTestHooks.ChooseEvent(dm, dm.CurrentOptions[0]));
        Check(actual == State(direct, dp, dm), "ThisOrThat direct native effect comparison");
    }
    private static void PageRoundtrip()
    {
        (NativeRun run, Player player, EventModel model) = Event<AbyssalBaths>();
        NativeDecisionTestSession session = new(); session.Event(model); RunDecisionRequest initial = session.Request;
        NativeDecisionSnapshot<EventOption> stale = RunDecisionImporter.Event(model, new(88, 999));
        Wait(session.Submit(Pick(initial, 0)));
        Check(model.CurrentOptions.Count == 2 && model.Description!.LocEntryKey.Contains("IMMERSE", StringComparison.Ordinal), "actual native page advanced");
        Reject(stale.ValidateCurrent, "native-stale"); session.Event(model);
        Check(session.Request.Token.Revision > initial.Token.Revision && Key(session.Request, session.Request.PageKey) != Key(initial, initial.PageKey), "page fresh token and key");
        RejectSubmit(session, Pick(initial, 1), "action-token", run, player, model);
        string actual = State(run, player, model);
        (NativeRun direct, Player dp, EventModel dm) = Event<AbyssalBaths>();
        Wait(NativeDecisionTestHooks.ChooseEvent(dm, dm.CurrentOptions[0]));
        Check(actual == State(direct, dp, dm), "actual page transition direct comparison");
    }
    private static void EventFixtures()
    {
        (NativeRun run, Player player, EventModel model) = Event<ThisOrThat>();
        List<EventOption> options = (List<EventOption>)model.CurrentOptions; options.Clear();
        int proceeds = 0;
        options.Add(new EventOption(model, null, "FIXTURE.LOCKED"));
        options.Add(new EventOption(model, () => { proceeds++; return Task.CompletedTask; }, "FIXTURE.PROCEED", isProceed: true));
        options.Add(new EventOption(model, () => Task.CompletedTask, "FIXTURE.REPEAT", disableOnChosen: false));
        Wait(options[2].Chosen()); NativeDecisionTestSession session = new(); session.Event(model); RunDecisionRequest request = session.Request;
        Check(request.OptionCount == 3 && request.Options[0].Flags == RunDecisionOptionFlags.Locked
            && request.Options[1].Kind == RunDecisionOptionKind.Leave && request.Options[2].Flags == RunDecisionOptionFlags.None,
            "locked proceed repeat semantics and order");
        RunAction locked = Pick(request, 1); locked.OptionIds[0] = request.Options[0].OptionId;
        RejectSubmit(session, locked, "action-disabled", run, player, model);
        Wait(session.Submit(Pick(request, 1))); Check(proceeds == 1, "original proceed callback dispatched directly");
        session.Event(model); request = session.Request;
        options[2] = new EventOption(model, () => Task.CompletedTask, "FIXTURE.REPEAT", disableOnChosen: false);
        RejectSubmit(session, Pick(request, 2), "native-stale", run, player, model);
        NativeDecisionSnapshot<EventOption> snapshot = RunDecisionImporter.Event(model, new(5001, 9002));
        RunDecisionRequest copy = snapshot.Request; copy.Options[2].OptionId = 999;
        Check(snapshot.Request.Options[2].OptionId != 999, "copy cannot mutate internal request");
        IReadOnlyList<EventOption> selected = snapshot.Resolve(Pick(snapshot.Request, 2));
        Check(selected is not EventOption[] && ReferenceEquals(selected[0], options[2]), "read only exact bindings");
    }
    private static void FieldRoundtrip()
    {
        (NativeRun run, Player player, EventModel model) = Event<FieldOfManSizedHoles>();
        NativeDecisionTestSession session = new(); PendingNativeCardSelector selector = new(session);
        using IDisposable scope = CardSelectCmd.UseSelector(selector);
        session.Event(model); RunDecisionRequest parent = session.Request; RunDecisionOption row = parent.Options[0];
        Check(parent.Facts[row.Effects.Start].Kind == RunDecisionFactKind.RemoveCards
            && parent.Facts[row.Effects.Start].Amount == model.DynamicVars.Cards.IntValue
            && parent.Facts[row.Effects.Start + 1].Kind == RunDecisionFactKind.AddCard, "Field typed remove and add facts");
        int count = player.Deck.Cards.Count; Task effect = session.Submit(Pick(parent, 0));
        Check(!effect.IsCompleted && session.HasPending && session.Request.Kind == RunDecision.CardSelect, "event actual pending callback");
        RunDecisionRequest child = session.Request;
        Check(child.Token.Revision > parent.Token.Revision && child.MinSelect == model.DynamicVars.Cards.IntValue, "nested revision and bounds");
        RejectSubmit(session, Pick(parent, 0), "action-token", run, player, model);
        CardModel[] removed = [selector.LastOptions![1], selector.LastOptions[0]];
        Wait(session.Submit(Pick(child, 1, 0), confirmed: true)); Wait(effect);
        Check(player.Deck.Cards.Count == count - child.MinSelect + 1 && removed.All(c => !player.Deck.Cards.Any(d => ReferenceEquals(c, d)))
            && player.Deck.Cards.Any(c => c is Normality), "native ordered multi removal and curse effect");
        string actual = State(run, player, model); scope.Dispose();
        (NativeRun direct, Player dp, EventModel dm) = Event<FieldOfManSizedHoles>();
        using IDisposable directScope = CardSelectCmd.UseSelector(new DirectSelector());
        Wait(NativeDecisionTestHooks.ChooseEvent(dm, dm.CurrentOptions[0]));
        Check(actual == State(direct, dp, dm), "multiselect effect independent direct native comparison");
    }
    private static void RestRoundtrip()
    {
        (NativeRun run, Player player, RestSiteRoom room) = Rest();
        player.Creature.SetCurrentHpInternal(player.Creature.CurrentHp - 20);
        NativeDecisionTestSession session = new(); string before = State(run, player); session.Rest(room, player);
        Check(before == State(run, player), "rest import preserves options and RNG");
        RunDecisionRequest request = session.Request;
        int heal = Enumerable.Range(0, room.Options.Count).Single(i => room.Options[i] is HealRestSiteOption);
        Check(Key(request, request.Options[heal].NativeKey) == room.Options[heal].OptionId, "rest native OptionId key");
        Wait(session.Submit(Pick(request, heal))); Check(room.Options.Count == 0, "rest synchronizer completes options");
        string actual = State(run, player);
        (NativeRun direct, Player dp, RestSiteRoom dr) = Rest(); dp.Creature.SetCurrentHpInternal(dp.Creature.CurrentHp - 20);
        Wait(RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(Enumerable.Range(0, dr.Options.Count).Single(i => dr.Options[i] is HealRestSiteOption)));
        Check(actual == State(direct, dp), "rest heal direct native comparison");
    }
    private static void SmithRoundtrip()
    {
        foreach (bool cancel in new[] { true, false })
        {
            (NativeRun run, Player player, RestSiteRoom room) = Rest();
            NativeDecisionTestSession session = new(); PendingNativeCardSelector selector = new(session);
            using IDisposable scope = CardSelectCmd.UseSelector(selector);
            session.Rest(room, player); RunDecisionRequest parent = session.Request;
            int smith = Enumerable.Range(0, room.Options.Count).Single(i => room.Options[i] is SmithRestSiteOption);
            Task effect = session.Submit(Pick(parent, smith));
            Check(!effect.IsCompleted && session.Request.Kind == RunDecision.CardSelect, "smith actual pending callback");
            RunDecisionRequest child = session.Request;
            Check(child.Cancelable == 1 && child.RequireConfirmation == 1 && child.SelectionPurpose == RunSelectionPurpose.Upgrade, "smith complete prefs and purpose");
            Check(child.Token.Revision > parent.Token.Revision, "smith fresh nested revision");
            Reject(() => session.Rest(room, player), "host-busy");
            RunAction answer = cancel ? RunDecisionProtocol.Cancel(child) : Pick(child, 0);
            if (!cancel) RejectSubmit(session, answer, "confirmation", run, player, confirmed: false);
            int upgrades = player.Deck.Cards.Sum(c => c.CurrentUpgradeLevel);
            Wait(session.Submit(answer, confirmed: !cancel)); Wait(effect);
            Check(player.Deck.Cards.Sum(c => c.CurrentUpgradeLevel) == upgrades + (cancel ? 0 : 1), "smith cancel or upgrade native effect");
            Check(cancel ? room.Options.Any(o => o is SmithRestSiteOption) : room.Options.Count == 0, "rest synchronizer smith result");
            RejectSubmit(session, Pick(parent, smith), "host-no-pending", run, player);
            RejectSubmit(session, answer, "host-no-pending", run, player);
            string actual = State(run, player); scope.Dispose();
            (NativeRun direct, Player dp, RestSiteRoom dr) = Rest();
            using IDisposable directScope = CardSelectCmd.UseSelector(new DirectSelector(cancel));
            Wait(RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(Enumerable.Range(0, dr.Options.Count).Single(i => dr.Options[i] is SmithRestSiteOption)));
            Check(actual == State(direct, dp), "smith independent native comparison cancel=" + cancel);
        }
        // A stale nested choice must fail the original waiting chain and release the parent busy state.
        (NativeRun staleRun, Player stalePlayer, RestSiteRoom staleRoom) = Rest();
        NativeDecisionTestSession staleSession = new(); PendingNativeCardSelector staleSelector = new(staleSession);
        using IDisposable staleScope = CardSelectCmd.UseSelector(staleSelector);
        staleSession.Rest(staleRoom, stalePlayer);
        int staleSmith = Enumerable.Range(0, staleRoom.Options.Count).Single(i => staleRoom.Options[i] is SmithRestSiteOption);
        Task staleEffect = staleSession.Submit(Pick(staleSession.Request, staleSmith));
        RunAction staleAnswer = Pick(staleSession.Request, 0);
        Wait(CardPileCmd.RemoveFromDeck(staleSelector.LastOptions![0]));
        RejectSubmit(staleSession, staleAnswer, "native-stale", staleRun, stalePlayer);
        Reject(() => Wait(staleEffect), "native-stale");
        Check(staleEffect.IsCompleted && !staleSession.HasPending, "stale nested callback fails original parent task");
        staleSession.Rest(staleRoom, stalePlayer);
        Check(staleSession.HasPending, "stale parent releases busy state after failure");
    }
    private static void RestFixtures()
    {
        (NativeRun run, Player player, RestSiteRoom room) = Rest();
        foreach (CardModel card in player.Deck.Cards) if (card.IsUpgradable) CardCmd.Upgrade(card);
        NativeDecisionTestSession session = new(); session.Rest(room, player); RunDecisionRequest request = session.Request;
        int smith = Enumerable.Range(0, room.Options.Count).Single(i => room.Options[i] is SmithRestSiteOption);
        Check(request.Options[smith].Flags == RunDecisionOptionFlags.Disabled, "disabled smith preserved");
        RunAction disabled = Pick(request, 0); disabled.OptionIds[0] = request.Options[smith].OptionId;
        RejectSubmit(session, disabled, "action-disabled", run, player);
        NativeDecisionSnapshot<RestSiteOption> frozen = RunDecisionImporter.Rest(room, player, new(55, 66));
        List<RestSiteOption> options = (List<RestSiteOption>)room.Options;
        RestSiteOption old = options[0]; options[0] = new HealRestSiteOption(player);
        Check(old.Equals(options[0]) && !ReferenceEquals(old, options[0]), "native rest equality is not identity");
        Reject(frozen.ValidateCurrent, "native-stale");
    }
    private static void CardFixtures()
    {
        (NativeRun run, Player player) = Start();
        List<CardModel> cards = player.Deck.Cards.Where(c => c.Id == player.Deck.Cards[0].Id).Take(2).ToList();
        Check(cards.Count == 2 && !ReferenceEquals(cards[0], cards[1]), "same model distinct native instances");
        CardCmd.Upgrade(cards[0]); CardModel enchanted = cards.First(c => ModelDb.Enchantment<PerfectFit>().CanEnchant(c));
        CardCmd.Enchant<PerfectFit>(enchanted, 1);
        CardSelectorPrefs prefs = new(CardSelectorPrefs.RemoveSelectionPrompt, 0, 2) { Cancelable = true, RequireManualConfirmation = true };
        NativeDecisionSnapshot<CardModel> snapshot = RunDecisionImporter.CardSelect(player, cards, prefs, NativeCardSelectionKind.DeckGeneric, new(7654321, 1234567));
        RunDecisionRequest request = snapshot.Request;
        Check(request.Options[0].Card.Id == request.Options[1].Card.Id && request.Options[0].Card.Upgrade != request.Options[1].Card.Upgrade
            && request.Options[0].Card.HasEnchantment == 1, "upgrade enchantment and duplicate models preserved");
        IReadOnlyList<CardModel> resolved = snapshot.Resolve(Pick(request, 1, 0));
        Check(ReferenceEquals(resolved[0], cards[1]) && ReferenceEquals(resolved[1], cards[0]), "ordered whole-list resolution");
        string before = State(run, player); snapshot.ValidateCurrent(); Check(before == State(run, player), "card import read only");
        Reject(() => RunDecisionImporter.CardSelect(player, [cards[0], cards[0]], prefs, NativeCardSelectionKind.DeckGeneric, new(7654321, 1234570)), "repeated native instance");
        cards.Reverse(); Reject(snapshot.ValidateCurrent, "native-stale"); cards.Reverse();
        snapshot = RunDecisionImporter.CardSelect(player, cards, prefs, NativeCardSelectionKind.DeckGeneric, new(7654321, 1234568));
        enchanted.Enchantment!.Amount++; Reject(snapshot.ValidateCurrent, "native-stale");
        snapshot = RunDecisionImporter.CardSelect(player, cards, prefs, NativeCardSelectionKind.DeckGeneric, new(7654321, 1234571));
        CardCmd.Upgrade(cards[1]); Reject(snapshot.ValidateCurrent, "native-stale");
        snapshot = RunDecisionImporter.CardSelect(player, cards, prefs, NativeCardSelectionKind.DeckGeneric, new(7654321, 1234569));
        Wait(CardPileCmd.RemoveFromDeck(cards[1])); Reject(snapshot.ValidateCurrent, "native-stale");
    }
    private static void SelectionBoundaries()
    {
        (NativeRun run, Player player) = Start();
        NativeDecisionTestSession session = new(); PendingNativeCardSelector selector = new(session);
        using IDisposable scope = CardSelectCmd.UseSelector(selector);
        CardSelectorPrefs prefs = new(CardSelectorPrefs.RemoveSelectionPrompt, 0, 2) { Cancelable = true, RequireManualConfirmation = true };
        Task<IEnumerable<CardModel>> effect = CardSelectCmd.FromDeckGeneric(player, prefs);
        Check(selector.CallCount == 1 && session.Request.MinSelect == 0, "min zero real pending");
        RunDecisionRequest request = session.Request;
        RunAction invalidMulti = Pick(request, 0, 1); invalidMulti.OptionIds[1] = uint.MaxValue;
        RejectSubmit(session, invalidMulti, "action-option", run, player);
        RunAction duplicate = Pick(request, 0, 1); duplicate.OptionIds[1] = duplicate.OptionIds[0];
        RejectSubmit(session, duplicate, "action-duplicate", run, player);
        RunAction empty = RunDecisionProtocol.Select(request, []); RunAction cancel = RunDecisionProtocol.Cancel(request);
        Check(empty.Kind != cancel.Kind && empty.Count == 0 && cancel.Count == 0, "empty Select distinct from Cancel");
        RejectSubmit(session, empty, "confirmation", run, player, confirmed: false);
        Wait(session.Submit(empty, confirmed: true)); Wait(effect); Check(!effect.GetAwaiter().GetResult().Any(), "empty native select result");
        effect = CardSelectCmd.FromDeckGeneric(player, prefs);
        Wait(session.Submit(RunDecisionProtocol.Cancel(session.Request))); Wait(effect);
        Check(!effect.GetAwaiter().GetResult().Any(), "cancel native UI result without confirmation");
        Task<IEnumerable<CardModel>> grid = CardSelectCmd.FromSimpleGrid(new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(), player.Deck.Cards, player, prefs);
        Check(session.Request.Kind == RunDecision.CardSelect && session.Request.Cancelable == 1, "simple grid actual callback complete prefs");
        CardModel gridFirst = selector.LastOptions![0];
        Wait(session.Submit(Pick(session.Request, 0), confirmed: true)); Wait(grid);
        Check(ReferenceEquals(grid.GetAwaiter().GetResult().Single(), gridFirst), "simple grid original result identity");
        int calls = selector.CallCount;
        CardSelectorPrefs automatic = new(CardSelectorPrefs.RemoveSelectionPrompt, player.Deck.Cards.Count + 2);
        Task<IEnumerable<CardModel>> all = CardSelectCmd.FromDeckGeneric(player, automatic); Wait(all);
        Check(all.GetAwaiter().GetResult().Count() == player.Deck.Cards.Count && selector.CallCount == calls && !session.HasPending, "auto all has no pending callback");
        Task<IEnumerable<CardModel>> none = CardSelectCmd.FromDeckGeneric(player, prefs, _ => false); Wait(none);
        Check(!none.GetAwaiter().GetResult().Any() && selector.CallCount == calls && !session.HasPending, "zero candidates no pending callback");
        Reject(() => RunDecisionImporter.CardSelect(player, player.Deck.Cards, automatic, NativeCardSelectionKind.DeckGeneric, new(1, 1)), "automatically");
        Reject(() => RunDecisionImporter.CardSelect(player, [], prefs, NativeCardSelectionKind.DeckGeneric, new(1, 2)), "automatically");
        CardSelectorPrefs impossible = new(CardSelectorPrefs.UpgradeSelectionPrompt, player.Deck.Cards.Count + 1) { RequireManualConfirmation = true };
        Reject(() => RunDecisionImporter.CardSelect(player, player.Deck.Cards, impossible, NativeCardSelectionKind.DeckUpgrade, new(1, 3)), "bounds");
        CardSelectorPrefs range = new(CardSelectorPrefs.UpgradeSelectionPrompt, 0, 2) { RequireManualConfirmation = true };
        Reject(() => RunDecisionImporter.CardSelect(player, player.Deck.Cards, range, NativeCardSelectionKind.DeckUpgrade, new(1, 4)), "exact selection");
        CardSelectorPrefs noCancel = new(CardSelectorPrefs.RemoveSelectionPrompt, 1);
        NativeDecisionSnapshot<CardModel> fixedChoice = RunDecisionImporter.CardSelect(player, player.Deck.Cards, noCancel, NativeCardSelectionKind.DeckGeneric, new(1, 5));
        Reject(() => RunDecisionProtocol.Cancel(fixedChoice.Request), "action-cancel");
    }
    private static void NegativeFixtures()
    {
        (NativeRun run, Player player, EventModel model) = Event<ThisOrThat>();
        string before = State(run, player, model);
        EventModel unknown = ModelDb.All.OfType<EventModel>().First(e => e.Id.Entry == "MOCK_EVENT_MODEL").ToMutable();
        Reject(() => RunDecisionImporter.Event(unknown, new(12, 34)), "decision-model");
        Reject(() => RunDecisionImporter.Event(ModelDb.Event<ThisOrThat>().ToMutable(), new(12, 35)), "has not begun");
        Check(before == State(run, player, model), "unknown and unstarted imports have no state or RNG effects");
        List<EventOption> options = (List<EventOption>)model.CurrentOptions; options.Clear();
        for (int i = 0; i <= RunLimits.DecisionOptions; i++) options.Add(new EventOption(model, () => Task.CompletedTask, "FIXTURE.CAPACITY"));
        string capacityBefore = State(run, player, model);
        bool capacity = false;
        try { RunDecisionImporter.Event(model, new(12, 36)); } catch (CapacityException) { capacity = true; }
        Check(capacity && capacityBefore == State(run, player, model), "capacity rejection without truncation or native effects"); options.Clear();
        options.Add(new EventOption(model, () => Task.FromException(new InvalidOperationException("native-dispatch-fault")), "FIXTURE.THROW"));
        NativeDecisionTestSession session = new(); session.Event(model); RunAction action = Pick(session.Request, 0);
        bool propagated = false;
        try { Wait(session.Submit(action)); } catch (InvalidOperationException e) when (e.Message == "native-dispatch-fault") { propagated = true; }
        Check(propagated && !session.HasPending, "original exception propagated and token consumed");
        RejectSubmit(session, action, "host-no-pending", run, player, model);
    }
    private sealed class DirectSelector(bool cancel = false) : MegaCrit.Sts2.Core.TestSupport.ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            CardModel[] cards = options.ToArray();
            IEnumerable<CardModel> result = cancel ? [] : minSelect == 2 ? [cards[1], cards[0]] : cards.Take(minSelect);
            return Task.FromResult(result);
        }
        public MegaCrit.Sts2.Core.TestSupport.CardRewardSelection GetSelectedCardReward(
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult> options,
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives)
            => throw new NotSupportedException("reward outside batch");
    }
}
