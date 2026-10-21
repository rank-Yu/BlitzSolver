using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Potions;

/// <summary>
/// <c>EssenceOfDarkness</c>: channel one Dark orb per orb slot
/// (<c>OrbPotionMirrors.EssenceOfDarknessOnUse</c>). The count is the current queue capacity.
/// </summary>
[FlashContent("ESSENCE_OF_DARKNESS")]
public sealed class EssenceOfDarkness : FlashPotionDef
{
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("DARK_ORB"), x.B.OrbCapacity);
    }
}
