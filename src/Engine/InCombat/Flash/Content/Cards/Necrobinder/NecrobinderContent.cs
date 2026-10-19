namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Necrobinder;

/// <summary>
/// Shared Necrobinder card helpers. The pet rules are the existing engine's
/// <c>SimulatedCombatState.SummonOsty</c>/<c>HealOsty</c> and <c>CombatPredictionSimulator</c>'s attack
/// pipeline; card conditionals are copied from the named mirror or compensation path.
/// </summary>
internal static class NecrobinderContent
{
    /// <summary><c>Osty.CheckMissingWithAnim</c> as a state query: the pet exists and is alive.</summary>
    public static bool HasOsty(in FlashBattle battle)
        => (battle.Flags & FlashBattleFlags.OstyPresent) != 0 && battle.Osty.IsAlive;

    /// <summary>
    /// The Osty proxy attack: the native <c>DamageCmd.Attack(...).FromOsty(osty, card, cardPlay)</c> path
    /// (<c>GeneralCardMirrors.GeneralAttackOnPlay</c>), which returns without attacking while the pet is
    /// missing. The rule table's target type picks single/all/random targeting and the pet is the dealer, so
    /// its Strength/Weak apply and the attack history is recorded for the pet.
    /// </summary>
    public static void OstyAttack(ref FlashExec x, decimal damage, int hitCount = 1)
    {
        if (!HasOsty(x.B))
            return;
        FlashTargetType targetType = FlashRegistry.Card(x.Card.Id).TargetType(x.B, x.R, x.Card);
        FlashAttackTargeting targeting = targetType switch
        {
            FlashTargetType.AnyEnemy => FlashAttackTargeting.Single,
            FlashTargetType.AllEnemies => FlashAttackTargeting.AllOpponents,
            FlashTargetType.RandomEnemy => FlashAttackTargeting.Random,
            _ => throw new FlashUnsupportedException($"Osty 攻击目标类型 {targetType} 尚未实现。"),
        };
        FlashKernel.CardAttack(ref x, damage, hitCount, targeting, x.Target, FlashValueProps.Move,
            FlashLimits.OstyOwner);
    }

    /// <summary>The i-th canonical variable of the current card.</summary>
    public static decimal Var(ref FlashExec x, int index) => FlashCardEffects.Var(x, index);

    /// <summary><c>Soul.Create(owner, count, combatState)</c>'s canonical token card, upgraded by the source card.</summary>
    public static FlashCard CreateSoul(in FlashBattle battle, FlashRules rules, bool upgraded)
    {
        if (!FlashRegistry.TryCard("SOUL", out ushort soul))
            throw new FlashUnsupportedException("灵魂牌没有 Flash 定义。");
        return FlashKernel.CreateCard(rules, soul, upgraded ? 1 : 0);
    }

    /// <summary>Number of cards in every pile whose canonical tags include Osty attack (Squeeze).</summary>
    public static int CountOstyAttackCards(in FlashBattle battle, FlashRules rules)
    {
        int count = 0;
        for (int position = 0; position < battle.CardCount; position++)
        {
            ref readonly FlashCard card = ref battle.Cards[position];
            if ((rules.Card(card.Id, card.Upgrade).Tags & FlashCardTag.OstyAttack) != 0)
                count++;
        }
        return count;
    }

    /// <summary><c>(CalculatedVar).Calculate</c>: CalculationBase + CalculationExtra/ExtraDamage × multiplier.</summary>
    public static decimal Calculated(ref FlashExec x, int baseVar, int extraVar, decimal multiplier)
        => Var(ref x, baseVar) + Var(ref x, extraVar) * multiplier;
}
