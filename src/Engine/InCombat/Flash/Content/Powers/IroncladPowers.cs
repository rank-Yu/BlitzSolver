namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Ironclad powers whose existing-engine handling lives in M/Hooks/**. Rage and FlameBarrier removal
// is handled by CorePowerSupport.TriggerTransientSideTurnEndPowers, which the Flash turn phases copy.
// Rupture is absent because the existing engine creates an unrepresentable RupturePredictionState on
// every card play.

/// <summary>
/// Native <c>FeelNoPainPower</c>: <c>AfterCardExhausted</c> gains its amount as Unpowered block
/// when the exhausted card belongs to the power owner
/// (M/Hooks/Card/AfterCardExhaustedMirrors.HandleFeelNoPainPower).
/// </summary>
[FlashContent("FEEL_NO_PAIN_POWER")]
public sealed class FeelNoPainPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card, bool causedByEthereal)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        FlashKernel.GainBlock(ref x, self.Owner, x.B.Powers[self.Index].Amount,
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// Native <c>JuggernautPower</c>: after its owner gains positive block, deal its amount as Unpowered
/// damage to a random hittable enemy (M/Hooks/Block/AfterBlockGainedMirrors.HandleJuggernautPower).
/// </summary>
[FlashContent("JUGGERNAUT_POWER")]
public sealed class JuggernautPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterBlockGained(ref FlashExec x, FlashListener self, byte owner, decimal amount,
        FlashValueProps props, bool fromCard)
    {
        if (owner != self.Owner || amount <= 0m)
            return;
        Span<byte> targets = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte candidate = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (FlashKernel.IsHittable(x.B, candidate))
                targets[count++] = candidate;
        }
        // Rng.NextItem returns before drawing when the list is empty.
        if (count == 0)
            return;
        int pick = x.B.RngStream(FlashRngStream.CombatTargets).NextInt(0, count);
        FlashKernel.Damage(ref x, targets[pick..(pick + 1)], x.B.Powers[self.Index].Amount,
            FlashValueProps.Unpowered, self.Owner, hasCardSource: false, default);
    }
}

/// <summary>
/// Native <c>CrueltyPower</c>: its amount is added to the Vulnerable multiplier as a percentage by
/// <c>ModifyDamageMirrors.HandleVulnerablePower</c>, which the Flash Vulnerable definition mirrors.
/// The power itself has no hook of its own.
/// </summary>
[FlashContent("CRUELTY_POWER")]
public sealed class CrueltyPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;
}

/// <summary>
/// Native <c>FlameBarrierPower</c>: <c>AfterDamageReceived</c> deals its amount as Unpowered damage
/// back to the powered-attack dealer, with the barrier owner as the damage dealer
/// (M/Hooks/Damage/AfterDamageReceivedMirrors.HandleFlameBarrierPower). The power itself is removed
/// by <c>CorePowerSupport.TriggerTransientSideTurnEndPowers</c> when the opposing side turn ends.
/// </summary>
[FlashContent("FLAME_BARRIER_POWER")]
public sealed class FlameBarrierPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || dealer == FlashLimits.NoOwner || !result.Props.IsPoweredAttack())
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = dealer;
        FlashKernel.Damage(ref x, targets, x.B.Powers[self.Index].Amount, FlashValueProps.Unpowered,
            self.Owner, hasCardSource: false, default);
    }
}

/// <summary>
/// Native <c>PyrePower</c>: <c>ModifyMaxEnergy</c> adds its amount to the owner's maximum energy
/// (src/Prediction/PersistentPowerSupport.cs).
/// </summary>
[FlashContent("PYRE_POWER")]
public sealed class PyrePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return amount;
        return amount + x.B.Powers[self.Index].Amount;
    }
}

/// <summary>
/// Native <c>DemonFormPower</c>: <c>AfterSideTurnStart</c> applies Strength equal to its amount to
/// its owner (src/Prediction/PersistentPowerSupport.cs, TriggerOwnerAfterSideTurnStart).
/// </summary>
[FlashContent("DEMON_FORM_POWER")]
public sealed class DemonFormPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref FlashExec x, FlashListener self)
        => FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<StrengthPower>(),
            x.B.Powers[self.Index].Amount, self.Owner);
}

/// <summary>
/// Native <c>BarricadePower</c>: <c>ShouldClearBlock</c> keeps its owner's block at side start
/// (src/Search/SimulatedCombatState.cs, game 0.111.0 Hook.ShouldClearBlock).
/// </summary>
[FlashContent("BARRICADE_POWER")]
public sealed class BarricadePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override bool ShouldClearBlock(ref FlashExec x, FlashListener self, byte owner)
        => owner != self.Owner;
}

/// <summary>
/// Native <c>ManglePower</c> (a <c>TemporaryStrengthPower</c> with negative sign): at its owner's
/// side turn end it is removed and its amount of Strength is restored
/// (src/Search/SimulatedCombatState.cs, RetireTemporaryStat, and EndTurnPowerSupport.TriggerRegular).
/// </summary>
[FlashContent("MANGLE_POWER")]
public sealed class ManglePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsTemporaryPower => true;
    // EndTurnPowerSupport.TriggerRegular: case TemporaryStrengthPower when the owner participates.
    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.RetireTemporaryStrength(ref x, self);
    }
}

/// <summary>
/// Native <c>SetupStrikePower</c> (a positive <c>TemporaryStrengthPower</c>): at its owner's side turn
/// end it is removed and its amount of Strength is taken back
/// (src/Search/SimulatedCombatState.cs, RetireTemporaryStat, and EndTurnPowerSupport.TriggerRegular).
/// </summary>
[FlashContent("SETUP_STRIKE_POWER")]
public sealed class SetupStrikePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsTemporaryPower => true;
    // EndTurnPowerSupport.TriggerRegular: case TemporaryStrengthPower when the owner participates.
    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.RetireTemporaryStrength(ref x, self);
    }
}

/// <summary>
/// Native <c>RagePower</c>: <c>AfterCardPlayed</c> gains its amount as Unpowered block after an
/// Attack card (<c>AfterCardPlayedMirrors.HandleRagePower</c>). Its removal at the player side turn
/// end is already part of <c>CorePowerSupport.TriggerTransientSideTurnEndPowers</c>.
/// </summary>
[FlashContent("RAGE_POWER")]
public sealed class RagePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Attack)
            return;
        FlashKernel.GainBlock(ref x, self.Owner, x.B.Powers[self.Index].Amount,
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// Native <c>JugglingPower</c>: <c>BeforeCardPlayed</c> counts Attack cards started this turn; the
/// third one adds its amount of clones of the played card to the hand; the player side turn end
/// resets the count (<c>BeforeCardPlayedMirrors.HandleJugglingPower</c>,
/// <c>EndTurnPowerSupport.TriggerRegular</c>, and
/// <c>SimulatedCombatState.InitializeJugglingAfterApplied</c> for the value at application).
/// Aux0 = attacks played this turn.
/// </summary>
[FlashContent("JUGGLING_POWER")]
public sealed class JugglingPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Attack)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        power.Aux0++;
        if (power.Aux0 != 3)
            return;
        for (int index = 0; index < power.Amount; index++)
        {
            if (!FlashKernel.AddGeneratedCard(ref x, FlashKernel.CloneCard(play.Card), FlashPile.Hand))
                return;
        }
    }

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            x.B.Powers[self.Index].Aux0 = 0;
    }
}

/// <summary>
/// Native <c>DarkEmbracePower</c>: a non-Ethereal exhaust draws its amount, an Ethereal exhaust is
/// deferred to the player side turn end, which then draws amount per deferred exhaust
/// (<c>AfterCardExhaustedMirrors.HandleDarkEmbracePower</c>,
/// <c>EndTurnPowerSupport.TriggerRegular</c>). Aux0 = deferred Ethereal exhausts.
/// </summary>
[FlashContent("DARK_EMBRACE_POWER")]
public sealed class DarkEmbracePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card, bool causedByEthereal)
    {
        if (causedByEthereal)
        {
            x.B.Powers[self.Index].Aux0++;
            return;
        }
        FlashKernel.Draw(ref x, x.B.Powers[self.Index].Amount, fromHandDraw: false);
    }

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        int draws = power.Amount * power.Aux0;
        if (draws > 0)
            FlashKernel.Draw(ref x, draws, fromHandDraw: false);
        power.Aux0 = 0;
    }
}

/// <summary>
/// Native <c>CrimsonMantlePower</c>: <c>AfterPlayerTurnStart</c> takes its accumulated
/// <c>SelfDamage</c> as Unblockable Unpowered damage, then gains its amount as Unpowered block
/// (<c>TurnStartPowerSupport.ContinueAfterPlayerTurnStart</c>). The SelfDamage dynamic variable is
/// private state; the bridge projects it into Aux0 and the card increments it on application.
/// </summary>
[FlashContent("CRIMSON_MANTLE_POWER")]
public sealed class CrimsonMantlePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Aux0 > 0)
        {
            Span<byte> target = stackalloc byte[1];
            target[0] = self.Owner;
            FlashKernel.Damage(ref x, target, power.Aux0,
                FlashValueProps.Unblockable | FlashValueProps.Unpowered, self.Owner,
                hasCardSource: false, default);
        }
        FlashKernel.GainBlock(ref x, self.Owner, power.Amount, FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// Native <c>InfernoPower</c>: <c>AfterPlayerTurnStart</c> takes its accumulated <c>SelfDamage</c>
/// as Unblockable Unpowered damage; on its own side, unblocked damage taken deals its amount as
/// Unpowered damage to every hittable enemy (<c>TurnStartPowerSupport.ContinueAfterPlayerTurnStart</c>,
/// <c>AfterDamageReceivedMirrors.HandleInfernoPower</c>). Aux0 = the private SelfDamage variable.
/// </summary>
[FlashContent("INFERNO_POWER")]
public sealed class InfernoPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Aux0 <= 0)
            return;
        Span<byte> target = stackalloc byte[1];
        target[0] = self.Owner;
        FlashKernel.Damage(ref x, target, power.Aux0,
            FlashValueProps.Unblockable | FlashValueProps.Unpowered, self.Owner,
            hasCardSource: false, default);
    }

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || result.UnblockedDamage <= 0 || x.B.Side != FlashSide.Player)
            return;
        FlashCardEffects.DamageAllEnemies(ref x, x.B.Powers[self.Index].Amount, FlashValueProps.Unpowered,
            self.Owner);
    }
}

// P14 batch. Aggression is TurnStartPowerSupport.ApplyBeforeSideTurnStartPower's dedicated pass;
// Unmovable is ModifyBlockMultiplicativeMirrors.HandleUnmovablePower; Rupture is the existing
// BeforeCardPlayed / AfterDamageReceived / AfterCardPlayed trio
// (BeforeCardPlayedMirrors.HandleRupturePower, AfterDamageReceivedMirrors.HandleRupturePower,
// AfterCardPlayedMirrors.HandleRupturePower). PlatingPower (StoneArmor) already has its definition
// in UnderdocksPowers.cs.

/// <summary>
/// Native <c>AggressionPower</c>: at its owner's side turn start it unstable-shuffles the Attack
/// cards of the discard pile with the CombatCardSelection stream and adds the first
/// <c>Amount</c> to the hand, upgrading each upgradable card
/// (<c>TurnStartPowerSupport.ApplyBeforeSideTurnStartPower</c>, the dedicated Aggression pass run
/// before every other power). The card does not keep private state: the selection is re-derived
/// from the discard pile, exactly as the existing engine does.
/// </summary>
[FlashContent("AGGRESSION_POWER")]
public sealed class AggressionPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, bool participates)
    {
        if (!participates || self.Owner != FlashLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        int discardCount = x.B.DiscardCount;
        int discardStart = FlashPiles.Start(x.B, FlashPile.Discard);
        Span<byte> order = stackalloc byte[discardCount];
        int candidates = 0;
        for (int index = 0; index < discardCount; index++)
        {
            FlashCard card = x.B.Cards[discardStart + index];
            if (x.R.Card(card.Id, card.Upgrade).Type == FlashCardType.Attack)
                order[candidates++] = checked((byte)index);
        }
        // ListExtensions.UnstableShuffle: Fisher-Yates from the end over the discard-order list.
        ref FlashRngState rng = ref x.B.RngStream(FlashRngStream.CombatCardSelection);
        for (int n = candidates; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (order[k], order[n]) = (order[n], order[k]);
        }
        Span<bool> removed = stackalloc bool[discardCount];
        int take = Math.Min(amount, candidates);
        for (int index = 0; index < take; index++)
        {
            int original = order[index];
            // AddToPile removes the card before inserting, so earlier removals shift this occurrence.
            int current = original;
            for (int earlier = 0; earlier < original; earlier++)
            {
                if (removed[earlier])
                    current--;
            }
            FlashPile destination = x.B.HandCount < x.B.MaxHandSize ? FlashPile.Hand : FlashPile.Discard;
            if (!FlashKernel.MoveCard(ref x.B, FlashPile.Discard, current, destination))
                return;
            // AddToPile inserts at the bottom (Hand, or Discard when the hand is full).
            int inserted = destination == FlashPile.Hand ? x.B.HandCount - 1 : x.B.DiscardCount - 1;
            ref FlashCard added = ref FlashPiles.At(ref x.B, destination, inserted);
            // CardCmd.Upgrade on an upgradable card; UpgradeCard is a no-op at the maximum level.
            FlashKernel.UpgradeCard(x.R, ref added);
            removed[original] = true;
        }
    }
}

/// <summary>
/// Native <c>UnmovablePower</c>: <c>ModifyBlockMultiplicative</c> doubles block from a card or
/// monster move while fewer than <c>Amount</c> block-gain events happened earlier this turn
/// (<c>ModifyBlockMultiplicativeMirrors.HandleUnmovablePower</c>). The existing engine subtracts the
/// current card play's powered block events from
/// <c>SimulatedCombatState.GetBlockCardsPlayedThisTurn</c>; Aux0 stores that turn counter when the
/// card play starts. Card-sourced block is the only reader, and it runs before the play's tail, so
/// Aux0 is reset in <c>AfterCardPlayed</c> and is zero at every action boundary
/// (<c>CombatPredictionSimulator.CardTailContinuation</c> releases the same per-play count there).
/// </summary>
[FlashContent("UNMOVABLE_POWER")]
public sealed class UnmovablePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    // Aux0 = BlockCardsPlayedThisTurn at the start of the current card play.
    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
        => x.B.Powers[self.Index].Aux0 = x.B.Counter(FlashCounter.BlockCardsPlayedThisTurn);

    // The play is complete: the engine releases its per-CardPlay powered-block count at the same tail.
    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
        => x.B.Powers[self.Index].Aux0 = 0;

    public override decimal ModifyBlockMultiplicative(ref FlashExec x, FlashListener self,
        in FlashBlockContext block, decimal amount)
    {
        // The native guard is target.IsMonster; Osty does not exist in an admitted root, so only the
        // power owner's block reaches the doubling branch.
        if (block.Target != self.Owner || (block.Props & FlashValueProps.Move) == 0)
            return 1m;
        // A card source from another owner cannot occur in single-player combat, and non-card block
        // (relic, power) has no card play, so the engine's CardSource owner guard passes.
        ref FlashPower power = ref x.B.Powers[self.Index];
        int earlier = block.HasCardSource
            ? power.Aux0
            : x.B.Counter(FlashCounter.BlockCardsPlayedThisTurn);
        return earlier < power.Amount ? 2m : 1m;
    }
}

/// <summary>
/// Native <c>RupturePower</c>: while its owner plays a card, unblocked damage that card deals to
/// the owner is accumulated and applied as Strength at the play's AfterCardPlayed; any other
/// unblocked damage taken on the owner's own side applies Strength immediately
/// (<c>BeforeCardPlayedMirrors.HandleRupturePower</c>,
/// <c>AfterDamageReceivedMirrors.HandleRupturePower</c>,
/// <c>AfterCardPlayedMirrors.HandleRupturePower</c>).
/// Aux0 = Strength accumulated for the current card play, Aux1 = the current play's pending entry,
/// Aux2 = the last damage event to the owner had a card source. The existing engine keeps the same
/// information in <c>RupturePredictionState.StrengthByCard</c>, a map keyed by CardModel; only one
/// card play can be in flight, so the map is empty or has exactly one keyed entry, and the pending
/// Strength is its only value. The bridge claims an empty entry and rejects a non-empty one
/// (<see cref="FlashStateProjections"/>), so every non-terminal imported state has Aux0..2 = 0 and
/// the three slots are written before each is read: BeforeCardPlayed starts a play,
/// BeforeDamageReceived records the event's card source, AfterDamageReceived consumes it, and
/// AfterCardPlayed settles the play.
/// </summary>
[FlashContent("RUPTURE_POWER")]
public sealed class RupturePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref FlashPower power = ref x.B.Powers[self.Index];
        power.Aux0 = 0;
        power.Aux1 = 1;
        power.Aux2 = 0;
    }

    public override void BeforeDamageReceived(ref FlashExec x, FlashListener self, byte target, decimal amount,
        FlashValueProps props, byte dealer, bool hasCardSource, in FlashCard cardSource)
    {
        if (target == self.Owner)
            x.B.Powers[self.Index].Aux2 = hasCardSource ? (short)1 : (short)0;
    }

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || result.UnblockedDamage <= 0 || x.B.Side != FlashSide.Player)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        bool fromPendingPlay = power.Aux1 != 0 && power.Aux2 != 0;
        power.Aux2 = 0;
        if (fromPendingPlay)
        {
            power.Aux0 += power.Amount;
            return;
        }
        // The existing engine applies with cardSource: null; Flash has no card source on powers.
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<StrengthPower>(), power.Amount, self.Owner);
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref FlashPower power = ref x.B.Powers[self.Index];
        int gained = power.Aux0;
        power.Aux0 = 0;
        power.Aux1 = 0;
        power.Aux2 = 0;
        if (gained != 0)
            FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<StrengthPower>(), gained, self.Owner);
    }
}
