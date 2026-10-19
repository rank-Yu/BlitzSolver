namespace CombatSolver.Engine.InCombat.Flash.Content.Enchantments;

// Enchantments the existing engine implements. EnchantDamage*/EnchantBlock* are called by
// HookMirrors before every listener; OnPlay is the EnchantmentOnPlayMirrors registry (Adroit,
// Corrupted, Inky, Momentum, Sown, Swift); EnchantPlayCount is CardModel.GetEnchantedReplayCount.
// Keyword-only enchantments (Goopy's Exhaust, Steady's Retain, RoyallyApproved's Innate/Retain,
// Tezcatara's Ember Eternal, SoulsPower removing Exhaust) are baked into the imported card value.
//
// Constants that have no rule-table entry are copied from the decompiled canonical model with the
// source noted on the definition.

/// <summary><c>Sharp</c>: adds the enchantment amount to powered attacks (decompiled Sharp.cs).</summary>
[FlashContent("SHARP")]
public sealed class Sharp : FlashEnchantmentDef
{
    public override decimal EnchantDamageAdditive(in FlashCard card, decimal damage, FlashValueProps props)
    {
        if (!props.IsPoweredAttack())
            return 0m;
        return card.EnchantmentAmount;
    }
}

/// <summary><c>Instinct</c>: doubles powered attack damage (decompiled Instinct.cs).</summary>
[FlashContent("INSTINCT")]
public sealed class Instinct : FlashEnchantmentDef
{
    public override decimal EnchantDamageMultiplicative(in FlashCard card, decimal damage, FlashValueProps props)
    {
        if (!props.IsPoweredAttack())
            return 1m;
        return 2m;
    }
}

/// <summary>
/// <c>Corrupted</c>: 1.5x powered attack damage, and on play the owner loses 2 HP
/// (Unblockable | Unpowered | Move) with this card as the source (decompiled Corrupted.cs).
/// </summary>
[FlashContent("CORRUPTED")]
public sealed class Corrupted : FlashEnchantmentDef
{
    private const decimal SelfDamage = 2m;

    public override decimal EnchantDamageMultiplicative(in FlashCard card, decimal damage, FlashValueProps props)
        => props.IsPoweredAttack() ? 1.5m : 1m;

    public override void OnPlay(ref FlashExec x, in FlashCard card, byte target)
        => FlashCardEffects.DamageOwnerFromCard(ref x, SelfDamage,
            FlashValueProps.Unblockable | FlashValueProps.Unpowered | FlashValueProps.Move);
}

/// <summary>
/// <c>Momentum</c>: kept <c>_extraDamage</c> adds to powered attacks; OnPlay increments it by the
/// enchantment amount (decompiled Momentum.cs).
/// </summary>
[FlashContent("MOMENTUM")]
public sealed class Momentum : FlashEnchantmentDef
{
    public override decimal EnchantDamageAdditive(in FlashCard card, decimal damage, FlashValueProps props)
    {
        if (!props.IsPoweredAttack())
            return 0m;
        return card.EnchantmentPrivate;
    }

    public override void OnPlay(ref FlashExec x, in FlashCard card, byte target)
    {
        ref FlashCard played = ref FlashKernel.CurrentPlayCard(ref x);
        long extra = (long)played.EnchantmentPrivate + played.EnchantmentAmount;
        if (extra is < short.MinValue or > short.MaxValue)
            throw new FlashUnsupportedException($"Momentum 累计伤害 {extra} 超出 Flash 的短整型表示。");
        played.EnchantmentPrivate = (short)extra;
        FlashKernel.SyncCurrentPlayCard(ref x);
    }
}

/// <summary><c>Nimble</c>: adds the enchantment amount to block gain (decompiled Nimble.cs).</summary>
[FlashContent("NIMBLE")]
public sealed class Nimble : FlashEnchantmentDef
{
    public override decimal EnchantBlockAdditive(in FlashCard card, decimal block)
    {
        return card.EnchantmentAmount;
    }
}

/// <summary>
/// <c>Goopy</c>: block additive is amount − 1, and every play increments the amount
/// (decompiled Goopy.cs).
/// </summary>
[FlashContent("GOOPY")]
public sealed class Goopy : FlashEnchantmentDef
{
    public override decimal EnchantBlockAdditive(in FlashCard card, decimal block)
    {
        return card.EnchantmentAmount - 1;
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (play.Position != self.Index)
            return;
        ref FlashCard card = ref x.B.Cards[self.Index];
        if (card.EnchantmentAmount == short.MaxValue)
            throw new FlashUnsupportedException($"Goopy 层数 {card.EnchantmentAmount} 超出 Flash 的短整型表示。");
        card.EnchantmentAmount++;
    }
}

/// <summary>
/// <c>TezcatarasEmber</c>: adds the canonical 3 damage to powered attacks; the zero cost and Eternal
/// keyword come from OnEnchant and are already in the imported card value (decompiled
/// TezcatarasEmber.cs, CanonicalVars DamageVar(3)).
/// </summary>
[FlashContent("TEZCATARAS_EMBER")]
public sealed class TezcatarasEmber : FlashEnchantmentDef
{
    private const decimal BonusDamage = 3m;

