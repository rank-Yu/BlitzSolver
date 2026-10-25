using BlitzSolver.Engine;

namespace BlitzSolver.Content.Relics;

/// <summary>
/// <c>DivineRight</c> (starter): <c>AfterRoomEntered</c> gains 3 stars, which runs before a combat root
/// is captured; the imported <see cref="BlitzBattle.Stars"/> already includes it and the relic has no
/// in-combat hook of its own.
/// </summary>
[BlitzContent("DIVINE_RIGHT")]
public sealed class DivineRight : BlitzRelicDef;

/// <summary>
/// <c>GalacticDust</c>: <c>SimulatedCombatState.TriggerRelicsAfterStarsSpent</c> accumulates stars in the
/// relic's StatefulRelicState (Counter) and gains Block per completed multiple of the Stars variable.
/// </summary>
[BlitzContent("GALACTIC_DUST")]
public sealed class GalacticDust : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Stars", "Block"];

    public override void AfterStarsSpent(ref BlitzExec x, BlitzListener self, int amount)
    {
        if (amount <= 0 || self.Owner != BlitzLimits.PlayerOwner)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        int threshold = (int)x.R.RelicVar(Id, 0);
        if (threshold <= 0)
            throw new BlitzUnsupportedException("银河星尘的星能阈值不是正数。");
        int total = relic.Counter + amount;
        int triggers = total / threshold;
        relic.Counter = total % threshold;
        if (triggers > 0)
            BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, (decimal)triggers * x.R.RelicVar(Id, 1),
                BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>MiniRegent</c>: AfterStarsSpent grants the Strength variable once per turn (StatefulRelicState
/// Current is the used flag), reset at the player's side turn start
/// (<c>BeforeSideTurnStartMirrors.Vanilla</c>).
/// </summary>
[BlitzContent("MINI_REGENT")]
public sealed class MiniRegent : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void AfterStarsSpent(ref BlitzExec x, BlitzListener self, int amount)
    {
        if (amount <= 0 || self.Owner != BlitzLimits.PlayerOwner)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter != 0)
            return;
        relic.Counter = 1;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
            (int)x.R.RelicVar(Id, 0), BlitzLimits.PlayerOwner);
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side == BlitzSide.Player)
            x.B.Relics[self.Index].Counter = 0;
    }
}

/// <summary>
/// <c>Regalite</c>: the first card generated for the player each turn gains the Block variable as
/// Unpowered block (<c>AfterCardGeneratedForCombatMirrors.HandleRegalite</c>; RegalitePredictionState's
/// UsedThisTurn flag). Reset at the player's side turn start.
/// </summary>
[BlitzContent("REGALITE")]
public sealed class Regalite : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterCardGeneratedForCombat(ref BlitzExec x, BlitzListener self, int position, byte creator)
    {
        if (creator != BlitzLimits.PlayerOwner || self.Owner != BlitzLimits.PlayerOwner)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter != 0)
            return;
        relic.Counter = 1;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            BlitzValueProps.Unpowered, fromCard: false);
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side == BlitzSide.Player)
            x.B.Relics[self.Index].Counter = 0;
    }
}

/// <summary>
/// <c>VitruvianMinion</c>: powered damage and block from a card tagged Minion deal/gain double
/// (the relic's ModifyDamageMultiplicative / ModifyBlockMultiplicative mirrors).
/// </summary>
[BlitzContent("VITRUVIAN_MINION")]
public sealed class VitruvianMinion : BlitzRelicDef
{
    public override decimal ModifyDamageMultiplicative(ref BlitzExec x, BlitzListener self,
        in BlitzDamageContext damage, decimal amount)
        => IsMinionCard(x, damage.HasCardSource, damage.CardSource) ? 2m : 1m;

    public override decimal ModifyBlockMultiplicative(ref BlitzExec x, BlitzListener self,
        in BlitzBlockContext block, decimal amount)
        => IsMinionCard(x, block.HasCardSource, block.CardSource) ? 2m : 1m;

    private static bool IsMinionCard(in BlitzExec x, bool hasCard, in BlitzCard card)
        => hasCard && (x.R.Card(card.Id, card.Upgrade).Tags & BlitzCardTag.Minion) != 0;
}

/// <summary>
/// <c>FencingManual</c>: <c>AfterSideTurnStart</c> forges 10 on turn 1 only. A combat root is captured
/// after that turn start, so the forge already happened before the root; later turns are no-ops.
/// </summary>
[BlitzContent("FENCING_MANUAL")]
public sealed class FencingManual : BlitzRelicDef;

/// <summary>
/// <c>OrangeDough</c>: <c>AfterSideTurnStart</c> generates two colorless cards on turn 1 only. A combat
/// root is captured after that turn start, so the generation already happened before the root; later
/// turns are no-ops.
/// </summary>
[BlitzContent("ORANGE_DOUGH")]
public sealed class OrangeDough : BlitzRelicDef;

/// <summary>
/// <c>BrilliantScarf</c>: while the owner has played Cards-1 non-auto cards this turn, cards in hand or
/// play cost 0 energy and 0 stars (<c>TryModifyEnergyCostInCombatLate</c> / <c>TryModifyStarCost</c>);
/// the per-turn counter increments in <c>AfterCardPlayed</c> for non-auto plays and resets at the
/// player's side turn start (<c>BeforeSideTurnStartMirrors.Vanilla</c>). Counter = the engine's
/// CounterPredictionState (the live <c>_cardsPlayedThisTurn</c> is its constructor's fallback).
/// </summary>
[BlitzContent("BRILLIANT_SCARF")]
public sealed class BrilliantScarf : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override decimal TryModifyEnergyCostInCombatLate(in BlitzBattle battle, BlitzRules rules,
        BlitzListener self, in BlitzCard card, BlitzPile pile, decimal amount)
        => IsFreeTurn(battle, rules, self.Index, pile) ? 0m : amount;

    public override decimal TryModifyStarCost(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzCard card, BlitzPile pile, decimal amount)
        => IsFreeTurn(battle, rules, self.Index, pile) ? 0m : amount;

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (x.CurrentPlayIsAutoPlay || self.Owner != BlitzLimits.PlayerOwner)
            return;
        x.B.Relics[self.Index].Counter++;
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side == BlitzSide.Player)
            x.B.Relics[self.Index].Counter = 0;
    }

    private bool IsFreeTurn(in BlitzBattle battle, BlitzRules rules, int relicIndex, BlitzPile pile)
    {
        if (pile is not (BlitzPile.Hand or BlitzPile.Play))
            return false;
        int threshold = (int)rules.RelicVar(Id, 0) - 1;
        return battle.Relics[relicIndex].Counter == threshold;
    }
}
