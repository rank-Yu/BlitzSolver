// Opening-combat values now come from ContentTable: the exporter executes the native
// MonsterModel.AfterAddedToRoom once per (monster, slot, ascension) and records the resulting powers
// and HP changes. This file only keeps the structural initial-state choices that are part of the
// state-machine compile, not content numbers.
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Monsters.Act1
{

public sealed partial class Nibbit : MonsterDef
{
    public override int CombatStartConditional(byte variant, in MonsterRules rules, int node)
    {
        if (rules.Nodes[node].NativeId != "INIT_MOVE")
            return -1;
        return variant switch
        {
            1 => rules.NodeIndex("SLICE_MOVE"),
            2 => rules.NodeIndex("HISS_MOVE"),
            3 => rules.NodeIndex("BUTT_MOVE"),
            _ => -1,
        };
    }
}

public sealed partial class Toadpole : MonsterDef
{
    public override int CombatStartConditional(byte variant, in MonsterRules rules, int node)
    {
        if (rules.Nodes[node].NativeId != "INIT_MOVE")
            return -1;
        return variant switch
        {
            1 => rules.NodeIndex("SPIKEN_MOVE"),
            2 => rules.NodeIndex("WHIRL_MOVE"),
            _ => -1,
        };
    }
}

}
