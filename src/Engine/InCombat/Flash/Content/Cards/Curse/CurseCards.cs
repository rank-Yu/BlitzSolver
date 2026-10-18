namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Curse;

// Curse cards. Their canonical stats (cost, keywords, variables) come from the rule compiler
// through FlashRules; the definitions mirror the existing engine's OnTurnEndInHand.
//
// Existing engine paths for this batch:
// - Decay, BadLuck, Doubt and Shame are CardOnTurnEndInHandMirrors: damage (Decay), HP loss
//   (BadLuck), Weak or Frail with the native first-duration-tick skip (Doubt, Shame).
// - AscendersBane, Clumsy, CurseOfTheBell, Folly, Greed, Guilty, Injury, PoorSleep, SporeMind
//   and Writhe override no combat hook. Guilty's AfterCombatEnd is outside combat.
// - SporeMind is playable (cost 1, Exhaust) with no effect.

/// <summary>
/// Native <c>AscendersBane</c>: Eternal, Unplayable, Ethereal; no combat hook is overridden. Its
/// Ethereal keyword makes the kernel exhaust it from the hand at turn end.
/// </summary>
[FlashContent("ASCENDERS_BANE")]
public sealed class AscendersBane : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

/// <summary>
/// Native <c>BadLuck</c>: Eternal, Unplayable; at the player's turn end it loses 13 HP
/// (Unblockable|Unpowered|Move) with the card as source.
/// </summary>
[FlashContent("BAD_LUCK")]
public sealed class BadLuck : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss"];

    public override bool HasTurnEndInHandEffect => true;

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OnTurnEndInHand(ref FlashExec x)
        => FlashCardEffects.DamageOwnerFromCard(ref x, FlashCardEffects.Var(x, 0),
            FlashValueProps.Unblockable | FlashValueProps.Unpowered | FlashValueProps.Move);
}

/// <summary>Native <c>Clumsy</c>: Unplayable, Ethereal; no combat hook is overridden.</summary>
[FlashContent("CLUMSY")]
public sealed class Clumsy : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

/// <summary>Native <c>CurseOfTheBell</c>: Eternal, Unplayable; no combat hook is overridden.</summary>
[FlashContent("CURSE_OF_THE_BELL")]
public sealed class CurseOfTheBell : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

/// <summary>
/// Native <c>Decay</c>: Unplayable; at the player's turn end it takes 2 unpowered, blockable
/// damage with the card as source.
/// </summary>
[FlashContent("DECAY")]
public sealed class Decay : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override bool HasTurnEndInHandEffect => true;

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OnTurnEndInHand(ref FlashExec x)
        => FlashCardEffects.DamageOwnerFromCard(ref x, FlashCardEffects.Var(x, 0),
            FlashValueProps.Unpowered | FlashValueProps.Move);
}

/// <summary>
/// Native <c>Doubt</c>: Unplayable; at the player's turn end it applies 1 Weak and skips that
/// instance's first duration tick. <see cref="FlashPowerDef.UsesNativeDurationSkip"/> on the Weak
/// definition performs the same first-application skip as the native <c>alreadyHasWeak</c> check.
/// </summary>
[FlashContent("DOUBT")]
public sealed class Doubt : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["WeakPower"];

    public override bool HasTurnEndInHandEffect => true;

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OnTurnEndInHand(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.WeakPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.NoOwner);
}

/// <summary>Native <c>Folly</c>: Unplayable, Eternal, Innate, Ethereal; no combat hook is overridden.</summary>
[FlashContent("FOLLY")]
public sealed class Folly : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

/// <summary>Native <c>Greed</c>: Eternal, Unplayable; no combat hook is overridden.</summary>
[FlashContent("GREED")]
public sealed class Greed : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

/// <summary>
/// Native <c>Guilty</c>: Unplayable. Its only override is AfterCombatEnd, which removes a deck copy
/// outside combat and is not a combat hook; nothing executes in combat.
/// </summary>
[FlashContent("GUILTY")]
public sealed class Guilty : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

/// <summary>Native <c>Injury</c>: Unplayable; no combat hook is overridden.</summary>
[FlashContent("INJURY")]
public sealed class Injury : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

/// <summary>Native <c>PoorSleep</c>: Unplayable, Retain; no combat hook is overridden.</summary>
[FlashContent("POOR_SLEEP")]
public sealed class PoorSleep : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

/// <summary>
/// Native <c>Shame</c>: Unplayable; at the player's turn end it applies 1 Frail and skips that
/// instance's first duration tick (the same first-application rule as Doubt).
/// </summary>
[FlashContent("SHAME")]
public sealed class Shame : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Frail"];

    public override bool HasTurnEndInHandEffect => true;

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OnTurnEndInHand(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FrailPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.NoOwner);
}

/// <summary>Native <c>SporeMind</c>: cost 1, Exhaust; playable with no effect.</summary>
[FlashContent("SPORE_MIND")]
public sealed class SporeMind : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

/// <summary>Native <c>Writhe</c>: Innate, Unplayable; no combat hook is overridden.</summary>
[FlashContent("WRITHE")]
public sealed class Writhe : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}
