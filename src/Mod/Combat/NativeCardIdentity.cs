using Sts2Solver.Simulation.Combat;
using MegaCrit.Sts2.Core.Models;

namespace Sts2Solver;

/// <summary>
/// The deployment identity of a card: native id plus current upgrade level. Choice matching also
/// verifies the request's source pile; equal ids with different upgrades are not interchangeable
/// for selection effects.
/// </summary>
internal readonly record struct NativeCardIdentity(string Id, int Upgrade, string? Enchantment,
    string? Affliction)
{
    internal static NativeCardIdentity From(Card card)
        => new(
            Registry.Card(card.Id).NativeId,
            card.Upgrade,
            card.EnchantmentId == 0
                ? null
                : Registry.Enchantment((ushort)(card.EnchantmentId - 1)).NativeId,
            card.AfflictionId == 0
                ? null
                : Registry.Affliction((ushort)(card.AfflictionId - 1)).NativeId);

    internal static NativeCardIdentity From(CardModel card)
        => new(card.Id.Entry, card.CurrentUpgradeLevel, card.Enchantment?.Id.Entry,
            card.Affliction?.Id.Entry);

    public override string ToString()
        => $"{Id}+{Upgrade}" +
            (Enchantment is null ? "" : $"/ench={Enchantment}") +
            (Affliction is null ? "" : $"/aff={Affliction}");
}
