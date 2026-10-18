namespace CombatSolver.Engine.InCombat.Flash.Content.Monsters.Act1;

// Remaining first-act (Underdocks) elite/boss monsters. Attacks, hit counts and branch weights come
// from the state machine compiled by the bridge; ApplyMoveEffects mirrors the post-attack part of
// MonsterMoveEffects.Apply for the same (monster, move) pair. Debuff helper means
// combat.ApplyFromMonster; combat.Apply is mirrored with ApplyPower.

/// <summary>
/// FossilStalker: "TACKLE_MOVE" applies Frail 1 to the player (<c>MonsterMoveEffects</c>
/// "FossilStalker"). Its SuckPower is a starting power imported from the root; LATCH/LASH are pure
/// attacks and the random branch weights are compiled from the native model.
/// </summary>
[FlashContent("FOSSIL_STALKER")]
public sealed class FossilStalker : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (x.R.Monster(owner).Nodes[moveNode].NativeId == "TACKLE_MOVE")
            FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                FlashRegistry.PowerId<Powers.FrailPower>(), 1, owner);
    }
}

/// <summary>
/// SoulFysh: "BECKON_MOVE" adds one Beckon to the draw pile at a random position and one to the
/// discard pile; "GAZE_MOVE" applies Vulnerable <c>ScreamMoveAmount</c> and adds
/// <c>GazeMoveAmount</c> Beckons to the discard pile; "SCREAM_MOVE" applies Vulnerable
/// <c>ScreamMoveAmount</c> to the player; "FADE_MOVE" applies Intangible 2 to itself
/// (<c>MonsterMoveEffects</c> "SoulFysh"; the Beckon counts are literals there: 1 random draw +
/// 1 discard, and the model's GazeMoveAmount/ScreamMoveAmount static values).
/// </summary>
[FlashContent("SOUL_FYSH")]
public sealed class SoulFysh : FlashMonsterDef
{
    private const int BeckonDrawAmount = 1;
    private const int BeckonDiscardAmount = 1;
    private const int FadeIntangible = 2;
    private const int ScreamMoveAmount = 0;
    private const int GazeMoveAmount = 1;

    public override IReadOnlyList<string> StaticIntNames => ["ScreamMoveAmount", "GazeMoveAmount"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("BECKON", 0)];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = x.R.Monster(owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "BECKON_MOVE":
                for (int index = 0; index < BeckonDrawAmount; index++)
                    FlashKernel.AddGeneratedCard(ref x,
                        FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Beckon>()),
                        FlashPile.Draw, FlashPilePosition.Random);
                for (int index = 0; index < BeckonDiscardAmount; index++)
                    FlashKernel.AddGeneratedCard(ref x,
                        FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Beckon>()), FlashPile.Discard);
                break;
            case "GAZE_MOVE":
                for (int index = 0; index < rules.StaticInts[GazeMoveAmount]; index++)
                    FlashKernel.AddGeneratedCard(ref x,
                        FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Beckon>()), FlashPile.Discard);
                break;
            case "SCREAM_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.VulnerablePower>(), rules.StaticInts[ScreamMoveAmount], owner);
                break;
            case "FADE_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.IntangiblePower>(),
                    FadeIntangible, owner);
                break;
        }
    }
}

/// <summary>
/// SkulkingColony: "INERTIA_MOVE" applies Strength <c>InertiaStrengthGain</c> to itself
/// (<c>MonsterMoveEffects</c> "SkulkingColony"). Its HardenedShellPower is a starting power imported
/// from the root; ZOOM/PIERCING_STABS are pure attacks.
/// </summary>
[FlashContent("SKULKING_COLONY")]
public sealed class SkulkingColony : FlashMonsterDef
{
    private const int InertiaStrengthGain = 0;

    public override IReadOnlyList<string> StaticIntNames => ["InertiaStrengthGain"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = x.R.Monster(owner);
        if (rules.Nodes[moveNode].NativeId == "INERTIA_MOVE")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(),
                rules.StaticInts[InertiaStrengthGain], owner);
    }
}
