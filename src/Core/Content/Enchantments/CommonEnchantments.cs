using BlitzSolver.Engine;

namespace BlitzSolver.Content.Enchantments;

/// <summary><c>Sharp</c>: adds the enchantment amount to powered attacks (decompiled Sharp.cs).</summary>
[BlitzContent("SHARP")]
public sealed class Sharp : BlitzEnchantmentDef
{
    public override decimal EnchantDamageAdditive(in BlitzCard card, decimal damage, BlitzValueProps props)
    {
        if (!props.IsPoweredAttack())
            return 0m;
        return card.EnchantmentAmount;
    }
}

/// <summary><c>Instinct</c>: doubles powered attack damage (decompiled Instinct.cs).</summary>
[BlitzContent("INSTINCT")]
public sealed class Instinct : BlitzEnchantmentDef
{
    public override decimal EnchantDamageMultiplicative(in BlitzCard card, decimal damage, BlitzValueProps props)
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
[BlitzContent("CORRUPTED")]
public sealed class Corrupted : BlitzEnchantmentDef
{
    private const decimal SelfDamage = 2m;

    public override decimal EnchantDamageMultiplicative(in BlitzCard card, decimal damage, BlitzValueProps props)
        => props.IsPoweredAttack() ? 1.5m : 1m;

    public override void OnPlay(ref BlitzExec x, in BlitzCard card, byte target)
        => BlitzCardEffects.DamageOwnerFromCard(ref x, SelfDamage,
            BlitzValueProps.Unblockable | BlitzValueProps.Unpowered | BlitzValueProps.Move);
}

/// <summary>
/// <c>Momentum</c>: kept <c>_extraDamage</c> adds to powered attacks; OnPlay increments it by the
/// enchantment amount (decompiled Momentum.cs).
/// </summary>
[BlitzContent("MOMENTUM")]
public sealed class Momentum : BlitzEnchantmentDef
{
    public override decimal EnchantDamageAdditive(in BlitzCard card, decimal damage, BlitzValueProps props)
    {
        if (!props.IsPoweredAttack())
            return 0m;
        return card.EnchantmentPrivate;
    }

    public override void OnPlay(ref BlitzExec x, in BlitzCard card, byte target)
    {
        ref BlitzCard played = ref BlitzKernel.CurrentPlayCard(ref x);
        long extra = (long)played.EnchantmentPrivate + played.EnchantmentAmount;
        if (extra is < short.MinValue or > short.MaxValue)
            throw new BlitzUnsupportedException($"Momentum 累计伤害 {extra} 超出 Blitz 的短整型表示。");
        played.EnchantmentPrivate = (short)extra;
        BlitzKernel.SyncCurrentPlayCard(ref x);
    }
}

/// <summary><c>Nimble</c>: adds the enchantment amount to block gain (decompiled Nimble.cs).</summary>
[BlitzContent("NIMBLE")]
public sealed class Nimble : BlitzEnchantmentDef
{
    public override decimal EnchantBlockAdditive(in BlitzCard card, decimal block)
    {
        return card.EnchantmentAmount;
    }
}

/// <summary>
/// <c>Goopy</c>: block additive is amount − 1, and every play increments the amount
/// (decompiled Goopy.cs).
/// </summary>
[BlitzContent("GOOPY")]
public sealed class Goopy : BlitzEnchantmentDef
{
    public override decimal EnchantBlockAdditive(in BlitzCard card, decimal block)
    {
        return card.EnchantmentAmount - 1;
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (play.Position != self.Index)
            return;
        ref BlitzCard card = ref x.B.Cards[self.Index];
        if (card.EnchantmentAmount == short.MaxValue)
            throw new BlitzUnsupportedException($"Goopy 层数 {card.EnchantmentAmount} 超出 Blitz 的短整型表示。");
        card.EnchantmentAmount++;
        if ((card.Flags & BlitzCardFlags.DeckCard) != 0)
        {
            // AfterCardPlayedMirrors.HandleGoopy records the long-term resource and growth reward
            // only for a card with a permanent deck version.
            x.B.AddCounter(BlitzCounter.LongTermResourceValue, 1);
            BlitzGrowthRewards.Add(ref x.B, BlitzGrowthSource.Goopy);
        }
    }
}

/// <summary>
/// <c>TezcatarasEmber</c>: adds the canonical 3 damage to powered attacks; the zero cost and Eternal
/// keyword come from OnEnchant and are already in the imported card value (decompiled
/// TezcatarasEmber.cs, CanonicalVars DamageVar(3)).
/// </summary>
[BlitzContent("TEZCATARAS_EMBER")]
public sealed class TezcatarasEmber : BlitzEnchantmentDef
{
    private const decimal BonusDamage = 3m;

