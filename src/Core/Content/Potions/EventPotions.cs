using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Potions;

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

/// <summary>
/// <c>Ambergris</c>: heal <c>MaxHp * HealPercent / 100</c>, then apply 1 <c>AmbergrisPower</c>
/// (<c>PotionOnUseSupport.Use</c> case Ambergris; the power grants the extra turn).
/// </summary>
[FlashContent("AMBERGRIS")]
public sealed class AmbergrisPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["HealPercent"];

    public override void OnUse(ref FlashExec x)
    {
        decimal percent = FlashKernel.PotionVar(x, this, 0);
        FlashKernel.Heal(ref x, FlashLimits.PlayerOwner, x.B.Creature(FlashLimits.PlayerOwner).MaxHp * percent / 100m);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.AmbergrisPower>(), 1, FlashLimits.PlayerOwner);
    }
}

[FlashContent("GLOWWATER_POTION")]
public sealed class GlowwaterPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // GlowwaterPotion: ExhaustHand(player), then Draw(player, Cards).
    public override void OnUse(ref FlashExec x)
    {
        int count = x.B.HandCount;
        for (int index = 0; index < count && x.B.HandCount > 0; index++)
        {
            FlashKernel.Exhaust(ref x, FlashPile.Hand, 0);
            if (x.NeedsChoice)
                return;
        }
        FlashKernel.Draw(ref x, (int)FlashKernel.PotionVar(x, this, 0), fromHandDraw: false);
    }
}
