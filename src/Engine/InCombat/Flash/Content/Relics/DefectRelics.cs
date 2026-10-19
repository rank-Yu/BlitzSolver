namespace CombatSolver.Engine.InCombat.Flash.Content.Relics;

// Defect relics. Existing engine paths:
//   CrackedCore      SimulatedCombatState.PrepareRelicBeforeSideTurnStart's CrackedCore case
//                    (player turn <= 1, OrbChannel<LightningOrb>(DynamicVars["Lightning"])).
//   RunicCapacitor   TriggerRelicsAfterSideTurnStart's RunicCapacitor case (turn <= 1, AddOrbSlots(Repeat)).
//   SymbioticVirus   TriggerRelicsAfterSideTurnStart's SymbioticVirus case (turn <= 1, OrbChannel<DarkOrb>).
//   GoldPlatedCables ModifyOrbPassiveTriggerCountMirrors.HandleGoldPlatedCables (the first orb in the
//                    queue triggers one extra passive).
//   PowerCell        BeforeSideTurnStartMirrors.Vanilla registers it ignored: the existing engine does
//                    not simulate its draw-two effect, so Flash must not either (a definition is still
//                    required for admission).
//   ReptileTrinket   SimulatedCombatState.TriggerRelicsAfterPotionUsed applies the temporary Strength
//                    after every potion use (AfterPotionUsed).
// UnsettlingLamp has no Flash definition: the existing engine's power-amount-given hook depends on the
// card source identity and a per-card doubled-power ledger that Flash does not model.

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
