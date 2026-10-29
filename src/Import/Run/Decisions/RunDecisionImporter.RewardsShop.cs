// Native semantics cross-checked against sts2 v0.111.0 RewardsSetSynchronizer,
// CardReward.OnSelect, PotionCmd, MerchantEntry and NMerchantInventory. Read only.
using System.Collections;
using System.Reflection;
using Sts2Solver.Simulation.Combat;
using Sts2Solver.Simulation.Run;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Solver.Import;

public static partial class RunDecisionImporter
{
    private static readonly FieldInfo RewardStates = RewardField(typeof(RewardsSetSynchronizer), "_rewardStates");
    private static readonly FieldInfo RewardGenerated = RewardField(typeof(RewardsSet), "_isGenerated");
    private static readonly FieldInfo RewardCards = RewardField(typeof(CardReward), "_cards");

    /// <summary>Actual visible reward buttons, including failed/skipped card or full-potion attempts.
    /// The host must provide live UI/callback state, not a cached remaining list. It owns terminal
    /// Proceed versus nonterminal Skip dispatch; importing never begins or generates the set.</summary>
    public static NativeDecisionSnapshot<object> Rewards(RewardsSet set, Func<IReadOnlyList<Reward>> visible,
        Func<bool> active, Func<bool> canLeave, RunDecisionToken token)
    {
        CheckPlayer(set.Player);
        if (!active()) throw new UnsupportedException("decision-rewards: no pending screen");
        CheckRewardSet(set, allowCompleted: true);
        Reward[] all = set.Rewards.ToArray();
        Reward[] rows = visible().ToArray();
        int setId = set.Id;
        bool leave = canLeave();
        if (RewardSetCompleted(set) && (set.Room is not CombatRoom || rows.Length != 0 || !leave))
            throw new UnsupportedException("decision-rewards: completed set has no pending reward rows");
        if (set.DisallowSkipping && rows.Length != 0) leave = false;
        if (leave && !Hook.ShouldProceedToNextMapPoint(set.Player.RunState)) leave = false;
        RunDecisionRequest request = New(RunDecision.CombatReward, token);
        List<object> bindings = [];
        List<object?> models = [];
        foreach (Reward reward in rows)
        {
            CheckReward(set, reward);
            RunDecisionOption row = new() { OptionId = Next(request), Kind = RunDecisionOptionKind.Reward,
                PayloadKind = RunDecisionPayloadKind.Reward };
            object? model = null;
            switch (reward)
            {
                case GoldReward gold:
                    if (gold.Amount < 0) throw new UnsupportedException("decision-rewards: negative gold amount");
                    row.RewardType = RunRewardType.Gold;
                    row.EffectsKnowledge = RunDecisionKnowledge.Complete;
                    if (gold.Amount > 0) row.Effects = RunDecisionBuilder.AddFacts(ref request,
                        [new() { Kind = RunDecisionFactKind.GoldGain, Amount = gold.Amount }]);
                    break;
                case RelicReward relic:
                    row.RewardType = RunRewardType.Relic; model = relic.Relic;
                    row.Model = Model(relic.Relic!); break;
                case PotionReward potion:
                    row.RewardType = RunRewardType.Potion; model = potion.Potion;
                    row.Model = Potion(potion.Potion!); break;
                case CardReward _:
                    // The container is visible; unopened internal cards are not.
                    row.RewardType = RunRewardType.Card; break;
                default: throw new UnsupportedException("decision-rewards: unsupported reward " + reward.GetType().Name);
            }
            RunDecisionBuilder.AddOption(ref request, row); bindings.Add(reward); models.Add(model);
        }
        if (leave)
        {
            RunDecisionBuilder.AddOption(ref request, new() { OptionId = Next(request), Kind = RunDecisionOptionKind.Leave });
            bindings.Add(set);
        }
        if (bindings.Count == 0) throw new UnsupportedException("decision-rewards: no pending choice; host must finish screen");
        AbstractRoom? room = set.Player.RunState.CurrentRoom;
        return new(request, bindings.ToArray(), () => Rewards(set, visible, active, canLeave, token),
            () => set.Id == setId && ReferenceEquals(room, set.Player.RunState.CurrentRoom) && SameReferences(all, set.Rewards)
                && rows.Select(RewardModel).SequenceEqual(models, ReferenceEqualityComparer.Instance));
    }

