namespace CombatSolver.Engine.InCombat.Flash.Content.Relics;

// Regent and shared relics used by the Regent content set. Existing engine paths are named per relic.

/// <summary>
/// <c>DivineRight</c> (starter): <c>AfterRoomEntered</c> gains 3 stars, which runs before a combat root
/// is captured; the imported <see cref="FlashBattle.Stars"/> already includes it and the relic has no
/// in-combat hook of its own.
/// </summary>
[FlashContent("DIVINE_RIGHT")]
public sealed class DivineRight : FlashRelicDef;

/// <summary>
/// <c>GalacticDust</c>: <c>SimulatedCombatState.TriggerRelicsAfterStarsSpent</c> accumulates stars in the
/// relic's StatefulRelicState (Counter) and gains Block per completed multiple of the Stars variable.
/// </summary>
[FlashContent("GALACTIC_DUST")]
public sealed class GalacticDust : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Stars", "Block"];

    public override void AfterStarsSpent(ref FlashExec x, FlashListener self, int amount)
    {
        if (amount <= 0 || self.Owner != FlashLimits.PlayerOwner)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        int threshold = (int)x.R.RelicVar(Id, 0);
        if (threshold <= 0)
            throw new FlashUnsupportedException("银河星尘的星能阈值不是正数。");
        int total = relic.Counter + amount;
        int triggers = total / threshold;
        relic.Counter = total % threshold;
        if (triggers > 0)
            FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, (decimal)triggers * x.R.RelicVar(Id, 1),
                FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>MiniRegent</c>: AfterStarsSpent grants the Strength variable once per turn (StatefulRelicState
/// Current is the used flag), reset at the player's side turn start
/// (<c>BeforeSideTurnStartMirrors.Vanilla</c>).
/// </summary>
[FlashContent("MINI_REGENT")]
public sealed class MiniRegent : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void AfterStarsSpent(ref FlashExec x, FlashListener self, int amount)
    {
        if (amount <= 0 || self.Owner != FlashLimits.PlayerOwner)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter != 0)
            return;
        relic.Counter = 1;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            (int)x.R.RelicVar(Id, 0), FlashLimits.PlayerOwner);
    }

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side == FlashSide.Player)
            x.B.Relics[self.Index].Counter = 0;
    }
}

/// <summary>
/// <c>Regalite</c>: the first card generated for the player each turn gains the Block variable as
/// Unpowered block (<c>AfterCardGeneratedForCombatMirrors.HandleRegalite</c>; RegalitePredictionState's
/// UsedThisTurn flag). Reset at the player's side turn start.
/// </summary>
[FlashContent("REGALITE")]
public sealed class Regalite : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterCardGeneratedForCombat(ref FlashExec x, FlashListener self, int position, byte creator)
    {
        if (creator != FlashLimits.PlayerOwner || self.Owner != FlashLimits.PlayerOwner)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter != 0)
            return;
        relic.Counter = 1;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            FlashValueProps.Unpowered, fromCard: false);
    }

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side == FlashSide.Player)
            x.B.Relics[self.Index].Counter = 0;
    }
}

/// <summary>
/// <c>VitruvianMinion</c>: powered damage and block from a card tagged Minion deal/gain double
/// (the relic's ModifyDamageMultiplicative / ModifyBlockMultiplicative mirrors).
/// </summary>
[FlashContent("VITRUVIAN_MINION")]
public sealed class VitruvianMinion : FlashRelicDef
{
    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self,
        in FlashDamageContext damage, decimal amount)
        => IsMinionCard(x, damage.HasCardSource, damage.CardSource) ? 2m : 1m;

    public override decimal ModifyBlockMultiplicative(ref FlashExec x, FlashListener self,
        in FlashBlockContext block, decimal amount)
        => IsMinionCard(x, block.HasCardSource, block.CardSource) ? 2m : 1m;

    private static bool IsMinionCard(in FlashExec x, bool hasCard, in FlashCard card)
        => hasCard && (x.R.Card(card.Id, card.Upgrade).Tags & FlashCardTag.Minion) != 0;
}

/// <summary>
/// <c>FencingManual</c>: <c>AfterSideTurnStart</c> forges 10 on turn 1 only. A combat root is captured
/// after that turn start, so the forge already happened before the root; later turns are no-ops.
/// </summary>
[FlashContent("FENCING_MANUAL")]
public sealed class FencingManual : FlashRelicDef;

/// <summary>
/// <c>OrangeDough</c>: <c>AfterSideTurnStart</c> generates two colorless cards on turn 1 only. A combat
/// root is captured after that turn start, so the generation already happened before the root; later
/// turns are no-ops.
/// </summary>
[FlashContent("ORANGE_DOUGH")]
public sealed class OrangeDough : FlashRelicDef;

/// <summary>
/// <c>BrilliantScarf</c>: while the owner has played Cards-1 non-auto cards this turn, cards in hand or
/// play cost 0 energy and 0 stars (<c>TryModifyEnergyCostInCombatLate</c> / <c>TryModifyStarCost</c>);
/// the per-turn counter increments in <c>AfterCardPlayed</c> for non-auto plays and resets at the
/// player's side turn start (<c>BeforeSideTurnStartMirrors.Vanilla</c>). Counter = the engine's
/// CounterPredictionState (the live <c>_cardsPlayedThisTurn</c> is its constructor's fallback).
/// </summary>
[FlashContent("BRILLIANT_SCARF")]
public sealed class BrilliantScarf : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override decimal TryModifyEnergyCostInCombatLate(in FlashBattle battle, FlashRules rules,
        FlashListener self, in FlashCard card, FlashPile pile, decimal amount)
        => IsFreeTurn(battle, rules, self.Index, pile) ? 0m : amount;

    public override decimal TryModifyStarCost(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount)
        => IsFreeTurn(battle, rules, self.Index, pile) ? 0m : amount;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.CurrentPlayIsAutoPlay || self.Owner != FlashLimits.PlayerOwner)
            return;
        x.B.Relics[self.Index].Counter++;
    }

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side == FlashSide.Player)
            x.B.Relics[self.Index].Counter = 0;
    }

    private bool IsFreeTurn(in FlashBattle battle, FlashRules rules, int relicIndex, FlashPile pile)
    {
        if (pile is not (FlashPile.Hand or FlashPile.Play))
            return false;
        int threshold = (int)rules.RelicVar(Id, 0) - 1;
        return battle.Relics[relicIndex].Counter == threshold;
    }
}
