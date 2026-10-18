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
