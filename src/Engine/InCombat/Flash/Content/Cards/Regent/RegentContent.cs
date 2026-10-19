namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Regent;

/// <summary>
/// Base of the cards whose effect forges: the rule closure must freeze the canonical Sovereign Blade
/// stats and admission must check its definition, because the effect can create the card at runtime
/// (<c>PersistentPowerSupport.Forge</c>).
/// </summary>
public abstract class ForgeCardDef : FlashCardDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [(FlashKernel.SovereignBladeId, 0)];
}

/// <summary>Shared Regent helpers.</summary>
internal static class RegentContent
{
    private const string SeekingEdgePowerId = "SEEKING_EDGE_POWER";
    private const string ParryPowerId = "PARRY_POWER";

    /// <summary><c>GetOwnerParryAmount</c>: the owner's ParryPower amount, 0 while it is absent.</summary>
    public static int ParryAmount(in FlashBattle battle)
        => FlashRegistry.TryPower(ParryPowerId, out ushort power)
            ? FlashKernel.GetAmount(battle, FlashLimits.PlayerOwner, power)
            : 0;

    /// <summary><c>SovereignBlade.HasSeekingEdge</c>: the owner holds SeekingEdgePower.</summary>
    public static bool HasSeekingEdge(in FlashBattle battle)
        => FlashRegistry.TryPower(SeekingEdgePowerId, out ushort power)
            && FlashKernel.GetAmount(battle, FlashLimits.PlayerOwner, power) > 0;
}
