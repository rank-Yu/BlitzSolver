using BlitzSolver.Engine;

namespace BlitzSolver.Content.Monsters.Act3;

/// <summary>
/// LivingShield: SMASH_MOVE applies Strength 3 to itself. SHIELD_SLAM_BRANCH picks SHIELD_SLAM while
/// another enemy is alive and SMASH otherwise (<c>MonsterMoveEffects</c> and
/// <c>BranchMonsterAi.ResolveConditional</c>). Its starting RampartPower 25 is a root power; the
/// existing engine has no Rampart mirror.
/// </summary>
[BlitzContent("LIVING_SHIELD")]
public sealed class LivingShield : BlitzMonsterDef
{
    public override int ResolveConditional(ref BlitzExec x, byte owner, int branchNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "SHIELD_SLAM_BRANCH")
            return -1;
        bool hasAliveAlly = false;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte other = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (other != owner && x.B.Creature(other).IsAlive)
            {
                hasAliveAlly = true;
                break;
            }
        }
        return hasAliveAlly ? rules.NodeIndex("SHIELD_SLAM_MOVE") : rules.NodeIndex("SMASH_MOVE");
    }

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "SMASH_MOVE")
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 3, owner);
    }
}

/// <summary>
/// TurretOperator: RELOAD_MOVE applies Strength 1 to itself; UNLOAD_MOVE and UNLOAD_MOVE_2 are plain
/// attacks (<c>MonsterMoveEffects</c> "TurretOperator"). Rampart, applied by the Living Shield, is a
/// root power on that ally.
/// </summary>
[BlitzContent("TURRET_OPERATOR")]
public sealed class TurretOperator : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "RELOAD_MOVE")
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 1, owner);
    }
}

/// <summary>
/// TorchHeadAmalgam: all five moves are plain attacks; its starting MinionPower 1 is a root power.
/// Its death drives the Queen's <c>_hasAmalgamDied</c> branch (<see cref="Queen"/>).
/// </summary>
[BlitzContent("TORCH_HEAD_AMALGAM")]
public sealed class TorchHeadAmalgam : BlitzMonsterDef;

/// <summary>
/// Aeonglass: EBB_MOVE gains <c>EbbBlock</c>; INCREASING_INTENSITY_MOVE fake-upgrades every Wither
/// (Damage +3), advances <c>WitherUpgradeCount</c>, adds <c>WitherAmount</c> Wither to the discard
/// and applies <c>IncreasingIntensityBaseStrength + AdditionalStrength</c>, advancing the latter
/// (<c>MonsterMoveEffects</c> "Aeonglass"). The two private counters are the branch dictionaries
/// projected through the AI registers; after every monster move the kernel normalizes every Wither
/// to the summed upgrade level (<c>SimulatedCombatState.NormalizeAeonglassWithers</c>).
/// </summary>
[BlitzContent("AEONGLASS")]
public sealed class Aeonglass : BlitzMonsterDef
{
    private const int EbbBlockStat = 0;
    private const int WitherAmountStat = 1;
    private const int IncreasingIntensityBaseStrengthStat = 2;
    private const int AdditionalStrengthRegister = 0;
    private const int WitherUpgradeCountRegister = 1;
    private const short WitherUpgradeDamage = 3;

    public override IReadOnlyList<string> StaticIntNames =>
        ["EbbBlock", "WitherAmount", "IncreasingIntensityBaseStrength"];

