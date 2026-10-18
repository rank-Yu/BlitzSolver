namespace CombatSolver.Engine.InCombat.Flash.Content.Monsters.Act1;

// Move effects mirror MonsterMoveEffects.Apply; attacks come from the compiled state machine.

[FlashContent("FUZZY_WURM_CRAWLER")]
public sealed class FuzzyWurmCrawler : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (x.R.Monster(owner).Nodes[moveNode].NativeId == "INHALE")
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
        FlashMonsterRules rules = x.R.Monster(owner);
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
