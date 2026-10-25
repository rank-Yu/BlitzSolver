using BlitzSolver.Engine;

namespace BlitzSolver.Content.Potions;

/// <summary>
/// Native <c>DeprecatedPotion</c>: a removed potion kept for run history. The existing search does
/// not offer an explicit use (<c>PotionOnUseSupport.CanSearch</c> is false), so the definition only
/// lets a root holding it pass admission.
/// </summary>
[BlitzContent("DEPRECATED_POTION")]
public sealed class DeprecatedPotion : BlitzPotionDef
{
    public override bool Searchable => false;

    // The existing search provides no explicit use for this potion.
    public override void OnUse(ref BlitzExec x)
        => throw new BlitzUnsupportedException("现有搜索不提供 DEPRECATED_POTION 的显式使用。");
}
