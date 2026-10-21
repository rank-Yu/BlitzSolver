using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Relics;

[FlashContent("CRACKED_CORE")]
public sealed class CrackedCore : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Lightning"];

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player || x.B.PlayerTurn > 1)
            return;
        int count = (int)x.R.RelicVar(x.B.Relics[self.Index].Id, 0);
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("LIGHTNING_ORB"), count);
    }
}

[FlashContent("RUNIC_CAPACITOR")]
public sealed class RunicCapacitor : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player || x.B.PlayerTurn > 1)
            return;
        FlashKernel.AddOrbSlots(ref x.B, (int)x.R.RelicVar(x.B.Relics[self.Index].Id, 0));
    }
}

[FlashContent("SYMBIOTIC_VIRUS")]
public sealed class SymbioticVirus : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Dark"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player || x.B.PlayerTurn > 1)
            return;
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("DARK_ORB"), (int)x.R.RelicVar(x.B.Relics[self.Index].Id, 0));
    }
}

[FlashContent("GOLD_PLATED_CABLES")]
public sealed class GoldPlatedCables : FlashRelicDef
{
    public override int ModifyOrbPassiveTriggerCounts(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashOrb orb, int orbIndex, int triggerCount)
        => orbIndex == 0 ? triggerCount + 1 : triggerCount;
}

[FlashContent("POWER_CELL")]
public sealed class PowerCell : FlashRelicDef
{
    // The existing engine's BeforeSideTurnStartMirrors.Vanilla registered PowerCell as ignored; the
    // definition exists so a deck containing it can be admitted, and it has no combat effect.
}

[FlashContent("REPTILE_TRINKET")]
public sealed class ReptileTrinket : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void AfterPotionUsed(ref FlashExec x, FlashListener self, ushort potionId)
    {
        if (x.B.Player.IsDead)
            return;
        FlashKernel.ApplyTemporaryStrength(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.ReptileTrinketPower>(), (int)x.R.RelicVar(x.B.Relics[self.Index].Id, 0),
            FlashLimits.PlayerOwner, sign: 1);
    }
}
