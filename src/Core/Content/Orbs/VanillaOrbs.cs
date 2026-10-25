using BlitzSolver.Engine;

namespace BlitzSolver.Content.Orbs;

[BlitzContent("DARK_ORB")]
public sealed class DarkOrb : BlitzOrbDef
{
    public override int InitialAux => 6;

    public override decimal BasePassiveValue(in BlitzBattle battle, in BlitzOrb orb) => 6m;

    public override decimal BaseEvokeValue(in BlitzBattle battle, in BlitzOrb orb) => orb.Aux;

    // DarkOrb.EvokeVal is the raw _evokeVal; ModifyOrbValue is only part of PassiveVal (the passive
    // accumulation), not the evoke damage.
    public override bool EvokeUsesModifyOrbValue => false;

    public override void Passive(ref BlitzExec x, int orbIndex, byte target)
    {
        if (target != BlitzLimits.NoOwner)
            throw new BlitzUnsupportedException("暗球被动不能指定目标。");
        decimal value = BlitzHooks.ModifyOrbValue(x.B, x.R, x.B.Orbs[orbIndex], 6m);
        if (value != decimal.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new BlitzUnsupportedException($"暗球累积值 {value} 不是整数。");
        long accumulated = (long)x.B.Orbs[orbIndex].Aux + (int)value;
        if (accumulated is < int.MinValue or > int.MaxValue)
            throw new BlitzUnsupportedException($"暗球累积值 {accumulated} 超出 Blitz 的整数表示。");
        x.B.Orbs[orbIndex].Aux = (int)accumulated;
    }

    public override int Evoke(ref BlitzExec x, in BlitzOrb orb, scoped Span<byte> targets)
    {
        Span<byte> candidates = stackalloc byte[BlitzLimits.Enemies];
        int count = HittableEnemies(x.B, candidates);
        if (count == 0)
            return 0;
        int weakest = 0;
        for (int index = 1; index < count; index++)
        {
            if (x.B.Creature(candidates[index]).CurrentHp < x.B.Creature(candidates[weakest]).CurrentHp)
                weakest = index;
        }
        targets[0] = candidates[weakest];
        // DarkOrb.EvokeVal returns _evokeVal without ModifyOrbValue.
        BlitzKernel.Damage(ref x, targets[..1], orb.Aux, BlitzValueProps.Unpowered, BlitzLimits.PlayerOwner,
            hasCardSource: false, default);
        return 1;
    }

    internal static int HittableEnemies(in BlitzBattle battle, Span<byte> output)
    {
        int count = 0;
        for (int position = 0; position < battle.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(battle.Roster[position]);
            if (BlitzKernel.IsHittable(battle, owner))
                output[count++] = owner;
        }
        return count;
    }
}

[BlitzContent("FROST_ORB")]
public sealed class FrostOrb : BlitzOrbDef
{
    public override decimal BasePassiveValue(in BlitzBattle battle, in BlitzOrb orb) => 2m;

    public override decimal BaseEvokeValue(in BlitzBattle battle, in BlitzOrb orb) => 5m;

    public override void Passive(ref BlitzExec x, int orbIndex, byte target)
    {
        if (target != BlitzLimits.NoOwner)
            throw new BlitzUnsupportedException("冰球被动不能指定目标。");
        decimal value = BlitzHooks.ModifyOrbValue(x.B, x.R, x.B.Orbs[orbIndex], 2m);
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, value, BlitzValueProps.Unpowered, fromCard: false);
    }

    public override int Evoke(ref BlitzExec x, in BlitzOrb orb, scoped Span<byte> targets)
    {
        targets[0] = BlitzLimits.PlayerOwner;
        decimal value = BlitzHooks.ModifyOrbValue(x.B, x.R, orb, 5m);
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, value, BlitzValueProps.Unpowered, fromCard: false);
        return 1;
    }
}

[BlitzContent("GLASS_ORB")]
public sealed class GlassOrb : BlitzOrbDef
{
    public override int InitialAux => 4;

    public override decimal BasePassiveValue(in BlitzBattle battle, in BlitzOrb orb) => orb.Aux;

    public override decimal BaseEvokeValue(in BlitzBattle battle, in BlitzOrb orb) => orb.Aux;

