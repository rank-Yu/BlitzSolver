namespace CombatSolver.Engine.InCombat.Flash.Content.Monsters.Act1;

// Move effects mirror MonsterMoveEffects.Apply; attacks come from the compiled state machine.

[FlashContent("FUZZY_WURM_CRAWLER")]
public sealed class FuzzyWurmCrawler : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "INHALE")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 7, owner);
    }
}

[FlashContent("NIBBIT")]
public sealed class Nibbit : FlashMonsterDef
{
    private const int SliceBlock = 0;
    private const int HissStrengthGain = 1;

    public override IReadOnlyList<string> StaticIntNames => ["SliceBlock", "HissStrengthGain"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "SLICE_MOVE":
                FlashKernel.GainBlock(ref x, owner, rules.StaticInts[SliceBlock], FlashValueProps.Move, fromCard: false);
                break;
            case "HISS_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), rules.StaticInts[HissStrengthGain], owner);
                break;
        }
    }
}

/// <summary>
/// Native <c>ThievingHopper</c> (THIEVING_HOPPER_WEAK). THIEVERY steals a deck card before its
/// attack (<c>MonsterMoveEffects.ApplyBeforeAttack</c>); FLUTTER gains FlutterPower; ESCAPE removes
/// the hopper from combat through <c>SimulatedCombatState.CreatureEscaped</c>. The room-entry
/// EscapeArtistPower 5 is applied by the root capture; the override keeps a summoned instance
/// consistent (no encounter summons one today).
/// </summary>
[FlashContent("THIEVING_HOPPER")]
public sealed class ThievingHopper : FlashMonsterDef
{
    public override void ApplyEntrancePowers(ref FlashExec x, byte owner)
    {
        // ThievingHopper.AfterAddedToRoom: PowerCmd.Apply<EscapeArtistPower>(..., 5m, ...).
        FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.EscapeArtistPower>(), 5, owner);
    }

    public override void BeforeMove(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "THIEVERY_MOVE")
            FlashKernel.StealCardForThievery(ref x, owner);
    }

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        switch (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "FLUTTER_MOVE":
                // MonsterMoveEffects.Apply: Apply<FlutterPower>(5).
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.FlutterPower>(), 5, owner);
                break;
            case "ESCAPE_MOVE":
                // MonsterMoveEffects.Apply: combat.CreatureEscaped(move.Owner); the stolen SwipePower
                // is removed without recovering its card.
                FlashKernel.EscapeEnemy(ref x, owner);
                break;
        }
    }
}
