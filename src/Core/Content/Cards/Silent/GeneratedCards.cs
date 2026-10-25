using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

/// <summary>
/// <c>Shiv</c>: 0-cost attack that exhausts. Its native <c>TargetType</c> is AllEnemies while the
/// owner holds FanOfKnivesPower and AnyEnemy otherwise (Shiv.cs), so the rule-table target is
/// overridden dynamically.
/// </summary>
[BlitzContent("SHIV")]
public sealed class Shiv : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override BlitzTargetType TargetType(in BlitzBattle battle, BlitzRules rules, in BlitzCard card)
        => SilentContent.HasFanOfKnives(battle) ? BlitzTargetType.AllEnemies
            : rules.Card(card.Id, card.Upgrade).Target;

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}
