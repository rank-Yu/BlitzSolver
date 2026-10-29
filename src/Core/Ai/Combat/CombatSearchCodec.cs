using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Ai.Combat;

/// <summary>Versioned combination request. Legacy SREQ continues to decode as one deterministic solve.</summary>
internal static class CombatSearchCodec
{
    private const uint Magic = 0x32564353; // SCV2
    private const ushort Version = 1;

    internal static byte[] Encode(CombatSearchRequest request) => Codec.ToBytes(writer =>
    {
        writer.Write(Magic);
        writer.Write(Version);
        byte[] root = SearchCodec.EncodeRequest(request.Case);
        writer.Write(root.Length);
        writer.Write(root);
        SearchPlan plan = request.Plan;
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
        }
        writer.Write(request.Case.Options.PotionFreeBaseline?.OutstandingStolenResource ?? int.MaxValue);
        writer.Write(request.Case.Options.HasUnboundedGrowthTargets);
    });

    internal static CombatSearchRequest Decode(byte[] bytes) => Codec.Read(bytes, "组合搜索请求", reader =>
    {
        if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Version)
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
                (SearchRetention)reader.ReadInt32(), (SearchPotionLimit)reader.ReadInt32()));
        plan = plan with { CustomMembers = members.MoveToImmutable() };
        int outstanding = reader.ReadInt32();
        bool unboundedGrowth = Codec.ReadBool(reader);
        searchCase = searchCase with { Options = searchCase.Options with
            { HasUnboundedGrowthTargets = unboundedGrowth } };
        if (searchCase.Options.PotionFreeBaseline is { } baseline)
            searchCase = searchCase with { Options = searchCase.Options with
                { PotionFreeBaseline = baseline with { OutstandingStolenResource = outstanding } } };
        return new CombatSearchRequest(searchCase, plan);
    });
}
