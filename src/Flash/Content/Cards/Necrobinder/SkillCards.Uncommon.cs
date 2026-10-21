using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

/// <summary>
/// <c>Deathbringer</c>: for each hittable enemy in roster order, apply Doom then Weak
/// (<c>CardOnPlaySupport.Apply</c>'s Deathbringer case).
/// </summary>
[FlashContent("DEATHBRINGER")]
public sealed class Deathbringer : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        int doom = (int)NecrobinderContent.Var(ref x, 0);
        int weak = (int)NecrobinderContent.Var(ref x, 1);
        ushort doomId = FlashRegistry.PowerId<Powers.DoomPower>();
        ushort weakId = FlashRegistry.PowerId<Powers.WeakPower>();
        // Native ApplyAll enumerates a HittableEnemies snapshot; a Doom application can kill its target
        // (Sleight of Flesh) and compact the live roster, which must not skip later enemies.
        Span<byte> owners = stackalloc byte[FlashLimits.Enemies];
        int count = x.B.RosterCount;
        for (int position = 0; position < count; position++)
            owners[position] = FlashKernel.EnemyOwner(x.B.Roster[position]);
        for (int position = 0; position < count; position++)
        {
            byte enemy = owners[position];
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            FlashKernel.ApplyPower(ref x, enemy, doomId, doom, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
            FlashKernel.ApplyPower(ref x, enemy, weakId, weak, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("PUTREFY")]
public sealed class Putrefy : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref FlashExec x)
    {
        int amount = (int)NecrobinderContent.Var(ref x, 0);
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 0);
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.VulnerablePower>(), amount,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>NoEscape</c>: apply Doom equal to CalculationBase + CalculationExtra × floor(target Doom /
/// DoomThreshold) (<c>CalculatedVarSpecRegistry.NoEscape</c>).
/// </summary>
[FlashContent("NO_ESCAPE")]
public sealed class NoEscape : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomThreshold", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        int doom = FlashKernel.GetAmount(x.B, x.Target, FlashRegistry.PowerId<Powers.DoomPower>());
        decimal multiplier = Math.Floor(doom / NecrobinderContent.Var(ref x, 0));
        int amount = (int)NecrobinderContent.Calculated(ref x, 1, 2, multiplier);
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.DoomPower>(), amount,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>DeathsDoor</c>: gain block once, plus the Repeat variable's count when Doom was applied by the
/// player this turn (<c>CardEffectSpecRegistry</c>'s DeathsDoor case reads
/// <c>WasDoomAppliedThisTurn</c>).
/// </summary>
[FlashContent("DEATHS_DOOR")]
public sealed class DeathsDoor : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Repeat"];

    public override void OnPlay(ref FlashExec x)
    {
        int gains = 1;
        if (x.B.Counter(FlashCounter.DoomAppliedThisTurn) > 0)
            gains += (int)NecrobinderContent.Var(ref x, 1);
        for (int index = 0; index < gains; index++)
        {
            FlashCardEffects.GeneralBlock(ref x, 0);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>Delay</c>: gain <c>Block</c>, then apply EnergyNextTurnPower. Existing engine:
/// the inferred block recipe plus <c>CardEffectSpecRegistry</c>'s Delay energy application.
/// </summary>
[FlashContent("DELAY")]
public sealed class Delay : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    public override IReadOnlyList<string> AppliedPowers => ["ENERGY_NEXT_TURN_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryPower("ENERGY_NEXT_TURN_POWER", out ushort power))
            throw new FlashUnsupportedException("Delay 需要 ENERGY_NEXT_TURN_POWER 定义。");
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)NecrobinderContent.Var(ref x, 1),
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Spur</c>: summon Osty for Summon, then heal it for Heal
/// (<c>CardOnPlaySupport.Batch043</c>'s Spur case).
/// </summary>
[FlashContent("SPUR")]
public sealed class Spur : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon", "Heal"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        FlashKernel.HealOsty(ref x, (int)NecrobinderContent.Var(ref x, 1));
    }
}

/// <summary>
/// <c>Cleanse</c>: summon Osty, then exhaust one draw-pile card
/// (<c>CardOnPlaySupport.Batch043</c> plus <c>CardChoiceSupport</c>'s Cleanse Exhaust spec).
/// </summary>
[FlashContent("CLEANSE")]
public sealed class Cleanse : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
    }

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Exhaust, FlashPile.Draw, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen > 0)
            FlashKernel.Exhaust(ref x, FlashPile.Draw, selected[0]);
    }
}

