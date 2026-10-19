namespace CombatSolver.Engine.InCombat.Flash.Content.Monsters.Act2;

// Second-act (Hive) monsters. Attacks, hit counts and branch weights come from the state machine
// compiled by the bridge; ApplyMoveEffects mirrors the post-attack part of
// MonsterMoveEffects.Apply for the same (monster, move) pair. The helper comment on each type names
// the existing-engine registration it copies. ApplyPowerFromMonster = combat.ApplyFromMonster;
// ApplyPower = combat.Apply.

/// <summary>Chomper: "SCREECH_MOVE" adds three Dazed to the discard pile; its Artifact is a starting power.</summary>
[FlashContent("CHOMPER")]
public sealed class Chomper : FlashMonsterDef
{
    private const int ScreechDazed = 3;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DAZED", 0)];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId != "SCREECH_MOVE")
            return;
        for (int index = 0; index < ScreechDazed; index++)
            FlashKernel.AddGeneratedCard(ref x,
                FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Dazed>()), FlashPile.Discard, creator: FlashLimits.NoOwner);
    }
}

/// <summary>
/// Entomancer: "PHEROMONE_SPIT_MOVE" adds one PersonalHive stack and one Strength while the hive is
/// below three, otherwise only Strength 2 (MonsterMoveEffects "Entomancer"). Its starting
/// PersonalHivePower is imported with the root.
/// </summary>
[FlashContent("ENTOMANCER")]
public sealed class Entomancer : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId != "PHEROMONE_SPIT_MOVE")
            return;
        ushort hive = FlashRegistry.PowerId<Powers.PersonalHivePower>();
        if (FlashKernel.GetAmount(x.B, owner, hive) >= 3)
        {
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
            return;
        }
        FlashKernel.ApplyPower(ref x, owner, hive, 1, owner);
        FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 1, owner);
    }
}

/// <summary>Exoskeleton: "ENRAGE_MOVE" applies Strength 2 to itself; HardToKill is a starting power.</summary>
[FlashContent("EXOSKELETON")]
public sealed class Exoskeleton : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "ENRAGE_MOVE")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
    }
}

/// <summary>Myte: "TOXIC_MOVE" adds two Toxic to the hand; "SUCK_MOVE" applies <c>SuckStrength</c> Strength.</summary>
[FlashContent("MYTE")]
public sealed class Myte : FlashMonsterDef
{
    private const int SuckStrength = 0;

    public override IReadOnlyList<string> StaticIntNames => ["SuckStrength"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("TOXIC", 0)];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "TOXIC_MOVE":
                for (int index = 0; index < 2; index++)
                {
                    FlashKernel.AddGeneratedCard(ref x,
                        FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Toxic>()), FlashPile.Hand, creator: FlashLimits.NoOwner);
                }
                break;
            case "SUCK_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(),
                    rules.StaticInts[SuckStrength], owner);
                break;
        }
    }
}

/// <summary>
/// Ovicopter: "LAY_EGGS_MOVE" summons up to three ToughEgg into the last free encounter slots as
/// minions (MonsterMoveEffects "Ovicopter"); "TENDERIZER_MOVE" applies Vulnerable 2;
/// "NUTRITIONAL_PASTE_MOVE" applies <c>NutritionalPasteStrengthAmount</c> Strength. The
/// SUMMON_BRANCH_STATE conditional lays eggs only while at most three enemies are alive
/// (BranchMonsterAi.ResolveConditional).
/// </summary>
[FlashContent("OVICOPTER")]
public sealed class Ovicopter : FlashMonsterDef
{
    private const int NutritionalPasteStrengthAmount = 0;

    public override IReadOnlyList<string> StaticIntNames => ["NutritionalPasteStrengthAmount"];

    public override IReadOnlyList<string> SpawnedMonsterIds => ["TOUGH_EGG"];

    public override int ResolveConditional(ref FlashExec x, byte owner, int branchNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "SUMMON_BRANCH_STATE")
            return -1;
        int alive = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            if (x.B.Creature(FlashKernel.EnemyOwner(x.B.Roster[position])).IsAlive)
                alive++;
        }
        return rules.NodeIndex(alive <= 3 ? "LAY_EGGS_MOVE" : "NUTRITIONAL_PASTE_MOVE");
    }

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "LAY_EGGS_MOVE":
                for (int index = 0; index < 3; index++)
                {
                    string? slot = FlashKernel.LastFreeSlot(x.B, x.R);
                    if (slot == null)
                        break;
                    FlashKernel.SpawnMonster(ref x, owner,
                        x.R.SpawnRulesId(FlashRegistry.MonsterId<ToughEgg>()),
                        FlashKernel.SlotIndex(x.R, slot), minion: true);
                }
                break;
            case "TENDERIZER_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.VulnerablePower>(), 2, owner);
                break;
            case "NUTRITIONAL_PASTE_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(),
                    rules.StaticInts[NutritionalPasteStrengthAmount], owner);
                break;
        }
    }
}

/// <summary>
/// ToughEgg: summoned by Ovicopter. Entrance applies HatchPower 2 during the enemy turn and 1
/// otherwise (MonsterSpawnSupport.ApplyNativeEntrancePowers). "HATCH_MOVE" removes every power except
/// Minion, rolls its new HP from the Niche stream in [HatchlingMinHp, HatchlingMaxHp] and sets both
/// current and max HP (MonsterMoveEffects "ToughEgg"). The follow-up NIBBLE_MOVE is a plain attack.
/// </summary>
[FlashContent("TOUGH_EGG")]
public sealed class ToughEgg : FlashMonsterDef
{
    private const int HatchlingMinHp = 0;
    private const int HatchlingMaxHp = 1;

