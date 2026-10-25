using BlitzSolver.Engine;

namespace BlitzSolver.Content.Monsters.Act2;

/// <summary>
/// BowlbugRock: "HEADBUTT_MOVE" is a plain attack; "DIZZY_MOVE" is the native stun animation state.
/// The existing engine ignores the private <c>_isOffBalance</c> flag: <c>BranchMonsterAi</c> resolves
/// POST_HEADBUTT to HEADBUTT_MOVE unconditionally, and a fully blocked HEADBUTT with ImbalancedPower
/// forces the synthetic STUNNED with HEADBUTT_MOVE as its follow-up
/// (<c>MonsterMoveSemantics.ApplyForecastMove</c>).
/// </summary>
[BlitzContent("BOWLBUG_ROCK")]
public sealed class BowlbugRock : BlitzMonsterDef
{
    public override int ResolveConditional(ref BlitzExec x, byte owner, int branchNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "POST_HEADBUTT")
            return -1;
        return rules.NodeIndex("HEADBUTT_MOVE");
    }

    public override int ImbalancedStunFollowUp(in BlitzMonsterRules rules) => rules.NodeIndex("HEADBUTT_MOVE");
}

/// <summary>
/// BowlbugEgg: "BITE_MOVE" is an attack followed by <c>ProtectBlock</c> unpowered-block-value block
/// (<c>MonsterMoveEffects</c> "BowlbugEgg", "BITE_MOVE").
/// </summary>
[BlitzContent("BOWLBUG_EGG")]
public sealed class BowlbugEgg : BlitzMonsterDef
{
    private const int ProtectBlock = 0;

    public override IReadOnlyList<string> StaticIntNames => ["ProtectBlock"];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId == "BITE_MOVE")
        {
            BlitzKernel.GainBlock(ref x, owner, rules.StaticInts[ProtectBlock], BlitzValueProps.Move, fromCard: false);
        }
    }
}

/// <summary>
/// BowlbugSilk: "TOXIC_SPIT_MOVE" applies Weak 1 to the player; "THRASH_MOVE" is a two-hit attack
/// (<c>MonsterMoveEffects</c> "BowlbugSilk", "TOXIC_SPIT_MOVE").
/// </summary>
[BlitzContent("BOWLBUG_SILK")]
public sealed class BowlbugSilk : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId == "TOXIC_SPIT_MOVE")
        {
            BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                BlitzRegistry.PowerId<Powers.WeakPower>(), 1, owner);
        }
    }
}

/// <summary>
/// BowlbugNectar: "BUFF_MOVE" applies <c>BuffStrengthGain</c> Strength to itself; THRASH_MOVE and
/// THRASH2_MOVE are plain attacks (<c>MonsterMoveEffects</c> "BowlbugNectar", "BUFF_MOVE").
/// </summary>
[BlitzContent("BOWLBUG_NECTAR")]
public sealed class BowlbugNectar : BlitzMonsterDef
{
    private const int BuffStrengthGain = 0;

    public override IReadOnlyList<string> StaticIntNames => ["BuffStrengthGain"];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "BUFF_MOVE")
            return;
        BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
            rules.StaticInts[BuffStrengthGain], owner);
    }
}

/// <summary>
/// SlumberingBeetle: SNORE_NEXT picks SNORE_MOVE while SlumberPower is positive and ROLL_OUT_MOVE
/// otherwise (<c>BranchMonsterAi.ResolveConditional</c>); "ROLL_OUT_MOVE" applies Strength 2
/// (<c>MonsterMoveEffects</c>); a STUNNED move removes Plating (the same effect as
/// LagavulinMatriarch's STUNNED case. <c>_isAwake</c> is only read by the fingerprint/continuation
/// text).
/// </summary>
[BlitzContent("SLUMBERING_BEETLE")]
public sealed class SlumberingBeetle : BlitzMonsterDef
{
    public override IReadOnlyList<string> InertMonsterIntNames => ["_isAwake"];

    public override int ResolveConditional(ref BlitzExec x, byte owner, int branchNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "SNORE_NEXT")
            return -1;
        return BlitzKernel.GetAmount(x.B, owner, BlitzRegistry.PowerId<Powers.SlumberPower>()) > 0
            ? rules.NodeIndex("SNORE_MOVE")
            : rules.NodeIndex("ROLL_OUT_MOVE");
    }

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "ROLL_OUT_MOVE":
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
                break;
            case "STUNNED":
                BlitzKernel.SetPowerAmount(ref x.B, owner, BlitzRegistry.PowerId<Powers.PlatingPower>(), 0);
                break;
        }
    }
}