/// <summary>
/// <c>Dirge</c>: X summons plus X Soul cards added to the draw pile at random positions
/// (<c>CardOnPlaySupport.Batch043</c>'s ApplyDirge).
/// </summary>
[FlashContent("DIRGE")]
public sealed class Dirge : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0), ("SOUL", 1)];

    public override void OnPlay(ref FlashExec x)
    {
        int xValue = FlashKernel.ResolveEnergyXValue(ref x);
        int summon = (int)NecrobinderContent.Var(ref x, 0);
        for (int index = 0; index < xValue; index++)
        {
            FlashKernel.SummonOsty(ref x, summon);
            if (x.NeedsChoice)
                return;
        }
        bool upgraded = x.Card.Upgrade > 0;
        for (int index = 0; index < xValue; index++)
        {
            FlashCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded);
            if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Draw, FlashPilePosition.Random))
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
[FlashContent("BORROWED_TIME")]
public sealed class BorrowedTime : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "ExtraCost"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.GainEnergy(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.BorrowedTimePower>(),
            (int)NecrobinderContent.Var(ref x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>EnfeeblingTouch</c>: apply a temporary Strength loss to the target
/// (<c>CardOnPlaySupport</c>'s ApplyTemporaryStrengthLoss).
/// </summary>
[FlashContent("ENFEEBLING_TOUCH")]
public sealed class EnfeeblingTouch : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyTemporaryStrength(ref x, x.Target,
            FlashRegistry.PowerId<Powers.EnfeeblingTouchPower>(), (int)NecrobinderContent.Var(ref x, 0),
            FlashLimits.PlayerOwner, -1);
    }
}

/// <summary>
/// <c>Dredge</c>: move min(Cards, hand room) cards from the discard pile to hand
/// (<c>CardChoiceSupport</c>'s Dredge spec).
/// </summary>
[FlashContent("DREDGE")]
public sealed class Dredge : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        int room = Math.Max(0, x.B.MaxHandSize - x.B.HandCount);
        int count = Math.Min((int)NecrobinderContent.Var(ref x, 0), room);
        if (count <= 0 || x.B.DiscardCount == 0)
            return;
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Discard, options, count,
                selected, out int chosen))
        {
            return;
        }
        if (chosen > 0)
            FlashKernel.MoveSelected(ref x.B, FlashPile.Discard, selected[..chosen], FlashPile.Hand,
                FlashPilePosition.Bottom);
    }
}

/// <summary>
/// <c>CaptureSpirit</c>: unblockable unpowered attack damage with the card as source, then Souls at
/// random draw-pile positions (<c>CardOnPlaySupport.Batch042</c>'s CaptureSpirit case).
/// </summary>
[FlashContent("CAPTURE_SPIRIT")]
public sealed class CaptureSpirit : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        Span<byte> target = stackalloc byte[1];
        target[0] = x.Target;
        FlashKernel.Damage(ref x, target, NecrobinderContent.Var(ref x, 0),
            FlashValueProps.Unblockable | FlashValueProps.Unpowered | FlashValueProps.Move,
            FlashLimits.PlayerOwner, hasCardSource: true, x.Card);
        if (x.NeedsChoice)
            return;
        int count = (int)NecrobinderContent.Var(ref x, 1);
        for (int index = 0; index < count; index++)
        {
            FlashCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded: false);
            if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Draw, FlashPilePosition.Random)
                || x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>Melancholy</c>: player block; after any death that was not prevented, each Melancholy copy in a
/// combat pile loses its Energy variable from this combat's cost (<c>AfterDeathMirrors</c>).
/// </summary>
[FlashContent("MELANCHOLY")]
public sealed class Melancholy : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    /// <summary>
    /// The run listeners include the deck copy of this card, but <c>AfterDeathMirrors.HandleMelancholy</c>
    /// resolves it with <c>State.FindCard(card)</c> and does nothing when the card is not in a combat pile.
    /// The deck copy therefore has no effect and need not exist in Flash (only combat piles are imported).
    /// </summary>
    public override bool DeckCopyOutsideCombatIsInert => true;

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override void AfterDeath(ref FlashExec x, FlashListener self, byte creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || (uint)self.Index >= (uint)x.B.CardCount)
            return;
        ref FlashCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        int energy = (int)x.R.Card(card.Id, card.Upgrade).Vars[1];
        if (energy > 0)
        {
            FlashKernel.AddEnergyCostModifier(ref card, -energy, FlashCostType.Relative,
                FlashCostExpiration.EndOfCombat, reduceOnly: false);
        }
    }
}

/// <summary><c>Parse</c>: draw its Cards variable (the inferred owner-draw recipe).</summary>
[FlashContent("PARSE")]
public sealed class Parse : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 0), fromHandDraw: false);
}
