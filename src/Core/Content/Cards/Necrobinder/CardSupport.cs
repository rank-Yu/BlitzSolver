using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Necrobinder;

/// <summary>
/// Shared Necrobinder card helpers. The pet rules are the existing engine's
/// <c>SimulatedCombatState.SummonOsty</c>/<c>HealOsty</c> and <c>CombatPredictionSimulator</c>'s attack
/// pipeline; card conditionals are copied from the named mirror or compensation path.
/// </summary>
internal static class NecrobinderContent
{
    /// <summary><c>Osty.CheckMissingWithAnim</c> as a state query: the pet exists and is alive.</summary>
    public static bool HasOsty(in Battle battle)
        => (battle.Flags & BattleFlags.OstyPresent) != 0 && battle.Osty.IsAlive;

    /// <summary>
    /// The Osty proxy attack: the native <c>DamageCmd.Attack(...).FromOsty(osty, card, cardPlay)</c> path
    /// (<c>GeneralCardMirrors.GeneralAttackOnPlay</c>), which returns without attacking while the pet is
    /// missing. The rule table's target type picks single/all/random targeting and the pet is the dealer, so
    /// its Strength/Weak apply and the attack history is recorded for the pet.
    /// </summary>
    public static void OstyAttack(ref Exec x, decimal damage, int hitCount = 1)
    {
        if (!HasOsty(x.B))
            return;
        TargetType targetType = Registry.Card(x.Card.Id).ResolveTargetType(x.B, x.R, x.Card);
        AttackTargeting targeting = targetType switch
        {
            TargetType.AnyEnemy => AttackTargeting.Single,
            TargetType.AllEnemies => AttackTargeting.AllOpponents,
            TargetType.RandomEnemy => AttackTargeting.Random,
            _ => throw new UnsupportedException($"Osty 攻击目标类型 {targetType} 尚未实现。"),
        };
        Kernel.CardAttack(ref x, damage, hitCount, targeting, x.Target, ValueProps.Move,
            Limits.OstyOwner);
    }

    /// <summary>The i-th canonical variable of the current card.</summary>
    public static decimal Var(ref Exec x, int index) => CardEffects.Var(x, index);

    /// <summary><c>Soul.Create(owner, count, combatState)</c>'s canonical token card, upgraded by the source card.</summary>
    public static Card CreateSoul(in Battle battle, Rules rules, bool upgraded)
    {
        if (!Registry.TryCard("SOUL", out ushort soul))
            throw new UnsupportedException("灵魂牌没有 Blitz 定义。");
        return Kernel.CreateCard(rules, soul, upgraded ? 1 : 0);
    }

    /// <summary>Number of cards in every pile whose canonical tags include Osty attack (Squeeze).</summary>
    public static int CountOstyAttackCards(in Battle battle, Rules rules)
    {
        int count = 0;
        for (int position = 0; position < battle.CardCount; position++)
        {
            ref readonly Card card = ref battle.Cards[position];
            if ((rules.Card(card.Id, card.Upgrade).Tags & CardTag.OstyAttack) != 0)
                count++;
        }
        return count;
    }

    /// <summary><c>(CalculatedVar).Calculate</c>: CalculationBase + CalculationExtra/ExtraDamage × multiplier.</summary>
    public static decimal Calculated(ref Exec x, int baseVar, int extraVar, decimal multiplier)
        => Var(ref x, baseVar) + Var(ref x, extraVar) * multiplier;
}
