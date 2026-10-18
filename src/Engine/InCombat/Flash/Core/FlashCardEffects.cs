namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Shared card effect building blocks, mirroring <c>GeneralCardMirrors</c> and the common
/// compensation calls. Variable index i reads <c>Stats.Vars[i]</c> plus the card's delta slot i.
/// </summary>
public static class FlashCardEffects
{
    /// <summary>Number of cards in every combat pile whose canonical tags include <paramref name="tag"/>.</summary>
    public static int CountCardsWithTag(in FlashExec x, FlashCardTag tag)
    {
        int count = 0;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref readonly FlashCard card = ref x.B.Cards[position];
            if ((x.R.Card(card.Id, card.Upgrade).Tags & tag) != 0)
                count++;
        }
        return count;
    }

    /// <summary>Current value of the card's i-th declared dynamic variable.</summary>
    public static decimal Var(in FlashExec x, int index)
    {
        FlashCardStats stats = x.Stats ?? throw new InvalidOperationException("当前没有正在打出的卡。");
        decimal delta = index switch
        {
            0 => x.Card.Var0,
            1 => x.Card.Var1,
            _ => 0,
        };
        return stats.Vars[index] + delta;
    }

    /// <summary>Mirrors <c>GeneralCardMirrors.GeneralAttackOnPlay</c> for a plain damage variable and one hit.</summary>
    public static void GeneralAttack(ref FlashExec x, int damageVar, int hitCount = 1)
        => Attack(ref x, Var(x, damageVar), hitCount);

    /// <summary>
    /// The attack half of the general recipe with an already-calculated damage value (BodySlam,
    /// PerfectedStrike and the other <c>CalculatedDamageVar</c> cards).
    /// </summary>
    public static void Attack(ref FlashExec x, decimal damage, int hitCount = 1)
    {
        FlashCardStats stats = x.Stats ?? throw new InvalidOperationException("当前没有正在打出的卡。");
        FlashAttackTargeting targeting = stats.Target switch
        {
            FlashTargetType.AnyEnemy => FlashAttackTargeting.Single,
            FlashTargetType.AllEnemies => FlashAttackTargeting.AllOpponents,
            FlashTargetType.RandomEnemy => FlashAttackTargeting.Random,
            _ => throw new FlashUnsupportedException($"攻击目标类型 {stats.Target} 尚未实现。"),
        };
        FlashKernel.CardAttack(ref x, damage, hitCount, targeting, x.Target);
    }

    /// <summary>
    /// Self damage from the compensation path: <c>simulator.Damage(owner, HpLoss, Unblockable |
    /// Unpowered | Move, owner)</c> with no card source (BloodWall, Breakthrough, Hemokinesis).
    /// </summary>
    public static void LoseHp(ref FlashExec x, decimal amount)
    {
        Span<byte> targets = stackalloc byte[1];
        targets[0] = FlashLimits.PlayerOwner;
        FlashKernel.Damage(ref x, targets, amount,
            FlashValueProps.Unblockable | FlashValueProps.Unpowered | FlashValueProps.Move,
            FlashLimits.PlayerOwner, hasCardSource: false, default);
    }

    /// <summary>Mirrors <c>GeneralCardMirrors.GeneralBlockOnPlay</c>.</summary>
    public static void GeneralBlock(ref FlashExec x, int blockVar)
        => FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, Var(x, blockVar), FlashValueProps.Move, fromCard: true);

    /// <summary>
    /// The common compensation <c>combat.Apply&lt;T&gt;(target, card.DynamicVars.X.IntValue, owner)</c>
    /// guarded by <c>when target != null</c>.
    /// </summary>
    public static void ApplyToTarget(ref FlashExec x, ushort powerId, int amountVar)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, powerId, (int)Var(x, amountVar), FlashLimits.PlayerOwner);
    }

    /// <summary>
    /// The common compensation <c>combat.Apply&lt;T&gt;(enemy, ...)</c> over
    /// <c>SimulatedCombatState.HittableEnemies</c> in roster order.
    /// </summary>
    public static void ApplyToAllEnemies(ref FlashExec x, ushort powerId, int amountVar)
    {
        int amount = (int)Var(x, amountVar);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyPower(ref x, owner, powerId, amount, FlashLimits.PlayerOwner);
        }
    }
}
