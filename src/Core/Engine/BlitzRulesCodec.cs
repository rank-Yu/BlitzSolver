using System.Text;

namespace BlitzSolver.Engine;

/// <summary>
/// Complete deterministic encoding of <see cref="BlitzRules"/>. Every table and scalar that the
/// constructor receives is written; dictionaries are ordered ordinally and decimals keep their
/// exact four-word representation, so equal rules always encode to equal bytes and
/// <c>Encode(Decode(bytes))</c> is byte-identical.
/// </summary>
public static class BlitzRulesCodec
{
    private const uint Magic = 0x4C555246; // "FRUL"

    /// <summary>Matches <see cref="BlitzRules.SchemaFieldCount"/>; the K0 contract checks both.</summary>
    internal const int EncodedFieldCount = 30;

    public static byte[] Encode(BlitzRules rules)
    {
        if (EncodedFieldCount != BlitzRules.SchemaFieldCount)
            throw new InvalidOperationException("BlitzRules 编解码字段数与 K0 合同不一致。");
        return BlitzCodec.ToBytes(writer =>
        {
            writer.Write(Magic);
            writer.Write(BlitzCodec.FormatVersion);
            writer.Write(EncodedFieldCount);

            // 1: cards[id][upgrade]
            writer.Write(rules.CardTable.Length);
            for (int id = 0; id < rules.CardTable.Length; id++)
            {
                BlitzCardStats?[] levels = rules.CardTable[id];
                writer.Write(levels.Length);
                for (int upgrade = 0; upgrade < levels.Length; upgrade++)
                {
                    writer.Write(levels[upgrade] != null);
                    if (levels[upgrade] is { } stats)
                        WriteCardStats(writer, stats);
                }
            }

            // 2: power variables
            writer.Write(rules.PowerVarTable.Length);
            for (int id = 0; id < rules.PowerVarTable.Length; id++)
                BlitzCodec.WriteDecimals(writer, rules.PowerVarTable[id]);

            // 3: potions
            writer.Write(rules.PotionTable.Length);
            for (int id = 0; id < rules.PotionTable.Length; id++)
            {
                writer.Write(rules.PotionTable[id] != null);
                if (rules.PotionTable[id] is { } potion)
                    WritePotion(writer, potion);
            }

            // 4: relic variables
            writer.Write(rules.RelicVarTable.Length);
            for (int id = 0; id < rules.RelicVarTable.Length; id++)
                BlitzCodec.WriteDecimals(writer, rules.RelicVarTable[id]);

            // 5: monster rules
            writer.Write(rules.MonsterTable.Length);
            for (int index = 0; index < rules.MonsterTable.Length; index++)
            {
                writer.Write(rules.MonsterTable[index] != null);
                if (rules.MonsterTable[index] is { } monster)
                    WriteMonster(writer, monster);
            }

            // 6: spawn rules, ordered by monster id
            KeyValuePair<ushort, int>[] spawns = [.. rules.SpawnRuleTable];
            Array.Sort(spawns, static (left, right) => left.Key.CompareTo(right.Key));
            writer.Write(spawns.Length);
            foreach (KeyValuePair<ushort, int> entry in spawns)
            {
                writer.Write(entry.Key);
                writer.Write(entry.Value);
            }

            // 7: card pools, ordered by key
            KeyValuePair<string, BlitzCardPool>[] pools = [.. rules.CardPoolTable];
            Array.Sort(pools, static (left, right) => string.CompareOrdinal(left.Key, right.Key));
            writer.Write(pools.Length);
            foreach (KeyValuePair<string, BlitzCardPool> entry in pools)
            {
                BlitzCodec.WriteString(writer, entry.Key);
                BlitzCodec.WriteUShortArray(writer, entry.Value.Cards);
            }

            // 8-11: pools and root potions
            BlitzCodec.WriteUShortArray(writer, rules.PotionPoolTable);
            BlitzCodec.WriteUShortArray(writer, rules.OutOfCombatPotionPoolTable);
            BlitzCodec.WriteUShortArray(writer, rules.RootPotionTable);
            writer.Write(rules.PowerDeathHistoryRiskTable.Length);
            for (int id = 0; id < rules.PowerDeathHistoryRiskTable.Length; id++)
                writer.Write(rules.PowerDeathHistoryRiskTable[id]);

            // 12-30: scalars
            writer.Write(rules.HasEncounter);
            BlitzCodec.WriteString(writer, string.Join("\u001f", rules.EncounterSlotTable));
            writer.Write(rules.RootEnemyCount);
            writer.Write(rules.BaseMaxEnergy);
            writer.Write(rules.PlayerSlotIndex);
            writer.Write(rules.BaseOrbSlotCount);
            writer.Write(rules.RootSwordSageAmount);
            writer.Write(rules.UnsettlingLampRelicId);
            writer.Write(rules.HasIdentityContent);
            writer.Write(rules.HasShuffleOrderEnchantment);
            writer.Write(rules.HasBeforeFlushEnchantment);
            writer.Write(rules.HasTheftContent);
            writer.Write(rules.HasDecimalCardVars);
            writer.Write(rules.HasInfiniteHpDisplayMonster);
            writer.Write(rules.HasPaelsLegion);
            writer.Write(rules.FurCoatSpawnHpOne);
            writer.Write(rules.RootLostHpMask);
            writer.Write(rules.HasHistoryCourse);
            writer.Write(rules.PenNibRelicIndex);
        });
    }

