using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

/// <summary>
/// Base of the cards that can generate Shivs. The closure declaration makes the rule compiler freeze
/// the Shiv's canonical stats (both upgrade levels) and admission check its definition.
/// </summary>
public abstract class ShivGeneratingCardDef : FlashCardDef
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
    public static void GenerateShivs(ref FlashExec x, int count, bool upgraded)
    {
        if (count <= 0)
            return;
        if (!FlashRegistry.TryCard(ShivId, out ushort shiv))
            throw new FlashUnsupportedException("飞刀生成需要 SHIV 的 Flash 定义。");
        for (int index = 0; index < count; index++)
        {
            FlashCard card = FlashKernel.CreateCard(x.R, shiv);
            if (upgraded)
                FlashKernel.UpgradeCard(x.R, ref card);
            if (!FlashKernel.AddGeneratedCard(ref x, card, FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// <c>GenerateInkyShivs(owner, count)</c>: the generated Shivs receive the Inky enchantment at
    /// amount 1 after they enter combat. Enchanting before insertion is equivalent: no Flash hook runs
    /// on Shiv entering combat and the enchantment is a pure card value.
    /// </summary>
    public static void GenerateInkyShivs(ref FlashExec x, int count)
    {
        if (count <= 0)
            return;
        if (!FlashRegistry.TryCard(ShivId, out ushort shiv))
            throw new FlashUnsupportedException("墨汁飞刀生成需要 SHIV 的 Flash 定义。");
        if (!FlashRegistry.TryEnchantment(InkyId, out ushort inky))
            throw new FlashUnsupportedException("墨汁飞刀生成需要 INKY 的 Flash 定义。");
        for (int index = 0; index < count; index++)
        {
            FlashCard card = FlashKernel.CreateCard(x.R, shiv);
            card.EnchantmentId = checked((byte)(inky + 1));
            card.EnchantmentAmount = 1;
            if (!FlashKernel.AddGeneratedCard(ref x, card, FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>Whether the player holds FanOfKnivesPower (Shiv's dynamic all-enemies targeting).</summary>
    public static bool HasFanOfKnives(in FlashBattle battle)
        => FlashRegistry.TryPower(FanOfKnivesPowerId, out ushort id)
            && FlashKernel.GetAmount(battle, FlashLimits.PlayerOwner, id) > 0;
}
