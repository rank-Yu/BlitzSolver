using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Ai.Combat;

/// <summary>Versioned combination request. Legacy SREQ continues to decode as one deterministic solve.</summary>
internal static class CombatSearchCodec
{
    private const uint Magic = 0x32564353; // SCV2
    private const ushort Version = 5;

    internal static byte[] Encode(CombatSearchRequest request) => Codec.ToBytes(writer =>
    {
        SearchPlan plan = request.Plan;
        bool independent = plan.MillisecondsPerWidth > 0;
        if (plan.MillisecondsPerWidth < 0 || plan.CustomMembers.Any(m => m.TimeLimitMilliseconds < 0
            || !independent && m.TimeLimitMilliseconds != 0))
            throw new InvalidDataException("成员时间配置无效。");
        if (plan.ContinuousSetupStrategies && (!independent || !plan.SetupStrategies))
            throw new InvalidDataException("连续增益补搜需要独立计时与能力补搜范围。");
        writer.Write(Magic);
        bool compact = request.Plan.UsesCompactOneStage;
        bool compactAllowed = request.Plan.DefaultPotionSchedule == DefaultPotionSchedule.CompactPotionPortfolio;
        writer.Write(plan.ContinuousSetupStrategies ? (ushort)13 : independent ? (ushort)12 : compactAllowed ? (ushort)11 : compact ? (ushort)10 : request.Plan.SetupStrategies ? (request.Plan.LegacySetupPortfolio ? (ushort)8 : (ushort)9) : request.Plan.QueenStrategies ? (ushort)7 : request.Plan.ExperimentalQueenSupplement ? (ushort)6 : Version);
        byte[] root = SearchCodec.EncodeRequest(request.Case);
        writer.Write(root.Length);
        writer.Write(root);
        writer.Write(plan.BaselineWidth);
        writer.Write(plan.NarrowWidth);
        writer.Write(plan.WideWidth);
        writer.Write(plan.OffensiveWidth);
        writer.Write(plan.TotalMilliseconds);
        writer.Write(plan.MemoryBytes);
        writer.Write(plan.EndedKeepWidths);
        writer.Write(plan.EndedTriggerWidths);
        writer.Write(plan.CustomMembers.Length);
        foreach (SearchMember member in plan.CustomMembers)
        {
            writer.Write(member.Width);
            writer.Write((int)member.Evaluation);
            writer.Write((int)member.Retention);
            writer.Write((int)member.Potions);
            if (independent) writer.Write(member.TimeLimitMilliseconds);
        }
        writer.Write(request.Case.Options.PotionFreeBaseline?.OutstandingStolenResource ?? int.MaxValue);
        writer.Write(request.Case.Options.HasUnboundedGrowthTargets);
        writer.Write(request.Case.Options.PoolStateBudgetBytes);
        if (!Enum.IsDefined(plan.DefaultPotionSchedule))
            throw new InvalidDataException("默认用药调度无效。");
        writer.Write((int)plan.DefaultPotionSchedule);
        if (independent || compact || plan.SetupStrategies || plan.QueenStrategies || plan.ExperimentalQueenSupplement) writer.Write(plan.ExperimentalQueenSupplement);
        if (independent || compact || plan.SetupStrategies || plan.QueenStrategies) writer.Write(plan.QueenStrategies);
        if (independent || compact || plan.SetupStrategies) writer.Write(plan.SetupStrategies);
        if (independent || compact) writer.Write(plan.LegacySetupPortfolio);
        if (independent) writer.Write(plan.MillisecondsPerWidth);
    });

