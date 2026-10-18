namespace CombatSolver.Engine.InCombat.Flash.Content.Relics;

/// <summary>
/// <c>MysticLighter</c>: powered attacks from an enchanted card of its owner gain the relic's
/// <c>Damage</c> (9). Mirrors <c>MysticLighter.ModifyDamageAdditive</c>, which the existing engine
/// runs as a verified native relic hook (<c>PredictionCoverage.IsVerifiedNativeRelicHook</c>).
/// </summary>
[FlashContent("MYSTIC_LIGHTER")]
public sealed class MysticLighter : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self, in FlashDamageContext damage,
        decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource || damage.CardSource.EnchantmentId == 0)
            return 0m;
        return x.R.RelicVar(x.B.Relics[self.Index].Id, 0);
    }
}