    public static BlitzRules Decode(ReadOnlySpan<byte> bytes)
        => BlitzCodec.Read(bytes.ToArray(), "BlitzRules", reader =>
        {
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("BlitzRules 魔数不符。");
            ushort version = reader.ReadUInt16();
            if (version != BlitzCodec.FormatVersion)
                throw new InvalidDataException($"BlitzRules 格式版本 {version} 不受支持（当前 {BlitzCodec.FormatVersion}）。");
            int fields = reader.ReadInt32();
            if (fields != EncodedFieldCount || fields != BlitzRules.SchemaFieldCount)
                throw new InvalidDataException(
                    $"BlitzRules 字段数 {fields} 与编解码合同 {EncodedFieldCount}/{BlitzRules.SchemaFieldCount} 不符。");

            int cardCount = BlitzCodec.ReadCount(reader, 1 << 16, "卡牌表");
            BlitzCardStats?[][] cards = new BlitzCardStats?[cardCount][];
            for (int id = 0; id < cardCount; id++)
            {
                int levels = BlitzCodec.ReadCount(reader, 64, "卡牌升级表");
                cards[id] = new BlitzCardStats?[levels];
                for (int upgrade = 0; upgrade < levels; upgrade++)
                {
                    if (BlitzCodec.ReadBool(reader))
                        cards[id][upgrade] = ReadCardStats(reader);
                }
            }

            int powerCount = BlitzCodec.ReadCount(reader, 1 << 16, "Power 变量表");
            decimal[][] powerVars = new decimal[powerCount][];
            for (int id = 0; id < powerCount; id++)
                powerVars[id] = BlitzCodec.ReadDecimals(reader);

            int potionCount = BlitzCodec.ReadCount(reader, 1 << 16, "药水表");
            BlitzPotionStats?[] potions = new BlitzPotionStats?[potionCount];
            for (int id = 0; id < potionCount; id++)
            {
                if (BlitzCodec.ReadBool(reader))
                    potions[id] = ReadPotion(reader);
            }

            int relicCount = BlitzCodec.ReadCount(reader, 1 << 16, "遗物变量表");
            decimal[][] relicVars = new decimal[relicCount][];
            for (int id = 0; id < relicCount; id++)
                relicVars[id] = BlitzCodec.ReadDecimals(reader);

            int monsterCount = BlitzCodec.ReadCount(reader, BlitzLimits.MonsterRules, "怪物表");
            BlitzMonsterRules?[] monsters = new BlitzMonsterRules?[monsterCount];
            for (int index = 0; index < monsterCount; index++)
            {
                if (BlitzCodec.ReadBool(reader))
                    monsters[index] = ReadMonster(reader);
            }

            int spawnCount = BlitzCodec.ReadCount(reader, 1 << 16, "生成规则表");
            Dictionary<ushort, int> spawns = [];
            for (int index = 0; index < spawnCount; index++)
            {
                ushort monsterId = reader.ReadUInt16();
                int rulesId = reader.ReadInt32();
                if (!spawns.TryAdd(monsterId, rulesId))
                    throw new InvalidDataException($"生成规则怪物 {monsterId} 重复。");
            }

            int poolCount = BlitzCodec.ReadCount(reader, 1 << 16, "卡池表");
            Dictionary<string, BlitzCardPool> cardPools = new(StringComparer.Ordinal);
            for (int index = 0; index < poolCount; index++)
            {
                string key = BlitzCodec.ReadString(reader)
                    ?? throw new InvalidDataException("卡池 key 不能为 null。");
                ushort[] poolCards = BlitzCodec.ReadUShortArray(reader, 1 << 16, "卡池卡牌");
                if (!cardPools.TryAdd(key, new BlitzCardPool { Cards = poolCards }))
                    throw new InvalidDataException($"卡池 {key} 重复。");
            }

            ushort[] potionPool = BlitzCodec.ReadUShortArray(reader, 1 << 16, "药水池");
            ushort[] outOfCombat = BlitzCodec.ReadUShortArray(reader, 1 << 16, "战斗外药水池");
            ushort[] rootPotions = BlitzCodec.ReadUShortArray(reader, BlitzLimits.PotionSlots, "根药水");

            int riskCount = BlitzCodec.ReadCount(reader, 1 << 16, "Power 死亡风险表");
            bool[] risk = new bool[riskCount];
            for (int id = 0; id < riskCount; id++)
                risk[id] = BlitzCodec.ReadBool(reader);

            bool hasEncounter = BlitzCodec.ReadBool(reader);
            string slotsText = BlitzCodec.ReadString(reader)
                ?? throw new InvalidDataException("遭遇槽位不能为 null。");
            string[] encounterSlots = slotsText.Length == 0
                ? []
                : slotsText.Split('\u001f', StringSplitOptions.None);
            int rootEnemyCount = reader.ReadInt32();
            int baseMaxEnergy = reader.ReadInt32();
            int playerSlotIndex = reader.ReadInt32();
            int baseOrbSlotCount = reader.ReadInt32();
            int rootSwordSageAmount = reader.ReadInt32();
            ushort unsettlingLampRelicId = reader.ReadUInt16();
            bool hasIdentityContent = BlitzCodec.ReadBool(reader);
            bool hasShuffleOrderEnchantment = BlitzCodec.ReadBool(reader);
            bool hasBeforeFlushEnchantment = BlitzCodec.ReadBool(reader);
            bool hasTheftContent = BlitzCodec.ReadBool(reader);
            bool hasDecimalCardVars = BlitzCodec.ReadBool(reader);
            bool hasInfiniteHpDisplayMonster = BlitzCodec.ReadBool(reader);
            bool hasPaelsLegion = BlitzCodec.ReadBool(reader);
            bool furCoatSpawnHpOne = BlitzCodec.ReadBool(reader);
            ushort rootLostHpMask = reader.ReadUInt16();
            bool hasHistoryCourse = BlitzCodec.ReadBool(reader);
            sbyte penNibRelicIndex = reader.ReadSByte();

            return new BlitzRules(cards, powerVars, potions, relicVars, monsters, spawns, cardPools,
                potionPool, outOfCombat, rootPotions, risk, hasEncounter, encounterSlots,
                rootEnemyCount, baseMaxEnergy, playerSlotIndex, baseOrbSlotCount, rootSwordSageAmount,
                unsettlingLampRelicId, hasIdentityContent, hasShuffleOrderEnchantment,
                hasBeforeFlushEnchantment, hasTheftContent, hasDecimalCardVars,
                hasInfiniteHpDisplayMonster, hasPaelsLegion, furCoatSpawnHpOne, rootLostHpMask,
                hasHistoryCourse, penNibRelicIndex);
        });

