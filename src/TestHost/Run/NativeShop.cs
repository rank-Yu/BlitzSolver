// Native merchant driver. It reads the public MerchantInventory entries, enumerates the same
// semantic legal options the Core shop decision exposes, and executes purchases through
// MerchantEntry.OnTryPurchaseWrapper. Card removal uses the test ICardSelector; the UI-only
// OnCardRemovalUsed callback is reproduced with the public SetUsed() after a successful removal.
using System.Linq;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.TestSupport;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;

namespace BlitzSolver.TestHost;

public enum NativeShopChoiceKind
{
    Card,
    Relic,
    Potion,
    CardRemoval,
    Leave,
}

public enum NativeShopStage
{
    Shop,
    CardRemoval,
}

public sealed class NativeShopChoice
{
    public required NativeShopChoiceKind Kind { get; init; }

    /// <summary>Same global entry index the Core request uses (0..4 character cards, 5..6
    /// colorless, 7..9 relics, 10..12 potions, 13 removal).</summary>
    public required int Index { get; init; }

    public int Slot { get; init; }

    public required int Price { get; init; }

    public MerchantEntry? Entry { get; init; }

    public CardModel? Card { get; init; }

    public RelicModel? Relic { get; init; }

    public PotionModel? Potion { get; init; }
}

/// <summary>One live merchant inventory plus the legal-action enumeration.</summary>
public sealed class NativeShopSession
{
    private readonly NativeRunState _run;
    private readonly Player _player;
    private readonly MerchantRoom _room;
    private readonly MerchantInventory _inventory;

    private NativeShopSession(NativeRunState run, Player player, MerchantRoom room,
        MerchantInventory inventory)
    {
        _run = run;
        _player = player;
        _room = room;
        _inventory = inventory;
    }

    public static NativeShopSession? TryCreate(NativeRunState run, Player player)
    {
        if (run.CurrentRoom is not MerchantRoom room || room.Inventories.Count == 0)
            return null;
        return new NativeShopSession(run, player, room, room.Inventories[0]);
    }

    public NativeShopStage Stage { get; private set; } = NativeShopStage.Shop;

    public MerchantInventory Inventory => _inventory;

    public IReadOnlyList<NativeShopChoice> Choices()
    {
        if (Stage == NativeShopStage.CardRemoval)
            return [];
        List<NativeShopChoice> choices = [];
        for (int slot = 0; slot < _inventory.CharacterCardEntries.Count; slot++)
        {
            NativeShopChoice? choice = CardChoice(_inventory.CharacterCardEntries[slot],
                slot, slot);
            if (choice != null)
                choices.Add(choice);
        }
        for (int slot = 0; slot < _inventory.ColorlessCardEntries.Count; slot++)
        {
            NativeShopChoice? choice = CardChoice(_inventory.ColorlessCardEntries[slot],
                _inventory.CharacterCardEntries.Count + slot, slot);
            if (choice != null)
                choices.Add(choice);
        }
        for (int slot = 0; slot < _inventory.RelicEntries.Count; slot++)
        {
            MerchantRelicEntry entry = _inventory.RelicEntries[slot];
            if (!entry.IsStocked || !entry.EnoughGold)
                continue;
            choices.Add(new NativeShopChoice
            {
                Kind = NativeShopChoiceKind.Relic,
                Index = RunShopGenerator.RelicBase + slot,
                Slot = slot,
                Price = entry.Cost,
                Entry = entry,
                Relic = entry.Model,
            });
        }
        bool canProcure = CanProcurePotion();
        for (int slot = 0; slot < _inventory.PotionEntries.Count; slot++)
        {
            MerchantPotionEntry entry = _inventory.PotionEntries[slot];
            if (!entry.IsStocked || !entry.EnoughGold || !canProcure)
                continue;
            choices.Add(new NativeShopChoice
            {
                Kind = NativeShopChoiceKind.Potion,
                Index = RunShopGenerator.PotionBase + slot,
                Slot = slot,
                Price = entry.Cost,
                Entry = entry,
                Potion = entry.Model,
            });
        }
        MerchantCardRemovalEntry removal = _inventory.CardRemovalEntry
            ?? throw new InvalidDataException("商人库存没有删牌条目。");
        if (!removal.Used && removal.EnoughGold && HasRemovableCard())
        {
            choices.Add(new NativeShopChoice
            {
                Kind = NativeShopChoiceKind.CardRemoval,
                Index = RunShopGenerator.RemovalIndex,
                Price = removal.Cost,
                Entry = removal,
            });
        }
        choices.Add(new NativeShopChoice
        {
            Kind = NativeShopChoiceKind.Leave,
            Index = choices.Count,
            Price = 0,
        });
        return choices;
    }

