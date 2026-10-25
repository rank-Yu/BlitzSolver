using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Ironclad;

[ContentId("CORRUPTION")]
public sealed class Corruption : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.CorruptionPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}
