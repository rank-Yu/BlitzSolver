using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Powers;

/// <summary>
/// Native <c>SentryModePower</c>: <c>BeforeHandDraw</c> creates one <c>SweepingGaze</c> token per
/// stack into the hand (<c>TurnStartPowerSupport</c>'s SentryMode case). The generated card enters
/// combat through the same generated-card path as every other in-combat creation.
/// </summary>
[FlashContent("SENTRY_MODE_POWER")]
public sealed class SentryModePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SWEEPING_GAZE", 0)];

    public override void BeforeHandDraw(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        if (!FlashRegistry.TryCard("SWEEPING_GAZE", out ushort gaze))
            throw new FlashUnsupportedException("SweepingGaze 没有 Flash 定义。");
        int amount = x.B.Powers[self.Index].Amount;
        for (int index = 0; index < amount; index++)
        {
            FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, gaze), FlashPile.Hand);
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
[FlashContent("CALL_OF_THE_VOID_POWER")]
public sealed class CallOfTheVoidPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void BeforeHandDraw(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        Span<FlashCard> sampled = stackalloc FlashCard[1];
        for (int index = 0; index < amount; index++)
        {
            int count = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.CharacterAll, 1, distinct: true,
                FlashRngStream.CombatCardGeneration, sampled);
            if (count == 0)
                return;
            FlashCard card = sampled[0];
            card.Keywords |= FlashKeywords.Ethereal;
            FlashKernel.AddGeneratedCard(ref x, card, FlashPile.Hand);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>PagestormPower</c>: when a card with the local Ethereal keyword is drawn, draw Amount
/// more (<c>AfterCardDrawnMirrors.HandlePagestormPower</c>). Hexed's lazily supplied Ethereal is not a
/// local keyword in the native check, so this reads <see cref="FlashCard.Keywords"/> directly instead
/// of <c>FlashKernel.HasKeyword</c>.
/// </summary>
[FlashContent("PAGESTORM_POWER")]
public sealed class PagestormPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        if ((uint)drawnPosition >= (uint)x.B.CardCount)
            throw new FlashUnsupportedException("Pagestorm 触发时抽到的牌已不在牌堆。");
        if ((x.B.Cards[drawnPosition].Keywords & FlashKeywords.Ethereal) == 0)
            return;
        FlashKernel.Draw(ref x, x.B.Powers[self.Index].Amount, fromHandDraw: false);
    }
}
