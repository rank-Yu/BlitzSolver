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
