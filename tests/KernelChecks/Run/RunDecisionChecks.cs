using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;

namespace BlitzSolver.KernelChecks;

internal static class RunDecisionChecks
{
    private delegate void Change(ref RunDecisionRequest request);
    private delegate void ChangeAction(ref RunAction action);

    internal static void Check()
    {
        Console.WriteLine($"RUN_DECISION_LAYOUT request={Unsafe.SizeOf<RunDecisionRequest>()} option={Unsafe.SizeOf<RunDecisionOption>()} fact={Unsafe.SizeOf<RunDecisionFact>()} action={Unsafe.SizeOf<RunAction>()} view={Unsafe.SizeOf<RunView>()}");
        Require(Unsafe.SizeOf<RunDecisionToken>() == 16 && Unsafe.SizeOf<RunDecisionSlice>() == 4, "token/slice sizes");
        Require(Unsafe.SizeOf<RunDecisionRequest>() == 15960, "request size");
        Require(Unsafe.SizeOf<RunDecisionOption>() == 44 && Unsafe.SizeOf<RunDecisionFact>() == 16, "payload sizes");
        Require(Unsafe.SizeOf<RunAction>() == 536 && Unsafe.SizeOf<RunView>() < 24 * 1024, "action/view budgets");
        Require(!RuntimeHelpers.IsReferenceOrContainsReferences<RunDecisionRequest>()
            && !RuntimeHelpers.IsReferenceOrContainsReferences<RunAction>()
            && !RuntimeHelpers.IsReferenceOrContainsReferences<RunView>(), "unmanaged protocol");
        CheckSelection();
        CheckMatrix();
        CheckPayloads();
        CheckInvalidRequests();
        CheckInvalidActions();
        CheckCapacityAndAtomicity();
        Console.WriteLine("RUN_DECISION_OK token/cardinality/kinds/payloads/visibility/atomicity");
    }

    private static void CheckSelection()
    {
        RunDecisionRequest request = Fixture();
        RunDecisionProtocol.ValidateAction(request, RunDecisionProtocol.Select(request, [12]));
        // Same model, distinct instances and upgrade/enchantment facts remain distinct.
        request.Options[1].Card.Upgrade = 1;
        request.Options[1].Card.HasEnchantment = 1;
        request.Options[1].Card.EnchantmentId = 0;
        request.Options[1].Card.EnchantmentAmount = 2;
        RunAction original = RunDecisionProtocol.Select(request, [12]);
        (request.Options[0], request.Options[1]) = (request.Options[1], request.Options[0]);
        RunDecisionProtocol.ValidateAction(request, original);
        Require(RunDecisionProtocol.ResolveOptionIndex(request, 12) == 0
            && request.Options[0].Card.Upgrade == 1, "stable option id after reorder");
        request.MinSelect = 0;
        RunDecisionProtocol.ValidateAction(request, RunDecisionProtocol.Select(request, []));
        Expect<InvalidDataException>("action-cancel", () => RunDecisionProtocol.Cancel(request));
        request.Cancelable = 1;
        RunDecisionProtocol.ValidateAction(request, RunDecisionProtocol.Cancel(request));
        request.RequireConfirmation = 1;
        request.MinSelect = request.MaxSelect = 2;
        RunAction both = RunDecisionProtocol.Select(request, [12, 11]);
        Require(both.Count == 2 && both.OptionIds[0] == 12 && both.OptionIds[1] == 11, "selection order");
        request.MinSelect = 1;
        RunDecisionProtocol.Select(request, [11]);
        RunDecisionProtocol.Select(request, [11, 12]);
        request = Fixture();
        request.OptionCount = request.MinSelect = request.MaxSelect = RunLimits.DecisionOptions;
        uint[] ids = new uint[RunLimits.DecisionOptions];
        for (int index = 0; index < ids.Length; index++)
        {
            ids[index] = (uint)(index + 1);
            request.Options[index] = Row(RunDecisionOptionKind.Card, ids[index]);
        }
        RunDecisionProtocol.ValidateAction(request, RunDecisionProtocol.Select(request, ids));
        request.MinSelect = 0;
        request.MaxSelect = 0;
        request.OptionCount = 0;
        RunDecisionProtocol.Select(request, []);
        RunView view = RunView.From(default);
        Require(view.Request.Token == default, "From has no pending request");
        Expect<InvalidDataException>("request-token", () => RunDecisionProtocol.ValidateRequest(view.Request));
    }

