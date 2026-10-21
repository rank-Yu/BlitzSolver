using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Potions;

// Each OnUse is the potion's case in PotionOnUseSupport.Use. Self-targeted potions act on the
// player (Use resolves a missing target to the owner when it is a valid target).

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
