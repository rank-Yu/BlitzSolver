using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

/// <summary>
/// <c>Shiv</c>: 0-cost attack that exhausts. Its native <c>TargetType</c> is AllEnemies while the
/// owner holds FanOfKnivesPower and AnyEnemy otherwise (Shiv.cs), so the rule-table target is
/// overridden dynamically.
/// </summary>
[FlashContent("SHIV")]
public sealed class Shiv : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override FlashTargetType TargetType(in FlashBattle battle, FlashRules rules, in FlashCard card)
        => SilentContent.HasFanOfKnives(battle) ? FlashTargetType.AllEnemies
            : rules.Card(card.Id, card.Upgrade).Target;

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}
