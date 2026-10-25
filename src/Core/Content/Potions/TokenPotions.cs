using BlitzSolver.Engine;

namespace BlitzSolver.Content.Potions;

[BlitzContent("POTION_SHAPED_ROCK")]
public sealed class PotionShapedRock : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // PotionShapedRock when target != null: Damage(target, Damage, Damage.Props, owner).
    public override void OnUse(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = x.Target;
        BlitzKernel.Damage(ref x, targets, BlitzKernel.PotionVar(x, this, 0),
            BlitzKernel.PotionVarProps(x, this, 0), BlitzLimits.PlayerOwner, hasCardSource: false, default);
    }
}
