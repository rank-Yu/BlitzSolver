using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Content.Cards.Necrobinder;

/// <summary>
/// <c>NegativePulse</c>: player block, then Doom to every hittable enemy
/// (<c>CardEffectSpecRegistry</c>'s AllEnemies Doom; the block is the general recipe).
/// </summary>
[ContentId("NEGATIVE_PULSE")]
public sealed class NegativePulse : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "DoomPower"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        CardEffects.ApplyToAllEnemies(ref x, Registry.PowerId<Powers.DoomPower>(), 1);
    }
}

/// <summary>
/// <c>Scourge</c>: apply Doom to the target, then draw the card's Cards variable
/// (<c>CardEffectSpecRegistry</c>'s target Doom; the draw is the native <c>CardPileCmd.Draw</c>).
/// </summary>
[ContentId("SCOURGE")]
public sealed class Scourge : CardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower", "Cards"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.ApplyToTarget(ref x, Registry.PowerId<Powers.DoomPower>(), 0);
        if (x.NeedsChoice)
            return;
        Kernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 1), fromHandDraw: false);
    }
}

/// <summary><c>Afterlife</c>: summon Osty for the Summon variable (<c>CardOnPlaySupport.Batch043</c>).</summary>
[ContentId("AFTERLIFE")]
public sealed class Afterlife : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref Exec x) => Kernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
}

/// <summary>
/// <c>PullAggro</c>: the general block recipe runs in the mirror phase, then summon Osty
/// (<c>CardEffectSpecRegistry</c>'s ResourceEffects completion).
/// </summary>
[ContentId("PULL_AGGRO")]
public sealed class PullAggro : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon", "Block"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice)
            return;
        Kernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
    }
}

/// <summary><c>Wisp</c>: gain its Energy variable (<c>CardOnPlayCompensationCatalog</c>).</summary>
[ContentId("WISP")]
public sealed class Wisp : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref Exec x) => Kernel.GainEnergy(ref x, NecrobinderContent.Var(ref x, 0));
}

/// <summary><c>Defy</c>: player block, then Weak on the target (<c>CardEffectSpecRegistry</c>).</summary>
[ContentId("DEFY")]
public sealed class Defy : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "WeakPower"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        CardEffects.ApplyToTarget(ref x, Registry.PowerId<Powers.WeakPower>(), 1);
    }
}

/// <summary>
/// <c>GraveWarden</c>: player block, then one Soul at a random draw-pile position
/// (<c>CardEffectSpecRegistry</c>'s GraveWarden completion).
/// </summary>
[ContentId("GRAVE_WARDEN")]
public sealed class GraveWarden : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = (int)NecrobinderContent.Var(ref x, 1);
        for (int index = 0; index < count; index++)
        {
            Card soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded: false);
            if (!Kernel.AddGeneratedCard(ref x, soul, Pile.Draw, PilePosition.Random)
                || x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>Invoke</c>: apply SummonNextTurn for the Summon variable, then EnergyNextTurn for Energy
/// (<c>CardPowerOnPlaySupport</c>'s Invoke case).
/// </summary>
[ContentId("INVOKE")]
public sealed class Invoke : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon", "Energy"];

    public override void OnPlay(ref Exec x)
    {
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.SummonNextTurnPower>(),
            (int)NecrobinderContent.Var(ref x, 0), Limits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)NecrobinderContent.Var(ref x, 1), Limits.PlayerOwner);
    }
}
