namespace CombatSolver.Engine.InCombat.Flash.Content.Potions;

/// <summary>
/// Native <c>DeprecatedPotion</c>: a removed potion kept for run history. The existing search does
/// not offer an explicit use (<c>PotionOnUseSupport.CanSearch</c> is false), so the definition only
/// lets a root holding it pass admission.
/// </summary>
[FlashContent("DEPRECATED_POTION")]
public sealed class DeprecatedPotion : FlashPotionDef
{
    public override bool Searchable => false;

    // The existing search provides no explicit use for this potion.
    public override void OnUse(ref FlashExec x)
        => throw new FlashUnsupportedException("现有搜索不提供 DEPRECATED_POTION 的显式使用。");
}

/// <summary>
/// Native <c>FairyInABottle</c>: an automatic potion (<c>PotionOnUseSupport.CanSearch</c> is false),
/// so the search never offers an explicit use. Its <c>ShouldDie</c> prevents its owner's death and
/// <c>AfterPreventingDeath</c> consumes the slot and heals
/// <c>max(1, 30% of max HP)</c> (<c>FairyInABottleMirrors</c>).
/// </summary>
[FlashContent("FAIRY_IN_A_BOTTLE")]
public sealed class FairyInABottle : FlashPotionDef
{
    public override bool Searchable => false;

    // Never reached through the search; the effect runs from AfterPreventingDeath below.
    public override void OnUse(ref FlashExec x)
        => throw new FlashUnsupportedException("现有搜索不提供 FAIRY_IN_A_BOTTLE 的显式使用。");

    public override bool ShouldDie(ref FlashExec x, FlashListener self, byte creature)
        => creature != self.Owner;

    public override void AfterPreventingDeath(ref FlashExec x, FlashListener self, byte creature)
    {
        // PotionOnUseMirrors consume the slot, then the native OnUse heals max(1, 30% max HP).
        x.B.Potions[self.Index] = 0;
        x.B.AddCounter(FlashCounter.AutomaticPotionUseCount, 1);
        decimal amount = Math.Max(1m, x.B.Creature(creature).MaxHp * 0.3m);
        int hpBeforeRevive = x.B.Creature(creature).CurrentHp;
        FlashKernel.Heal(ref x, creature, amount);
        int restored = x.B.Creature(creature).CurrentHp - hpBeforeRevive;
        if (restored > 0)
        {
            x.B.AddCounter(FlashCounter.DeathSavePotionHpRestored, restored);
            x.B.AddCounter(FlashCounter.DeathSaveUseCount, 1);
        }
    }
}
