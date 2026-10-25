using BlitzSolver.Engine;

namespace BlitzSolver.Content.Monsters.Act1;

[BlitzContent("FUZZY_WURM_CRAWLER")]
public sealed class FuzzyWurmCrawler : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "INHALE")
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 7, owner);
    }
}

[BlitzContent("NIBBIT")]
public sealed class Nibbit : BlitzMonsterDef
{
    private const int SliceBlock = 0;
    private const int HissStrengthGain = 1;

    public override IReadOnlyList<string> StaticIntNames => ["SliceBlock", "HissStrengthGain"];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "SLICE_MOVE":
                BlitzKernel.GainBlock(ref x, owner, rules.StaticInts[SliceBlock], BlitzValueProps.Move, fromCard: false);
                break;
            case "HISS_MOVE":
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), rules.StaticInts[HissStrengthGain], owner);
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
[BlitzContent("THIEVING_HOPPER")]
public sealed class ThievingHopper : BlitzMonsterDef
{
    public override void ApplyEntrancePowers(ref BlitzExec x, byte owner)
    {
        // ThievingHopper.AfterAddedToRoom: PowerCmd.Apply<EscapeArtistPower>(..., 5m, ...).
        BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.EscapeArtistPower>(), 5, owner);
    }

    public override void BeforeMove(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "THIEVERY_MOVE")
            BlitzKernel.StealCardForThievery(ref x, owner);
    }

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        switch (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "FLUTTER_MOVE":
                // MonsterMoveEffects.Apply: Apply<FlutterPower>(5).
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.FlutterPower>(), 5, owner);
                break;
            case "ESCAPE_MOVE":
                // MonsterMoveEffects.Apply: combat.CreatureEscaped(move.Owner); the stolen SwipePower
                // is removed without recovering its card.
                BlitzKernel.EscapeEnemy(ref x, owner);
                break;
        }
    }
}
