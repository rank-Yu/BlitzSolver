using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// Native <c>SentryModePower</c>: <c>BeforeHandDraw</c> creates one <c>SweepingGaze</c> token per
/// stack into the hand (<c>TurnStartPowerSupport</c>'s SentryMode case). The generated card enters
/// combat through the same generated-card path as every other in-combat creation.
/// </summary>
[BlitzContent("SENTRY_MODE_POWER")]
public sealed class SentryModePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SWEEPING_GAZE", 0)];

    public override void BeforeHandDraw(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        if (!BlitzRegistry.TryCard("SWEEPING_GAZE", out ushort gaze))
            throw new BlitzUnsupportedException("SweepingGaze 没有 Blitz 定义。");
        int amount = x.B.Powers[self.Index].Amount;
        for (int index = 0; index < amount; index++)
        {
            BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CreateCard(x.R, gaze), BlitzPile.Hand);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>CallOfTheVoidPower</c>: <c>BeforeHandDraw</c> generates <c>Amount</c> distinct cards
/// one at a time from the character pool excluding Basic/Ancient, applies Ethereal to each, then adds
/// them to the hand (<c>TurnStartPowerSupport</c>'s CallOfTheVoid case). The existing engine's
/// <c>PrepareCharacterGenerationCandidates(NonBasicAndAncient)</c> plus
/// <c>FilterForCombatAndPlayerCount</c> equals the frozen <c>CharacterAll</c> pool (that filter
/// already excludes Basic/Ancient/Event), and <c>GetDistinctForCombat(1)</c> per amount is one full
/// <c>TakeRandom</c> shuffle per generated card.
/// </summary>
[BlitzContent("CALL_OF_THE_VOID_POWER")]
public sealed class CallOfTheVoidPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAll];

    public override void BeforeHandDraw(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        Span<BlitzCard> sampled = stackalloc BlitzCard[1];
        for (int index = 0; index < amount; index++)
        {
            int count = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.CharacterAll, 1, distinct: true,
                BlitzRngStream.CombatCardGeneration, sampled);
            if (count == 0)
                return;
            BlitzCard card = sampled[0];
            card.Keywords |= BlitzKeywords.Ethereal;
            BlitzKernel.AddGeneratedCard(ref x, card, BlitzPile.Hand);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>PagestormPower</c>: when a card with the local Ethereal keyword is drawn, draw Amount
/// more (<c>AfterCardDrawnMirrors.HandlePagestormPower</c>). Hexed's lazily supplied Ethereal is not a
/// local keyword in the native check, so this reads <see cref="BlitzCard.Keywords"/> directly instead
/// of <c>BlitzKernel.HasKeyword</c>.
/// </summary>
[BlitzContent("PAGESTORM_POWER")]
public sealed class PagestormPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardDrawn(ref BlitzExec x, BlitzListener self, int drawnPosition, bool fromHandDraw)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        if ((uint)drawnPosition >= (uint)x.B.CardCount)
            throw new BlitzUnsupportedException("Pagestorm 触发时抽到的牌已不在牌堆。");
        if ((x.B.Cards[drawnPosition].Keywords & BlitzKeywords.Ethereal) == 0)
            return;
        BlitzKernel.Draw(ref x, x.B.Powers[self.Index].Amount, fromHandDraw: false);
    }
}