/// <summary>
/// Decimillipede segment shared logic. "BULK_MOVE" applies Strength 2, "CONSTRICT_MOVE" applies Weak
/// 1 to the player, "DEAD_MOVE" does nothing, and REATTACH_MOVE runs
/// <c>SimulatedCombatState.ResolveReviveMove</c>'s Decimillipede case: while another segment can
/// still act, heal the ReattachPower amount and clear the reviving phase. The three concrete ids
/// differ only in visuals.
/// </summary>
public abstract class DecimillipedeSegmentDef : BlitzMonsterDef
{
    public override void ResolveReviveMove(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "REATTACH_MOVE")
            return;
        ushort reattach = BlitzRegistry.PowerId<Powers.ReattachPower>();
        bool allOthersDead = true;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte other = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (other == owner || BlitzKernel.GetAmount(x.B, other, reattach) <= 0)
                continue;
            if (BlitzKernel.CanPerformMonsterMove(x.B.Enemy(other)))
            {
                allOthersDead = false;
                break;
            }
        }
        if (allOthersDead)
        {
            return;
        }
        BlitzKernel.Heal(ref x, owner, BlitzKernel.GetAmount(x.B, owner, reattach));
        if (x.NeedsChoice)
            return;
        x.B.Enemy(owner).DeathPhase = BlitzDeathPhase.None;
    }

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "BULK_MOVE":
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
                break;
            case "CONSTRICT_MOVE":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.WeakPower>(), 1, owner);
                break;
        }
    }
}

[BlitzContent("DECIMILLIPEDE_SEGMENT_FRONT")]
public sealed class DecimillipedeSegmentFront : DecimillipedeSegmentDef
{
}

[BlitzContent("DECIMILLIPEDE_SEGMENT_MIDDLE")]
public sealed class DecimillipedeSegmentMiddle : DecimillipedeSegmentDef
{
}

[BlitzContent("DECIMILLIPEDE_SEGMENT_BACK")]
public sealed class DecimillipedeSegmentBack : DecimillipedeSegmentDef
{
}

/// <summary>
/// PhrogParasite: "INFECT_MOVE" adds three Infection to the discard pile
/// (<c>MonsterMoveEffects</c> "PhrogParasite", "INFECT_MOVE"); LASH_MOVE is a four-hit attack. Its
/// starting InfestedPower spawns the Wrigglers on death.
/// </summary>
[BlitzContent("PHROG_PARASITE")]
public sealed class PhrogParasite : BlitzMonsterDef
{
    public override IReadOnlyList<string> SpawnedMonsterIds => ["WRIGGLER"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("INFECTION", 0)];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "INFECT_MOVE")
            return;
        for (int index = 0; index < 3; index++)
        {
            BlitzKernel.AddGeneratedCard(ref x,
                BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Infection>()), BlitzPile.Discard, creator: BlitzLimits.NoOwner);
        }
    }
}

/// <summary>
/// Wriggler: spawned only by <c>InfestedPower</c> with <c>StartStunned = true</c>, so its compiled
/// spawn entry starts at SPAWNED_MOVE and its INIT_MOVE conditional selects the first real move from
/// the slot name (wriggler1/3 NASTY_BITE_MOVE, wriggler2/4 WRIGGLE_MOVE;
/// <c>BranchMonsterAi.ResolveConditional</c> has no special case, so the native sequential conditions
/// are reproduced here). "WRIGGLE_MOVE" adds one Infection to the discard pile and applies Strength 2
/// (<c>MonsterMoveEffects</c> "Wriggler", "WRIGGLE_MOVE").
/// </summary>
[BlitzContent("WRIGGLER")]
public sealed class Wriggler : BlitzMonsterDef
{
    public override string? SpawnSlotName => "wriggler1";

