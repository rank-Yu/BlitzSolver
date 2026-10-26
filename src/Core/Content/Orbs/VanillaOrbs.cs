using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Content.Orbs;

[ContentId("DARK_ORB")]
public sealed class DarkOrb : OrbDef
{
    public override int InitialAux => 6;

    public override decimal BasePassiveValue(in Battle battle, in Orb orb) => 6m;

    public override decimal BaseEvokeValue(in Battle battle, in Orb orb) => orb.Aux;

    // DarkOrb.EvokeVal is the raw _evokeVal; ModifyOrbValue is only part of PassiveVal (the passive
    // accumulation), not the evoke damage.
    public override bool EvokeUsesModifyOrbValue => false;

    public override void Passive(ref Exec x, int orbIndex, byte target)
    {
        if (target != Limits.NoOwner)
            throw new UnsupportedException("暗球被动不能指定目标。");
        decimal value = HookDispatch.ModifyOrbValue(x.B, x.R, x.B.Orbs[orbIndex], 6m);
        if (value != decimal.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new UnsupportedException($"暗球累积值 {value} 不是整数。");
        long accumulated = (long)x.B.Orbs[orbIndex].Aux + (int)value;
        if (accumulated is < int.MinValue or > int.MaxValue)
            throw new UnsupportedException($"暗球累积值 {accumulated} 超出 Blitz 的整数表示。");
        x.B.Orbs[orbIndex].Aux = (int)accumulated;
    }

    public override int Evoke(ref Exec x, in Orb orb, scoped Span<byte> targets)
    {
        Span<byte> candidates = stackalloc byte[Limits.Enemies];
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
        Kernel.Damage(ref x, targets[..1], orb.Aux, ValueProps.Unpowered, Limits.PlayerOwner,
            hasCardSource: false, default);
        return 1;
    }

    internal static int HittableEnemies(in Battle battle, Span<byte> output)
    {
        int count = 0;
        for (int position = 0; position < battle.RosterCount; position++)
        {
            byte owner = Kernel.EnemyOwner(battle.Roster[position]);
            if (Kernel.IsHittable(battle, owner))
                output[count++] = owner;
        }
        return count;
    }
}

[ContentId("FROST_ORB")]
public sealed class FrostOrb : OrbDef
{
    public override decimal BasePassiveValue(in Battle battle, in Orb orb) => 2m;

    public override decimal BaseEvokeValue(in Battle battle, in Orb orb) => 5m;

    public override void Passive(ref Exec x, int orbIndex, byte target)
    {
        if (target != Limits.NoOwner)
            throw new UnsupportedException("冰球被动不能指定目标。");
        decimal value = HookDispatch.ModifyOrbValue(x.B, x.R, x.B.Orbs[orbIndex], 2m);
        Kernel.GainBlock(ref x, Limits.PlayerOwner, value, ValueProps.Unpowered, fromCard: false);
    }

    public override int Evoke(ref Exec x, in Orb orb, scoped Span<byte> targets)
    {
        targets[0] = Limits.PlayerOwner;
        decimal value = HookDispatch.ModifyOrbValue(x.B, x.R, orb, 5m);
        Kernel.GainBlock(ref x, Limits.PlayerOwner, value, ValueProps.Unpowered, fromCard: false);
        return 1;
    }
}

[ContentId("GLASS_ORB")]
public sealed class GlassOrb : OrbDef
{
    public override int InitialAux => 4;

    public override decimal BasePassiveValue(in Battle battle, in Orb orb) => orb.Aux;

    public override decimal BaseEvokeValue(in Battle battle, in Orb orb) => orb.Aux;

    public override void Passive(ref Exec x, int orbIndex, byte target)
    {
        if (target != Limits.NoOwner)
            throw new UnsupportedException("玻璃球被动不能指定目标。");
        decimal value = HookDispatch.ModifyOrbValue(x.B, x.R, x.B.Orbs[orbIndex], x.B.Orbs[orbIndex].Aux);
        if (value <= 0m)
            return;
        // Native: _passiveVal = max(0, _passiveVal - 1) after reading the (modified) PassiveVal.
        x.B.Orbs[orbIndex].Aux = Math.Max(0, x.B.Orbs[orbIndex].Aux - 1);
        Span<byte> candidates = stackalloc byte[Limits.Enemies];
        int count = DarkOrb.HittableEnemies(x.B, candidates);
        if (count == 0)
            return;
        Kernel.Damage(ref x, candidates[..count], value, ValueProps.Unpowered, Limits.PlayerOwner,
            hasCardSource: false, default);
    }