    private static void CheckMatrix()
    {
        RunDecisionOptionKind[][] allowed =
        [
            [],
            [RunDecisionOptionKind.MapPoint],
            [RunDecisionOptionKind.EventOption, RunDecisionOptionKind.Leave],
            [RunDecisionOptionKind.Reward, RunDecisionOptionKind.Leave],
            [RunDecisionOptionKind.Card, RunDecisionOptionKind.CardBundle, RunDecisionOptionKind.Skip, RunDecisionOptionKind.Reroll],
            [RunDecisionOptionKind.ShopEntry, RunDecisionOptionKind.CardRemoval, RunDecisionOptionKind.Leave],
            [RunDecisionOptionKind.RestSiteOption, RunDecisionOptionKind.Leave],
            [RunDecisionOptionKind.Card],
            [RunDecisionOptionKind.Relic, RunDecisionOptionKind.BossRelic, RunDecisionOptionKind.Skip],
            [RunDecisionOptionKind.Potion, RunDecisionOptionKind.Skip],
            [], // GameOver remains a terminal signal, never a request.
            [RunDecisionOptionKind.Open, RunDecisionOptionKind.Skip, RunDecisionOptionKind.Leave],
        ];
        for (int kind = 1; kind <= 11; kind++)
        {
            if (kind == 10) continue;
            for (int optionKind = 1; optionKind <= 15; optionKind++)
            {
                RunDecisionRequest request = Fixture((RunDecision)kind);
                request.OptionCount = 1;
                request.Options[0] = Row((RunDecisionOptionKind)optionKind, 11);
                if (optionKind == (int)RunDecisionOptionKind.CardBundle)
                    request.Options[0].Bundle = RunDecisionBuilder.AddCards(ref request, [default(RunView.CardView)]);
                if (allowed[kind].Contains((RunDecisionOptionKind)optionKind))
                    RunDecisionProtocol.Select(request, [11]);
                else
                    Expect<InvalidDataException>("kind-payload", () => RunDecisionProtocol.ValidateRequest(request));
            }
        }
        RunDecisionRequest bundle = Fixture(RunDecision.CardReward);
        bundle.Options[0] = Row(RunDecisionOptionKind.CardBundle, 11);
        bundle.Options[1] = Row(RunDecisionOptionKind.CardBundle, 12);
        // ScrollBoxes may offer three copies of Claw in one bundle: model repetition is legal.
        bundle.Options[0].Bundle = RunDecisionBuilder.AddCards(ref bundle,
            [default(RunView.CardView), default(RunView.CardView), default(RunView.CardView)]);
        bundle.Options[1].Bundle = RunDecisionBuilder.AddCards(ref bundle,
            [new RunView.CardView { Upgrade = 1 }, default(RunView.CardView)]);
        Require(RunDecisionProtocol.Select(bundle, [12]).Count == 1, "bundle is one choice");
        bundle.Options[1] = Row(RunDecisionOptionKind.Card, 12);
        Expect<InvalidDataException>("reward-entity", () => RunDecisionProtocol.ValidateRequest(bundle));
    }

