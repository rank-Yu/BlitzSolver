namespace CombatSolver.Engine.InCombat.Flash.Content.Monsters.Act3;

// Third-act (Glory) monsters. Attacks, hit counts and branch weights come from the compiled state
// machine; ApplyMoveEffects copies the post-attack part of MonsterMoveEffects.Apply for the same
// (monster, move) pair. ApplyPowerFromMonster = combat.ApplyFromMonster; ApplyPower with the owner as
// applier = combat.Apply(..., owner); ApplyPower with NoOwner = combat.Apply with a null applier;
// GainBlock = simulator.GainBlock(..., ValueProp.Move).

/// <summary>
/// DevotedSculptor: "FORBIDDEN_INCANTATION_MOVE" applies RitualPower with the private
/// <c>_ritualGain</c> field (9), and SAVAGE_MOVE is a plain attack
/// (<c>MonsterMoveEffects</c> "DevotedSculptor").
/// </summary>
[FlashContent("DEVOTED_SCULPTOR")]
public sealed class DevotedSculptor : FlashMonsterDef
{
    private const int RitualGain = 0;

    public override IReadOnlyList<string> StaticIntNames => ["_ritualGain"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "FORBIDDEN_INCANTATION_MOVE")
            return;
        FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.RitualPower>(),
            rules.StaticInts[RitualGain], FlashLimits.NoOwner);
    }
}

/// <summary>
/// FlailKnight: WAR_CHANT applies Strength 3 to itself; FLAIL_MOVE and RAM_MOVE are plain attacks
/// (<c>MonsterMoveEffects</c> "FlailKnight").
/// </summary>
[FlashContent("FLAIL_KNIGHT")]
public sealed class FlailKnight : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "WAR_CHANT")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 3, owner);
    }
}

/// <summary>
/// SpectralKnight: "HEX" applies HexPower 2 to the player; SOUL_SLASH and SOUL_FLAME are plain attacks
/// (<c>MonsterMoveEffects</c> "SpectralKnight").
/// </summary>
[FlashContent("SPECTRAL_KNIGHT")]
public sealed class SpectralKnight : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId != "HEX")
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.HexPower>(), 2, owner);
    }
}

/// <summary>
/// GlobeHead: SHOCKING_SLAP applies Frail 2 to the player, GALVANIC_BURST applies Strength 2 to
/// itself; THUNDER_STRIKE is a plain attack (<c>MonsterMoveEffects</c> "GlobeHead"). Its starting
/// GalvanicPower 6 (8 on DeadlyEnemies) comes from the root powers, and Galvanized is normalized by
/// the kernel.
/// </summary>
[FlashContent("GLOBE_HEAD")]
public sealed class GlobeHead : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        switch (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "SHOCKING_SLAP":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.FrailPower>(), 2, owner);
                break;
            case "GALVANIC_BURST":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
                break;
        }
    }
}

/// <summary>
/// FrogKnight: FOR_THE_QUEEN applies Strength 5, TONGUE_LASH applies Frail 2, BEETLE_CHARGE marks
/// <c>_hasBeetleCharged</c>; HALF_HEALTH picks TONGUE_LASH while charged or at half HP or more and
/// BEETLE_CHARGE otherwise (<c>MonsterMoveEffects</c> and <c>BranchMonsterAi.ResolveConditional</c>).
/// The starting PlatingPower 15 (19 on ToughEnemies) is a root power.
/// </summary>
[FlashContent("FROG_KNIGHT")]
public sealed class FrogKnight : FlashMonsterDef
{
    private const int HasBeetleCharged = 0;

    public override IReadOnlyList<string> AiRegisterNames => ["_hasBeetleCharged"];

    public override int ResolveConditional(ref FlashExec x, byte owner, int branchNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "HALF_HEALTH")
            return -1;
        ref readonly FlashCreature creature = ref x.B.Creature(owner);
        bool charged = x.B.Enemy(owner).Ai[HasBeetleCharged] != 0;
        return charged || creature.CurrentHp >= creature.MaxHp / 2
            ? rules.NodeIndex("TONGUE_LASH")
            : rules.NodeIndex("BEETLE_CHARGE");
    }

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "FOR_THE_QUEEN":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 5, owner);
                break;
            case "TONGUE_LASH":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.FrailPower>(), 2, owner);
                break;
            case "BEETLE_CHARGE":
                // MonsterMoveEffects.Apply sets the private flag before the switch, after the attack.
                x.B.Enemy(owner).Ai[HasBeetleCharged] = 1;
                break;
        }
    }
}

