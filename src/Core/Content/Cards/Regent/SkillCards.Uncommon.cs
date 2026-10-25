using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary>
/// <c>LunarBlast</c>: CalculatedHits = CalculationBase + CalculationExtra × Skill cards played this
/// turn (<c>CalculatedVarSpecRegistry.LunarBlast</c> → <c>GetSkillCardsPlayedThisTurn</c>), used as the
/// general attack's hit count.
/// </summary>
[BlitzContent("LUNAR_BLAST")]
public sealed class LunarBlast : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref BlitzExec x)
    {
        int hits = (int)(BlitzCardEffects.Var(x, 1)
            + BlitzCardEffects.Var(x, 2) * x.B.Counter(BlitzCounter.SkillCardsPlayedThisTurn));
        BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: hits);
    }
}

/// <summary>
/// <c>ParticleWall</c> (2 star cost): the general block, then CardResultLocationMirrors sends a discard
/// result to the hand instead.
/// </summary>
[BlitzContent("PARTICLE_WALL")]
public sealed class ParticleWall : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);

    public override BlitzPile ResultLocation(in BlitzBattle battle, BlitzRules rules, in BlitzCard card,
        BlitzPile baseLocation, out BlitzPilePosition position)
    {
        position = BlitzPilePosition.Bottom;
        return baseLocation == BlitzPile.Discard ? BlitzPile.Hand : baseLocation;
    }
}

/// <summary><c>Prophesize</c>: the general draw-card recipe with the Cards variable.</summary>
[BlitzContent("PROPHESIZE")]
public sealed class Prophesize : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 0), fromHandDraw: false);
}

/// <summary>
/// <c>Resonance</c> (2 star cost): CorePowerSupport gains the Strength variable for the owner, then
/// applies -1 Strength to every hittable enemy in roster order.
/// </summary>
[BlitzContent("RESONANCE")]
public sealed class Resonance : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        ushort strength = BlitzRegistry.PowerId<Powers.StrengthPower>();
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, strength, (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (!BlitzKernel.IsHittable(x.B, enemy))
                continue;
            BlitzKernel.ApplyPower(ref x, enemy, strength, -1, BlitzLimits.PlayerOwner);
        }
    }
}

/// <summary><c>Terraforming</c>: CardPowerOnPlaySupport's late pass applies VigorPower.</summary>
[BlitzContent("TERRAFORMING")]
public sealed class Terraforming : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["VigorPower"];

    public override IReadOnlyList<string> AppliedPowers => ["VIGOR_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.VigorPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>Glimmer</c>: draw the Cards variable, then choose one hand card and put it on top of the draw
/// pile.
/// </summary>
[BlitzContent("GLIMMER")]
public sealed class Glimmer : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "PutBack"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.MoveToDrawTop, BlitzPile.Hand, options, 1,
                selected, out int chosen))
        {
            return;
        }
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Hand, selected[..chosen], BlitzPile.Draw,
            BlitzPilePosition.Top);
    }
}

/// <summary>
/// <c>Charge</c>: choose the Cards number of draw-pile cards and transform each into the canonical
/// Minion Dive Bomb, upgraded when Charge is upgraded
/// (<c>CardChoiceResolution.TransformSelectedCards</c>).
/// </summary>
[BlitzContent("CHARGE")]
public sealed class Charge : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("MINION_DIVE_BOMB", 0)];

    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        int count = Math.Min((int)BlitzCardEffects.Var(x, 0), x.B.DrawCount);
        if (count <= 0)
            return;
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Transform, BlitzPile.Draw, options, count,
                selected, out int chosen))
        {
            return;
        }
        TransformSelected(ref x, BlitzPile.Draw, selected[..chosen], "MINION_DIVE_BOMB");
    }

    internal static void TransformSelected(ref BlitzExec x, BlitzPile pile, scoped ReadOnlySpan<int> selected,
        string cardId)
    {
        if (selected.IsEmpty || BlitzKernel.IsOverOrEnding(x.B))
            return;
        if (!BlitzRegistry.TryCard(cardId, out ushort replacementId))
            throw new BlitzUnsupportedException($"变化需要 {cardId} 的 Blitz 定义。");
        BlitzKernel.TransformSelectedTo(ref x, pile, selected, replacementId, x.Card.Upgrade > 0);
    }
}