    private static void WriteCardStats(BinaryWriter writer, BlitzCardStats stats)
    {
        writer.Write((byte)stats.Type);
        writer.Write((byte)stats.Target);
        writer.Write((byte)stats.Rarity);
        writer.Write((byte)stats.Pool);
        writer.Write(stats.EnergyCost);
        writer.Write(stats.CostsX);
        writer.Write(stats.StarCost);
        writer.Write(stats.StarCostsX);
        writer.Write((byte)stats.Keywords);
        writer.Write(stats.HasDynamicType);
        writer.Write(stats.MaxUpgradeLevel);
        writer.Write((byte)stats.Tags);
        writer.Write(stats.IsSoul);
        writer.Write(stats.IsColorless);
        writer.Write(stats.PlaysRecordRisk);
        BlitzCodec.WriteDecimals(writer, stats.Vars);
    }

    private static BlitzCardStats ReadCardStats(BinaryReader reader)
        => new()
        {
            Type = (BlitzCardType)ReadByteEnum(reader, "卡牌类型"),
            Target = (BlitzTargetType)ReadByteEnum(reader, "卡牌目标"),
            Rarity = (BlitzCardRarity)ReadByteEnum(reader, "卡牌稀有度"),
            Pool = (BlitzTransformPool)ReadByteEnum(reader, "卡牌池"),
            EnergyCost = reader.ReadInt32(),
            CostsX = BlitzCodec.ReadBool(reader),
            StarCost = reader.ReadInt32(),
            StarCostsX = BlitzCodec.ReadBool(reader),
            Keywords = (BlitzKeywords)ReadByteEnum(reader, "卡牌关键字"),
            HasDynamicType = BlitzCodec.ReadBool(reader),
            MaxUpgradeLevel = reader.ReadInt32(),
            Tags = (BlitzCardTag)ReadByteEnum(reader, "卡牌标签"),
            IsSoul = BlitzCodec.ReadBool(reader),
            IsColorless = BlitzCodec.ReadBool(reader),
            PlaysRecordRisk = BlitzCodec.ReadBool(reader),
            Vars = BlitzCodec.ReadDecimals(reader),
        };

