using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Orbs;

// The five vanilla orbs. Values come from the native orb classes (decompiled 0.111.0):
// LightningOrb.PassiveVal/EvokeVal = 3/8, FrostOrb = 2/5, DarkOrb.PassiveVal = 6 and
// DarkOrb._evokeVal starts at 6, PlasmaOrb = 1/2, GlassOrb._passiveVal starts at 4 and its
// evoke is PassiveVal * 2. Every value goes through OrbModel.ModifyOrbValue (Focus) as the native
// property does. Target selection and RNG usage mirror OrbMirrors:
//   - Lightning/Glass use HittableEnemies in roster order; Lightning consumes CombatTargets only when
//     it has to pick a target (Passive without an explicit target, Evoke).
//   - Dark evokes the minimum-CurrentHp hittable enemy (first on ties).
//   - Frost blocks the owner (Hibernate only matters in multiplayer, which Flash rejects).

[FlashContent("DARK_ORB")]
public sealed class DarkOrb : FlashOrbDef
{
    public override int InitialAux => 6;

    public override decimal BasePassiveValue(in FlashBattle battle, in FlashOrb orb) => 6m;

    public override decimal BaseEvokeValue(in FlashBattle battle, in FlashOrb orb) => orb.Aux;

    // DarkOrb.EvokeVal is the raw _evokeVal; ModifyOrbValue is only part of PassiveVal (the passive
    // accumulation), not the evoke damage.
    public override bool EvokeUsesModifyOrbValue => false;

    public override void Passive(ref FlashExec x, int orbIndex, byte target)
    {
        if (target != FlashLimits.NoOwner)
            throw new FlashUnsupportedException("暗球被动不能指定目标。");
        decimal value = FlashHooks.ModifyOrbValue(x.B, x.R, x.B.Orbs[orbIndex], 6m);
        if (value != decimal.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new FlashUnsupportedException($"暗球累积值 {value} 不是整数。");
        long accumulated = (long)x.B.Orbs[orbIndex].Aux + (int)value;
        if (accumulated is < int.MinValue or > int.MaxValue)
            throw new FlashUnsupportedException($"暗球累积值 {accumulated} 超出 Flash 的整数表示。");
        x.B.Orbs[orbIndex].Aux = (int)accumulated;
    }

    public override int Evoke(ref FlashExec x, in FlashOrb orb, scoped Span<byte> targets)
    {
        Span<byte> candidates = stackalloc byte[FlashLimits.Enemies];
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
        FlashKernel.Damage(ref x, targets[..1], orb.Aux, FlashValueProps.Unpowered, FlashLimits.PlayerOwner,
            hasCardSource: false, default);
        return 1;
    }

    internal static int HittableEnemies(in FlashBattle battle, Span<byte> output)
    {
        int count = 0;
        for (int position = 0; position < battle.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(battle.Roster[position]);
            if (FlashKernel.IsHittable(battle, owner))
                output[count++] = owner;
        }
        return count;
    }
}

[FlashContent("FROST_ORB")]
public sealed class FrostOrb : FlashOrbDef
{
    public override decimal BasePassiveValue(in FlashBattle battle, in FlashOrb orb) => 2m;

    public override decimal BaseEvokeValue(in FlashBattle battle, in FlashOrb orb) => 5m;

    public override void Passive(ref FlashExec x, int orbIndex, byte target)
    {
        if (target != FlashLimits.NoOwner)
            throw new FlashUnsupportedException("冰球被动不能指定目标。");
        decimal value = FlashHooks.ModifyOrbValue(x.B, x.R, x.B.Orbs[orbIndex], 2m);
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, value, FlashValueProps.Unpowered, fromCard: false);
    }

    public override int Evoke(ref FlashExec x, in FlashOrb orb, scoped Span<byte> targets)
    {
        targets[0] = FlashLimits.PlayerOwner;
        decimal value = FlashHooks.ModifyOrbValue(x.B, x.R, orb, 5m);
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, value, FlashValueProps.Unpowered, fromCard: false);
        return 1;
    }
}

[FlashContent("GLASS_ORB")]
public sealed class GlassOrb : FlashOrbDef
{
    public override int InitialAux => 4;

    public override decimal BasePassiveValue(in FlashBattle battle, in FlashOrb orb) => orb.Aux;

    public override decimal BaseEvokeValue(in FlashBattle battle, in FlashOrb orb) => orb.Aux;

