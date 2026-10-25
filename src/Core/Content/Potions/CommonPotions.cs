using BlitzSolver.Engine;

namespace BlitzSolver.Content.Potions;

[BlitzContent("FIRE_POTION")]
public sealed class FirePotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // FirePotion when target != null: Damage(target, Damage, Damage.Props, owner).
    public override void OnUse(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = x.Target;
        BlitzKernel.Damage(ref x, targets, BlitzKernel.PotionVar(x, this, 0), BlitzKernel.PotionVarProps(x, this, 0),
            BlitzLimits.PlayerOwner, hasCardSource: false, default);
    }
}

[BlitzContent("BLOCK_POTION")]
public sealed class BlockPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    // BlockPotion: GainBlock(player, Block, Unpowered).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, BlitzKernel.PotionVar(x, this, 0), BlitzValueProps.Unpowered, fromCard: false);
}

[BlitzContent("STRENGTH_POTION")]
public sealed class StrengthPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    // StrengthPotion: combat.Apply<StrengthPower>(player, Strength, owner).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("ENERGY_POTION")]
public sealed class EnergyPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    // EnergyPotion: GainEnergy(player, Energy).
    public override void OnUse(ref BlitzExec x) => BlitzKernel.GainEnergy(ref x, BlitzKernel.PotionVar(x, this, 0));
}

[BlitzContent("ATTACK_POTION")]
public sealed class AttackPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAll];

    public override void OnUse(ref BlitzExec x)
        => GenerateToHand.Choose(ref x, BlitzCardType.Attack, BlitzCardPools.CharacterAll);
}

[BlitzContent("SKILL_POTION")]
public sealed class SkillPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAll];

    public override void OnUse(ref BlitzExec x)
        => GenerateToHand.Choose(ref x, BlitzCardType.Skill, BlitzCardPools.CharacterAll);
}

[BlitzContent("POWER_POTION")]
public sealed class PowerPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAll];

    public override void OnUse(ref BlitzExec x)
        => GenerateToHand.Choose(ref x, BlitzCardType.Power, BlitzCardPools.CharacterAll);
}

[BlitzContent("COLORLESS_POTION")]
public sealed class ColorlessPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.ColorlessAll];

    public override void OnUse(ref BlitzExec x)
        => GenerateToHand.Choose(ref x, null, BlitzCardPools.ColorlessAll);
}

[BlitzContent("EXPLOSIVE_AMPOULE")]
public sealed class ExplosiveAmpoule : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // ExplosiveAmpoule: simulator.Damage(combat.HittableEnemies, Damage, Damage.Props, owner).
    public override void OnUse(ref BlitzExec x)
        => BlitzCardEffects.DamageAllEnemies(ref x, BlitzKernel.PotionVar(x, this, 0),
            BlitzKernel.PotionVarProps(x, this, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("DEXTERITY_POTION")]
public sealed class DexterityPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    // DexterityPotion: combat.Apply<DexterityPower>(player, Dexterity, owner).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.DexterityPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("FLEX_POTION")]
public sealed class FlexPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    // FlexPotion: combat.ApplyTemporaryStrengthGain<FlexPotionPower>(player, Strength, owner).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.ApplyTemporaryStrength(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.FlexPotionPower>(), (int)BlitzKernel.PotionVar(x, this, 0),
            BlitzLimits.PlayerOwner, sign: 1);
}

[BlitzContent("SPEED_POTION")]
public sealed class SpeedPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    // SpeedPotion: combat.ApplyTemporaryDexterity<SpeedPotionPower>(player, Dexterity, owner).
    // SimulatedCombatState.ApplyTemporaryStat applies the Dexterity before the temporary power on a
    // first application, and only the applied delta when the temporary power was already present.
    public override void OnUse(ref BlitzExec x)
    {
        int amount = (int)BlitzKernel.PotionVar(x, this, 0);
        ushort temporary = BlitzRegistry.PowerId<Powers.SpeedPotionPower>();
        ushort dexterity = BlitzRegistry.PowerId<Powers.DexterityPower>();
        bool alreadyApplied = BlitzKernel.GetAmount(x.B, BlitzLimits.PlayerOwner, temporary) != 0;
        if (!alreadyApplied)
            BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, dexterity, amount, BlitzLimits.PlayerOwner);
        int applied = BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, temporary, amount, BlitzLimits.PlayerOwner);
        if (alreadyApplied && applied != 0)
            BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, dexterity, applied, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("VULNERABLE_POTION")]
public sealed class VulnerablePotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    // VulnerablePotion when target != null: combat.Apply<VulnerablePower>(target, Vulnerable, owner).
    public override void OnUse(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.VulnerablePower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("WEAK_POTION")]
public sealed class WeakPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["WeakPower"];

    // WeakPotion when target != null: combat.Apply<WeakPower>(target, Weak, owner).
    public override void OnUse(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.WeakPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
    }
}

/// <summary><c>FocusPotion</c>: <c>combat.Apply&lt;FocusPower&gt;(player, FocusPower, owner)</c>.</summary>
[BlitzContent("FOCUS_POTION")]
public sealed class FocusPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower"];

    public override void OnUse(ref BlitzExec x)
    {
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.FocusPower>(), (int)BlitzKernel.PotionVar(x, this, 0),
            BlitzLimits.PlayerOwner);
    }
}

/// <summary><c>PoisonPotion</c>: applies Poison to the chosen enemy (case PoisonPotion when target != null).</summary>
[BlitzContent("POISON_POTION")]
public sealed class PoisonPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override void OnUse(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.PoisonPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
    }
}

/// <summary><c>PotionOfDoom</c>: applies Doom to the chosen enemy (case PotionOfDoom when target != null).</summary>
[BlitzContent("POTION_OF_DOOM")]
public sealed class PotionOfDoom : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower"];

    public override void OnUse(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.DoomPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("BLOOD_POTION")]
public sealed class BloodPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["HealPercent"];

    // BloodPotion: Heal(player, MaxHp * HealPercent / 100m).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.Heal(ref x, BlitzLimits.PlayerOwner,
            x.B.Player.MaxHp * BlitzKernel.PotionVar(x, this, 0) / 100m);
}

[BlitzContent("SWIFT_POTION")]
public sealed class SwiftPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // SwiftPotion: Draw(player, Cards).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.Draw(ref x, (int)BlitzKernel.PotionVar(x, this, 0), fromHandDraw: false);
}

[BlitzContent("STAR_POTION")]
public sealed class StarPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Stars"];

    // StarPotion: PlayerCmd.GainStars(Stars variable, target.Player) through
    // CombatPredictionSimulator.GainStars (PotionOnUseSupport.Use case StarPotion).
    public override void OnUse(ref BlitzExec x)
    {
        BlitzKernel.GainStars(ref x, (int)BlitzKernel.PotionVar(x, this, 0));
    }
}
