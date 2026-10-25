using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

/// <summary>
/// Base of the cards that can generate Shivs. The closure declaration makes the rule compiler freeze
/// the Shiv's canonical stats (both upgrade levels) and admission check its definition.
/// </summary>
public abstract class ShivGeneratingCardDef : CardDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [(SilentContent.ShivId, 0)];
}

/// <summary>
/// Shared Silent generation helpers. They mirror the existing engine's
/// <c>CardPileOnPlaySupport.GenerateShivs</c> / <c>GenerateInkyShivs</c>.
/// </summary>
internal static class SilentContent
{
    public const string ShivId = "SHIV";
    public const string FanOfKnivesPowerId = "FAN_OF_KNIVES_POWER";
    public const string InkyId = "INKY";

    /// <summary>
    /// <c>GenerateShivs(owner, count, upgraded)</c>: canonical Shivs added to the hand one at a time
    /// (<c>AddGeneratedCardsToCombat(..., Hand, Bottom)</c>), upgraded once when requested.
    /// </summary>
    public static void GenerateShivs(ref Exec x, int count, bool upgraded)
    {
        if (count <= 0)
            return;
        if (!Registry.TryCard(ShivId, out ushort shiv))
            throw new UnsupportedException("飞刀生成需要 SHIV 的 Blitz 定义。");
        for (int index = 0; index < count; index++)
        {
            Card card = Kernel.CreateCard(x.R, shiv);
            if (upgraded)
                Kernel.UpgradeCard(x.R, ref card);
            if (!Kernel.AddGeneratedCard(ref x, card, Pile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// <c>GenerateInkyShivs(owner, count)</c>: the generated Shivs receive the Inky enchantment at
    /// amount 1 after they enter combat. Enchanting before insertion is equivalent: no Blitz hook runs
    /// on Shiv entering combat and the enchantment is a pure card value.
    /// </summary>
    public static void GenerateInkyShivs(ref Exec x, int count)
    {
        if (count <= 0)
            return;
        if (!Registry.TryCard(ShivId, out ushort shiv))
            throw new UnsupportedException("墨汁飞刀生成需要 SHIV 的 Blitz 定义。");
        if (!Registry.TryEnchantment(InkyId, out ushort inky))
            throw new UnsupportedException("墨汁飞刀生成需要 INKY 的 Blitz 定义。");
        for (int index = 0; index < count; index++)
        {
            Card card = Kernel.CreateCard(x.R, shiv);
            card.EnchantmentId = checked((byte)(inky + 1));
            card.EnchantmentAmount = 1;
            if (!Kernel.AddGeneratedCard(ref x, card, Pile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>Whether the player holds FanOfKnivesPower (Shiv's dynamic all-enemies targeting).</summary>
    public static bool HasFanOfKnives(in Battle battle)
        => Registry.TryPower(FanOfKnivesPowerId, out ushort id)
            && Kernel.GetAmount(battle, Limits.PlayerOwner, id) > 0;
}
