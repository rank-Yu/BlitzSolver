using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// Native <c>FeelNoPainPower</c>: <c>AfterCardExhausted</c> gains its amount as Unpowered block
/// when the exhausted card belongs to the power owner
/// (M/Hooks/Card/AfterCardExhaustedMirrors.HandleFeelNoPainPower).
/// </summary>
[BlitzContent("FEEL_NO_PAIN_POWER")]
public sealed class FeelNoPainPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardExhausted(ref BlitzExec x, BlitzListener self, in BlitzCard card, bool causedByEthereal)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        BlitzKernel.GainBlock(ref x, self.Owner, x.B.Powers[self.Index].Amount,
            BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// Native <c>JuggernautPower</c>: after its owner gains positive block, deal its amount as Unpowered
/// damage to a random hittable enemy (M/Hooks/Block/AfterBlockGainedMirrors.HandleJuggernautPower).
/// </summary>
[BlitzContent("JUGGERNAUT_POWER")]
public sealed class JuggernautPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterBlockGained(ref BlitzExec x, BlitzListener self, byte owner, decimal amount,
        BlitzValueProps props, bool fromCard)
    {
        if (owner != self.Owner || amount <= 0m)
            return;
        Span<byte> targets = stackalloc byte[BlitzLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte candidate = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (BlitzKernel.IsHittable(x.B, candidate))
                targets[count++] = candidate;
        }
        // Rng.NextItem returns before drawing when the list is empty.
        if (count == 0)
            return;
        int pick = x.B.RngStream(BlitzRngStream.CombatTargets).NextInt(0, count);
        BlitzKernel.Damage(ref x, targets[pick..(pick + 1)], x.B.Powers[self.Index].Amount,
            BlitzValueProps.Unpowered, self.Owner, hasCardSource: false, default);
    }
}

/// <summary>
/// Native <c>CrueltyPower</c>: its amount is added to the Vulnerable multiplier as a percentage by
/// <c>ModifyDamageMirrors.HandleVulnerablePower</c>, which the Blitz Vulnerable definition mirrors.
/// The power itself has no hook of its own.
/// </summary>
[BlitzContent("CRUELTY_POWER")]
public sealed class CrueltyPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;
}

/// <summary>
/// Native <c>FlameBarrierPower</c>: <c>AfterDamageReceived</c> deals its amount as Unpowered damage
/// back to the powered-attack dealer, with the barrier owner as the damage dealer
/// (M/Hooks/Damage/AfterDamageReceivedMirrors.HandleFlameBarrierPower). The power itself is removed
/// by <c>CorePowerSupport.TriggerTransientSideTurnEndPowers</c> when the opposing side turn ends.
/// </summary>
[BlitzContent("FLAME_BARRIER_POWER")]
public sealed class FlameBarrierPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterDamageReceived(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || dealer == BlitzLimits.NoOwner || !result.Props.IsPoweredAttack())
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = dealer;
        BlitzKernel.Damage(ref x, targets, x.B.Powers[self.Index].Amount, BlitzValueProps.Unpowered,
            self.Owner, hasCardSource: false, default);
    }
}

/// <summary>
/// Native <c>PyrePower</c>: <c>ModifyMaxEnergy</c> adds its amount to the owner's maximum energy
/// (src/Prediction/PersistentPowerSupport.cs).
/// </summary>
[BlitzContent("PYRE_POWER")]
public sealed class PyrePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return amount;
        return amount + x.B.Powers[self.Index].Amount;
    }
}

/// <summary>
/// Native <c>DemonFormPower</c>: <c>AfterSideTurnStart</c> applies Strength equal to its amount to
/// its owner (src/Prediction/PersistentPowerSupport.cs, TriggerOwnerAfterSideTurnStart).
/// </summary>
[BlitzContent("DEMON_FORM_POWER")]
public sealed class DemonFormPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref BlitzExec x, BlitzListener self)
        => BlitzKernel.ApplyPower(ref x, self.Owner, BlitzRegistry.PowerId<StrengthPower>(),
            x.B.Powers[self.Index].Amount, self.Owner);
}

