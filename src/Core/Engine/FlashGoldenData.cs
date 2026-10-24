using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CombatSolver.Flash.Kernel;

/// <summary>
/// D1 kernel golden file: the Flash root(s), the input mapping that produced them, every checked K1
/// completion and every recorded K2 walk. The file is a plain deterministic binary payload; the
/// harness compresses it, and <c>FlashGolden</c> replays it against the Kernel only.
/// </summary>
public sealed class FlashGoldenRootRecord
{
    public required string InputJson { get; init; }
    public required string RulesSha256 { get; init; }
    public required List<FlashGoldenBattleRoot> Roots { get; init; }
    public required List<FlashGoldenK1Action> K1 { get; init; }
    public required List<FlashGoldenWalk> Walks { get; init; }
}

public sealed class FlashGoldenBattleRoot
{
    public FlashBattle Battle { get; init; }
    public string Choices { get; init; } = "";
    public ulong ScoreHash { get; init; }
    public byte BoundStatus { get; init; }
    public ulong BoundHash { get; init; }
}

public sealed class FlashGoldenK1Action
{
    public int RootIndex { get; init; }

    /// <summary>The action without carried choices; selections are appended one Step at a time.</summary>
    public FlashAction BaseAction { get; init; }

    public ulong[] Selections { get; init; } = [];
    public FlashChoiceRequest[] Requests { get; init; } = [];
    public ulong ChildHash { get; init; }
    public bool Ended { get; init; }
    public ulong ScoreHash { get; init; }
    public byte BoundStatus { get; init; }
    public ulong BoundHash { get; init; }
    public bool TraitPresent { get; init; }
    public ulong TraitHash { get; init; }
}

public sealed class FlashGoldenWalk
{
    public int RootIndex { get; init; }
    public int Seed { get; init; }
    public required List<FlashGoldenState> States { get; init; }
}

public sealed class FlashGoldenState
{
    public ulong StateHash { get; init; }
    public FlashAction[] LegalActions { get; init; } = [];

    /// <summary>Per legal action: 0 = completes, 1 = needs the parallel request, 2 = unsupported.</summary>
    public byte[] ImmediateStatuses { get; init; } = [];
    public FlashChoiceRequest[] ImmediateRequests { get; init; } = [];

    public bool HasStep { get; init; }
    public FlashAction StepBase { get; init; }
    public ulong[] StepSelections { get; init; } = [];
    public FlashChoiceRequest[] StepRequests { get; init; } = [];
    public ulong ChildHash { get; init; }
    public bool Ended { get; init; }
    public ulong ScoreHash { get; init; }
    public byte BoundStatus { get; init; }
    public ulong BoundHash { get; init; }
    public bool TraitPresent { get; init; }
    public ulong TraitHash { get; init; }
}

/// <summary>Canonical action, choice-request and D1 file encoding. No padding bytes are written.</summary>
public static class FlashGoldenData
{
    private const uint Magic = 0x31444746; // "FGD1"

