// Ported from CombatSolver src/Search/RelicCounterPolicy.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

internal enum RelicCounterId
{
    HappyFlower, FakeHappyFlower, Pendulum, PollinousCore, PenNib,
    Nunchaku, TuningFork, JossPaper, IronClub, GalacticDust, MeatOnTheBone,
}

internal readonly record struct RelicCounterTarget(RelicCounterId Id, int Minimum, int Maximum, int HpAllowance, int Period, int Priority = 1);

internal readonly record struct RelicCounterEvaluation(ulong SatisfiedMask, ulong TargetMask, int HpCredit, int Distance)
{
    public ulong CounterValues { get; init; }
    public int SatisfiedPriority { get; init; }
    public int HealingHpCredit { get; init; }
    public ulong TargetMinimums { get; init; }
    public ulong TargetMaximums { get; init; }
    public int Value(RelicCounterId id) => (int)((CounterValues >> ((int)id * 4)) & 15);
    public int Minimum(RelicCounterId id) => (int)((TargetMinimums >> ((int)id * 4)) & 15);
    public int Maximum(RelicCounterId id) => (int)((TargetMaximums >> ((int)id * 4)) & 15);
    public bool Satisfied => SatisfiedMask == TargetMask;
    public int SatisfiedCount => System.Numerics.BitOperations.PopCount(SatisfiedMask);
}

internal static class RelicCounterPolicy
{
    public static RelicCounterEvaluation Add(RelicCounterEvaluation evaluation, RelicCounterTarget target, int value)
    {
        ulong bit = 1UL << (int)target.Id;
        bool satisfied = value >= target.Minimum && value <= target.Maximum;
        bool rewardGoal = satisfied && target.Id != RelicCounterId.MeatOnTheBone;
        int distance = satisfied ? 0 : value < target.Minimum ? target.Minimum - value : target.Period - value + target.Minimum;
        return new(evaluation.SatisfiedMask | (satisfied ? bit : 0), evaluation.TargetMask | bit,
            evaluation.HpCredit + (rewardGoal ? target.HpAllowance : 0), evaluation.Distance + distance)
        {
            SatisfiedPriority = evaluation.SatisfiedPriority + (rewardGoal ? 1 << ((target.Priority - 1) * 4) : 0),
            CounterValues = (evaluation.CounterValues & ~(15UL << ((int)target.Id * 4)))
                | ((ulong)value << ((int)target.Id * 4)),
            TargetMinimums = (evaluation.TargetMinimums & ~(15UL << ((int)target.Id * 4)))
                | ((ulong)target.Minimum << ((int)target.Id * 4)),
            TargetMaximums = (evaluation.TargetMaximums & ~(15UL << ((int)target.Id * 4)))
                | ((ulong)target.Maximum << ((int)target.Id * 4)),
        };
    }
}