/// <summary>
/// Native <c>BarricadePower</c>: <c>ShouldClearBlock</c> keeps its owner's block at side start
/// (src/Search/SimulatedCombatState.cs, game 0.111.0 Hook.ShouldClearBlock).
/// </summary>
[BlitzContent("BARRICADE_POWER")]
public sealed class BarricadePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override bool ShouldClearBlock(ref BlitzExec x, BlitzListener self, byte owner)
        => owner != self.Owner;
}

/// <summary>
/// Native <c>ManglePower</c> (a <c>TemporaryStrengthPower</c> with negative sign): at its owner's
/// side turn end it is removed and its amount of Strength is restored
/// (src/Search/SimulatedCombatState.cs, RetireTemporaryStat, and EndTurnPowerSupport.TriggerRegular).
/// </summary>
[BlitzContent("MANGLE_POWER")]
public sealed class ManglePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override string? InternallyAppliedPowerId => "STRENGTH_POWER";
    public override bool IsTemporaryPower => true;
    // EndTurnPowerSupport.TriggerRegular: case TemporaryStrengthPower when the owner participates.
    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
            BlitzKernel.RetireTemporaryStrength(ref x, self);
    }
}

/// <summary>
/// Native <c>SetupStrikePower</c> (a positive <c>TemporaryStrengthPower</c>): at its owner's side turn
/// end it is removed and its amount of Strength is taken back
/// (src/Search/SimulatedCombatState.cs, RetireTemporaryStat, and EndTurnPowerSupport.TriggerRegular).
/// </summary>
[BlitzContent("SETUP_STRIKE_POWER")]
public sealed class SetupStrikePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override string? InternallyAppliedPowerId => "STRENGTH_POWER";
    public override bool IsTemporaryPower => true;
    // EndTurnPowerSupport.TriggerRegular: case TemporaryStrengthPower when the owner participates.
    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
            BlitzKernel.RetireTemporaryStrength(ref x, self);
    }
}

/// <summary>
/// Native <c>RagePower</c>: <c>AfterCardPlayed</c> gains its amount as Unpowered block after an
/// Attack card (<c>AfterCardPlayedMirrors.HandleRagePower</c>). Its removal at the player side turn
/// end is already part of <c>CorePowerSupport.TriggerTransientSideTurnEndPowers</c>.
/// </summary>
[BlitzContent("RAGE_POWER")]
public sealed class RagePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Attack)
            return;
        BlitzKernel.GainBlock(ref x, self.Owner, x.B.Powers[self.Index].Amount,
            BlitzValueProps.Unpowered, fromCard: false);
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
[BlitzContent("JUGGLING_POWER")]
public sealed class JugglingPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Attack)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        power.Aux0++;
        if (power.Aux0 != 3)
            return;
        for (int index = 0; index < power.Amount; index++)
        {
            if (!BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CloneCard(play.Card), BlitzPile.Hand))
                return;
        }
    }

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
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
[BlitzContent("DARK_EMBRACE_POWER")]
public sealed class DarkEmbracePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardExhausted(ref BlitzExec x, BlitzListener self, in BlitzCard card, bool causedByEthereal)
    {
        if (causedByEthereal)
        {
            x.B.Powers[self.Index].Aux0++;
            return;
        }
        BlitzKernel.Draw(ref x, x.B.Powers[self.Index].Amount, fromHandDraw: false);
    }

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (!participates)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        int draws = power.Amount * power.Aux0;
        if (draws > 0)
            BlitzKernel.Draw(ref x, draws, fromHandDraw: false);
        power.Aux0 = 0;
    }
}