    private static void CheckPayloads()
    {
        RunDecisionRequest request = Fixture(RunDecision.Event);
        Require(RunCatalog.TryEvent("THIS_OR_THAT", out ushort thisOrThat), "ThisOrThat catalog");
        request.Source = new RunRef(RunRefKind.Event, thisOrThat);
        request.PageKey = RunDecisionBuilder.AddKey(ref request, "THIS_OR_THAT.pages.INITIAL");
        request.Options[0].NativeKey = RunDecisionBuilder.AddKey(ref request, "THIS_OR_THAT.pages.INITIAL.options.PLAIN");
        request.Options[0].Costs = RunDecisionBuilder.AddFacts(ref request,
            [new() { Kind = RunDecisionFactKind.HpLoss, Amount = 6 }]);
        request.Options[0].Effects = RunDecisionBuilder.AddFacts(ref request,
            [new() { Kind = RunDecisionFactKind.GoldGain, Amount = 51 }]);
        request.Options[0].CostsKnowledge = request.Options[0].EffectsKnowledge = RunDecisionKnowledge.Complete;
        RunDecisionProtocol.ValidateRequest(request);
        // Constructor fixture derived from the named native methods, not a captured natural run.
        Require(Registry.TryCard("NORMALITY", out ushort normality), "Normality catalog");
        Require(RunCatalog.TryEvent("FIELD_OF_MAN_SIZED_HOLES", out ushort field), "Field catalog");
        request = Fixture(RunDecision.Event);
        request.Source = new RunRef(RunRefKind.Event, field);
        request.Options[1].NativeKey = RunDecisionBuilder.AddKey(ref request, "FIELD_OF_MAN_SIZED_HOLES.pages.INITIAL.options.RESIST");
        request.Options[1].Effects = RunDecisionBuilder.AddFacts(ref request,
            [new() { Kind = RunDecisionFactKind.RemoveCards, Amount = 2 },
             new() { Kind = RunDecisionFactKind.AddCard, Amount = 1, Card = new() { Id = normality } }]);
        request.Options[1].EffectsKnowledge = RunDecisionKnowledge.Complete;
        request.Options[1].CostsKnowledge = RunDecisionKnowledge.Complete;
        // Synthetic multi-cost row: these two amounts do not claim a natural Field event option.
        request.Options[0].CostsKnowledge = RunDecisionKnowledge.Complete;
        request.Options[0].Costs = RunDecisionBuilder.AddFacts(ref request,
            [new() { Kind = RunDecisionFactKind.GoldLoss, Amount = 17 },
             new() { Kind = RunDecisionFactKind.HpLoss, Amount = 3 }]);
        RunDecisionProtocol.ValidateRequest(request);
        Require(request.Options[1].Costs.Count == 0 && request.Options[1].CostsKnowledge == RunDecisionKnowledge.Complete,
            "known empty costs");
        request.Options[1].CostsKnowledge = RunDecisionKnowledge.Unknown;
        RunDecisionProtocol.ValidateRequest(request);
        request.Options[1].EffectsKnowledge = RunDecisionKnowledge.Partial;
        RunDecisionProtocol.ValidateRequest(request);
        request.Options[1].PayloadKind = RunDecisionPayloadKind.Model;
        request.Options[1].Model = new RunRef(RunRefKind.Relic, 0); // Neow EventOption.Relic.
        RunDecisionProtocol.ValidateRequest(request);
        request = Fixture();
        foreach (RunSelectionPurpose purpose in Enum.GetValues<RunSelectionPurpose>())
        {
            request.SelectionPurpose = purpose;
            RunDecisionProtocol.ValidateRequest(request);
        }
        request = Fixture(RunDecision.Shop);
        request.Options[0] = Row(RunDecisionOptionKind.ShopEntry, 11);
        request.Options[0].PayloadKind = RunDecisionPayloadKind.Model;
        request.Options[0].Model = new RunRef(RunRefKind.Potion, 0);
        RunDecisionProtocol.ValidateRequest(request);
        request.Options[0].Model = new RunRef(RunRefKind.Relic, 0);
        RunDecisionProtocol.ValidateRequest(request);
    }