    public override IReadOnlyList<string> AiRegisterNames => ["AdditionalStrength", "WitherUpgradeCount"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("WITHER", 0)];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "EBB_MOVE":
                BlitzKernel.GainBlock(ref x, owner, rules.StaticInts[EbbBlockStat],
                    BlitzValueProps.Move, fromCard: false);
                break;
            case "INCREASING_INTENSITY_MOVE":
                FakeUpgradeWithers(ref x);
                ref BlitzEnemy enemy = ref x.B.Enemy(owner);
                enemy.Ai[WitherUpgradeCountRegister]++;
                for (int index = 0; index < rules.StaticInts[WitherAmountStat]; index++)
                {
                    BlitzKernel.AddGeneratedCard(ref x,
                        BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Wither>()), BlitzPile.Discard, creator: BlitzLimits.NoOwner);
                }
                int strength = rules.StaticInts[IncreasingIntensityBaseStrengthStat]
                    + x.B.Enemy(owner).Ai[AdditionalStrengthRegister];
                x.B.Enemy(owner).Ai[AdditionalStrengthRegister]++;
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), strength, owner);
                break;
        }
    }

    public override void AfterCardGeneratedForCombat(ref BlitzExec x, BlitzListener self, int position, byte creator)
    {
        if (x.B.Cards[position].Id != BlitzRegistry.CardId<Cards.Status.Wither>())
            return;
        int upgrades = x.B.Enemy(self.Owner).Ai[WitherUpgradeCountRegister];
        int damage = x.B.Cards[position].Var0 + upgrades * WitherUpgradeDamage;
        if (damage > short.MaxValue)
            throw new BlitzUnsupportedException("Wither 的伪升级层数超出短整型表示。");
        x.B.Cards[position].Var0 = (short)damage;
    }

    private static void FakeUpgradeWithers(ref BlitzExec x)
    {
        if (!BlitzRegistry.TryCard("WITHER", out ushort wither))
            return;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref BlitzCard card = ref x.B.Cards[position];
            if (card.Id != wither)
                continue;
            int upgraded = card.Var0 + WitherUpgradeDamage;
            card.Var0 = upgraded > short.MaxValue
                ? throw new BlitzUnsupportedException("Wither 的伪升级层数超出短整型表示。")
                : (short)upgraded;
        }
    }
}

/// <summary>
/// Fabricator: FABRICATE_MOVE summons one defensive and one aggro bot, FABRICATING_STRIKE_MOVE
/// summons one aggro bot after its attack; fabriateBranch picks RAND while fewer than four enemies
/// are alive and DISINTEGRATE_MOVE otherwise (<c>MonsterMoveEffects</c> "Fabricator",
/// <c>BranchMonsterAi.ResolveConditional</c>). <c>fabricator_last_spawn</c> stores the last bot kind
/// and excludes it from the next <c>Rng.MonsterAi.NextItem</c> pick.
/// </summary>
[BlitzContent("FABRICATOR")]
public sealed class Fabricator : BlitzMonsterDef
{
    private const int LastSpawnRegister = 0;
    private const int BotKindGuardbot = 1;
    private const int BotKindNoisebot = 2;
    private const int BotKindZapbot = 3;
    private const int BotKindStabbot = 4;

    public override IReadOnlyList<string> AiRegisterNames => ["fabricator_last_spawn"];

    public override IReadOnlyList<string> SpawnedMonsterIds =>
        ["GUARDBOT", "NOISEBOT", "ZAPBOT", "STABBOT"];

