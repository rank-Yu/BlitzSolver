// Directed fixtures using original RewardsSet.Offer and MerchantRoom.Enter. No natural-run claim.
// Native semantics: sts2 v0.111.0 CardReward.OnSelect, NRewardButton.GetReward,
// NMerchantCardRemoval.OnCardRemovalUsed, MerchantPotionEntry.CalcCost.
using System.Reflection;
using System.Reflection.Emit;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;

namespace Differential;

internal static class RunRewardShopCompare
{
    private static MainLoopContext _loop = null!;
    private static int _checks, _cases;
    public static int Run(string[] args)
    {
        if (args.Length != 0) throw new InvalidDataException("--run-reward-shop-compare accepts no arguments");
        _loop = new(); SynchronizationContext.SetSynchronizationContext(_loop);
        GameBootstrap.ApplyGodotBypasses(); GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState(); NativeDecisionTestHooks.Install();
        GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Assets.PreloadManager), "LoadRoomMerchantAssets"),
            prefix: new HarmonyMethod(typeof(RunRewardShopCompare), nameof(Completed)));
        GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(MerchantPotionEntry), nameof(MerchantPotionEntry.CalcCost)),
            prefix: new HarmonyMethod(typeof(RunRewardShopCompare), nameof(PriceBegin)),
            finalizer: new HarmonyMethod(typeof(RunRewardShopCompare), nameof(PriceEnd)));
        // This mode restores native Generate; only Godot hotkey string reads in the constructor
        // are bypassed. Existing probe modes retain GameBootstrap's old alternatives guard.
        GameBootstrap.Harmony.Unpatch(AccessTools.Method(typeof(CardRewardAlternative), nameof(CardRewardAlternative.Generate)),
            AccessTools.Method(typeof(GameBootstrap), "NoCardRewardAlternativesPrefix"));
        GameBootstrap.Harmony.Patch(AccessTools.Constructor(typeof(CardRewardAlternative),
            [typeof(string), typeof(Func<Task>), typeof(PostAlternateCardRewardAction)]),
            transpiler: new HarmonyMethod(typeof(RunRewardShopCompare), nameof(AlternativeHotkeys)));
        Case("rewards-visible-native-roundtrip", RewardsRoundtrip);
        Case("rewards-forced-pending-and-terminal", RewardBoundaries);
        Case("card-reroll-skip-and-fresh-callback", RerollRoundtrip);
        Case("card-unknown-capacity-and-parent-identity", CardNegatives);
        Case("potion-too-full-discard-return-and-retry", PotionRoundtrip);
        Case("potion-not-allowed-native-consumes", PotionNotAllowed);
        Case("shop-visible-disabled-and-stale", ShopBoundaries);
        Case("shop-card-relic-potion-native-roundtrip", ShopRoundtrip);
        Case("shop-discount-restock-model-identity", ShopRestock);
        Case("shop-removal-success-cancel-and-stale-child", ShopRemoval);
        Case("price-scope-exception-restoration", PriceRestoration);
        Console.WriteLine($"REWARDSHOP status=pass cases={_cases} checks={_checks}"); return 0;
    }
    private static IEnumerable<CodeInstruction> AlternativeHotkeys(IEnumerable<CodeInstruction> instructions)
    {
        int fields = 0, conversions = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field
                && field.DeclaringType == typeof(MegaCrit.Sts2.Core.ControllerInput.MegaInput))
            {
                fields++; instruction.opcode = OpCodes.Ldstr; instruction.operand = "";
            }
            else if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Godot.StringName)
                && method.Name == "op_Implicit" && method.ReturnType == typeof(string))
            {
                conversions++; instruction.opcode = OpCodes.Nop; instruction.operand = null;
            }
            yield return instruction;
        }
        if (fields != 3 || conversions != 3) throw new InvalidDataException($"native alternative hotkey IL changed: {fields}/{conversions}");
    }
    private static bool Completed(ref Task __result) { __result = Task.CompletedTask; return false; }
    private static void PriceBegin(out bool __state) { __state = TestMode.IsOn; TestMode.IsOn = false; }
    private static void PriceEnd(bool __state) => TestMode.IsOn = __state;
    private static void Case(string name, Action test)
    {
        Console.WriteLine($"REWARDSHOP kind=fixture case={name} status=start");
        try { test(); _cases++; Console.WriteLine($"REWARDSHOP kind=fixture case={name} status=pass checks={_checks}"); }
        finally { RewardsSet.testSelector = null; if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false); }
    }
    private static void Check(bool value, string reason) { _checks++; if (!value) throw new InvalidDataException("reward-shop-check: " + reason); }
    private static void Wait(Task task) => _loop.RunUntilCompleted(task, TimeSpan.FromSeconds(60), "reward/shop fixture");
    private static (NativeRun Run, Player Player) Start(bool shop = false)
    {
        if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false);
        Task<(NativeRun Run, Player Player)> start = RunTestHost.StartNewRunBeforeChoicesAsync(
            ModelDb.AllCharacters.First(c => c.GetType().Name == "Ironclad"), 0, "R3B3-REWARD-SHOP", UnlockState.none);
        Wait(start); (NativeRun run, Player player) = start.Result;
        Wait(shop ? RunManager.Instance.EnterRoomDebug(RoomType.Shop, showTransition: false)
            : RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<ThisOrThat>(), showTransition: false));
        return (run, player);
    }
    private static string State(NativeRun run, Player player, RewardsSet? set = null)
    {
        string value = NativeRunDump.Capture(run, player);
        if (set != null) value += "\nrewards=" + set.Id + ":" + string.Join("|", set.Rewards.Select(r =>
            r.GetType().Name + ":" + r.SuccessfullySelected + ":" + (r is GoldReward g ? g.Amount.ToString() : r is CardReward c ? string.Join(",", c.Cards.Select(k => k.Id.Entry)) : "")));
        if (run.CurrentRoom is MerchantRoom shop) value += "\nshop=" + string.Join("|", shop.GetLocalInventory().AllEntries.Select(e =>
            e.GetType().Name + ":" + e.IsStocked + ":" + e.Cost + ":" + EntryId(e)));
        return value;
    }
    private static string EntryId(MerchantEntry entry) => entry switch
    {
        MerchantCardEntry c => c.CreationResult?.Card.Id.Entry ?? "sold", MerchantRelicEntry r => r.Model?.Id.Entry ?? "sold",
        MerchantPotionEntry p => p.Model?.Id.Entry ?? "sold", MerchantCardRemovalEntry r => r.Used.ToString(), _ => "unknown",
    };
    private static RunAction Pick(RunDecisionRequest request, int index) => RunDecisionProtocol.Select(request, [request.Options[index].OptionId]);
    private static int Row(RunDecisionRequest request, RunDecisionOptionKind kind)
        => Enumerable.Range(0, request.OptionCount).Single(i => request.Options[i].Kind == kind);
    private static int EntryRow(NativeDecisionSnapshot<object> snapshot, object entry)
        => Enumerable.Range(0, snapshot.Request.OptionCount).Where(i => snapshot.Request.Options[i].Flags == RunDecisionOptionFlags.None).Single(i => ReferenceEquals(snapshot.Resolve(Pick(snapshot.Request, i))[0], entry));
    private static int ShopRow(NativeDecisionTestSession session, MerchantRoom room, Player player, object entry)
        => EntryRow(RunDecisionImporter.Shop(room, player, () => true, () => true, session.Request.Token), entry);
    private static void Reject(Action action, string reason)
    {
        _checks++;
        try { action(); }
        catch (InvalidDataException e) when (e.Message.Contains(reason, StringComparison.Ordinal)) { return; }
        catch (BlitzSolver.Simulation.Combat.UnsupportedException e) when (e.Message.Contains(reason, StringComparison.Ordinal)) { return; }
        catch (UnsupportedImportException e) when (e.Message.Contains(reason, StringComparison.Ordinal)) { return; }
        throw new InvalidDataException("expected rejection: " + reason);
    }
    private static void RejectSubmit(NativeDecisionTestSession session, RunAction action, string reason, NativeRun run, Player player, RewardsSet? set = null)
    {
        string before = State(run, player, set); int calls = session.NativeCalls;
        Reject(() => Wait(session.Submit(action, confirmed: true)), reason);
        Check(calls == session.NativeCalls && before == State(run, player, set), "rejection preserves native state/RNG: " + reason);
    }
    private static void Offer(RewardsSet set, Action action)
    {
        RewardsSet.testSelector = offered => { Check(ReferenceEquals(offered, set), "actual Offer callback set"); action(); return Task.CompletedTask; };
        try { Wait(set.Offer()); } finally { RewardsSet.testSelector = null; }
    }
    private static CardReward Cards(NativeRun run, Player player, bool skip = true, bool reroll = false)
        => new([run.CreateCard<Anger>(player), run.CreateCard<Anger>(player)], CardCreationSource.Encounter, player,
            CardCreationOptions.ForRoom(player, RoomType.Monster)) { CanSkip = skip, CanReroll = reroll };
    private static RewardsSet Set(Player player, params Reward[] rewards) => new RewardsSet(player).WithCustomRewards(rewards.ToList());

    private static void RewardsRoundtrip()
    {
        (NativeRun run, Player player) = Start();
        CardReward cards = Cards(run, player, skip: false);
        GoldReward gold = new(17, 19, player); RelicReward relic = new(ModelDb.Relic<Vajra>().ToMutable(), player);
        PotionReward potion = new(ModelDb.Potion<BlockPotion>().ToMutable(), player);
        RewardsSet set = Set(player, gold, relic, potion, cards).WithSkippingDisallowed();
        NativeDecisionTestSession session = new(); CardModel? picked = null;
        PendingRewardCardSelector selector = new(session, request =>
        {
            Check(request.OptionCount == 2 && request.Options[0].Card.Id == request.Options[1].Card.Id, "forced duplicate-model reward cards");
            Check(!ReferenceEquals(selectorCard(0), selectorCard(1)), "native reward cards are different instances");
            picked = selectorCard(1); Reject(() => RunDecisionProtocol.Cancel(request), "action-cancel");
            Wait(session.Submit(Pick(request, 1)));
        });
        CardModel selectorCard(int index) => cards.Cards.ElementAt(index);
        using IDisposable scope = CardSelectCmd.UseSelector(selector);
        Offer(set, () =>
        {
            string before = State(run, player, set); int id = set.Id;
            NativeDecisionSnapshot<object> snapshot = RunDecisionImporter.Rewards(set,
                () => set.Rewards.Where(r => !r.SuccessfullySelected).ToArray(), () => true, () => true, new(710, 1));
            snapshot.ValidateCurrent();
            Check(before == State(run, player, set) && set.Id == id, "already begun imports neither regenerate nor begin and preserve RNG");
            Check(snapshot.Request.OptionCount == 4 && snapshot.Request.CardCount == 0, "forced set no Leave and unopened cards hidden");
            int gi = EntryRow(snapshot, gold); RunDecisionOption row = snapshot.Request.Options[gi];
            Check(row.RewardType == RunRewardType.Gold && snapshot.Request.Facts[row.Effects.Start].Kind == RunDecisionFactKind.GoldGain
                && snapshot.Request.Facts[row.Effects.Start].Amount == gold.Amount, "typed actual populated gold amount");
            RunDecisionToken prior = default;
            while (!set.AllRewardsSuccessfullySelected)
            {
                session.Rewards(set, () => true, () => true, () => { }); RunDecisionRequest request = session.Request;
                Check(request.Token.Revision > prior.Revision, "return to list gets fresh revision");
                RunAction action = Pick(request, 0); Wait(session.Submit(action));
                RejectSubmit(session, action, "host-no-pending", run, player, set); prior = request.Token;
            }
            Check(picked != null && player.Deck.Cards.Any(c => ReferenceEquals(c, picked)), "exact second duplicate card added by native");
        });
        string actual = State(run, player, set); scope.Dispose();
        (NativeRun direct, Player dp) = Start(); CardReward dc = Cards(direct, dp, skip: false);
        RewardsSet ds = Set(dp, new GoldReward(17, 19, dp), new RelicReward(ModelDb.Relic<Vajra>().ToMutable(), dp),
            new PotionReward(ModelDb.Potion<BlockPotion>().ToMutable(), dp), dc).WithSkippingDisallowed();
        using IDisposable directScope = CardSelectCmd.UseSelector(new ScriptSelector((options, _) => new() { card = options[1].Card }));
        Offer(ds, () => { foreach (Reward reward in ds.Rewards) Wait(RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward)); });
        Check(actual == State(direct, dp, ds), "reward list/card/potion/relic independent native direct result and RNG");
    }

    private static void RewardBoundaries()
    {
        (NativeRun run, Player player) = Start(); RewardsSet set = Set(player, new GoldReward(10, player));
        string before = State(run, player, set);
        Reject(() => RunDecisionImporter.Rewards(set, () => set.Rewards, () => true, () => true, new(711, 1)), "not generated");
        Check(before == State(run, player, set), "not pending import no effects");
        NativeDecisionSnapshot<object>? old = null;
        Offer(set, () =>
        {
            bool active = true; NativeDecisionTestSession session = new();
            old = RunDecisionImporter.Rewards(set, () => set.Rewards, () => active, () => true, new(711, 2));
            session.Rewards(set, () => active, () => true, () => active = false);
            RunAction malformed = Pick(session.Request, 0); malformed.OptionIds[0] = uint.MaxValue;
            RejectSubmit(session, malformed, "action-option", run, player, set);
            Wait(session.Submit(Pick(session.Request, Row(session.Request, RunDecisionOptionKind.Leave))));
            Reject(old.ValidateCurrent, "no pending screen");
        });
        Reject(() => RunDecisionImporter.Rewards(set, () => set.Rewards, () => true, () => true, new(711, 3)), "completed set");
        Reject(() => RunDecisionImporter.Rewards(set, () => [], () => true, () => true, new(711, 4)), "completed set");
        // Directed terminal fixture: original CombatRoom and EmptyForRoom, without playing a battle.
        CombatRoom combat = new(ModelDb.All.OfType<EncounterModel>().First(e => e.RoomType == RoomType.Monster).ToMutable(), run);
        run.PushRoom(combat);
        RewardsSet terminalSet = new RewardsSet(player).EmptyForRoom(combat);
        bool terminal = true;
        Offer(terminalSet, () =>
        {
            NativeDecisionSnapshot<object> done = RunDecisionImporter.Rewards(terminalSet, () => [], () => terminal, () => true, new(711, 7));
            Check(done.Request.OptionCount == 1 && done.Request.Options[0].Kind == RunDecisionOptionKind.Leave, "actual CombatRoom terminal shape has Proceed only");
            terminal = false; Reject(done.ValidateCurrent, "no pending screen");
        });
        run.PopCurrentRoom();
        RewardsSet forced = Set(player, new GoldReward(3, player)).WithSkippingDisallowed();
        Offer(forced, () =>
        {
            NativeDecisionSnapshot<object> snap = RunDecisionImporter.Rewards(forced, () => forced.Rewards, () => true, () => true, new(711, 5));
            Check(snap.Request.OptionCount == 1, "forced set cannot Leave");
            Wait(RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(forced.Rewards[0]));
        });
        Start(); Reject(() => RunDecisionImporter.Rewards(set, () => [], () => true, () => true, new(711, 6)), "active native run");
    }

    private static void RerollRoundtrip()
    {
        (NativeRun run, Player player) = Start(); CardReward cards = Cards(run, player, reroll: true); RewardsSet set = Set(player, cards);
        NativeDecisionTestSession session = new(); RunDecisionRequest first = default; NativeDecisionSnapshot<object>? stale = null;
        int callbacks = 0; CardModel[]? original = null;
        IReadOnlyList<CardCreationResult>? selectorOptions = null; IReadOnlyList<CardRewardAlternative>? selectorAlternatives = null;
        PendingRewardCardSelector selector = new(session, request =>
        {
            callbacks++;
            if (callbacks == 1)
            {
                first = request; original = cards.Cards.ToArray();
                stale = RunDecisionImporter.CardReward(cards, selectorOptions!, selectorAlternatives!, () => true, new(712, 1));
                Wait(session.Submit(Pick(request, Row(request, RunDecisionOptionKind.Reroll))));
            }
            else
            {
                Check(request.Token.Revision > first.Token.Revision && cards.Cards.All(c => !original!.Any(o => ReferenceEquals(o, c))), "native Reroll replaces cards and creates fresh token");
                Check(!Enumerable.Range(0, request.OptionCount).Any(i => request.Options[i].Kind == RunDecisionOptionKind.Reroll), "stale native test callback REROLL is no longer displayed");
                Reject(stale!.ValidateCurrent, "native-stale");
                RejectSubmit(session, Pick(first, 0), "action-token", run, player, set);
                Wait(session.Submit(Pick(request, Row(request, RunDecisionOptionKind.Skip))));
            }
        });
        // Capture the actual original callback collections before the session imports them.
        using IDisposable scope = CardSelectCmd.UseSelector(new ScriptSelector((o, a) =>
        { selectorOptions = o; selectorAlternatives = a; return selector.GetSelectedCardReward(o, a); }));
        Offer(set, () =>
        {
            session.Rewards(set, () => true, () => true, () => { }); RunDecisionRequest parent = session.Request;
            Wait(session.Submit(Pick(parent, 0)));
            Check(callbacks == 2 && !cards.SuccessfullySelected, "real while re-enters callback and Skip returns false");
            session.Rewards(set, () => true, () => true, () => { });
            Check(session.Request.OptionCount == 2 && session.Request.Options[0].Kind == RunDecisionOptionKind.Reward
                && session.Request.Token.Revision > first.Token.Revision, "Skip keeps native reward button visible with fresh list token");
            Wait(session.Submit(Pick(session.Request, Row(session.Request, RunDecisionOptionKind.Leave))));
        });
        string actual = State(run, player, set); scope.Dispose();
        (NativeRun direct, Player dp) = Start(); CardReward dc = Cards(direct, dp, reroll: true); RewardsSet ds = Set(dp, dc); int call = 0;
        using IDisposable directScope = CardSelectCmd.UseSelector(new ScriptSelector((_, alternatives) =>
        { call++; return new() { alternative = alternatives.Single(a => a.OptionId == (call == 1 ? "REROLL" : "Skip")) }; }));
        Offer(ds, () => { Wait(RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(dc)); RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet(); });
        Check(actual == State(direct, dp, ds), "reroll/skip agree with independent original native callbacks and RNG");
    }

    private static void CardNegatives()
    {
        (NativeRun run, Player player) = Start(); CardReward cards = Cards(run, player); RewardsSet set = Set(player, cards);
        using IDisposable scope = CardSelectCmd.UseSelector(new ScriptSelector((o, a) =>
        {
            string before = State(run, player, set);
            List<CardRewardAlternative> actual = (List<CardRewardAlternative>)a;
            actual.Add(new CardRewardAlternative("HEAL", PostAlternateCardRewardAction.EndSelectionAndCompleteReward));
            Reject(() => RunDecisionImporter.CardReward(cards, o, a, () => true, new(713, 1)), "unsupported alternative");
            actual.RemoveAt(actual.Count - 1);
            Check(before == State(run, player, set), "unknown alternative import has no effects");
            NativeDecisionSnapshot<object> snap = RunDecisionImporter.CardReward(cards, o, a, () => true, new(713, 2));
            Reject(() => RunDecisionImporter.CardReward(cards, o.ToArray(), a, () => true, new(713, 3)), "actual native collection");
            RewardsSet nested = Set(player, cards); Wait(nested.GenerateWithoutOffering());
            _ = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(nested);
            Reject(snap.ValidateCurrent, "native-stale"); RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            return new() { alternative = a.Single(x => x.OptionId == "Skip") };
        }));
        Offer(set, () => { Wait(RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(cards)); RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet(); });
        scope.Dispose();
        CardReward faultCards = Cards(run, player); RewardsSet faultSet = Set(player, faultCards);
        NativeDecisionTestSession faultSession = new();
        using IDisposable faultScope = CardSelectCmd.UseSelector(new PendingRewardCardSelector(faultSession,
            _ => throw new InvalidDataException("fixture-reward-answer-fault")));
        Offer(faultSet, () =>
        {
            faultSession.Rewards(faultSet, () => true, () => true, () => { });
            Reject(() => Wait(faultSession.Submit(Pick(faultSession.Request, 0))), "fixture-reward-answer-fault");
            Check(!faultSession.HasPending, "synchronous callback exception clears only its child pending");
            faultSession.Rewards(faultSet, () => true, () => true, () => { });
            Check(faultSession.HasPending, "callback exception releases parent busy and permits new list");
            Wait(faultSession.Submit(Pick(faultSession.Request, Row(faultSession.Request, RunDecisionOptionKind.Leave))));
        });
        faultScope.Dispose();
        player.AddRelicInternal(ModelDb.Relic<PaelsWing>().ToMutable());
        CardReward wingCards = Cards(run, player); RewardsSet wingSet = Set(player, wingCards);
        using IDisposable wingScope = CardSelectCmd.UseSelector(new ScriptSelector((options, alternatives) =>
        {
            Check(alternatives.Any(a => a.OptionId == "SACRIFICE"), "native Generate runs actual PaelsWing hook");
            string before = State(run, player, wingSet);
            Reject(() => RunDecisionImporter.CardReward(wingCards, options, alternatives, () => true, new(713, 6)), "unsupported alternative SACRIFICE");
            Check(before == State(run, player, wingSet), "real unknown alternative rejected read only");
            return new() { alternative = alternatives.Single(a => a.OptionId == "Skip") };
        }));
        Offer(wingSet, () => { Wait(RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(wingCards)); RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet(); });
        RewardsSet capacity = Set(player, Enumerable.Range(0, RunLimits.DecisionOptions + 1).Select(_ => (Reward)new GoldReward(1, player)).ToArray());
        Offer(capacity, () =>
        {
            string before = State(run, player, capacity); bool failed = false;
            try { RunDecisionImporter.Rewards(capacity, () => capacity.Rewards, () => true, () => true, new(713, 4)); }
            catch (CapacityException) { failed = true; }
            Check(failed && before == State(run, player, capacity), "capacity fails without truncating or changing native state/RNG");
            RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
        });
        RewardsSet unknown = Set(player, new CardRemovalReward(player));
        Offer(unknown, () =>
        {
            Reject(() => RunDecisionImporter.Rewards(unknown, () => unknown.Rewards, () => true, () => true, new(713, 5)), "unsupported reward");
            RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
        });
    }

    private static void Fill(Player player)
    {
        while (player.Potions.Count() < player.MaxPotionCount)
            Check(player.AddPotionInternal(ModelDb.Potion<BlockPotion>().ToMutable()).success, "direct full-slot fixture");
    }
    private static void PotionRoundtrip()
    {
        foreach (bool back in new[] { true, false })
        {
            (NativeRun run, Player player) = Start(); Fill(player);
            PotionReward potion = new(ModelDb.Potion<FirePotion>().ToMutable(), player); RewardsSet set = Set(player, potion);
            NativeDecisionTestSession session = new();
            Offer(set, () =>
            {
                session.Rewards(set, () => true, () => true, () => { }); Wait(session.Submit(Pick(session.Request, 0)));
                Check(!potion.SuccessfullySelected && potion.ClaimedPotion == null, "native TooFull returns false and keeps reward");
                bool active = true; session.Potions(potion, () => active, () => active = false);
                RunDecisionRequest request = session.Request;
                NativeDecisionSnapshot<object> binding = RunDecisionImporter.PotionDiscard(potion, () => true, new(714, 1));
                PotionModel first = player.GetPotionAtSlotIndex(0)!; PotionModel second = player.GetPotionAtSlotIndex(1)!;
                Check(!ReferenceEquals(first, second) && first.Id == second.Id
                    && ReferenceEquals(binding.Resolve(Pick(binding.Request, 1))[0], second), "same-model potion slots preserve original identity");
                string before = State(run, player, set); binding.ValidateCurrent(); Check(before == State(run, player, set), "potion import read only/RNG");
                AccessTools.Property(typeof(PotionModel), nameof(PotionModel.IsQueued)).SetValue(first, true);
                Reject(binding.ValidateCurrent, "native-stale");
                NativeDecisionSnapshot<object> queued = RunDecisionImporter.PotionDiscard(potion, () => true, new(714, 2));
                Check(queued.Request.Options[0].Flags == RunDecisionOptionFlags.Disabled, "queued native potion cannot be discarded");
                AccessTools.Property(typeof(PotionModel), nameof(PotionModel.IsQueued)).SetValue(first, false);
                Wait(session.Submit(Pick(request, back ? Row(request, RunDecisionOptionKind.Skip) : 1)));
                Check(!potion.SuccessfullySelected && potion.ClaimedPotion == null, "management never auto claims reward");
                Check(back ? player.Potions.Count() == player.MaxPotionCount : player.GetPotionAtSlotIndex(1) == null
                    && ReferenceEquals(first, player.GetPotionAtSlotIndex(0)), "back preserves slots; discard only exact second slot");
                session.Rewards(set, () => true, () => true, () => { });
                if (back) Wait(session.Submit(Pick(session.Request, Row(session.Request, RunDecisionOptionKind.Leave))));
                else { Wait(session.Submit(Pick(session.Request, 0))); Check(potion.SuccessfullySelected, "separate fresh list action retries native reward"); }
            });
            string actual = State(run, player, set);
            (NativeRun direct, Player dp) = Start(); Fill(dp); PotionReward pr = new(ModelDb.Potion<FirePotion>().ToMutable(), dp); RewardsSet ds = Set(dp, pr);
            Offer(ds, () =>
            {
                Wait(RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(pr));
                if (back) RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
                else { Wait(PotionCmd.Discard(dp.GetPotionAtSlotIndex(1)!)); Wait(RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(pr)); }
            });
            Check(actual == State(direct, dp, ds), "potion management direct model entry comparison back=" + back);
        }
    }
    private static void PotionNotAllowed()
    {
        (NativeRun run, Player player) = Start(); Fill(player); player.AddRelicInternal(ModelDb.Relic<Sozu>().ToMutable());
        PotionReward potion = new(ModelDb.Potion<FirePotion>().ToMutable(), player); RewardsSet set = Set(player, potion);
        Offer(set, () =>
        {
            Reject(() => RunDecisionImporter.PotionDiscard(potion, () => true, new(715, 1)), "not procurable");
            NativeDecisionTestSession session = new(); session.Rewards(set, () => true, () => true, () => { });
            Wait(session.Submit(Pick(session.Request, 0)));
            Check(potion.SuccessfullySelected && ReferenceEquals(potion.ClaimedPotion, potion.Potion)
                && player.Potions.All(p => p is BlockPotion), "NotAllowed is consumed native reward, distinct from TooFull");
        });
        string actual = State(run, player, set);
        (NativeRun direct, Player dp) = Start(); Fill(dp); dp.AddRelicInternal(ModelDb.Relic<Sozu>().ToMutable());
        PotionReward pr = new(ModelDb.Potion<FirePotion>().ToMutable(), dp); RewardsSet ds = Set(dp, pr);
        Offer(ds, () => Wait(RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(pr)));
        Check(actual == State(direct, dp, ds), "NotAllowed independent original result/RNG");
    }

    private static void ShopBoundaries()
    {
        (NativeRun run, Player player) = Start(shop: true); MerchantRoom room = (MerchantRoom)run.CurrentRoom!;
        MerchantInventory inventory = room.GetLocalInventory(); player.Gold = 0; Fill(player);
        NativeDecisionTestSession session = new(); bool active = true;
        session.Shop(room, player, () => active, () => true, () => active = false);
        RunDecisionRequest request = session.Request;
        Check(request.OptionCount == inventory.AllEntries.Count() + 1, "all displayed unaffordable stock preserved");
        Check(Enumerable.Range(0, request.OptionCount - 1).All(i => request.Options[i].Flags == RunDecisionOptionFlags.Disabled), "unaffordable and full potions disabled");
        RunAction disabled = Pick(request, request.OptionCount - 1); disabled.OptionIds[0] = request.Options[0].OptionId;
        RejectSubmit(session, disabled, "action-disabled", run, player);
        NativeDecisionSnapshot<object> stale = RunDecisionImporter.Shop(room, player, () => active, () => true, new(716, 1));
        Wait(session.Submit(Pick(request, Row(request, RunDecisionOptionKind.Leave))));
        Check(ReferenceEquals(run.CurrentRoom, room), "Leave closes inventory without popping room or map travel");
        Reject(stale.ValidateCurrent, "no pending");
        player.Gold = 9999; active = true;
        NativeDecisionSnapshot<object> before = RunDecisionImporter.Shop(room, player, () => active, () => true, new(716, 2));
        string state = State(run, player); before.ValidateCurrent(); Check(state == State(run, player), "shop import keeps prices and native RNG");
        Wait(inventory.CharacterCardEntries[0].OnTryPurchaseWrapper(inventory)); Reject(before.ValidateCurrent, "native-stale");
        NativeDecisionSnapshot<object> after = RunDecisionImporter.Shop(room, player, () => active, () => true, new(716, 3));
        Check(after.Request.OptionCount == request.OptionCount - 1, "stockless item hidden");
        inventory.CardRemovalEntry!.SetUsed(); after = RunDecisionImporter.Shop(room, player, () => active, () => true, new(716, 4));
        Check(after.Request.Options[Row(after.Request, RunDecisionOptionKind.CardRemoval)].Flags == RunDecisionOptionFlags.Disabled, "used removal placard retained disabled");
        foreach (PotionModel potion in player.Potions.ToArray()) Wait(PotionCmd.Discard(potion));
        player.AddRelicInternal(ModelDb.Relic<Sozu>().ToMutable());
        NativeDecisionSnapshot<object> forbidden = RunDecisionImporter.Shop(room, player, () => true, () => true, new(716, 7));
        Check(Enumerable.Range(0, forbidden.Request.OptionCount).Where(i => forbidden.Request.Options[i].Model.Kind == RunRefKind.Potion)
            .All(i => forbidden.Request.Options[i].Flags == RunDecisionOptionFlags.Disabled), "per-entry PotionCmd hook disallows stocked potions even with space");
        string blocked = State(run, player); Task<bool> failure = inventory.PotionEntries[0].OnTryPurchaseWrapper(inventory); Wait(failure);
        Check(!failure.Result && blocked == State(run, player), "blocked native merchant potion leaves gold stock and RNG unchanged");
        NativeDecisionSnapshot<object> price = RunDecisionImporter.Shop(room, player, () => true, () => true, new(716, 5));
        player.AddRelicInternal(ModelDb.Relic<MembershipCard>().ToMutable()); Reject(price.ValidateCurrent, "native-stale");
        NativeDecisionSnapshot<object> runStale = RunDecisionImporter.Shop(room, player, () => true, () => true, new(716, 6));
        Start(); Reject(runStale.ValidateCurrent, "active native run");
    }
    private static void ShopRoundtrip()
    {
        foreach (string kind in new[] { "card", "relic", "potion" })
        {
            (NativeRun run, Player player) = Start(shop: true); player.Gold = 9999;
            MerchantRoom room = (MerchantRoom)run.CurrentRoom!; MerchantInventory inventory = room.GetLocalInventory();
            MerchantEntry entry = kind == "card" ? inventory.CharacterCardEntries[0] : kind == "relic" ? inventory.RelicEntries[0] : inventory.PotionEntries[0];
            NativeDecisionTestSession session = new(); session.Shop(room, player, () => true, () => true, () => { });
            int rowIndex = ShopRow(session, room, player, entry); RunDecisionOption row = session.Request.Options[rowIndex];
            Check(row.CostsKnowledge == RunDecisionKnowledge.Complete && session.Request.Facts[row.Costs.Start].Kind == RunDecisionFactKind.GoldLoss
                && session.Request.Facts[row.Costs.Start].Amount == entry.Cost, "native hooked price positive GoldLoss " + kind);
            int gold = player.Gold, price = entry.Cost; RunAction action = Pick(session.Request, rowIndex); Wait(session.Submit(action));
            Check(player.Gold == gold - price && !entry.IsStocked, "native purchase deducts exact price and clears stock " + kind);
            RejectSubmit(session, action, "host-no-pending", run, player);
            string actual = State(run, player);
            (NativeRun direct, Player dp) = Start(shop: true); dp.Gold = 9999; MerchantInventory di = ((MerchantRoom)direct.CurrentRoom!).GetLocalInventory();
            MerchantEntry de = kind == "card" ? di.CharacterCardEntries[0] : kind == "relic" ? di.RelicEntries[0] : di.PotionEntries[0];
            Wait(de.OnTryPurchaseWrapper(di)); Check(actual == State(direct, dp), "independent original purchase and RNG " + kind);
        }
    }
    private static void ShopRestock()
    {
        (NativeRun run, Player player) = Start(shop: true); player.Gold = 9999; player.AddRelicInternal(ModelDb.Relic<TheCourier>().ToMutable());
        MerchantRoom room = (MerchantRoom)run.CurrentRoom!; MerchantInventory inventory = room.GetLocalInventory(); MerchantCardEntry entry = inventory.CharacterCardEntries[0];
        NativeDecisionSnapshot<object> stale = RunDecisionImporter.Shop(room, player, () => true, () => true, new(717, 1));
        CardModel original = entry.CreationResult!.Card; int price = entry.Cost;
        NativeDecisionTestSession session = new(); session.Shop(room, player, () => true, () => true, () => { });
        Check(session.Request.Facts[session.Request.Options[ShopRow(session, room, player, entry)].Costs.Start].Amount == price, "Courier price read directly");
        Wait(session.Submit(Pick(session.Request, ShopRow(session, room, player, entry))));
        Check(entry.IsStocked && !ReferenceEquals(original, entry.CreationResult!.Card), "native Courier restocks same entry with new card instance");
        Reject(stale.ValidateCurrent, "native-stale");
        string actual = State(run, player);
        (NativeRun direct, Player dp) = Start(shop: true); dp.Gold = 9999; dp.AddRelicInternal(ModelDb.Relic<TheCourier>().ToMutable());
        MerchantInventory di = ((MerchantRoom)direct.CurrentRoom!).GetLocalInventory();
        Wait(di.CharacterCardEntries[0].OnTryPurchaseWrapper(di));
        Check(actual == State(direct, dp), "Courier discounted purchase/restock independent native result and RNG");
        // Resume with the active direct run for the identity-only fixture.
        run = direct; player = dp; room = (MerchantRoom)direct.CurrentRoom!; inventory = di; entry = di.CharacterCardEntries[0];
        // Keep payload/price identical: identity alone must still reject same-entry replacement.
        stale = RunDecisionImporter.Shop(room, player, () => true, () => true, new(717, 2));
        CardModel sameModel = run.CreateCard(entry.CreationResult!.Card.CanonicalInstance, player);
        AccessTools.Property(typeof(MerchantCardEntry), nameof(MerchantCardEntry.CreationResult)).SetValue(entry, new CardCreationResult(sameModel));
        Reject(stale.ValidateCurrent, "native-stale");
        NativeDecisionSnapshot<object> free;
        AccessTools.Field(typeof(MerchantEntry), "_cost").SetValue(entry, 0);
        free = RunDecisionImporter.Shop(room, player, () => true, () => true, new(717, 3));
        RunDecisionOption zero = free.Request.Options[EntryRow(free, entry)];
        Check(zero.CostsKnowledge == RunDecisionKnowledge.Complete && zero.Costs.Count == 0, "zero price explicit Complete empty costs");
        AccessTools.Field(typeof(MerchantEntry), "_cost").SetValue(entry, -10);
        Reject(() => RunDecisionImporter.Shop(room, player, () => true, () => true, new(717, 4)), "negative price");
    }
    private static void ShopRemoval()
    {
        foreach (bool cancel in new[] { true, false })
        {
            (NativeRun run, Player player) = Start(shop: true); player.Gold = 9999;
            MerchantRoom room = (MerchantRoom)run.CurrentRoom!; MerchantCardRemovalEntry removal = room.GetLocalInventory().CardRemovalEntry!;
            NativeDecisionTestSession session = new(); PendingNativeCardSelector selector = new(session);
            using IDisposable scope = CardSelectCmd.UseSelector(selector);
            session.Shop(room, player, () => true, () => true, () => { }); RunDecisionRequest parent = session.Request;
            Task effect = session.Submit(Pick(parent, Row(parent, RunDecisionOptionKind.CardRemoval)));
            Check(!effect.IsCompleted && session.Request.Kind == RunDecision.CardSelect, "real OneOffSynchronizer pending removal child");
            RunDecisionRequest child = session.Request; int gold = player.Gold, deck = player.Deck.Cards.Count, price = removal.Cost;
            Check(child.Cancelable == 1 && child.RequireConfirmation == 1 && child.SelectionPurpose == RunSelectionPurpose.Remove, "complete actual removal prefs and purpose");
            CardModel selected = selector.LastOptions![1];
            RejectSubmit(session, Pick(parent, Row(parent, RunDecisionOptionKind.CardRemoval)), "action-token", run, player);
            Wait(session.Submit(cancel ? RunDecisionProtocol.Cancel(child) : Pick(child, 1), confirmed: !cancel)); Wait(effect);
            Check(player.Gold == gold - (cancel ? 0 : price) && player.Deck.Cards.Count == deck - (cancel ? 0 : 1)
                && removal.Used == !cancel && player.ExtraFields.CardShopRemovalsUsed == (cancel ? 0 : 1), "native removal success/cancel bookkeeping");
            Check(cancel || !player.Deck.Cards.Any(c => ReferenceEquals(c, selected)), "removal selects exact original duplicate instance");
            RejectSubmit(session, Pick(child, 1), "host-no-pending", run, player);
            session.Shop(room, player, () => true, () => true, () => { }); Check(session.Request.Token.Revision > child.Token.Revision, "fresh parent shop after child");
            string actual = State(run, player); scope.Dispose();
            (NativeRun direct, Player dp) = Start(shop: true); dp.Gold = 9999; MerchantInventory di = ((MerchantRoom)direct.CurrentRoom!).GetLocalInventory();
            using IDisposable directScope = CardSelectCmd.UseSelector(new ScriptSelector(null, (cards, _, _) => cancel ? [] : [cards.ElementAt(1)]));
            Task<bool> directEffect = di.CardRemovalEntry!.OnTryPurchaseWrapper(di); Wait(directEffect);
            if (directEffect.Result) di.CardRemovalEntry.SetUsed();
            Check(actual == State(direct, dp), "removal independent native dispatch comparison cancel=" + cancel);
        }
        (NativeRun sr, Player sp) = Start(shop: true); sp.Gold = 9999; MerchantRoom shop = (MerchantRoom)sr.CurrentRoom!;
        NativeDecisionTestSession stale = new(); PendingNativeCardSelector pending = new(stale); using IDisposable staleScope = CardSelectCmd.UseSelector(pending);
        stale.Shop(shop, sp, () => true, () => true, () => { }); Task parentTask = stale.Submit(Pick(stale.Request, Row(stale.Request, RunDecisionOptionKind.CardRemoval)));
        RunAction answer = Pick(stale.Request, 0); Wait(CardPileCmd.RemoveFromDeck(pending.LastOptions![0]));
        RejectSubmit(stale, answer, "native-stale", sr, sp); Reject(() => Wait(parentTask), "native-stale");
        Check(!stale.HasPending && parentTask.IsCompleted, "stale removal child terminates original wait chain");
        stale.Shop(shop, sp, () => true, () => true, () => { }); Check(stale.HasPending, "failed child releases host busy state");
    }
    private static void PriceRestoration()
    {
        (NativeRun run, Player player) = Start(shop: true); bool wasOn = TestMode.IsOn;
        MerchantPotionEntry entry = ((MerchantRoom)run.CurrentRoom!).GetLocalInventory().PotionEntries[0];
        int before = player.PlayerRng.Shops.ToSerializable().counter;
        entry.CalcCost();
        Check(TestMode.IsOn == wasOn && player.PlayerRng.Shops.ToSerializable().counter == before + 1,
            "normal local original-price scope draws one Shops value and restores true");
        TestMode.IsOn = false;
        try { entry.CalcCost(); Check(!TestMode.IsOn, "local original-price scope restores false entry"); }
        finally { TestMode.IsOn = wasOn; }
        AccessTools.Property(typeof(MerchantPotionEntry), nameof(MerchantPotionEntry.Model)).SetValue(entry, null);
        bool failed = false;
        try { entry.CalcCost(); } catch (InvalidOperationException e) when (e.Message.Contains("no item", StringComparison.Ordinal)) { failed = true; }
        Check(failed && TestMode.IsOn == wasOn && wasOn, "local original-price scope restores TestMode on native exception");
    }
    private sealed class ScriptSelector(
        Func<IReadOnlyList<CardCreationResult>, IReadOnlyList<CardRewardAlternative>, CardRewardSelection>? reward,
        Func<IEnumerable<CardModel>, int, int, IEnumerable<CardModel>>? deck = null) : ICardSelector
    {
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
            => reward?.Invoke(options, alternatives) ?? throw new NotSupportedException("unexpected reward callback");
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
            => Task.FromResult(deck?.Invoke(options, minSelect, maxSelect) ?? throw new NotSupportedException("unexpected deck callback"));
    }
}
