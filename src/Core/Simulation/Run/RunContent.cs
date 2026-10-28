using System.Runtime.InteropServices;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// The initial current values of the run odds set read from the native constructors. Only the
/// unknown-map-point odds are needed by the retained map-entry prediction.
/// </summary>
public sealed class ContentOdds
{
    public required float Monster { get; init; }
    public required float Elite { get; init; }
    public required float Treasure { get; init; }
    public required float Shop { get; init; }
}

/// <summary>Which native <c>CardRarityOddsType</c> a combat room uses.</summary>
public enum RoomRarityOdds : byte
{
    Regular = 0,
    Elite = 1,
    Boss = 2,
}

/// <summary>
/// Opening-combat <c>AbstractModel</c> hooks that the first playable boundary depends on. The
/// content table records, per listener model, which bits the canonical type overrides and which
/// of those the retained setup path implements; any other override is unsupported.
/// </summary>
public enum ContentSetupHook : uint
{
    None = 0,
    AfterRoomEntered = 1u << 0,
    BeforeCombatStart = 1u << 1,
    BeforeCombatStartLate = 1u << 2,
    BeforeSideTurnStart = 1u << 3,
    AfterEnergyReset = 1u << 4,
    AfterEnergyResetLate = 1u << 5,
    BeforeHandDraw = 1u << 6,
    BeforeHandDrawLate = 1u << 7,
    ModifyHandDraw = 1u << 8,
    ModifyHandDrawLate = 1u << 9,
    AfterPlayerTurnStartEarly = 1u << 10,
    AfterPlayerTurnStart = 1u << 11,
    AfterPlayerTurnStartLate = 1u << 12,
    AfterSideTurnStart = 1u << 13,
    AfterSideTurnStartLate = 1u << 14,
    ModifyMaxEnergy = 1u << 15,
    ModifyOrbValue = 1u << 16,
    ShouldProcurePotion = 1u << 17,
    AfterCardDrawnEarly = 1u << 18,
    AfterCardDrawn = 1u << 19,
    AfterCardEnteredCombat = 1u << 20,
    AfterCardGeneratedForCombat = 1u << 21,
    ModifyShuffleOrder = 1u << 22,
    AfterAutoPrePlayPhaseEntered = 1u << 23,
    AfterAutoPrePlayPhaseEnteredLate = 1u << 24,
}

/// <summary>Overridden and implemented setup-hook bits of one listener model.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ContentSetupHookMask
{
    public uint Overridden;
    public uint Implemented;

    public readonly uint Unsupported => Overridden & ~Implemented;

    public static ContentSetupHookMask From(
        IEnumerable<(ContentSetupHook Hook, bool Overridden, bool Implemented)> values)
    {
        ContentSetupHookMask mask = default;
        foreach ((ContentSetupHook hook, bool overridden, bool implemented) in values)
        {
            if (overridden)
                mask.Overridden |= (uint)hook;
            if (implemented)
                mask.Implemented |= (uint)hook;
        }
        return mask;
    }
}