    /// <summary>Only from CardReward.OnSelect's actual callback (or its visible UI collection).
    /// Alternatives are supplied by native Generate; the importer must never generate them.</summary>
    public static NativeDecisionSnapshot<object> CardReward(CardReward reward,
        IReadOnlyList<CardCreationResult> cards, IReadOnlyList<CardRewardAlternative> alternatives,
        Func<bool> active, RunDecisionToken token)
    {
        CheckPlayer(reward.Player);
        if (!active()) throw new UnsupportedException("decision-card-reward: no pending callback");
        RewardsSet parent = CheckRewardInTopSet(reward);
        int parentId = parent.Id;
        if (!ReferenceEquals(RewardCards.GetValue(reward), cards))
            throw new UnsupportedException("decision-card-reward: candidates are not the actual native collection");
        RunDecisionRequest request = New(RunDecision.CardReward, token);
        List<object> bindings = [];
        foreach (CardCreationResult result in cards)
        {
            RunView.CardView view = Card(result.Card);
            if (!ReferenceEquals(result.Card.Owner, reward.Player))
                throw new UnsupportedException("decision-card-reward: wrong card owner");
            RunDecisionBuilder.AddOption(ref request, new() { OptionId = Next(request), Kind = RunDecisionOptionKind.Card,
                PayloadKind = RunDecisionPayloadKind.Card, Card = view });
            bindings.Add(result.Card);
        }
        foreach (CardRewardAlternative alternative in alternatives)
        {
            NativeAccess.RequireOriginalType(alternative.GetType());
            RunDecisionOptionKind kind = alternative.OptionId switch
            {
                "Skip" when alternative.AfterSelected == PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward
                    && reward.CanSkip => RunDecisionOptionKind.Skip,
                "REROLL" when alternative.AfterSelected == PostAlternateCardRewardAction.DoNothing => RunDecisionOptionKind.Reroll,
                _ => throw new UnsupportedException("decision-card-reward: unsupported alternative " + alternative.OptionId),
            };
            // OnSelect's native while loop retains its original alternatives after Reroll.
            // The displayed screen refresh removes REROLL. Do not offer the stale callback alternative.
            if (kind == RunDecisionOptionKind.Reroll && !reward.CanReroll) continue;
            RunDecisionBuilder.AddOption(ref request, new() { OptionId = Next(request), Kind = kind,
                NativeKey = RunDecisionBuilder.AddKey(ref request, alternative.OptionId) });
            bindings.Add(alternative);
        }
        AbstractRoom? room = reward.Player.RunState.CurrentRoom;
        bool skip = reward.CanSkip, reroll = reward.CanReroll;
        CardRewardAlternative[] originalAlternatives = alternatives.ToArray();
        return new(request, bindings.ToArray(), () => CardReward(reward, cards, alternatives, active, token),
            () => parent.Id == parentId && ReferenceEquals(parent, TopRewardSet(reward.Player))
                && ReferenceEquals(room, reward.Player.RunState.CurrentRoom) && skip == reward.CanSkip && reroll == reward.CanReroll
                && SameReferences(originalAlternatives, alternatives));
    }