    private NativeShopChoice? CardChoice(MerchantCardEntry entry, int index, int slot)
    {
        if (!entry.IsStocked || !entry.EnoughGold || entry.CreationResult is not { } result)
            return null;
        return new NativeShopChoice
        {
            Kind = NativeShopChoiceKind.Card,
            Index = index,
            Slot = slot,
            Price = entry.Cost,
            Entry = entry,
            Card = result.Card,
        };
    }

    public IReadOnlyList<CardModel> RemovalChoices()
    {
        List<CardModel> deck = [.. _player.Deck.Cards];
        List<CardModel> result = [];
        foreach (CardModel card in deck)
        {
            if (card.IsRemovable && card.Type == CardType.Curse)
                result.Add(card);
        }
        foreach (CardModel card in deck)
        {
            if (card.IsRemovable && card.Type != CardType.Curse)
                result.Add(card);
        }
        return result;
    }

    public void EnterRemoval()
    {
        if (Stage != NativeShopStage.Shop)
            throw new InvalidOperationException("当前不是商店界面。");
        Stage = NativeShopStage.CardRemoval;
    }

    public async Task<bool> BuyAsync(NativeShopChoice choice)
    {
        if (choice.Entry == null)
            throw new InvalidOperationException("商店选项没有条目。");
        bool success;
        if (choice.Kind == NativeShopChoiceKind.CardRemoval)
        {
            MerchantCardRemovalEntry removal = (MerchantCardRemovalEntry)choice.Entry;
            using (CardSelectCmd.PushSelector(new RemovalSelector(null), localOnly: false))
            {
                success = await removal.OnTryPurchaseWrapper(_inventory, ignoreCost: false,
                    cancelable: true);
            }
            if (success)
                removal.SetUsed();
            Stage = NativeShopStage.Shop;
            return success;
        }
        success = await choice.Entry.OnTryPurchaseWrapper(_inventory);
        return success;
    }

    public async Task<bool> RemoveAsync(CardModel? card)
    {
        if (Stage != NativeShopStage.CardRemoval)
            throw new InvalidOperationException("当前不是删牌选卡。");
        MerchantCardRemovalEntry removal = _inventory.CardRemovalEntry
            ?? throw new InvalidDataException("商人库存没有删牌条目。");
        bool success;
        using (CardSelectCmd.PushSelector(new RemovalSelector(card), localOnly: false))
        {
            success = await removal.OnTryPurchaseWrapper(_inventory, ignoreCost: false,
                cancelable: true);
        }
        if (success)
            removal.SetUsed();
        Stage = NativeShopStage.Shop;
        return success;
    }

    public void Leave()
    {
        if (Stage != NativeShopStage.Shop)
            throw new InvalidOperationException("当前不是商店界面。");
        if (!ReferenceEquals(_run.CurrentRoom, _room))
            throw new InvalidDataException("当前房间不是商人房间。");
        _run.PopCurrentRoom();
        _room.Exit(_run);
    }

    private bool HasRemovableCard() => _player.Deck.Cards.Any(static card => card.IsRemovable);

    private bool CanProcurePotion()
    {
        for (int slot = 0; slot < _player.MaxPotionCount; slot++)
        {
            if (_player.GetPotionAtSlotIndex(slot) == null)
            {
                return _inventory.PotionEntries.Any(entry => entry.Model != null
                    && Hook.ShouldProcurePotion(_run, _player.Creature.CombatState,
                        entry.Model, _player));
            }
        }
        return false;
    }

    private sealed class RemovalSelector(CardModel? selected) : ICardSelector
    {
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<CardRewardAlternative> alternatives)
            => throw new InvalidOperationException("删牌选择器收到了卡牌奖励请求。");

        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,
            int minSelect, int maxSelect)
        {
            List<CardModel> list = [.. options];
            if (selected == null)
                return Task.FromResult(Enumerable.Empty<CardModel>());
            CardModel? match = list.FirstOrDefault(card =>
                ReferenceEquals(card, selected)
                || (card.Id == selected.Id && card.CurrentUpgradeLevel == selected.CurrentUpgradeLevel));
            if (match == null)
                throw new InvalidDataException($"删牌选项没有 {selected.Id.Entry}。");
            return Task.FromResult<IEnumerable<CardModel>>([match]);
        }
    }
}