    public override decimal EnchantDamageAdditive(in FlashCard card, decimal damage, FlashValueProps props)
    {
        if (!props.IsPoweredAttack())
            return 0m;
        return BonusDamage;
    }
}

/// <summary>
/// <c>Vigorous</c>: adds its amount to powered attacks while Normal, then disables itself after the
/// card is played (decompiled Vigorous.cs).
/// </summary>
[FlashContent("VIGOROUS")]
public sealed class Vigorous : FlashEnchantmentDef
{
    public override decimal EnchantDamageAdditive(in FlashCard card, decimal damage, FlashValueProps props)
        => card.EnchantmentStatus == 0 && props.IsPoweredAttack() ? card.EnchantmentAmount : 0m;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (play.Position != self.Index || x.B.Cards[self.Index].EnchantmentStatus != 0)
            return;
        x.B.Cards[self.Index].EnchantmentStatus = 1;
    }
}

/// <summary><c>Adroit</c>: OnPlay gains block equal to the enchantment amount (decompiled Adroit.cs).</summary>
[FlashContent("ADROIT")]
public sealed class Adroit : FlashEnchantmentDef
{
    public override void OnPlay(ref FlashExec x, in FlashCard card, byte target)
    {
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, card.EnchantmentAmount, FlashValueProps.Move, fromCard: true);
    }
}

/// <summary><c>Inky</c>: OnPlay applies Weak 1 (decompiled Inky.cs, PowerVar&lt;WeakPower&gt;(1)).</summary>
[FlashContent("INKY")]
public sealed class Inky : FlashEnchantmentDef
{
    private const int WeakAmount = 1;

    public override void OnPlay(ref FlashExec x, in FlashCard card, byte target)
    {
        // Inky.cs branches on the native CardModel.TargetType, which is dynamic (Shiv targets all
        // enemies while FanOfKnivesPower is present).
        FlashTargetType targetType = FlashRegistry.Card(card.Id).TargetType(x.B, x.R, card);
        ushort weak = FlashRegistry.PowerId<Powers.WeakPower>();
        if (targetType == FlashTargetType.AllEnemies)
        {
            for (int position = 0; position < x.B.RosterCount; position++)
            {
                byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
                if (!FlashKernel.IsHittable(x.B, owner))
                    continue;
                FlashKernel.ApplyPowerFromCard(ref x, owner, weak, WeakAmount, FlashLimits.PlayerOwner, card);
                if (x.NeedsChoice)
                    return;
            }
            return;
        }
        if (target != FlashLimits.NoOwner)
            FlashKernel.ApplyPowerFromCard(ref x, target, weak, WeakAmount, FlashLimits.PlayerOwner, card);
    }
}

/// <summary><c>Sown</c>: the first play gains the enchantment amount of energy and disables itself (decompiled Sown.cs).</summary>
[FlashContent("SOWN")]
public sealed class Sown : FlashEnchantmentDef
{
    public override void OnPlay(ref FlashExec x, in FlashCard card, byte target)
    {
        if (card.EnchantmentStatus != 0)
            return;
        ref FlashCard played = ref FlashKernel.CurrentPlayCard(ref x);
        played.EnchantmentStatus = 1;
        FlashKernel.SyncCurrentPlayCard(ref x);
        FlashKernel.GainEnergy(ref x, played.EnchantmentAmount);
    }
}

/// <summary><c>Swift</c>: the first play draws the enchantment amount and disables itself (decompiled Swift.cs).</summary>
[FlashContent("SWIFT")]
public sealed class Swift : FlashEnchantmentDef
{
    public override void OnPlay(ref FlashExec x, in FlashCard card, byte target)
    {
        if (card.EnchantmentStatus != 0)
            return;
        ref FlashCard played = ref FlashKernel.CurrentPlayCard(ref x);
        played.EnchantmentStatus = 1;
        FlashKernel.SyncCurrentPlayCard(ref x);
        FlashKernel.Draw(ref x, played.EnchantmentAmount, fromHandDraw: false);
    }
}

/// <summary>
/// <c>Glam</c>: the first play of the combat adds its canonical Times (1) replay, then disables
/// itself; <c>_usedThisCombat</c> is the card's enchantment private slot (decompiled Glam.cs).
/// </summary>
[FlashContent("GLAM")]
public sealed class Glam : FlashEnchantmentDef
{
    private const int Times = 1;

    public override int EnchantPlayCount(in FlashCard card, int replayCount)
        => card.EnchantmentPrivate == 0 ? replayCount + Times : replayCount;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (play.Position != self.Index)
            return;
        ref FlashCard card = ref x.B.Cards[self.Index];
        if (card.EnchantmentPrivate != 0)
            return;
        card.EnchantmentPrivate = 1;
        card.EnchantmentStatus = 1;
    }
}

/// <summary><c>Spiral</c>: adds its canonical Times (1) replay to Basic Strikes/Defends (decompiled Spiral.cs).</summary>
[FlashContent("SPIRAL")]
public sealed class Spiral : FlashEnchantmentDef
{
    private const int Times = 1;