    public override decimal EnchantDamageAdditive(in BlitzCard card, decimal damage, BlitzValueProps props)
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
[BlitzContent("VIGOROUS")]
public sealed class Vigorous : BlitzEnchantmentDef
{
    public override decimal EnchantDamageAdditive(in BlitzCard card, decimal damage, BlitzValueProps props)
        => card.EnchantmentStatus == 0 && props.IsPoweredAttack() ? card.EnchantmentAmount : 0m;

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (play.Position != self.Index || x.B.Cards[self.Index].EnchantmentStatus != 0)
            return;
        x.B.Cards[self.Index].EnchantmentStatus = 1;
    }
}

/// <summary><c>Adroit</c>: OnPlay gains block equal to the enchantment amount (decompiled Adroit.cs).</summary>
[BlitzContent("ADROIT")]
public sealed class Adroit : BlitzEnchantmentDef
{
    public override void OnPlay(ref BlitzExec x, in BlitzCard card, byte target)
    {
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, card.EnchantmentAmount, BlitzValueProps.Move, fromCard: true);
    }
}

/// <summary><c>Inky</c>: OnPlay applies Weak 1 (decompiled Inky.cs, PowerVar&lt;WeakPower&gt;(1)).</summary>
[BlitzContent("INKY")]
public sealed class Inky : BlitzEnchantmentDef
{
    private const int WeakAmount = 1;

    public override void OnPlay(ref BlitzExec x, in BlitzCard card, byte target)
    {
        // Inky.cs branches on the native CardModel.TargetType, which is dynamic (Shiv targets all
        // enemies while FanOfKnivesPower is present).
        BlitzTargetType targetType = BlitzRegistry.Card(card.Id).TargetType(x.B, x.R, card);
        ushort weak = BlitzRegistry.PowerId<Powers.WeakPower>();
        if (targetType == BlitzTargetType.AllEnemies)
        {
            for (int position = 0; position < x.B.RosterCount; position++)
            {
                byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
                if (!BlitzKernel.IsHittable(x.B, owner))
                    continue;
                BlitzKernel.ApplyPowerFromCard(ref x, owner, weak, WeakAmount, BlitzLimits.PlayerOwner, card);
                if (x.NeedsChoice)
                    return;
            }
            return;
        }
        if (target != BlitzLimits.NoOwner)
            BlitzKernel.ApplyPowerFromCard(ref x, target, weak, WeakAmount, BlitzLimits.PlayerOwner, card);
    }
}

/// <summary><c>Sown</c>: the first play gains the enchantment amount of energy and disables itself (decompiled Sown.cs).</summary>
[BlitzContent("SOWN")]
public sealed class Sown : BlitzEnchantmentDef
{
    public override void OnPlay(ref BlitzExec x, in BlitzCard card, byte target)
    {
        if (card.EnchantmentStatus != 0)
            return;
        ref BlitzCard played = ref BlitzKernel.CurrentPlayCard(ref x);
        played.EnchantmentStatus = 1;
        BlitzKernel.SyncCurrentPlayCard(ref x);
        BlitzKernel.GainEnergy(ref x, played.EnchantmentAmount);
    }
}

/// <summary><c>Swift</c>: the first play draws the enchantment amount and disables itself (decompiled Swift.cs).</summary>
[BlitzContent("SWIFT")]
public sealed class Swift : BlitzEnchantmentDef
{
    public override void OnPlay(ref BlitzExec x, in BlitzCard card, byte target)
    {
        if (card.EnchantmentStatus != 0)
            return;
        ref BlitzCard played = ref BlitzKernel.CurrentPlayCard(ref x);
        played.EnchantmentStatus = 1;
        BlitzKernel.SyncCurrentPlayCard(ref x);
        BlitzKernel.Draw(ref x, played.EnchantmentAmount, fromHandDraw: false);
    }
}

/// <summary>
/// <c>Glam</c>: the first play of the combat adds its canonical Times (1) replay, then disables
/// itself; <c>_usedThisCombat</c> is the card's enchantment private slot (decompiled Glam.cs).
/// </summary>
[BlitzContent("GLAM")]
public sealed class Glam : BlitzEnchantmentDef
{
    private const int Times = 1;

    public override int EnchantPlayCount(in BlitzCard card, int replayCount)
        => card.EnchantmentPrivate == 0 ? replayCount + Times : replayCount;

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (play.Position != self.Index)
            return;
        ref BlitzCard card = ref x.B.Cards[self.Index];
        if (card.EnchantmentPrivate != 0)
            return;
        card.EnchantmentPrivate = 1;
        card.EnchantmentStatus = 1;
    }
}

/// <summary><c>Spiral</c>: adds its canonical Times (1) replay to Basic Strikes/Defends (decompiled Spiral.cs).</summary>
[BlitzContent("SPIRAL")]
public sealed class Spiral : BlitzEnchantmentDef
{
    private const int Times = 1;

    public override int EnchantPlayCount(in BlitzCard card, int replayCount)
    {
        return replayCount + Times;
    }
}

/// <summary><c>Clone</c>: no combat behavior; admitted so a cloned deck card is not rejected (decompiled Clone.cs).</summary>
[BlitzContent("CLONE")]
public sealed class Clone : BlitzEnchantmentDef;

