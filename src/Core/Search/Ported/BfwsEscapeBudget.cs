// Ported from CombatSolver src/Search/BfwsEscapeBudget.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

// Shared by all familiar descendants of one nearest novel ancestor. A novel
// descendant starts its own allowance; siblings never replenish this allowance.
internal sealed class BfwsEscapeBudget(int maximum)
{
    public int Admitted { get; private set; }

    public bool TryAdmit()
    {
        if (Admitted >= maximum) return false;
        Admitted++;
        return true;
    }
}
