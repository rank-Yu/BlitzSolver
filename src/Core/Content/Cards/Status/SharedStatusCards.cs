using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Status;

/// <summary>
/// Native <c>Beckon</c>: cost 1, playable with no OnPlay effect; at the player's turn end it loses
/// 6 HP (Unblockable|Unpowered|Move) with the card as source.
/// </summary>
[BlitzContent("BECKON")]
public sealed class Beckon : BlitzCardDef
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

/// <summary>
/// Native <c>Burn</c>: cost -1, Unplayable; at the player's turn end it takes 2 unpowered,
/// blockable damage with the card as source.
/// </summary>
[BlitzContent("BURN")]
public sealed class Burn : BlitzCardDef
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

/// <summary>Native <c>Debris</c>: cost 1, Exhaust; its OnPlay is an explicit no-op.</summary>
[BlitzContent("DEBRIS")]
public sealed class Debris : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>
/// Native <c>Infection</c>: cost -1, Unplayable; at the player's turn end it takes 3 unpowered,
/// blockable damage with the card as source.
/// </summary>
[BlitzContent("INFECTION")]
public sealed class Infection : BlitzCardDef
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

/// <summary>Native <c>Soot</c>: cost -1, Unplayable; no combat hook is overridden.</summary>
[BlitzContent("SOOT")]
public sealed class Soot : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>
/// Native <c>Toxic</c>: cost 1, Exhaust; playable but its OnPlay has no effect. At the player's
/// turn end it takes 5 unpowered, blockable damage with the card as source.
/// </summary>
[BlitzContent("TOXIC")]
public sealed class Toxic : BlitzCardDef
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
/// Native <c>Wither</c>: cost -1, Unplayable; at the player's turn end it takes 3 unpowered,
/// blockable damage with the card as source. Aeonglass upgrades the Damage variable through a
/// private counter that admission rejects, so the rule table value is the whole supported state.
/// </summary>
[BlitzContent("WITHER")]
public sealed class Wither : BlitzCardDef
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

/// <summary>Native <c>Wound</c>: cost -1, Unplayable; no combat hook is overridden.</summary>
[BlitzContent("WOUND")]
public sealed class Wound : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

/// <summary>
/// Native <c>Void</c>: cost -1, Unplayable, Ethereal; when drawn it loses its Energy
/// (<c>AfterCardDrawnMirrors.HandleVoid</c>, claimed once with
/// <c>BlitzKernel.TakeIntrinsicDraw</c>). The Energy variable comes from the card drawn at
/// <paramref name="drawnPosition"/>; the definition touches no other card state.
/// </summary>
[BlitzContent("VOID")]
public sealed class Void : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void AfterCardDrawn(ref BlitzExec x, BlitzListener self, int drawnPosition, bool fromHandDraw)
    {
        if (!BlitzKernel.TakeIntrinsicDraw(ref x, self, drawnPosition))
            return;
        ref readonly BlitzCard drawn = ref x.B.Cards[drawnPosition];
        BlitzKernel.LoseEnergy(ref x, x.R.Card(drawn.Id, drawn.Upgrade).Vars[0]);
    }
}

/// <summary>
/// Native <c>FranticEscape</c> (TheInsatiable's LIQUIFY_GROUND): when played, increments the Sandpit
/// targeting counter of its owner and adds one to its own combat energy cost
/// (<c>P/CardOnPlaySupport.Batch042.cs:64</c>, the existing engine's compensation; its native
/// damage/effect hooks are otherwise out of scope). The played card is the last entry of the play
/// pile while OnPlay runs, matching UpMySleeve's handling.
/// </summary>
[BlitzContent("FRANTIC_ESCAPE")]
public sealed class FranticEscape : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.IncrementSandpitTargeting(ref x, BlitzLimits.PlayerOwner);
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("夺路而逃没有正在打出的卡。");
        BlitzKernel.AddEnergyCostModifier(ref BlitzPiles.At(ref x.B, BlitzPile.Play, position), 1,
            BlitzCostType.Relative, BlitzCostExpiration.EndOfCombat, reduceOnly: false);
    }
}