    /// <summary>Explicit host potion-management step after native TooFull. Native reward UI does
    /// not open a discard screen or retry automatically. Skip returns to the unchanged reward list;
    /// selecting a potion only discards that instance, and claiming the reward needs a fresh action.</summary>
    public static NativeDecisionSnapshot<object> PotionDiscard(PotionReward reward, Func<bool> active, RunDecisionToken token)
    {
        CheckPlayer(reward.Player);
        if (!active()) throw new UnsupportedException("decision-potion-discard: no pending management step");
        RewardsSet parent = CheckRewardInTopSet(reward);
        int parentId = parent.Id;
        Player player = reward.Player;
        if (reward.Potion == null) throw new UnsupportedException("decision-potion-discard: missing source potion");
        _ = Potion(reward.Potion);
        if (player.Creature.IsDead || !Hook.ShouldProcurePotion(player.RunState, player.Creature.CombatState, reward.Potion, player))
            throw new UnsupportedException("decision-potion-discard: reward is not procurable");
        PotionModel?[] slots = Enumerable.Range(0, player.MaxPotionCount).Select(player.GetPotionAtSlotIndex).ToArray();
        if (slots.Length == 0 || slots.Any(p => p == null))
            throw new UnsupportedException("decision-potion-discard: potions are not full");
        RunDecisionRequest request = New(RunDecision.PotionDiscard, token);
        List<object> bindings = [];
        foreach (PotionModel? potion in slots)
        {
            RunRef model = Potion(potion!);
            if (!ReferenceEquals(potion!.Owner, player))
                throw new UnsupportedException("decision-potion-discard: wrong potion owner");
            RunDecisionBuilder.AddOption(ref request, new() { OptionId = Next(request), Kind = RunDecisionOptionKind.Potion,
                PayloadKind = RunDecisionPayloadKind.Model, Model = model,
                Flags = potion.IsQueued ? RunDecisionOptionFlags.Disabled : RunDecisionOptionFlags.None });
            bindings.Add(potion!);
        }
        RunDecisionBuilder.AddOption(ref request, new() { OptionId = Next(request), Kind = RunDecisionOptionKind.Skip });
        bindings.Add(reward);
        AbstractRoom? room = player.RunState.CurrentRoom;
        PotionModel source = reward.Potion;
        return new(request, bindings.ToArray(), () => PotionDiscard(reward, active, token),
            () => parent.Id == parentId && ReferenceEquals(parent, TopRewardSet(player))
                && ReferenceEquals(room, player.RunState.CurrentRoom) && ReferenceEquals(source, reward.Potion)
                && slots.Length == player.MaxPotionCount && slots.Where((p, i) => !ReferenceEquals(p, player.GetPotionAtSlotIndex(i))).Count() == 0);
    }

    /// <summary>Current local merchant inventory while its screen is open. Stockless item slots
    /// are hidden by native UI; the used removal placard remains visible and disabled. Prices are
    /// the already-calculated Entry.Cost (including hooks), never CalcCost.</summary>
    public static NativeDecisionSnapshot<object> Shop(MerchantRoom room, Player player, Func<bool> active,
        Func<bool> canLeave, RunDecisionToken token)
    {
        CheckPlayer(player);
        if (!active() || !ReferenceEquals(room, player.RunState.CurrentRoom))
            throw new UnsupportedException("decision-shop: no pending current merchant screen");
        MerchantInventory inventory = room.GetLocalInventory();
        if (!ReferenceEquals(inventory.Player, player)) throw new UnsupportedException("decision-shop: wrong inventory owner");
        RunDecisionRequest request = New(RunDecision.Shop, token);
        MerchantEntry[] entries = inventory.AllEntries.ToArray();
        foreach (MerchantEntry entry in entries) NativeAccess.RequireOriginalType(entry.GetType());
        object?[] models = entries.Select(EntryModel).ToArray();
        List<object> bindings = [];
        foreach (MerchantEntry entry in entries)
        {
            NativeAccess.RequireOriginalType(entry.GetType());
            if (!entry.IsStocked && entry is not MerchantCardRemovalEntry) continue;
            int price = entry.Cost;
            if (price < 0) throw new UnsupportedException("decision-shop: negative price");
            RunDecisionOption row = new() { OptionId = Next(request), Kind = RunDecisionOptionKind.ShopEntry,
                CostsKnowledge = RunDecisionKnowledge.Complete };
            bool enabled = entry.IsStocked && price <= player.Gold;
            switch (entry)
            {
                case MerchantCardEntry card:
                    CardModel model = card.CreationResult!.Card;
                    row.PayloadKind = RunDecisionPayloadKind.Card; row.Card = Card(model);
                    enabled &= Hook.ShouldAddToDeck(player.RunState, model, out _); break;
                case MerchantRelicEntry relic:
                    row.PayloadKind = RunDecisionPayloadKind.Model; row.Model = Model(relic.Model!); break;
                case MerchantPotionEntry potion:
                    row.PayloadKind = RunDecisionPayloadKind.Model; row.Model = Potion(potion.Model!);
                    enabled &= Enumerable.Range(0, player.MaxPotionCount).Any(i => player.GetPotionAtSlotIndex(i) == null)
                        && Hook.ShouldProcurePotion(player.RunState, player.Creature.CombatState, potion.Model!, player); break;
                case MerchantCardRemovalEntry:
                    row.Kind = RunDecisionOptionKind.CardRemoval;
                    enabled &= Hook.ShouldAllowMerchantCardRemoval(player.RunState, player) && player.Deck.Cards.Any(c => c.IsRemovable);
                    break;
                default: throw new UnsupportedException("decision-shop: unsupported entry " + entry.GetType().Name);
            }
            if (!enabled) row.Flags = RunDecisionOptionFlags.Disabled;
            if (price > 0) row.Costs = RunDecisionBuilder.AddFacts(ref request,
                [new() { Kind = RunDecisionFactKind.GoldLoss, Amount = price }]);
            RunDecisionBuilder.AddOption(ref request, row); bindings.Add(entry);
        }
        if (canLeave())
        {
            RunDecisionBuilder.AddOption(ref request, new() { OptionId = Next(request), Kind = RunDecisionOptionKind.Leave });
            bindings.Add(inventory);
        }
        return new(request, bindings.ToArray(), () => Shop(room, player, active, canLeave, token),
            () => ReferenceEquals(room, player.RunState.CurrentRoom) && ReferenceEquals(inventory, room.GetLocalInventory())
                && SameReferences(entries, inventory.AllEntries.ToArray())
                && entries.Select(EntryModel).SequenceEqual(models, ReferenceEqualityComparer.Instance));
    }