    public static byte[] Encode(FlashGoldenRootRecord record)
        => FlashCodec.ToBytes(writer =>
        {
            writer.Write(Magic);
            writer.Write(FlashCodec.FormatVersion);
            FlashCodec.WriteString(writer, record.InputJson);
            FlashCodec.WriteString(writer, record.RulesSha256);
            writer.Write(record.Roots.Count);
            foreach (FlashGoldenBattleRoot root in record.Roots)
            {
                byte[] bytes = FlashBattleCodec.Encode(root.Battle);
                writer.Write(bytes.Length);
                writer.Write(bytes);
                FlashCodec.WriteString(writer, root.Choices);
                writer.Write(root.ScoreHash);
                writer.Write(root.BoundStatus);
                writer.Write(root.BoundHash);
            }
            writer.Write(record.K1.Count);
            foreach (FlashGoldenK1Action action in record.K1)
            {
                writer.Write(action.RootIndex);
                WriteAction(writer, action.BaseAction);
                WriteSelections(writer, action.Selections);
                WriteRequests(writer, action.Requests);
                writer.Write(action.ChildHash);
                writer.Write(action.Ended);
                writer.Write(action.ScoreHash);
                writer.Write(action.BoundStatus);
                writer.Write(action.BoundHash);
                writer.Write(action.TraitPresent);
                writer.Write(action.TraitHash);
            }
            writer.Write(record.Walks.Count);
            foreach (FlashGoldenWalk walk in record.Walks)
            {
                writer.Write(walk.RootIndex);
                writer.Write(walk.Seed);
                writer.Write(walk.States.Count);
                foreach (FlashGoldenState state in walk.States)
                {
                    writer.Write(state.StateHash);
                    writer.Write(state.LegalActions.Length);
                    foreach (FlashAction legal in state.LegalActions)
                        WriteAction(writer, legal);
                    writer.Write(state.ImmediateStatuses.Length);
                    foreach (byte status in state.ImmediateStatuses)
                        writer.Write(status);
                    WriteRequests(writer, state.ImmediateRequests);
                    writer.Write(state.HasStep);
                    if (!state.HasStep)
                        continue;
                    WriteAction(writer, state.StepBase);
                    WriteSelections(writer, state.StepSelections);
                    WriteRequests(writer, state.StepRequests);
                    writer.Write(state.ChildHash);
                    writer.Write(state.Ended);
                    writer.Write(state.ScoreHash);
                    writer.Write(state.BoundStatus);
                    writer.Write(state.BoundHash);
                    writer.Write(state.TraitPresent);
                    writer.Write(state.TraitHash);
                }
            }
        });

