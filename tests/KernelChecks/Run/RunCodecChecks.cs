using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sts2Solver.Simulation.Run;

namespace Sts2Solver.KernelChecks;

internal static class RunCodecChecks
{
    internal static void Check(RunState state)
    {
        int[] padding = AuditLayout();
        // Keep each family containing alignment live, including nested references and card shelves.
        state.Shop.Stage = RunShopStage.Shop;
        state.Shop.CharacterCards[0].Flags = RunShopEntryFlags.Stocked;
        state.Shop.CharacterCards[0].Card.Id = 0;
        state.Rooms.Count = 1;
        state.Rooms.Items[0].RoomType = RunRoomType.Event;
        state.Rooms.Items[0].Model = new RunRef(RunRefKind.Event, 0);
        state.Rooms.Items[0].ParentEvent = new RunRef(RunRefKind.Event, 1);
        state.Rooms.Items[0].GoldProportion = 1;
        state.History.Count = 1;
        state.History.Entries[0].RoomCount = 1;
        state.History.Entries[0].Rooms[0].RoomType = RunRoomType.Event;
        state.History.Entries[0].Rooms[0].Model = new RunRef(RunRefKind.Event, 0);
        state.History.Entries[0].Rooms[0].MonsterCount = 1;
        state.Rng.EventCount = 1;
        byte[] expected = RunCodec.Encode(state);
        ulong expectedHash = RunCodec.Hash(state);
        string fields = RunDump.Capture(state);
        Span<byte> raw = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref state, 1));
        foreach (int offset in padding) raw[offset] = 0xA5;
        byte[] before = raw.ToArray();
        Require(expected.AsSpan().SequenceEqual(RunCodec.Encode(state)), "全部隐式 padding 扰动后 Encode 改变。");
        Require(expectedHash == RunCodec.Hash(state), "全部隐式 padding 扰动后 Hash 改变。");
        Require(before.AsSpan().SequenceEqual(raw), "Encode/Hash 修改输入原始字节。");
        foreach (int offset in padding)
            Require(expected[RunCodec.HeaderSize + offset] == 0, $"编码 padding {offset} 未清零。");

        byte[] legacy = (byte[])expected.Clone();
        foreach (int offset in padding) legacy[RunCodec.HeaderSize + offset] = 0x5A;
        byte[] legacyBefore = (byte[])legacy.Clone();
        RunState decoded = RunCodec.Decode(legacy);
        Require(fields == RunDump.Capture(decoded), "旧 v3 脏 padding 解码丢失字段。");
        Require(expected.AsSpan().SequenceEqual(RunCodec.Encode(decoded)), "旧 v3 重编码未规范化。");
        Require(legacyBefore.AsSpan().SequenceEqual(legacy), "Decode 修改旧 v3 输入。");

        Sensitive(s => { s.History.Entries[0].Rooms[0].Model = new RunRef(RunRefKind.Encounter, 0); return s; }, "RunRef.Kind");
        Sensitive(s => { s.History.Entries[0].Rooms[0].Model = new RunRef(RunRefKind.Event, 1); return s; }, "RunRef.Id");
        Sensitive(s => { s.History.Entries[0].Rooms[0].RoomType = RunRoomType.Monster; return s; }, "History.RoomType");
        Sensitive(s => { s.History.Entries[0].Rooms[0].MonsterCount = 2; return s; }, "History.MonsterCount");
        Sensitive(s => { s.History.Entries[0].Rooms[0].TurnsTaken++; return s; }, "History.TurnsTaken");
        Sensitive(s => { s.Rooms.Items[0].ParentEvent = new RunRef(RunRefKind.Event, 2); return s; }, "Room.ParentEvent.Id");
        Sensitive(s => { s.Rooms.Items[0].IsPreFinished = 1; return s; }, "Room.IsPreFinished");
        Sensitive(s => { s.Rooms.Items[0].GoldProportion = 0.5f; return s; }, "Room.GoldProportion");
        Sensitive(s => { s.Deck.Cards[0].Flags = RunCardFlags.Removable; return s; }, "RunCard.Flags");
        Sensitive(s => { s.Deck.Cards[0].HasEnchantment = 1; s.Deck.Cards[0].EnchantmentAmount = 2; return s; }, "RunCard.EnchantmentAmount");
        Sensitive(s => { s.Deck.Cards[0].FloorAddedToDeck++; return s; }, "RunCard.FloorAddedToDeck");
        Sensitive(s => { s.Deck.Cards[0].Properties.Bytes[63] = 1; return s; }, "RunCard.Properties last byte");
        Sensitive(s => { s.Player.Character = 1; return s; }, "Player.Character");
        Sensitive(s => { s.Player.CurrentHp++; return s; }, "Player.CurrentHp");
        Sensitive(s => { s.Relics.Items[0].StackCount++; return s; }, "Relic.StackCount");
        Sensitive(s => { s.Relics.Items[0].DisplayAmount++; return s; }, "Relic.DisplayAmount");
        Sensitive(s => { s.Shop.CharacterCards[0].Flags |= RunShopEntryFlags.OnSale; return s; }, "ShopCard.Flags");
        // Explicit reserved fields are declared fields, never inferred as implicit padding by name.
        Sensitive(s => { s._padding1 = 0x1234; return s; }, "declared reserved field");

        byte[] invalid = (byte[])legacy.Clone();
        int decision = (int)Marshal.OffsetOf<RunState>(nameof(RunState.Decision));
        invalid[RunCodec.HeaderSize + decision] = 255;
        try { _ = RunCodec.Decode(invalid); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("脏 padding 旧输入的非法枚举未被拒绝。");

        void Sensitive(Func<RunState, RunState> change, string field)
        {
            RunState changed = change(state);
            Require(!expected.AsSpan().SequenceEqual(RunCodec.Encode(changed))
                && expectedHash != RunCodec.Hash(changed), $"正式编码忽略有效字段 {field}。");
        }
    }

    private static int[] AuditLayout()
    {
        // Independent layout inventory: assert local holes, then mark every scalar field byte.
        // A future field occupying any former hole changes this inventory and requires review.
        Dictionary<string, string> expectedHoles = new()
        {
            ["RunUnlockState"] = "36,37,38,39", ["RunPlayer"] = "2,3,58,59",
            ["RunCard"] = "5,10,11,146,147", ["RunShopCard"] = "157,158,159",
            ["RunShopModel"] = "13,14,15", ["RunDeck"] = "2,3",
            ["RunRelic"] = "6,7,81,82,83", ["RunRelics"] = "1,2,3",
            ["RunAct"] = "3,9,18,19", ["RunActs"] = "2,3", ["RunRef"] = "1",
            ["RunHistoryRoom"] = "1,7", ["RunHistoryEntry"] = "3",
            ["RunHistory"] = "2,3", ["RunRoom"] = "1,14,15", ["RunRooms"] = "1,2,3",
            ["RunVisited"] = "129,130,131,132,133,134,135", ["RunEventRng"] = "4,5,6,7",
            ["RunRngState"] = "781,782,783", ["RunState"] = "18,19,44,45,46,47,37558,37559",
        };
        HashSet<Type> audited = [];
        bool[] fields = new bool[Unsafe.SizeOf<RunState>()];
        Mark(typeof(RunState), 0);
        int[] padding = Enumerable.Range(0, fields.Length).Where(i => !fields[i]).ToArray();
        Require(padding.Length == 2452 && expectedHoles.Count == 0, "RunState 隐式 padding 布局盘点改变。");
        return padding;

        void Mark(Type type, int start)
        {
            int size = Size(type);
            if (type.IsPrimitive || type.IsEnum)
            {
                for (int i = start; i < start + size; i++)
                {
                    Require(!fields[i], "布局字段重叠。");
                    fields[i] = true;
                }
                return;
            }
            FieldInfo[] members = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (type.GetCustomAttribute<InlineArrayAttribute>() is { } array)
            {
                Type element = members.Single().FieldType;
                Require(size == Size(element) * array.Length, "InlineArray 真实步长改变。");
                for (int i = 0; i < array.Length; i++) Mark(element, start + i * Size(element));
                return;
            }
            if (audited.Add(type))
            {
                bool[] covered = new bool[size];
                foreach (FieldInfo field in members)
                {
                    int offset = (int)Marshal.OffsetOf(type, field.Name);
                    Array.Fill(covered, true, offset, Size(field.FieldType));
                }
                string holes = string.Join(",", Enumerable.Range(0, size).Where(i => !covered[i]));
                expectedHoles.Remove(type.Name, out string? expected);
                Require(holes == (expected ?? ""), $"{type.Name} 局部 padding 改变：{holes}。");
            }
            foreach (FieldInfo field in members)
                Mark(field.FieldType, start + (int)Marshal.OffsetOf(type, field.Name));
        }
    }

    private static int Size(Type type) => (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!
        .MakeGenericMethod(type).Invoke(null, null)!;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