/// <summary>
/// Native <c>CrimsonMantlePower</c>: <c>AfterPlayerTurnStart</c> takes its accumulated
/// <c>SelfDamage</c> as Unblockable Unpowered damage, then gains its amount as Unpowered block
/// (<c>TurnStartPowerSupport.ContinueAfterPlayerTurnStart</c>). The SelfDamage dynamic variable is
/// private state; the bridge projects it into Aux0 and the card increments it on application.
/// </summary>
[BlitzContent("CRIMSON_MANTLE_POWER")]
public sealed class CrimsonMantlePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterPlayerTurnStart(ref BlitzExec x, BlitzListener self)
    {
        ref BlitzPower power = ref x.B.Powers[self.Index];
        if (power.Aux0 > 0)
        {
            Span<byte> target = stackalloc byte[1];
            target[0] = self.Owner;
            BlitzKernel.Damage(ref x, target, power.Aux0,
                BlitzValueProps.Unblockable | BlitzValueProps.Unpowered, self.Owner,
                hasCardSource: false, default);
        }
        BlitzKernel.GainBlock(ref x, self.Owner, power.Amount, BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// Native <c>InfernoPower</c>: <c>AfterPlayerTurnStart</c> takes its accumulated <c>SelfDamage</c>
/// as Unblockable Unpowered damage; on its own side, unblocked damage taken deals its amount as
/// Unpowered damage to every hittable enemy (<c>TurnStartPowerSupport.ContinueAfterPlayerTurnStart</c>,
/// <c>AfterDamageReceivedMirrors.HandleInfernoPower</c>). Aux0 = the private SelfDamage variable.
/// </summary>
[BlitzContent("INFERNO_POWER")]
public sealed class InfernoPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterPlayerTurnStart(ref BlitzExec x, BlitzListener self)
    {
        ref BlitzPower power = ref x.B.Powers[self.Index];
        if (power.Aux0 <= 0)
            return;
        Span<byte> target = stackalloc byte[1];
        target[0] = self.Owner;
        BlitzKernel.Damage(ref x, target, power.Aux0,
            BlitzValueProps.Unblockable | BlitzValueProps.Unpowered, self.Owner,
            hasCardSource: false, default);
    }

    public override void AfterDamageReceived(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || result.UnblockedDamage <= 0 || x.B.Side != BlitzSide.Player)
            return;
        BlitzCardEffects.DamageAllEnemies(ref x, x.B.Powers[self.Index].Amount, BlitzValueProps.Unpowered,
            self.Owner);
    }
}

/// <summary>
/// Native <c>AggressionPower</c>: at its owner's side turn start it unstable-shuffles the Attack
/// cards of the discard pile with the CombatCardSelection stream and adds the first
/// <c>Amount</c> to the hand, upgrading each upgradable card
/// (<c>TurnStartPowerSupport.ApplyBeforeSideTurnStartPower</c>, the dedicated Aggression pass run
/// before every other power). The card does not keep private state: the selection is re-derived
/// from the discard pile, exactly as the existing engine does.
/// </summary>
[BlitzContent("AGGRESSION_POWER")]
public sealed class AggressionPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, bool participates)
    {
        if (!participates || self.Owner != BlitzLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        int discardCount = x.B.DiscardCount;
        int discardStart = BlitzPiles.Start(x.B, BlitzPile.Discard);
        Span<byte> order = stackalloc byte[discardCount];
        int candidates = 0;
        for (int index = 0; index < discardCount; index++)
        {
            BlitzCard card = x.B.Cards[discardStart + index];
            if (BlitzKernel.CardType(x.B, x.R, card) == BlitzCardType.Attack)
                order[candidates++] = checked((byte)index);
        }
        // ListExtensions.UnstableShuffle: Fisher-Yates from the end over the discard-order list.
        ref BlitzRngState rng = ref x.B.RngStream(BlitzRngStream.CombatCardSelection);
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
            BlitzPile destination = x.B.HandCount < x.B.MaxHandSize ? BlitzPile.Hand : BlitzPile.Discard;
            if (!BlitzKernel.MoveCard(ref x.B, BlitzPile.Discard, current, destination))
                return;
            // AddToPile inserts at the bottom (Hand, or Discard when the hand is full).
            int inserted = destination == BlitzPile.Hand ? x.B.HandCount - 1 : x.B.DiscardCount - 1;
            ref BlitzCard added = ref BlitzPiles.At(ref x.B, destination, inserted);
            // CardCmd.Upgrade on an upgradable card; UpgradeCard is a no-op at the maximum level.
            BlitzKernel.UpgradeCard(x.R, ref added);
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
[BlitzContent("UNMOVABLE_POWER")]
public sealed class UnmovablePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    // Aux0 = BlockCardsPlayedThisTurn at the start of the current card play.
    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
        => x.B.Powers[self.Index].Aux0 = x.B.Counter(BlitzCounter.BlockCardsPlayedThisTurn);

    // The play is complete: the engine releases its per-CardPlay powered-block count at the same tail.
    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
        => x.B.Powers[self.Index].Aux0 = 0;

    public override decimal ModifyBlockMultiplicative(ref BlitzExec x, BlitzListener self,
        in BlitzBlockContext block, decimal amount)
    {
        // The native guard is target.IsMonster; Osty does not exist in an admitted root, so only the
        // power owner's block reaches the doubling branch.
        if (block.Target != self.Owner || (block.Props & BlitzValueProps.Move) == 0)
            return 1m;
        // A card source from another owner cannot occur in single-player combat, and non-card block
        // (relic, power) has no card play, so the engine's CardSource owner guard passes.
        ref BlitzPower power = ref x.B.Powers[self.Index];
        int earlier = block.HasCardSource
            ? power.Aux0
            : x.B.Counter(BlitzCounter.BlockCardsPlayedThisTurn);
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
/// Aux0 = Strength accumulated for the current card play, Aux1 = the current play's pending entry.
/// The card source is read directly from <see cref="BlitzDamageResult.HasCardSource"/> (the existing
/// engine passes <c>AfterDamageReceivedMirrorContext.Source</c>), so no source marker is staged on
/// BeforeDamageReceived. The existing engine keeps the pending information in
/// <c>RupturePredictionState.StrengthByCard</c>, a map keyed by CardModel; only one card play can be
/// in flight, so the map is empty or has exactly one keyed entry, and the pending Strength is its
/// only value. The bridge claims an empty entry and rejects a non-empty one
/// (<see cref="BlitzStateProjections"/>), so every non-terminal imported state has Aux0 = Aux1 = 0.
/// </summary>
[BlitzContent("RUPTURE_POWER")]
public sealed class RupturePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        ref BlitzPower power = ref x.B.Powers[self.Index];
        power.Aux0 = 0;
        power.Aux1 = 1;
    }

    public override void AfterDamageReceived(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || result.UnblockedDamage <= 0 || x.B.Side != BlitzSide.Player)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        if (power.Aux1 != 0 && result.HasCardSource)
        {
            power.Aux0 += power.Amount;
            return;
        }
        // The existing engine applies with cardSource: null; Blitz has no card source on powers.
        BlitzKernel.ApplyPower(ref x, self.Owner, BlitzRegistry.PowerId<StrengthPower>(), power.Amount, self.Owner);
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        ref BlitzPower power = ref x.B.Powers[self.Index];
        int gained = power.Aux0;
        power.Aux0 = 0;
        power.Aux1 = 0;
        if (gained != 0)
            BlitzKernel.ApplyPower(ref x, self.Owner, BlitzRegistry.PowerId<StrengthPower>(), gained, self.Owner);
    }
}