    private static void CheckInvalidRequests()
    {
        Bad("request-token", (ref RunDecisionRequest r) => r.Token = new(0, 3));
        Bad("request-token", (ref RunDecisionRequest r) => r.Token = new(7, 0));
        foreach (RunDecision kind in new[] { RunDecision.None, RunDecision.GameOver, (RunDecision)255 })
        {
            RunDecisionRequest request = Fixture(); request.Kind = kind;
            Expect<UnsupportedException>("", () => RunDecisionProtocol.ValidateRequest(request));
        }
        Bad("request-flags", (ref RunDecisionRequest r) => r.Cancelable = 2);
        Bad("request-flags", (ref RunDecisionRequest r) => r.RequireConfirmation = 255);
        Bad("selection-purpose", (ref RunDecisionRequest r) => r.SelectionPurpose = (RunSelectionPurpose)255);
        RunDecisionRequest wrongPurpose = Fixture(RunDecision.Event); wrongPurpose.SelectionPurpose = RunSelectionPurpose.Remove;
        Expect<InvalidDataException>("selection-purpose", () => RunDecisionProtocol.ValidateRequest(wrongPurpose));
        Bad("selection-bounds", (ref RunDecisionRequest r) => { r.MinSelect = 2; r.MaxSelect = 1; });
        Bad("selection-bounds", (ref RunDecisionRequest r) => r.MaxSelect = 3);
        Bad("enabled-count", (ref RunDecisionRequest r) => { r.Options[0].Flags = RunDecisionOptionFlags.Locked; r.Options[1].Flags = RunDecisionOptionFlags.Disabled; });
        Bad("option-id", (ref RunDecisionRequest r) => r.Options[0].OptionId = 0);
        Bad("option-id", (ref RunDecisionRequest r) => r.Options[1].OptionId = 11);
        Bad("option-flags", (ref RunDecisionRequest r) => r.Options[0].Flags = (RunDecisionOptionFlags)4);
        Bad("option-kind", (ref RunDecisionRequest r) => r.Options[0].Kind = (RunDecisionOptionKind)255);
        Bad("payload-kind", (ref RunDecisionRequest r) => r.Options[0].PayloadKind = (RunDecisionPayloadKind)255);
        Bad("payload-unused", (ref RunDecisionRequest r) => r.Options[0].Model = new(RunRefKind.Relic, 0));
        Bad("payload-unused", (ref RunDecisionRequest r) => r.Options[0].RewardType = RunRewardType.Gold);
        Bad("payload-unused", (ref RunDecisionRequest r) => r.Options[0].Coord = new(2, 3));
        Bad("payload-slice", (ref RunDecisionRequest r) => r.Options[0].Costs = new(ushort.MaxValue, ushort.MaxValue));
        Bad("payload-slice", (ref RunDecisionRequest r) => r.Options[0].Effects = new(0, 1));
        Bad("payload-slice", (ref RunDecisionRequest r) => r.Options[0].Bundle = new(1, 0));
        Bad("payload-slice", (ref RunDecisionRequest r) => r.Options[0].NativeKey = new(0, 1));
        Bad("knowledge", (ref RunDecisionRequest r) => r.Options[0].CostsKnowledge = RunDecisionKnowledge.Partial);
        Bad("knowledge", (ref RunDecisionRequest r) => r.Options[0].EffectsKnowledge = (RunDecisionKnowledge)255);
        Bad("knowledge", (ref RunDecisionRequest r) => { r.FactCount = 1; r.Facts[0] = new() { Kind = RunDecisionFactKind.GoldLoss, Amount = 1 }; r.Options[0].Costs = new(0, 1); });
        Bad("fact-amount", (ref RunDecisionRequest r) => { r.FactCount = 1; r.Facts[0] = new() { Kind = RunDecisionFactKind.GoldLoss, Amount = -1 }; });
        Bad("fact-amount", (ref RunDecisionRequest r) => { r.FactCount = 1; r.Facts[0] = new() { Kind = RunDecisionFactKind.GoldGain }; });
        Bad("fact-kind", (ref RunDecisionRequest r) => { r.FactCount = 1; r.Facts[0].Kind = (RunDecisionFactKind)255; });
        Bad("fact-card", (ref RunDecisionRequest r) => { r.FactCount = 1; r.Facts[0] = new() { Kind = RunDecisionFactKind.GoldGain, Amount = 1, Card = new() { Upgrade = 1 } }; });
        Bad("cost-kind", (ref RunDecisionRequest r) => { r.FactCount = 1; r.Facts[0] = new() { Kind = RunDecisionFactKind.GoldGain, Amount = 1 }; r.Options[0].Costs = new(0, 1); r.Options[0].CostsKnowledge = RunDecisionKnowledge.Complete; });
        Bad("effect-kind", (ref RunDecisionRequest r) => { r.FactCount = 1; r.Facts[0] = new() { Kind = RunDecisionFactKind.HpLoss, Amount = 1 }; r.Options[0].Effects = new(0, 1); r.Options[0].EffectsKnowledge = RunDecisionKnowledge.Complete; });
        Bad("card-flags", (ref RunDecisionRequest r) => r.Options[0].Card.HasEnchantment = 2);
        Bad("card-enchantment", (ref RunDecisionRequest r) => r.Options[0].Card.EnchantmentAmount = 1);
        Bad("source-kind", (ref RunDecisionRequest r) => r.Source = new(RunRefKind.Card, 0));
        Bad("model-ref", (ref RunDecisionRequest r) => r.Source = new(RunRefKind.None, 2));
        Bad("key-utf8", (ref RunDecisionRequest r) => { r.KeyByteCount = 1; r.Keys[0] = 255; });
        Bad("key-utf8", (ref RunDecisionRequest r) => { r.Options[0].NativeKey = RunDecisionBuilder.AddKey(ref r, "牌"); r.Options[0].NativeKey = new(1, 1); });
        BadUnsupported((ref RunDecisionRequest r) => r.Options[0].Card.Id = ushort.MaxValue);
        BadUnsupported((ref RunDecisionRequest r) => { r.Options[0].Card.HasEnchantment = 1; r.Options[0].Card.EnchantmentId = ushort.MaxValue; });
        BadUnsupported((ref RunDecisionRequest r) => r.Source = new(RunRefKind.Event, ushort.MaxValue));
        RunDecisionRequest shape = Fixture(RunDecision.MapPoint); shape.Options[0].PointType = (RunMapPointType)255;
        Expect<InvalidDataException>("map-point-type", () => RunDecisionProtocol.ValidateRequest(shape));
        shape = Fixture(RunDecision.Event); shape.MinSelect = 0;
        Expect<InvalidDataException>("selection-bounds", () => RunDecisionProtocol.ValidateRequest(shape));
        shape = Fixture(RunDecision.Event); shape.Options[0].PayloadKind = RunDecisionPayloadKind.Model;
        Expect<InvalidDataException>("payload-model", () => RunDecisionProtocol.ValidateRequest(shape));
        shape.Options[0].Model = new((RunRefKind)255, 0);
        Expect<InvalidDataException>("model-ref", () => RunDecisionProtocol.ValidateRequest(shape));
        shape = Fixture(RunDecision.CardReward); shape.Options[0] = Row(RunDecisionOptionKind.CardBundle, 11);
        Expect<InvalidDataException>("bundle-empty", () => RunDecisionProtocol.ValidateRequest(shape));
        shape.Options[0].Bundle = new(0, 1);
        Expect<InvalidDataException>("payload-slice", () => RunDecisionProtocol.ValidateRequest(shape));
        shape = Fixture(RunDecision.CombatReward); shape.Options[0].RewardType = (RunRewardType)255;
        Expect<InvalidDataException>("reward-type", () => RunDecisionProtocol.ValidateRequest(shape));
        shape = Fixture(RunDecision.CombatReward); shape.Options[0].Model = new(RunRefKind.Potion, 0);
        Expect<InvalidDataException>("reward-model", () => RunDecisionProtocol.ValidateRequest(shape));
    }

