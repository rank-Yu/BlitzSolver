namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Colorless-pool powers whose native hook is dispatched by the kernel. Existing engine paths:
// - DarkShacklesPower is a native TemporaryStrengthPower applied through
//   SimulatedCombatState.ApplyTemporaryStrengthLoss and retired by EndTurnPowerSupport.TriggerRegular
//   (src/Prediction/CardOnPlaySupport.cs, src/Search/SimulatedCombatState.cs).
// - FastenPower.ModifyBlockAdditive is invoked by HookMirrors.ModifyBlock through the native listener
//   method (src/Engine/InCombat/Mirrors/HookMirrors.cs:53); its AfterModifyingBlockAmount flash is
//   presentation-only (AfterModifyingBlockAmountMirrors registers it as ignored).
// - NoBlockPower.ModifyBlockMultiplicative is invoked through the same native fallback
//   (ModifyBlockMultiplicativeMirrors.Invoke) and its duration ticks at the enemy side end in
//   CorePowerSupport.TickDurations.
// - PrepTimePower is the PREP_TIME_POWER case of PersistentPowerSupport.TriggerOwnerAfterSideTurnStart
//   (src/Prediction/PersistentPowerSupport.cs:213).

/// <summary>
/// Native <c>DarkShacklesPower</c> (a negative <c>TemporaryStrengthPower</c>): the application path
/// also changes Strength by -amount (DarkShackles card), and the power is removed at its owner's side
/// turn end while restoring that Strength.
/// </summary>
[FlashContent("DARK_SHACKLES_POWER")]
public sealed class DarkShacklesPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override string? InternallyAppliedPowerId => "STRENGTH_POWER";

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsTemporaryPower => true;

    // EndTurnPowerSupport.TriggerRegular: case TemporaryStrengthPower when the owner participates.
    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.RetireTemporaryStrength(ref x, self);
    }
}

/// <summary>
/// Native <c>FastenPower</c>: additive block for its owner's powered card/monster-move block, and
/// only for card sources tagged Defend. The native <c>AfterModifyingBlockAmount</c> body is a visual
/// flash, which the existing engine registers as ignored.
/// </summary>
[FlashContent("FASTEN_POWER")]
public sealed class FastenPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyBlockAdditive(ref FlashExec x, FlashListener self, in FlashBlockContext block, decimal amount)
    {
        if (block.Target != self.Owner)
            return 0m;
        if ((block.Props & FlashValueProps.Move) == 0 || (block.Props & FlashValueProps.Unpowered) != 0)
            return 0m;
        if (block.HasCardSource
            && (x.R.Card(block.CardSource.Id, block.CardSource.Upgrade).Tags & FlashCardTag.Defend) == 0)
        {
            return 0m;
        }
        return x.B.Powers[self.Index].Amount;
    }
}

/// <summary>
/// Native <c>NoBlockPower</c>: powered block from a card source becomes zero for its owner, and the
/// power loses one stack at the end of the enemy side (CorePowerSupport.TickDurations).
/// </summary>
[FlashContent("NO_BLOCK_POWER")]
public sealed class NoBlockPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool TicksAtEnemySideEnd => true;

    public override decimal ModifyBlockMultiplicative(ref FlashExec x, FlashListener self, in FlashBlockContext block, decimal amount)
    {
        if (block.Target != self.Owner)
            return 1m;
        if ((block.Props & FlashValueProps.Unpowered) != 0)
            return 1m;
        if (!block.HasCardSource)
            return 1m;
        return 0m;
    }
}

/// <summary>
/// Native <c>PrepTimePower</c>: at its owner's side turn start it applies <c>Amount</c> Vigor to the
/// owner. The existing engine resolves it in PersistentPowerSupport.TriggerOwnerAfterSideTurnStart.
/// </summary>
[FlashContent("PREP_TIME_POWER")]
public sealed class PrepTimePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref FlashExec x, FlashListener self)
    {
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<VigorPower>(),
            x.B.Powers[self.Index].Amount, self.Owner);
    }
}

/// <summary>
/// Native <c>TheGambitPower</c>: a single-stack debuff that removes itself and kills its owner when
/// the owner takes unblocked damage from a powered attack (AfterDamageReceivedMirrors.HandleTheGambitPower).
/// </summary>
[FlashContent("THE_GAMBIT_POWER")]
public sealed class TheGambitPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || !result.Props.IsPoweredAttack() || result.UnblockedDamage <= 0)
            return;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
        Span<byte> targets = stackalloc byte[1];
        targets[0] = self.Owner;
        // CreatureCmd.Kill(owner): a normal (non-forced) kill, so death prevention still runs.
        FlashKernel.Kill(ref x, targets, force: false);
    }
}

