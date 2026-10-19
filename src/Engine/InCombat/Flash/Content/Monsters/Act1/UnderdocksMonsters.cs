namespace CombatSolver.Engine.InCombat.Flash.Content.Monsters.Act1;

// First-act (Underdocks) monsters. Attacks, hit counts and branch weights come from the state machine
// compiled by the bridge; ApplyMoveEffects mirrors the post-attack part of
// MonsterMoveEffects.Apply for the same (monster, move) pair. Debuff helper means
// combat.ApplyFromMonster; combat.Apply is mirrored with ApplyPower.

/// <summary>CalcifiedCultist: "INCANTATION_MOVE" applies Ritual <c>IncantationAmount</c> to itself.</summary>
[FlashContent("CALCIFIED_CULTIST")]
public sealed class CalcifiedCultist : FlashMonsterDef
{
    private const int IncantationAmount = 0;

    public override IReadOnlyList<string> StaticIntNames => ["IncantationAmount"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId == "INCANTATION_MOVE")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.RitualPower>(),
                rules.StaticInts[IncantationAmount], owner);
    }
}

/// <summary>DampCultist: "INCANTATION_MOVE" applies Ritual <c>IncantationAmount</c> to itself.</summary>
[FlashContent("DAMP_CULTIST")]
public sealed class DampCultist : FlashMonsterDef
{
    private const int IncantationAmount = 0;

    public override IReadOnlyList<string> StaticIntNames => ["IncantationAmount"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId == "INCANTATION_MOVE")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.RitualPower>(),
                rules.StaticInts[IncantationAmount], owner);
    }
}

/// <summary>
/// HauntedShip: "HAUNT_MOVE" applies Weak 3 to the player (<c>Debuff</c>) and adds five Dazed to the
/// discard pile (MonsterMoveEffects "HauntedShip", "HAUNT_MOVE", literals in the existing engine).
/// Its other two moves are pure attacks.
/// </summary>
[FlashContent("HAUNTED_SHIP")]
public sealed class HauntedShip : FlashMonsterDef
{
    private const int HauntWeak = 3;
    private const int HauntDazed = 5;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DAZED", 0)];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId != "HAUNT_MOVE")
            return;
        FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.WeakPower>(), HauntWeak, owner);
        for (int index = 0; index < HauntDazed; index++)
            FlashKernel.AddGeneratedCard(ref x,
                FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Dazed>()), FlashPile.Discard, creator: FlashLimits.NoOwner);
    }
}

/// <summary>
/// PunchConstruct: "READY_MOVE" gains 10 move block and "FAST_PUNCH_MOVE" applies Frail 1 to the
/// player. The values are literals in MonsterMoveEffects.Apply ("PunchConstruct", "READY_MOVE" /
/// "FAST_PUNCH_MOVE"), not static model values. Its starting Artifact is imported, not replayed here.
/// </summary>
[FlashContent("PUNCH_CONSTRUCT")]
public sealed class PunchConstruct : FlashMonsterDef
{
    private const int ReadyBlock = 10;
    private const int FastPunchFrail = 1;

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        switch (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "READY_MOVE":
                FlashKernel.GainBlock(ref x, owner, ReadyBlock, FlashValueProps.Move, fromCard: false);
                break;
            case "FAST_PUNCH_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.FrailPower>(), FastPunchFrail, owner);
                break;
        }
    }
}

/// <summary>
/// Seapunk: "BUBBLE_BURP_MOVE" gains <c>BubbleBlock</c> move block and applies <c>BubbleStr</c>
/// Strength to itself (both are static model values captured for MonsterMoveEffects).
/// </summary>
[FlashContent("SEAPUNK")]
public sealed class Seapunk : FlashMonsterDef
{
    private const int BubbleBlock = 0;
    private const int BubbleStr = 1;

    public override IReadOnlyList<string> StaticIntNames => ["BubbleBlock", "BubbleStr"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "BUBBLE_BURP_MOVE")
            return;
        FlashKernel.GainBlock(ref x, owner, rules.StaticInts[BubbleBlock], FlashValueProps.Move, fromCard: false);
        FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            rules.StaticInts[BubbleStr], owner);
    }
}

/// <summary>
/// SewerClam: "PRESSURIZE_MOVE" applies Strength 4 to itself (literal in MonsterMoveEffects.Apply,
/// "SewerClam", "PRESSURIZE_MOVE"). Its starting Plating is imported, not replayed here.
/// </summary>
[FlashContent("SEWER_CLAM")]
public sealed class SewerClam : FlashMonsterDef
{
    private const int PressurizeStrength = 4;

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "PRESSURIZE_MOVE")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(),
                PressurizeStrength, owner);
    }
}

/// <summary>SludgeSpinner: "OIL_SPRAY_MOVE" applies Weak 1 to the player; "RAGE_MOVE" applies Strength 3 to itself.</summary>
[FlashContent("SLUDGE_SPINNER")]
public sealed class SludgeSpinner : FlashMonsterDef
{
    private const int OilSprayWeak = 1;
    private const int RageStrength = 3;

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        switch (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "OIL_SPRAY_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.WeakPower>(), OilSprayWeak, owner);
                break;
            case "RAGE_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(),
                    RageStrength, owner);
                break;
        }
    }
}