    public override int EnchantPlayCount(in FlashCard card, int replayCount)
    {
        return replayCount + Times;
    }
}

/// <summary><c>Clone</c>: no combat behavior; admitted so a cloned deck card is not rejected (decompiled Clone.cs).</summary>
[FlashContent("CLONE")]
public sealed class Clone : FlashEnchantmentDef;

/// <summary><c>Steady</c>: Retain keyword only, already in the imported card value (decompiled Steady.cs).</summary>
[FlashContent("STEADY")]
public sealed class Steady : FlashEnchantmentDef;

/// <summary><c>RoyallyApproved</c>: Innate/Retain only, already in the imported card value (decompiled RoyallyApproved.cs).</summary>
[FlashContent("ROYALLY_APPROVED")]
public sealed class RoyallyApproved : FlashEnchantmentDef;

/// <summary><c>SoulsPower</c>: removes Exhaust at enchant time, already in the imported card value (decompiled SoulsPower.cs).</summary>
[FlashContent("SOULS_POWER")]
public sealed class SoulsPower : FlashEnchantmentDef;

/// <summary>
/// <c>Imbued</c>: on the first player turn, after the scheduled auto-plays, every card with this
/// enchantment is auto-played (<c>EnchantmentLifecycleSupport.TriggerAutoPrePlay</c>, vanilla
/// <c>AfterAutoPrePlayPhaseEntered</c>). The auto-play itself is kernel-side in
/// <c>FlashKernel.AutoPrePlay</c>; the definition only admits the enchantment. ShouldStartAtBottomOfDrawPile
/// is applied by the root setup before the snapshot, so the imported draw pile already reflects it.
/// </summary>
[FlashContent("IMBUED")]
public sealed class Imbued : FlashEnchantmentDef;

/// <summary>
/// <c>Slither</c>: when this card is drawn into the hand, its combat energy cost becomes a random
/// 0..3 (<c>CombatEnergyCosts.NextInt(4)</c>, <c>SetThisCombat</c> absolute; decompiled Slither.cs and
/// <c>AfterCardDrawnMirrors.HandleSlither</c>). The existing engine ignores the test-only override.
/// </summary>
[FlashContent("SLITHER")]
public sealed class Slither : FlashEnchantmentDef
{
    private const int CostRange = 4;

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        // HandleSlither matches the drawn object against the enchantment's card and requires it to be
        // in hand. The kernel collects listeners after the card moved, so drawnPosition is the card's
        // hand slot; a hook that moved it first makes this no longer the drawn card's slot (the same
        // position contract used by TakeIntrinsicDraw).
        if (self.Kind != FlashListenerKind.Enchantment || self.Index != drawnPosition
            || (uint)drawnPosition >= (uint)x.B.HandCount)
            return;
        int cost = x.B.RngStream(FlashRngStream.CombatEnergyCosts).NextInt(CostRange);
        FlashKernel.AddEnergyCostModifier(ref x.B.Cards[drawnPosition], cost, FlashCostType.Absolute,
            FlashCostExpiration.EndOfCombat, reduceOnly: false);
    }
}

/// <summary>
/// <c>SlumberingEssence</c>: at the player turn end, before the hand is flushed, the card gains a
/// <c>WhenPlayed</c> −1 energy cost modifier (vanilla <c>BeforeFlush</c>;
/// <c>EnchantmentLifecycleSupport.BeforeFlush</c>).
/// </summary>
[FlashContent("SLUMBERING_ESSENCE")]
public sealed class SlumberingEssence : FlashEnchantmentDef
{
    public override void BeforeFlush(ref FlashExec x, FlashListener self)
    {
        if ((uint)self.Index >= (uint)x.B.HandCount)
            return;
        FlashKernel.AddEnergyCostModifier(ref x.B.Cards[self.Index], -1, FlashCostType.Relative,
            FlashCostExpiration.WhenPlayed, reduceOnly: false);
    }
}

/// <summary>
/// <c>PerfectFit</c>: every non-initial shuffle that contains this card moves it to the front of the
/// merged list (vanilla <c>ModifyShuffleOrder</c>, <c>ModifyShuffleOrderMirrors.HandlePerfectFit</c>).
/// </summary>
[FlashContent("PERFECT_FIT")]
public sealed class PerfectFit : FlashEnchantmentDef
{
    public override void ModifyShuffleOrder(ref FlashExec x, FlashListener self, scoped Span<FlashCard> cards, int count,
        bool isInitialShuffle)
    {
        if (isInitialShuffle || (uint)self.Index >= (uint)x.B.CardCount)
            return;
        FlashCard own = x.B.Cards[self.Index];
        int found = -1;
        for (int index = 0; index < count; index++)
        {
            if (FlashCardEffects.Equal(cards[index], own))
            {
                found = index;
                break;
            }
        }
        if (found <= 0)
            return;
        FlashCard card = cards[found];
        for (int index = found; index > 0; index--)
            cards[index] = cards[index - 1];
        cards[0] = card;
    }
}
