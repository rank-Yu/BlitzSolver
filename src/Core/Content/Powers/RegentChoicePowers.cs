using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// <c>TyrannyPower</c>: the owner draws Amount extra cards (<c>ModifyHandDraw</c>) and at the player
/// turn start exhausts Amount cards from hand (<c>TurnStartPowerSupport.ContinueAfterPlayerTurnStart</c>
/// TyrannyPower case → <c>TurnStartChoiceSupport.Resolve(Exhaust)</c>).
/// </summary>
[BlitzContent("TYRANNY_POWER")]
public sealed class TyrannyPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
        => self.Owner == BlitzLimits.PlayerOwner ? amount + x.B.Powers[self.Index].Amount : amount;

    public override void AfterPlayerTurnStart(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        int count = Math.Min(x.B.Powers[self.Index].Amount, x.B.HandCount);
        if (count <= 0)
            return;
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Exhaust, BlitzPile.Hand, options, count, selected,
                out int chosen))
        {
            return;
        }
        // TurnStartChoiceCursor.BeforeNextTake starts the side before the choice's effect.
        BlitzKernel.StartPlayerSideTurnIfNeeded(ref x);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ExhaustSelected(ref x, BlitzPile.Hand, selected[..chosen]);
    }
}

/// <summary>
/// <c>ForegoneConclusionPower</c>: at the player's BeforeHandDraw, shuffle if necessary, move Amount
/// cards from the draw pile into hand, then remove the power
/// (<c>TurnStartPowerSupport.ContinueBeforeHandDraw</c> ForegoneConclusionPower case).
/// </summary>
[BlitzContent("FOREGONE_CONCLUSION_POWER")]
public sealed class ForegoneConclusionPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeHandDraw(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        if (x.B.DrawCount == 0 && x.B.DiscardCount != 0)
        {
            BlitzKernel.Shuffle(ref x);
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
            if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.MoveToHand, BlitzPile.Draw, options, count,
                    selected, out int chosen))
            {
                return;
            }
            // The choice is taken before the side starts; StartSide runs before the cards move.
            BlitzKernel.StartPlayerSideTurnIfNeeded(ref x);
            if (x.NeedsChoice)
                return;
            BlitzKernel.MoveSelected(ref x.B, BlitzPile.Draw, selected[..chosen], BlitzPile.Hand,
                BlitzPilePosition.Bottom);
            if (x.NeedsChoice)
                return;
        }
        BlitzKernel.SetPowerAmount(ref x.B, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<ForegoneConclusionPower>(), 0);
    }
}

/// <summary>
/// <c>TheSealedThronePower</c>: its owner gains Amount stars before each card play
/// (<c>BeforeCardPlayedMirrors.HandleTheSealedThronePower</c>).
/// </summary>
[BlitzContent("THE_SEALED_THRONE_POWER")]
public sealed class TheSealedThronePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        BlitzKernel.GainStars(ref x, x.B.Powers[self.Index].Amount);
    }
}

/// <summary>
/// <c>VoidFormPower</c>: the first Amount non-auto card plays of its owner each turn cost 0 energy and
/// 0 stars in Hand/Play (<c>ModifyEnergyCostInCombatMirrors</c>,
/// <c>ModifyStarCostMirrors</c> and <c>AfterCardPlayedMirrors</c>). <see cref="BlitzPower.Aux0"/> is
/// <c>VoidFormPredictionState.CardsPlayedThisTurn</c>; <c>PrepareVoidFormApplication</c> maxes it when
/// the power is already active during a VoidForm play.
/// </summary>
[BlitzContent("VOID_FORM_POWER")]
public sealed class VoidFormPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombatLate(in BlitzBattle battle, BlitzRules rules,
        BlitzListener self, in BlitzCard card, BlitzPile pile, decimal amount)
        => ShouldFree(battle, self, pile) ? 0m : amount;

    public override decimal TryModifyStarCost(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzCard card, BlitzPile pile, decimal amount)
        => ShouldFree(battle, self, pile) ? 0m : amount;

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Owner != BlitzLimits.PlayerOwner || x.CurrentPlayIsAutoPlay
            || play.PlayIndex != play.PlayCount - 1)
        {
            return;
        }
        ref BlitzPower power = ref x.B.Powers[self.Index];
        if (power.Amount <= 0)
            return;
        if (power.Aux0 == int.MaxValue)
            throw new BlitzUnsupportedException("VoidForm 的本回合出牌计数溢出。");
        power.Aux0++;
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, bool participates)
    {
        if (participates && self.Owner == BlitzLimits.PlayerOwner)
            x.B.Powers[self.Index].Aux0 = 0;
    }

    private static bool ShouldFree(in BlitzBattle battle, BlitzListener self, BlitzPile pile)
    {
        if (self.Owner != BlitzLimits.PlayerOwner || pile is not (BlitzPile.Hand or BlitzPile.Play))
            return false;
        return battle.Powers[self.Index].Aux0 < battle.Powers[self.Index].Amount;
    }
}
