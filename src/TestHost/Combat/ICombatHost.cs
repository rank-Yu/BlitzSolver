// Shared by tests/Differential (message-loop pumping, no Godot) and the in-game Mod (native NGame
// startup and frame waits). ScenarioBuilder owns all fixture construction, so both hosts run the
// exact same injection sequence and differ only in how the run and the main loop are driven.
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace BlitzSolver.TestHost;

internal interface ICombatHost
{
    /// <summary>Creates a new single-player run, finalizes relics, launches it and enters act 0.</summary>
    Task<RunState> StartRunAsync(CharacterModel character, int ascension, string seed);

    /// <summary>Waits until the native combat reaches the local player's Play phase.</summary>
    Task<CombatState> WaitForPlayableCombatAsync();
}