    internal static CombatSearchRequest Decode(byte[] bytes) => Codec.Read(bytes, "组合搜索请求", reader =>
    {
        if (reader.ReadUInt32() != Magic)
            throw new InvalidDataException("组合搜索请求魔数不符。");
        ushort version = reader.ReadUInt16();
        if (version is not (1 or 2 or 3 or 4 or Version or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13))
            throw new InvalidDataException("组合搜索请求版本不受支持。");
        int length = Codec.ReadCount(reader, 1 << 28, "搜索根字节");
        byte[] root = reader.ReadBytes(length);
        if (root.Length != length)
            throw new InvalidDataException("组合搜索根已截断。");
        SearchCase searchCase = SearchCodec.DecodeRequest(root);
        SearchPlan plan = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt64())
        {
            EndedKeepWidths = reader.ReadInt32(),
            EndedTriggerWidths = reader.ReadInt32(),
        };
        int memberCount = Codec.ReadCount(reader, 256, "组合成员");
        var members = System.Collections.Immutable.ImmutableArray.CreateBuilder<SearchMember>(memberCount);
        for (int i = 0; i < memberCount; i++)
            members.Add(new SearchMember(reader.ReadInt32(), (SearchEvaluation)reader.ReadInt32(),
                (SearchRetention)reader.ReadInt32(), (SearchPotionLimit)reader.ReadInt32())
                { TimeLimitMilliseconds = version >= 12 ? reader.ReadInt32() : 0 });
        if (version < 3 && members.Any(m => (int)m.Potions > (int)SearchPotionLimit.Allowed))
            throw new InvalidDataException("旧版本不支持此用药上限。");
        plan = plan with { CustomMembers = members.MoveToImmutable() };
        int outstanding = reader.ReadInt32();
        bool unboundedGrowth = Codec.ReadBool(reader);
        int poolStateBudget = version >= 2 ? reader.ReadInt32() : 0;
        if (poolStateBudget < 0) throw new InvalidDataException("状态池预算不能为负。");
        DefaultPotionSchedule schedule = version >= 4 ? (DefaultPotionSchedule)reader.ReadInt32()
            : version == 3 ? DefaultPotionSchedule.OneTwoThree : DefaultPotionSchedule.OneAllowed;
        if (!Enum.IsDefined(schedule) || version < 5 && schedule == DefaultPotionSchedule.StagedPortfolio
            || version < 10 && schedule == DefaultPotionSchedule.CompactOnePortfolio
            || version == 10 && schedule != DefaultPotionSchedule.CompactOnePortfolio
            || version < 11 && schedule == DefaultPotionSchedule.CompactPotionPortfolio
            || version == 11 && schedule != DefaultPotionSchedule.CompactPotionPortfolio)
            throw new InvalidDataException("默认用药调度无效。");
        plan = plan with { DefaultPotionSchedule = schedule,
            ExperimentalQueenSupplement = version >= 6 && Codec.ReadBool(reader),
            QueenStrategies = version >= 7 && Codec.ReadBool(reader),
            SetupStrategies = version >= 8 && Codec.ReadBool(reader),
            LegacySetupPortfolio = version == 8 || version >= 10 && Codec.ReadBool(reader),
            ContinuousSetupStrategies = version >= 13 };
        if (version >= 12)
        {
            int millisecondsPerWidth = reader.ReadInt32();
            if (millisecondsPerWidth <= 0 || plan.CustomMembers.Any(m => m.TimeLimitMilliseconds < 0))
                throw new InvalidDataException("成员时间配置无效。");
            plan = plan with { MillisecondsPerWidth = millisecondsPerWidth };
        }
        if (plan.ContinuousSetupStrategies && !plan.SetupStrategies)
            throw new InvalidDataException("连续增益补搜范围无效。");
        searchCase = searchCase with { Options = searchCase.Options with
            { HasUnboundedGrowthTargets = unboundedGrowth, PoolStateBudgetBytes = poolStateBudget } };
        if (searchCase.Options.PotionFreeBaseline is { } baseline)
            searchCase = searchCase with { Options = searchCase.Options with
                { PotionFreeBaseline = baseline with { OutstandingStolenResource = outstanding } } };
        // A legacy default request represents the historical One + Allowed schedule. Materialize
        // it so re-encoding cannot silently change its historical custom-member behavior.
        if (version < 3 && plan.CustomMembers.IsDefaultOrEmpty)
        {
            bool forced = PotionSearchSchedule.HasForcedDirectives(searchCase.Options);
            SearchMember[] legacy = PortfolioBuilder.Build(plan,
                searchCase.Options.PowerCommitments is { Rules.Length: > 0 }, forced);
            plan = plan with { CustomMembers = System.Collections.Immutable.ImmutableArray.CreateRange(legacy) };
        }
        return new CombatSearchRequest(searchCase, plan);
    });
}