    public static FlashGoldenRootRecord Decode(byte[] bytes)
        => FlashCodec.Read(bytes, "D1 golden", reader =>
        {
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("D1 golden 魔数不符。");
            ushort version = reader.ReadUInt16();
            if (version != FlashCodec.FormatVersion)
                throw new InvalidDataException($"D1 golden 版本 {version} 不受支持。");
            string input = FlashCodec.ReadString(reader)
                ?? throw new InvalidDataException("D1 golden 缺少输入映射。");
            string rules = FlashCodec.ReadString(reader)
                ?? throw new InvalidDataException("D1 golden 缺少规则哈希。");
            int rootCount = FlashCodec.ReadCount(reader, 4096, "D1 根");
            List<FlashGoldenBattleRoot> roots = new(rootCount);
            for (int index = 0; index < rootCount; index++)
            {
                int length = FlashCodec.ReadCount(reader, FlashBattleCodec.HeaderSize + FlashBattle.ExpectedSize,
                    "D1 根状态字节");
                byte[] image = reader.ReadBytes(length);
                if (image.Length != length)
                    throw new InvalidDataException("D1 根状态被截断。");
                roots.Add(new FlashGoldenBattleRoot
                {
                    Battle = FlashBattleCodec.Decode(image),
                    Choices = FlashCodec.ReadString(reader)
                        ?? throw new InvalidDataException("D1 根选择向量缺失。"),
                    ScoreHash = reader.ReadUInt64(),
                    BoundStatus = reader.ReadByte(),
                    BoundHash = reader.ReadUInt64(),
                });
            }
            int k1Count = FlashCodec.ReadCount(reader, 1 << 24, "D1 K1");
            List<FlashGoldenK1Action> k1 = new(k1Count);
            for (int index = 0; index < k1Count; index++)
            {
                k1.Add(new FlashGoldenK1Action
                {
                    RootIndex = reader.ReadInt32(),
                    BaseAction = ReadAction(reader),
                    Selections = ReadSelections(reader),
                    Requests = ReadRequests(reader),
                    ChildHash = reader.ReadUInt64(),
                    Ended = FlashCodec.ReadBool(reader),
                    ScoreHash = reader.ReadUInt64(),
                    BoundStatus = reader.ReadByte(),
                    BoundHash = reader.ReadUInt64(),
                    TraitPresent = FlashCodec.ReadBool(reader),
                    TraitHash = reader.ReadUInt64(),
                });
            }
            int walkCount = FlashCodec.ReadCount(reader, 1 << 16, "D1 游走");
            List<FlashGoldenWalk> walks = new(walkCount);
            for (int index = 0; index < walkCount; index++)
            {
                int rootIndex = reader.ReadInt32();
                int seed = reader.ReadInt32();
                int stateCount = FlashCodec.ReadCount(reader, 1 << 20, "D1 游走状态");
                List<FlashGoldenState> states = new(stateCount);
                for (int stateIndex = 0; stateIndex < stateCount; stateIndex++)
                {
                    ulong stateHash = reader.ReadUInt64();
                    int legalCount = FlashCodec.ReadCount(reader, FlashKernel.MaxLegalActions, "D1 合法动作");
                    FlashAction[] legal = new FlashAction[legalCount];
                    for (int legalIndex = 0; legalIndex < legalCount; legalIndex++)
                        legal[legalIndex] = ReadAction(reader);
                    int statusCount = FlashCodec.ReadCount(reader, FlashKernel.MaxLegalActions, "D1 动作状态");
                    byte[] statuses = new byte[statusCount];
                    for (int statusIndex = 0; statusIndex < statusCount; statusIndex++)
                        statuses[statusIndex] = reader.ReadByte();
                    FlashChoiceRequest[] immediate = ReadRequests(reader);
                    bool hasStep = FlashCodec.ReadBool(reader);
                    if (!hasStep)
                    {
                        states.Add(new FlashGoldenState
                        {
                            StateHash = stateHash,
                            LegalActions = legal,
                            ImmediateStatuses = statuses,
                            ImmediateRequests = immediate,
                        });
                        continue;
                    }
                    states.Add(new FlashGoldenState
                    {
                        StateHash = stateHash,
                        LegalActions = legal,
                        ImmediateStatuses = statuses,
                        ImmediateRequests = immediate,
                        HasStep = true,
                        StepBase = ReadAction(reader),
                        StepSelections = ReadSelections(reader),
                        StepRequests = ReadRequests(reader),
                        ChildHash = reader.ReadUInt64(),
                        Ended = FlashCodec.ReadBool(reader),
                        ScoreHash = reader.ReadUInt64(),
                        BoundStatus = reader.ReadByte(),
                        BoundHash = reader.ReadUInt64(),
                        TraitPresent = FlashCodec.ReadBool(reader),
                        TraitHash = reader.ReadUInt64(),
                    });
                }
                walks.Add(new FlashGoldenWalk { RootIndex = rootIndex, Seed = seed, States = states });
            }
            return new FlashGoldenRootRecord
            {
                InputJson = input,
                RulesSha256 = rules,
                Roots = roots,
                K1 = k1,
                Walks = walks,
            };
        });

    public static void WriteAction(BinaryWriter writer, in FlashAction action)
    {
        writer.Write((byte)action.Kind);
        writer.Write(action.HandIndex);
        writer.Write(action.Target);
        writer.Write(action.PotionSlot);
        writer.Write(action.ChoiceCount);
        for (int index = 0; index < action.ChoiceCount; index++)
            writer.Write(action.Choices[index]);
    }

    public static FlashAction ReadAction(BinaryReader reader)
    {
        FlashAction action = new()
        {
            Kind = (FlashActionKind)reader.ReadByte(),
            HandIndex = reader.ReadByte(),
            Target = reader.ReadByte(),
            PotionSlot = reader.ReadByte(),
        };
        int count = reader.ReadByte();
        if (count > FlashLimits.MaxChoices)
            throw new InvalidDataException($"D1 动作选择数 {count} 超过上限。");
        action.ChoiceCount = (byte)count;
        if (!Enum.IsDefined(action.Kind))
            throw new InvalidDataException($"D1 动作种类 {(byte)action.Kind} 未定义。");
        for (int index = 0; index < count; index++)
            action.Choices[index] = reader.ReadUInt64();
        return action;
    }