/// <summary>
/// Native <c>HellraiserPower</c>: when its owner draws a Strike, auto-play that card
/// (M/Hooks/Card/AfterCardDrawnMirrors.HandleHellraiserPower). When every hittable enemy shows
/// infinite HP (Waterfall Giant's steam-eruption rewrite) the auto-play stops after nine draws and
/// the counter keeps incrementing; the finite branch resets it. Aux0 is
/// <c>infiniteAutoPlaysThisTurn</c> (HellraiserPredictionState; without an entry the model's internal
/// data). <c>EndTurnBatch048</c> resets it when the owner participates (ResetInfiniteAutoPlayData).
/// </summary>
[BlitzContent("HELLRAISER_POWER")]
public sealed class HellraiserPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override void AfterCardDrawnEarly(ref BlitzExec x, BlitzListener self, int drawnPosition, bool fromHandDraw)
    {
        if (self.Owner != BlitzLimits.PlayerOwner || drawnPosition >= x.B.HandCount)
            return;
        BlitzCardStats stats = x.R.Card(x.B.Cards[drawnPosition].Id, x.B.Cards[drawnPosition].Upgrade);
        if ((stats.Tags & BlitzCardTag.Strike) == 0)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        bool autoPlay = true;
        if (AllHittableEnemiesInfinite(ref x))
        {
            if (power.Aux0 >= InfiniteAutoPlayCap)
                autoPlay = false;
            power.Aux0++;
        }
        else
        {
            power.Aux0 = 0;
        }
        if (autoPlay)
            BlitzKernel.AutoPlay(ref x, BlitzPile.Hand, drawnPosition);
    }

    public override void EndTurnBatch048(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        // CorePowerSupport/EndTurnPowerSupport resets the turn counter when the owner participates.
        if (participates)
            x.B.Powers[self.Index].Aux0 = 0;
    }

    private const int InfiniteAutoPlayCap = 9;

    /// <summary>
    /// <c>HittableEnemies.All(c =&gt; c.HpDisplay.IsInfinite())</c>: hittable enemies are the roster
    /// entries not in a death phase. The only infinite display is a Waterfall Giant rewritten by
    /// <c>TryTriggerSteamEruptionDeath</c>; Blitz records that as the steam phase or the value-clamp HP
    /// (the Explode phase clears SteamPhase but keeps the rewritten HP until the forced kill).
    /// </summary>
    private static bool AllHittableEnemiesInfinite(ref BlitzExec x)
    {
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            ref BlitzEnemy enemy = ref x.B.Enemy(owner);
            if (enemy.DeathPhase != BlitzDeathPhase.None)
                continue;
            // A non-empty hittable set needs the one native infinite-display source. An empty set
            // (every enemy reviving, as with the Test Subject's first form) makes All() true.
            if (!x.R.HasInfiniteHpDisplayMonster
                || BlitzRegistry.Monster(enemy.MonsterId).NativeId != "WATERFALL_GIANT")
            {
                return false;
            }
            if (enemy.SteamPhase == BlitzSteamPhase.None
                && x.B.Creature(owner).CurrentHp < BlitzLimits.ValueClamp)
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Native <c>StampedePower</c>: at the auto-post-play phase, auto-play up to its amount of Attacks from
/// hand, each picked with the Shuffle stream
/// (M/Hooks/TurnEnd/AfterAutoPostPlayPhaseEnteredMirrors.HandleStampedePower).
/// </summary>
[BlitzContent("STAMPEDE_POWER")]
public sealed class StampedePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterAutoPostPlayPhaseEntered(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        for (int index = 0; index < amount; index++)
        {
            int count = 0;
            int pick = -1;
            for (int hand = 0; hand < x.B.HandCount; hand++)
            {
                BlitzCard card = x.B.Cards[hand];
                BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
                if (stats.Type != BlitzCardType.Attack || card.HasKeyword(BlitzKeywords.Unplayable))
                    continue;
                count++;
            }
            if (count == 0)
                return;
            int selected = x.B.RngStream(BlitzRngStream.Shuffle).NextInt(0, count);
            for (int hand = 0; hand < x.B.HandCount; hand++)
            {
                BlitzCard card = x.B.Cards[hand];
                BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
                if (stats.Type != BlitzCardType.Attack || card.HasKeyword(BlitzKeywords.Unplayable))
                    continue;
                if (selected-- == 0)
                {
                    pick = hand;
                    break;
                }
            }
            if (pick < 0)
                throw new BlitzUnsupportedException("Stampede 无法在手牌中定位选中的攻击牌。");
            BlitzKernel.AutoPlay(ref x, BlitzPile.Hand, pick);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>CalamityPower</c>: an Attack play snapshots the power amount, then
/// <c>AfterCardPlayed</c> generates that many character Attacks with replacement into hand
/// (BeforeCardPlayedMirrors.HandleCalamityPower / AfterCardPlayedMirrors.HandleCalamityPower).
/// Aux0 is the pending-play marker; it is zero at every action boundary.
/// </summary>
[BlitzContent("CALAMITY_POWER")]
public sealed class CalamityPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAttacks];

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        BlitzCardStats stats = x.R.Card(play.Card.Id, play.Card.Upgrade);
        if (stats.Type != BlitzCardType.Attack)
            return;
        // Native keys the pending amount by the played CardModel, so a nested auto-play gets its own
        // entry and the outer Attack still fires afterwards. The play-index stack has the same
        // nesting order; a single Aux flag would let the nested After consume the outer play.
        x.PushCardPlayPair(BlitzCardPlayPairKind.CalamityAttacks, self.Owner,
            x.B.Powers[self.Index].Id, x.B.Powers[self.Index].Amount);
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (!x.TryTakeCardPlayPair(BlitzCardPlayPairKind.CalamityAttacks, self.Owner,
                x.B.Powers[self.Index].Id, out int amount))
        {
            return;
        }
        if (amount > BlitzLimits.Cards)
            throw new BlitzCapacityException("Cards", BlitzLimits.Cards);
        Span<BlitzCard> generated = stackalloc BlitzCard[Math.Max(0, amount)];
        int count = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.CharacterAttacks,
            amount, distinct: false, BlitzRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < count; index++)
            BlitzKernel.AddGeneratedCard(ref x, generated[index], BlitzPile.Hand);
        if (x.NeedsChoice)
            return;
    }
}