    private static void CheckInvalidActions()
    {
        BadAction("action-token", (ref RunAction a) => a.Token = new(72, 2));
        BadAction("action-token", (ref RunAction a) => a.Token = new(71, 1));
        BadAction("action-token", (ref RunAction a) => a.Token = default);
        BadAction("action-kind", (ref RunAction a) => a.Kind = (RunActionKind)255);
        BadAction("action-count", (ref RunAction a) => a.Count = 0);
        BadAction("action-count", (ref RunAction a) => a.Count = 2);
        BadAction("action-option", (ref RunAction a) => a.OptionIds[0] = 99);
        BadAction("action-cancel", (ref RunAction a) => a.Kind = RunActionKind.Cancel);
        RunDecisionRequest request = Fixture(); request.MaxSelect = 2;
        Expect<InvalidDataException>("action-duplicate", () => RunDecisionProtocol.Select(request, [11, 11]));
        request.Options[0].Flags = RunDecisionOptionFlags.Locked;
        Expect<InvalidDataException>("action-disabled", () => RunDecisionProtocol.Select(request, [11]));
        request.Options[0].Flags = RunDecisionOptionFlags.Disabled;
        Expect<InvalidDataException>("action-disabled", () => RunDecisionProtocol.Select(request, [11]));
        request.Cancelable = 1;
        RunAction cancel = RunDecisionProtocol.Cancel(request); cancel.Count = 1;
        Expect<InvalidDataException>("action-cancel", () => RunDecisionProtocol.ValidateAction(request, cancel));
        request.OptionCount = ushort.MaxValue;
        Expect<CapacityException>("", () => RunDecisionProtocol.ValidateAction(request, default));
    }