    public override int ResolveConditional(ref BlitzExec x, byte owner, int branchNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "fabricateBranch")
            return -1;
        int alive = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            if (x.B.Creature(BlitzKernel.EnemyOwner(x.B.Roster[position])).IsAlive)
                alive++;
        }
        return alive < 4 ? rules.NodeIndex("RAND") : rules.NodeIndex("DISINTEGRATE_MOVE");
    }

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        switch (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "FABRICATE_MOVE":
                SpawnBot(ref x, owner, defensive: true);
                if (!x.NeedsChoice)
                    SpawnBot(ref x, owner, defensive: false);
                break;
            case "FABRICATING_STRIKE_MOVE":
                SpawnBot(ref x, owner, defensive: false);
                break;
        }
    }

    private static void SpawnBot(ref BlitzExec x, byte owner, bool defensive)
    {
        int previous = x.B.Enemy(owner).Ai[LastSpawnRegister];
        Span<int> candidates = stackalloc int[defensive ? 2 : 2];
        int count = 0;
        int first = defensive ? BotKindGuardbot : BotKindZapbot;
        int second = defensive ? BotKindNoisebot : BotKindStabbot;
        if (first != previous)
            candidates[count++] = first;
        if (second != previous)
            candidates[count++] = second;
        int selected = candidates[x.B.RngStream(BlitzRngStream.MonsterAi).NextInt(0, count)];
        x.B.Enemy(owner).Ai[LastSpawnRegister] = selected;
        string nativeId = selected switch
        {
            BotKindGuardbot => "GUARDBOT",
            BotKindNoisebot => "NOISEBOT",
            BotKindZapbot => "ZAPBOT",
            BotKindStabbot => "STABBOT",
            _ => throw new BlitzUnsupportedException($"未知的组装师召唤编号 {selected}。"),
        };
        if (!BlitzRegistry.TryMonster(nativeId, out ushort monsterId)
            || !x.R.TrySpawnRulesId(monsterId, out int rulesId))
        {
            throw new BlitzUnsupportedException($"组装师召唤 {nativeId} 没有生成规则条目。");
        }
        string? slot = BlitzKernel.NextFreeSlot(x.B, x.R);
        int slotIndex = string.IsNullOrEmpty(slot) ? BlitzKernel.NoSlot : BlitzKernel.SlotIndex(x.R, slot);
        BlitzKernel.SpawnMonster(ref x, owner, rulesId, slotIndex, minion: true);
    }
}

/// <summary>Guardbot: GUARD_MOVE grants 15 Unpowered Block to every Fabricator.</summary>
[BlitzContent("GUARDBOT")]
public sealed class Guardbot : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId != "GUARD_MOVE")
            return;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte other = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (x.B.Enemy(other).MonsterId == BlitzRegistry.MonsterId<Fabricator>())
                BlitzKernel.GainBlock(ref x, other, 15, BlitzValueProps.Unpowered, fromCard: false);
        }
    }
}

/// <summary>Noisebot: NOISE_MOVE adds one Dazed to the discard pile and one at a random draw-pile position.</summary>
[BlitzContent("NOISEBOT")]
public sealed class Noisebot : BlitzMonsterDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DAZED", 0)];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId != "NOISE_MOVE")
            return;
        ushort dazed = BlitzRegistry.CardId<Cards.Status.Dazed>();
        BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CreateCard(x.R, dazed), BlitzPile.Discard, creator: BlitzLimits.NoOwner);
        if (x.NeedsChoice)
            return;
        BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CreateCard(x.R, dazed), BlitzPile.Draw,
            BlitzPilePosition.Random, creator: BlitzLimits.NoOwner);
    }
}

/// <summary>
/// Zapbot: ZAP is a plain attack. Its entrance HighVoltagePower 2 is applied by
/// <see cref="ApplyEntrancePowers"/> (<c>MonsterSpawnSupport.ApplyNativeEntrancePowers</c>).
/// </summary>
[BlitzContent("ZAPBOT")]
public sealed class Zapbot : BlitzMonsterDef
{
    public override void ApplyEntrancePowers(ref BlitzExec x, byte owner)
        => BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.HighVoltagePower>(), 2, owner);
}

/// <summary>Stabbot: STAB_MOVE applies Frail 1 to the player (<c>MonsterMoveEffects</c> "Stabbot").</summary>
[BlitzContent("STABBOT")]
public sealed class Stabbot : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "STAB_MOVE")
            BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                BlitzRegistry.PowerId<Powers.FrailPower>(), 1, owner);
    }
}