/// <summary>
/// Native <c>AutomationPower</c> (instanced): every 10 cards drawn grants its owner <c>Amount</c>
/// energy and restarts the counter. AfterCardDrawnMirrors keeps the counter in
/// AutomationPredictionState; the Flash row stores it in Aux0.
/// </summary>
[FlashContent("AUTOMATION_POWER")]
public sealed class AutomationPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsInstanced => true;

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Aux0 <= 0)
            power.Aux0 = 10;
        power.Aux0--;
        if (power.Aux0 > 0)
            return;
        FlashKernel.GainEnergy(ref x, power.Amount);
        power.Aux0 = 10;
    }
}

/// <summary>
/// Native <c>PanachePower</c> (instanced): every fifth card played after the power, deal
/// <c>Amount</c> unpowered damage to all hittable enemies and restart the cycle. The first
/// AfterCardPlayed (the card that applied it) only arms the counter. Aux0 is CardsLeft, Aux1 is
/// alreadyApplied; EndTurnPowerSupport.Batch048 resets CardsLeft at the owner's side turn end.
/// </summary>
[FlashContent("PANACHE_POWER")]
public sealed class PanachePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsInstanced => true;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Aux1 == 0)
        {
            power.Aux1 = 1;
            return;
        }
        if (power.Aux0 <= 0)
            power.Aux0 = 5;
        power.Aux0--;
        if (power.Aux0 > 0)
            return;
        FlashCardEffects.DamageAllEnemies(ref x, power.Amount, FlashValueProps.Unpowered, self.Owner);
        power.Aux0 = 5;
    }

    public override void EndTurnBatch048(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            x.B.Powers[self.Index].Aux0 = 5;
    }
}

/// <summary>
/// Native <c>RollingBoulderPower</c> (instanced): at its owner's player turn start deal its Amount
/// as unpowered damage to every hittable enemy, then add the power's canonical Damage (5) to Amount.
/// TurnStartPowerSupport's RollingBoulderPower case; Amount is the damage.
/// </summary>
[FlashContent("ROLLING_BOULDER_POWER")]
public sealed class RollingBoulderPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsInstanced => true;

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
        ref FlashPower power = ref x.B.Powers[self.Index];
        FlashCardEffects.DamageAllEnemies(ref x, power.Amount, FlashValueProps.Unpowered, self.Owner);
        if (x.NeedsChoice)
            return;
        // RollingBoulderPower.CanonicalVars Damage = 5; the card upgrade raises the initial Amount,
        // not this increment (RollingBoulder.incrementAmount).
        power.Amount = (int)Math.Clamp((long)power.Amount + 5, 0, FlashLimits.ValueClamp);
    }
}

/// <summary>
/// Native <c>TheBombPower</c> (instanced): Amount is the turns left; Aux0 is the per-instance bomb
/// damage (SetDamage from the card's BombDamage). At its owner's side turn end it decrements, and on
/// the last turn deals Aux0 unpowered damage to every hittable enemy and removes itself.
/// </summary>
[FlashContent("THE_BOMB_POWER")]
public sealed class TheBombPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsInstanced => true;

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (!FlashKernel.Participates(participants, self.Owner))
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Amount > 1)
        {
            power.Amount--;
            return;
        }
        int damage = power.Aux0;
        FlashCardEffects.DamageAllEnemies(ref x, damage, FlashValueProps.Unpowered, self.Owner);
        if (x.NeedsChoice)
            return;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}

/// <summary>
/// Native <c>NostalgiaPower</c>: an Attack or Skill card that would go to the discard pile returns to
/// the top of the draw pile while fewer than Amount Attack/Skill cards have been played this turn.
/// ModifyCardPlayResultLocationMirrors.HandleNostalgiaPower.
/// </summary>
[FlashContent("NOSTALGIA_POWER")]
public sealed class NostalgiaPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override FlashPile ModifyCardPlayResultLocation(ref FlashExec x, FlashListener self, in FlashCard card,
        bool isAutoPlay, int energyValue, FlashPile location, out FlashPilePosition position)
    {
        position = FlashPilePosition.Bottom;
        if (location != FlashPile.Discard)
            return location;
        FlashCardType type = x.R.Card(card.Id, card.Upgrade).Type;
        if (type is not (FlashCardType.Attack or FlashCardType.Skill))
            return location;
        if (x.B.Counter(FlashCounter.AttackSkillStartsThisTurn) >= x.B.Powers[self.Index].Amount)
            return location;
        position = FlashPilePosition.Top;
        return FlashPile.Draw;
    }
}