/// <summary><c>Steady</c>: Retain keyword only, already in the imported card value (decompiled Steady.cs).</summary>
[BlitzContent("STEADY")]
public sealed class Steady : BlitzEnchantmentDef;

/// <summary><c>RoyallyApproved</c>: Innate/Retain only, already in the imported card value (decompiled RoyallyApproved.cs).</summary>
[BlitzContent("ROYALLY_APPROVED")]
public sealed class RoyallyApproved : BlitzEnchantmentDef;

/// <summary><c>SoulsPower</c>: removes Exhaust at enchant time, already in the imported card value (decompiled SoulsPower.cs).</summary>
[BlitzContent("SOULS_POWER")]
public sealed class SoulsPower : BlitzEnchantmentDef;

/// <summary>
/// <c>Imbued</c>: on the first player turn, after the scheduled auto-plays, every card with this
/// enchantment is auto-played (<c>EnchantmentLifecycleSupport.TriggerAutoPrePlay</c>, vanilla
/// <c>AfterAutoPrePlayPhaseEntered</c>). The auto-play itself is kernel-side in
/// <c>BlitzKernel.AutoPrePlay</c>; the definition only admits the enchantment. ShouldStartAtBottomOfDrawPile
/// is applied by the root setup before the snapshot, so the imported draw pile already reflects it.
/// </summary>
[BlitzContent("IMBUED")]
public sealed class Imbued : BlitzEnchantmentDef;

/// <summary>
/// <c>Slither</c>: when this card is drawn into the hand, its combat energy cost becomes a random
/// 0..3 (<c>CombatEnergyCosts.NextInt(4)</c>, <c>SetThisCombat</c> absolute; decompiled Slither.cs and
/// <c>AfterCardDrawnMirrors.HandleSlither</c>). The existing engine ignores the test-only override.
/// </summary>
[BlitzContent("SLITHER")]
public sealed class Slither : BlitzEnchantmentDef
{
    private const int CostRange = 4;

    public override void AfterCardDrawn(ref BlitzExec x, BlitzListener self, int drawnPosition, bool fromHandDraw)
    {
        // HandleSlither matches the drawn object against the enchantment's card and requires it to be
        // in hand. The kernel collects listeners after the card moved, so drawnPosition is the card's
        // hand slot; a hook that moved it first makes this no longer the drawn card's slot (the same
        // position contract used by TakeIntrinsicDraw).
        if (self.Kind != BlitzListenerKind.Enchantment || self.Index != drawnPosition
            || (uint)drawnPosition >= (uint)x.B.HandCount)
            return;
        int cost = x.B.RngStream(BlitzRngStream.CombatEnergyCosts).NextInt(CostRange);
        BlitzKernel.AddEnergyCostModifier(ref x.B.Cards[drawnPosition], cost, BlitzCostType.Absolute,
            BlitzCostExpiration.EndOfCombat, reduceOnly: false);
    }
}

/// <summary>
/// <c>SlumberingEssence</c>: at the player turn end, before the hand is flushed, the card gains a
/// <c>WhenPlayed</c> −1 energy cost modifier (vanilla <c>BeforeFlush</c>;
/// <c>EnchantmentLifecycleSupport.BeforeFlush</c>).
/// </summary>
[BlitzContent("SLUMBERING_ESSENCE")]
public sealed class SlumberingEssence : BlitzEnchantmentDef
{
    public override void BeforeFlush(ref BlitzExec x, BlitzListener self)
    {
        if ((uint)self.Index >= (uint)x.B.HandCount)
            return;
        BlitzKernel.AddEnergyCostModifier(ref x.B.Cards[self.Index], -1, BlitzCostType.Relative,
            BlitzCostExpiration.WhenPlayed, reduceOnly: false);
    }
}

/// <summary>
/// <c>PerfectFit</c>: every non-initial shuffle that contains this card moves it to the front of the
/// merged list (vanilla <c>ModifyShuffleOrder</c>, <c>ModifyShuffleOrderMirrors.HandlePerfectFit</c>).
/// </summary>
[BlitzContent("PERFECT_FIT")]
public sealed class PerfectFit : BlitzEnchantmentDef
{
    public override void ModifyShuffleOrder(ref BlitzExec x, BlitzListener self, scoped Span<BlitzCard> cards, int count,
        bool isInitialShuffle)
    {
        if (isInitialShuffle || (uint)self.Index >= (uint)x.B.CardCount)
            return;
        BlitzCard own = x.B.Cards[self.Index];
        int found = -1;
        for (int index = 0; index < count; index++)
        {
            if (BlitzCardEffects.Equal(cards[index], own))
            {
                found = index;
                break;
            }
        }
        if (found <= 0)
            return;
        BlitzCard card = cards[found];
        for (int index = found; index > 0; index--)
            cards[index] = cards[index - 1];
        cards[0] = card;
    }
}