    public override int Evoke(ref Exec x, in Orb orb, scoped Span<byte> targets)
    {
        decimal value = HookDispatch.ModifyOrbValue(x.B, x.R, orb, orb.Aux) * 2m;
        if (value <= 0m)
            return 0;
        int count = DarkOrb.HittableEnemies(x.B, targets);
        if (count == 0)
            return 0;
        Kernel.Damage(ref x, targets[..count], value, ValueProps.Unpowered, Limits.PlayerOwner,
            hasCardSource: false, default);
        return count;
    }

    public override int RetentionMultiplier(in Battle battle, int aliveEnemyCount) => aliveEnemyCount;
}

[ContentId("LIGHTNING_ORB")]
public sealed class LightningOrb : OrbDef
{
    public override decimal BasePassiveValue(in Battle battle, in Orb orb) => 3m;

    public override decimal BaseEvokeValue(in Battle battle, in Orb orb) => 8m;

    public override void Passive(ref Exec x, int orbIndex, byte target)
    {
        Orb orb = x.B.Orbs[orbIndex];
        decimal value = HookDispatch.ModifyOrbValue(x.B, x.R, orb, 3m);
        Deal(ref x, orb, value, target);
    }

    public override int Evoke(ref Exec x, in Orb orb, scoped Span<byte> targets)
    {
        decimal value = HookDispatch.ModifyOrbValue(x.B, x.R, orb, 8m);
        return Deal(ref x, orb, value, Limits.NoOwner, targets);
    }

    private static void Deal(ref Exec x, in Orb orb, decimal value, byte target)
    {
        Span<byte> one = stackalloc byte[1];
        if (Deal(ref x, orb, value, target, one) == 0)
            return;
    }

    private static int Deal(ref Exec x, in Orb orb, decimal value, byte target, scoped Span<byte> targets)
    {
        Span<byte> candidates = stackalloc byte[Limits.Enemies];
        int count = DarkOrb.HittableEnemies(x.B, candidates);
        if (count == 0)
            return 0;
        if (target == Limits.NoOwner)
            target = candidates[x.B.RngStream(RngStream.CombatTargets).NextInt(0, count)];
        targets[0] = target;
        Kernel.Damage(ref x, targets[..1], value, ValueProps.Unpowered, Limits.PlayerOwner,
            hasCardSource: false, default);
        return 1;
    }
}

[ContentId("PLASMA_ORB")]
public sealed class PlasmaOrb : OrbDef
{
    public override decimal BasePassiveValue(in Battle battle, in Orb orb) => 1m;

    public override decimal BaseEvokeValue(in Battle battle, in Orb orb) => 2m;

    // PlasmaOrb.PassiveVal is the literal 1; OrbMirrors.GetPassiveValue does not run ModifyOrbValue
    // for it, unlike Lightning/Frost/Dark/Glass.
    public override bool PassiveUsesModifyOrbValue => false;

    // PlasmaOrb overrides AfterTurnStartOrbTrigger instead of BeforeTurnEndOrbTrigger.
    public override void BeforeTurnEndOrbTrigger(ref Exec x, int orbIndex)
    {
    }

    public override void AfterTurnStartOrbTrigger(ref Exec x, int orbIndex)
        => Kernel.TriggerOrbPassive(ref x, orbIndex);

    public override void Passive(ref Exec x, int orbIndex, byte target)
    {
        if (target != Limits.NoOwner)
            throw new UnsupportedException("等离子球被动不能指定目标。");
        Kernel.GainEnergy(ref x, 1m);
    }

    public override int Evoke(ref Exec x, in Orb orb, scoped Span<byte> targets)
    {
        targets[0] = Limits.PlayerOwner;
        Kernel.GainEnergy(ref x, 2m);
        return 1;
    }
}
