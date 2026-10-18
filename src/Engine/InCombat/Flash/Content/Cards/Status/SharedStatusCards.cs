namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Status;

// Status cards without a character-specific pile source. Their canonical stats (cost, keywords,
// damaged/hp-loss variables) come from the rule compiler; the definitions mirror the existing
// engine's OnPlay / OnTurnEndInHand.
//
// Existing engine paths for this batch:
// - Burn, Infection, Toxic and Wither are CardOnTurnEndInHandMirrors.HandleDamage: blockable
//   self damage with the card as source (DamageVar Unpowered|Move).
// - Beckon is CardOnTurnEndInHandMirrors.HandleHpLoss: unblockable, unpowered self HP loss with
//   the card as source (HpLossVar, DamageProps.cardHpLoss).
// - Debris is an explicit no-op (CardOnPlaySupport `case Debris: break;`); the native OnPlay
//   returns Task.CompletedTask.
// - Soot and Wound override no combat hook; they are only Unplayable.
// - Toxic and Debris are playable; their play itself has no effect.

/// <summary>
/// Native <c>Beckon</c>: cost 1, playable with no OnPlay effect; at the player's turn end it loses
/// 6 HP (Unblockable|Unpowered|Move) with the card as source.
/// </summary>
[FlashContent("BECKON")]
public sealed class Beckon : FlashCardDef
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

/// <summary>
/// Native <c>Burn</c>: cost -1, Unplayable; at the player's turn end it takes 2 unpowered,
/// blockable damage with the card as source.
/// </summary>
[FlashContent("BURN")]
public sealed class Burn : FlashCardDef
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

/// <summary>Native <c>Debris</c>: cost 1, Exhaust; its OnPlay is an explicit no-op.</summary>
[FlashContent("DEBRIS")]
public sealed class Debris : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

/// <summary>
/// Native <c>Infection</c>: cost -1, Unplayable; at the player's turn end it takes 3 unpowered,
/// blockable damage with the card as source.
/// </summary>
[FlashContent("INFECTION")]
public sealed class Infection : FlashCardDef
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

/// <summary>Native <c>Soot</c>: cost -1, Unplayable; no combat hook is overridden.</summary>
[FlashContent("SOOT")]
public sealed class Soot : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

/// <summary>
/// Native <c>Toxic</c>: cost 1, Exhaust; playable but its OnPlay has no effect. At the player's
/// turn end it takes 5 unpowered, blockable damage with the card as source.
/// </summary>
[FlashContent("TOXIC")]
public sealed class Toxic : FlashCardDef
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
/// Native <c>Wither</c>: cost -1, Unplayable; at the player's turn end it takes 3 unpowered,
/// blockable damage with the card as source. Aeonglass upgrades the Damage variable through a
/// private counter that admission rejects, so the rule table value is the whole supported state.
/// </summary>
[FlashContent("WITHER")]
public sealed class Wither : FlashCardDef
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

/// <summary>Native <c>Wound</c>: cost -1, Unplayable; no combat hook is overridden.</summary>
[FlashContent("WOUND")]
public sealed class Wound : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}

/// <summary>
/// Native <c>Void</c>: cost -1, Unplayable, Ethereal; when drawn it loses its Energy
/// (<c>AfterCardDrawnMirrors.HandleVoid</c>, claimed once with
/// <c>FlashKernel.TakeIntrinsicDraw</c>). The Energy variable comes from the card drawn at
/// <paramref name="drawnPosition"/>; the definition touches no other card state.
/// </summary>
[FlashContent("VOID")]
public sealed class Void : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        if (!FlashKernel.TakeIntrinsicDraw(ref x, self, drawnPosition))
            return;
        ref readonly FlashCard drawn = ref x.B.Cards[drawnPosition];
        FlashKernel.LoseEnergy(ref x, x.R.Card(drawn.Id, drawn.Upgrade).Vars[0]);
    }
}
