using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

/// <summary>
/// <c>LunarBlast</c>: CalculatedHits = CalculationBase + CalculationExtra × Skill cards played this
/// turn (<c>CalculatedVarSpecRegistry.LunarBlast</c> → <c>GetSkillCardsPlayedThisTurn</c>), used as the
/// general attack's hit count.
/// </summary>
[FlashContent("LUNAR_BLAST")]
public sealed class LunarBlast : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref FlashExec x)
    {
        int hits = (int)(FlashCardEffects.Var(x, 1)
            + FlashCardEffects.Var(x, 2) * x.B.Counter(FlashCounter.SkillCardsPlayedThisTurn));
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: hits);
    }
}

/// <summary>
/// <c>ParticleWall</c> (2 star cost): the general block, then CardResultLocationMirrors sends a discard
/// result to the hand instead.
/// </summary>
[FlashContent("PARTICLE_WALL")]
public sealed class ParticleWall : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override FlashPile ResultLocation(in FlashBattle battle, FlashRules rules, in FlashCard card,
        FlashPile baseLocation, out FlashPilePosition position)
    {
        position = FlashPilePosition.Bottom;
        return baseLocation == FlashPile.Discard ? FlashPile.Hand : baseLocation;
    }
}

/// <summary><c>Prophesize</c>: the general draw-card recipe with the Cards variable.</summary>
[FlashContent("PROPHESIZE")]
public sealed class Prophesize : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);
}

/// <summary>
/// <c>Resonance</c> (2 star cost): CorePowerSupport gains the Strength variable for the owner, then
/// applies -1 Strength to every hittable enemy in roster order.
/// </summary>
[FlashContent("RESONANCE")]
public sealed class Resonance : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref FlashExec x)
    {
        ushort strength = FlashRegistry.PowerId<Powers.StrengthPower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, strength, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            FlashKernel.ApplyPower(ref x, enemy, strength, -1, FlashLimits.PlayerOwner);
        }
    }
}

/// <summary><c>Terraforming</c>: CardPowerOnPlaySupport's late pass applies VigorPower.</summary>
[FlashContent("TERRAFORMING")]
public sealed class Terraforming : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["VigorPower"];

    public override IReadOnlyList<string> AppliedPowers => ["VIGOR_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.VigorPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>Glimmer</c>: draw the Cards variable, then choose one hand card and put it on top of the draw
/// pile.
/// </summary>
[FlashContent("GLIMMER")]
public sealed class Glimmer : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "PutBack"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToDrawTop, FlashPile.Hand, options, 1,
                selected, out int chosen))
        {
            return;
        }
        FlashKernel.MoveSelected(ref x.B, FlashPile.Hand, selected[..chosen], FlashPile.Draw,
            FlashPilePosition.Top);
    }
}

/// <summary>
/// <c>Charge</c>: choose the Cards number of draw-pile cards and transform each into the canonical
/// Minion Dive Bomb, upgraded when Charge is upgraded
/// (<c>CardChoiceResolution.TransformSelectedCards</c>).
/// </summary>
[FlashContent("CHARGE")]
public sealed class Charge : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("MINION_DIVE_BOMB", 0)];

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        int count = Math.Min((int)FlashCardEffects.Var(x, 0), x.B.DrawCount);
        if (count <= 0)
            return;
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Transform, FlashPile.Draw, options, count,
                selected, out int chosen))
        {
            return;
        }
        TransformSelected(ref x, FlashPile.Draw, selected[..chosen], "MINION_DIVE_BOMB");
    }

    internal static void TransformSelected(ref FlashExec x, FlashPile pile, scoped ReadOnlySpan<int> selected,
        string cardId)
    {
        if (selected.IsEmpty || FlashKernel.IsOverOrEnding(x.B))
            return;
        if (!FlashRegistry.TryCard(cardId, out ushort replacementId))
            throw new FlashUnsupportedException($"变化需要 {cardId} 的 Flash 定义。");
        FlashKernel.TransformSelectedTo(ref x, pile, selected, replacementId, x.Card.Upgrade > 0);
    }
}

/// <summary>
/// Native <c>Monologue</c>: apply an instanced MonologuePower and copy the card's Power variable into
/// that instance. Existing engine: <c>CardOnPlaySupport.Batch043</c>'s Monologue case.
/// </summary>
[FlashContent("MONOLOGUE")]
public sealed class Monologue : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Power"];

    public override IReadOnlyList<string> AppliedPowers => ["MONOLOGUE_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryPower("MONOLOGUE_POWER", out ushort power))
            throw new FlashUnsupportedException("Monologue 需要 MONOLOGUE_POWER 定义。");
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, 1, FlashLimits.PlayerOwner,
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int row = FlashKernel.FindLastPower(x.B, FlashLimits.PlayerOwner, power);
        if (row < 0)
            return;
        // Monologue.OnPlay sets DynamicVars.Strength after PowerCmd.Apply returns.
        x.B.Powers[row].Aux0 = (int)FlashCardEffects.Var(x, 0);
    }
}

/// <summary>
/// Native <c>SummonForth</c>: move every Sovereign Blade outside the hand to the hand bottom, then
/// forge. Existing engine: <c>CardPileOnPlaySupport.MoveSovereignBladesToHand</c> followed by
/// <c>CardOnPlaySupport</c>'s SummonForth forge.
/// </summary>
[FlashContent("SUMMON_FORTH")]
public sealed class SummonForth : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryCard(FlashKernel.SovereignBladeId, out ushort blade))
            throw new FlashUnsupportedException("SummonForth 需要 SOVEREIGN_BLADE 定义。");
        FlashPile[] piles = [FlashPile.Draw, FlashPile.Discard, FlashPile.Exhaust, FlashPile.Play];
        foreach (FlashPile pile in piles)
        {
            int index = 0;
            while (index < FlashPiles.Count(x.B, pile))
            {
                if (x.B.Cards[FlashPiles.Start(x.B, pile) + index].Id != blade)
                {
                    index++;
                    continue;
                }
                FlashCard card = FlashPiles.RemoveAt(ref x.B, pile, index);
                if (pile == FlashPile.Play && index < x.PlayPosition)
                    x.PlayPosition--;
                FlashPiles.Append(ref x.B, FlashPile.Hand, card);
            }
        }
        if (x.NeedsChoice)
            return;
        FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 0));
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

/// <summary><c>RoyalGamble</c>: CardOnPlaySupport's <c>RoyalGamble</c> case gains Stars.</summary>
[FlashContent("ROYAL_GAMBLE")]
public sealed class RoyalGamble : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 0));
}

/// <summary><c>Alignment</c> (2 star cost): CardOnPlaySupport's <c>Alignment</c> case gains Energy.</summary>
[FlashContent("ALIGNMENT")]
public sealed class Alignment : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
}

/// <summary>
/// <c>Convergence</c>: CardOnPlaySupport applies RetainHandPower 1, EnergyNextTurnPower (Energy) and
/// StarNextTurnPower (Stars), in that order.
/// </summary>
[FlashContent("CONVERGENCE")]
public sealed class Convergence : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Stars"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RetainHandPower>(),
            1, FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StarNextTurnPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}