    public override IReadOnlyList<string> StaticIntNames => ["HatchlingMinHp", "HatchlingMaxHp"];

    // MonsterValueReader/fingerprint only: HATCH_MOVE clears itself; the native IsHatched gate in
    // AfterAddedToRoom is unreachable for a freshly summoned egg (it enters unhatched).
    public override IReadOnlyList<string> InertMonsterIntNames => ["_isHatched", "_hatched"];

    public override void ApplyEntrancePowers(ref FlashExec x, byte owner)
    {
        FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.HatchPower>(),
            x.B.Side == FlashSide.Enemy ? 2 : 1, owner);
    }

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "HATCH_MOVE")
            return;
        for (int index = x.B.PowerCount - 1; index >= 0; index--)
        {
            if (x.B.Powers[index].Owner != owner)
                continue;
            if (FlashRegistry.Power(x.B.Powers[index].Id).NativeId == "MINION_POWER")
                continue;
            FlashKernel.SetPowerAmount(ref x.B, owner, x.B.Powers[index].Id, 0);
        }
        int hp = x.B.RngStream(FlashRngStream.Niche).NextInt(
            rules.StaticInts[HatchlingMinHp], rules.StaticInts[HatchlingMaxHp] + 1);
        ref FlashCreature creature = ref x.B.Creature(owner);
        creature.MaxHp = hp;
        creature.CurrentHp = hp;
    }
}

/// <summary>
/// SpinyToad: "PROTRUDING_SPIKES_MOVE" applies Thorns 5; "SPIKE_EXPLOSION_MOVE" applies Thorns -5.
/// The private <c>_isSpiny</c> flag only selects animations.
/// </summary>
[FlashContent("SPINY_TOAD")]
public sealed class SpinyToad : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        switch (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "PROTRUDING_SPIKES_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.ThornsPower>(), 5, owner);
                break;
            case "SPIKE_EXPLOSION_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.ThornsPower>(), -5, owner);
                break;
        }
    }
}

/// <summary>
/// TheObscura: "ILLUSION_MOVE" summons a Parafright into the encounter's "illusion" slot
/// (MonsterMoveEffects "TheObscura"); "SAIL_MOVE" applies Strength 3 to every living enemy including
/// itself; "HARDENING_STRIKE_MOVE" gains <c>HardeningStrikeBlock</c> move block. The private
/// <c>_hasSummoned</c> flag only selects animations.
/// </summary>
[FlashContent("THE_OBSCURA")]
public sealed class TheObscura : FlashMonsterDef
{
    private const int HardeningStrikeBlock = 0;

    public override IReadOnlyList<string> StaticIntNames => ["HardeningStrikeBlock"];

    // MonsterMoveEffects writes it; only the native animator reads it.
    public override IReadOnlyList<string> InertMonsterIntNames => ["_hasSummoned"];

    public override IReadOnlyList<string> SpawnedMonsterIds => ["PARAFRIGHT"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "ILLUSION_MOVE":
                FlashKernel.SpawnMonster(ref x, owner,
                    x.R.SpawnRulesId(FlashRegistry.MonsterId<Parafright>()),
                    FlashKernel.SlotIndex(x.R, "illusion"), minion: false);
                break;
            case "SAIL_MOVE":
                for (int position = 0; position < x.B.RosterCount; position++)
                {
                    byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
                    if (x.B.Creature(enemy).IsAlive)
                        FlashKernel.ApplyPower(ref x, enemy, FlashRegistry.PowerId<Powers.StrengthPower>(), 3, owner);
                }
                break;
            case "HARDENING_STRIKE_MOVE":
                FlashKernel.GainBlock(ref x, owner, rules.StaticInts[HardeningStrikeBlock],
                    FlashValueProps.Move, fromCard: false);
                break;
        }
    }
}

/// <summary>
/// Parafright: summoned illusion. <c>MonsterSpawnSupport.ApplyNativeEntrancePowers</c> gives it
/// IllusionPower 1 (applier itself) and MinionPower 1 (no applier); its only move SLAM_MOVE is a plain
/// attack. IllusionPower owns the revive, so the kernel appends the synthetic REVIVE_MOVE node.
/// </summary>
[FlashContent("PARAFRIGHT")]
public sealed class Parafright : FlashMonsterDef
{
    public override bool IllusionRevive => true;

    public override void ApplyEntrancePowers(ref FlashExec x, byte owner)
    {
        FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.IllusionPower>(), 1, owner);
        FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.MinionPower>(), 1, FlashLimits.NoOwner);
    }
}

/// <summary>
/// Tunneler: "BURROW_MOVE" applies BurrowedPower 1 and <c>BlockGain</c> move block
/// (MonsterMoveEffects "Tunneler"). BurrowedPower owns the retained block, the block-break stun and the
/// forced BITE_MOVE follow-up. The private <c>_isStunned</c> flag only selects animations.
/// </summary>
[FlashContent("TUNNELER")]
public sealed class Tunneler : FlashMonsterDef
{
    private const int BlockGain = 0;

    public override IReadOnlyList<string> StaticIntNames => ["BlockGain"];

    // TriggeredPowerSupport/MonsterMoveEffects write it; only the native animator reads it.
    public override IReadOnlyList<string> InertMonsterIntNames => ["_isStunned"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "BURROW_MOVE")
            return;
        FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.BurrowedPower>(), 1, owner);
        FlashKernel.GainBlock(ref x, owner, rules.StaticInts[BlockGain], FlashValueProps.Move, fromCard: false);
    }
}