    private static void CheckCapacityAndAtomicity()
    {
        foreach (Change change in new Change[]
        {
            (ref RunDecisionRequest r) => r.OptionCount = RunLimits.DecisionOptions + 1,
            (ref RunDecisionRequest r) => r.FactCount = RunDecisionLimits.Facts + 1,
            (ref RunDecisionRequest r) => r.CardCount = RunDecisionLimits.Cards + 1,
            (ref RunDecisionRequest r) => r.KeyByteCount = RunDecisionLimits.KeyBytes + 1,
        })
        {
            RunDecisionRequest request = Fixture(); change(ref request);
            Expect<CapacityException>("", () => RunDecisionProtocol.ValidateRequest(request));
        }
        RunDecisionRequest full = Fixture();
        RunDecisionSlice allFacts = RunDecisionBuilder.AddFacts(ref full,
            new RunDecisionFact[RunDecisionLimits.Facts]);
        RunDecisionSlice allCards = RunDecisionBuilder.AddCards(ref full,
            new RunView.CardView[RunDecisionLimits.Cards]);
        Require(allFacts.Count == RunDecisionLimits.Facts && allCards.Count == RunDecisionLimits.Cards,
            "exact shared pool capacities");
        full = Fixture();
        full.OptionCount = RunLimits.DecisionOptions;
        AtomicFailure(() => RunDecisionBuilder.AddOption(ref full, default), () => Bytes(full));
        full = Fixture(); full.FactCount = RunDecisionLimits.Facts;
        AtomicFailure(() => RunDecisionBuilder.AddFacts(ref full, [default(RunDecisionFact)]), () => Bytes(full));
        full = Fixture(); full.CardCount = RunDecisionLimits.Cards;
        AtomicFailure(() => RunDecisionBuilder.AddCards(ref full, [default(RunView.CardView)]), () => Bytes(full));
        full = Fixture();
        RunDecisionSlice key = RunDecisionBuilder.AddKey(ref full, new string('x', RunDecisionLimits.KeyBytes));
        Require(key.Count == RunDecisionLimits.KeyBytes, "exact key capacity");
        AtomicFailure(() => RunDecisionBuilder.AddKey(ref full, "字"), () => Bytes(full));
        // Pool source aliases the destination and overlaps one element. CopyTo must act like memmove.
        full = Fixture();
        full.FactCount = 1;
        full.Facts[0] = new() { Kind = RunDecisionFactKind.GoldLoss, Amount = 17 };
        full.Facts[1] = new() { Kind = RunDecisionFactKind.HpLoss, Amount = 3 };
        ReadOnlySpan<RunDecisionFact> factAlias = full.Facts;
        RunDecisionSlice factSlice = RunDecisionBuilder.AddFacts(ref full, factAlias.Slice(0, 2));
        Require(factSlice == new RunDecisionSlice(1, 2) && full.Facts[1].Amount == 17
            && full.Facts[2].Amount == 3, "overlapping fact pool input");
        full.CardCount = 1;
        full.Cards[0] = new() { Upgrade = 1 };
        full.Cards[1] = new() { Upgrade = 2 };
        ReadOnlySpan<RunView.CardView> cardAlias = full.Cards;
        RunDecisionSlice cardSlice = RunDecisionBuilder.AddCards(ref full, cardAlias.Slice(0, 2));
        Require(cardSlice == new RunDecisionSlice(1, 2) && full.Cards[1].Upgrade == 1
            && full.Cards[2].Upgrade == 2, "overlapping card pool input");
        full = Fixture();
        byte[] before = Bytes(full);
        Expect<InvalidDataException>("key-utf8", () => RunDecisionBuilder.AddKey(ref full, "\uD800"));
        Require(before.AsSpan().SequenceEqual(Bytes(full)), "invalid key leaves request unchanged");
        full = Fixture();
        RunView view = RunView.From(default);
        RunView attached = view.WithRequest(full);
        Require(attached.Decision == full.Kind && attached.DecisionOptionCount == full.OptionCount
            && attached.Request.Token == full.Token && view.Request.Token == default, "atomic view attachment");
        full.Options[1].OptionId = 11;
        byte[] viewBefore = Bytes(view);
        Expect<InvalidDataException>("option-id", () => view.WithRequest(full));
        Require(viewBefore.AsSpan().SequenceEqual(Bytes(view)), "rejected attachment leaves view unchanged");
        full = Fixture(); RunAction action = RunDecisionProtocol.Select(full, [11]);
        byte[] requestBefore = Bytes(full), actionBefore = Bytes(action);
        RunDecisionProtocol.ValidateAction(full, action);
        Require(requestBefore.AsSpan().SequenceEqual(Bytes(full)) && actionBefore.AsSpan().SequenceEqual(Bytes(action)), "validation is read only");
        action.Count = RunLimits.DecisionOptions + 1;
        Expect<CapacityException>("", () => RunDecisionProtocol.ValidateAction(full, action));
        Expect<CapacityException>("", () => RunDecisionProtocol.Select(full, new uint[RunLimits.DecisionOptions + 1]));
    }

