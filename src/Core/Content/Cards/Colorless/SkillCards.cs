using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Colorless;

[FlashContent("DARK_SHACKLES")]
public sealed class DarkShackles : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyTemporaryStrength(ref x, x.Target,
            FlashRegistry.PowerId<Powers.DarkShacklesPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner, sign: -1);
    }
}

[FlashContent("EQUILIBRIUM")]
public sealed class Equilibrium : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Equilibrium"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RetainHandPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("FINESSE")]
public sealed class Finesse : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[FlashContent("IMPATIENCE")]
public sealed class Impatience : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        // CardDrawCardMirrors.ImpatienceOnPlay: draw only while no Attack card is in hand. The
        // played card is already in the play pile, so the hand read here excludes it.
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            if (FlashKernel.CardType(x.B, x.R, card) == FlashCardType.Attack)
                return;
        }
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);
    }
}

[FlashContent("MASTER_OF_STRATEGY")]
public sealed class MasterOfStrategy : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);
}

[FlashContent("PANIC_BUTTON")]
public sealed class PanicButton : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Turns"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.NoBlockPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("PRODUCTION")]
public sealed class Production : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
}

[FlashContent("PROLONG")]
public sealed class Prolong : FlashCardDef
{
    // CardOnPlaySupport `case Prolong`: BlockNextTurnPower amount = the owner's current block.
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.BlockNextTurnPower>(),
            Math.Max(0, x.B.Player.Block), FlashLimits.PlayerOwner);
}

[FlashContent("PURITY")]
public sealed class Purity : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // CardChoiceSupport.GetSpec: RangeSpec(Exhaust, Hand, 0, Cards, every hand card); an empty hand
    // still asks an (empty) choice.
    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[options.Length];
        int max = (int)FlashCardEffects.Var(x, 0);
        if (!FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand, options,
                0, max, selected, out int chosen))
        {
            return;
        }
        FlashKernel.ExhaustSelected(ref x, FlashPile.Hand, selected[..chosen]);
    }
}

[FlashContent("RESTLESSNESS")]
public sealed class Restlessness : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        // Native IsOnlyCardInHand: this card is in the play pile during resolution, so the condition
        // is an empty hand. Each of the Cards draws is a separate native Draw(1) call.
        if (x.B.HandCount != 0)
            return;
        // CardDrawCardMirrors.RestlessnessOnPlay draws the whole Cards amount in one call.
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 1));
    }
}

[FlashContent("SCRAWL")]
public sealed class Scrawl : FlashCardDef
{
    // CardDrawCardMirrors.ScrawlOnPlay / native: draw up to the hand size limit.
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, Math.Max(0, x.B.MaxHandSize - x.B.HandCount), fromHandDraw: false);
}

[FlashContent("SECRET_TECHNIQUE")]
public sealed class SecretTechnique : FlashCardDef
{
    // GetSpec: MoveToHand, Draw, 1, the draw pile's Skill cards; no options asks nothing.
    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        int start = FlashPiles.Start(x.B, FlashPile.Draw);
        Span<int> options = stackalloc int[x.B.DrawCount];
        int count = 0;
        for (int index = 0; index < x.B.DrawCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[start + index];
            if (FlashKernel.CardType(x.B, x.R, card) == FlashCardType.Skill)
                options[count++] = index;
        }
        Span<int> selected = stackalloc int[Math.Max(1, count)];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Draw, options[..count],
                1, selected, out int chosen))
        {
            return;
        }
        FlashKernel.MoveSelected(ref x.B, FlashPile.Draw, selected[..chosen], FlashPile.Hand, FlashPilePosition.Bottom);
    }
}

[FlashContent("SECRET_WEAPON")]
public sealed class SecretWeapon : FlashCardDef
{
    // GetSpec: MoveToHand, Draw, 1, the draw pile's Attack cards; no options asks nothing.
    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        int start = FlashPiles.Start(x.B, FlashPile.Draw);
        Span<int> options = stackalloc int[x.B.DrawCount];
        int count = 0;
        for (int index = 0; index < x.B.DrawCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[start + index];
            if (FlashKernel.CardType(x.B, x.R, card) == FlashCardType.Attack)
                options[count++] = index;
        }
        Span<int> selected = stackalloc int[Math.Max(1, count)];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Draw, options[..count],
                1, selected, out int chosen))
        {
            return;
        }
        FlashKernel.MoveSelected(ref x.B, FlashPile.Draw, selected[..chosen], FlashPile.Hand, FlashPilePosition.Bottom);
    }
}

[FlashContent("SHOCKWAVE")]
public sealed class Shockwave : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref FlashExec x)
    {
        // CardOnPlaySupport `case Shockwave`: per hittable enemy in roster order, Weak then Vulnerable.
        int amount = (int)FlashCardEffects.Var(x, 0);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.WeakPower>(), amount, FlashLimits.PlayerOwner);
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.VulnerablePower>(), amount, FlashLimits.PlayerOwner);
        }
    }
}

