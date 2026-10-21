using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Powers;

// Regent choice and generation powers. Each definition copies the existing engine path named in its
// comment; the decompiled model is only used to confirm constants and native conditions.

/// <summary>
/// <c>TyrannyPower</c>: the owner draws Amount extra cards (<c>ModifyHandDraw</c>) and at the player
/// turn start exhausts Amount cards from hand (<c>TurnStartPowerSupport.ContinueAfterPlayerTurnStart</c>
/// TyrannyPower case → <c>TurnStartChoiceSupport.Resolve(Exhaust)</c>).
/// </summary>
[FlashContent("TYRANNY_POWER")]
public sealed class TyrannyPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
        => self.Owner == FlashLimits.PlayerOwner ? amount + x.B.Powers[self.Index].Amount : amount;

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        int count = Math.Min(x.B.Powers[self.Index].Amount, x.B.HandCount);
        if (count <= 0)
            return;
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand, options, count, selected,
                out int chosen))
        {
            return;
        }
        // TurnStartChoiceCursor.BeforeNextTake starts the side before the choice's effect.
        FlashKernel.StartPlayerSideTurnIfNeeded(ref x);
        if (x.NeedsChoice)
            return;
        FlashKernel.ExhaustSelected(ref x, FlashPile.Hand, selected[..chosen]);
    }
}

/// <summary>
/// <c>ForegoneConclusionPower</c>: at the player's BeforeHandDraw, shuffle if necessary, move Amount
/// cards from the draw pile into hand, then remove the power
/// (<c>TurnStartPowerSupport.ContinueBeforeHandDraw</c> ForegoneConclusionPower case).
/// </summary>
[FlashContent("FOREGONE_CONCLUSION_POWER")]
public sealed class ForegoneConclusionPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeHandDraw(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        if (x.B.DrawCount == 0 && x.B.DiscardCount != 0)
        {
            FlashKernel.Shuffle(ref x);
            if (x.NeedsChoice)
                return;
        }
        int count = Math.Min(x.B.Powers[self.Index].Amount, x.B.DrawCount);
        if (count > 0)
        {
            Span<int> options = stackalloc int[x.B.DrawCount];
            for (int index = 0; index < options.Length; index++)
                options[index] = index;
            Span<int> selected = stackalloc int[count];
            if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Draw, options, count,
                    selected, out int chosen))
            {
                return;
            }
            // The choice is taken before the side starts; StartSide runs before the cards move.
            FlashKernel.StartPlayerSideTurnIfNeeded(ref x);
            if (x.NeedsChoice)
                return;
            FlashKernel.MoveSelected(ref x.B, FlashPile.Draw, selected[..chosen], FlashPile.Hand,
                FlashPilePosition.Bottom);
            if (x.NeedsChoice)
                return;
        }
        FlashKernel.SetPowerAmount(ref x.B, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<ForegoneConclusionPower>(), 0);
    }
}

/// <summary>
/// <c>TheSealedThronePower</c>: its owner gains Amount stars before each card play
/// (<c>BeforeCardPlayedMirrors.HandleTheSealedThronePower</c>).
/// </summary>
[FlashContent("THE_SEALED_THRONE_POWER")]
public sealed class TheSealedThronePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        FlashKernel.GainStars(ref x, x.B.Powers[self.Index].Amount);
    }
}


/// <summary>
/// <c>VoidFormPower</c>: the first Amount non-auto card plays of its owner each turn cost 0 energy and
/// 0 stars in Hand/Play (<c>ModifyEnergyCostInCombatMirrors</c>,
/// <c>ModifyStarCostMirrors</c> and <c>AfterCardPlayedMirrors</c>). <see cref="FlashPower.Aux0"/> is
/// <c>VoidFormPredictionState.CardsPlayedThisTurn</c>; <c>PrepareVoidFormApplication</c> maxes it when
/// the power is already active during a VoidForm play.
/// </summary>
[FlashContent("VOID_FORM_POWER")]
public sealed class VoidFormPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombatLate(in FlashBattle battle, FlashRules rules,
        FlashListener self, in FlashCard card, FlashPile pile, decimal amount)
        => ShouldFree(battle, self, pile) ? 0m : amount;

    public override decimal TryModifyStarCost(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount)
        => ShouldFree(battle, self, pile) ? 0m : amount;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Owner != FlashLimits.PlayerOwner || x.CurrentPlayIsAutoPlay
            || play.PlayIndex != play.PlayCount - 1)
        {
            return;
        }
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Amount <= 0)
            return;
        if (power.Aux0 == int.MaxValue)
            throw new FlashUnsupportedException("VoidForm 的本回合出牌计数溢出。");
        power.Aux0++;
    }

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, bool participates)
    {
        if (participates && self.Owner == FlashLimits.PlayerOwner)
            x.B.Powers[self.Index].Aux0 = 0;
    }

    private static bool ShouldFree(in FlashBattle battle, FlashListener self, FlashPile pile)
    {
        if (self.Owner != FlashLimits.PlayerOwner || pile is not (FlashPile.Hand or FlashPile.Play))
            return false;
        return battle.Powers[self.Index].Aux0 < battle.Powers[self.Index].Amount;
    }
}
