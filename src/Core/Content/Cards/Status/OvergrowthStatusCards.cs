using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Status;

/// <summary>
/// Native <c>Slimed</c>: cost 1, Exhaust; drawing one card. The existing engine runs the inferred
/// <c>Cards</c> draw recipe (<c>CardDrawCardMirrors</c> / general draw), so Blitz draws after the
/// normal mirrored/compensated split with no other effects.
/// </summary>
[ContentId("SLIMED")]
public sealed class Slimed : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref Exec x)
        => Kernel.Draw(ref x, CardEffects.Var(x, 0), fromHandDraw: false);
}
