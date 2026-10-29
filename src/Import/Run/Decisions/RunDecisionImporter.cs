// Native semantics cross-checked against sts2 v0.111.0: NMapScreen.RecalculateTravelability,
// EventOption, CardSelectCmd and CardSelectorPrefs. Reads only; lifecycle belongs to the host.
using System.Reflection;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;

namespace BlitzSolver.Import;

/// <summary>The actual command that reached the pending callback, not a guess from its prompt.</summary>
public enum NativeCardSelectionKind { DeckGeneric, DeckUpgrade, SimpleGrid }

/// <summary>Read-only native choices. Host tokens must be fresh; importing does not reserve or consume them.</summary>
public static class RunDecisionImporter
{
    private static readonly FieldInfo EventOptions = typeof(EventModel).GetField("_currentOptions", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo EventVars = typeof(EventModel).GetField("_dynamicVars", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly PropertyInfo DisableChosen = typeof(EventOption).GetProperty("DisableOnChosen", BindingFlags.NonPublic | BindingFlags.Instance)!;

    public static NativeDecisionSnapshot<MapPoint> Map(NativeRun run, RunDecisionToken token, Func<bool> isTravelEnabled)
    {
        CheckRun(run);
        if (!isTravelEnabled() || !Hook.ShouldProceedToNextMapPoint(run))
            throw new UnsupportedException("decision-map: native map travel is not enabled");
        AbstractRoom? room = run.CurrentRoom;
        MapCoord[] visited = run.VisitedMapCoords.ToArray();
        RunDecisionRequest request = New(RunDecision.MapPoint, token);
        MapPoint[] points = MapPoints(run);
        foreach (MapPoint point in points)
        {
            if (!Enum.IsDefined(point.PointType)) throw new UnsupportedException("decision-map: unknown point type");
            if (point.coord.col is < 0 or > 255 || point.coord.row is < 0 or > 255)
                throw new UnsupportedException("decision-map: coordinate out of range");
            RunDecisionBuilder.AddOption(ref request, new RunDecisionOption
            {
                OptionId = Next(request), Kind = RunDecisionOptionKind.MapPoint,
                PayloadKind = RunDecisionPayloadKind.Map,
                Coord = new RunCoord((byte)point.coord.col, (byte)point.coord.row),
                PointType = (RunMapPointType)(int)point.PointType,
            });
        }
        return new(request, points, () => Map(run, token, isTravelEnabled),
            () => ReferenceEquals(room, run.CurrentRoom) && visited.SequenceEqual(run.VisitedMapCoords));
    }

    public static NativeDecisionSnapshot<EventOption> Event(EventModel model, RunDecisionToken token)
    {
        NativeAccess.RequireOriginal(model);
        RunRef source = Model(model);
        Player owner = model.Owner ?? throw new UnsupportedException("decision-event: event has not begun");
        CheckPlayer(owner);
        if (model.Description == null || EventOptions.GetValue(model) is not IReadOnlyList<EventOption> options
            || EventVars.GetValue(model) == null)
            throw new UnsupportedException("decision-event: no initialized pending page");
        AbstractRoom? room = owner.RunState.CurrentRoom;
        if (room is not EventRoom eventRoom || !ReferenceEquals(eventRoom.LocalMutableEvent, model))
            throw new UnsupportedException("decision-event: model is not the current local event");
        if (options.Count == 0) throw new UnsupportedException("decision-event: page has no pending options");
        RunDecisionRequest request = New(RunDecision.Event, token);
        request.Source = source;
        request.PageKey = RunDecisionBuilder.AddKey(ref request, model.Description.LocEntryKey);
        foreach (EventOption option in options)
        {
            RunDecisionOption row = new()
            {
                OptionId = Next(request), Kind = option.IsProceed ? RunDecisionOptionKind.Leave : RunDecisionOptionKind.EventOption,
                NativeKey = RunDecisionBuilder.AddKey(ref request, option.TextKey),
                Flags = option.IsLocked ? RunDecisionOptionFlags.Locked : RunDecisionOptionFlags.None,
            };
            if (option.WasChosen && (bool)DisableChosen.GetValue(option)!) row.Flags |= RunDecisionOptionFlags.Disabled;
            if (!option.IsProceed)
            {
                row.Model = option.Relic is { } relic ? Model(relic) : request.Source;
                row.PayloadKind = RunDecisionPayloadKind.Model;
                VisibleFacts(model, option, ref request, ref row);
            }
            RunDecisionBuilder.AddOption(ref request, row);
        }
        return new(request, options.ToArray(), () => Event(model, token),
            () => ReferenceEquals(room, owner.RunState.CurrentRoom)
                && (room is not EventRoom current || ReferenceEquals(current.LocalMutableEvent, model)));
    }

    public static NativeDecisionSnapshot<RestSiteOption> Rest(RestSiteRoom room, Player player, RunDecisionToken token)
    {
        CheckPlayer(player);
        if (!ReferenceEquals(player.RunState.CurrentRoom, room))
            throw new UnsupportedException("decision-rest: room is not current");
        RunDecisionRequest request = New(RunDecision.RestSite, token);
        RestSiteOption[] options = room.Options.ToArray();
        foreach (RestSiteOption option in options)
        {
            NativeAccess.RequireOriginalType(option.GetType());
            RunDecisionBuilder.AddOption(ref request, new RunDecisionOption
            {
                OptionId = Next(request), Kind = RunDecisionOptionKind.RestSiteOption,
                NativeKey = RunDecisionBuilder.AddKey(ref request, option.OptionId),
                Flags = option.IsEnabled ? RunDecisionOptionFlags.None : RunDecisionOptionFlags.Disabled,
            });
        }
        return new(request, options, () => Rest(room, player, token),
            () => ReferenceEquals(room, player.RunState.CurrentRoom));
    }

    /// <summary>Call only from an actual pending callback with its complete prefs and actual command kind.
    /// Automatic selections and zero-candidate early returns are not pending choices.</summary>
    public static NativeDecisionSnapshot<CardModel> CardSelect(Player player, IReadOnlyList<CardModel> options,
        CardSelectorPrefs prefs, NativeCardSelectionKind kind, RunDecisionToken token,
        RunSelectionPurpose purpose = RunSelectionPurpose.None)
    {
        CheckPlayer(player);
        if (options.Count > RunLimits.DecisionOptions) throw new CapacityException("Decision.Options", RunLimits.DecisionOptions);
        if (!Enum.IsDefined(kind)) throw new UnsupportedException("decision-card: unknown native command");
        if (options.Count == 0 || !prefs.RequireManualConfirmation && options.Count <= prefs.MinSelect)
            throw new UnsupportedException("decision-card: native command returns automatically, no pending choice");
        if (prefs.MinSelect < 0 || prefs.MaxSelect < prefs.MinSelect || prefs.MinSelect > options.Count)
            throw new UnsupportedException("decision-card: unsupported native selection bounds");
        if (kind == NativeCardSelectionKind.DeckUpgrade && prefs.MinSelect != prefs.MaxSelect)
            throw new UnsupportedException("decision-card: upgrade preview requires exact selection count");
        CardModel[]? deck = kind == NativeCardSelectionKind.SimpleGrid ? null : player.Deck.Cards.ToArray();
        foreach (CardModel card in options)
        {
            NativeAccess.RequireOriginal(card);
            if (!Registry.TryCard(card.Id.Entry, out _))
                throw new UnsupportedException("decision-card: unknown card " + card.Id.Entry);
            if (!ReferenceEquals(card.Owner, player) || deck != null && !deck.Any(c => ReferenceEquals(c, card))
                || kind == NativeCardSelectionKind.DeckUpgrade && !card.IsUpgradable)
                throw new UnsupportedException("decision-card: candidate is not eligible in current source");
        }
        RunDecisionRequest request = New(RunDecision.CardSelect, token);
        request.MinSelect = checked((ushort)prefs.MinSelect);
        request.MaxSelect = checked((ushort)Math.Min(prefs.MaxSelect, options.Count));
        request.Cancelable = prefs.Cancelable ? (byte)1 : (byte)0;
        request.RequireConfirmation = prefs.RequireManualConfirmation ? (byte)1 : (byte)0;
        request.SelectionPurpose = purpose;
        request.PageKey = RunDecisionBuilder.AddKey(ref request, prefs.Prompt.LocEntryKey);
        foreach (CardModel card in options)
            RunDecisionBuilder.AddOption(ref request, new RunDecisionOption
            {
                OptionId = Next(request), Kind = RunDecisionOptionKind.Card,
                PayloadKind = RunDecisionPayloadKind.Card, Card = Card(card),
            });
        AbstractRoom? room = player.RunState.CurrentRoom;
        return new(request, options.ToArray(), () => CardSelect(player, options, prefs, kind, token, purpose),
            () => ReferenceEquals(room, player.RunState.CurrentRoom)
                && (deck == null || SameInstances(deck, player.Deck.Cards)));
    }

    private static bool SameInstances(IReadOnlyList<CardModel> a, IReadOnlyList<CardModel> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }

    private static void VisibleFacts(EventModel model, EventOption option, ref RunDecisionRequest request, ref RunDecisionOption row)
    {
        if (model is ThisOrThat && option.TextKey == "THIS_OR_THAT.pages.INITIAL.options.PLAIN")
        {
            row.Costs = RunDecisionBuilder.AddFacts(ref request,
                [new() { Kind = RunDecisionFactKind.HpLoss, Amount = model.DynamicVars.HpLoss.IntValue }]);
            row.Effects = RunDecisionBuilder.AddFacts(ref request,
                [new() { Kind = RunDecisionFactKind.GoldGain, Amount = model.DynamicVars.Gold.IntValue }]);
            row.CostsKnowledge = row.EffectsKnowledge = RunDecisionKnowledge.Complete;
        }
        else if (model is FieldOfManSizedHoles && option.TextKey == "FIELD_OF_MAN_SIZED_HOLES.pages.INITIAL.options.RESIST")
        {
            row.Effects = RunDecisionBuilder.AddFacts(ref request,
                [new() { Kind = RunDecisionFactKind.RemoveCards, Amount = model.DynamicVars.Cards.IntValue },
                 new() { Kind = RunDecisionFactKind.AddCard, Amount = 1, Card = Card(ModelDb.Card<Normality>()) }]);
            row.CostsKnowledge = row.EffectsKnowledge = RunDecisionKnowledge.Complete;
        }
    }

    private static MapPoint[] MapPoints(NativeRun run)
    {
        if (run.VisitedMapCoords.Count == 0) return [run.Map.StartingMapPoint];
        MapCoord last = run.VisitedMapCoords[^1];
        MapPoint current = run.Map.GetPoint(last) ?? throw new UnsupportedException("decision-map: visited point missing");
        if (run.Map.SecondBossMapPoint != null && last.Equals(run.Map.BossMapPoint.coord)) return [run.Map.SecondBossMapPoint];
        if (last.row == run.Map.GetRowCount() - 1) return [run.Map.BossMapPoint];
        return MapTravel.GetTravelablePointsFrom(run, current).ToArray();
    }

    private static RunDecisionRequest New(RunDecision kind, RunDecisionToken token)
        => new() { Kind = kind, Token = token, MinSelect = 1, MaxSelect = 1 };
    private static uint Next(in RunDecisionRequest request) => (uint)request.OptionCount + 1;

    private static RunRef Model(AbstractModel model)
    {
        NativeAccess.RequireOriginal(model);
        if (model is EventModel && RunCatalog.TryEvent(model.Id.Entry, out ushort eventId)) return new(RunRefKind.Event, eventId);
        if (model is RelicModel && Registry.TryRelic(model.Id.Entry, out ushort relicId)) return new(RunRefKind.Relic, relicId);
        throw new UnsupportedException("decision-model: " + model.Id.Entry);
    }

    private static RunView.CardView Card(CardModel card)
    {
        NativeAccess.RequireOriginal(card);
        if (!Registry.TryCard(card.Id.Entry, out ushort id))
            throw new UnsupportedException("decision-card: unknown card " + card.Id.Entry);
        if (card.CurrentUpgradeLevel is < 0 or > byte.MaxValue)
            throw new CapacityException("Decision.CardUpgrade", byte.MaxValue);
        RunView.CardView view = new() { Id = id, Upgrade = (byte)card.CurrentUpgradeLevel };
        if (card.Enchantment is { } enchantment)
        {
            NativeAccess.RequireOriginal(enchantment);
            if (!Registry.TryEnchantment(enchantment.Id.Entry, out ushort enchantmentId))
                throw new UnsupportedException("decision-card: unknown enchantment " + enchantment.Id.Entry);
            if (enchantment.Amount is < short.MinValue or > short.MaxValue)
                throw new CapacityException("Decision.EnchantmentAmount", short.MaxValue);
            view.HasEnchantment = 1;
            view.EnchantmentId = enchantmentId;
            view.EnchantmentAmount = (short)enchantment.Amount;
        }
        return view;
    }

    private static void CheckPlayer(Player player)
    {
        if (player.RunState is not NativeRun run) throw new UnsupportedException("decision-run: concrete run required");
        CheckRun(run);
        if (!ReferenceEquals(player, run.Players[0])) throw new UnsupportedException("decision-player: not local player");
    }

    private static void CheckRun(NativeRun run)
    {
        if (!ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), run))
            throw new UnsupportedException("decision-run: source is not the active native run");
        if (run.Players.Count != 1 || !ReferenceEquals(LocalContext.GetMe(run), run.Players[0]))
            throw new UnsupportedException("decision-run: single local player required");
        if (run.GameMode != GameMode.Standard || run.Modifiers.Count != 0 || run.IsGameOver)
            throw new UnsupportedException("decision-run: unsupported run mode or finished run");
        NativeAccess.RequireOriginal(run.Players[0].Character);
        if (!RunCatalog.TryCharacter(run.Players[0].Character.Id.Entry, out _))
            throw new UnsupportedException("decision-character: unknown character");
    }
}