/// <summary>
/// Native <c>Monologue</c>: apply an instanced MonologuePower and copy the card's Power variable into
/// that instance. Existing engine: <c>CardOnPlaySupport.Batch043</c>'s Monologue case.
/// </summary>
[BlitzContent("MONOLOGUE")]
public sealed class Monologue : BlitzCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Power"];

    public override IReadOnlyList<string> AppliedPowers => ["MONOLOGUE_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!BlitzRegistry.TryPower("MONOLOGUE_POWER", out ushort power))
            throw new BlitzUnsupportedException("Monologue 需要 MONOLOGUE_POWER 定义。");
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, power, 1, BlitzLimits.PlayerOwner,
            BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int row = BlitzKernel.FindLastPower(x.B, BlitzLimits.PlayerOwner, power);
        if (row < 0)
            return;
        // Monologue.OnPlay sets DynamicVars.Strength after PowerCmd.Apply returns.
        x.B.Powers[row].Aux0 = (int)BlitzCardEffects.Var(x, 0);
    }
}

/// <summary>
/// Native <c>SummonForth</c>: move every Sovereign Blade outside the hand to the hand bottom, then
/// forge. Existing engine: <c>CardPileOnPlaySupport.MoveSovereignBladesToHand</c> followed by
/// <c>CardOnPlaySupport</c>'s SummonForth forge.
/// </summary>
[BlitzContent("SUMMON_FORTH")]
public sealed class SummonForth : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!BlitzRegistry.TryCard(BlitzKernel.SovereignBladeId, out ushort blade))
            throw new BlitzUnsupportedException("SummonForth 需要 SOVEREIGN_BLADE 定义。");
        BlitzPile[] piles = [BlitzPile.Draw, BlitzPile.Discard, BlitzPile.Exhaust, BlitzPile.Play];
        foreach (BlitzPile pile in piles)
        {
            int index = 0;
            while (index < BlitzPiles.Count(x.B, pile))
            {
                if (x.B.Cards[BlitzPiles.Start(x.B, pile) + index].Id != blade)
                {
                    index++;
                    continue;
                }
                BlitzCard card = BlitzPiles.RemoveAt(ref x.B, pile, index);
                if (pile == BlitzPile.Play && index < x.PlayPosition)
                    x.PlayPosition--;
                BlitzPiles.Append(ref x.B, BlitzPile.Hand, card);
            }
        }
        if (x.NeedsChoice)
            return;
        BlitzKernel.Forge(ref x, (int)BlitzCardEffects.Var(x, 0));
    }
}

/// <summary><c>Bulwark</c>: the inferred recipe gains Block, then CardEffectSpecRegistry forges.</summary>
[BlitzContent("BULWARK")]
public sealed class Bulwark : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Forge"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Forge(ref x, (int)BlitzCardEffects.Var(x, 1));
    }
}

/// <summary>
/// <c>Conqueror</c>: CardOnPlaySupport forges, then applies ConquerorPower 1 to the chosen enemy.
/// </summary>
[BlitzContent("CONQUEROR")]
public sealed class Conqueror : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!BlitzKernel.Forge(ref x, (int)BlitzCardEffects.Var(x, 0)))
            return;
        if (x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.ConquerorPower>(), 1,
            BlitzLimits.PlayerOwner);
    }
}

/// <summary><c>Reflect</c> (3 star cost): the inferred block, then CardEffectSpecRegistry's ReflectPower 1.</summary>
[BlitzContent("REFLECT")]
public sealed class Reflect : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<string> AppliedPowers => ["REFLECT_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.ReflectPower>(), 1,
            BlitzLimits.PlayerOwner);
    }
}

/// <summary><c>RoyalGamble</c>: CardOnPlaySupport's <c>RoyalGamble</c> case gains Stars.</summary>
[BlitzContent("ROYAL_GAMBLE")]
public sealed class RoyalGamble : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.GainStars(ref x, (int)BlitzCardEffects.Var(x, 0));
}

/// <summary><c>Alignment</c> (2 star cost): CardOnPlaySupport's <c>Alignment</c> case gains Energy.</summary>
[BlitzContent("ALIGNMENT")]
public sealed class Alignment : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 0));
}

/// <summary>
/// <c>Convergence</c>: CardOnPlaySupport applies RetainHandPower 1, EnergyNextTurnPower (Energy) and
/// StarNextTurnPower (Stars), in that order.
/// </summary>
[BlitzContent("CONVERGENCE")]
public sealed class Convergence : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Stars"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.RetainHandPower>(),
            1, BlitzLimits.PlayerOwner);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StarNextTurnPower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}
