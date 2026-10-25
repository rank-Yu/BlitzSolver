using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Curse;

/// <summary>
/// Native <c>AscendersBane</c>: Eternal, Unplayable, Ethereal; no combat hook is overridden. Its
/// Ethereal keyword makes the kernel exhaust it from the hand at turn end.
/// </summary>
[BlitzContent("ASCENDERS_BANE")]
public sealed class AscendersBane : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>
/// Native <c>BadLuck</c>: Eternal, Unplayable; at the player's turn end it loses 13 HP
/// (Unblockable|Unpowered|Move) with the card as source.
/// </summary>
[BlitzContent("BAD_LUCK")]
public sealed class BadLuck : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss"];

    public override bool HasTurnEndInHandEffect => true;

    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OnTurnEndInHand(ref BlitzExec x)
        => BlitzCardEffects.DamageOwnerFromCard(ref x, BlitzCardEffects.Var(x, 0),
            BlitzValueProps.Unblockable | BlitzValueProps.Unpowered | BlitzValueProps.Move);
}

/// <summary>Native <c>Clumsy</c>: Unplayable, Ethereal; no combat hook is overridden.</summary>
[BlitzContent("CLUMSY")]
public sealed class Clumsy : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>Native <c>CurseOfTheBell</c>: Eternal, Unplayable; no combat hook is overridden.</summary>
[BlitzContent("CURSE_OF_THE_BELL")]
public sealed class CurseOfTheBell : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>
/// Native <c>Decay</c>: Unplayable; at the player's turn end it takes 2 unpowered, blockable
/// damage with the card as source.
/// </summary>
[BlitzContent("DECAY")]
public sealed class Decay : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override bool HasTurnEndInHandEffect => true;

    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OnTurnEndInHand(ref BlitzExec x)
        => BlitzCardEffects.DamageOwnerFromCard(ref x, BlitzCardEffects.Var(x, 0),
            BlitzValueProps.Unpowered | BlitzValueProps.Move);
}

/// <summary>
/// Native <c>Doubt</c>: Unplayable; at the player's turn end it applies 1 Weak and skips that
/// instance's first duration tick. <see cref="BlitzPowerDef.UsesNativeDurationSkip"/> on the Weak
/// definition performs the same first-application skip as the native <c>alreadyHasWeak</c> check.
/// </summary>
[BlitzContent("DOUBT")]
public sealed class Doubt : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["WeakPower"];

    public override bool HasTurnEndInHandEffect => true;

    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OnTurnEndInHand(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.WeakPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.NoOwner);
}

/// <summary>Native <c>Folly</c>: Unplayable, Eternal, Innate, Ethereal; no combat hook is overridden.</summary>
[BlitzContent("FOLLY")]
public sealed class Folly : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>Native <c>Greed</c>: Eternal, Unplayable; no combat hook is overridden.</summary>
[BlitzContent("GREED")]
public sealed class Greed : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>
/// Native <c>Guilty</c>: Unplayable. Its only override is AfterCombatEnd, which removes a deck copy
/// outside combat and is not a combat hook; nothing executes in combat.
/// </summary>
[BlitzContent("GUILTY")]
public sealed class Guilty : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>Native <c>Injury</c>: Unplayable; no combat hook is overridden.</summary>
[BlitzContent("INJURY")]
public sealed class Injury : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>Native <c>PoorSleep</c>: Unplayable, Retain; no combat hook is overridden.</summary>
[BlitzContent("POOR_SLEEP")]
public sealed class PoorSleep : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>
/// Native <c>Shame</c>: Unplayable; at the player's turn end it applies 1 Frail and skips that
/// instance's first duration tick (the same first-application rule as Doubt).
/// </summary>
[BlitzContent("SHAME")]
public sealed class Shame : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Frail"];

    public override bool HasTurnEndInHandEffect => true;

    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OnTurnEndInHand(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.FrailPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.NoOwner);
}

/// <summary>Native <c>SporeMind</c>: cost 1, Exhaust; playable with no effect.</summary>
[BlitzContent("SPORE_MIND")]
public sealed class SporeMind : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>Native <c>Writhe</c>: Innate, Unplayable; no combat hook is overridden.</summary>
[BlitzContent("WRITHE")]
public sealed class Writhe : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>
/// Native <c>Enthralled</c>: cost 2, Eternal; while it is in hand no other card can be played
/// (<c>SimulatedCombatState.IsCardPlayPrevented</c>, implemented by
/// <see cref="BlitzCardDef.LocksOtherHandCards"/>). It has no OnPlay effect.
/// </summary>
[BlitzContent("ENTHRALLED")]
public sealed class Enthralled : BlitzCardDef
{
    public override bool LocksOtherHandCards => true;

    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>
/// Native <c>Normality</c>: Unplayable; while it is in hand no card can be played once three card
/// plays have started this turn (<c>ShouldPlayMirrors.HandleNormality</c>, the native
/// <c>_numOfCardsPerTurn</c> constant).
/// </summary>
[BlitzContent("NORMALITY")]
public sealed class Normality : BlitzCardDef
{
    private const int CardsPerTurn = 3;

    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override bool ShouldPlay(in BlitzBattle battle, BlitzRules rules, BlitzListener self, in BlitzCard card)
        => self.Index >= battle.HandCount
            || battle.Counter(BlitzCounter.CardPlayStartsThisTurn) < CardsPerTurn;
}

/// <summary>
/// Native <c>Regret</c>: Unplayable; <c>BeforeSideTurnEnd</c> records the hand size in the card's
/// private counter (<c>BeforeSideTurnEndMirrors.HandleRegret</c>), and at the turn end it loses that
/// much HP unblockable/unpowered and resets the counter
/// (<c>CardOnTurnEndInHandMirrors.HandleRegret</c>). Private = cards in hand when the turn end began.
/// </summary>
[BlitzContent("REGRET")]
public sealed class Regret : BlitzCardDef
{
    public override bool HasTurnEndInHandEffect => true;

    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void BeforeSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        if (!BlitzKernel.Participates(participants, self.Owner) || self.Index >= x.B.HandCount)
            return;
        ref BlitzCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        card.Private = checked((short)x.B.HandCount);
    }

    public override void OnTurnEndInHand(ref BlitzExec x)
    {
        BlitzCardEffects.DamageOwnerFromCard(ref x, x.Card.Private,
            BlitzValueProps.Unblockable | BlitzValueProps.Unpowered | BlitzValueProps.Move);
        if (x.NeedsChoice)
            return;
        int position = x.B.PlayCount - 1;
        ref BlitzCard played = ref BlitzPiles.At(ref x.B, BlitzPile.Play, position);
        if (played.Id != x.Card.Id || played.Upgrade != x.Card.Upgrade)
            throw new InvalidOperationException("回合末在手效果没有把瑞格瑞特放在打出堆顶。");
        played.Private = 0;
        x.Card.Private = 0;
    }
}

/// <summary>
/// Native <c>Debt</c>: Unplayable, no upgrade; its <c>OnTurnEndInHand</c> loses up to 10 gold. The
/// existing engine has no mirror/compensation for that hook, so Blitz keeps the definition empty to
/// match the authority (the card still enters the play pile and is discarded at turn end).
/// </summary>
[BlitzContent("DEBT")]
public sealed class Debt : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}