    private static void AtomicFailure(Action attempt, Func<byte[]> snapshot)
    {
        byte[] before = snapshot();
        Expect<CapacityException>("", attempt);
        Require(before.AsSpan().SequenceEqual(snapshot()), "capacity failure leaves request unchanged");
    }

    private static void Bad(string reason, Change change)
    {
        RunDecisionRequest request = Fixture(); change(ref request);
        byte[] before = Bytes(request);
        Expect<InvalidDataException>(reason, () => RunDecisionProtocol.ValidateRequest(request));
        Require(before.AsSpan().SequenceEqual(Bytes(request)), "rejected request is read only");
    }

    private static void BadUnsupported(Change change)
    {
        RunDecisionRequest request = Fixture(); change(ref request);
        Expect<UnsupportedException>("", () => RunDecisionProtocol.ValidateRequest(request));
    }

    private static void BadAction(string reason, ChangeAction change)
    {
        RunDecisionRequest request = Fixture(); RunAction action = RunDecisionProtocol.Select(request, [11]); change(ref action);
        byte[] before = Bytes(action);
        Expect<InvalidDataException>(reason, () => RunDecisionProtocol.ValidateAction(request, action));
        Require(before.AsSpan().SequenceEqual(Bytes(action)), "rejected action is read only");
    }

    private static RunDecisionRequest Fixture(RunDecision kind = RunDecision.CardSelect)
    {
        RunDecisionRequest request = default;
        request.Token = new(71, 2); request.Kind = kind; request.MinSelect = request.MaxSelect = 1;
        RunDecisionOptionKind row = kind switch
        {
            RunDecision.MapPoint => RunDecisionOptionKind.MapPoint,
            RunDecision.Event => RunDecisionOptionKind.EventOption,
            RunDecision.CombatReward => RunDecisionOptionKind.Reward,
            RunDecision.Shop => RunDecisionOptionKind.ShopEntry,
            RunDecision.RestSite => RunDecisionOptionKind.RestSiteOption,
            RunDecision.RelicSelect => RunDecisionOptionKind.Relic,
            RunDecision.PotionDiscard => RunDecisionOptionKind.Potion,
            RunDecision.Treasure => RunDecisionOptionKind.Open,
            _ => RunDecisionOptionKind.Card,
        };
        RunDecisionBuilder.AddOption(ref request, Row(row, 11));
        RunDecisionBuilder.AddOption(ref request, Row(row, 12));
        return request;
    }

    private static RunDecisionOption Row(RunDecisionOptionKind kind, uint id)
    {
        RunDecisionOption row = default; row.Kind = kind; row.OptionId = id;
        switch (kind)
        {
            case RunDecisionOptionKind.MapPoint: row.PayloadKind = RunDecisionPayloadKind.Map; row.PointType = RunMapPointType.Monster; row.Coord = new(2, 3); break;
            case RunDecisionOptionKind.Card or RunDecisionOptionKind.ShopEntry: row.PayloadKind = RunDecisionPayloadKind.Card; break;
            case RunDecisionOptionKind.CardBundle: row.PayloadKind = RunDecisionPayloadKind.CardBundle; break;
            case RunDecisionOptionKind.Potion: row.PayloadKind = RunDecisionPayloadKind.Model; row.Model = new(RunRefKind.Potion, 0); break;
            case RunDecisionOptionKind.Relic or RunDecisionOptionKind.BossRelic: row.PayloadKind = RunDecisionPayloadKind.Model; row.Model = new(RunRefKind.Relic, 0); break;
            case RunDecisionOptionKind.Reward: row.PayloadKind = RunDecisionPayloadKind.Reward; row.RewardType = RunRewardType.Gold; break;
        }
        return row;
    }

    private static byte[] Bytes<T>(T value) where T : unmanaged
        => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)).ToArray();

    private static void Expect<T>(string prefix, Action action) where T : Exception
    {
        try { action(); }
        catch (InvalidDataException error) when (typeof(T) == typeof(InvalidDataException))
        { Require(error.Message.StartsWith(prefix + ":", StringComparison.Ordinal), "wrong rejection: " + error.Message); return; }
        catch (CapacityException) when (typeof(T) == typeof(CapacityException)) { return; }
        catch (UnsupportedException) when (typeof(T) == typeof(UnsupportedException)) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + " " + prefix);
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("RunDecision contract: " + message); }
}
