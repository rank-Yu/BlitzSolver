namespace CombatSolver.Engine.InCombat.Flash.Content.Potions;

// Potions whose existing-engine case deals damage or applies a power. Each OnUse is the potion's
// branch in PotionOnUseSupport.Use; potion variables and their value props come from FlashRules.

[FlashContent("BEETLE_JUICE")]
public sealed class BeetleJuice : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["DamageDecrease", "Repeat"];

    // BeetleJuice when target != null: combat.Apply<ShrinkPower>(target, Repeat.IntValue, owner).
    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.ShrinkPower>(),
            (int)FlashKernel.PotionVar(x, this, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("EXPLOSIVE_AMPOULE")]
public sealed class ExplosiveAmpoule : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // ExplosiveAmpoule: simulator.Damage(combat.HittableEnemies, Damage, Damage.Props, owner).
    public override void OnUse(ref FlashExec x)
        => FlashCardEffects.DamageAllEnemies(ref x, FlashKernel.PotionVar(x, this, 0),
            FlashKernel.PotionVarProps(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("FOUL_POTION")]
public sealed class FoulPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // FoulPotion in combat: Damage every non-pet living creature (the player and the hittable
    // enemies) for Damage with Damage.Props, dealt by the owner.
    public override void OnUse(ref FlashExec x)
    {
        Span<byte> targets = stackalloc byte[FlashLimits.Enemies + 1];
        int count = 0;
        targets[count++] = FlashLimits.PlayerOwner;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (FlashKernel.IsHittable(x.B, owner))
                targets[count++] = owner;
        }
        FlashKernel.Damage(ref x, targets[..count], FlashKernel.PotionVar(x, this, 0),
            FlashKernel.PotionVarProps(x, this, 0), FlashLimits.PlayerOwner, hasCardSource: false, default);
    }
}

[FlashContent("POTION_SHAPED_ROCK")]
public sealed class PotionShapedRock : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // PotionShapedRock when target != null: Damage(target, Damage, Damage.Props, owner).
    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = x.Target;
        FlashKernel.Damage(ref x, targets, FlashKernel.PotionVar(x, this, 0),
            FlashKernel.PotionVarProps(x, this, 0), FlashLimits.PlayerOwner, hasCardSource: false, default);
    }
}

[FlashContent("DEXTERITY_POTION")]
public sealed class DexterityPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    // DexterityPotion: combat.Apply<DexterityPower>(player, Dexterity, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DexterityPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("FLEX_POTION")]
public sealed class FlexPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    // FlexPotion: combat.ApplyTemporaryStrengthGain<FlexPotionPower>(player, Strength, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyTemporaryStrength(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.FlexPotionPower>(), (int)FlashKernel.PotionVar(x, this, 0),
            FlashLimits.PlayerOwner, sign: 1);
}

[FlashContent("FYSH_OIL")]
public sealed class FyshOil : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    // FyshOil: Apply<StrengthPower>(player, Strength, owner), then Apply<DexterityPower>(player, Dexterity, owner).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DexterityPower>(),
            (int)FlashKernel.PotionVar(x, this, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("HEART_OF_IRON")]
public sealed class HeartOfIron : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    // HeartOfIron: combat.Apply<PlatingPower>(player, PlatingPower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.PlatingPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("LIQUID_BRONZE")]
public sealed class LiquidBronze : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["ThornsPower"];

    // LiquidBronze: combat.Apply<ThornsPower>(player, ThornsPower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ThornsPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("MAZALETHS_GIFT")]
public sealed class MazalethsGift : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["RitualPower"];

    // MazalethsGift: combat.Apply<RitualPower>(player, RitualPower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RitualPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("POTION_OF_BINDING")]
public sealed class PotionOfBinding : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower", "WeakPower"];

    // PotionOfBinding: for each hittable enemy, apply Weak with the VulnerablePower amount and
    // Vulnerable with the WeakPower amount (the existing engine's branch swaps the two variables).
    public override void OnUse(ref FlashExec x)
    {
        int weak = (int)FlashKernel.PotionVar(x, this, 0);
        int vulnerable = (int)FlashKernel.PotionVar(x, this, 1);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.WeakPower>(), weak,
                FlashLimits.PlayerOwner);
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.VulnerablePower>(), vulnerable,
                FlashLimits.PlayerOwner);
        }
    }
}

[FlashContent("POWDERED_DEMISE")]
public sealed class PowderedDemise : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Demise"];

    // PowderedDemise when target != null: combat.Apply<DemisePower>(target, Demise, owner).
    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.DemisePower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
    }
}

[FlashContent("SHACKLING_POTION")]
public sealed class ShacklingPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    // ShacklingPotion: combat.ApplyTemporaryStrengthLoss<ShacklingPotionPower> for each living
    // hittable enemy with the Strength amount.
    public override void OnUse(ref FlashExec x)
    {
        int amount = (int)FlashKernel.PotionVar(x, this, 0);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyTemporaryStrength(ref x, owner, FlashRegistry.PowerId<Powers.ShacklingPotionPower>(),
                amount, FlashLimits.PlayerOwner, sign: -1);
        }
    }
}

[FlashContent("SPEED_POTION")]
public sealed class SpeedPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    // SpeedPotion: combat.ApplyTemporaryDexterity<SpeedPotionPower>(player, Dexterity, owner).
    // SimulatedCombatState.ApplyTemporaryStat applies the Dexterity before the temporary power on a
    // first application, and only the applied delta when the temporary power was already present.
    public override void OnUse(ref FlashExec x)
    {
        int amount = (int)FlashKernel.PotionVar(x, this, 0);
        ushort temporary = FlashRegistry.PowerId<Powers.SpeedPotionPower>();
        ushort dexterity = FlashRegistry.PowerId<Powers.DexterityPower>();
        bool alreadyApplied = FlashKernel.GetAmount(x.B, FlashLimits.PlayerOwner, temporary) != 0;
        if (!alreadyApplied)
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, dexterity, amount, FlashLimits.PlayerOwner);
        int applied = FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, temporary, amount, FlashLimits.PlayerOwner);
        if (alreadyApplied && applied != 0)
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, dexterity, applied, FlashLimits.PlayerOwner);
    }
}

[FlashContent("VULNERABLE_POTION")]
public sealed class VulnerablePotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    // VulnerablePotion when target != null: combat.Apply<VulnerablePower>(target, Vulnerable, owner).
    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.VulnerablePower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
    }
}

[FlashContent("WEAK_POTION")]
public sealed class WeakPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["WeakPower"];

    // WeakPotion when target != null: combat.Apply<WeakPower>(target, Weak, owner).
    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.WeakPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
    }
}

[FlashContent("GHOST_IN_A_JAR")]
public sealed class GhostInAJar : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["IntangiblePower"];

    // GhostInAJar: combat.Apply<IntangiblePower>(playerTarget, IntangiblePower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.IntangiblePower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("LUCKY_TONIC")]
public sealed class LuckyTonic : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["BufferPower"];

    // LuckyTonic: combat.Apply<BufferPower>(playerTarget, BufferPower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.BufferPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}