[FlashContent("THE_GAMBIT")]
public sealed class TheGambit : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.TheGambitPower>(),
            1, FlashLimits.PlayerOwner);
    }
}

[FlashContent("THINKING_AHEAD")]
public sealed class ThinkingAhead : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: MoveToDrawTop, Hand, 1, every hand card.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[Math.Max(1, options.Length)];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToDrawTop, FlashPile.Hand, options,
                1, selected, out int chosen))
        {
            return;
        }
        FlashKernel.MoveSelected(ref x.B, FlashPile.Hand, selected[..chosen], FlashPile.Draw, FlashPilePosition.Top);
    }
}

[FlashContent("ULTIMATE_DEFEND")]
public sealed class UltimateDefend : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("ANOINTED")]
public sealed class Anointed : FlashCardDef
{
    // CardSelectionCardMirrors.AnointedOnPlay: hand-space many rare cards from the draw pile taken by
    // TakeRandom (a full UnstableShuffle over the rare list, then the first cards) and put into hand.
    public override void OnPlay(ref FlashExec x)
    {
        int space = x.B.MaxHandSize - x.B.HandCount;
        if (space <= 0 || x.B.DrawCount == 0)
            return;
        Span<int> rares = stackalloc int[x.B.DrawCount];
        int rareCount = 0;
        for (int index = 0; index < x.B.DrawCount; index++)
        {
            FlashCard card = FlashPiles.At(ref x.B, FlashPile.Draw, index);
            if (x.R.Card(card.Id, card.Upgrade).Rarity == FlashCardRarity.Rare)
                rares[rareCount++] = index;
        }
        if (rareCount == 0)
            return;
        rares = rares[..rareCount];
        ref FlashRngState rng = ref x.B.RngStream(FlashRngStream.CombatCardSelection);
        for (int n = rares.Length; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (rares[k], rares[n]) = (rares[n], rares[k]);
        }
        int take = Math.Min(space, rares.Length);
        FlashKernel.MoveSelected(ref x.B, FlashPile.Draw, rares[..take], FlashPile.Hand, FlashPilePosition.Bottom);
    }
}

[FlashContent("HIDDEN_GEM")]
public sealed class HiddenGem : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Replay"];

    // CardSelectionCardMirrors.HiddenGemOnPlay: CombatCardSelection.NextItem over the playable draw
    // cards (Attack/Skill/Power preferred, else the others), then BaseReplayCount += Replay.
    public override void OnPlay(ref FlashExec x)
    {
        if (x.B.DrawCount == 0)
            return;
        Span<int> eligible = stackalloc int[x.B.DrawCount];
        Span<int> preferred = stackalloc int[x.B.DrawCount];
        int eligibleCount = 0;
        int preferredCount = 0;
        for (int index = 0; index < x.B.DrawCount; index++)
        {
            FlashCard card = FlashPiles.At(ref x.B, FlashPile.Draw, index);
            FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if (card.HasKeyword(FlashKeywords.Unplayable)
                || stats.Type is FlashCardType.Curse or FlashCardType.Quest
                || FlashKernel.EnchantedReplayCount(card) >= 1)
                continue;
            eligible[eligibleCount++] = index;
            if (stats.Type is FlashCardType.Attack or FlashCardType.Skill or FlashCardType.Power)
                preferred[preferredCount++] = index;
        }
        ReadOnlySpan<int> pool = preferredCount > 0 ? preferred[..preferredCount] : eligible[..eligibleCount];
        if (pool.IsEmpty)
            return;
        int pick = pool[x.B.RngStream(FlashRngStream.CombatCardSelection).NextInt(0, pool.Length)];
        int replay = (int)FlashCardEffects.Var(x, 0);
        ref FlashCard chosen = ref FlashPiles.At(ref x.B, FlashPile.Draw, pick);
        int next = chosen.ReplayCount + replay;
        if ((uint)next > byte.MaxValue)
            throw new FlashCapacityException("Card.ReplayCount", byte.MaxValue);
        chosen.ReplayCount = (byte)next;
    }
}

[FlashContent("THE_BOMB")]
public sealed class TheBomb : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Turns", "BombDamage"];

    // CardOnPlaySupport.Batch043's TheBomb case: AddPowerInstance<TheBombPower>(Turns) then
    // SetDamage(BombDamage). Amount is the turn count; the damage lives in Aux0.
    public override void OnPlay(ref FlashExec x)
    {
        ushort id = FlashRegistry.PowerId<Powers.TheBombPower>();
        int turns = (int)FlashCardEffects.Var(x, 0);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, id, turns, FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int index = FlashKernel.FindLastPower(x.B, FlashLimits.PlayerOwner, id);
        if (index < 0)
            return;
        // SimulatedCombatState.AddPowerInstance sets _target = owner (unlike Apply<T>).
        x.B.Powers[index].Target = FlashLimits.PlayerOwner;
        x.B.Powers[index].Aux0 = (int)FlashCardEffects.Var(x, 1);
    }
}