/// <summary>
/// Toadpole: "SPIKEN_MOVE" applies Thorns 2 to itself and "SPIKE_SPIT_MOVE" removes 2 Thorns
/// (MonsterMoveEffects "Toadpole", literals <c>SpikenAmount = 2</c> in the decompiled model).
/// </summary>
[FlashContent("TOADPOLE")]
public sealed class Toadpole : FlashMonsterDef
{
    private const int SpikenAmount = 2;

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        switch (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "SPIKEN_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.ThornsPower>(),
                    SpikenAmount, owner);
                break;
            case "SPIKE_SPIT_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.ThornsPower>(),
                    -SpikenAmount, owner);
                break;
        }
    }
}

/// <summary>
/// TwoTailedRat: the only first-act summoner whose private counters drive branch selection.
/// SCRATCH_MOVE, DISEASE_BITE_MOVE and SCREECH_MOVE decrement <c>_turnsUntilSummonable</c>;
/// CALL_FOR_BACKUP_MOVE spawns another rat in <c>LastFreeSlot</c> and raises every roster rat's
/// <c>_callForBackupCount</c> to (max + 1) (<c>MonsterMoveEffects</c> "TwoTailedRat"). The branch
/// weights are the literals in <c>BranchMonsterAi.GetBranchWeight</c> (0.75 for CALL_FOR_BACKUP,
/// 1/12 for the other three while the rat can summon, 1 otherwise) and its
/// <c>CanTwoTailedRatSummon</c> checks the turn counter, the 3-use cap, the next free slot and the
/// other rats' current intent. Registers: 0 = _turnsUntilSummonable, 1 = _callForBackupCount.
/// </summary>
[FlashContent("TWO_TAILED_RAT")]
public sealed class TwoTailedRat : FlashMonsterDef
{
    private const int TurnsUntilSummonable = 0;
    private const int CallForBackupCount = 1;
    private const float CallForBackupWeight = 0.75f;
    private const float OtherBranchWeightWhenSummonable = 1f / 12f;
    private const int CallForBackupLimit = 3;

    public override IReadOnlyList<string> SpawnedMonsterIds => ["TWO_TAILED_RAT"];

    public override IReadOnlyList<string> AiRegisterNames => ["_turnsUntilSummonable", "_callForBackupCount"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        string id = rules.Nodes[moveNode].NativeId;
        if (id is "SCRATCH_MOVE" or "DISEASE_BITE_MOVE" or "SCREECH_MOVE")
        {
            ref FlashEnemy enemy = ref x.B.Enemy(owner);
            enemy.Ai[TurnsUntilSummonable] = enemy.Ai[TurnsUntilSummonable] - 1;
        }
        switch (id)
        {
            case "SCREECH_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.FrailPower>(), 1, owner);
                break;
            case "CALL_FOR_BACKUP_MOVE":
            {
                string? slot = FlashKernel.LastFreeSlot(x.B, x.R);
                if (!string.IsNullOrEmpty(slot))
                {
                    FlashKernel.SpawnMonster(ref x, owner,
                        x.R.SpawnRulesId(FlashRegistry.MonsterId<TwoTailedRat>()),
                        FlashKernel.SlotIndex(x.R, slot), minion: false);
                }
                ushort ratId = FlashRegistry.MonsterId<TwoTailedRat>();
                int next = int.MinValue;
                for (int position = 0; position < x.B.RosterCount; position++)
                {
                    byte rat = FlashKernel.EnemyOwner(x.B.Roster[position]);
                    if (x.R.MonsterRule(x.B.Enemy(rat).Rules).MonsterId == ratId)
                        next = Math.Max(next, x.B.Enemy(rat).Ai[CallForBackupCount] + 1);
                }
                if (next == int.MinValue)
                    throw new FlashUnsupportedException("召唤备用时场上没有双尾鼠。");
                for (int position = 0; position < x.B.RosterCount; position++)
                {
                    byte rat = FlashKernel.EnemyOwner(x.B.Roster[position]);
                    if (x.R.MonsterRule(x.B.Enemy(rat).Rules).MonsterId == ratId)
                        x.B.Enemy(rat).Ai[CallForBackupCount] = next;
                }
                break;
            }
        }
    }

    public override float BranchWeight(ref FlashExec x, byte owner, int branchNode, int option, float baseWeight)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "RAND")
            return baseWeight;
        bool canSummon = CanSummon(ref x, owner);
        string optionId = rules.Nodes[rules.Nodes[branchNode].Options[option].Target].NativeId;
        return optionId switch
        {
            "CALL_FOR_BACKUP_MOVE" => canSummon ? CallForBackupWeight : 0f,
            "SCREECH_MOVE" or "SCRATCH_MOVE" or "DISEASE_BITE_MOVE"
                => canSummon ? OtherBranchWeightWhenSummonable : 1f,
            _ => baseWeight,
        };
    }

    /// <summary>Mirrors <c>BranchMonsterAi.CanTwoTailedRatSummon</c>.</summary>
    private static bool CanSummon(ref FlashExec x, byte owner)
    {
        ref FlashEnemy enemy = ref x.B.Enemy(owner);
        if (enemy.Ai[TurnsUntilSummonable] > 0 || enemy.Ai[CallForBackupCount] >= CallForBackupLimit)
            return false;
        if (string.IsNullOrEmpty(FlashKernel.NextFreeSlot(x.B, x.R)))
            return false;
        ushort ratId = FlashRegistry.MonsterId<TwoTailedRat>();
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte other = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (other == owner)
                continue;
            ref FlashEnemy candidate = ref x.B.Enemy(other);
            if (x.R.MonsterRule(candidate.Rules).MonsterId == ratId
                && x.R.MonsterRule(candidate.Rules).Nodes[candidate.Move].NativeId == "CALL_FOR_BACKUP_MOVE")
                return false;
        }
        return true;
    }
}
