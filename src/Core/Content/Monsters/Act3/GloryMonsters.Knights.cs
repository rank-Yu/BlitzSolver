using BlitzSolver.Engine;

namespace BlitzSolver.Content.Monsters.Act3;

/// <summary>
/// DevotedSculptor: "FORBIDDEN_INCANTATION_MOVE" applies RitualPower with the private
/// <c>_ritualGain</c> field (9), and SAVAGE_MOVE is a plain attack
/// (<c>MonsterMoveEffects</c> "DevotedSculptor").
/// </summary>
[BlitzContent("DEVOTED_SCULPTOR")]
public sealed class DevotedSculptor : BlitzMonsterDef
{
    private const int RitualGain = 0;

    public override IReadOnlyList<string> StaticIntNames => ["_ritualGain"];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "FORBIDDEN_INCANTATION_MOVE")
            return;
        BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.RitualPower>(),
            rules.StaticInts[RitualGain], BlitzLimits.NoOwner);
    }
}

/// <summary>
/// FlailKnight: WAR_CHANT applies Strength 3 to itself; FLAIL_MOVE and RAM_MOVE are plain attacks
/// (<c>MonsterMoveEffects</c> "FlailKnight").
/// </summary>
[BlitzContent("FLAIL_KNIGHT")]
public sealed class FlailKnight : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "WAR_CHANT")
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 3, owner);
    }
}

/// <summary>
/// SpectralKnight: "HEX" applies HexPower 2 to the player; SOUL_SLASH and SOUL_FLAME are plain attacks
/// (<c>MonsterMoveEffects</c> "SpectralKnight").
/// </summary>
[BlitzContent("SPECTRAL_KNIGHT")]
public sealed class SpectralKnight : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId != "HEX")
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.HexPower>(), 2, owner);
    }
}

/// <summary>
/// GlobeHead: SHOCKING_SLAP applies Frail 2 to the player, GALVANIC_BURST applies Strength 2 to
/// itself; THUNDER_STRIKE is a plain attack (<c>MonsterMoveEffects</c> "GlobeHead"). Its starting
/// GalvanicPower 6 (8 on DeadlyEnemies) comes from the root powers, and Galvanized is normalized by
/// the kernel.
/// </summary>
[BlitzContent("GLOBE_HEAD")]
public sealed class GlobeHead : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        switch (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "SHOCKING_SLAP":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.FrailPower>(), 2, owner);
                break;
            case "GALVANIC_BURST":
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
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
[BlitzContent("FROG_KNIGHT")]
public sealed class FrogKnight : BlitzMonsterDef
{
    private const int HasBeetleCharged = 0;

    public override IReadOnlyList<string> AiRegisterNames => ["_hasBeetleCharged"];

    public override int ResolveConditional(ref BlitzExec x, byte owner, int branchNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "HALF_HEALTH")
            return -1;
        ref readonly BlitzCreature creature = ref x.B.Creature(owner);
        bool charged = x.B.Enemy(owner).Ai[HasBeetleCharged] != 0;
        return charged || creature.CurrentHp >= creature.MaxHp / 2
            ? rules.NodeIndex("TONGUE_LASH")
            : rules.NodeIndex("BEETLE_CHARGE");
    }

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "FOR_THE_QUEEN":
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 5, owner);
                break;
            case "TONGUE_LASH":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.FrailPower>(), 2, owner);
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
[BlitzContent("MECHA_KNIGHT")]
public sealed class MechaKnight : BlitzMonsterDef
{
    private const int FlamethrowerBurn = 4;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("BURN", 0)];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        switch (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "FLAMETHROWER_MOVE":
                for (int index = 0; index < FlamethrowerBurn; index++)
                {
                    BlitzKernel.AddGeneratedCard(ref x,
                        BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Burn>()), BlitzPile.Hand, creator: BlitzLimits.NoOwner);
                }
                break;
            case "WINDUP_MOVE":
                BlitzKernel.GainBlock(ref x, owner, 15, BlitzValueProps.Move, fromCard: false);
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 5, owner);
                break;
        }
    }
}

/// <summary>
/// OwlMagistrate: JUDICIAL_FLIGHT applies SoarPower 1, VERDICT applies Vulnerable 4 and removes Soar
/// (<c>MonsterMoveEffects</c> "OwlMagistrate"). Soar's damage reduction has no existing-engine
/// mirror, so <see cref="Powers.SoarPower"/> stays inert.
/// </summary>
[BlitzContent("OWL_MAGISTRATE")]
public sealed class OwlMagistrate : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "JUDICIAL_FLIGHT":
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.SoarPower>(), 1, owner);
                break;
            case "VERDICT":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.VulnerablePower>(), 4, owner);
                BlitzKernel.SetPowerAmount(ref x.B, owner, BlitzRegistry.PowerId<Powers.SoarPower>(), 0);
                break;
        }
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
[BlitzContent("QUEEN")]
public sealed class Queen : BlitzMonsterDef
{
    private const int HasAmalgamDied = 0;

