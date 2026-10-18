namespace CombatSolver.Engine.InCombat.Flash.Content.Monsters.Act1;

// Remaining first-act (Overgrowth) elite/boss monsters. Attacks, hit counts and branch weights come
// from the state machine compiled by the bridge; ApplyMoveEffects mirrors the post-attack part of
// MonsterMoveEffects.Apply for the same (monster, move) pair. Debuff helper means
// combat.ApplyFromMonster; combat.Apply is mirrored with ApplyPower.

/// <summary>
/// Vantom: "DISMEMBER_MOVE" adds three Wounds to the discard pile; "PREPARE_MOVE" applies Strength 2
/// to itself (MonsterMoveEffects "Vantom", literals). Its SlipperyPower 8/9 is a starting power
/// imported from the root, not replayed here.
/// </summary>
[FlashContent("VANTOM")]
public sealed class Vantom : FlashMonsterDef
{
    private const int DismemberWounds = 3;
    private const int PrepareStrength = 2;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("WOUND", 0)];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        switch (x.R.Monster(owner).Nodes[moveNode].NativeId)
        {
            case "DISMEMBER_MOVE":
                for (int index = 0; index < DismemberWounds; index++)
                    FlashKernel.AddGeneratedCard(ref x,
                        FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Wound>()), FlashPile.Discard);
                break;
            case "PREPARE_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(),
                    PrepareStrength, owner);
                break;
        }
    }
}

/// <summary>
/// KinPriest: "ORB_OF_FRAILTY_MOVE" applies Frail 1 to the player, "ORB_OF_WEAKNESS_MOVE" Weak 1
/// (both <c>ApplyFromMonster</c>), and "RITUAL_MOVE" applies Strength <c>RitualStrength</c> to itself
/// (<c>MonsterMoveEffects</c> "KinPriest"; the existing engine applies Strength, not RitualPower).
/// </summary>
[FlashContent("KIN_PRIEST")]
public sealed class KinPriest : FlashMonsterDef
{
    private const int RitualStrength = 0;

    public override IReadOnlyList<string> StaticIntNames => ["RitualStrength"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = x.R.Monster(owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "ORB_OF_FRAILTY_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.FrailPower>(), 1, owner);
                break;
            case "ORB_OF_WEAKNESS_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.WeakPower>(), 1, owner);
                break;
            case "RITUAL_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(),
                    rules.StaticInts[RitualStrength], owner);
                break;
        }
    }
}

/// <summary>
/// KinFollower: "POWER_DANCE_MOVE" applies Strength <c>DanceStrength</c> to itself
/// (<c>MonsterMoveEffects</c> "KinFollower"). Its MinionPower is a starting power imported from the
/// root. The encounter's first follower starts on the dance move; that initial state is imported.
/// </summary>
[FlashContent("KIN_FOLLOWER")]
public sealed class KinFollower : FlashMonsterDef
{
    private const int DanceStrength = 0;

    public override IReadOnlyList<string> StaticIntNames => ["DanceStrength"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = x.R.Monster(owner);
        if (rules.Nodes[moveNode].NativeId == "POWER_DANCE_MOVE")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(),
                rules.StaticInts[DanceStrength], owner);
    }
}

/// <summary>
/// PhantasmalGardener: "ENLARGE_MOVE" applies Strength <c>EnlargeStr</c> to itself
/// (<c>MonsterMoveEffects</c> "PhantasmalGardener"). Its SkittishPower is a starting power imported
/// from the root.
///
/// The native state machine's initial state INIT_MOVE is a conditional branch keyed by the
/// encounter slot ("first" → FLAIL_MOVE, "second" → BITE_MOVE, "third" → LASH_MOVE,
/// "fourth" → ENLARGE_MOVE) that lives outside MonsterMoveStateMachine.States, so the rule compiler
/// stores no root selection for it. At the root each gardener's current move is already resolved and
/// imported; the branch is only walked for a fresh initial roll, and the four-gardener encounter
/// spawns them in slot order, so the enemy index recovers the native slot.
/// </summary>
[FlashContent("PHANTASMAL_GARDENER")]
public sealed class PhantasmalGardener : FlashMonsterDef
{
    private const int EnlargeStr = 0;

    public override IReadOnlyList<string> StaticIntNames => ["EnlargeStr"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = x.R.Monster(owner);
        if (rules.Nodes[moveNode].NativeId == "ENLARGE_MOVE")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(),
                rules.StaticInts[EnlargeStr], owner);
    }

    public override int ResolveConditional(ref FlashExec x, byte owner, int branchNode)
    {
        FlashMonsterRules rules = x.R.Monster(owner);
        if (rules.Nodes[branchNode].NativeId != "INIT_MOVE")
            return -1;
        string move = (owner - FlashLimits.FirstEnemyOwner) switch
        {
            0 => "FLAIL_MOVE",
            1 => "BITE_MOVE",
            2 => "LASH_MOVE",
            3 => "ENLARGE_MOVE",
            int index => throw new FlashUnsupportedException($"花园幽灵鳗没有第 {index} 个槽位的初始行动。"),
        };
        return rules.NodeIndex(move);
    }
}
