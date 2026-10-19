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
                FlashKernel.ApplyPower(ref x, owner, weak, WeakAmount, FlashLimits.PlayerOwner);
                if (x.NeedsChoice)
                    return;
            }
            return;
        }
        if (target != FlashLimits.NoOwner)
            FlashKernel.ApplyPower(ref x, target, weak, WeakAmount, FlashLimits.PlayerOwner);
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
