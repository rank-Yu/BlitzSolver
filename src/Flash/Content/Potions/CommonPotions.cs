using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Potions;

[FlashContent("FIRE_POTION")]
public sealed class FirePotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // FirePotion when target != null: Damage(target, Damage, Damage.Props, owner).
    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = x.Target;
        FlashKernel.Damage(ref x, targets, FlashKernel.PotionVar(x, this, 0), FlashKernel.PotionVarProps(x, this, 0),
            FlashLimits.PlayerOwner, hasCardSource: false, default);
    }
}

[FlashContent("BLOCK_POTION")]
public sealed class BlockPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    // BlockPotion: GainBlock(player, Block, Unpowered).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, FlashKernel.PotionVar(x, this, 0), FlashValueProps.Unpowered, fromCard: false);
}

[FlashContent("STRENGTH_POTION")]
public sealed class StrengthPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    // StrengthPotion: combat.Apply<StrengthPower>(player, Strength, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("ENERGY_POTION")]
public sealed class EnergyPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    // EnergyPotion: GainEnergy(player, Energy).
    public override void OnUse(ref FlashExec x) => FlashKernel.GainEnergy(ref x, FlashKernel.PotionVar(x, this, 0));
}

[FlashContent("ATTACK_POTION")]
public sealed class AttackPotion : FlashPotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void OnUse(ref FlashExec x)
        => GenerateToHand.Choose(ref x, FlashCardType.Attack, FlashCardPools.CharacterAll);
}

[FlashContent("SKILL_POTION")]
public sealed class SkillPotion : FlashPotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void OnUse(ref FlashExec x)
        => GenerateToHand.Choose(ref x, FlashCardType.Skill, FlashCardPools.CharacterAll);
}

[FlashContent("POWER_POTION")]
public sealed class PowerPotion : FlashPotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void OnUse(ref FlashExec x)
        => GenerateToHand.Choose(ref x, FlashCardType.Power, FlashCardPools.CharacterAll);
}

[FlashContent("COLORLESS_POTION")]
public sealed class ColorlessPotion : FlashPotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.ColorlessAll];

    public override void OnUse(ref FlashExec x)
        => GenerateToHand.Choose(ref x, null, FlashCardPools.ColorlessAll);
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

/// <summary><c>FocusPotion</c>: <c>combat.Apply&lt;FocusPower&gt;(player, FocusPower, owner)</c>.</summary>
[FlashContent("FOCUS_POTION")]
public sealed class FocusPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower"];

    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.FocusPower>(), (int)FlashKernel.PotionVar(x, this, 0),
            FlashLimits.PlayerOwner);
    }
}

/// <summary><c>PoisonPotion</c>: applies Poison to the chosen enemy (case PoisonPotion when target != null).</summary>
[FlashContent("POISON_POTION")]
public sealed class PoisonPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.PoisonPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
    }
}

/// <summary><c>PotionOfDoom</c>: applies Doom to the chosen enemy (case PotionOfDoom when target != null).</summary>
[FlashContent("POTION_OF_DOOM")]
public sealed class PotionOfDoom : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower"];

    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.DoomPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
    }
}

[FlashContent("BLOOD_POTION")]
public sealed class BloodPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["HealPercent"];

    // BloodPotion: Heal(player, MaxHp * HealPercent / 100m).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.Heal(ref x, FlashLimits.PlayerOwner,
            x.B.Player.MaxHp * FlashKernel.PotionVar(x, this, 0) / 100m);
}

[FlashContent("SWIFT_POTION")]
public sealed class SwiftPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // SwiftPotion: Draw(player, Cards).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.Draw(ref x, (int)FlashKernel.PotionVar(x, this, 0), fromHandDraw: false);
}

[FlashContent("STAR_POTION")]
public sealed class StarPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Stars"];

    // StarPotion: PlayerCmd.GainStars(Stars variable, target.Player) through
    // CombatPredictionSimulator.GainStars (PotionOnUseSupport.Use case StarPotion).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.GainStars(ref x, (int)FlashKernel.PotionVar(x, this, 0));
    }
}
