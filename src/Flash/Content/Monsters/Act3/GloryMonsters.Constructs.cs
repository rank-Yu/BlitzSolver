using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Monsters.Act3;

/// <summary>
/// LivingShield: SMASH_MOVE applies Strength 3 to itself. SHIELD_SLAM_BRANCH picks SHIELD_SLAM while
/// another enemy is alive and SMASH otherwise (<c>MonsterMoveEffects</c> and
/// <c>BranchMonsterAi.ResolveConditional</c>). Its starting RampartPower 25 is a root power; the
/// existing engine has no Rampart mirror.
/// </summary>
[FlashContent("LIVING_SHIELD")]
public sealed class LivingShield : FlashMonsterDef
{
    public override int ResolveConditional(ref FlashExec x, byte owner, int branchNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "SHIELD_SLAM_BRANCH")
            return -1;
        bool hasAliveAlly = false;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte other = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (other != owner && x.B.Creature(other).IsAlive)
            {
                hasAliveAlly = true;
                break;
            }
        }
        return hasAliveAlly ? rules.NodeIndex("SHIELD_SLAM_MOVE") : rules.NodeIndex("SMASH_MOVE");
    }

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "SMASH_MOVE")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 3, owner);
    }
}

/// <summary>
/// TurretOperator: RELOAD_MOVE applies Strength 1 to itself; UNLOAD_MOVE and UNLOAD_MOVE_2 are plain
/// attacks (<c>MonsterMoveEffects</c> "TurretOperator"). Rampart, applied by the Living Shield, is a
/// root power on that ally.
/// </summary>
[FlashContent("TURRET_OPERATOR")]
public sealed class TurretOperator : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "RELOAD_MOVE")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 1, owner);
    }
}

/// <summary>
/// TorchHeadAmalgam: all five moves are plain attacks; its starting MinionPower 1 is a root power.
/// Its death drives the Queen's <c>_hasAmalgamDied</c> branch (<see cref="Queen"/>).
/// </summary>
[FlashContent("TORCH_HEAD_AMALGAM")]
public sealed class TorchHeadAmalgam : FlashMonsterDef;

/// <summary>
/// Aeonglass: EBB_MOVE gains <c>EbbBlock</c>; INCREASING_INTENSITY_MOVE fake-upgrades every Wither
/// (Damage +3), advances <c>WitherUpgradeCount</c>, adds <c>WitherAmount</c> Wither to the discard
/// and applies <c>IncreasingIntensityBaseStrength + AdditionalStrength</c>, advancing the latter
/// (<c>MonsterMoveEffects</c> "Aeonglass"). The two private counters are the branch dictionaries
/// projected through the AI registers; after every monster move the kernel normalizes every Wither
/// to the summed upgrade level (<c>SimulatedCombatState.NormalizeAeonglassWithers</c>).
/// </summary>
[FlashContent("AEONGLASS")]
public sealed class Aeonglass : FlashMonsterDef
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

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "EBB_MOVE":
                FlashKernel.GainBlock(ref x, owner, rules.StaticInts[EbbBlockStat],
                    FlashValueProps.Move, fromCard: false);
                break;
            case "INCREASING_INTENSITY_MOVE":
                FakeUpgradeWithers(ref x);
                ref FlashEnemy enemy = ref x.B.Enemy(owner);
                enemy.Ai[WitherUpgradeCountRegister]++;
                for (int index = 0; index < rules.StaticInts[WitherAmountStat]; index++)
                {
                    FlashKernel.AddGeneratedCard(ref x,
                        FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Wither>()), FlashPile.Discard, creator: FlashLimits.NoOwner);
                }
                int strength = rules.StaticInts[IncreasingIntensityBaseStrengthStat]
                    + x.B.Enemy(owner).Ai[AdditionalStrengthRegister];
                x.B.Enemy(owner).Ai[AdditionalStrengthRegister]++;
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), strength, owner);
                break;
        }
    }

    public override void AfterCardGeneratedForCombat(ref FlashExec x, FlashListener self, int position, byte creator)
    {
        if (x.B.Cards[position].Id != FlashRegistry.CardId<Cards.Status.Wither>())
            return;
        int upgrades = x.B.Enemy(self.Owner).Ai[WitherUpgradeCountRegister];
        int damage = x.B.Cards[position].Var0 + upgrades * WitherUpgradeDamage;
        if (damage > short.MaxValue)
            throw new FlashUnsupportedException("Wither 的伪升级层数超出短整型表示。");
        x.B.Cards[position].Var0 = (short)damage;
    }

    private static void FakeUpgradeWithers(ref FlashExec x)
    {
        if (!FlashRegistry.TryCard("WITHER", out ushort wither))
            return;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref FlashCard card = ref x.B.Cards[position];
            if (card.Id != wither)
                continue;
            int upgraded = card.Var0 + WitherUpgradeDamage;
            card.Var0 = upgraded > short.MaxValue
                ? throw new FlashUnsupportedException("Wither 的伪升级层数超出短整型表示。")
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
[FlashContent("FABRICATOR")]
public sealed class Fabricator : FlashMonsterDef
{
    private const int LastSpawnRegister = 0;
    private const int BotKindGuardbot = 1;
    private const int BotKindNoisebot = 2;
    private const int BotKindZapbot = 3;
    private const int BotKindStabbot = 4;

    public override IReadOnlyList<string> AiRegisterNames => ["fabricator_last_spawn"];

    public override IReadOnlyList<string> SpawnedMonsterIds =>
        ["GUARDBOT", "NOISEBOT", "ZAPBOT", "STABBOT"];

    public override int ResolveConditional(ref FlashExec x, byte owner, int branchNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "fabricateBranch")
            return -1;
        int alive = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            if (x.B.Creature(FlashKernel.EnemyOwner(x.B.Roster[position])).IsAlive)
                alive++;
        }
        return alive < 4 ? rules.NodeIndex("RAND") : rules.NodeIndex("DISINTEGRATE_MOVE");
    }

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        switch (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
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

    private static void SpawnBot(ref FlashExec x, byte owner, bool defensive)
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
        int selected = candidates[x.B.RngStream(FlashRngStream.MonsterAi).NextInt(0, count)];
        x.B.Enemy(owner).Ai[LastSpawnRegister] = selected;
        string nativeId = selected switch
        {
            BotKindGuardbot => "GUARDBOT",
            BotKindNoisebot => "NOISEBOT",
            BotKindZapbot => "ZAPBOT",
            BotKindStabbot => "STABBOT",
            _ => throw new FlashUnsupportedException($"未知的组装师召唤编号 {selected}。"),
        };
        if (!FlashRegistry.TryMonster(nativeId, out ushort monsterId)
            || !x.R.TrySpawnRulesId(monsterId, out int rulesId))
        {
            throw new FlashUnsupportedException($"组装师召唤 {nativeId} 没有生成规则条目。");
        }
        string? slot = FlashKernel.NextFreeSlot(x.B, x.R);
        int slotIndex = string.IsNullOrEmpty(slot) ? FlashKernel.NoSlot : FlashKernel.SlotIndex(x.R, slot);
        FlashKernel.SpawnMonster(ref x, owner, rulesId, slotIndex, minion: true);
    }
}

