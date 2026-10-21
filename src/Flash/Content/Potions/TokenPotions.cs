using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Potions;

[FlashContent("POTION_SHAPED_ROCK")]
public sealed class PotionShapedRock : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // PotionShapedRock when target != null: Damage(target, Damage, Damage.Props, owner).
    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = x.Target;
        FlashKernel.Damage(ref x, targets, FlashKernel.PotionVar(x, this, 0),
            FlashKernel.PotionVarProps(x, this, 0), FlashLimits.PlayerOwner, hasCardSource: false, default);
    }
}
