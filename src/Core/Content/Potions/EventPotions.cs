using BlitzSolver.Engine;

namespace BlitzSolver.Content.Potions;

[BlitzContent("FOUL_POTION")]
public sealed class FoulPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // FoulPotion in combat: Damage every non-pet living creature (the player and the hittable
    // enemies) for Damage with Damage.Props, dealt by the owner.
    public override void OnUse(ref BlitzExec x)
    {
        Span<byte> targets = stackalloc byte[BlitzLimits.Enemies + 1];
        int count = 0;
        targets[count++] = BlitzLimits.PlayerOwner;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (BlitzKernel.IsHittable(x.B, owner))
                targets[count++] = owner;
        }
        BlitzKernel.Damage(ref x, targets[..count], BlitzKernel.PotionVar(x, this, 0),
            BlitzKernel.PotionVarProps(x, this, 0), BlitzLimits.PlayerOwner, hasCardSource: false, default);
    }
}

/// <summary>
/// <c>Ambergris</c>: heal <c>MaxHp * HealPercent / 100</c>, then apply 1 <c>AmbergrisPower</c>
/// (<c>PotionOnUseSupport.Use</c> case Ambergris; the power grants the extra turn).
/// </summary>
[BlitzContent("AMBERGRIS")]
public sealed class AmbergrisPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["HealPercent"];

    public override void OnUse(ref BlitzExec x)
    {
        decimal percent = BlitzKernel.PotionVar(x, this, 0);
        BlitzKernel.Heal(ref x, BlitzLimits.PlayerOwner, x.B.Creature(BlitzLimits.PlayerOwner).MaxHp * percent / 100m);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.AmbergrisPower>(), 1, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("GLOWWATER_POTION")]
public sealed class GlowwaterPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // GlowwaterPotion: ExhaustHand(player), then Draw(player, Cards).
    public override void OnUse(ref BlitzExec x)
    {
        int count = x.B.HandCount;
        for (int index = 0; index < count && x.B.HandCount > 0; index++)
        {
            BlitzKernel.Exhaust(ref x, BlitzPile.Hand, 0);
            if (x.NeedsChoice)
                return;
        }
        BlitzKernel.Draw(ref x, (int)BlitzKernel.PotionVar(x, this, 0), fromHandDraw: false);
    }
}
