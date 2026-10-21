using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

/// <summary>
/// <c>NegativePulse</c>: player block, then Doom to every hittable enemy
/// (<c>CardEffectSpecRegistry</c>'s AllEnemies Doom; the block is the general recipe).
/// </summary>
[FlashContent("NEGATIVE_PULSE")]
public sealed class NegativePulse : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "DoomPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToAllEnemies(ref x, FlashRegistry.PowerId<Powers.DoomPower>(), 1);
    }
}

/// <summary>
/// <c>Scourge</c>: apply Doom to the target, then draw the card's Cards variable
/// (<c>CardEffectSpecRegistry</c>'s target Doom; the draw is the native <c>CardPileCmd.Draw</c>).
/// </summary>
[FlashContent("SCOURGE")]
public sealed class Scourge : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.DoomPower>(), 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 1), fromHandDraw: false);
    }
}

/// <summary><c>Afterlife</c>: summon Osty for the Summon variable (<c>CardOnPlaySupport.Batch043</c>).</summary>
[FlashContent("AFTERLIFE")]
public sealed class Afterlife : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
}

/// <summary>
/// <c>PullAggro</c>: the general block recipe runs in the mirror phase, then summon Osty
/// (<c>CardEffectSpecRegistry</c>'s ResourceEffects completion).
/// </summary>
[FlashContent("PULL_AGGRO")]
public sealed class PullAggro : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon", "Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice)
            return;
        FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
    }
}

/// <summary><c>Wisp</c>: gain its Energy variable (<c>CardOnPlayCompensationCatalog</c>).</summary>
[FlashContent("WISP")]
public sealed class Wisp : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.GainEnergy(ref x, NecrobinderContent.Var(ref x, 0));
}

/// <summary><c>Defy</c>: player block, then Weak on the target (<c>CardEffectSpecRegistry</c>).</summary>
[FlashContent("DEFY")]
public sealed class Defy : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

/// <summary>
/// <c>GraveWarden</c>: player block, then one Soul at a random draw-pile position
/// (<c>CardEffectSpecRegistry</c>'s GraveWarden completion).
/// </summary>
[FlashContent("GRAVE_WARDEN")]
public sealed class GraveWarden : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = (int)NecrobinderContent.Var(ref x, 1);
        for (int index = 0; index < count; index++)
        {
            FlashCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded: false);
            if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Draw, FlashPilePosition.Random)
                || x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>Invoke</c>: apply SummonNextTurn for the Summon variable, then EnergyNextTurn for Energy
/// (<c>CardPowerOnPlaySupport</c>'s Invoke case).
/// </summary>
[FlashContent("INVOKE")]
public sealed class Invoke : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon", "Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.SummonNextTurnPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)NecrobinderContent.Var(ref x, 1), FlashLimits.PlayerOwner);
    }
}
