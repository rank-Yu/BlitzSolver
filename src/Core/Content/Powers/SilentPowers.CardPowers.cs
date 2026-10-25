using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// Native <c>BurstPower</c>: a Skill gets one extra play; each modification decrements the power,
/// and the whole power is removed at the end of its owner's side turn
/// (<c>ModifyCardPlayCountMirrors</c>, <c>CorePowerSupport.EndTurnRegular</c>'s Burst case).
/// </summary>
[BlitzContent("BURST_POWER")]
public sealed class BurstPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override int ModifyCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card, byte target,
        int playCount)
    {
        if (self.Owner != BlitzLimits.PlayerOwner || BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Skill)
            return playCount;
        return playCount + 1;
    }

    public override void AfterModifyingCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card)
    {
        if (x.B.Powers[self.Index].Amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id,
                x.B.Powers[self.Index].Amount - 1);
    }

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates && x.B.Powers[self.Index].Amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// Native <c>ShadowmeldPower</c>: every block its owner gains is multiplied by 2^Amount
/// (<c>ModifyBlockMultiplicativeMirrors.HandleShadowmeldPower</c>); removed at the end of the owner's
/// side turn.
/// </summary>
[BlitzContent("SHADOWMELD_POWER")]
public sealed class ShadowmeldPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyBlockMultiplicative(ref BlitzExec x, BlitzListener self, in BlitzBlockContext block,
        decimal amount)
    {
        if (block.Target != self.Owner)
            return 1m;
        int stacks = x.B.Powers[self.Index].Amount;
        // The native double already leaves decimal range at 96 stacks; the existing engine's facade
        // returns decimal.MaxValue there so the settlement ceiling applies.
        return stacks >= 96 ? decimal.MaxValue : (decimal)Math.Pow(2.0, stacks);
    }

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates && x.B.Powers[self.Index].Amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// Native <c>ToolsOfTheTradePower</c>: <c>ModifyHandDraw</c> adds the amount, and at the player's
/// turn start the owner discards that many cards (<c>TurnStartPowerSupport</c>'s
/// ToolsOfTheTradePower case with <c>PlanChoiceEffect.Discard</c>).
/// </summary>
[BlitzContent("TOOLS_OF_THE_TRADE_POWER")]
public sealed class ToolsOfTheTradePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
        => self.Owner == BlitzLimits.PlayerOwner ? amount + x.B.Powers[self.Index].Amount : amount;

    public override void AfterPlayerTurnStart(ref BlitzExec x, BlitzListener self)
    {
        int hand = x.B.HandCount;
        int amount = x.B.Powers[self.Index].Amount;
        if (hand == 0 || amount <= 0)
            return;
        Span<int> options = stackalloc int[hand];
        for (int index = 0; index < hand; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[hand];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Discard, BlitzPile.Hand, options, amount, selected,
                out int chosen))
        {
            return;
        }
        BlitzKernel.StartPlayerSideTurnIfNeeded(ref x);
        if (x.NeedsChoice)
            return;
        // TurnStartChoiceSupport.Resolve(Discard) calls simulator.Discard(selected), which is the same
        // path as DiscardAndDraw with no draw: counter, AfterCardDiscarded and Sly auto-play.
        BlitzKernel.DiscardAndDraw(ref x, BlitzPile.Hand, selected[..chosen], 0);
    }
}

/// <summary>
/// Native <c>TheHuntPower</c>: a visual counter only. Its amount is read by the evaluation
/// (<c>missedTheHuntRewards</c>); it has no combat hook.
/// </summary>
[BlitzContent("THE_HUNT_POWER")]
public sealed class TheHuntPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;
}

/// <summary>
/// Native <c>AnticipatePower</c> (a positive <c>TemporaryDexterityPower</c>): removed at its owner's
/// side turn end, taking back its amount of Dexterity
/// (<c>EndTurnPowerSupport.TriggerRegular</c>, <c>SimulatedCombatState.RetireTemporaryStat</c>).
/// </summary>
[BlitzContent("ANTICIPATE_POWER")]
public sealed class AnticipatePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override string? InternallyAppliedPowerId => "DEXTERITY_POWER";

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool IsTemporaryPower => true;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (!participates)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
        if (amount == 0)
            return;
        BlitzKernel.ApplyPower(ref x, self.Owner, BlitzRegistry.PowerId<DexterityPower>(), -amount, self.Owner);
    }
}

/// <summary>
/// Native <c>SerpentFormPower</c>: before a card is played, snapshot the power amount for that play;
/// after it, deal that amount as Unpowered damage to a random hittable enemy
/// (<c>BeforeCardPlayedMirrors.HandleSerpentFormPower</c> + <c>AfterCardPlayedMirrors</c>). The
/// per-play snapshot is the stack-only <see cref="BlitzCardPlayPairKind.SerpentFormDamage"/> pair.
/// </summary>
[BlitzContent("SERPENT_FORM_POWER")]
public sealed class SerpentFormPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        x.PushCardPlayPair(BlitzCardPlayPairKind.SerpentFormDamage, self.Owner, Id,
            x.B.Powers[self.Index].Amount);
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (!x.TryTakeCardPlayPair(BlitzCardPlayPairKind.SerpentFormDamage, self.Owner, Id, out int amount))
            return;
        if (amount <= 0)
            return;
        BlitzCardEffects.DamageRandomEnemy(ref x, amount, BlitzValueProps.Unpowered, self.Owner);
    }
}
