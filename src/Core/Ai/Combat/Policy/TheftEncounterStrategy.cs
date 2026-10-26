// Ported from CombatSolver src/Search/TheftEncounterStrategy.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace BlitzSolver.Ai.Combat.Policy;

internal static class TheftEncounterStrategy
{
    public static int CompareRecovery(TheftPolicy? policy,
        bool candidateWon, int candidateOutstanding, bool currentWon, int currentOutstanding)
    {
        int victory = currentWon.CompareTo(candidateWon);
        return victory != 0 ? victory : policy == TheftPolicy.PreserveResources
            ? candidateOutstanding.CompareTo(currentOutstanding) : 0;
    }
}
