namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Instance-identity powers. Existing-engine paths are named on each definition; the decompiled
// models are only used to confirm constants and conditions.

/// <summary>
/// Native <c>MonologuePower</c> (instanced): every card the player plays while it is active adds its
/// Strength to the owner after the play, then the applied total is removed at the owner's side turn
/// end. Strength lives in Aux0, StrengthApplied in Aux1; the transient pending list is in
/// <see cref="FlashExec.MonologuePending"/>. Existing engine:
/// <c>CorePowerSupport.CapturePendingMonologues/ResolveMonologues</c> and
/// <c>EndTurnPowerSupport.Batch048</c>'s Monologue case.
/// </summary>
[FlashContent("MONOLOGUE_POWER")]
public sealed class MonologuePower : FlashPowerDef
{
    public override bool IsIdentityContent => true;

    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsInstanced => true;

    public override void EndTurnBatch048(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        int strengthApplied = power.Aux1;
        int amount = power.Amount;
        int strengthValue = power.Aux0;
        byte owner = power.Owner;
        if (strengthApplied != 0 && FlashRegistry.TryPower("STRENGTH_POWER", out ushort strength))
        {
            // Retiring the temporary Strength buff can remove it (and shift this row), so address the
            // Monologue instance by its captured value, not by the pre-application row index.
            FlashKernel.ApplyPower(ref x, owner, strength, -strengthApplied, owner);
        }
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly FlashPower candidate = ref x.B.Powers[index];
            if (candidate.Id == Id && candidate.Owner == owner && candidate.Amount == amount
                && candidate.Aux0 == strengthValue && candidate.Aux1 == strengthApplied)
            {
                FlashKernel.SetPowerAmountAt(ref x.B, index, 0);
                return;
            }
        }
    }
}

/// <summary>
/// Native <c>SwordSagePower</c>: its only state is the amount. The per-blade applied replay bonus is
/// normalized by <c>FlashKernel.NormalizeCardAfflictions</c> exactly like
/// <c>SimulatedCombatState.NormalizeSwordSageReplays</c>; the native <c>AfterCardEnteredCombat</c> /
/// <c>AfterRemoved</c> / <c>AfterPowerAmountChanged</c> have no separate mirror in the existing engine.
/// </summary>
[FlashContent("SWORD_SAGE_POWER")]
public sealed class SwordSagePower : FlashPowerDef
{
    public override bool IsIdentityContent => true;

    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;
}

/// <summary>
/// Native <c>OrbitPower</c> (instanced): four energy spent trigger <c>Amount</c> energy. Aux0 is the
/// accumulated remainder (<c>SimulatedCombatState._orbitEnergyRemainders</c>,
/// <c>PowerLifecycleSupport.AfterEnergySpent</c>).
/// </summary>
[FlashContent("ORBIT_POWER")]
public sealed class OrbitPower : FlashPowerDef
{
    public override bool IsIdentityContent => true;

    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsInstanced => true;

    public override void AfterEnergySpent(ref FlashExec x, FlashListener self, in FlashCard card, int amount)
    {
        if (amount <= 0 || self.Owner != FlashLimits.PlayerOwner)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        int total = power.Aux0 + amount;
        power.Aux0 = total % 4;
        int triggers = total / 4;
        if (triggers > 0)
        {
            FlashKernel.GainEnergy(ref x, power.Amount * triggers);
        }
    }
}

/// <summary>
/// Native <c>PaleBlueDotPower</c>: a single stacking power whose per-turn latch (Aux0) prevents more
/// than one next-turn draw grant. <c>PowerLifecycleSupport.AfterCardPlayed</c> and
/// <c>EndTurnPowerSupport.TriggerRegular</c>'s reset are mirrored by the kernel and this definition.
/// </summary>
[FlashContent("PALE_BLUE_DOT_POWER")]
public sealed class PaleBlueDotPower : FlashPowerDef
{
    public override bool IsIdentityContent => true;

    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates && self.Owner == FlashLimits.PlayerOwner)
            x.B.Powers[self.Index].Aux0 = 0;
    }
}

/// <summary>
/// Native <c>PhantomBladesPower</c>: Shivs gain Retain on application and on entering combat, and the
/// first Shiv powered attack of a turn gains <c>Amount</c> damage.
/// <c>PhantomBladesPowerMirrors.AfterApplied/AfterCardEnteredCombat</c> and
/// <c>ModifyDamageMirrors.HandlePhantomBladesPower</c>.
/// </summary>
[FlashContent("PHANTOM_BLADES_POWER")]
public sealed class PhantomBladesPower : FlashPowerDef
{
    public override bool IsIdentityContent => true;

    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardEnteredCombat(ref FlashExec x, FlashListener self, FlashPile pile, int index,
        in FlashCard card)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        if ((stats.Tags & FlashCardTag.Shiv) == 0)
            return;
        FlashPiles.At(ref x.B, pile, index).Keywords |= FlashKeywords.Retain;
    }

    public override decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self, in FlashDamageContext damage,
        decimal amount)
    {
        if (!damage.Props.IsPoweredAttack()
            || !damage.HasCardSource
            || (x.R.Card(damage.CardSource.Id, damage.CardSource.Upgrade).Tags & FlashCardTag.Shiv) == 0
            || damage.Dealer != self.Owner
            || x.PlayIndex > 0
            || x.B.Counter(FlashCounter.ShivsPlayedThisTurn) != 0)
        {
            return 0m;
        }
        return x.B.Powers[self.Index].Amount;
    }
}

/// <summary>
/// Native <c>SpectrumShiftPower</c>: BeforeHandDraw adds <c>Amount</c> distinct colorless cards to the
/// hand. Existing engine: <c>TurnStartPowerSupport.ContinueBeforeHandDraw</c>'s SpectrumShift case.
/// </summary>
[FlashContent("SPECTRUM_SHIFT_POWER")]
public sealed class SpectrumShiftPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.ColorlessAll];

    public override void BeforeHandDraw(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        if (amount > FlashLimits.Cards)
            throw new FlashCapacityException("GeneratedCards", FlashLimits.Cards);
        Span<FlashCard> generated = stackalloc FlashCard[amount];
        int sampled = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.ColorlessAll, amount,
            distinct: true, FlashRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < sampled; index++)
        {
            FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Hand);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>NightmarePower</c> (instanced): at the owner's BeforeHandDraw, add <c>Amount</c> clones of
/// the selected card to the hand, then remove the instance. The selected card is a detached value in
/// <see cref="FlashBattle.DetachedCards"/> (Aux0 = slot + 1). Existing engine:
/// <c>GenerateTurnStartPowerCards</c> and <c>SetNightmareSelection</c>.
/// </summary>
[FlashContent("NIGHTMARE_POWER")]
public sealed class NightmarePower : FlashPowerDef
{
    public override bool IsIdentityContent => true;

    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsInstanced => true;

    public override bool UsesDetachedCard => true;

    public override void BeforeHandDraw(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        int amount = power.Amount;
        ushort slot = (ushort)power.Aux0;
        FlashCard selected = FlashKernel.DetachedCard(x.B, power);
        for (int index = 0; index < amount; index++)
        {
            FlashKernel.AddGeneratedCard(ref x, FlashKernel.CloneCard(selected), FlashPile.Hand);
            if (x.NeedsChoice)
                return;
        }
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly FlashPower candidate = ref x.B.Powers[index];
            if (candidate.Id == Id && candidate.Owner == self.Owner && candidate.Aux0 == slot)
            {
                FlashKernel.SetPowerAmountAt(ref x.B, index, 0);
                return;
            }
        }
    }
}