/// <summary>
/// Axebot: BOOT_UP_MOVE gains <c>BootUpBlock</c> and Strength <c>BootUpStrGain × RespawnCount</c>
/// (RespawnCount = 2 - StockPower), HAMMER_UPPERCUT_MOVE applies Weak/Frail 2
/// (<c>MonsterMoveEffects</c> "Axebot"). On death with StockPower it spawns the next Axebot in the
/// same slot with one less stock (<c>StockPower.AfterDeath</c>, <c>SpawnStockReplacement</c>); the
/// spawn rule is compiled with StockAmount 1 so it starts from BOOT_UP_MOVE and the +10 HP range, and
/// the entrance adds the remaining +10 when the replacement has no stock left.
/// </summary>
[BlitzContent("AXEBOT")]
public sealed class Axebot : BlitzMonsterDef
{
    private const int BootUpBlockStat = 0;
    private const int BootUpStrengthStat = 1;
    private const int StockPerRespawnHp = 10;

    public override IReadOnlyList<string> StaticIntNames => ["BootUpBlock", "BootUpStrGain"];

    public override IReadOnlyList<string> SpawnedMonsterIds => ["AXEBOT"];

    public override int SpawnStockAmount => 1;

    public override void ApplyEntrancePowers(ref BlitzExec x, byte owner, byte source)
    {
        if (!BlitzRegistry.TryPower("STOCK_POWER", out ushort stock))
            return;
        int newStock = BlitzKernel.GetAmount(x.B, source, stock) - 1;
        if (newStock < 0)
            newStock = 0;
        // The HP roll already used the replacement's RespawnCount (see AfterDeath); the entrance only
        // applies the remaining StockPower (native ApplyNativeEntrancePowers).
        if (newStock > 0)
            BlitzKernel.ApplyPower(ref x, owner, stock, newStock, BlitzLimits.NoOwner);
    }

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "BOOT_UP_MOVE":
                BlitzKernel.GainBlock(ref x, owner, rules.StaticInts[BootUpBlockStat],
                    BlitzValueProps.Move, fromCard: false);
                int stock = BlitzRegistry.TryPower("STOCK_POWER", out ushort stockId)
                    ? BlitzKernel.GetAmount(x.B, owner, stockId)
                    : 0;
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
                    rules.StaticInts[BootUpStrengthStat] * (2 - stock), owner);
                break;
            case "HAMMER_UPPERCUT_MOVE":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.WeakPower>(), 2, owner);
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.FrailPower>(), 2, owner);
                break;
        }
    }

    public override void AfterDeath(ref BlitzExec x, BlitzListener self, byte creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || creature != self.Owner
            || !BlitzRegistry.TryPower("STOCK_POWER", out ushort stock)
            || BlitzKernel.GetAmount(x.B, self.Owner, stock) <= 0)
        {
            return;
        }
        if (!BlitzRegistry.TryMonster("AXEBOT", out ushort monsterId)
            || !x.R.TrySpawnRulesId(monsterId, out int rulesId))
        {
            throw new BlitzUnsupportedException("Axebot 补货没有生成规则条目。");
        }
        int newStock = BlitzKernel.GetAmount(x.B, self.Owner, stock) - 1;
        if (newStock < 0)
            newStock = 0;
        // The compiled rule is the StockAmount-1 variant (RespawnCount 1, +10 HP range); missing
        // further stock shifts the whole range by another RespawnMaxHpBonus (native RespawnCount =
        // 2 - StockAmount feeds MinInitialHp/MaxInitialHp before CreatePredictedMonster rolls).
        BlitzKernel.SpawnMonster(ref x, self.Owner, rulesId, x.B.Enemy(self.Owner).Slot, minion: false,
            hpRangeShift: (1 - newStock) * StockPerRespawnHp);
    }
}

/// <summary>
/// TestSubject: the three-form boss. AdaptablePower keeps the body and forces the native
/// RESPAWN_MOVE; <see cref="ResolveReviveMove"/> sets the next form's HP, applies PainfulStabs 1
/// (first respawn) or Nemesis 1 (second, removing Adaptable and PainfulStabs), mirroring
/// <c>SimulatedCombatState.ResolveTestSubjectRevive</c>. MULTI_CLAW_MOVE's hit count is
/// <c>BaseMultiClawCount + _extraMultiClawCount</c> and increments the extra count afterwards; SKULL
/// BASH applies Vulnerable 1 and BURNING GROWL adds Burn plus Strength (<c>MonsterMoveEffects</c>,
/// <c>BranchMonsterAi.CurrentMove</c>).
/// </summary>
[BlitzContent("TEST_SUBJECT")]
public sealed class TestSubject : BlitzMonsterDef
{
    internal const string MultiClawMoveId = "MULTI_CLAW_MOVE";