    public override void Passive(ref FlashExec x, int orbIndex, byte target)
    {
        if (target != FlashLimits.NoOwner)
            throw new FlashUnsupportedException("玻璃球被动不能指定目标。");
        decimal value = FlashHooks.ModifyOrbValue(x.B, x.R, x.B.Orbs[orbIndex], x.B.Orbs[orbIndex].Aux);
        if (value <= 0m)
            return;
        // Native: _passiveVal = max(0, _passiveVal - 1) after reading the (modified) PassiveVal.
        x.B.Orbs[orbIndex].Aux = Math.Max(0, x.B.Orbs[orbIndex].Aux - 1);
        Span<byte> candidates = stackalloc byte[FlashLimits.Enemies];
        int count = DarkOrb.HittableEnemies(x.B, candidates);
        if (count == 0)
            return;
        FlashKernel.Damage(ref x, candidates[..count], value, FlashValueProps.Unpowered, FlashLimits.PlayerOwner,
            hasCardSource: false, default);
    }

    public override int Evoke(ref FlashExec x, in FlashOrb orb, scoped Span<byte> targets)
    {
        decimal value = FlashHooks.ModifyOrbValue(x.B, x.R, orb, orb.Aux) * 2m;
        if (value <= 0m)
            return 0;
        int count = DarkOrb.HittableEnemies(x.B, targets);
        if (count == 0)
            return 0;
        FlashKernel.Damage(ref x, targets[..count], value, FlashValueProps.Unpowered, FlashLimits.PlayerOwner,
            hasCardSource: false, default);
        return count;
    }

    public override int RetentionMultiplier(in FlashBattle battle, int aliveEnemyCount) => aliveEnemyCount;
}

[FlashContent("LIGHTNING_ORB")]
public sealed class LightningOrb : FlashOrbDef
{
    public override decimal BasePassiveValue(in FlashBattle battle, in FlashOrb orb) => 3m;

    public override decimal BaseEvokeValue(in FlashBattle battle, in FlashOrb orb) => 8m;

    public override void Passive(ref FlashExec x, int orbIndex, byte target)
    {
        FlashOrb orb = x.B.Orbs[orbIndex];
        decimal value = FlashHooks.ModifyOrbValue(x.B, x.R, orb, 3m);
        Deal(ref x, orb, value, target);
    }

    public override int Evoke(ref FlashExec x, in FlashOrb orb, scoped Span<byte> targets)
    {
        decimal value = FlashHooks.ModifyOrbValue(x.B, x.R, orb, 8m);
        return Deal(ref x, orb, value, FlashLimits.NoOwner, targets);
    }

    private static void Deal(ref FlashExec x, in FlashOrb orb, decimal value, byte target)
    {
        Span<byte> one = stackalloc byte[1];
        if (Deal(ref x, orb, value, target, one) == 0)
            return;
    }

    private static int Deal(ref FlashExec x, in FlashOrb orb, decimal value, byte target, scoped Span<byte> targets)
    {
        Span<byte> candidates = stackalloc byte[FlashLimits.Enemies];
        int count = DarkOrb.HittableEnemies(x.B, candidates);
        if (count == 0)
            return 0;
        if (target == FlashLimits.NoOwner)
            target = candidates[x.B.RngStream(FlashRngStream.CombatTargets).NextInt(0, count)];
        targets[0] = target;
        FlashKernel.Damage(ref x, targets[..1], value, FlashValueProps.Unpowered, FlashLimits.PlayerOwner,
            hasCardSource: false, default);
        return 1;
    }
}

[FlashContent("PLASMA_ORB")]
public sealed class PlasmaOrb : FlashOrbDef
{
    public override decimal BasePassiveValue(in FlashBattle battle, in FlashOrb orb) => 1m;

    public override decimal BaseEvokeValue(in FlashBattle battle, in FlashOrb orb) => 2m;

    // PlasmaOrb.PassiveVal is the literal 1; OrbMirrors.GetPassiveValue does not run ModifyOrbValue
    // for it, unlike Lightning/Frost/Dark/Glass.
    public override bool PassiveUsesModifyOrbValue => false;

    // PlasmaOrb overrides AfterTurnStartOrbTrigger instead of BeforeTurnEndOrbTrigger.
    public override void BeforeTurnEndOrbTrigger(ref FlashExec x, int orbIndex)
    {
    }

    public override void AfterTurnStartOrbTrigger(ref FlashExec x, int orbIndex)
        => FlashKernel.TriggerOrbPassive(ref x, orbIndex);

    public override void Passive(ref FlashExec x, int orbIndex, byte target)
    {
        if (target != FlashLimits.NoOwner)
            throw new FlashUnsupportedException("等离子球被动不能指定目标。");
        FlashKernel.GainEnergy(ref x, 1m);
    }

    public override int Evoke(ref FlashExec x, in FlashOrb orb, scoped Span<byte> targets)
    {
        targets[0] = FlashLimits.PlayerOwner;
        FlashKernel.GainEnergy(ref x, 2m);
        return 1;
    }
}