    private static object? RewardModel(Reward reward) => reward switch
        { RelicReward relic => relic.Relic, PotionReward potion => potion.Potion, _ => null };
    private static object? EntryModel(MerchantEntry entry) => entry switch
        { MerchantCardEntry card => card.CreationResult?.Card, MerchantRelicEntry relic => relic.Model,
          MerchantPotionEntry potion => potion.Model, _ => null };
    private static bool SameReferences<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) where T : class
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }
    private static RunRef Potion(PotionModel potion)
    {
        NativeAccess.RequireOriginal(potion);
        if (!Registry.TryPotion(potion.Id.Entry, out ushort id))
            throw new UnsupportedException("decision-potion: unknown potion " + potion.Id.Entry);
        return new(RunRefKind.Potion, id);
    }
    private static FieldInfo RewardField(Type type, string name)
        => type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new UnsupportedException("decision-rewards: missing native field " + name);
    private static object? RewardState(Player player)
    {
        IList states = (IList)RewardStates.GetValue(RunManager.Instance.RewardsSetSynchronizer)!;
        return states.Count == 0 ? null : states[0];
    }
    private static bool RewardSetCompleted(RewardsSet set)
    {
        object? state = RewardState(set.Player);
        return state != null && set.Id >= 0
            && ((IDictionary)RewardField(state.GetType(), "completedRewards").GetValue(state)!).Contains(set.Id);
    }
    private static RewardsSet? TopRewardSet(Player player)
    {
        // Do not call GetRewardStateForPlayer: that apparently read-only method can grow the list.
        object? state = RewardState(player);
        if (state == null) return null;
        IList stack = (IList)RewardField(state.GetType(), "rewardsStack").GetValue(state)!;
        if (stack.Count == 0) return null;
        object top = stack[stack.Count - 1]!;
        return (RewardsSet)RewardField(top.GetType(), "set").GetValue(top)!;
    }
    private static void CheckRewardSet(RewardsSet set, bool allowCompleted = false)
    {
        NativeAccess.RequireOriginalType(set.GetType());
        if (!(bool)RewardGenerated.GetValue(set)! || set.Id < 0
            || !ReferenceEquals(TopRewardSet(set.Player), set)
                && !(allowCompleted && RewardSetCompleted(set)))
            throw new UnsupportedException("decision-rewards: set is not generated and pending on native stack");
        if (set.Room != null && !ReferenceEquals(set.Room, set.Player.RunState.CurrentRoom))
            throw new UnsupportedException("decision-rewards: source room is not current");
    }
    private static void CheckReward(RewardsSet set, Reward reward)
    {
        NativeAccess.RequireOriginalType(reward.GetType());
        if (!ReferenceEquals(reward.Player, set.Player) || !set.Rewards.Any(r => ReferenceEquals(r, reward))
            || !reward.IsPopulated || reward.SuccessfullySelected || reward.ParentRewardSet != null)
            throw new UnsupportedException("decision-rewards: reward is not a pending visible member");
    }
    private static RewardsSet CheckRewardInTopSet(Reward reward)
    {
        RewardsSet set = TopRewardSet(reward.Player)
            ?? throw new UnsupportedException("decision-rewards: no pending native set");
        CheckRewardSet(set); CheckReward(set, reward);
        return set;
    }
}
