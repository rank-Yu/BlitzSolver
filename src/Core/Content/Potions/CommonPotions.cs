using BlitzSolver.Engine;

namespace BlitzSolver.Content.Potions;

[ContentId("FIRE_POTION")]
public sealed class FirePotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // FirePotion when target != null: Damage(target, Damage, Damage.Props, owner).
    public override void OnUse(ref Exec x)
    {
        if (x.Target == Limits.NoOwner)
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = x.Target;
        Kernel.Damage(ref x, targets, Kernel.PotionVar(x, this, 0), Kernel.PotionVarProps(x, this, 0),
            Limits.PlayerOwner, hasCardSource: false, default);
    }
}

[ContentId("BLOCK_POTION")]
public sealed class BlockPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    // BlockPotion: GainBlock(player, Block, Unpowered).
    public override void OnUse(ref Exec x)
        => Kernel.GainBlock(ref x, Limits.PlayerOwner, Kernel.PotionVar(x, this, 0), ValueProps.Unpowered, fromCard: false);
}

[ContentId("STRENGTH_POTION")]
public sealed class StrengthPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    // StrengthPotion: combat.Apply<StrengthPower>(player, Strength, owner).
    public override void OnUse(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StrengthPower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
}

[ContentId("ENERGY_POTION")]
public sealed class EnergyPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    // EnergyPotion: GainEnergy(player, Energy).
    public override void OnUse(ref Exec x) => Kernel.GainEnergy(ref x, Kernel.PotionVar(x, this, 0));
}

[ContentId("ATTACK_POTION")]
public sealed class AttackPotion : PotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [CardPools.CharacterAll];

    public override void OnUse(ref Exec x)
        => GenerateToHand.Choose(ref x, CardType.Attack, CardPools.CharacterAll);
}

[ContentId("SKILL_POTION")]
public sealed class SkillPotion : PotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [CardPools.CharacterAll];

    public override void OnUse(ref Exec x)
        => GenerateToHand.Choose(ref x, CardType.Skill, CardPools.CharacterAll);
}

[ContentId("POWER_POTION")]
public sealed class PowerPotion : PotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [CardPools.CharacterAll];

    public override void OnUse(ref Exec x)
        => GenerateToHand.Choose(ref x, CardType.Power, CardPools.CharacterAll);
}

[ContentId("COLORLESS_POTION")]
public sealed class ColorlessPotion : PotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [CardPools.ColorlessAll];

    public override void OnUse(ref Exec x)
        => GenerateToHand.Choose(ref x, null, CardPools.ColorlessAll);
}

[ContentId("EXPLOSIVE_AMPOULE")]
public sealed class ExplosiveAmpoule : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // ExplosiveAmpoule: simulator.Damage(combat.HittableEnemies, Damage, Damage.Props, owner).
    public override void OnUse(ref Exec x)
        => CardEffects.DamageAllEnemies(ref x, Kernel.PotionVar(x, this, 0),
            Kernel.PotionVarProps(x, this, 0), Limits.PlayerOwner);
}

[ContentId("DEXTERITY_POTION")]
public sealed class DexterityPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    // DexterityPotion: combat.Apply<DexterityPower>(player, Dexterity, owner).
    public override void OnUse(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.DexterityPower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
}

[ContentId("FLEX_POTION")]
public sealed class FlexPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    // FlexPotion: combat.ApplyTemporaryStrengthGain<FlexPotionPower>(player, Strength, owner).
    public override void OnUse(ref Exec x)
        => Kernel.ApplyTemporaryStrength(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.FlexPotionPower>(), (int)Kernel.PotionVar(x, this, 0),
            Limits.PlayerOwner, sign: 1);
}

[ContentId("SPEED_POTION")]
public sealed class SpeedPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    // SpeedPotion: combat.ApplyTemporaryDexterity<SpeedPotionPower>(player, Dexterity, owner).
    // TemporaryDexterityPower.BeforeApplied applies Dexterity first, then the temporary row.
    public override void OnUse(ref Exec x)
        => Kernel.ApplyTemporaryDexterity(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.SpeedPotionPower>(), (int)Kernel.PotionVar(x, this, 0),
            Limits.PlayerOwner);
}

[ContentId("VULNERABLE_POTION")]
public sealed class VulnerablePotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    // VulnerablePotion when target != null: combat.Apply<VulnerablePower>(target, Vulnerable, owner).
    public override void OnUse(ref Exec x)
    {
        if (x.Target == Limits.NoOwner)
            return;
        Kernel.ApplyPower(ref x, x.Target, Registry.PowerId<Powers.VulnerablePower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
    }
}

[ContentId("WEAK_POTION")]
public sealed class WeakPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["WeakPower"];

    // WeakPotion when target != null: combat.Apply<WeakPower>(target, Weak, owner).
    public override void OnUse(ref Exec x)
    {
        if (x.Target == Limits.NoOwner)
            return;
        Kernel.ApplyPower(ref x, x.Target, Registry.PowerId<Powers.WeakPower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
    }
}

/// <summary><c>FocusPotion</c>: <c>combat.Apply&lt;FocusPower&gt;(player, FocusPower, owner)</c>.</summary>
[ContentId("FOCUS_POTION")]
public sealed class FocusPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower"];

    public override void OnUse(ref Exec x)
    {
        Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.FocusPower>(), (int)Kernel.PotionVar(x, this, 0),
            Limits.PlayerOwner);
    }
}

/// <summary><c>PoisonPotion</c>: applies Poison to the chosen enemy (case PoisonPotion when target != null).</summary>
[ContentId("POISON_POTION")]
public sealed class PoisonPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override void OnUse(ref Exec x)
    {
        if (x.Target == Limits.NoOwner)
            return;
        Kernel.ApplyPower(ref x, x.Target, Registry.PowerId<Powers.PoisonPower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
    }
}

/// <summary><c>PotionOfDoom</c>: applies Doom to the chosen enemy (case PotionOfDoom when target != null).</summary>
[ContentId("POTION_OF_DOOM")]
public sealed class PotionOfDoom : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower"];

    public override void OnUse(ref Exec x)
    {
        if (x.Target == Limits.NoOwner)
            return;
        Kernel.ApplyPower(ref x, x.Target, Registry.PowerId<Powers.DoomPower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
    }
}

[ContentId("BLOOD_POTION")]
public sealed class BloodPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["HealPercent"];

    // BloodPotion: Heal(player, MaxHp * HealPercent / 100m).
    public override void OnUse(ref Exec x)
        => Kernel.Heal(ref x, Limits.PlayerOwner,
            x.B.Player.MaxHp * Kernel.PotionVar(x, this, 0) / 100m);
}

[ContentId("SWIFT_POTION")]
public sealed class SwiftPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // SwiftPotion: Draw(player, Cards).
    public override void OnUse(ref Exec x)
        => Kernel.Draw(ref x, (int)Kernel.PotionVar(x, this, 0), fromHandDraw: false);
}

[ContentId("STAR_POTION")]
public sealed class StarPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Stars"];

    // StarPotion: PlayerCmd.GainStars(Stars variable, target.Player) through
    // CombatPredictionSimulator.GainStars (PotionOnUseSupport.Use case StarPotion).
    public override void OnUse(ref Exec x)
    {
        Kernel.GainStars(ref x, (int)Kernel.PotionVar(x, this, 0));
    }
}