/// <summary>
/// Native <c>StratagemPower</c>: after each shuffle choose Amount cards from the draw pile and put
/// them into hand (AfterShuffleMirrors.HandleStratagemPower →
/// TurnStartChoiceSupport.Resolve MoveToHand over the draw pile).
/// </summary>
[FlashContent("STRATAGEM_POWER")]
public sealed class StratagemPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterShuffle(ref FlashExec x, FlashListener self, byte shuffler)
    {
        int amount = x.B.Powers[self.Index].Amount;
        int options = x.B.DrawCount;
        int count = Math.Min(amount, options);
        if (count <= 0)
            return;
        Span<int> all = stackalloc int[options];
        for (int index = 0; index < options; index++)
            all[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Draw, all,
                count, count, selected, out int selectedCount))
            return;
        FlashKernel.MoveSelected(ref x.B, FlashPile.Draw, selected[..selectedCount], FlashPile.Hand,
            FlashPilePosition.Bottom);
    }
}

/// <summary>
/// Native <c>MayhemPower</c>: the existing engine models its auto-play in
/// <c>TriggerScheduledAutoPlays</c> (AutoPrePlay phase) at the player's turn start. The kernel's
/// <see cref="FlashKernel.AutoPrePlay"/> reads this amount, so the definition keeps only the power
/// value and no hook of its own.
/// </summary>
[FlashContent("MAYHEM_POWER")]
public sealed class MayhemPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;
}

/// <summary>
/// Native <c>EntropyPower</c>: at the owner's player turn start, choose Amount cards from hand and
/// transform each into a random unlocked card of the same pool (TurnStartPowerSupport's EntropyPower
/// case → TurnStartChoiceSupport.Transform → CombatPredictionSimulator.CreateRandomCardForTransform).
/// </summary>
[FlashContent("ENTROPY_POWER")]
public sealed class EntropyPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override IReadOnlyList<string> GeneratedCardPools =>
        [FlashCardPools.CharacterAll, FlashCardPools.ColorlessAll, FlashCardPools.StatusAll, FlashCardPools.CurseAll];

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
        int hand = x.B.HandCount;
        if (hand == 0)
            return;
        int count = Math.Min(x.B.Powers[self.Index].Amount, hand);
        if (count <= 0)
            return;
        Span<int> options = stackalloc int[hand];
        for (int index = 0; index < hand; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.Transform, FlashPile.Hand, options,
                count, count, selected, out int selectedCount))
            return;
        for (int index = 0; index < selectedCount; index++)
        {
            ref FlashCard original = ref FlashPiles.At(ref x.B, FlashPile.Hand, selected[index]);
            FlashCard replacement = RandomTransform(ref x, original);
            FlashKernel.ReplaceCard(ref x, FlashPile.Hand, selected[index], replacement);
            if (x.NeedsChoice)
                return;
        }
    }

    private static FlashCard RandomTransform(ref FlashExec x, in FlashCard original)
    {
        FlashCardStats stats = x.R.Card(original.Id, original.Upgrade);
        // CardFactory.GetDefaultTransformationOptions picks original.Pool unless the card is a
        // Quest/Event/Ancient/Token card (then the colorless pool). CharacterAll excludes Basic cards
        // (FilterForCombat), so a starter card still belongs to the character pool.
        // CardFactory.GetDefaultTransformationOptions: a Quest/Event/Ancient/Token card uses the
        // colorless pool; every other card uses its canonical pool (character, colorless, status or
        // curse), regardless of whether that particular card is currently unlocked.
        string poolKey = stats.Type == FlashCardType.Quest
            || stats.Rarity is FlashCardRarity.Event or FlashCardRarity.Ancient or FlashCardRarity.Token
            ? FlashCardPools.ColorlessAll
            : stats.Pool switch
            {
                FlashTransformPool.Status => FlashCardPools.StatusAll,
                FlashTransformPool.Curse => FlashCardPools.CurseAll,
                FlashTransformPool.Colorless => FlashCardPools.ColorlessAll,
                _ => FlashCardPools.CharacterAll,
            };
        ReadOnlySpan<ushort> pool = x.R.CardPool(poolKey).Cards;
        Span<ushort> candidates = stackalloc ushort[pool.Length];
        int count = 0;
        // CardFactory.GetFilteredTransformationOptions keeps the original's rarity band for ordinary
        // cards; Status and Curse are the two "rarity - 8 <= 1" values that skip the band filter.
        bool filterRarity = stats.Rarity is not (FlashCardRarity.Event or FlashCardRarity.Ancient
            or FlashCardRarity.Status or FlashCardRarity.Curse);
        for (int index = 0; index < pool.Length; index++)
        {
            ushort id = pool[index];
            if (id == original.Id)
                continue;
            FlashCardRarity rarity = x.R.Card(id, 0).Rarity;
            if (filterRarity && rarity is not (FlashCardRarity.Common or FlashCardRarity.Uncommon
                or FlashCardRarity.Rare))
                continue;
            candidates[count++] = id;
        }
        if (count == 0)
            throw new FlashUnsupportedException($"随机变化没有可用的 {poolKey} 候选。");
        ushort chosen = candidates[x.B.RngStream(FlashRngStream.CombatCardSelection).NextInt(0, count)];
        return FlashKernel.CreateCard(x.R, chosen);
    }
}
