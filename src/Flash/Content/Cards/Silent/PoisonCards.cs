using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

// Poison cards. Existing engine: CardOnPlaySupport (BubbleBubble/DeadlyPoison/Haze/Snakebite),
// CorePowerSupport.ApplyCardPowers (PoisonedStab/LegSweep/...), CardOnPlaySupport.Batch042
// (BouncingFlask), CardPowerOnPlaySupport (Envenom/NoxiousFumes/CorrosiveWave/Accelerant), and the
// poison trigger in CorePowerSupport.TriggerPoison (kernel FlashKernel.TriggerPoison).

[FlashContent("DEADLY_POISON")]
public sealed class DeadlyPoison : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.PoisonPower>(), 0);
}

[FlashContent("POISONED_STAB")]
public sealed class PoisonedStab : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "PoisonPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.PoisonPower>(), 1);
    }
}

[FlashContent("SNAKEBITE")]
public sealed class Snakebite : FlashCardDef
{
    // Snakebite.cs: a Retain Skill that applies Poison; it has no damage variable.
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.PoisonPower>(), 0);
}

[FlashContent("BUBBLE_BUBBLE")]
public sealed class BubbleBubble : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    // CardOnPlaySupport.BubbleBubble: only when the target already has Poison.
    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        if (FlashKernel.GetAmount(x.B, x.Target, FlashRegistry.PowerId<Powers.PoisonPower>()) <= 0)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.PoisonPower>(), 0);
    }
}

[FlashContent("BOUNCING_FLASK")]
public sealed class BouncingFlask : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower", "Repeat"];

    // CardOnPlaySupport.Batch042.ApplyBouncingFlask: Repeat times, a random living hittable enemy
    // (CombatTargets NextItem), applying Poison; an empty candidate list stops without consuming RNG.
    public override void OnPlay(ref FlashExec x)
    {
        int repeats = (int)FlashCardEffects.Var(x, 1);
        ushort poison = FlashRegistry.PowerId<Powers.PoisonPower>();
        int amount = (int)FlashCardEffects.Var(x, 0);
        Span<byte> candidates = stackalloc byte[FlashLimits.Enemies];
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            int count = 0;
            for (int position = 0; position < x.B.RosterCount; position++)
            {
                byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
                if (FlashKernel.IsHittable(x.B, owner))
                    candidates[count++] = owner;
            }
            if (count == 0)
                break;
            byte target = candidates[x.B.RngStream(FlashRngStream.CombatTargets).NextInt(0, count)];
            FlashKernel.ApplyPower(ref x, target, poison, amount, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("HAZE")]
public sealed class Haze : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower", "WeakPower"];

    // CardOnPlaySupport.Haze: Poison to every hittable enemy, then Weak to every hittable enemy. The
    // existing case applies both passes without resolving the power amount changes in between.
    public override void OnPlay(ref FlashExec x)
    {
        ushort poison = FlashRegistry.PowerId<Powers.PoisonPower>();
        ushort weak = FlashRegistry.PowerId<Powers.WeakPower>();
        int poisonAmount = (int)FlashCardEffects.Var(x, 0);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyPower(ref x, owner, poison, poisonAmount, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
        int weakAmount = (int)FlashCardEffects.Var(x, 1);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyPower(ref x, owner, weak, weakAmount, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("NOXIOUS_FUMES")]
public sealed class NoxiousFumes : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPerTurn"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.NoxiousFumesPower>(), 0);
}

[FlashContent("ENVENOM")]
public sealed class Envenom : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["EnvenomPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.EnvenomPower>(), 0);
}

[FlashContent("CORROSIVE_WAVE")]
public sealed class CorrosiveWave : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CorrosiveWave"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.CorrosiveWavePower>(), 0);
}

[FlashContent("ACCELERANT")]
public sealed class Accelerant : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Accelerant"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.AccelerantPower>(), 0);
}

[FlashContent("MALAISE")]
public sealed class Malaise : FlashCardDef
{
    // CorePowerSupport.ApplyCardPowers.Malaise: X value (after ModifyXValue) plus one when upgraded;
    // Strength -malaise then Weak malaise on the target. The card has no dynamic variables.
    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        int amount = FlashKernel.ResolveEnergyXValue(ref x) + (x.Card.Upgrade != 0 ? 1 : 0);
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.StrengthPower>(), -amount,
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.WeakPower>(), amount,
            FlashLimits.PlayerOwner);
    }
}