    public static void WriteRequests(BinaryWriter writer, FlashChoiceRequest[] requests)
    {
        writer.Write(requests.Length);
        foreach (FlashChoiceRequest request in requests)
        {
            writer.Write((byte)request.Kind);
            writer.Write((byte)request.Effect);
            writer.Write((byte)request.Timing);
            writer.Write((byte)request.Source);
            writer.Write(request.SourceIsNone);
            writer.Write(request.OptionsAreGenerated);
            writer.Write(request.OptionCount);
            writer.Write(request.MinCount);
            writer.Write(request.MaxCount);
            for (int index = 0; index < request.OptionCount; index++)
                writer.Write(request.Options[index]);
            if (request.OptionsAreGenerated)
            {
                for (int index = 0; index < request.OptionCount; index++)
                {
                    FlashCard card = request.GeneratedOptions[index];
                    writer.Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in card, 1)));
                }
            }
        }
    }

    public static FlashChoiceRequest[] ReadRequests(BinaryReader reader)
    {
        int count = FlashCodec.ReadCount(reader, 1 << 20, "D1 选择请求");
        FlashChoiceRequest[] requests = new FlashChoiceRequest[count];
        for (int index = 0; index < count; index++)
        {
            FlashChoiceRequest request = new()
            {
                Kind = (FlashChoiceKind)reader.ReadByte(),
                Effect = (FlashChoiceEffect)reader.ReadByte(),
                Timing = (FlashChoiceTiming)reader.ReadByte(),
                Source = (FlashPile)reader.ReadByte(),
                SourceIsNone = FlashCodec.ReadBool(reader),
                OptionsAreGenerated = FlashCodec.ReadBool(reader),
            };
            byte optionCount = reader.ReadByte();
            if (optionCount > FlashLimits.MaxChoiceOptions
                || (request.OptionsAreGenerated && optionCount > FlashLimits.MaxGeneratedChoiceOptions))
                throw new InvalidDataException($"D1 选择请求选项数 {optionCount} 超过上限。");
            request.OptionCount = optionCount;
            request.MinCount = reader.ReadByte();
            request.MaxCount = reader.ReadByte();
            if (!Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.Effect)
                || !Enum.IsDefined(request.Timing) || !Enum.IsDefined(request.Source))
                throw new InvalidDataException("D1 选择请求包含未定义枚举值。");
            if (request.MinCount > request.MaxCount || request.MaxCount > optionCount)
                throw new InvalidDataException(
                    $"D1 选择请求边界非法：{request.MinCount}..{request.MaxCount} / {optionCount}。");
            for (int option = 0; option < optionCount; option++)
                request.Options[option] = reader.ReadByte();
            if (request.OptionsAreGenerated)
            {
                for (int option = 0; option < optionCount; option++)
                {
                    byte[] image = reader.ReadBytes(Unsafe.SizeOf<FlashCard>());
                    if (image.Length != Unsafe.SizeOf<FlashCard>())
                        throw new InvalidDataException("D1 生成选项卡牌被截断。");
                    FlashCard card = default;
                    image.CopyTo(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref card, 1)));
                    request.GeneratedOptions[option] = card;
                }
            }
            requests[index] = request;
        }
        return requests;
    }

    private static void WriteSelections(BinaryWriter writer, ulong[] selections)
    {
        writer.Write(selections.Length);
        foreach (ulong selection in selections)
            writer.Write(selection);
    }

    private static ulong[] ReadSelections(BinaryReader reader)
    {
        int count = FlashCodec.ReadCount(reader, FlashLimits.MaxChoices, "D1 选择");
        ulong[] selections = new ulong[count];
        for (int index = 0; index < count; index++)
            selections[index] = reader.ReadUInt64();
        return selections;
    }

    public static FlashAction WithSelections(in FlashAction baseAction, ulong[] selections)
    {
        FlashAction action = baseAction;
        for (int index = 0; index < selections.Length; index++)
            action = action.WithChoice(selections[index]);
        return action;
    }
}
