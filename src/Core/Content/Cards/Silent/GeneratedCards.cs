using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Content.Cards.Silent;

/// <summary>
/// <c>Shiv</c>: 0-cost attack that exhausts. Its native <c>TargetType</c> is AllEnemies while the
/// owner holds FanOfKnivesPower and AnyEnemy otherwise (Shiv.cs), so the rule-table target is
/// overridden dynamically.
/// </summary>
[ContentId("SHIV")]
public sealed class Shiv : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override TargetType ResolveTargetType(in Battle battle, Rules rules, in Card card)
        => SilentContent.HasFanOfKnives(battle) ? TargetType.AllEnemies
            : rules.Card(card.Id, card.Upgrade).Target;

    public override void OnPlay(ref Exec x) => CardEffects.GeneralAttack(ref x, 0);
}
