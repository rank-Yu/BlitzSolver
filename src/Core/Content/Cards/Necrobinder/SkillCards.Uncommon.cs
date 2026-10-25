using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Necrobinder;

/// <summary>
/// <c>Deathbringer</c>: for each hittable enemy in roster order, apply Doom then Weak
/// (<c>CardOnPlaySupport.Apply</c>'s Deathbringer case).
/// </summary>
[BlitzContent("DEATHBRINGER")]
public sealed class Deathbringer : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower", "WeakPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        int doom = (int)NecrobinderContent.Var(ref x, 0);
        int weak = (int)NecrobinderContent.Var(ref x, 1);
        ushort doomId = BlitzRegistry.PowerId<Powers.DoomPower>();
        ushort weakId = BlitzRegistry.PowerId<Powers.WeakPower>();
        // Native ApplyAll enumerates a HittableEnemies snapshot; a Doom application can kill its target
        // (Sleight of Flesh) and compact the live roster, which must not skip later enemies.
        Span<byte> owners = stackalloc byte[BlitzLimits.Enemies];
        int count = x.B.RosterCount;
        for (int position = 0; position < count; position++)
            owners[position] = BlitzKernel.EnemyOwner(x.B.Roster[position]);
        for (int position = 0; position < count; position++)
        {
            byte enemy = owners[position];
            if (!BlitzKernel.IsHittable(x.B, enemy))
                continue;
            BlitzKernel.ApplyPower(ref x, enemy, doomId, doom, BlitzLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
            BlitzKernel.ApplyPower(ref x, enemy, weakId, weak, BlitzLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("PUTREFY")]
public sealed class Putrefy : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref BlitzExec x)
    {
        int amount = (int)NecrobinderContent.Var(ref x, 0);
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.WeakPower>(), 0);
        if (x.NeedsChoice || x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.VulnerablePower>(), amount,
            BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>NoEscape</c>: apply Doom equal to CalculationBase + CalculationExtra × floor(target Doom /
/// DoomThreshold) (<c>CalculatedVarSpecRegistry.NoEscape</c>).
/// </summary>
[BlitzContent("NO_ESCAPE")]
public sealed class NoEscape : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomThreshold", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        int doom = BlitzKernel.GetAmount(x.B, x.Target, BlitzRegistry.PowerId<Powers.DoomPower>());
        decimal multiplier = Math.Floor(doom / NecrobinderContent.Var(ref x, 0));
        int amount = (int)NecrobinderContent.Calculated(ref x, 1, 2, multiplier);
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.DoomPower>(), amount,
            BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>DeathsDoor</c>: gain block once, plus the Repeat variable's count when Doom was applied by the
/// player this turn (<c>CardEffectSpecRegistry</c>'s DeathsDoor case reads
/// <c>WasDoomAppliedThisTurn</c>).
/// </summary>
[BlitzContent("DEATHS_DOOR")]
public sealed class DeathsDoor : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Repeat"];

    public override void OnPlay(ref BlitzExec x)
    {
        int gains = 1;
        if (x.B.Counter(BlitzCounter.DoomAppliedThisTurn) > 0)
            gains += (int)NecrobinderContent.Var(ref x, 1);
        for (int index = 0; index < gains; index++)
        {
            BlitzCardEffects.GeneralBlock(ref x, 0);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>Delay</c>: gain <c>Block</c>, then apply EnergyNextTurnPower. Existing engine:
/// the inferred block recipe plus <c>CardEffectSpecRegistry</c>'s Delay energy application.
/// </summary>
[BlitzContent("DELAY")]
public sealed class Delay : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    public override IReadOnlyList<string> AppliedPowers => ["ENERGY_NEXT_TURN_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!BlitzRegistry.TryPower("ENERGY_NEXT_TURN_POWER", out ushort power))
            throw new BlitzUnsupportedException("Delay 需要 ENERGY_NEXT_TURN_POWER 定义。");
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, power, (int)NecrobinderContent.Var(ref x, 1),
            BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Spur</c>: summon Osty for Summon, then heal it for Heal
/// (<c>CardOnPlaySupport.Batch043</c>'s Spur case).
/// </summary>
[BlitzContent("SPUR")]
public sealed class Spur : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon", "Heal"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        BlitzKernel.HealOsty(ref x, (int)NecrobinderContent.Var(ref x, 1));
    }
}

/// <summary>
/// <c>Cleanse</c>: summon Osty, then exhaust one draw-pile card
/// (<c>CardOnPlaySupport.Batch043</c> plus <c>CardChoiceSupport</c>'s Cleanse Exhaust spec).
/// </summary>
[BlitzContent("CLEANSE")]
public sealed class Cleanse : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Exhaust, BlitzPile.Draw, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen > 0)
            BlitzKernel.Exhaust(ref x, BlitzPile.Draw, selected[0]);
    }
}

/// <summary>
/// <c>Dirge</c>: X summons plus X Soul cards added to the draw pile at random positions
/// (<c>CardOnPlaySupport.Batch043</c>'s ApplyDirge).
/// </summary>
[BlitzContent("DIRGE")]
public sealed class Dirge : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0), ("SOUL", 1)];

    public override void OnPlay(ref BlitzExec x)
    {
        int xValue = BlitzKernel.ResolveEnergyXValue(ref x);
        int summon = (int)NecrobinderContent.Var(ref x, 0);
        for (int index = 0; index < xValue; index++)
        {
            BlitzKernel.SummonOsty(ref x, summon);
            if (x.NeedsChoice)
                return;
        }
        bool upgraded = x.Card.Upgrade > 0;
        for (int index = 0; index < xValue; index++)
        {
            BlitzCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded);
            if (!BlitzKernel.AddGeneratedCard(ref x, soul, BlitzPile.Draw, BlitzPilePosition.Random))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>BorrowedTime</c>: gain Energy, then apply BorrowedTimePower for ExtraCost
/// (<c>CardOnPlaySupport</c>: energy first, then the power).
/// </summary>
[BlitzContent("BORROWED_TIME")]
public sealed class BorrowedTime : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "ExtraCost"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.GainEnergy(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.BorrowedTimePower>(),
            (int)NecrobinderContent.Var(ref x, 1), BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>EnfeeblingTouch</c>: apply a temporary Strength loss to the target
/// (<c>CardOnPlaySupport</c>'s ApplyTemporaryStrengthLoss).
/// </summary>
[BlitzContent("ENFEEBLING_TOUCH")]
public sealed class EnfeeblingTouch : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyTemporaryStrength(ref x, x.Target,
            BlitzRegistry.PowerId<Powers.EnfeeblingTouchPower>(), (int)NecrobinderContent.Var(ref x, 0),
            BlitzLimits.PlayerOwner, -1);
    }
}

