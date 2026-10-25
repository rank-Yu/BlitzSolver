using BlitzSolver.Engine;

namespace BlitzSolver.Content.Potions;

[ContentId("POTION_SHAPED_ROCK")]
public sealed class PotionShapedRock : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // PotionShapedRock when target != null: Damage(target, Damage, Damage.Props, owner).
    public override void OnUse(ref Exec x)
    {
        if (x.Target == Limits.NoOwner)
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = x.Target;
        Kernel.Damage(ref x, targets, Kernel.PotionVar(x, this, 0),
            Kernel.PotionVarProps(x, this, 0), Limits.PlayerOwner, hasCardSource: false, default);
    }
}
