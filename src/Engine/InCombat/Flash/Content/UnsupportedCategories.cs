namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Native ids of content that is deliberately outside the single-player vanilla scope. The Bridge
/// classifies a missing definition with these categories instead of reporting it as an undefined card
/// or power; the inventory and reasons are in docs/flash-search/admission-and-verification.md.
/// </summary>
public static class FlashUnsupportedNativeIds
{
    /// <summary>
    /// The six multiplayer-only powers referenced by the 37 multiplayer-only cards
    /// (<c>CoordinatePower</c>..<c>TankPower</c>). They are unreachable from single-player play.
    /// </summary>
    public static readonly string[] MultiplayerOnlyPowers =
    [
        "COORDINATE_POWER",
        "FADE_POWER",
        "HAMMER_TIME_POWER",
        "LEADERSHIP_POWER",
        "ONE_FOR_ALL_POWER",
        "TANK_POWER",
    ];

    /// <summary>Every mock/test power id uses this prefix (<c>MockCardPool</c> card ids use <c>MOCK_</c> too).</summary>
    public const string MockPrefix = "MOCK_";
}