/// <summary>
/// <c>Dredge</c>: move min(Cards, hand room) cards from the discard pile to hand
/// (<c>CardChoiceSupport</c>'s Dredge spec).
/// </summary>
[BlitzContent("DREDGE")]
public sealed class Dredge : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        int room = Math.Max(0, x.B.MaxHandSize - x.B.HandCount);
        int count = Math.Min((int)NecrobinderContent.Var(ref x, 0), room);
        if (count <= 0 || x.B.DiscardCount == 0)
            return;
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.MoveToHand, BlitzPile.Discard, options, count,
                selected, out int chosen))
        {
            return;
        }
        if (chosen > 0)
            BlitzKernel.MoveSelected(ref x.B, BlitzPile.Discard, selected[..chosen], BlitzPile.Hand,
                BlitzPilePosition.Bottom);
    }
}

/// <summary>
/// <c>CaptureSpirit</c>: unblockable unpowered attack damage with the card as source, then Souls at
/// random draw-pile positions (<c>CardOnPlaySupport.Batch042</c>'s CaptureSpirit case).
/// </summary>
[BlitzContent("CAPTURE_SPIRIT")]
public sealed class CaptureSpirit : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        Span<byte> target = stackalloc byte[1];
        target[0] = x.Target;
        BlitzKernel.Damage(ref x, target, NecrobinderContent.Var(ref x, 0),
            BlitzValueProps.Unblockable | BlitzValueProps.Unpowered | BlitzValueProps.Move,
            BlitzLimits.PlayerOwner, hasCardSource: true, x.Card);
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
/// <c>Melancholy</c>: player block; after any death that was not prevented, each Melancholy copy in a
/// combat pile loses its Energy variable from this combat's cost (<c>AfterDeathMirrors</c>).
/// </summary>
[BlitzContent("MELANCHOLY")]
public sealed class Melancholy : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    /// <summary>
    /// The run listeners include the deck copy of this card, but <c>AfterDeathMirrors.HandleMelancholy</c>
    /// resolves it with <c>State.FindCard(card)</c> and does nothing when the card is not in a combat pile.
    /// The deck copy therefore has no effect and need not exist in Blitz (only combat piles are imported).
    /// </summary>
    public override bool DeckCopyOutsideCombatIsInert => true;

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);

    public override void AfterDeath(ref BlitzExec x, BlitzListener self, byte creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || (uint)self.Index >= (uint)x.B.CardCount)
            return;
        ref BlitzCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        int energy = (int)x.R.Card(card.Id, card.Upgrade).Vars[1];
        if (energy > 0)
        {
            BlitzKernel.AddEnergyCostModifier(ref card, -energy, BlitzCostType.Relative,
                BlitzCostExpiration.EndOfCombat, reduceOnly: false);
        }
    }
}

/// <summary><c>Parse</c>: draw its Cards variable (the inferred owner-draw recipe).</summary>
[BlitzContent("PARSE")]
public sealed class Parse : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x) => BlitzKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 0), fromHandDraw: false);
}