/// <summary>
/// Native <c>CorruptionPower</c>: Skills cost 0 through the late energy-cost pass, and a played Skill
/// changes its result location to the exhaust pile
/// (<c>ModifyEnergyCostInCombatMirrors</c>, <c>ModifyCardPlayResultLocationMirrors</c>). The native
/// <c>AfterModifyingCardPlayResultLocation</c> only flashes; it has no state.
/// </summary>
[BlitzContent("CORRUPTION_POWER")]
public sealed class CorruptionPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override decimal TryModifyEnergyCostInCombatLate(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzCard card, BlitzPile pile, decimal amount)
    {
        if (self.Owner != BlitzLimits.PlayerOwner
            || BlitzRegistry.Card(card.Id).CardType(battle, rules, card) != BlitzCardType.Skill)
        {
            return amount;
        }
        return 0m;
    }

    public override BlitzPile ModifyCardPlayResultLocation(ref BlitzExec x, BlitzListener self, in BlitzCard card,
        bool isAutoPlay, int energyValue, BlitzPile location, out BlitzPilePosition position)
    {
        position = BlitzPilePosition.Bottom;
        if (self.Owner != BlitzLimits.PlayerOwner
            || BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Skill)
        {
            return location;
        }
        return BlitzPile.Exhaust;
    }

    public override void AfterModifyingCardPlayResultLocation(ref BlitzExec x, BlitzListener self, in BlitzCard card,
        BlitzPile location)
    {
    }
}