    private static void WritePotion(BinaryWriter writer, BlitzPotionStats potion)
    {
        writer.Write((byte)potion.Target);
        writer.Write((byte)potion.Rarity);
        writer.Write(potion.StrategicCost);
        BlitzCodec.WriteDecimals(writer, potion.Vars);
        writer.Write(potion.VarProps.Length);
        for (int index = 0; index < potion.VarProps.Length; index++)
            writer.Write((byte)potion.VarProps[index]);
    }

    private static BlitzPotionStats ReadPotion(BinaryReader reader)
    {
        BlitzTargetType target = (BlitzTargetType)ReadByteEnum(reader, "药水目标");
        BlitzPotionRarity rarity = (BlitzPotionRarity)ReadByteEnum(reader, "药水稀有度");
        int cost = reader.ReadInt32();
        decimal[] vars = BlitzCodec.ReadDecimals(reader);
        int propsCount = BlitzCodec.ReadCount(reader, 1 << 16, "药水变量属性");
        BlitzValueProps[] props = new BlitzValueProps[propsCount];
        for (int index = 0; index < propsCount; index++)
            props[index] = (BlitzValueProps)ReadByteEnum(reader, "药水变量属性");
        return new BlitzPotionStats
        {
            Target = target,
            Rarity = rarity,
            StrategicCost = cost,
            Vars = vars,
            VarProps = props,
        };
    }