/// <summary>
/// MechaKnight: FLAMETHROWER_MOVE adds four Burn to the player's hand, WINDUP_MOVE gains 15 Block and
/// Strength 5 (<c>MonsterMoveEffects</c> "MechaKnight"). The starting Artifact 3 is a root power.
/// </summary>
[FlashContent("MECHA_KNIGHT")]
public sealed class MechaKnight : FlashMonsterDef
{
    private const int FlamethrowerBurn = 4;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("BURN", 0)];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        switch (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "FLAMETHROWER_MOVE":
                for (int index = 0; index < FlamethrowerBurn; index++)
                {
                    FlashKernel.AddGeneratedCard(ref x,
                        FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Burn>()), FlashPile.Hand);
                }
                break;
            case "WINDUP_MOVE":
                FlashKernel.GainBlock(ref x, owner, 15, FlashValueProps.Move, fromCard: false);
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 5, owner);
                break;
        }
    }
}

/// <summary>
/// OwlMagistrate: JUDICIAL_FLIGHT applies SoarPower 1, VERDICT applies Vulnerable 4 and removes Soar
/// (<c>MonsterMoveEffects</c> "OwlMagistrate"). Soar's damage reduction has no existing-engine
/// mirror, so <see cref="Powers.SoarPower"/> stays inert.
/// </summary>
[FlashContent("OWL_MAGISTRATE")]
public sealed class OwlMagistrate : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "JUDICIAL_FLIGHT":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.SoarPower>(), 1, owner);
                break;
            case "VERDICT":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.VulnerablePower>(), 4, owner);
                FlashKernel.SetPowerAmount(ref x.B, owner, FlashRegistry.PowerId<Powers.SoarPower>(), 0);
                break;
        }
    }
}

/// <summary>
/// ScrollOfBiting: MORE_TEETH applies Strength 2 to itself; its starting PaperCutsPower 2 is a root
/// power (<c>MonsterMoveEffects</c> "ScrollOfBiting", native <c>AfterAddedToRoom</c>). The initial
/// move differs per instance (<c>StarterMoveIdx</c>) and is already the compiled initial node.
/// </summary>
[FlashContent("SCROLL_OF_BITING")]
public sealed class ScrollOfBiting : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "MORE_TEETH")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
    }
}

/// <summary>
/// SlimedBerserker: VOMIT_ICHOR_MOVE adds ten Slimed to the discard pile, LEECHING_HUG_MOVE applies
/// Weak 3 to the player (with the monster as applier in the existing engine) and Strength 3 to itself
/// (<c>MonsterMoveEffects</c> "SlimedBerserker").
/// </summary>
[FlashContent("SLIMED_BERSERKER")]
public sealed class SlimedBerserker : FlashMonsterDef
{
    private const int VomitSlime = 10;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SLIMED", 0)];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        switch (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "VOMIT_ICHOR_MOVE":
                for (int index = 0; index < VomitSlime; index++)
                {
                    FlashKernel.AddGeneratedCard(ref x,
                        FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Slimed>()), FlashPile.Discard);
                }
                break;
            case "LEECHING_HUG_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.WeakPower>(), 3, owner);
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 3, owner);
                break;
        }
    }
}

/// <summary>
/// SoulNexus: DRAIN_LIFE_MOVE applies Vulnerable 2 and Weak 2 to the player
/// (<c>MonsterMoveEffects</c> "SoulNexus"); the other moves are plain attacks.
/// </summary>
[FlashContent("SOUL_NEXUS")]
public sealed class SoulNexus : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId != "DRAIN_LIFE_MOVE")
            return;
        FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.VulnerablePower>(), 2, owner);
        FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.WeakPower>(), 2, owner);
    }
}

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
/// Queen: PUPPET_STRINGS applies ChainsOfBindingPower 3, YOU_ARE_MINE applies Frail/Weak/Vulnerable
/// 99, BURN_BRIGHT_FOR_ME applies Strength 1 to every other enemy then gains 20 Block, ENRAGE applies
/// Strength 2 (<c>MonsterMoveEffects</c> "Queen"). YOURE_MINE_NOW_BRANCH and
/// BURN_BRIGHT_FOR_ME_BRANCH pick OFF_WITH_YOUR_HEAD once the Torch Head Amalgam died and
/// BURN_BRIGHT_FOR_ME otherwise. The amalgam's death sets <c>_hasAmalgamDied</c> and, while the
/// Queen's current move is BURN_BRIGHT_FOR_ME, forces ENRAGE_MOVE
/// (<c>AfterDeathMirrors.HandleQueen</c>, <c>BranchMonsterAi.ResolveConditional</c>).
/// </summary>
[FlashContent("QUEEN")]
public sealed class Queen : FlashMonsterDef
{
    private const int HasAmalgamDied = 0;