/// <summary>
/// Native <c>FreeAttackPower</c>: Attack cards in hand or the play pile cost 0 through the late cost
/// pass, and the power loses one stack on <c>BeforeCardPlayed</c> for such an Attack
/// (<c>ModifyEnergyCostInCombatMirrors.HandleFreeAttackPower</c>,
/// <c>BeforeCardPlayedMirrors.HandleFreeAttackPower</c>).
/// </summary>
[BlitzContent("FREE_ATTACK_POWER")]
public sealed class FreeAttackPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombatLate(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzCard card, BlitzPile pile, decimal amount)
    {
        if (self.Owner != BlitzLimits.PlayerOwner
            || BlitzRegistry.Card(card.Id).CardType(battle, rules, card) != BlitzCardType.Attack
            || pile is not (BlitzPile.Hand or BlitzPile.Play))
        {
            return amount;
        }
        return 0m;
    }

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Owner != BlitzLimits.PlayerOwner
            || BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Attack)
        {
            return;
        }
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }
}

/// <summary>
/// Native <c>OneTwoPunchPower</c>: each played Attack owned by the power owner gets one extra play;
/// <c>AfterModifyingCardPlayCount</c> decrements the power, and the player turn end removes it
/// (<c>ModifyCardPlayCountMirrors</c>, <c>CorePowerSupport.TriggerTransientSideTurnEndPowers</c>).
/// </summary>
[BlitzContent("ONE_TWO_PUNCH_POWER")]
public sealed class OneTwoPunchPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override int ModifyCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card, byte target,
        int playCount)
    {
        if (self.Owner != BlitzLimits.PlayerOwner
            || BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Attack)
        {
            return playCount;
        }
        return playCount + 1;
    }

    public override void AfterModifyingCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }
}

/// <summary>
/// Native <c>ViciousPower</c>: when its owner applies Vulnerable, draw one card per stack
/// (<c>PowerLifecycleSupport.ResolvePowerAmountChanges</c>'s Vicious case). The draw happens when the
/// queued amount change is resolved, not inside the effect that applied Vulnerable.
/// </summary>
[BlitzContent("VICIOUS_POWER")]
public sealed class ViciousPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterPowerAmountChanged(ref BlitzExec x, BlitzListener self, ushort powerId, int delta,
        byte applier, byte target)
    {
        if (self.Owner != BlitzLimits.PlayerOwner
            || delta <= 0
            || applier != self.Owner
            || !BlitzRegistry.TryPower("VULNERABLE_POWER", out ushort vulnerable)
            || powerId != vulnerable)
        {
            return;
        }
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            BlitzKernel.Draw(ref x, amount, fromHandDraw: false);
    }
}
