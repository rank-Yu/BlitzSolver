using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

/// <summary>
/// <c>EndOfDays</c>: apply Doom to every hittable enemy, then kill the doomed ones together
/// (<c>DoomPower.GetDoomedCreatures</c> + <c>DoomPower.DoomKill</c>; <c>CardOnPlaySupport.Batch042</c>).
/// </summary>
[FlashContent("END_OF_DAYS")]
public sealed class EndOfDays : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower"];

    public override void OnPlay(ref FlashExec x)
    {
        int amount = (int)NecrobinderContent.Var(ref x, 0);
        ushort doomId = FlashRegistry.PowerId<Powers.DoomPower>();
        Span<byte> doomed = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        // ApplyEndOfDays resolves power changes after each enemy; a Sleight of Flesh death compacts
        // the live roster, so visit the same snapshot the native loop sees.
        Span<byte> owners = stackalloc byte[FlashLimits.Enemies];
        int rosterCount = FlashKernel.RosterOwners(x.B, owners);
        for (int position = 0; position < rosterCount; position++)
        {
            byte enemy = owners[position];
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            FlashKernel.ApplyPower(ref x, enemy, doomId, amount, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
            // ApplyEndOfDays drains the pending amount changes before its Doom threshold check,
            // so ShroudPower's block gain lands while the combat is still running.
            FlashKernel.ResolvePowerAmountChanges(ref x);
            if (x.NeedsChoice)
                return;
            FlashKernel.ApplyEnemyDeathPowers(ref x);
            if (x.NeedsChoice)
                return;
        }
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            int doom = FlashKernel.GetAmount(x.B, enemy, doomId);
            if (doom > 0 && x.B.Creature(enemy).CurrentHp <= doom)
                doomed[count++] = enemy;
        }
        if (count > 0)
            FlashKernel.DoomKill(ref x, doomed[..count]);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyEnemyDeathPowers(ref x);
    }
}

/// <summary>
/// <c>Sacrifice</c>: block for three times Osty's max HP, then kill the pet with force
/// (<c>BespokeCardMirrors.SacrificeOnPlay</c>). Nothing happens while Osty is missing.
/// </summary>
[FlashContent("SACRIFICE")]
public sealed class Sacrifice : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        decimal block = x.B.Osty.MaxHp * 3;
        Span<byte> osty = stackalloc byte[1];
        osty[0] = FlashLimits.OstyOwner;
        FlashKernel.Kill(ref x, osty, force: true);
        if (x.NeedsChoice)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, block, FlashValueProps.Move, fromCard: true);
    }
}

/// <summary>
/// <c>Seance</c>: choose Cards cards from the draw pile and transform each into <c>Soul</c>
/// (<c>CardChoiceSupport</c>'s Seance spec, <c>CardChoiceResolution</c>'s TransformSelectedCards).
/// The replacement is the level-0 canonical Soul; the upgrade only reduces Seance's cost.
/// </summary>
[FlashContent("SEANCE")]
public sealed class Seance : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // TransformSelectedCards creates the level-0 canonical Soul; without this declaration the rule
    // table has no Soul stats and the first transform reports an unsupported kernel boundary.
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.B.DrawCount == 0)
            return;
        if (!FlashRegistry.TryCard("SOUL", out ushort soul))
            throw new FlashUnsupportedException("Soul 没有 Flash 定义。");
        int count = Math.Min((int)FlashCardEffects.Var(x, 0), x.B.DrawCount);
        if (count <= 0)
            return;
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Transform, FlashPile.Draw, options, count, selected,
                out int chosen))
        {
            return;
        }
        FlashKernel.TransformCards(ref x, FlashPile.Draw, selected[..chosen],
            FlashKernel.CreateCard(x.R, soul), upgrade: 0);
    }
}

/// <summary><c>Reanimate</c>: summon Osty for the Summon variable (<c>CardOnPlaySupport.Batch043</c>).</summary>
[FlashContent("REANIMATE")]
public sealed class Reanimate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
}

/// <summary>
/// <c>SharedFate</c>: lose PlayerStrengthLoss Strength, then apply EnemyStrengthLoss to the target
/// (<c>CorePowerSupport</c>'s SharedFate case).
/// </summary>
[FlashContent("SHARED_FATE")]
public sealed class SharedFate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["EnemyStrengthLoss", "PlayerStrengthLoss"];

    public override void OnPlay(ref FlashExec x)
    {
        ushort strength = FlashRegistry.PowerId<Powers.StrengthPower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, strength,
            -(int)NecrobinderContent.Var(ref x, 1), FlashLimits.PlayerOwner);
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, strength, -(int)NecrobinderContent.Var(ref x, 0),
            FlashLimits.PlayerOwner);
    }
}

/// <summary><c>Oblivion</c>: apply OblivionPower to the target for its Doom variable (<c>CardOnPlaySupport</c>).</summary>
[FlashContent("OBLIVION")]
public sealed class Oblivion : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.OblivionPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Undeath</c>: player block, then a clone of this card into the discard pile
/// (<c>CardEffectSpecRegistry</c>'s Undeath completion + <c>CardModel.CreateClone</c>).
/// </summary>
[FlashContent("UNDEATH")]
public sealed class Undeath : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("UNDEATH", 0), ("UNDEATH", 1)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCard clone = FlashKernel.CloneCard(x.Card);
        FlashKernel.AddGeneratedCard(ref x, clone, FlashPile.Discard);
    }
}

/// <summary>
/// <c>Transfigure</c>: add one this-combat cost to a selected hand card and increase its base replay
/// count (<c>CardChoiceResolution.ModifySelectedCard</c>).
/// </summary>
[FlashContent("TRANSFIGURE")]
public sealed class Transfigure : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Modify, FlashPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen == 0)
            return;
        ref FlashCard card = ref FlashPiles.At(ref x.B, FlashPile.Hand, selected[0]);
        int cost = FlashKernel.EnergyCostWithLocalModifiers(x.B, card);
        if ((card.Flags & FlashCardFlags.CostsX) == 0 && cost >= 0)
        {
            FlashKernel.AddEnergyCostModifier(ref card, 1, FlashCostType.Relative, FlashCostExpiration.EndOfCombat,
                reduceOnly: false);
        }
        card.ReplayCount = checked((byte)(card.ReplayCount + 1));
    }
}

/// <summary>
/// <c>Eidolon</c>: auto-play every Ethereal, playable card in the exhaust pile in pile order
/// (<c>CardOnPlaySupport.Batch043</c>'s ApplyEidolon). The snapshot follows the existing engine's
/// <c>ExhaustPile.Cards.Where(...).ToArray()</c>: positions are taken before the loop and shifted only by
/// the removals of earlier plays, so a card that exhausts back into the pile is not played again.
/// </summary>
[FlashContent("EIDOLON")]
public sealed class Eidolon : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        Span<int> positions = stackalloc int[x.B.ExhaustCount];
        int count = 0;
        for (int offset = 0; offset < x.B.ExhaustCount; offset++)
        {
            FlashCard card = FlashPiles.At(ref x.B, FlashPile.Exhaust, offset);
            if (card.HasKeyword(FlashKeywords.Ethereal) && !card.HasKeyword(FlashKeywords.Unplayable))
                positions[count++] = offset;
        }
        for (int index = 0; index < count; index++)
        {
            int position = positions[index];
            if (!FlashKernel.AutoPlay(ref x, FlashPile.Exhaust, position))
                return;
            if (x.NeedsChoice)
                return;
            for (int later = index + 1; later < count; later++)
            {
                if (positions[later] > position)
                    positions[later]--;
            }
        }
    }
}
