using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Powers;

/// <summary>
/// Native <c>BurstPower</c>: a Skill gets one extra play; each modification decrements the power,
/// and the whole power is removed at the end of its owner's side turn
/// (<c>ModifyCardPlayCountMirrors</c>, <c>CorePowerSupport.EndTurnRegular</c>'s Burst case).
/// </summary>
[FlashContent("BURST_POWER")]
public sealed class BurstPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override int ModifyCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card, byte target,
        int playCount)
    {
        if (self.Owner != FlashLimits.PlayerOwner || FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Skill)
            return playCount;
        return playCount + 1;
    }

    public override void AfterModifyingCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card)
    {
        if (x.B.Powers[self.Index].Amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id,
                x.B.Powers[self.Index].Amount - 1);
    }

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates && x.B.Powers[self.Index].Amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// Native <c>ShadowmeldPower</c>: every block its owner gains is multiplied by 2^Amount
/// (<c>ModifyBlockMultiplicativeMirrors.HandleShadowmeldPower</c>); removed at the end of the owner's
/// side turn.
/// </summary>
[FlashContent("SHADOWMELD_POWER")]
public sealed class ShadowmeldPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyBlockMultiplicative(ref FlashExec x, FlashListener self, in FlashBlockContext block,
        decimal amount)
    {
        if (block.Target != self.Owner)
            return 1m;
        int stacks = x.B.Powers[self.Index].Amount;
        // The native double already leaves decimal range at 96 stacks; the existing engine's facade
        // returns decimal.MaxValue there so the settlement ceiling applies.
        return stacks >= 96 ? decimal.MaxValue : (decimal)Math.Pow(2.0, stacks);
    }

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates && x.B.Powers[self.Index].Amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// Native <c>ToolsOfTheTradePower</c>: <c>ModifyHandDraw</c> adds the amount, and at the player's
/// turn start the owner discards that many cards (<c>TurnStartPowerSupport</c>'s
/// ToolsOfTheTradePower case with <c>PlanChoiceEffect.Discard</c>).
/// </summary>
[FlashContent("TOOLS_OF_THE_TRADE_POWER")]
public sealed class ToolsOfTheTradePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
        => self.Owner == FlashLimits.PlayerOwner ? amount + x.B.Powers[self.Index].Amount : amount;

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
        int hand = x.B.HandCount;
        int amount = x.B.Powers[self.Index].Amount;
        if (hand == 0 || amount <= 0)
            return;
        Span<int> options = stackalloc int[hand];
        for (int index = 0; index < hand; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[hand];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Discard, FlashPile.Hand, options, amount, selected,
                out int chosen))
        {
            return;
        }
        FlashKernel.StartPlayerSideTurnIfNeeded(ref x);
        if (x.NeedsChoice)
            return;
        // TurnStartChoiceSupport.Resolve(Discard) calls simulator.Discard(selected), which is the same
        // path as DiscardAndDraw with no draw: counter, AfterCardDiscarded and Sly auto-play.
        FlashKernel.DiscardAndDraw(ref x, FlashPile.Hand, selected[..chosen], 0);
    }
}

/// <summary>
/// Native <c>TheHuntPower</c>: a visual counter only. Its amount is read by the evaluation
/// (<c>missedTheHuntRewards</c>); it has no combat hook.
/// </summary>
[FlashContent("THE_HUNT_POWER")]
public sealed class TheHuntPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;
}

/// <summary>
/// Native <c>AnticipatePower</c> (a positive <c>TemporaryDexterityPower</c>): removed at its owner's
/// side turn end, taking back its amount of Dexterity
/// (<c>EndTurnPowerSupport.TriggerRegular</c>, <c>SimulatedCombatState.RetireTemporaryStat</c>).
/// </summary>
[FlashContent("ANTICIPATE_POWER")]
public sealed class AnticipatePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override string? InternallyAppliedPowerId => "DEXTERITY_POWER";

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsTemporaryPower => true;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
        if (amount == 0)
            return;
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<DexterityPower>(), -amount, self.Owner);
    }
}

/// <summary>
/// Native <c>SerpentFormPower</c>: before a card is played, snapshot the power amount for that play;
/// after it, deal that amount as Unpowered damage to a random hittable enemy
/// (<c>BeforeCardPlayedMirrors.HandleSerpentFormPower</c> + <c>AfterCardPlayedMirrors</c>). The
/// per-play snapshot is the stack-only <see cref="FlashCardPlayPairKind.SerpentFormDamage"/> pair.
/// </summary>
[FlashContent("SERPENT_FORM_POWER")]
public sealed class SerpentFormPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        x.PushCardPlayPair(FlashCardPlayPairKind.SerpentFormDamage, self.Owner, Id,
            x.B.Powers[self.Index].Amount);
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (!x.TryTakeCardPlayPair(FlashCardPlayPairKind.SerpentFormDamage, self.Owner, Id, out int amount))
            return;
        if (amount <= 0)
            return;
        FlashCardEffects.DamageRandomEnemy(ref x, amount, FlashValueProps.Unpowered, self.Owner);
    }
}
