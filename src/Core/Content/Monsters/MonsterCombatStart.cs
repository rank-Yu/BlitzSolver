// Native opening-combat values are captured by the importer. These overrides keep only
// structural initial-state choices used by the combat state-machine compiler.
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
