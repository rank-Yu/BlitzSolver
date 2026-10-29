namespace Sts2Solver.Simulation.Combat;

/// <summary>
/// Read-only content values for the opening-combat build. The run layer implements this over the
/// process-local content table; the combat layer only consumes it while the battle is being built.
/// </summary>
public interface ICombatStartContent
{
    /// <summary>Canonical native <c>DynamicVars[name].BaseValue</c> of one relic.</summary>
    decimal RelicVar(ushort relicId, string name);
}

/// <summary>Where a monster's opening power is placed.</summary>
public enum CombatStartPowerTarget : byte
{
    /// <summary>The monster that owns the effect.</summary>
    Owner = 0,

    /// <summary>The local player creature.</summary>
    Player = 1,
}

/// <summary>One power application recorded from a monster's native <c>AfterAddedToRoom</c>.</summary>
public readonly struct CombatStartPower
{
    public required ushort PowerId { get; init; }
    public required int Amount { get; init; }
    public required CombatStartPowerTarget Target { get; init; }

    /// <summary>True when the native applier is the monster itself; false for no applier.</summary>
    public required bool ApplierIsOwner { get; init; }

    /// <summary>Native <c>PowerModel.Target</c> is the local player (WitheringPresence).</summary>
    public required bool TargetsPlayer { get; init; }

    /// <summary>Opening auxiliary value read from the native power (WitheringPresence CardsLeft).</summary>
    public required int Aux0 { get; init; }
}

/// <summary>How a monster changes its own HP in <c>AfterAddedToRoom</c>.</summary>
public enum CombatStartHpMode : byte
{
    None = 0,

    /// <summary>Native DecimillipedeSegment: round up to even, then avoid equal teammate HP.</summary>
    UniqueEven = 1,
}

/// <summary>Opening state recorded for one (monster, slot, ascension) combination.</summary>
public sealed class CombatStartRecord
{
    public required CombatStartPower[] Powers { get; init; }
    public required CombatStartHpMode HpMode { get; init; }

    /// <summary>Step used by <see cref="CombatStartHpMode.UniqueEven"/>; 0 for other modes.</summary>
    public required int UniqueHpStep { get; init; }
}

/// <summary>Helpers for content definitions that read opening values instead of literals.</summary>
public static class CombatStartValues
{
    public static decimal RelicVar(ref Exec x, Listener self, string name)
    {
        if (x.SetupContent is not { } content)
            throw new UnsupportedException("建局遗物变量只能在开战上下文里读取。");
        ushort relicId = x.B.Relics[self.Index].Id;
        return content.RelicVar(relicId, name);
    }

    public static int IntRelicVar(ref Exec x, Listener self, string name)
        => checked((int)RelicVar(ref x, self, name));
}