    private static void WriteMonster(BinaryWriter writer, BlitzMonsterRules monster)
    {
        writer.Write(monster.MonsterId);
        writer.Write(monster.Nodes.Length);
        for (int index = 0; index < monster.Nodes.Length; index++)
        {
            BlitzMonsterNode node = monster.Nodes[index];
            BlitzCodec.WriteString(writer, node.NativeId);
            writer.Write((byte)node.Kind);
            writer.Write(node.FollowUp);
            writer.Write(node.MustPerformOnceBeforeTransitioning);
            writer.Write(node.Attacks.Length);
            for (int attack = 0; attack < node.Attacks.Length; attack++)
            {
                writer.Write(node.Attacks[attack].BaseDamage);
                writer.Write(node.Attacks[attack].Repeats);
            }
            writer.Write(node.Options.Length);
            for (int option = 0; option < node.Options.Length; option++)
            {
                writer.Write(node.Options[option].Target);
                writer.Write(node.Options[option].BaseWeight);
                writer.Write((byte)node.Options[option].RepeatType);
                writer.Write(node.Options[option].MaxTimes);
                writer.Write(node.Options[option].Cooldown);
            }
            writer.Write(node.ConditionalTarget);
        }
        writer.Write(monster.InitialNode);
        writer.Write(monster.PendingInitialMoveNode);
        writer.Write(monster.InitialMoveLogged);
        writer.Write(monster.StunnedNode);
        writer.Write(monster.ReviveNode);
        writer.Write(monster.InitialHpMin);
        writer.Write(monster.InitialHpMax);
        BlitzCodec.WriteIntArray(writer, monster.StaticInts);
        BlitzCodec.WriteIntArray(writer, monster.RootPowerAmounts);
        BlitzCodec.WriteIntArray(writer, monster.InitialAiRegisters);
    }

    private static BlitzMonsterRules ReadMonster(BinaryReader reader)
    {
        ushort monsterId = reader.ReadUInt16();
        int nodeCount = BlitzCodec.ReadCount(reader, 1 << 12, "怪物节点");
        BlitzMonsterNode[] nodes = new BlitzMonsterNode[nodeCount];
        for (int index = 0; index < nodeCount; index++)
        {
            string nativeId = BlitzCodec.ReadString(reader)
                ?? throw new InvalidDataException("怪物节点 id 不能为 null。");
            BlitzMonsterNodeKind kind = (BlitzMonsterNodeKind)ReadByteEnum(reader, "怪物节点类型");
            int followUp = reader.ReadInt32();
            bool mustPerform = BlitzCodec.ReadBool(reader);
            int attackCount = BlitzCodec.ReadCount(reader, 1 << 12, "怪物攻击");
            BlitzMonsterAttack[] attacks = new BlitzMonsterAttack[attackCount];
            for (int attack = 0; attack < attackCount; attack++)
                attacks[attack] = new BlitzMonsterAttack(reader.ReadInt32(), reader.ReadInt32());
            int optionCount = BlitzCodec.ReadCount(reader, 1 << 12, "怪物分支");
            BlitzRandomBranchOption[] options = new BlitzRandomBranchOption[optionCount];
            for (int option = 0; option < optionCount; option++)
            {
                int target = reader.ReadInt32();
                float weight = reader.ReadSingle();
                BlitzMoveRepeatType repeat = (BlitzMoveRepeatType)ReadByteEnum(reader, "怪物重复类型");
                options[option] = new BlitzRandomBranchOption(target, weight, repeat,
                    reader.ReadInt32(), reader.ReadInt32());
            }
            nodes[index] = new BlitzMonsterNode
            {
                NativeId = nativeId,
                Kind = kind,
                FollowUp = followUp,
                MustPerformOnceBeforeTransitioning = mustPerform,
                Attacks = attacks,
                Options = options,
                ConditionalTarget = reader.ReadInt32(),
            };
        }
        return new BlitzMonsterRules
        {
            MonsterId = monsterId,
            Nodes = nodes,
            InitialNode = reader.ReadInt32(),
            PendingInitialMoveNode = reader.ReadInt32(),
            InitialMoveLogged = BlitzCodec.ReadBool(reader),
            StunnedNode = reader.ReadInt32(),
            ReviveNode = reader.ReadInt32(),
            InitialHpMin = reader.ReadInt32(),
            InitialHpMax = reader.ReadInt32(),
            StaticInts = BlitzCodec.ReadIntArray(reader, 1 << 16, "怪物静态整数"),
            RootPowerAmounts = BlitzCodec.ReadIntArray(reader, 1 << 16, "怪物根 Power 数值"),
            InitialAiRegisters = BlitzCodec.ReadIntArray(reader, 1 << 16, "怪物初始 AI 寄存器"),
        };
    }

    private static byte ReadByteEnum(BinaryReader reader, string what)
    {
        byte value = reader.ReadByte();
        return value;
    }
}
