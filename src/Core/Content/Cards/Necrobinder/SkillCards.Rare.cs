using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Necrobinder;

/// <summary>
/// <c>EndOfDays</c>: apply Doom to every hittable enemy, then kill the doomed ones together
/// (<c>DoomPower.GetDoomedCreatures</c> + <c>DoomPower.DoomKill</c>; <c>CardOnPlaySupport.Batch042</c>).
/// </summary>
[BlitzContent("END_OF_DAYS")]
public sealed class EndOfDays : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        int amount = (int)NecrobinderContent.Var(ref x, 0);
        ushort doomId = BlitzRegistry.PowerId<Powers.DoomPower>();
        Span<byte> doomed = stackalloc byte[BlitzLimits.Enemies];
        int count = 0;
        // ApplyEndOfDays resolves power changes after each enemy; a Sleight of Flesh death compacts
        // the live roster, so visit the same snapshot the native loop sees.
        Span<byte> owners = stackalloc byte[BlitzLimits.Enemies];
        int rosterCount = BlitzKernel.RosterOwners(x.B, owners);
        for (int position = 0; position < rosterCount; position++)
        {
            byte enemy = owners[position];
            if (!BlitzKernel.IsHittable(x.B, enemy))
                continue;
            BlitzKernel.ApplyPower(ref x, enemy, doomId, amount, BlitzLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
            // ApplyEndOfDays drains the pending amount changes before its Doom threshold check,
            // so ShroudPower's block gain lands while the combat is still running.
            BlitzKernel.ResolvePowerAmountChanges(ref x);
            if (x.NeedsChoice)
                return;
            BlitzKernel.ApplyEnemyDeathPowers(ref x);
            if (x.NeedsChoice)
                return;
        }
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (!BlitzKernel.IsHittable(x.B, enemy))
                continue;
            int doom = BlitzKernel.GetAmount(x.B, enemy, doomId);
            if (doom > 0 && x.B.Creature(enemy).CurrentHp <= doom)
                doomed[count++] = enemy;
        }
        if (count > 0)
            BlitzKernel.DoomKill(ref x, doomed[..count]);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
    }
}

/// <summary>
/// <c>Sacrifice</c>: block for three times Osty's max HP, then kill the pet with force
/// (<c>BespokeCardMirrors.SacrificeOnPlay</c>). Nothing happens while Osty is missing.
/// </summary>
[BlitzContent("SACRIFICE")]
public sealed class Sacrifice : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        decimal block = x.B.Osty.MaxHp * 3;
        Span<byte> osty = stackalloc byte[1];
        osty[0] = BlitzLimits.OstyOwner;
        BlitzKernel.Kill(ref x, osty, force: true);
        if (x.NeedsChoice)
            return;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, block, BlitzValueProps.Move, fromCard: true);
    }
}

/// <summary>
/// <c>Seance</c>: choose Cards cards from the draw pile and transform each into <c>Soul</c>
/// (<c>CardChoiceSupport</c>'s Seance spec, <c>CardChoiceResolution</c>'s TransformSelectedCards).
/// The replacement is the level-0 canonical Soul; the upgrade only reduces Seance's cost.
/// </summary>
[BlitzContent("SEANCE")]
public sealed class Seance : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // TransformSelectedCards creates the level-0 canonical Soul; without this declaration the rule
    // table has no Soul stats and the first transform reports an unsupported kernel boundary.
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref BlitzExec x)
    {
        if (x.B.DrawCount == 0)
            return;
        if (!BlitzRegistry.TryCard("SOUL", out ushort soul))
            throw new BlitzUnsupportedException("Soul 没有 Blitz 定义。");
        int count = Math.Min((int)BlitzCardEffects.Var(x, 0), x.B.DrawCount);
        if (count <= 0)
            return;
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Transform, BlitzPile.Draw, options, count, selected,
                out int chosen))
        {
            return;
        }
        BlitzKernel.TransformCards(ref x, BlitzPile.Draw, selected[..chosen],
            BlitzKernel.CreateCard(x.R, soul), upgrade: 0);
    }
}

/// <summary><c>Reanimate</c>: summon Osty for the Summon variable (<c>CardOnPlaySupport.Batch043</c>).</summary>
[BlitzContent("REANIMATE")]
public sealed class Reanimate : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref BlitzExec x) => BlitzKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
}

/// <summary>
/// <c>SharedFate</c>: lose PlayerStrengthLoss Strength, then apply EnemyStrengthLoss to the target
/// (<c>CorePowerSupport</c>'s SharedFate case).
/// </summary>
[BlitzContent("SHARED_FATE")]
public sealed class SharedFate : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["EnemyStrengthLoss", "PlayerStrengthLoss"];

    public override void OnPlay(ref BlitzExec x)
    {
        ushort strength = BlitzRegistry.PowerId<Powers.StrengthPower>();
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, strength,
            -(int)NecrobinderContent.Var(ref x, 1), BlitzLimits.PlayerOwner);
        if (x.NeedsChoice || x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, strength, -(int)NecrobinderContent.Var(ref x, 0),
            BlitzLimits.PlayerOwner);
    }
}

/// <summary><c>Oblivion</c>: apply OblivionPower to the target for its Doom variable (<c>CardOnPlaySupport</c>).</summary>
[BlitzContent("OBLIVION")]
public sealed class Oblivion : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.OblivionPower>(),
            (int)NecrobinderContent.Var(ref x, 0), BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Undeath</c>: player block, then a clone of this card into the discard pile
/// (<c>CardEffectSpecRegistry</c>'s Undeath completion + <c>CardModel.CreateClone</c>).
/// </summary>
[BlitzContent("UNDEATH")]
public sealed class Undeath : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("UNDEATH", 0), ("UNDEATH", 1)];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzCard clone = BlitzKernel.CloneCard(x.Card);
        BlitzKernel.AddGeneratedCard(ref x, clone, BlitzPile.Discard);
    }
}

/// <summary>
/// <c>Transfigure</c>: add one this-combat cost to a selected hand card and increase its base replay
/// count (<c>CardChoiceResolution.ModifySelectedCard</c>).
/// </summary>
[BlitzContent("TRANSFIGURE")]
public sealed class Transfigure : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Modify, BlitzPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen == 0)
            return;
        ref BlitzCard card = ref BlitzPiles.At(ref x.B, BlitzPile.Hand, selected[0]);
        int cost = BlitzKernel.EnergyCostWithLocalModifiers(x.B, card);
        if ((card.Flags & BlitzCardFlags.CostsX) == 0 && cost >= 0)
        {
            BlitzKernel.AddEnergyCostModifier(ref card, 1, BlitzCostType.Relative, BlitzCostExpiration.EndOfCombat,
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
[BlitzContent("EIDOLON")]
public sealed class Eidolon : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        Span<int> positions = stackalloc int[x.B.ExhaustCount];
        int count = 0;
        for (int offset = 0; offset < x.B.ExhaustCount; offset++)
        {
            BlitzCard card = BlitzPiles.At(ref x.B, BlitzPile.Exhaust, offset);
            if (card.HasKeyword(BlitzKeywords.Ethereal) && !card.HasKeyword(BlitzKeywords.Unplayable))
                positions[count++] = offset;
        }
        for (int index = 0; index < count; index++)
        {
            int position = positions[index];
            if (!BlitzKernel.AutoPlay(ref x, BlitzPile.Exhaust, position))
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
