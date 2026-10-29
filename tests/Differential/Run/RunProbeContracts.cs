using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;

namespace Differential;

internal static class RunProbeContracts
{
    internal static void Check(Battle basis)
    {
        Battle dead = basis;
        ref Enemy enemy = ref dead.Enemies[0];
        enemy.Creature.CurrentHp = 0;
        enemy.DeathPhase = DeathPhase.PermanentlyDead;
        enemy.Flags = EnemyFlags.DeathProcessed;
        int write = 0;
        for (int read = 0; read < dead.RosterCount; read++)
            if (dead.Roster[read] != 0) dead.Roster[write++] = dead.Roster[read];
        dead.RosterCount = (byte)write;
        byte[] original = BattleCodec.Encode(dead);
        CheckPair("removed-permanent-dead", dead, same: true);
        _ = BattleReconciler.Normalize(dead);
        if (!original.AsSpan().SequenceEqual(BattleCodec.Encode(dead)))
            throw new InvalidDataException("Normalize修改了原输入");
        Battle alive = basis;
        alive.Enemies[0].DeathPhase = DeathPhase.None;
        alive.Enemies[0].Creature.CurrentHp = 1;
        alive.Enemies[0].Flags &= ~EnemyFlags.SkipNextMove;
        CheckPair("alive", alive, same: false);
        Battle reviving = dead;
        reviving.Enemies[0].DeathPhase = DeathPhase.Reviving;
        CheckPair("reviving", reviving, same: false);
        Battle roster = dead;
        roster.Roster[roster.RosterCount++] = 0;
        CheckPair("still-in-roster", roster, same: false);
        Battle inCombat = dead;
        inCombat.Enemies[0].Flags |= EnemyFlags.InCombat;
        CheckPair("still-in-combat", inCombat, same: false);
        Battle positiveHp = dead;
        positiveHp.Enemies[0].Creature.CurrentHp = 1;
        CheckPair("positive-hp", positiveHp, same: false);
        Battle other = dead;
        other.Enemies[0].Flags |= EnemyFlags.PerformedMoveThisRound;
        if (BattleReconciler.Same(dead, other))
            throw new InvalidDataException("tombstone合同放宽了其他Flags");
        other = dead;
        other.Enemies[0].Flags = EnemyFlags.Escaped;
        if (BattleReconciler.Same(dead, other))
            throw new InvalidDataException("Escaped不能与DeathProcessed互换");
        Console.WriteLine("CONTRACT tombstone cases=9 passed");
    }

    internal static void CheckEventOptions(MegaCrit.Sts2.Core.Models.EventModel model)
    {
        int calls = 0;
        MegaCrit.Sts2.Core.Events.EventOption repeat = new(model,
            () => { calls++; return Task.CompletedTask; }, "probe", disableOnChosen: false);
        repeat.Chosen().GetAwaiter().GetResult();
        repeat.Chosen().GetAwaiter().GetResult();
        if (!repeat.WasChosen || repeat.IsLocked || calls != 2)
            throw new InvalidDataException("可重复原版选项不能用WasChosen过滤");
        calls = 0;
        MegaCrit.Sts2.Core.Events.EventOption once = new(model,
            () => { calls++; return Task.CompletedTask; }, "probe", disableOnChosen: true);
        once.Chosen().GetAwaiter().GetResult();
        once.Chosen().GetAwaiter().GetResult();
        if (calls != 1) throw new InvalidDataException("原版一次性选项应保持一次执行");
        Console.WriteLine("CONTRACT native event repeat/once cases=2 passed");
    }

    internal static void CheckRunCodec(BlitzSolver.Simulation.Run.RunState run)
    {
        byte[] bytes = BlitzSolver.Simulation.Run.RunCodec.Encode(run);
        var decoded = BlitzSolver.Simulation.Run.RunCodec.Decode(bytes);
        if (!bytes.AsSpan().SequenceEqual(BlitzSolver.Simulation.Run.RunCodec.Encode(decoded)))
            throw new InvalidDataException("正式 RunCodec 编码往返合同失败");
        Console.WriteLine("CONTRACT official RunCodec canonical roundtrip passed; padding coverage in KernelChecks");
    }

    private static void CheckPair(string label, Battle before, bool same)
    {
        Battle changed = before;
        changed.Enemies[0].Flags ^= EnemyFlags.SkipNextMove;
        if (BattleReconciler.Same(before, changed) != same)
            throw new InvalidDataException($"tombstone合同失败：{label}");
    }
}