/// <summary>Guardbot: GUARD_MOVE grants 15 Unpowered Block to every Fabricator.</summary>
[FlashContent("GUARDBOT")]
public sealed class Guardbot : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId != "GUARD_MOVE")
            return;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte other = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (x.B.Enemy(other).MonsterId == FlashRegistry.MonsterId<Fabricator>())
                FlashKernel.GainBlock(ref x, other, 15, FlashValueProps.Unpowered, fromCard: false);
        }
    }
}

/// <summary>Noisebot: NOISE_MOVE adds one Dazed to the discard pile and one at a random draw-pile position.</summary>
[FlashContent("NOISEBOT")]
public sealed class Noisebot : FlashMonsterDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DAZED", 0)];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId != "NOISE_MOVE")
            return;
        ushort dazed = FlashRegistry.CardId<Cards.Status.Dazed>();
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, dazed), FlashPile.Discard, creator: FlashLimits.NoOwner);
        if (x.NeedsChoice)
            return;
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, dazed), FlashPile.Draw,
            FlashPilePosition.Random, creator: FlashLimits.NoOwner);
    }
}

/// <summary>
/// Zapbot: ZAP is a plain attack. Its entrance HighVoltagePower 2 is applied by
/// <see cref="ApplyEntrancePowers"/> (<c>MonsterSpawnSupport.ApplyNativeEntrancePowers</c>).
/// </summary>
[FlashContent("ZAPBOT")]
public sealed class Zapbot : FlashMonsterDef
{
    public override void ApplyEntrancePowers(ref FlashExec x, byte owner)
        => FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.HighVoltagePower>(), 2, owner);
}

/// <summary>Stabbot: STAB_MOVE applies Frail 1 to the player (<c>MonsterMoveEffects</c> "Stabbot").</summary>
[FlashContent("STABBOT")]
public sealed class Stabbot : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "STAB_MOVE")
            FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                FlashRegistry.PowerId<Powers.FrailPower>(), 1, owner);
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
[FlashContent("AXEBOT")]
public sealed class Axebot : FlashMonsterDef
{
    private const int BootUpBlockStat = 0;
    private const int BootUpStrengthStat = 1;
    private const int StockPerRespawnHp = 10;

    public override IReadOnlyList<string> StaticIntNames => ["BootUpBlock", "BootUpStrGain"];

    public override IReadOnlyList<string> SpawnedMonsterIds => ["AXEBOT"];

    public override int SpawnStockAmount => 1;

