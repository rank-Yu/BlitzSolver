using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Necrobinder;

/// <summary>
/// <c>NegativePulse</c>: player block, then Doom to every hittable enemy
/// (<c>CardEffectSpecRegistry</c>'s AllEnemies Doom; the block is the general recipe).
/// </summary>
[BlitzContent("NEGATIVE_PULSE")]
public sealed class NegativePulse : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "DoomPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToAllEnemies(ref x, BlitzRegistry.PowerId<Powers.DoomPower>(), 1);
    }
}

/// <summary>
/// <c>Scourge</c>: apply Doom to the target, then draw the card's Cards variable
/// (<c>CardEffectSpecRegistry</c>'s target Doom; the draw is the native <c>CardPileCmd.Draw</c>).
/// </summary>
[BlitzContent("SCOURGE")]
public sealed class Scourge : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.DoomPower>(), 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 1), fromHandDraw: false);
    }
}

/// <summary><c>Afterlife</c>: summon Osty for the Summon variable (<c>CardOnPlaySupport.Batch043</c>).</summary>
[BlitzContent("AFTERLIFE")]
public sealed class Afterlife : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref BlitzExec x) => BlitzKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
}

/// <summary>
/// <c>PullAggro</c>: the general block recipe runs in the mirror phase, then summon Osty
/// (<c>CardEffectSpecRegistry</c>'s ResourceEffects completion).
/// </summary>
[BlitzContent("PULL_AGGRO")]
public sealed class PullAggro : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon", "Block"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice)
            return;
        BlitzKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
    }
}

/// <summary><c>Wisp</c>: gain its Energy variable (<c>CardOnPlayCompensationCatalog</c>).</summary>
[BlitzContent("WISP")]
public sealed class Wisp : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref BlitzExec x) => BlitzKernel.GainEnergy(ref x, NecrobinderContent.Var(ref x, 0));
}

/// <summary><c>Defy</c>: player block, then Weak on the target (<c>CardEffectSpecRegistry</c>).</summary>
[BlitzContent("DEFY")]
public sealed class Defy : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "WeakPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

/// <summary>
/// <c>GraveWarden</c>: player block, then one Soul at a random draw-pile position
/// (<c>CardEffectSpecRegistry</c>'s GraveWarden completion).
/// </summary>
[BlitzContent("GRAVE_WARDEN")]
public sealed class GraveWarden : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = (int)NecrobinderContent.Var(ref x, 1);
        for (int index = 0; index < count; index++)
        {
            BlitzCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded: false);
            if (!BlitzKernel.AddGeneratedCard(ref x, soul, BlitzPile.Draw, BlitzPilePosition.Random)
                || x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>Invoke</c>: apply SummonNextTurn for the Summon variable, then EnergyNextTurn for Energy
/// (<c>CardPowerOnPlaySupport</c>'s Invoke case).
/// </summary>
[BlitzContent("INVOKE")]
public sealed class Invoke : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon", "Energy"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.SummonNextTurnPower>(),
            (int)NecrobinderContent.Var(ref x, 0), BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)NecrobinderContent.Var(ref x, 1), BlitzLimits.PlayerOwner);
    }
}
