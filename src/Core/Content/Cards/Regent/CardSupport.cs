using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Cards.Regent;

/// <summary>
/// Base of the cards whose effect forges: the rule closure must freeze the canonical Sovereign Blade
/// stats and admission must check its definition, because the effect can create the card at runtime
/// (<c>PersistentPowerSupport.Forge</c>).
/// </summary>
public abstract class ForgeCardDef : CardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [(Kernel.SovereignBladeId, 0)];
}

/// <summary>Shared Regent helpers.</summary>
internal static class RegentContent
{
    private const string SeekingEdgePowerId = "SEEKING_EDGE_POWER";
    private const string ParryPowerId = "PARRY_POWER";

    /// <summary><c>GetOwnerParryAmount</c>: the owner's ParryPower amount, 0 while it is absent.</summary>
    public static int ParryAmount(in Battle battle)
        => Registry.TryPower(ParryPowerId, out ushort power)
            ? Kernel.GetAmount(battle, Limits.PlayerOwner, power)
            : 0;

    /// <summary><c>SovereignBlade.HasSeekingEdge</c>: the owner holds SeekingEdgePower.</summary>
    public static bool HasSeekingEdge(in Battle battle)
        => Registry.TryPower(SeekingEdgePowerId, out ushort power)
            && Kernel.GetAmount(battle, Limits.PlayerOwner, power) > 0;
}
