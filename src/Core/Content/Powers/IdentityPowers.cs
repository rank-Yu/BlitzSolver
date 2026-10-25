using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// Native <c>MonologuePower</c> (instanced): every card the player plays while it is active adds its
/// Strength to the owner after the play, then the applied total is removed at the owner's side turn
/// end. Strength lives in Aux0, StrengthApplied in Aux1; the transient pending list is in
/// <see cref="BlitzExec.MonologuePending"/>. Existing engine:
/// <c>CorePowerSupport.CapturePendingMonologues/ResolveMonologues</c> and
/// <c>EndTurnPowerSupport.Batch048</c>'s Monologue case.
/// </summary>
[BlitzContent("MONOLOGUE_POWER")]
public sealed class MonologuePower : BlitzPowerDef
{
    public override bool IsIdentityContent => true;

    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool IsInstanced => true;

    public override void EndTurnBatch048(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (!participates)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        int strengthApplied = power.Aux1;
        int amount = power.Amount;
        int strengthValue = power.Aux0;
        byte owner = power.Owner;
        if (strengthApplied != 0 && BlitzRegistry.TryPower("STRENGTH_POWER", out ushort strength))
        {
            // Retiring the temporary Strength buff can remove it (and shift this row), so address the
            // Monologue instance by its captured value, not by the pre-application row index.
            BlitzKernel.ApplyPower(ref x, owner, strength, -strengthApplied, owner);
        }
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly BlitzPower candidate = ref x.B.Powers[index];
            if (candidate.Id == Id && candidate.Owner == owner && candidate.Amount == amount
                && candidate.Aux0 == strengthValue && candidate.Aux1 == strengthApplied)
            {
                BlitzKernel.SetPowerAmountAt(ref x.B, index, 0);
                return;
            }
        }
    }
}

/// <summary>
/// Native <c>SwordSagePower</c>: its only state is the amount. The per-blade applied replay bonus is
/// normalized by <c>BlitzKernel.NormalizeCardAfflictions</c> exactly like
/// <c>SimulatedCombatState.NormalizeSwordSageReplays</c>; the native <c>AfterCardEnteredCombat</c> /
/// <c>AfterRemoved</c> / <c>AfterPowerAmountChanged</c> have no separate mirror in the existing engine.
/// </summary>
[BlitzContent("SWORD_SAGE_POWER")]
public sealed class SwordSagePower : BlitzPowerDef
{
    public override bool IsIdentityContent => true;

    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;
}

/// <summary>
/// Native <c>OrbitPower</c> (instanced): four energy spent trigger <c>Amount</c> energy. Aux0 is the
/// accumulated remainder (<c>SimulatedCombatState._orbitEnergyRemainders</c>,
/// <c>PowerLifecycleSupport.AfterEnergySpent</c>).
/// </summary>
[BlitzContent("ORBIT_POWER")]
public sealed class OrbitPower : BlitzPowerDef
{
    public override bool IsIdentityContent => true;

    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool IsInstanced => true;

    public override void AfterEnergySpent(ref BlitzExec x, BlitzListener self, in BlitzCard card, int amount)
    {
        if (amount <= 0 || self.Owner != BlitzLimits.PlayerOwner)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        int total = power.Aux0 + amount;
        power.Aux0 = total % 4;
        int triggers = total / 4;
        if (triggers > 0)
        {
            BlitzKernel.GainEnergy(ref x, power.Amount * triggers);
        }
    }
}

/// <summary>
/// Native <c>PaleBlueDotPower</c>: a single stacking power whose per-turn latch (Aux0) prevents more
/// than one next-turn draw grant. <c>PowerLifecycleSupport.AfterCardPlayed</c> and
/// <c>EndTurnPowerSupport.TriggerRegular</c>'s reset are mirrored by the kernel and this definition.
/// </summary>
[BlitzContent("PALE_BLUE_DOT_POWER")]
public sealed class PaleBlueDotPower : BlitzPowerDef
{
    public override bool IsIdentityContent => true;

    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates && self.Owner == BlitzLimits.PlayerOwner)
            x.B.Powers[self.Index].Aux0 = 0;
    }
}

/// <summary>
/// Native <c>PhantomBladesPower</c>: Shivs gain Retain on application and on entering combat, and the
/// first Shiv powered attack of a turn gains <c>Amount</c> damage.
/// <c>PhantomBladesPowerMirrors.AfterApplied/AfterCardEnteredCombat</c> and
/// <c>ModifyDamageMirrors.HandlePhantomBladesPower</c>.
/// </summary>
[BlitzContent("PHANTOM_BLADES_POWER")]
public sealed class PhantomBladesPower : BlitzPowerDef
{
    public override bool IsIdentityContent => true;

    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardEnteredCombat(ref BlitzExec x, BlitzListener self, BlitzPile pile, int index,
        in BlitzCard card)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
        if ((stats.Tags & BlitzCardTag.Shiv) == 0)
            return;
        BlitzPiles.At(ref x.B, pile, index).Keywords |= BlitzKeywords.Retain;
    }

    public override decimal ModifyDamageAdditive(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage,
        decimal amount)
    {
        if (!damage.Props.IsPoweredAttack()
            || !damage.HasCardSource
            || (x.R.Card(damage.CardSource.Id, damage.CardSource.Upgrade).Tags & BlitzCardTag.Shiv) == 0
            || damage.Dealer != self.Owner
            || x.PlayIndex > 0
            || x.B.Counter(BlitzCounter.ShivsPlayedThisTurn) != 0)
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
[BlitzContent("SPECTRUM_SHIFT_POWER")]
public sealed class SpectrumShiftPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.ColorlessAll];

    public override void BeforeHandDraw(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        if (amount > BlitzLimits.Cards)
            throw new BlitzCapacityException("GeneratedCards", BlitzLimits.Cards);
        Span<BlitzCard> generated = stackalloc BlitzCard[amount];
        int sampled = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.ColorlessAll, amount,
            distinct: true, BlitzRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < sampled; index++)
        {
            BlitzKernel.AddGeneratedCard(ref x, generated[index], BlitzPile.Hand);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>NightmarePower</c> (instanced): at the owner's BeforeHandDraw, add <c>Amount</c> clones of
/// the selected card to the hand, then remove the instance. The selected card is a detached value in
/// <see cref="BlitzBattle.DetachedCards"/> (Aux0 = slot + 1). Existing engine:
/// <c>GenerateTurnStartPowerCards</c> and <c>SetNightmareSelection</c>.
/// </summary>
[BlitzContent("NIGHTMARE_POWER")]
public sealed class NightmarePower : BlitzPowerDef
{
    public override bool IsIdentityContent => true;

    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool IsInstanced => true;

    public override bool UsesDetachedCard => true;

    public override void BeforeHandDraw(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        int amount = power.Amount;
        ushort slot = (ushort)power.Aux0;
        BlitzCard selected = BlitzKernel.DetachedCard(x.B, power);
        for (int index = 0; index < amount; index++)
        {
            BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CloneCard(selected), BlitzPile.Hand);
            if (x.NeedsChoice)
                return;
        }
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly BlitzPower candidate = ref x.B.Powers[index];
            if (candidate.Id == Id && candidate.Owner == self.Owner && candidate.Aux0 == slot)
            {
                BlitzKernel.SetPowerAmountAt(ref x.B, index, 0);
                return;
            }
        }
    }
}