    public override void ApplyEntrancePowers(ref FlashExec x, byte owner, byte source)
    {
        if (!FlashRegistry.TryPower("STOCK_POWER", out ushort stock))
            return;
        int newStock = FlashKernel.GetAmount(x.B, source, stock) - 1;
        if (newStock < 0)
            newStock = 0;
        // The HP roll already used the replacement's RespawnCount (see AfterDeath); the entrance only
        // applies the remaining StockPower (native ApplyNativeEntrancePowers).
        if (newStock > 0)
            FlashKernel.ApplyPower(ref x, owner, stock, newStock, FlashLimits.NoOwner);
    }

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "BOOT_UP_MOVE":
                FlashKernel.GainBlock(ref x, owner, rules.StaticInts[BootUpBlockStat],
                    FlashValueProps.Move, fromCard: false);
                int stock = FlashRegistry.TryPower("STOCK_POWER", out ushort stockId)
                    ? FlashKernel.GetAmount(x.B, owner, stockId)
                    : 0;
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(),
                    rules.StaticInts[BootUpStrengthStat] * (2 - stock), owner);
                break;
            case "HAMMER_UPPERCUT_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.WeakPower>(), 2, owner);
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.FrailPower>(), 2, owner);
                break;
        }
    }

    public override void AfterDeath(ref FlashExec x, FlashListener self, byte creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || creature != self.Owner
            || !FlashRegistry.TryPower("STOCK_POWER", out ushort stock)
            || FlashKernel.GetAmount(x.B, self.Owner, stock) <= 0)
        {
            return;
        }
        if (!FlashRegistry.TryMonster("AXEBOT", out ushort monsterId)
            || !x.R.TrySpawnRulesId(monsterId, out int rulesId))
        {
            throw new FlashUnsupportedException("Axebot 补货没有生成规则条目。");
        }
        int newStock = FlashKernel.GetAmount(x.B, self.Owner, stock) - 1;
        if (newStock < 0)
            newStock = 0;
        // The compiled rule is the StockAmount-1 variant (RespawnCount 1, +10 HP range); missing
        // further stock shifts the whole range by another RespawnMaxHpBonus (native RespawnCount =
        // 2 - StockAmount feeds MinInitialHp/MaxInitialHp before CreatePredictedMonster rolls).
        FlashKernel.SpawnMonster(ref x, self.Owner, rulesId, x.B.Enemy(self.Owner).Slot, minion: false,
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
[FlashContent("TEST_SUBJECT")]
public sealed class TestSubject : FlashMonsterDef
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

    public override int ResolveConditional(ref FlashExec x, byte owner, int branchNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "REVIVE_BRANCH")
            return -1;
        return x.B.Enemy(owner).Ai[RespawnsRegister] < 2
            ? rules.NodeIndex("MULTI_CLAW_MOVE")
            : rules.NodeIndex("PHASE3_LACERATE_MOVE");
    }

    public override int ModifyAttackRepeats(in FlashBattle battle, FlashRules rules, byte owner, int moveNode,
        int attackIndex, int repeats)
    {
        if (rules.MonsterRule(battle.Enemy(owner).Rules).Nodes[moveNode].NativeId != "MULTI_CLAW_MOVE")
            return repeats;
        return repeats + battle.Enemy(owner).Ai[ExtraMultiClawRegister];
    }

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "SKULL_BASH_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.VulnerablePower>(), 1, owner);
                break;
            case "MULTI_CLAW_MOVE":
                x.B.Enemy(owner).Ai[ExtraMultiClawRegister]++;
                break;
            case "BURNING_GROWL_MOVE":
                for (int index = 0; index < rules.StaticInts[BurningGrowlBurnStat]; index++)
                {
                    FlashKernel.AddGeneratedCard(ref x,
                        FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Burn>()), FlashPile.Discard, creator: FlashLimits.NoOwner);
                }
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(),
                    rules.StaticInts[BurningGrowlStrengthStat], owner);
                break;
        }
    }

    public override void ResolveReviveMove(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "RESPAWN_MOVE")
            return;
        int respawns = x.B.Enemy(owner).Ai[RespawnsRegister] + 1;
        if (respawns is < 1 or > 2)
            throw new FlashUnsupportedException($"测试体出现未知复活阶段 {respawns}。");
        x.B.Enemy(owner).Ai[RespawnsRegister] = respawns;
        int hp = x.B.Enemy(owner).Ai[respawns == 1 ? SecondFormHpRegister : ThirdFormHpRegister];
        ref FlashCreature creature = ref x.B.Creature(owner);
        creature.MaxHp = hp;
        creature.CurrentHp = hp;
        x.B.Enemy(owner).DeathPhase = FlashDeathPhase.None;
        if (respawns == 1)
        {
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.PainfulStabsPower>(), 1, owner);
        }
        else
        {
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.NemesisPower>(), 1, owner);
            FlashKernel.SetPowerAmount(ref x.B, owner, FlashRegistry.PowerId<Powers.AdaptablePower>(), 0);
            FlashKernel.SetPowerAmount(ref x.B, owner, FlashRegistry.PowerId<Powers.PainfulStabsPower>(), 0);
        }
    }
}
