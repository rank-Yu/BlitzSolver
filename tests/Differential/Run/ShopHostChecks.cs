// Directed production OfflineRunHost shop fixtures. Debug setup is test data only.
// Purchases/selectors/history are compared with direct original methods on a fresh identical run.
using System.Text.Json;
using BlitzSolver.Ai.Run;
using BlitzSolver.Autoplay;
using BlitzSolver.Import;
using BlitzSolver.OfflineHost.Run;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;
using Bootstrap = BlitzSolver.OfflineHost.Runtime.GameBootstrap;

namespace Differential;

internal static class ShopHostChecks
{
    private static MainLoopContext _loop = null!;
    private static int _checks, _cases;
    internal static int Run()
    {
        _loop = new(); SynchronizationContext.SetSynchronizationContext(_loop);
        Bootstrap.ApplyGodotBypasses(); Bootstrap.SkipGodotNodeStaticConstructors(); Bootstrap.InitializeStaticState();
        foreach (string kind in new[] { "card", "relic", "potion" }) Case("purchase-" + kind, () => Purchase(kind, false));
        Case("discount-restock-exact-instance", () => Purchase("card", true));
        Case("gold-stale-and-insufficient", Stale);
        Case("removal-cancel", () => Removal(true));
        Case("removal-success", () => Removal(false));
        Case("removal-external-cancel-late", Cancel);
        Case("leave-defers-native-history", Leave);
        Case("normal-price-rng-and-finally-restoration", Price);
        Case("full-shop-potion-disabled", FullPotions);
        Case("unknown-nested-native-card-entry-rejected", Unsupported);
        Console.WriteLine($"SHOPHOST status=pass cases={_cases} checks={_checks}"); return 0;
    }
    private static void Check(bool condition, string reason) { _checks++; if (!condition) throw new InvalidDataException("shop-host:" + reason); }
    private static void Case(string name, Action test)
    {
        Console.WriteLine($"SHOPHOST case={name} status=start");
        try { test(); _cases++; Console.WriteLine($"SHOPHOST case={name} status=pass"); }
        finally { if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false); }
    }
    private static void Wait(Task task) => _loop.RunUntilCompleted(task, TimeSpan.FromSeconds(60), "shop-host-fixture");
    private static T Get<T>(ValueTask<T> value) { var task = value.AsTask(); Wait(task); return task.Result; }
    private static RunPilotAnswer Pick(RunView view, int index)
        => new(RunDecisionProtocol.Select(view.Request, [view.Request.Options[index].OptionId]), true);
    private static RunPilotAnswer Kind(RunView view, RunDecisionOptionKind kind)
        => Pick(view, Enumerable.Range(0, view.Request.OptionCount).Single(i => view.Request.Options[i].Kind == kind));
    private static RunDecisionCallback NoChild => (_, _) => throw new InvalidDataException("unexpected-shop-child");
    private static (NativeRun Run, Player Player, MerchantRoom Room) Start(bool courier = false)
    {
        if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false);
        var task = RunTestHost.StartNewRunBeforeChoicesAsync(ModelDb.AllCharacters.Single(c => c.GetType().Name == "Ironclad"),
            0, "R3B3-REWARD-SHOP", UnlockState.none);
        Wait(task); var (run, player) = task.Result;
        Wait(RunManager.Instance.EnterRoomDebug(RoomType.Shop, showTransition: false));
        player.Gold = 9999;
        if (courier) player.AddRelicInternal(ModelDb.Relic<TheCourier>().ToMutable());
        return (run, player, (MerchantRoom)run.CurrentRoom!);
    }
    private static OfflineRunHost Host(NativeRun run, Player player, MerchantRoom room, Action? close = null)
        => new(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Shop,
            Shop: room, ShopActive: () => ReferenceEquals(run.CurrentRoom, room), CloseShop: close ?? (() => { }))));
    private static int Row(RunView view, MerchantRoom room, Player player, MerchantEntry entry)
    {
        var snapshot = RunDecisionImporter.Shop(room, player, () => true, () => true, view.Request.Token);
        return Enumerable.Range(0, view.Request.OptionCount).Where(i => view.Request.Options[i].Flags == RunDecisionOptionFlags.None)
            .Single(i => ReferenceEquals(snapshot.Resolve(Pick(view, i).Action)[0], entry));
    }
    private static string State(NativeRun run, Player player) => NativeRunDump.Capture(run, player)
        + JsonSerializer.Serialize(run.MapPointHistory, new JsonSerializerOptions { IncludeFields = true })
        + (run.CurrentRoom is MerchantRoom room ? string.Join("|", room.GetLocalInventory().AllEntries.Select(e =>
            e.GetType().Name + ":" + e.IsStocked + ":" + e.Cost + ":" + (e is MerchantCardEntry c ? c.CreationResult?.Card.Id.Entry
            : e is MerchantRelicEntry r ? r.Model?.Id.Entry : e is MerchantPotionEntry p ? p.Model?.Id.Entry : ""))) : "");
    private static MerchantEntry Entry(MerchantInventory inventory, string kind) => kind switch
    { "card" => inventory.CharacterCardEntries[0], "relic" => inventory.RelicEntries[0], _ => inventory.PotionEntries[0] };
    private static void Purchase(string kind, bool courier)
    {
        var (run, player, room) = Start(courier); OfflineRunHost host = Host(run, player, room);
        var inventory = room.GetLocalInventory(); var entry = Entry(inventory, kind);
        var original = entry is MerchantCardEntry c ? c.CreationResult!.Card : null;
        RunView view = Get(host.WaitForBoundaryAsync(default)).View; int row = Row(view, room, player, entry);
        int price = entry.Cost, before = player.Gold;
        Check(view.Request.Facts[view.Request.Options[row].Costs.Start].Amount == price, "already-calculated hooked price");
        Wait(host.ExecuteAsync(Pick(view, row), NoChild, default).AsTask());
        Check(player.Gold == before - price, "exact native payment " + kind);
        if (courier) Check(entry.IsStocked && !ReferenceEquals(original, ((MerchantCardEntry)entry).CreationResult!.Card), "native restock replaces instance");
        else Check(!entry.IsStocked, "sold stock");
        string actual = State(run, player); Wait(host.DisposeAsync().AsTask());
        var (direct, dp, dr) = Start(courier); Wait(Entry(dr.GetLocalInventory(), kind).OnTryPurchaseWrapper(dr.GetLocalInventory()));
        Check(actual == State(direct, dp), "independent direct purchase/full history/RNG " + kind);
    }
    private static void Stale()
    {
        var (run, player, room) = Start(); var host = Host(run, player, room);
        RunView view = Get(host.WaitForBoundaryAsync(default)).View;
        int row = Row(view, room, player, room.GetLocalInventory().CharacterCardEntries[0]);
        player.Gold = 0;
        bool rejected = false;
        try { Wait(host.ExecuteAsync(Pick(view, row), NoChild, default).AsTask()); }
        catch (InvalidDataException e) when (e.Message.Contains("stale", StringComparison.Ordinal)) { rejected = true; }
        Check(rejected, "gold changes invalidate pending quote");
        view = Get(host.WaitForBoundaryAsync(default)).View;
        Check(Enumerable.Range(0, view.Request.OptionCount).All(i => view.Request.Options[i].Kind == RunDecisionOptionKind.Leave
            || view.Request.Options[i].Flags != RunDecisionOptionFlags.None), "insufficient gold disables native goods");
        host.InvalidatePending(); player.Gold = 9999;
        view = Get(host.WaitForBoundaryAsync(default)).View;
        var entry = room.GetLocalInventory().CharacterCardEntries[0]; row = Row(view, room, player, entry);
        AccessTools.Property(typeof(MerchantCardEntry), nameof(MerchantCardEntry.CreationResult)).SetValue(entry,
            new MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult(run.CreateCard(entry.CreationResult!.Card.CanonicalInstance, player)));
        rejected = false;
        try { Wait(host.ExecuteAsync(Pick(view, row), NoChild, default).AsTask()); }
        catch (InvalidDataException e) when (e.Message.Contains("stale", StringComparison.Ordinal)) { rejected = true; }
        Check(rejected, "same-model replacement rejects stale instance");
        Wait(host.DisposeAsync().AsTask());
    }
    private static void Removal(bool cancel)
    {
        var (run, player, room) = Start(); var host = Host(run, player, room);
        RunView parent = Get(host.WaitForBoundaryAsync(default)).View, child = default;
        var removal = room.GetLocalInventory().CardRemovalEntry!;
        int gold = player.Gold, deck = player.Deck.Cards.Count, price = removal.Cost;
        TaskCompletionSource<RunPilotAnswer> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task task = host.ExecuteAsync(Kind(parent, RunDecisionOptionKind.CardRemoval), (v, _) => { child = v; return new(answer.Task); }, default).AsTask();
        Check(!task.IsCompleted && child.Request.SelectionPurpose == RunSelectionPurpose.Remove
            && child.Request.Cancelable == 1 && child.Request.RequireConfirmation == 1, "real asynchronous removal/full prefs/purpose");
        Check(player.Gold == gold && player.Deck.Cards.Count == deck && !removal.Used, "no effects before child answer");
        answer.SetResult(cancel ? new(RunDecisionProtocol.Cancel(child.Request), false) : Pick(child, 1)); Wait(task);
        Check(player.Gold == gold - (cancel ? 0 : price) && player.Deck.Cards.Count == deck - (cancel ? 0 : 1)
            && removal.Used == !cancel && player.ExtraFields.CardShopRemovalsUsed == (cancel ? 0 : 1), "native cancel/success bookkeeping");
        RunView next = Get(host.WaitForBoundaryAsync(default)).View;
        Check(next.Request.Token.Revision > child.Request.Token.Revision, "fresh monotonic shop token");
        string actual = State(run, player); Wait(host.DisposeAsync().AsTask());
        var (direct, dp, dr) = Start();
        using IDisposable selector = CardSelectCmd.UseSelector(new DirectSelector(cancel));
        Task<bool> effect = dr.GetLocalInventory().CardRemovalEntry!.OnTryPurchaseWrapper(dr.GetLocalInventory(), cancelable: true);
        Wait(effect); if (effect.Result) dr.GetLocalInventory().CardRemovalEntry!.SetUsed();
        Check(actual == State(direct, dp), "independent original removal/full history/RNG");
        Check(CardSelectCmd.Selector != null, "direct selector scope active"); selector.Dispose();
        Check(CardSelectCmd.Selector == null, "all selectors released");
    }
    private static void Cancel()
    {
        var (run, player, room) = Start(); var host = Host(run, player, room);
        RunView parent = Get(host.WaitForBoundaryAsync(default)).View, child = default;
        var late = new TaskCompletionSource<RunPilotAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        int gold = player.Gold, deck = player.Deck.Cards.Count;
        Task task = host.ExecuteAsync(Kind(parent, RunDecisionOptionKind.CardRemoval),
            (v, _) => { child = v; return new(late.Task); }, cancellation.Token).AsTask();
        cancellation.Cancel(); bool cancelled = false;
        try { Wait(task); } catch (OperationCanceledException) { cancelled = true; }
        Wait(host.DisposeAsync().AsTask()); late.SetResult(Pick(child, 1)); _loop.Pump(TimeSpan.FromMilliseconds(10));
        Check(cancelled && player.Gold == gold && player.Deck.Cards.Count == deck && !room.GetLocalInventory().CardRemovalEntry!.Used,
            "external cancellation/late answer cannot remove or pay");
        Check(CardSelectCmd.Selector == null, "selector restored after cancellation");
    }
    private static void Leave()
    {
        var (run, player, room) = Start(); bool closed = false; var host = Host(run, player, room, () => closed = true);
        string before = State(run, player); RunView view = Get(host.WaitForBoundaryAsync(default)).View;
        Wait(host.ExecuteAsync(Kind(view, RunDecisionOptionKind.Leave), NoChild, default).AsTask());
        Check(closed && before == State(run, player), "Leave only closes presentation");
        Wait(host.DisposeAsync().AsTask());
        Wait(RunManager.Instance.EnterRoomDebug(RoomType.RestSite, showTransition: false));
        string actual = State(run, player);
        Check(before != actual && run.MapPointHistory.SelectMany(a => a).SelectMany(h => h.PlayerStats).Any(h => h.CardChoices.Count > 0), "actual original Exit writes unbought choices");
        var (direct, dp, dr) = Start();
        bool previous = TestMode.IsOn; TestMode.IsOn = false;
        try { Wait(dr.Exit(direct)); } finally { TestMode.IsOn = previous; }
        // The direct Exit checkpoint remains in the shop; compare the exact history serialization.
        Check(JsonSerializer.Serialize(run.MapPointHistory.SelectMany(a => a).Where(h => h.PlayerStats.Any(s => s.CardChoices.Count > 0)), new JsonSerializerOptions { IncludeFields = true })
            == JsonSerializer.Serialize(direct.MapPointHistory.SelectMany(a => a).Where(h => h.PlayerStats.Any(s => s.CardChoices.Count > 0)), new JsonSerializerOptions { IncludeFields = true }), "history matches normal original Exit");
        Check(TestMode.IsOn, "Exit restores TestMode");
    }
    private static void Price()
    {
        var (_, player, room) = Start(); MerchantPotionEntry entry = room.GetLocalInventory().PotionEntries[0];
        var rng = player.PlayerRng.Shops.ToSerializable(); entry.CalcCost(); int cost = entry.Cost;
        Check(TestMode.IsOn && player.PlayerRng.Shops.ToSerializable().counter == rng.counter + 1, "normal price consumes Shops RNG");
        // Same native input/RNG with genuinely false TestMode must produce identical native price.
        var (_, directPlayer, directRoom) = Start();
        entry = directRoom.GetLocalInventory().PotionEntries[0];
        bool previous = TestMode.IsOn; TestMode.IsOn = false;
        try { entry.CalcCost(); Check(entry.Cost == cost && !TestMode.IsOn
            && directPlayer.PlayerRng.Shops.ToSerializable().counter == rng.counter + 1, "normal false price/RNG and scope restore false"); }
        finally { TestMode.IsOn = previous; }
        AccessTools.Property(typeof(MerchantPotionEntry), nameof(MerchantPotionEntry.Model)).SetValue(entry, null);
        bool failed = false; try { entry.CalcCost(); } catch (InvalidOperationException e) when (e.Message.Contains("no item", StringComparison.Ordinal)) { failed = true; }
        Check(failed && TestMode.IsOn, "native exception finally restores true");
    }
    private static void FullPotions()
    {
        var (run, player, room) = Start();
        for (int i = 0; i < player.MaxPotionCount; i++)
            Wait(PotionCmd.TryToProcure(ModelDb.Potion<MegaCrit.Sts2.Core.Models.Potions.BlockPotion>().ToMutable(), player));
        var host = Host(run, player, room);
        RunView view = Get(host.WaitForBoundaryAsync(default)).View;
        int potionRows = Enumerable.Range(0, view.Request.OptionCount).Count(i =>
            view.Request.Options[i].Model.Kind == RunRefKind.Potion && view.Request.Options[i].Flags == RunDecisionOptionFlags.Disabled);
        Check(potionRows == room.GetLocalInventory().PotionEntries.Count, "full shop potions disabled without reward-discard child");
        string before = State(run, player);
        int row = Enumerable.Range(0, view.Request.OptionCount).First(i => view.Request.Options[i].Model.Kind == RunRefKind.Potion);
        bool rejected = false;
        try { Wait(host.ExecuteAsync(Pick(view, row), NoChild, default).AsTask()); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && before == State(run, player), "disabled shop action has no inventory effects");
        Wait(host.DisposeAsync().AsTask());
    }
    private static void Unsupported()
    {
        var (run, player, room) = Start();
        var entry = room.GetLocalInventory().RelicEntries[0];
        AccessTools.Property(typeof(MerchantRelicEntry), nameof(MerchantRelicEntry.Model))
            .SetValue(entry, ModelDb.Relic<HeftyTablet>().ToMutable());
        var host = Host(run, player, room); RunView view = Get(host.WaitForBoundaryAsync(default)).View;
        int deck = player.Deck.Cards.Count; bool rejected = false;
        try { Wait(host.ExecuteAsync(Pick(view, Row(view, room, player, entry)), NoChild, default).AsTask()); }
        catch (BlitzSolver.Simulation.Combat.UnsupportedException e) when (e.Message.Contains("FromChooseACardScreen", StringComparison.Ordinal)) { rejected = true; }
        Check(rejected && player.Deck.Cards.Count == deck, "unsupported relic callback rejected before native automatic choice");
        Wait(host.DisposeAsync().AsTask());
        Check(CardSelectCmd.Selector == null && !OfflineRunHost.IsShopRemoval, "unsupported shop releases selector/purpose scope");
    }
    private sealed class DirectSelector(bool cancel) : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards, int min, int max)
            => Task.FromResult(cancel ? Enumerable.Empty<CardModel>() : cards.Skip(1).Take(1));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult> cards,
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives)
            => throw new InvalidDataException("unexpected-reward");
    }
}
