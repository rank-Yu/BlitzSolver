using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Colorless;

[BlitzContent("DARK_SHACKLES")]
public sealed class DarkShackles : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyTemporaryStrength(ref x, x.Target,
            BlitzRegistry.PowerId<Powers.DarkShacklesPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner, sign: -1);
    }
}

[BlitzContent("EQUILIBRIUM")]
public sealed class Equilibrium : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Equilibrium"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.RetainHandPower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("FINESSE")]
public sealed class Finesse : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[BlitzContent("IMPATIENCE")]
public sealed class Impatience : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        // CardDrawCardMirrors.ImpatienceOnPlay: draw only while no Attack card is in hand. The
        // played card is already in the play pile, so the hand read here excludes it.
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[index];
            if (BlitzKernel.CardType(x.B, x.R, card) == BlitzCardType.Attack)
                return;
        }
        BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 0), fromHandDraw: false);
    }
}

[BlitzContent("MASTER_OF_STRATEGY")]
public sealed class MasterOfStrategy : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 0), fromHandDraw: false);
}

[BlitzContent("PANIC_BUTTON")]
public sealed class PanicButton : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Turns"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.NoBlockPower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("PRODUCTION")]
public sealed class Production : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 0));
}

[BlitzContent("PROLONG")]
public sealed class Prolong : BlitzCardDef
{
    // CardOnPlaySupport `case Prolong`: BlockNextTurnPower amount = the owner's current block.
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.BlockNextTurnPower>(),
            Math.Max(0, x.B.Player.Block), BlitzLimits.PlayerOwner);
}

[BlitzContent("PURITY")]
public sealed class Purity : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // CardChoiceSupport.GetSpec: RangeSpec(Exhaust, Hand, 0, Cards, every hand card); an empty hand
    // still asks an (empty) choice.
    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[options.Length];
        int max = (int)BlitzCardEffects.Var(x, 0);
        if (!BlitzKernel.ChooseCardsInRange(ref x, BlitzChoiceEffect.Exhaust, BlitzPile.Hand, options,
                0, max, selected, out int chosen))
        {
            return;
        }
        BlitzKernel.ExhaustSelected(ref x, BlitzPile.Hand, selected[..chosen]);
    }
}

[BlitzContent("RESTLESSNESS")]
public sealed class Restlessness : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Energy"];

    public override void OnPlay(ref BlitzExec x)
    {
        // Native IsOnlyCardInHand: this card is in the play pile during resolution, so the condition
        // is an empty hand. Each of the Cards draws is a separate native Draw(1) call.
        if (x.B.HandCount != 0)
            return;
        // CardDrawCardMirrors.RestlessnessOnPlay draws the whole Cards amount in one call.
        BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 0), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 1));
    }
}

[BlitzContent("SCRAWL")]
public sealed class Scrawl : BlitzCardDef
{
    // CardDrawCardMirrors.ScrawlOnPlay / native: draw up to the hand size limit.
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Draw(ref x, Math.Max(0, x.B.MaxHandSize - x.B.HandCount), fromHandDraw: false);
}

[BlitzContent("SECRET_TECHNIQUE")]
public sealed class SecretTechnique : BlitzCardDef
{
    // GetSpec: MoveToHand, Draw, 1, the draw pile's Skill cards; no options asks nothing.
    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        int start = BlitzPiles.Start(x.B, BlitzPile.Draw);
        Span<int> options = stackalloc int[x.B.DrawCount];
        int count = 0;
        for (int index = 0; index < x.B.DrawCount; index++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[start + index];
            if (BlitzKernel.CardType(x.B, x.R, card) == BlitzCardType.Skill)
                options[count++] = index;
        }
        Span<int> selected = stackalloc int[Math.Max(1, count)];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.MoveToHand, BlitzPile.Draw, options[..count],
                1, selected, out int chosen))
        {
            return;
        }
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Draw, selected[..chosen], BlitzPile.Hand, BlitzPilePosition.Bottom);
    }
}

[BlitzContent("SECRET_WEAPON")]
public sealed class SecretWeapon : BlitzCardDef
{
    // GetSpec: MoveToHand, Draw, 1, the draw pile's Attack cards; no options asks nothing.
    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        int start = BlitzPiles.Start(x.B, BlitzPile.Draw);
        Span<int> options = stackalloc int[x.B.DrawCount];
        int count = 0;
        for (int index = 0; index < x.B.DrawCount; index++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[start + index];
            if (BlitzKernel.CardType(x.B, x.R, card) == BlitzCardType.Attack)
                options[count++] = index;
        }
        Span<int> selected = stackalloc int[Math.Max(1, count)];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.MoveToHand, BlitzPile.Draw, options[..count],
                1, selected, out int chosen))
        {
            return;
        }
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Draw, selected[..chosen], BlitzPile.Hand, BlitzPilePosition.Bottom);
    }
}

[BlitzContent("SHOCKWAVE")]
public sealed class Shockwave : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref BlitzExec x)
    {
        // CardOnPlaySupport `case Shockwave`: per hittable enemy in roster order, Weak then Vulnerable.
        int amount = (int)BlitzCardEffects.Var(x, 0);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (!BlitzKernel.IsHittable(x.B, owner))
                continue;
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.WeakPower>(), amount, BlitzLimits.PlayerOwner);
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.VulnerablePower>(), amount, BlitzLimits.PlayerOwner);
        }
    }
}