    public override void Passive(ref BlitzExec x, int orbIndex, byte target)
    {
        if (target != BlitzLimits.NoOwner)
            throw new BlitzUnsupportedException("玻璃球被动不能指定目标。");
        decimal value = BlitzHooks.ModifyOrbValue(x.B, x.R, x.B.Orbs[orbIndex], x.B.Orbs[orbIndex].Aux);
        if (value <= 0m)
            return;
        // Native: _passiveVal = max(0, _passiveVal - 1) after reading the (modified) PassiveVal.
        x.B.Orbs[orbIndex].Aux = Math.Max(0, x.B.Orbs[orbIndex].Aux - 1);
        Span<byte> candidates = stackalloc byte[BlitzLimits.Enemies];
        int count = DarkOrb.HittableEnemies(x.B, candidates);
        if (count == 0)
            return;
        BlitzKernel.Damage(ref x, candidates[..count], value, BlitzValueProps.Unpowered, BlitzLimits.PlayerOwner,
            hasCardSource: false, default);
    }

    public override int Evoke(ref BlitzExec x, in BlitzOrb orb, scoped Span<byte> targets)
    {
        decimal value = BlitzHooks.ModifyOrbValue(x.B, x.R, orb, orb.Aux) * 2m;
        if (value <= 0m)
            return 0;
        int count = DarkOrb.HittableEnemies(x.B, targets);
        if (count == 0)
            return 0;
        BlitzKernel.Damage(ref x, targets[..count], value, BlitzValueProps.Unpowered, BlitzLimits.PlayerOwner,
            hasCardSource: false, default);
        return count;
    }

    public override int RetentionMultiplier(in BlitzBattle battle, int aliveEnemyCount) => aliveEnemyCount;
}

[BlitzContent("LIGHTNING_ORB")]
public sealed class LightningOrb : BlitzOrbDef
{
    public override decimal BasePassiveValue(in BlitzBattle battle, in BlitzOrb orb) => 3m;

    public override decimal BaseEvokeValue(in BlitzBattle battle, in BlitzOrb orb) => 8m;

    public override void Passive(ref BlitzExec x, int orbIndex, byte target)
    {
        BlitzOrb orb = x.B.Orbs[orbIndex];
        decimal value = BlitzHooks.ModifyOrbValue(x.B, x.R, orb, 3m);
        Deal(ref x, orb, value, target);
    }

    public override int Evoke(ref BlitzExec x, in BlitzOrb orb, scoped Span<byte> targets)
    {
        decimal value = BlitzHooks.ModifyOrbValue(x.B, x.R, orb, 8m);
        return Deal(ref x, orb, value, BlitzLimits.NoOwner, targets);
    }

    private static void Deal(ref BlitzExec x, in BlitzOrb orb, decimal value, byte target)
    {
        Span<byte> one = stackalloc byte[1];
        if (Deal(ref x, orb, value, target, one) == 0)
            return;
    }

    private static int Deal(ref BlitzExec x, in BlitzOrb orb, decimal value, byte target, scoped Span<byte> targets)
    {
        Span<byte> candidates = stackalloc byte[BlitzLimits.Enemies];
        int count = DarkOrb.HittableEnemies(x.B, candidates);
        if (count == 0)
            return 0;
        if (target == BlitzLimits.NoOwner)
            target = candidates[x.B.RngStream(BlitzRngStream.CombatTargets).NextInt(0, count)];
        targets[0] = target;
        BlitzKernel.Damage(ref x, targets[..1], value, BlitzValueProps.Unpowered, BlitzLimits.PlayerOwner,
            hasCardSource: false, default);
        return 1;
    }
}

[BlitzContent("PLASMA_ORB")]
public sealed class PlasmaOrb : BlitzOrbDef
{
    public override decimal BasePassiveValue(in BlitzBattle battle, in BlitzOrb orb) => 1m;

    public override decimal BaseEvokeValue(in BlitzBattle battle, in BlitzOrb orb) => 2m;

    // PlasmaOrb.PassiveVal is the literal 1; OrbMirrors.GetPassiveValue does not run ModifyOrbValue
    // for it, unlike Lightning/Frost/Dark/Glass.
    public override bool PassiveUsesModifyOrbValue => false;

    // PlasmaOrb overrides AfterTurnStartOrbTrigger instead of BeforeTurnEndOrbTrigger.
    public override void BeforeTurnEndOrbTrigger(ref BlitzExec x, int orbIndex)
    {
    }

    public override void AfterTurnStartOrbTrigger(ref BlitzExec x, int orbIndex)
        => BlitzKernel.TriggerOrbPassive(ref x, orbIndex);

    public override void Passive(ref BlitzExec x, int orbIndex, byte target)
    {
        if (target != BlitzLimits.NoOwner)
            throw new BlitzUnsupportedException("等离子球被动不能指定目标。");
        BlitzKernel.GainEnergy(ref x, 1m);
    }

    public override int Evoke(ref BlitzExec x, in BlitzOrb orb, scoped Span<byte> targets)
    {
        targets[0] = BlitzLimits.PlayerOwner;
        BlitzKernel.GainEnergy(ref x, 2m);
        return 1;
    }
}
