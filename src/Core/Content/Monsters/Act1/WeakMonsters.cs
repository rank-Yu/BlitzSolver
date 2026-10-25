using BlitzSolver.Engine;

namespace BlitzSolver.Content.Monsters.Act1;

[ContentId("FUZZY_WURM_CRAWLER")]
public sealed class FuzzyWurmCrawler : MonsterDef
{
    public override void ApplyMoveEffects(ref Exec x, byte owner, int moveNode)
    {
        if (Kernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "INHALE")
            Kernel.ApplyPower(ref x, owner, Registry.PowerId<Powers.StrengthPower>(), 7, owner);
    }
}

[ContentId("NIBBIT")]
public sealed class Nibbit : MonsterDef
{
    private const int SliceBlock = 0;
    private const int HissStrengthGain = 1;

    public override IReadOnlyList<string> StaticIntNames => ["SliceBlock", "HissStrengthGain"];

    public override void ApplyMoveEffects(ref Exec x, byte owner, int moveNode)
    {
        MonsterRules rules = Kernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "SLICE_MOVE":
                Kernel.GainBlock(ref x, owner, rules.StaticInts[SliceBlock], ValueProps.Move, fromCard: false);
                break;
            case "HISS_MOVE":
                Kernel.ApplyPower(ref x, owner, Registry.PowerId<Powers.StrengthPower>(), rules.StaticInts[HissStrengthGain], owner);
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
[ContentId("THIEVING_HOPPER")]
public sealed class ThievingHopper : MonsterDef
{
    public override void ApplyEntrancePowers(ref Exec x, byte owner)
    {
        // ThievingHopper.AfterAddedToRoom: PowerCmd.Apply<EscapeArtistPower>(..., 5m, ...).
        Kernel.ApplyPower(ref x, owner, Registry.PowerId<Powers.EscapeArtistPower>(), 5, owner);
    }

    public override void BeforeMove(ref Exec x, byte owner, int moveNode)
    {
        if (Kernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "THIEVERY_MOVE")
            Kernel.StealCardForThievery(ref x, owner);
    }

    public override void ApplyMoveEffects(ref Exec x, byte owner, int moveNode)
    {
        switch (Kernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "FLUTTER_MOVE":
                // MonsterMoveEffects.Apply: Apply<FlutterPower>(5).
                Kernel.ApplyPower(ref x, owner, Registry.PowerId<Powers.FlutterPower>(), 5, owner);
                break;
            case "ESCAPE_MOVE":
                // MonsterMoveEffects.Apply: combat.CreatureEscaped(move.Owner); the stolen SwipePower
                // is removed without recovering its card.
                Kernel.EscapeEnemy(ref x, owner);
                break;
        }
    }
}