    public override IReadOnlyList<string> AiRegisterNames => ["_hasAmalgamDied"];

    public override int ResolveConditional(ref FlashExec x, byte owner, int branchNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        string native = rules.Nodes[branchNode].NativeId;
        if (native is not ("YOURE_MINE_NOW_BRANCH" or "BURN_BRIGHT_FOR_ME_BRANCH"))
            return -1;
        return x.B.Enemy(owner).Ai[HasAmalgamDied] != 0
            ? rules.NodeIndex("OFF_WITH_YOUR_HEAD_MOVE")
            : rules.NodeIndex("BURN_BRIGHT_FOR_ME_MOVE");
    }

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "PUPPET_STRINGS_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.ChainsOfBindingPower>(), 3, owner);
                break;
            case "YOU_ARE_MINE_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.FrailPower>(), 99, owner);
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.WeakPower>(), 99, owner);
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.VulnerablePower>(), 99, owner);
                break;
            case "BURN_BRIGHT_FOR_ME_MOVE":
                for (int position = 0; position < x.B.RosterCount; position++)
                {
                    byte other = FlashKernel.EnemyOwner(x.B.Roster[position]);
                    if (other != owner)
                        FlashKernel.ApplyPower(ref x, other, FlashRegistry.PowerId<Powers.StrengthPower>(), 1, owner);
                }
                FlashKernel.GainBlock(ref x, owner, 20, FlashValueProps.Move, fromCard: false);
                break;
            case "ENRAGE_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
                break;
        }
    }

    public override void AfterDeath(ref FlashExec x, FlashListener self, byte creature, bool wasRemovalPrevented)
    {
        if (creature < FlashLimits.FirstEnemyOwner
            || creature == self.Owner
            || x.B.Enemy(creature).MonsterId != FlashRegistry.MonsterId<TorchHeadAmalgam>()
            || !x.B.Creature(self.Owner).IsAlive)
        {
            return;
        }
        x.B.Enemy(self.Owner).Ai[HasAmalgamDied] = 1;
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, self.Owner);
        if (rules.Nodes[x.B.Enemy(self.Owner).Move].NativeId == "BURN_BRIGHT_FOR_ME_MOVE")
        {
            FlashKernel.ForceMonsterMove(ref x, self.Owner, rules.NodeIndex("ENRAGE_MOVE"));
        }
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
                        FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Wither>()), FlashPile.Discard);
                }
                int strength = rules.StaticInts[IncreasingIntensityBaseStrengthStat]
                    + x.B.Enemy(owner).Ai[AdditionalStrengthRegister];
                x.B.Enemy(owner).Ai[AdditionalStrengthRegister]++;
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), strength, owner);
                break;
        }
    }

    public override void AfterCardGeneratedForCombat(ref FlashExec x, FlashListener self, int position)
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
/// MagiKnight: DAMPEN_MOVE runs <c>SimulatedCombatState.ApplyDampen</c> — one DampenPower per caster
/// (tracked as a bitmask; the power is applied only for the first caster) downgrades every upgraded
/// combat card once. POWER_SHIELD_MOVE and PREP_MOVE gain <c>PowerShieldBlock</c>; the rest are plain
/// attacks (<c>MonsterMoveEffects</c> "MagiKnight"). When a caster dies, its bit is cleared and the
/// last death removes DampenPower and restores every downgraded card
/// (<c>RemoveDampenCaster</c>, <c>AfterDeathMirrors.HandleDampen</c>).
/// </summary>
[FlashContent("MAGI_KNIGHT")]
public sealed class MagiKnight : FlashMonsterDef
{
    private const int PowerShieldBlockStat = 0;
    private const int LastKnownUpgradeLevel = 2;