    public override bool SpawnedStunned => true;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("INFECTION", 0)];

    public override int ResolveConditional(ref BlitzExec x, byte owner, int branchNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[branchNode].NativeId != "INIT_MOVE")
            return -1;
        byte slot = x.B.Enemy(owner).Slot;
        if (slot == BlitzKernel.NoSlot)
            throw new BlitzUnsupportedException("Wriggler 的 INIT_MOVE 没有遭遇槽位。");
        string name = x.R.EncounterSlots[slot];
        return name switch
        {
            "wriggler1" or "wriggler3" => rules.NodeIndex("NASTY_BITE_MOVE"),
            "wriggler2" or "wriggler4" => rules.NodeIndex("WRIGGLE_MOVE"),
            _ => throw new BlitzUnsupportedException($"Wriggler 的 INIT_MOVE 没有槽位 {name} 的分支。"),
        };
    }

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "WRIGGLE_MOVE")
            return;
        BlitzKernel.AddGeneratedCard(ref x,
            BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Infection>()), BlitzPile.Discard, creator: BlitzLimits.NoOwner);
        BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
    }
}

/// <summary>
/// HunterKiller: "TENDERIZING_GOOP_MOVE" applies TenderPower 1 to the player; BITE_MOVE and
/// PUNCTURE_MOVE are attacks (<c>MonsterMoveEffects</c> "HunterKiller", "TENDERIZING_GOOP_MOVE").
/// </summary>
[BlitzContent("HUNTER_KILLER")]
public sealed class HunterKiller : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId == "TENDERIZING_GOOP_MOVE")
        {
            BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                BlitzRegistry.PowerId<Powers.TenderPower>(), 1, owner);
        }
    }
}

/// <summary>
/// LouseProgenitor: "WEB_CANNON_MOVE" is an attack followed by Frail 2; "CURL_AND_GROW_MOVE" gains
/// <c>CurlBlock</c> block and <c>GrowStrength</c> Strength (<c>MonsterMoveEffects</c>
/// "LouseProgenitor"). Its starting CurlUpPower handles the card-triggered block. The private
/// <c>_curled</c> animation flag is never written by the existing engine and no resolution path reads
/// it.
/// </summary>
[BlitzContent("LOUSE_PROGENITOR")]
public sealed class LouseProgenitor : BlitzMonsterDef
{
    private const int CurlBlock = 0;
    private const int GrowStrength = 1;

    public override IReadOnlyList<string> StaticIntNames => ["CurlBlock", "GrowStrength"];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "WEB_CANNON_MOVE":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.FrailPower>(), 2, owner);
                break;
            case "CURL_AND_GROW_MOVE":
                BlitzKernel.GainBlock(ref x, owner, rules.StaticInts[CurlBlock], BlitzValueProps.Move, fromCard: false);
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
                    rules.StaticInts[GrowStrength], owner);
                break;
        }
    }
}

/// <summary>
/// Crusher (Kaiser Crab): "BUG_STING_MOVE" applies Weak 2 and Frail 2 to the player; "ADAPT_MOVE"
/// applies <c>AdaptStrengthGain</c> Strength; "GUARDED_STRIKE_MOVE" gains 18 Unpowered block-value
/// block (<c>MonsterMoveEffects</c> "Crusher"). BackAttackLeft/CrabRage start on the root creature.
/// </summary>
[BlitzContent("CRUSHER")]
public sealed class Crusher : BlitzMonsterDef
{
    private const int AdaptStrengthGain = 0;
    private const int GuardedStrikeBlock = 18;

    public override IReadOnlyList<string> StaticIntNames => ["AdaptStrengthGain"];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "BUG_STING_MOVE":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.WeakPower>(), 2, owner);
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.FrailPower>(), 2, owner);
                break;
            case "ADAPT_MOVE":
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
                    rules.StaticInts[AdaptStrengthGain], owner);
                break;
            case "GUARDED_STRIKE_MOVE":
                // MonsterMoveEffects hard-codes 18 (ValueProp.Move).
                BlitzKernel.GainBlock(ref x, owner, GuardedStrikeBlock, BlitzValueProps.Move, fromCard: false);
                break;
        }
    }
}

/// <summary>
/// Rocket (Kaiser Crab): "CHARGE_UP_MOVE" applies <c>ChargeUpStrengthGain</c> Strength
/// (<c>MonsterMoveEffects</c> "Rocket"); the other moves are attacks. Surrounded/BackAttackRight/
/// CrabRage start on the root creature and its opponent.
/// </summary>
[BlitzContent("ROCKET")]
public sealed class Rocket : BlitzMonsterDef
{
    private const int ChargeUpStrengthGain = 0;

    public override IReadOnlyList<string> StaticIntNames => ["ChargeUpStrengthGain"];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "CHARGE_UP_MOVE")
            return;
        BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
            rules.StaticInts[ChargeUpStrengthGain], owner);
    }
}