    public override IReadOnlyList<string> AiRegisterNames => ["_hasAmalgamDied"];

    public override int ResolveConditional(ref BlitzExec x, byte owner, int branchNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        string native = rules.Nodes[branchNode].NativeId;
        if (native is not ("YOURE_MINE_NOW_BRANCH" or "BURN_BRIGHT_FOR_ME_BRANCH"))
            return -1;
        return x.B.Enemy(owner).Ai[HasAmalgamDied] != 0
            ? rules.NodeIndex("OFF_WITH_YOUR_HEAD_MOVE")
            : rules.NodeIndex("BURN_BRIGHT_FOR_ME_MOVE");
    }

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "PUPPET_STRINGS_MOVE":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.ChainsOfBindingPower>(), 3, owner);
                break;
            case "YOU_ARE_MINE_MOVE":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.FrailPower>(), 99, owner);
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.WeakPower>(), 99, owner);
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.VulnerablePower>(), 99, owner);
                break;
            case "BURN_BRIGHT_FOR_ME_MOVE":
                for (int position = 0; position < x.B.RosterCount; position++)
                {
                    byte other = BlitzKernel.EnemyOwner(x.B.Roster[position]);
                    if (other != owner)
                        BlitzKernel.ApplyPower(ref x, other, BlitzRegistry.PowerId<Powers.StrengthPower>(), 1, owner);
                }
                BlitzKernel.GainBlock(ref x, owner, 20, BlitzValueProps.Move, fromCard: false);
                break;
            case "ENRAGE_MOVE":
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
                break;
        }
    }

    public override void AfterDeath(ref BlitzExec x, BlitzListener self, byte creature, bool wasRemovalPrevented)
    {
        if (creature < BlitzLimits.FirstEnemyOwner
            || creature == self.Owner
            || x.B.Enemy(creature).MonsterId != BlitzRegistry.MonsterId<TorchHeadAmalgam>()
            || !x.B.Creature(self.Owner).IsAlive)
        {
            return;
        }
        x.B.Enemy(self.Owner).Ai[HasAmalgamDied] = 1;
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, self.Owner);
        if (rules.Nodes[x.B.Enemy(self.Owner).Move].NativeId == "BURN_BRIGHT_FOR_ME_MOVE")
        {
            BlitzKernel.ForceMonsterMove(ref x, self.Owner, rules.NodeIndex("ENRAGE_MOVE"));
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
[BlitzContent("MAGI_KNIGHT")]
public sealed class MagiKnight : BlitzMonsterDef
{
    private const int PowerShieldBlockStat = 0;
    private const int LastKnownUpgradeLevel = 2;

    public override IReadOnlyList<string> StaticIntNames => ["PowerShieldBlock"];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "POWER_SHIELD_MOVE":
            case "PREP_MOVE":
                BlitzKernel.GainBlock(ref x, owner, rules.StaticInts[PowerShieldBlockStat],
                    BlitzValueProps.Move, fromCard: false);
                break;
            case "DAMPEN_MOVE":
                ApplyDampen(ref x, owner);
                break;
        }
    }

    private static void ApplyDampen(ref BlitzExec x, byte owner)
    {
        int bit = 1 << (owner - BlitzLimits.FirstEnemyOwner);
        int mask = x.B.Counter(BlitzCounter.DampenCasterMask);
        if ((mask & bit) != 0)
            return;
        x.B.SetCounter(BlitzCounter.DampenCasterMask, mask | bit);
        if (mask != 0)
            return;
        if (BlitzRegistry.TryPower("DAMPEN_POWER", out ushort dampen))
            BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, dampen, 1, owner);
        for (int position = 0; position < x.B.CardCount; position++)
        {
            if (x.B.Cards[position].Upgrade != 0)
                BlitzKernel.DowngradeCard(ref x, position);
        }
    }

    public override void AfterDeath(ref BlitzExec x, BlitzListener self, byte creature, bool wasRemovalPrevented)
    {
        if (creature != self.Owner || wasRemovalPrevented)
            return;
        int bit = 1 << (self.Owner - BlitzLimits.FirstEnemyOwner);
        int mask = x.B.Counter(BlitzCounter.DampenCasterMask);
        if ((mask & bit) == 0)
            return;
        mask &= ~bit;
        x.B.SetCounter(BlitzCounter.DampenCasterMask, mask);
        if (mask != 0)
            return;
        if (BlitzRegistry.TryPower("DAMPEN_POWER", out ushort dampen))
            BlitzKernel.SetPowerAmount(ref x.B, BlitzLimits.PlayerOwner, dampen, 0);
        BlitzKernel.RestoreDampenedCards(ref x);
    }
}