    public override IReadOnlyList<string> StaticIntNames => ["PowerShieldBlock"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "POWER_SHIELD_MOVE":
            case "PREP_MOVE":
                FlashKernel.GainBlock(ref x, owner, rules.StaticInts[PowerShieldBlockStat],
                    FlashValueProps.Move, fromCard: false);
                break;
            case "DAMPEN_MOVE":
                ApplyDampen(ref x, owner);
                break;
        }
    }

    private static void ApplyDampen(ref FlashExec x, byte owner)
    {
        int bit = 1 << (owner - FlashLimits.FirstEnemyOwner);
        int mask = x.B.Counter(FlashCounter.DampenCasterMask);
        if ((mask & bit) != 0)
            return;
        x.B.SetCounter(FlashCounter.DampenCasterMask, mask | bit);
        if (mask != 0)
            return;
        if (FlashRegistry.TryPower("DAMPEN_POWER", out ushort dampen))
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, dampen, 1, owner);
        for (int position = 0; position < x.B.CardCount; position++)
        {
            if (x.B.Cards[position].Upgrade != 0)
                FlashKernel.DowngradeCard(ref x, position);
        }
    }

    public override void AfterDeath(ref FlashExec x, FlashListener self, byte creature, bool wasRemovalPrevented)
    {
        if (creature != self.Owner || wasRemovalPrevented)
            return;
        int bit = 1 << (self.Owner - FlashLimits.FirstEnemyOwner);
        int mask = x.B.Counter(FlashCounter.DampenCasterMask);
        if ((mask & bit) == 0)
            return;
        mask &= ~bit;
        x.B.SetCounter(FlashCounter.DampenCasterMask, mask);
        if (mask != 0)
            return;
        if (FlashRegistry.TryPower("DAMPEN_POWER", out ushort dampen))
            FlashKernel.SetPowerAmount(ref x.B, FlashLimits.PlayerOwner, dampen, 0);
        FlashKernel.RestoreDampenedCards(ref x);
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
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, dazed), FlashPile.Discard);
        if (x.NeedsChoice)
            return;
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, dazed), FlashPile.Draw,
            FlashPilePosition.Random);
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
/// TheLost: DEBILITATING_SMOG takes <c>DebilitatingSmogStrengthStealAmount</c> Strength from the
/// player and gives it to itself; EYE_LASERS is a plain attack. Its starting PossessStrengthPower 1
/// is a root power (<c>MonsterMoveEffects</c> "TheLost").
/// </summary>
[FlashContent("THE_LOST")]
public sealed class TheLost : FlashMonsterDef
{
    private const int StrengthStealStat = 0;

    public override IReadOnlyList<string> StaticIntNames => ["DebilitatingSmogStrengthStealAmount"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "DEBILITATING_SMOG")
            return;
        int stolen = rules.StaticInts[StrengthStealStat];
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            -stolen, owner);
        FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), stolen, owner);
    }
}

/// <summary>
/// TheForgotten: MIASMA takes <c>DebilitatingSmogDexStealAmount</c> Dexterity from the player, gains
/// 8 Block and gives the Dexterity to itself; DREAD's damage is its base plus the owner's current
/// Dexterity minus its root value (<c>MonsterMoveEffects</c>, <c>AdjustMonsterMoveDamage</c>). Its
/// starting PossessSpeedPower 1 is a root power.
/// </summary>
[FlashContent("THE_FORGOTTEN")]
public sealed class TheForgotten : FlashMonsterDef
{
    private const int DexterityStealStat = 0;

    public override IReadOnlyList<string> StaticIntNames => ["DebilitatingSmogDexStealAmount"];

    public override IReadOnlyList<string> RootPowerAmountNames => ["DEXTERITY_POWER"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "MIASMA")
            return;
        int stolen = rules.StaticInts[DexterityStealStat];
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DexterityPower>(),
            -stolen, owner);
        FlashKernel.GainBlock(ref x, owner, 8, FlashValueProps.Move, fromCard: false);
        FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.DexterityPower>(), stolen, owner);
    }

    public override int ModifyAttackDamage(in FlashBattle battle, FlashRules rules, byte owner, int moveNode,
        int attackIndex, int damage)
    {
        FlashMonsterRules machine = rules.MonsterRule(battle.Enemy(owner).Rules);
        if (machine.Nodes[moveNode].NativeId != "DREAD")
            return damage;
        int current = FlashKernel.GetAmount(battle, owner, FlashRegistry.PowerId<Powers.DexterityPower>());
        return damage + current - machine.RootPowerAmounts[0];
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
        // The compiled spawn rule already has RespawnCount 1 (StockAmount 1); each further missing
        // stock adds another 10 maximum HP (native RespawnMaxHpBonus = RespawnCount * 10).
        int bonus = (1 - newStock) * StockPerRespawnHp;
        if (bonus > 0)
        {
            ref FlashCreature creature = ref x.B.Creature(owner);
            creature.MaxHp += bonus;
            creature.CurrentHp += bonus;
        }
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
        FlashKernel.SpawnMonster(ref x, self.Owner, rulesId, x.B.Enemy(self.Owner).Slot, minion: false);
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
                        FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Burn>()), FlashPile.Discard);
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