    private const int BurningGrowlBurnStat = 0;
    private const int BurningGrowlStrengthStat = 1;
    private const int RespawnsRegister = 0;
    private const int ExtraMultiClawRegister = 1;
    private const int SecondFormHpRegister = 2;
    private const int ThirdFormHpRegister = 3;

    public override IReadOnlyList<string> StaticIntNames =>
        ["BurningGrowlBurnCount", "BurningGrowlStrengthGain"];

    public override IReadOnlyList<string> AiRegisterNames =>
        ["_respawns", "_extraMultiClawCount", "SecondFormHp", "ThirdFormHp"];

    public override IReadOnlyList<string> AppliedPowers => ["PAINFUL_STABS_POWER", "NEMESIS_POWER"];

    public override bool HasAdaptableFormHp => true;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("BURN", 0)];

    public override int ResolveConditional(ref BlitzExec x, byte owner, int branchNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "REVIVE_BRANCH")
            return -1;
        return x.B.Enemy(owner).Ai[RespawnsRegister] < 2
            ? rules.NodeIndex("MULTI_CLAW_MOVE")
            : rules.NodeIndex("PHASE3_LACERATE_MOVE");
    }

    public override int ModifyAttackRepeats(in BlitzBattle battle, BlitzRules rules, byte owner, int moveNode,
        int attackIndex, int repeats)
    {
        if (rules.MonsterRule(battle.Enemy(owner).Rules).Nodes[moveNode].NativeId != "MULTI_CLAW_MOVE")
            return repeats;
        return repeats + battle.Enemy(owner).Ai[ExtraMultiClawRegister];
    }

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "SKULL_BASH_MOVE":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.VulnerablePower>(), 1, owner);
                break;
            case "MULTI_CLAW_MOVE":
                x.B.Enemy(owner).Ai[ExtraMultiClawRegister]++;
                break;
            case "BURNING_GROWL_MOVE":
                for (int index = 0; index < rules.StaticInts[BurningGrowlBurnStat]; index++)
                {
                    BlitzKernel.AddGeneratedCard(ref x,
                        BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Burn>()), BlitzPile.Discard, creator: BlitzLimits.NoOwner);
                }
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
                    rules.StaticInts[BurningGrowlStrengthStat], owner);
                break;
        }
    }

    public override void ResolveReviveMove(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "RESPAWN_MOVE")
            return;
        int respawns = x.B.Enemy(owner).Ai[RespawnsRegister] + 1;
        if (respawns is < 1 or > 2)
            throw new BlitzUnsupportedException($"测试体出现未知复活阶段 {respawns}。");
        x.B.Enemy(owner).Ai[RespawnsRegister] = respawns;
        int hp = x.B.Enemy(owner).Ai[respawns == 1 ? SecondFormHpRegister : ThirdFormHpRegister];
        ref BlitzCreature creature = ref x.B.Creature(owner);
        creature.MaxHp = hp;
        creature.CurrentHp = hp;
        x.B.Enemy(owner).DeathPhase = BlitzDeathPhase.None;
        if (respawns == 1)
        {
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.PainfulStabsPower>(), 1, owner);
        }
        else
        {
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.NemesisPower>(), 1, owner);
            BlitzKernel.SetPowerAmount(ref x.B, owner, BlitzRegistry.PowerId<Powers.AdaptablePower>(), 0);
            BlitzKernel.SetPowerAmount(ref x.B, owner, BlitzRegistry.PowerId<Powers.PainfulStabsPower>(), 0);
        }
    }
}
