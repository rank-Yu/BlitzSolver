namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Forge (Regent): <c>PersistentPowerSupport.Forge</c> in the existing engine. The first forge of a
/// combat creates a canonical Sovereign Blade in the hand; every forge then adds its amount to every
/// non-duplicate Sovereign Blade in the player's combat piles (the existing engine walks
/// <c>PlayerCombatState.AllCards</c>, whose order is Hand, Draw, Discard, Exhaust, Play).
/// </summary>
public static partial class FlashKernel
{
    public const string SovereignBladeId = "SOVEREIGN_BLADE";

    /// <summary>
    /// <c>PersistentPowerSupport.Forge</c>. Returns false when a hook left a pending choice. The
    /// existing engine has no combat-ending gate here: <c>AddGeneratedCardToCombat</c> refuses to add
    /// the blade while ending, but the damage increase still runs over the existing blades.
    /// </summary>
    public static bool Forge(ref FlashExec x, int amount)
    {
        if (x.NeedsChoice)
            return false;
        if (!FlashRegistry.TryCard(SovereignBladeId, out ushort blade))
            throw new FlashUnsupportedException("锻造需要 SOVEREIGN_BLADE 的 Flash 定义。");
        if (!HasUnexhaustedSovereignBlade(x.B, blade))
        {
            FlashCard created = CreateCard(x.R, blade);
            // PredictedCard.Create(CanonicalModels.Card<SovereignBlade>()) then AddGeneratedCardToCombat
            // (Hand, Bottom, Fixed): CreatedThroughForge only drives visuals.
            AddGeneratedCard(ref x, created, FlashPile.Hand);
            if (x.NeedsChoice)
                return false;
        }
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref FlashCard card = ref x.B.Cards[position];
            if (card.Id != blade)
                continue;
            long value = (long)card.Var0 + amount;
            if (value is < short.MinValue or > short.MaxValue)
                throw new FlashCapacityException("Card.Var0", short.MaxValue);
            card.Var0 = (short)value;
        }
        return true;
    }

    /// <summary>
    /// <c>GetSovereignBlades(player, includeExhausted: false)</c>: a Sovereign Blade in any pile other
    /// than the exhaust pile (a card with no pile counts as unexhausted natively; in Flash every card
    /// is in a pile).
    /// </summary>
    private static bool HasUnexhaustedSovereignBlade(in FlashBattle battle, ushort blade)
    {
        int exhaustStart = FlashPiles.Start(battle, FlashPile.Exhaust);
        int exhaustEnd = exhaustStart + battle.ExhaustCount;
        for (int position = 0; position < battle.CardCount; position++)
        {
            if (position >= exhaustStart && position < exhaustEnd)
                continue;
            if (battle.Cards[position].Id == blade)
                return true;
        }
        return false;
    }
}
