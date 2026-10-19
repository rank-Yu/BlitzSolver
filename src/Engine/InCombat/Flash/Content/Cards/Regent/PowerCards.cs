namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Regent;

// Regent forge and power cards. Existing engine paths: PersistentPowerSupport.Forge for every forge,
// CorePowerSupport.ApplyCardPowers / CardEffectSpecRegistry for the power applications, and
// CardOnPlayMirrors' SovereignBlade mirror.

/// <summary>
/// <c>RefineBlade</c>: CorePowerSupport forges the Forge variable, then queues EnergyNextTurnPower
/// (<c>combat.AddEnergyNextTurn</c>).
/// </summary>
[FlashContent("REFINE_BLADE")]
public sealed class RefineBlade : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge", "Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 0)))
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary><c>Bulwark</c>: the inferred recipe gains Block, then CardEffectSpecRegistry forges.</summary>
[FlashContent("BULWARK")]
public sealed class Bulwark : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Forge"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 1));
    }
}

/// <summary><c>WroughtInWar</c>: the inferred attack, then CardEffectSpecRegistry forges.</summary>
[FlashContent("WROUGHT_IN_WAR")]
public sealed class WroughtInWar : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Forge"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 1));
    }
}

/// <summary>
/// <c>Conqueror</c>: CardOnPlaySupport forges, then applies ConquerorPower 1 to the chosen enemy.
/// </summary>
[FlashContent("CONQUEROR")]
public sealed class Conqueror : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 0)))
            return;
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.ConquerorPower>(), 1,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>SeekingEdge</c>: CardOnPlaySupport forges, then CardPowerOnPlaySupport's late pass applies
/// SeekingEdgePower 1.
/// </summary>
[FlashContent("SEEKING_EDGE")]
public sealed class SeekingEdge : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override IReadOnlyList<string> AppliedPowers => ["SEEKING_EDGE_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 0)))
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.SeekingEdgePower>(), 1,
            FlashLimits.PlayerOwner);
    }
}

/// <summary><c>TheSmith</c>: CardOnPlaySupport forges the Forge variable.</summary>
[FlashContent("THE_SMITH")]
public sealed class TheSmith : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 0));
}

/// <summary><c>Furnace</c>: CardPowerOnPlaySupport applies FurnacePower with the Forge variable.</summary>
[FlashContent("FURNACE")]
public sealed class Furnace : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override IReadOnlyList<string> AppliedPowers => ["FURNACE_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FurnacePower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>SovereignBlade</c> (a token): BespokeCardMirrors attacks once per Repeat with the card's Damage
/// (SeekingEdgePower switches the target to every enemy), then gains block equal to ParryPower when
/// its owner has one. Repeat is always 1: no vanilla effect calls <c>SetRepeats</c>.
/// </summary>
[FlashContent("SOVEREIGN_BLADE")]
public sealed class SovereignBlade : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override bool UsesIdentityLedger => true;

    public override FlashTargetType TargetType(in FlashBattle battle, FlashRules rules, in FlashCard card)
        => RegentContent.HasSeekingEdge(battle) ? FlashTargetType.AllEnemies : FlashTargetType.AnyEnemy;

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: (int)FlashCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        int parry = RegentContent.ParryAmount(x.B);
        if (parry > 0)
            FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, parry, FlashValueProps.Move, fromCard: true);
    }
}

/// <summary>
/// <c>Parry</c>: CardPowerOnPlaySupport applies ParryPower with the "ParryPower" variable; Sovereign
/// Blade reads the power for its block.
/// </summary>
[FlashContent("PARRY")]
public sealed class Parry : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["ParryPower"];

    public override IReadOnlyList<string> AppliedPowers => ["PARRY_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ParryPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>ChildOfTheStars</c>: CardPowerOnPlaySupport applies ChildOfTheStarsPower.</summary>
[FlashContent("CHILD_OF_THE_STARS")]
public sealed class ChildOfTheStars : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["BlockForStars"];

    public override IReadOnlyList<string> AppliedPowers => ["CHILD_OF_THE_STARS_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.ChildOfTheStarsPower>(), (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
}

/// <summary><c>Genesis</c>: CardPowerOnPlaySupport applies GenesisPower with "StarsPerTurn".</summary>
[FlashContent("GENESIS")]
public sealed class Genesis : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StarsPerTurn"];

    public override IReadOnlyList<string> AppliedPowers => ["GENESIS_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.GenesisPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>Arsenal</c>: CardPowerOnPlaySupport applies ArsenalPower with "ArsenalPower".</summary>
[FlashContent("ARSENAL")]
public sealed class Arsenal : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["ArsenalPower"];

    public override IReadOnlyList<string> AppliedPowers => ["ARSENAL_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ArsenalPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>Reflect</c> (3 star cost): the inferred block, then CardEffectSpecRegistry's ReflectPower 1.</summary>
[FlashContent("REFLECT")]
public sealed class Reflect : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<string> AppliedPowers => ["REFLECT_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ReflectPower>(), 1,
            FlashLimits.PlayerOwner);
    }
}
