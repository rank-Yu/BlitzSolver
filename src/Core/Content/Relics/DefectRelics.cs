using BlitzSolver.Engine;

namespace BlitzSolver.Content.Relics;

[BlitzContent("CRACKED_CORE")]
public sealed class CrackedCore : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Lightning"];

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player || x.B.PlayerTurn > 1)
            return;
        int count = (int)x.R.RelicVar(x.B.Relics[self.Index].Id, 0);
        BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("LIGHTNING_ORB"), count);
    }
}

[BlitzContent("RUNIC_CAPACITOR")]
public sealed class RunicCapacitor : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player || x.B.PlayerTurn > 1)
            return;
        BlitzKernel.AddOrbSlots(ref x.B, (int)x.R.RelicVar(x.B.Relics[self.Index].Id, 0));
    }
}

[BlitzContent("SYMBIOTIC_VIRUS")]
public sealed class SymbioticVirus : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Dark"];

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player || x.B.PlayerTurn > 1)
            return;
        BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("DARK_ORB"), (int)x.R.RelicVar(x.B.Relics[self.Index].Id, 0));
    }
}

[BlitzContent("GOLD_PLATED_CABLES")]
public sealed class GoldPlatedCables : BlitzRelicDef
{
    public override int ModifyOrbPassiveTriggerCounts(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzOrb orb, int orbIndex, int triggerCount)
        => orbIndex == 0 ? triggerCount + 1 : triggerCount;
}

[BlitzContent("POWER_CELL")]
public sealed class PowerCell : BlitzRelicDef
{
    // The existing engine's BeforeSideTurnStartMirrors.Vanilla registered PowerCell as ignored; the
    // definition exists so a deck containing it can be admitted, and it has no combat effect.
}

[BlitzContent("REPTILE_TRINKET")]
public sealed class ReptileTrinket : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void AfterPotionUsed(ref BlitzExec x, BlitzListener self, ushort potionId)
    {
        if (x.B.Player.IsDead)
            return;
        BlitzKernel.ApplyTemporaryStrength(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.ReptileTrinketPower>(), (int)x.R.RelicVar(x.B.Relics[self.Index].Id, 0),
            BlitzLimits.PlayerOwner, sign: 1);
    }
}