[BlitzContent("THE_GAMBIT")]
public sealed class TheGambit : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.TheGambitPower>(),
            1, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("THINKING_AHEAD")]
public sealed class ThinkingAhead : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref BlitzExec x)
    {
        // GetSpec: MoveToDrawTop, Hand, 1, every hand card.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[Math.Max(1, options.Length)];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.MoveToDrawTop, BlitzPile.Hand, options,
                1, selected, out int chosen))
        {
            return;
        }
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Hand, selected[..chosen], BlitzPile.Draw, BlitzPilePosition.Top);
    }
}

[BlitzContent("ULTIMATE_DEFEND")]
public sealed class UltimateDefend : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.GeneralBlock(ref x, 0);
}

[BlitzContent("ANOINTED")]
public sealed class Anointed : BlitzCardDef
{
    // CardSelectionCardMirrors.AnointedOnPlay: hand-space many rare cards from the draw pile taken by
    // TakeRandom (a full UnstableShuffle over the rare list, then the first cards) and put into hand.
    public override void OnPlay(ref BlitzExec x)
    {
        int space = x.B.MaxHandSize - x.B.HandCount;
        if (space <= 0 || x.B.DrawCount == 0)
            return;
        Span<int> rares = stackalloc int[x.B.DrawCount];
        int rareCount = 0;
        for (int index = 0; index < x.B.DrawCount; index++)
        {
            BlitzCard card = BlitzPiles.At(ref x.B, BlitzPile.Draw, index);
            if (x.R.Card(card.Id, card.Upgrade).Rarity == BlitzCardRarity.Rare)
                rares[rareCount++] = index;
        }
        if (rareCount == 0)
            return;
        rares = rares[..rareCount];
        ref BlitzRngState rng = ref x.B.RngStream(BlitzRngStream.CombatCardSelection);
        for (int n = rares.Length; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (rares[k], rares[n]) = (rares[n], rares[k]);
        }
        int take = Math.Min(space, rares.Length);
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Draw, rares[..take], BlitzPile.Hand, BlitzPilePosition.Bottom);
    }
}

[BlitzContent("HIDDEN_GEM")]
public sealed class HiddenGem : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Replay"];

    // CardSelectionCardMirrors.HiddenGemOnPlay: CombatCardSelection.NextItem over the playable draw
    // cards (Attack/Skill/Power preferred, else the others), then BaseReplayCount += Replay.
    public override void OnPlay(ref BlitzExec x)
    {
        if (x.B.DrawCount == 0)
            return;
        Span<int> eligible = stackalloc int[x.B.DrawCount];
        Span<int> preferred = stackalloc int[x.B.DrawCount];
        int eligibleCount = 0;
        int preferredCount = 0;
        for (int index = 0; index < x.B.DrawCount; index++)
        {
            BlitzCard card = BlitzPiles.At(ref x.B, BlitzPile.Draw, index);
            BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if (card.HasKeyword(BlitzKeywords.Unplayable)
                || stats.Type is BlitzCardType.Curse or BlitzCardType.Quest
                || BlitzKernel.EnchantedReplayCount(card) >= 1)
                continue;
            eligible[eligibleCount++] = index;
            if (stats.Type is BlitzCardType.Attack or BlitzCardType.Skill or BlitzCardType.Power)
                preferred[preferredCount++] = index;
        }
        ReadOnlySpan<int> pool = preferredCount > 0 ? preferred[..preferredCount] : eligible[..eligibleCount];
        if (pool.IsEmpty)
            return;
        int pick = pool[x.B.RngStream(BlitzRngStream.CombatCardSelection).NextInt(0, pool.Length)];
        int replay = (int)BlitzCardEffects.Var(x, 0);
        ref BlitzCard chosen = ref BlitzPiles.At(ref x.B, BlitzPile.Draw, pick);
        int next = chosen.ReplayCount + replay;
        if ((uint)next > byte.MaxValue)
            throw new BlitzCapacityException("Card.ReplayCount", byte.MaxValue);
        chosen.ReplayCount = (byte)next;
    }
}

[BlitzContent("THE_BOMB")]
public sealed class TheBomb : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Turns", "BombDamage"];

    // CardOnPlaySupport.Batch043's TheBomb case: AddPowerInstance<TheBombPower>(Turns) then
    // SetDamage(BombDamage). Amount is the turn count; the damage lives in Aux0.
    public override void OnPlay(ref BlitzExec x)
    {
        ushort id = BlitzRegistry.PowerId<Powers.TheBombPower>();
        int turns = (int)BlitzCardEffects.Var(x, 0);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, id, turns, BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int index = BlitzKernel.FindLastPower(x.B, BlitzLimits.PlayerOwner, id);
        if (index < 0)
            return;
        // SimulatedCombatState.AddPowerInstance sets _target = owner (unlike Apply<T>).
        x.B.Powers[index].Target = BlitzLimits.PlayerOwner;
        x.B.Powers[index].Aux0 = (int)BlitzCardEffects.Var(x, 1);
    }
}
