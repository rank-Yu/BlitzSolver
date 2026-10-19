using System.Reflection;

namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>Declares the native model id implemented by a Flash content definition.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FlashContentAttribute(string nativeId) : Attribute
{
    public string NativeId { get; } = nativeId;
}

/// <summary>Native <c>ValueProp</c> flags.</summary>
[Flags]
public enum FlashValueProps : byte
{
    None = 0,
    Unblockable = 2,
    Unpowered = 4,
    Move = 8,
    SkipHurtAnim = 0x10,
}

public static class FlashValuePropsExtensions
{
    /// <summary>Native <c>ValueProp.IsPoweredAttack()</c>.</summary>
    public static bool IsPoweredAttack(this FlashValueProps props)
        => (props & FlashValueProps.Move) != 0 && (props & FlashValueProps.Unpowered) == 0;
}

public enum FlashListenerKind : byte
{
    Power,
    Relic,
    Potion,
    Monster,
    Orb,
    Card,
    Affliction,
    Enchantment,
}

/// <summary>Identity of the listener a hook is being dispatched to.</summary>
public readonly record struct FlashListener(FlashListenerKind Kind, byte Index, byte Owner);

/// <summary>Arguments of the damage modification hooks.</summary>
public readonly struct FlashDamageContext
{
    public FlashDamageContext(byte target, byte dealer, FlashValueProps props, bool hasCardSource, in FlashCard cardSource)
    {
        Target = target;
        Dealer = dealer;
        Props = props;
        HasCardSource = hasCardSource;
        CardSource = cardSource;
    }

    /// <summary>Target owner, or <see cref="FlashLimits.NoOwner"/>.</summary>
    public byte Target { get; }

    /// <summary>Dealer owner, or <see cref="FlashLimits.NoOwner"/>.</summary>
    public byte Dealer { get; }

    public FlashValueProps Props { get; }

    public bool HasCardSource { get; }

    public FlashCard CardSource { get; }
}

/// <summary>Arguments of the HP loss modification hooks (<c>ModifyHpLostMirrorContext</c>).</summary>
public readonly struct FlashHpLossContext
{
    public FlashHpLossContext(byte target, FlashValueProps props, byte dealer, bool hasCardSource, in FlashCard cardSource)
    {
        Target = target;
        Props = props;
        Dealer = dealer;
        HasCardSource = hasCardSource;
        CardSource = cardSource;
    }

    public byte Target { get; }

    public FlashValueProps Props { get; }

    /// <summary>Dealer owner, or <see cref="FlashLimits.NoOwner"/>.</summary>
    public byte Dealer { get; }

    public bool HasCardSource { get; }

    public FlashCard CardSource { get; }
}

/// <summary>Arguments of the block modification hooks.</summary>
public readonly struct FlashBlockContext
{
    public FlashBlockContext(byte target, FlashValueProps props, bool hasCardSource, in FlashCard cardSource)
    {
        Target = target;
        Props = props;
        HasCardSource = hasCardSource;
        CardSource = cardSource;
    }

    public byte Target { get; }

    public FlashValueProps Props { get; }

    public bool HasCardSource { get; }

    public FlashCard CardSource { get; }
}

/// <summary>Hooks a definition overrides; computed once by reflection.</summary>
[Flags]
public enum FlashHookMask : ulong
{
    None = 0,
    ModifyDamageAdditive = 1UL << 0,
    ModifyDamageMultiplicative = 1UL << 1,
    ModifyDamageCap = 1UL << 2,
    ModifyBlockAdditive = 1UL << 3,
    ModifyBlockMultiplicative = 1UL << 4,
    AfterDamageReceived = 1UL << 5,
    AfterDamageReceivedLate = 1UL << 6,
    AfterDamageGiven = 1UL << 7,
    BeforeCardPlayed = 1UL << 8,
    AfterCardPlayed = 1UL << 9,
    AfterCardPlayedLate = 1UL << 10,
    AfterCardExhausted = 1UL << 11,
    AfterBlockGained = 1UL << 12,
    AfterAttack = 1UL << 13,
    BeforeAttack = 1UL << 38,
    ShouldClearBlock = 1UL << 14,
    ModifyMaxEnergy = 1UL << 15,
    ModifyHandDraw = 1UL << 16,
    ModifyHandDrawLate = 1UL << 17,
    BeforeDamageReceived = 1UL << 18,
    BeforeSideTurnEndVeryEarly = 1UL << 19,
    BeforeSideTurnEndEarly = 1UL << 20,
    BeforeSideTurnEnd = 1UL << 21,
    ShouldDraw = 1UL << 22,
    AfterCardDrawnEarly = 1UL << 23,
    AfterCardDrawn = 1UL << 24,
    ModifyHpLostBeforeOsty = 1UL << 25,
    ModifyHpLostBeforeOstyLate = 1UL << 26,
    ModifyHpLostAfterOsty = 1UL << 27,
    ModifyHpLostAfterOstyLate = 1UL << 28,
    AfterModifyingHpLostAfterOsty = 1UL << 29,
    AfterCurrentHpChanged = 1UL << 30,
    BeforeDeath = 1UL << 31,
    ShouldDie = 1UL << 32,
    ShouldDieLate = 1UL << 33,
    AfterDeath = 1UL << 34,
    AfterBlockBroken = 1UL << 35,
    ShouldPlay = 1UL << 36,
    ShouldFlush = 1UL << 37,
    AfterAutoPostPlayPhaseEntered = 1UL << 39,
    TryModifyEnergyCostInCombat = 1UL << 40,
    TryModifyEnergyCostInCombatLate = 1UL << 41,
    ModifyCardPlayResultLocation = 1UL << 42,
    AfterModifyingCardPlayResultLocation = 1UL << 43,
    ModifyCardPlayCount = 1UL << 44,
    AfterModifyingCardPlayCount = 1UL << 45,
    AfterPowerAmountChanged = 1UL << 46,
    AfterShuffle = 1UL << 47,
    ModifyXValue = 1UL << 48,
    ShouldPlayerResetEnergy = 1UL << 49,
    TryModifyPowerAmountReceived = 1UL << 50,
    AfterModifyingPowerAmountReceived = 1UL << 51,
    AfterCardEnteredCombat = 1UL << 52,

    /// <summary><c>PersistentPowerSupport.TriggerRampart</c>: player-side turn start, after the owner passes.</summary>
    Rampart = 1UL << 53,

    /// <summary><c>HookMirrors.AfterCardGeneratedForCombat</c> (Aeonglass upgrades generated Withers).</summary>
    AfterCardGeneratedForCombat = 1UL << 54,

    /// <summary><c>HookMirrors.AfterCardDiscarded</c> (<c>CombatPredictionSimulator.Discard</c>).</summary>
    AfterCardDiscarded = 1UL << 55,

    /// <summary><c>HookMirrors.AfterOrbChanneled</c> (<c>CombatPredictionSimulator.OrbChannel</c>).</summary>
    AfterOrbChanneled = 1UL << 56,

    /// <summary><c>HookMirrors.AfterOrbEvoked</c> (<c>CombatPredictionSimulator.OrbEvoke</c>).</summary>
    AfterOrbEvoked = 1UL << 57,

    /// <summary><c>HookMirrors.ModifyOrbPassiveTriggerCount</c> (<c>OrbModel.TriggerPassive</c>).</summary>
    ModifyOrbPassiveTriggerCounts = 1UL << 58,

    /// <summary><c>HookMirrors.ModifyOrbValue</c> (<c>OrbModel.ModifyOrbValue</c>): a pure query.</summary>
    ModifyOrbValue = 1UL << 59,

    /// <summary><c>SimulatedCombatState.AfterPotionUsed</c>'s relic reactions (<c>ReptileTrinket</c>).</summary>
    AfterPotionUsed = 1UL << 60,

    /// <summary>
    /// <c>PowerLifecycleSupport.AfterStarsSpent</c>: <c>TriggerRelicsAfterStarsSpent</c> then the
    /// <c>ChildOfTheStarsPower</c> loop. The existing engine uses a fixed relic-then-power order
    /// instead of listener order, so <c>FlashHooks.AfterStarsSpent</c> dispatches in two passes.
    /// </summary>
    AfterStarsSpent = 1UL << 61,

    /// <summary><c>HookMirrors.AfterStarsGained</c> (BlackHolePower).</summary>
    AfterStarsGained = 1UL << 62,

    /// <summary><c>HookMirrors.ModifyStarCost</c> (<c>AbstractModel.TryModifyStarCost</c>): a pure query.</summary>
    TryModifyStarCost = 1UL << 63,

    /// <summary>
    /// Hooks the existing engine dispatches over the run listeners, which also include the deck versions of
    /// cards outside combat; a root whose deck holds a card implementing one of them is rejected.
    /// </summary>
    RunListenerHooks = ModifyDamageAdditive | ModifyDamageMultiplicative | ModifyDamageCap | BeforeDamageReceived
        | AfterDamageReceived | AfterDamageReceivedLate | AfterDamageGiven | ModifyHpLostBeforeOsty
        | ModifyHpLostBeforeOstyLate | ModifyHpLostAfterOsty | ModifyHpLostAfterOstyLate | AfterCurrentHpChanged
        | BeforeDeath | ShouldDie | ShouldDieLate | AfterDeath,
}

/// <summary>
/// Hooks past the 64 bits of <see cref="FlashHookMask"/>, computed by the same reflection into
/// <see cref="FlashContentDef.HooksExt"/> and collected by the matching
/// <see cref="FlashHooks.CollectListeners(in FlashBattle, FlashHookMaskExt, Span{FlashListener})"/> overload.
/// </summary>
[Flags]
public enum FlashHookMaskExt : ulong
{
    None = 0,

    /// <summary>
    /// Native <c>Hook.ModifyUnblockedDamageTarget</c>: a pure query chained over the combat listeners
    /// while damage resolves, without the ending gate. Supports <c>DieForYouPower</c>'s Osty redirect.
    /// </summary>
    ModifyUnblockedDamageTarget = 1UL << 0,

    /// <summary>
    /// Native <c>AbstractModel.AfterSideTurnEndLate</c>: the fresh-listener late pass after the regular
    /// end-of-side hooks (DisintegrationPower). Dispatch is in combat listener order, as
    /// <c>HookMirrors.AfterSideTurnEndLate</c>.
    /// </summary>
    AfterSideTurnEndLate = 1UL << 32,

    /// <summary>
    /// Native <c>Hook.ModifyPowerAmountGiven</c>: a pure query over the combat listeners before the
    /// received-side pass and the Artifact check (<c>PersistentRelicSupport.ModifyPowerAmountGiven</c>,
    /// SneckoSkull's Poison bonus). Uses the batch's assigned bits 16-31.
    /// </summary>
    ModifyPowerAmountGiven = 1UL << 16,

    /// <summary>
    /// Native <c>ModifyPowerAmountGivenMultiplicative</c>: a pure query after the additive given pass
    /// and before the received-side pass (<c>UnsettlingLamp</c>'s per-card debuff doubling).
    /// </summary>
    ModifyPowerAmountGivenMultiplicative = 1UL << 17,

    /// <summary>
    /// Native <c>BeforePowerAmountChanged</c>: the observation pass at the start of
    /// <c>SimulatedCombatState.ModifyPowerAmountForRelics</c> (UnsettlingLamp records the card and the
    /// temporary power's internal type). Must not change the amount.
    /// </summary>
    BeforePowerAmountChanged = 1UL << 18,

    /// <summary>
    /// Native <c>AfterModifyingBlockAmount</c> for the listeners that changed the settled block,
    /// dispatched before the block is added (<c>HookMirrors.ModifyBlock</c> / <c>GainBlock</c>).
    /// </summary>
    AfterModifyingBlockAmount = 1UL << 19,

    /// <summary>
    /// Native <c>AfterEnergySpent</c> (<c>CardModel.SpendResources</c>, before star spending):
    /// <c>OrbitPower</c> accumulates spent energy and grants energy on each full cycle.
    /// </summary>
    AfterEnergySpent = 1UL << 20,

    /// <summary>
    /// Native <c>EnchantmentModel.BeforeFlush</c> (<c>EnchantmentLifecycleSupport.BeforeFlush</c>):
    /// the player turn end applies it to every hand card before <c>ShouldFlush</c>
    /// (SlumberingEssence's <c>AddUntilPlayed(-1)</c>). Uses the batch's assigned bits 1-15.
    /// </summary>
    BeforeFlush = 1UL << 1,

    /// <summary>
    /// Native <c>AbstractModel.ModifyShuffleOrder</c> (<c>CombatPredictionSimulator.Shuffle</c> after the
    /// stable shuffle, before the cards are placed into the draw pile; PerfectFit moves its card to the
    /// front). Uses the batch's assigned bits 1-15.
    /// </summary>
    ModifyShuffleOrder = 1UL << 2,
}

/// <summary>
/// Candidate lists frozen by the Bridge with the existing engine's own generation queries. A
/// definition declaring one of these ids requires the rule table to contain the list and every
/// candidate to have a Flash card definition (admission rejects the root otherwise).
/// </summary>
public static class FlashCardPools
{
    /// <summary>Player's unlocked character cards, filtered for combat generation.</summary>
    public const string CharacterAll = "character.all";

    /// <summary>Player's unlocked character Attacks, filtered for combat generation.</summary>
    public const string CharacterAttacks = "character.attacks";

    /// <summary>Player's unlocked character cards with canonical cost 0 and no X cost.</summary>
    public const string CharacterZeroCost = "character.zero-cost";

    /// <summary>Player's unlocked colorless cards, filtered for combat generation.</summary>
    public const string ColorlessAll = "colorless.all";

    /// <summary>Colorless cards except JackOfAllTrades itself.</summary>
    public const string ColorlessExceptJackOfAllTrades = "colorless.except-jack-of-all-trades";

    /// <summary>Attacks from every other unlocked character pool (Splash).</summary>
    public const string OtherCharacterAttacks = "character.other-attacks";

    /// <summary>Player's unlocked character Power cards (WhiteNoise, CreativeAiPower).</summary>
    public const string CharacterPowers = "character.powers";

    /// <summary>Player's unlocked character cards whose canonical keywords include Ethereal (BigHat).</summary>
    public const string CharacterEthereal = "character.ethereal";

    /// <summary>Unlocked status cards (Entropy's transformation of a Status card).</summary>
    public const string StatusAll = "status.all";

    /// <summary>Unlocked curse cards (Entropy's transformation of a Curse card).</summary>
    public const string CurseAll = "curse.all";
}

public abstract class FlashContentDef
{
    public string NativeId { get; internal set; } = "";

    public ushort Id { get; internal set; }

    public FlashHookMask Hooks { get; internal set; }

    public FlashHookMaskExt HooksExt { get; internal set; }

    /// <summary>Native dynamic-variable names compiled into the rule table, in order.</summary>
    public virtual IReadOnlyList<string> VarNames => [];

    /// <summary>
    /// Native card ids (with upgrade level) this content can put into combat. The rule compiler adds
    /// their canonical stats, and admission rejects the root when any of them has no Flash definition.
    /// </summary>
    public virtual IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [];

    /// <summary>
    /// Candidate pools (see <see cref="FlashCardPools"/>) this content can generate from. The Bridge
    /// freezes each pool with the existing engine's own query and admission rejects the root when a
    /// candidate has no Flash definition.
    /// </summary>
    public virtual IReadOnlyList<string> GeneratedCardPools => [];

    /// <summary>
    /// Native power ids this content applies during its effect. The closure follows them so a power
    /// that can generate cards (CalamityPower) is frozen and checked before the root is admitted.
    /// </summary>
    public virtual IReadOnlyList<string> AppliedPowers => [];

    /// <summary>
    /// True when this content can create a random potion in combat (<c>PotionFactory.CreateRandomPotionInCombat</c>).
    /// The Bridge then freezes the potion option pool and admission rejects an undefined candidate.
    /// </summary>
    public virtual bool GeneratesRandomPotion => false;
}

/// <summary>
/// Base of every definition that can listen to hooks. Hook methods are named after the native
/// hook and receive the listener identity so the definition can read its own state.
/// Definitions are stateless; every mutable value lives in <see cref="FlashBattle"/>.
/// </summary>
public abstract class FlashListenerDef : FlashContentDef
{
    /// <summary>
    /// Native <c>AbstractModel.AfterSideTurnEndLate</c>: the late pass with a fresh combat-listener
    /// snapshot. <paramref name="participates"/> is <c>participants.Contains(self.Owner)</c>.
    /// </summary>
    public virtual void AfterSideTurnEndLate(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
    }

    /// <summary>Case in <c>PersistentPowerSupport.TriggerRampart</c> (player-side turn start only).</summary>
    public virtual void Rampart(ref FlashExec x, FlashListener self)
    {
    }

    /// <summary>
    /// <c>HookMirrors.AfterCardGeneratedForCombat</c> for the card at <paramref name="position"/>.
    /// <paramref name="creator"/> is the player owner, or <see cref="FlashLimits.NoOwner"/> when a
    /// monster generated the card (the existing engine passes a null <c>Player</c> creator there).
    /// </summary>
    public virtual void AfterCardGeneratedForCombat(ref FlashExec x, FlashListener self, int position,
        byte creator)
    {
    }

    public virtual decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self, in FlashDamageContext damage, decimal amount) => 0m;

    public virtual decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self, in FlashDamageContext damage, decimal amount) => 1m;

    public virtual decimal ModifyDamageCap(ref FlashExec x, FlashListener self, in FlashDamageContext damage) => decimal.MaxValue;

    public virtual decimal ModifyBlockAdditive(ref FlashExec x, FlashListener self, in FlashBlockContext block, decimal amount) => 0m;

    public virtual decimal ModifyBlockMultiplicative(ref FlashExec x, FlashListener self, in FlashBlockContext block, decimal amount) => 1m;

    /// <summary>
    /// Native <c>AfterModifyingBlockAmount</c> for a listener that changed the settled block, before
    /// the block is added. <paramref name="modified"/> is the final settled amount
    /// (<c>HookMirrors.AfterModifyingBlockAmount</c>).
    /// </summary>
    public virtual void AfterModifyingBlockAmount(ref FlashExec x, FlashListener self, decimal modified,
        in FlashBlockContext block)
    {
    }

    // HP loss modification (HookMirrors.ModifyHpLost over the run listeners, no ending gate): before the
    // Osty redirect, then after it, each with a Late pass. A listener that changes the truncated amount in an
    // after-Osty pass is notified by AfterModifyingHpLostAfterOsty.

    public virtual decimal ModifyHpLostBeforeOsty(ref FlashExec x, FlashListener self, in FlashHpLossContext loss, decimal amount) => amount;

    public virtual decimal ModifyHpLostBeforeOstyLate(ref FlashExec x, FlashListener self, in FlashHpLossContext loss, decimal amount) => amount;

    public virtual decimal ModifyHpLostAfterOsty(ref FlashExec x, FlashListener self, in FlashHpLossContext loss, decimal amount) => amount;

    public virtual decimal ModifyHpLostAfterOstyLate(ref FlashExec x, FlashListener self, in FlashHpLossContext loss, decimal amount) => amount;

    public virtual void AfterModifyingHpLostAfterOsty(ref FlashExec x, FlashListener self)
    {
    }

    // Death (CombatPredictionSimulator.KillWithoutCheckingWinCondition) and HP changes, over the run
    // listeners without the ending gate.

    /// <summary>Native <c>AfterCurrentHpChanged</c>: after damage, a kill or a heal (<paramref name="delta"/> is the requested change).</summary>
    public virtual void AfterCurrentHpChanged(ref FlashExec x, FlashListener self, byte creature, decimal delta)
    {
    }

    public virtual void BeforeDeath(ref FlashExec x, FlashListener self, byte creature)
    {
    }

    /// <summary>Native <c>ShouldDie</c>: return false to prevent the death; the first preventer gets <see cref="AfterPreventingDeath"/>.</summary>
    public virtual bool ShouldDie(ref FlashExec x, FlashListener self, byte creature) => true;

    public virtual bool ShouldDieLate(ref FlashExec x, FlashListener self, byte creature) => true;

    public virtual void AfterDeath(ref FlashExec x, FlashListener self, byte creature, bool wasRemovalPrevented)
    {
    }

    /// <summary>Called on the preventer only (<c>HookMirrors.AfterPreventingDeath</c>).</summary>
    public virtual void AfterPreventingDeath(ref FlashExec x, FlashListener self, byte creature)
    {
    }

    /// <summary>Native <c>AfterBlockBroken</c> over the combat listeners (no ending gate), from <c>ProcessDamageResults</c>.</summary>
    public virtual void AfterBlockBroken(ref FlashExec x, FlashListener self, byte target, byte breaker)
    {
    }

    // Action hooks. Each dispatch point in the kernel names the existing-engine call it mirrors.

    /// <summary>
    /// Native <c>BeforeDamageReceived</c>, dispatched after damage modification and before block
    /// (<c>CombatPredictionSimulator.TryDamageTarget</c>), over every listener without the ending gate.
    /// </summary>
    public virtual void BeforeDamageReceived(ref FlashExec x, FlashListener self, byte target, decimal amount,
        FlashValueProps props, byte dealer, bool hasCardSource, in FlashCard cardSource)
    {
    }

    /// <summary>
    /// Native <c>ModifyUnblockedDamageTarget</c> (<c>Hook.ModifyUnblockedDamageTarget</c>), chained over the
    /// combat listeners without the ending gate: returns the receiver of the HP loss, which may differ from
    /// <paramref name="target"/> (Osty's <c>DieForYouPower</c>). Pure: prediction and evaluation call it read-only.
    /// </summary>
    public virtual byte ModifyUnblockedDamageTarget(ref FlashExec x, FlashListener self, byte target,
        decimal amount, FlashValueProps props, byte dealer) => target;

    public virtual void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
    }

    /// <summary>
    /// Native <c>ShouldPlay</c> in the playability check (<c>CombatPredictionSimulator.CanPlay</c>, after the costs;
    /// gated by combat ending): return false to forbid playing <paramref name="card"/>. A pure query of the battle.
    /// </summary>
    public virtual bool ShouldPlay(in FlashBattle battle, FlashRules rules, FlashListener self, in FlashCard card) => true;

    /// <summary>
    /// Native <c>ShouldFlush</c> at the player's turn end (<c>PersistentRelicSupport.ShouldFlush</c>, every listener,
    /// no ending gate): return false to keep the whole hand.
    /// </summary>
    public virtual bool ShouldFlush(ref FlashExec x, FlashListener self) => true;

    /// <summary>
    /// Native <c>EnchantmentModel.BeforeFlush</c> (<c>EnchantmentLifecycleSupport.BeforeFlush</c>): runs for every
    /// hand card before <c>ShouldFlush</c> at the player turn end. SlumberingEssence adds a
    /// <c>WhenPlayed</c> −1 cost modifier to its own card.
    /// </summary>
    public virtual void BeforeFlush(ref FlashExec x, FlashListener self)
    {
    }

    /// <summary>
    /// Native <c>AbstractModel.ModifyShuffleOrder</c> (<c>HookMirrors.ModifyShuffleOrder</c>): the existing engine
    /// runs it after the stable shuffle and before the merged cards are placed into the draw pile. The kernel
    /// passes the local <paramref name="cards"/> region (<paramref name="count"/> entries); <paramref name="self"/>
    /// identifies the listener's own card by its absolute position in <see cref="FlashBattle.Cards"/>.
    /// </summary>
    public virtual void ModifyShuffleOrder(ref FlashExec x, FlashListener self, scoped Span<FlashCard> cards, int count,
        bool isInitialShuffle)
    {
    }

    /// <summary>Native <c>ShouldDraw</c> at the start of a draw (gated by combat ending): return false to prevent it.</summary>
    public virtual bool ShouldDraw(ref FlashExec x, FlashListener self, bool fromHandDraw) => true;

    /// <summary>
    /// <c>HookMirrors.AfterCardDrawn</c> Early pass for the card just drawn to <paramref name="drawnPosition"/>
    /// (absolute position in <see cref="FlashBattle.Cards"/>).
    /// </summary>
    public virtual void AfterCardDrawnEarly(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
    }

    /// <summary>
    /// <c>HookMirrors.AfterCardDrawn</c> normal pass. A card reacting to being drawn itself (Void and similar)
    /// claims that once with <see cref="FlashKernel.TakeIntrinsicDraw"/>; the kernel calls it after the pass
    /// when its listener visit did not (for example while combat is ending).
    /// </summary>
    public virtual void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
    }

    /// <summary>
    /// Native <c>AfterAutoPostPlayPhaseEntered</c> (<c>HookMirrors.AfterAutoPostPlayPhaseEntered</c>),
    /// dispatched over the combat listeners while the player phase is AutoPostPlay, before it moves to
    /// End. HowlFromBeyond and StampedePower are the supported listeners.
    /// </summary>
    public virtual void AfterAutoPostPlayPhaseEntered(ref FlashExec x, FlashListener self)
    {
    }

    // HookMirrors.BeforeSideTurnEnd at the player's turn end (phase one, after AfterAutoPostPlayPhaseEntered):
    // VeryEarly, Early and normal passes over the combat listeners, each gated by combat ending.
    // participants is a bit set over owner indexes (FlashKernel.Participates).

    public virtual void BeforeSideTurnEndVeryEarly(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
    }

    public virtual void BeforeSideTurnEndEarly(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
    }

    public virtual void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
    }

    public virtual void AfterDamageReceivedLate(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
    }

    public virtual void AfterDamageGiven(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
    }

    // Energy cost and play-result hooks. The cost hooks are pure queries (no state mutation): the
    // kernel reads them from playability checks and the evaluation as well as from an action.
    // HookMirrors.ModifyEnergyCostInCombat runs the normal pass over every combat listener, then the
    // late pass over every combat listener; the caller clamps the result at zero.

    /// <summary>Native <c>TryModifyEnergyCostInCombat</c> normal pass; <paramref name="pile"/> is the card's pile.</summary>
    public virtual decimal TryModifyEnergyCostInCombat(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount) => amount;

    /// <summary>Native <c>TryModifyEnergyCostInCombatLate</c> late pass (Corruption, FreeAttackPower).</summary>
    public virtual decimal TryModifyEnergyCostInCombatLate(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount) => amount;

    /// <summary>
    /// Native <c>ModifyCardPlayResultLocation</c> (<c>CardModel.OnPlayWrapper</c>), after the base
    /// result location. <paramref name="energyValue"/> is <c>CardPlay.Resources.EnergyValue</c>;
    /// <paramref name="position"/> is the native <c>CardLocation.position</c> (Nostalgia returns to
    /// the top of the draw pile; the base locations are Bottom).
    /// </summary>
    public virtual FlashPile ModifyCardPlayResultLocation(ref FlashExec x, FlashListener self, in FlashCard card,
        bool isAutoPlay, int energyValue, FlashPile location, out FlashPilePosition position)
    {
        position = FlashPilePosition.Bottom;
        return location;
    }

    /// <summary>
    /// <c>CardModel.OnPlayWrapper</c>'s direct dispatch over the modifiers that changed the result
    /// location (<c>HookMirrors.AfterModifyingCardPlayResultLocation</c>); not re-read from the listener list.
    /// </summary>
    public virtual void AfterModifyingCardPlayResultLocation(ref FlashExec x, FlashListener self, in FlashCard card,
        FlashPile location)
    {
    }

    /// <summary>
    /// Native <c>ModifyCardPlayCount</c> over every combat listener; the existing engine records the
    /// listeners whose value changed for the after pass.
    /// </summary>
    public virtual int ModifyCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card, byte target,
        int playCount) => playCount;

    /// <summary>
    /// <c>Hook.AfterModifyingCardPlayCount</c>: only the modifiers recorded by
    /// <see cref="ModifyCardPlayCount"/>, in listener order (OneTwoPunch, Duplication).
    /// </summary>
    public virtual void AfterModifyingCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card)
    {
    }

    /// <summary>
    /// <c>PowerLifecycleSupport.ResolvePowerAmountChanges</c>: a queued power amount change from
    /// <c>ApplyWithBeforeApplied</c>. The existing engine dispatches this over the current powers only.
    /// </summary>
    public virtual void AfterPowerAmountChanged(ref FlashExec x, FlashListener self, ushort powerId, int delta,
        byte applier, byte target)
    {
    }

    /// <summary>Native <c>AfterShuffle</c> over the combat listeners, after the shuffle event count.</summary>
    public virtual void AfterShuffle(ref FlashExec x, FlashListener self, byte shuffler)
    {
    }

    /// <summary>Native <c>ModifyXValue</c> over the combat listeners (<c>Hook.ModifyXValue</c>).</summary>
    public virtual int ModifyXValue(in FlashBattle battle, FlashRules rules, FlashListener self, in FlashCard card,
        int value) => value;

    /// <summary>
    /// <c>HookMirrors.ModifyStarCost</c> (<c>AbstractModel.TryModifyStarCost</c>): a pure query over
    /// the combat listeners, run only for a non-negative original cost and skipped while the combat is
    /// over/ending. <paramref name="pile"/> is the card's current pile (VoidFormPower/BrilliantScarf
    /// only modify cards in Hand or Play).
    /// </summary>
    public virtual decimal TryModifyStarCost(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount) => amount;

    /// <summary>
    /// <c>PowerLifecycleSupport.AfterStarsSpent</c> (<c>CombatPredictionSimulator.SpendResources</c>):
    /// the spender's relics first (<c>TriggerRelicsAfterStarsSpent</c>: MiniRegent, then GalacticDust),
    /// then every effective power in listener order (ChildOfTheStarsPower). <paramref name="amount"/>
    /// is the star cost actually paid and is always positive here.
    /// </summary>
    public virtual void AfterStarsSpent(ref FlashExec x, FlashListener self, int amount)
    {
    }

    /// <summary>
    /// Native <c>AfterEnergySpent</c> (before star spending). The amount is the energy actually paid;
    /// the existing engine calls the sink even for zero, whose <c>OrbitPower</c> case ignores it.
    /// </summary>
    public virtual void AfterEnergySpent(ref FlashExec x, FlashListener self, in FlashCard card, int amount)
    {
    }

    /// <summary>
    /// <c>HookMirrors.AfterStarsGained</c> after <c>PlayerCmd.GainStars</c> mutates the player's
    /// stars. <paramref name="amount"/> is the requested gain (BlackHolePower ignores non-positive
    /// amounts).
    /// </summary>
    public virtual void AfterStarsGained(ref FlashExec x, FlashListener self, int amount)
    {
    }

    /// <summary>
    /// <c>Hook.ModifyOrbValue</c> over the combat listeners (<c>FocusPower</c>, <c>InfusedCore</c>).
    /// A pure query: prediction and evaluation call it read-only.
    /// </summary>
    public virtual decimal ModifyOrbValue(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashOrb orb, decimal value) => value;

    /// <summary><c>HookMirrors.AfterOrbChanneled</c> for the orb now at <paramref name="orb"/>.</summary>
    public virtual void AfterOrbChanneled(ref FlashExec x, FlashListener self, in FlashOrb orb)
    {
    }

    /// <summary>
    /// <c>HookMirrors.AfterOrbEvoked</c>: the evoked orb value and its result targets (owner indexes,
    /// in <c>OrbModel.Evoke</c> order), dispatched after the evoke.
    /// </summary>
    public virtual void AfterOrbEvoked(ref FlashExec x, FlashListener self, in FlashOrb orb,
        scoped ReadOnlySpan<byte> targets)
    {
    }

    /// <summary>
    /// <c>Hook.ModifyOrbPassiveTriggerCount</c>: a pure query over every combat listener; the listeners
    /// that change the count are recorded for <c>AfterModifyingOrbPassiveTriggerCount</c>, whose only
    /// vanilla listener (GoldPlatedCables) is cosmetic and has no Flash definition.
    /// </summary>
    public virtual int ModifyOrbPassiveTriggerCounts(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashOrb orb, int orbIndex, int triggerCount) => triggerCount;

    /// <summary>
    /// Native <c>ShouldPlayerResetEnergy</c> over the combat listeners before the player turn's energy
    /// reset (<c>PersistentRelicSupport.ShouldPlayerResetEnergy</c>); false keeps the current energy.
    /// </summary>
    public virtual bool ShouldPlayerResetEnergy(in FlashBattle battle, FlashRules rules, FlashListener self,
        byte player) => true;

    /// <summary>
    /// Native <c>TryModifyPowerAmountReceived</c> (RuinedHelmet). This pass must not mutate state; the
    /// kernel records the listeners that changed the amount and calls
    /// <see cref="AfterModifyingPowerAmountReceived"/> on them before the Artifact check, matching
    /// <c>SimulatedCombatState.ModifyPowerAmountForRelics</c>.
    /// </summary>
    /// <summary>
    /// <c>Hook.ModifyPowerAmountGiven</c>: <c>PersistentRelicSupport.ModifyPowerAmountGiven</c> iterates
    /// the combat listeners and passes the already-modified amount, so each listener adds its delta.
    /// A pure query; the received-side pass follows it.
    /// </summary>
    public virtual decimal ModifyPowerAmountGiven(in FlashBattle battle, FlashRules rules, FlashListener self,
        ushort powerId, byte giver, byte target, decimal amount) => 0m;

    /// <summary>
    /// Native <c>Hook.ModifyPowerAmountGivenMultiplicative</c>
    /// (<c>UnsettlingLamp.ModifyPowerAmountGivenMultiplicative</c>): a pure query over the combat
    /// listeners after the additive given pass. <paramref name="cardSourceSerial"/> is 0 when the
    /// application has no card source.
    /// </summary>
    public virtual decimal ModifyPowerAmountGivenMultiplicative(ref FlashExec x, FlashListener self,
        ushort powerId, byte giver, byte target, decimal amount, int cardSourceSerial) => 1m;

    /// <summary>
    /// Native <c>BeforePowerAmountChanged</c> at the start of
    /// <c>SimulatedCombatState.ModifyPowerAmountForRelics</c>. UnsettlingLamp records the triggering
    /// card and the temporary power's internal type; the amount must not be changed here.
    /// </summary>
    public virtual void BeforePowerAmountChanged(ref FlashExec x, FlashListener self, ushort powerId,
        int amount, byte target, byte applier, int cardSourceSerial)
    {
    }

    public virtual bool TryModifyPowerAmountReceived(in FlashBattle battle, FlashRules rules, FlashListener self,
        ushort powerId, byte target, int amount, out int modified)
    {
        modified = amount;
        return false;
    }

    /// <summary>Native <c>AfterModifyingPowerAmountReceived</c> for the listeners recorded by the received pass.</summary>
    public virtual void AfterModifyingPowerAmountReceived(ref FlashExec x, FlashListener self, ushort powerId,
        byte target)
    {
    }

    /// <summary>
    /// Native <c>AfterCardEnteredCombat</c> over the player's relics when a card enters combat from
    /// outside the combat piles (<c>SimulatedCombatState.CardEventHistory.AfterCardEnteredCombat</c>
    /// dispatches the relic before the card-specific cost reactions).
    /// </summary>
    public virtual void AfterCardEnteredCombat(ref FlashExec x, FlashListener self, FlashPile pile, int index,
        in FlashCard card)
    {
    }

    /// <summary>
    /// <c>SimulatedCombatState.AfterPotionUsed</c>'s relic reactions, dispatched while the player lives
    /// after the potion effect and before <c>ResolvePowerAmountChanges</c> (ReptileTrinket).
    /// </summary>
    public virtual void AfterPotionUsed(ref FlashExec x, FlashListener self, ushort potionId)
    {
    }

    public virtual void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
    }

    public virtual void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
    }

    public virtual void AfterCardPlayedLate(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
    }

    public virtual void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card, bool causedByEthereal)
    {
    }

    /// <summary>
    /// Native <c>AfterCardDiscarded</c> (<c>HookMirrors.AfterCardDiscarded</c>), dispatched after a
    /// card effect moved a card to the discard pile (Tingsha, ToughBandages) and before its Sly
    /// auto-play. The turn-end hand flush does not dispatch it, matching
    /// <c>CorePowerSupport.FlushPlayerHandAtTurnEnd</c>.
    /// </summary>
    public virtual void AfterCardDiscarded(ref FlashExec x, FlashListener self, in FlashCard card)
    {
    }

    public virtual void AfterBlockGained(ref FlashExec x, FlashListener self, byte owner, decimal amount, FlashValueProps props, bool fromCard)
    {
    }

    /// <summary>
    /// Native <c>BeforeAttack</c> (<c>CombatPredictionSimulator.BeginAttackContext</c>), before the first
    /// hit. Every Flash attack comes from a card or a monster move; <paramref name="props"/> are its
    /// damage props.
    /// </summary>
    public virtual void BeforeAttack(ref FlashExec x, FlashListener self, byte attacker, FlashValueProps props)
    {
    }

    public virtual void AfterAttack(ref FlashExec x, FlashListener self, byte attacker)
    {
    }

    /// <summary>Native <c>ShouldClearBlock</c>: return false to keep the owner's block at its side start.</summary>
    public virtual bool ShouldClearBlock(ref FlashExec x, FlashListener self, byte owner) => true;

    /// <summary>
    /// Block kept after this listener prevented the clear (<c>PersistentRelicSupport.BlockAfterPreventingClear</c>);
    /// the excess is removed as unpowered block damage.
    /// </summary>
    public virtual int BlockAfterPreventingClear(ref FlashExec x, FlashListener self, byte owner, int block) => block;

    public virtual decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount) => amount;

    /// <summary>
    /// <c>PersistentPowerSupport.GetModifiedHandDraw</c> normal pass. Evaluated exactly once per turn-start hand draw
    /// (<c>ConsumeModifiedHandDraw</c>), so a listener may advance its own per-turn state here as the existing engine does.
    /// </summary>
    public virtual decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount) => amount;

    public virtual decimal ModifyHandDrawLate(ref FlashExec x, FlashListener self, decimal amount) => amount;
}

public abstract class FlashCardDef : FlashListenerDef
{
    /// <summary>
    /// Cards whose power compensation lives in <c>CorePowerSupport.ApplyCardPowers</c>'s switch after
    /// <c>ResolveMonologues</c>. The kernel resolves pending Monologue Strength before such a card's
    /// first power application so the power table keeps the existing engine's acquisition order. The
    /// list is the case-label set of that switch (the Flash content split is by native card id).
    /// </summary>
    public bool ResolvesMonologuesBeforeCompensation => NativeId is
            "ABRASIVE"
            or "ACCELERANT"
            or "ACCURACY"
            or "AFTERIMAGE"
            or "AGGRESSION"
            or "ANGER"
            or "ANTICIPATE"
            or "ASSASSINATE"
            or "BARRICADE"
            or "BASH"
            or "BATTLE_TRANCE"
            or "BEAM_CELL"
            or "BIASED_COGNITION"
            or "BREAK"
            or "BUFFER"
            or "BULK_UP"
            or "CAPACITOR"
            or "CORRUPTION"
            or "CREATIVE_AI"
            or "CRIMSON_MANTLE"
            or "DARK_EMBRACE"
            or "DEFRAGMENT"
            or "DEMON_FORM"
            or "DODGE_AND_ROLL"
            or "DOMINATE"
            or "ECHO_FORM"
            or "ENVENOM"
            or "FEAR"
            or "FEEL_NO_PAIN"
            or "FISTICUFFS"
            or "FOOTWORK"
            or "FRIENDSHIP"
            or "FURNACE"
            or "GAMMA_BLAST"
            or "HIGH_FIVE"
            or "INFLAME"
            or "KNOW_THY_PLACE"
            or "LEG_SWEEP"
            or "METEOR_SHOWER"
            or "PIERCING_WAIL"
            or "PROWESS"
            or "REFINE_BLADE"
            or "RESONANCE"
            or "SHOCKWAVE"
            or "SIDESTEP"
            or "SUCKER_PUNCH"
            or "THUNDERCLAP"
            or "TREMBLE";

    /// <summary>
    /// Card effect in the existing engine's order: mirrored attack/block/draw first, then the
    /// compensated power/resource/generation effects.
    /// </summary>
    public abstract void OnPlay(ref FlashExec x);

    /// <summary>
    /// The card's own choice and its post-choice effects, run after <see cref="OnPlay"/> as the
    /// existing engine's <c>ResolveManualCardChoice</c> stage does (<c>CardChoiceSupport.GetSpec</c>,
    /// <c>Apply</c> and <c>ApplyPostChoiceEffects</c>). Most cards have none.
    /// </summary>
    public virtual void OwnChoice(ref FlashExec x)
    {
    }

    /// <summary>
    /// <c>SimulatedCombatState.IsCardPlayPrevented</c>: while this card is in hand, no card without this flag can be
    /// played (Enthralled).
    /// </summary>
    public virtual bool LocksOtherHandCards => false;

    /// <summary>Native <c>CardModel.HasTurnEndInHandEffect</c>: the card runs <see cref="OnTurnEndInHand"/> at the turn end.</summary>
    public virtual bool HasTurnEndInHandEffect => false;

    /// <summary>
    /// This card uses <see cref="FlashCard.Private"/> as the per-object identity ledger the existing
    /// engine keeps in a dictionary (<c>SwordSagePower</c>'s applied replay bonus; the same field's
    /// bit 0 is Fetch's "this exact card was played this turn" bit).
    /// </summary>
    public virtual bool UsesIdentityLedger => false;

    /// <summary>
    /// Native <c>Fetch</c>: <c>SimulatedCombatState._fetchCardsPlayedThisTurn</c> records this exact
    /// card object when the play completes. The kernel sets bit 0 of <see cref="FlashCard.Private"/>.
    /// </summary>
    public virtual bool TracksFetchPlay => false;

    /// <summary>
    /// This card can create a card whose identity the existing engine keeps in a per-object
    /// dictionary (a random/selected card can be a Sovereign Blade, Fetch, or Nightmare target).
    /// Used only to decide whether a root must enable the identity scans.
    /// </summary>
    public virtual bool MayGenerateIdentityContent => false;

    /// <summary>This card is one of the ID batch's per-instance cards.</summary>
    public virtual bool IsIdentityContent => false;

    /// <summary>
    /// The card's <c>CardOnTurnEndInHandMirrors</c> effect at the player's turn end, while it sits in the play
    /// pile (<c>CombatPredictionSimulator.DoTurnEndCards</c>); <see cref="FlashExec.Card"/> holds its value.
    /// </summary>
    public virtual void OnTurnEndInHand(ref FlashExec x)
    {
    }

    /// <summary>Card-specific playability (<c>CardIsPlayableMirrors</c>); most cards are always playable.</summary>
    public virtual bool IsPlayable(in FlashBattle battle, FlashRules rules, in FlashCard card) => true;

    /// <summary>
    /// Native <c>CardModel.TargetType</c>. The rule table freezes the canonical value; a card whose
    /// native override is dynamic (Shiv while the owner holds <c>FanOfKnivesPower</c>) overrides this.
    /// </summary>
    public virtual FlashTargetType TargetType(in FlashBattle battle, FlashRules rules, in FlashCard card)
        => rules.Card(card.Id, card.Upgrade).Target;

    /// <summary>
    /// Native <c>CardModel.GetResultLocationForCardPlay</c> override (<c>CardResultLocationMirrors</c>).
    /// The base location is Discard, Exhaust or Play (a power removes the card from combat); a card that
    /// returns to the hand or the top of the draw pile rewrites the discard case here. <paramref name="position"/>
    /// is the native <c>CardLocation.position</c> for the returned pile.
    /// </summary>
    public virtual FlashPile ResultLocation(in FlashBattle battle, FlashRules rules, in FlashCard card,
        FlashPile baseLocation, out FlashPilePosition position)
    {
        position = FlashPilePosition.Bottom;
        return baseLocation;
    }

    /// <summary>
    /// A card's native <c>CalculatedDamage</c> formula for another effect that reads this card's damage
    /// (Thrash). Returns false for cards without a calculated damage variable; the caller then reads
    /// the named <c>Damage</c>/<c>OstyDamage</c> variable. <paramref name="self"/> is this card's value.
    /// </summary>
    public virtual bool TryGetCalculatedDamage(ref FlashExec x, in FlashCard self, byte target, out decimal damage)
    {
        damage = 0m;
        return false;
    }

    /// <summary>
    /// True when every <see cref="FlashHookMask.RunListenerHooks"/> hook this definition implements is a
    /// no-op for the copy that lives in the player's deck outside combat. The existing engine dispatches
    /// those hooks over the run listeners, which include deck cards; Flash only has the combat copies.
    /// Melancholy qualifies: its <c>AfterDeath</c> handler resolves the card through the combat state and
    /// returns without effect when the card is not in a combat pile. Admission still rejects every other
    /// deck card implementing a run-listener hook. See admission-and-verification.md.
    /// </summary>
    public virtual bool DeckCopyOutsideCombatIsInert => false;

    /// <summary>
    /// Card-specific adjustment when this card enters combat from outside the combat piles
    /// (<c>SimulatedCombatState.AfterCardEnteredCombat</c>, for example Stomp or Pinpoint cost changes).
    /// </summary>
    public virtual void AfterEnteredCombat(ref FlashExec x, FlashPile pile, int index)
    {
    }

    /// <summary>
    /// Card-specific <c>AfterDowngraded</c> (MagiKnight's DampenPower). Called after
    /// <see cref="FlashKernel.DowngradeCard"/> reset the value to upgrade level 0. Claw and
    /// GeneticAlgorithm re-add their private per-play accumulator to the Damage/Block variable.
    /// </summary>
    public virtual void AfterDowngraded(ref FlashBattle battle, int position)
    {
    }

    /// <summary>
    /// Fills <see cref="FlashCard.Private"/> for a card value imported from the existing engine, after
    /// the variable deltas were computed. Only cards whose definition reads the private counter may
    /// use it; the existing engine's per-card accumulator is available in the var delta
    /// (Claw/GeneticAlgorithm).
    /// </summary>
    public virtual void ProjectPrivate(ref FlashCard card)
    {
    }
}

public enum FlashPowerType : byte
{
    None = 0,
    Buff = 1,
    Debuff = 2,
}

public enum FlashPowerStackType : byte
{
    None = 0,
    Counter = 1,
    Single = 2,
}

public abstract class FlashPowerDef : FlashListenerDef
{
    /// <summary>This power is one of the ID batch's per-instance powers.</summary>
    public virtual bool IsIdentityContent => false;

    public abstract FlashPowerType Type { get; }

    public abstract FlashPowerStackType StackType { get; }

    public virtual bool AllowNegative => false;

    /// <summary>Native instanced powers get a new instance per application instead of stacking.</summary>
    public virtual bool IsInstanced => false;

    /// <summary>
    /// Native <c>PowerModel.IsVisible</c>. The single-player visibility test reduces to the hidden
    /// internal override (only <c>AmbergrisPower</c> sets <c>IsVisibleInternal=false</c>); all visible
    /// powers here return true.
    /// </summary>
    public virtual bool IsVisible => true;

    /// <summary>
    /// The instance carries a selected card value in <see cref="FlashBattle.DetachedCards"/>
    /// (<see cref="FlashPower.Aux0"/> = slot + 1). Removing the power clears the slot.
    /// </summary>
    public virtual bool UsesDetachedCard => false;

    /// <summary>Native <c>GetTypeForAmount</c>: a negative amount of a buff that allows negatives is a debuff.</summary>
    public virtual FlashPowerType TypeForAmount(int amount)
        => AllowNegative && amount < 0 && Type == FlashPowerType.Buff ? FlashPowerType.Debuff : Type;

    /// <summary>A newly applied player-side instance skips its first duration tick (Weak, Vulnerable, Frail).</summary>
    public virtual bool UsesNativeDurationSkip => false;

    /// <summary>
    /// The native <c>PowerModel.AmountOnTurnStart</c> is read for settlement only by
    /// <c>DrawCardsNextTurnPower</c>, <c>SummonNextTurnPower</c> and <c>HelloWorldPower</c>
    /// (<c>PowerLifecycleSupport.SemanticallyRelevantAmountOnTurnStart</c>). Other powers only carry
    /// the value in the native model, where a mirrored decrement (PowerAmountPredictionState) leaves
    /// the model amount one action behind until <c>SynchronizePowerAmountPredictionStates</c>. Flash
    /// applies mirrored decrements immediately, so for every non-reader the value is projected and
    /// snapshotted as zero instead of trying to reproduce a lagging model field.
    /// </summary>
    public virtual bool UsesAmountOnTurnStart => false;

    /// <summary>Ticks down at the end of the enemy side in <c>CorePowerSupport.TickDurations</c>.</summary>
    public virtual bool TicksAtEnemySideEnd => false;

    /// <summary>Native <c>PowerModel.OwnerIsSecondaryEnemy</c>: a positive amount makes its owner a secondary enemy.</summary>
    public virtual bool OwnerIsSecondaryEnemy => false;

    /// <summary>
    /// Native <c>RitualPower._wasJustAppliedByEnemy</c>: an enemy that gains the power from zero marks it
    /// (<see cref="FlashPowerFlags.WasJustAppliedByEnemy"/>, set in <c>ApplyWithBeforeApplied</c>).
    /// </summary>
    public virtual bool MarksJustAppliedByEnemy => false;

    /// <summary>
    /// <c>ShouldRemoveAfterDeath</c>: a positive amount keeps its dead owner in the enemy roster (Adaptable, Illusion,
    /// Reattach, Steam Eruption).
    /// </summary>
    public virtual bool PreventsRemovalAfterDeath => false;

    /// <summary>
    /// Native <c>ShouldStopCombatFromEnding</c> for a power on a roster creature (Adaptable, Stock).
    /// </summary>
    public virtual bool PreventsCombatEnding => false;

    /// <summary>
    /// Native <c>PowerModel.ShouldPowerBeRemovedAfterOwnerDeath</c>, read by
    /// <c>SimulatedCombatState.RemovePowersAfterDeath</c>.
    /// </summary>
    public virtual bool ShouldPowerBeRemovedAfterOwnerDeath => true;

    /// <summary>
    /// The existing engine's death cleanup removes every power of a creature that already left the
    /// roster (the second <c>RemovePowersAfterDeath</c> loop). A later power-amount synchronization can
    /// still restore a power whose mirrored amount was pending; FlutterPower is the observed case and
    /// keeps its row here. Default false mirrors the cleanup.
    /// </summary>
    public virtual bool PowerRemainsAfterOwnerLeavesRoster => false;

    /// <summary>
    /// Native <c>PowerModel.ShouldOwnerDeathTriggerFatal</c> (true by default; MinionPower and a
    /// ReattachPower with living segments return false). HandOfGreed reads it before gaining gold.
    /// </summary>
    public virtual bool ShouldOwnerDeathTriggerFatal(in FlashBattle battle, FlashListener self) => true;

    /// <summary>Native <c>ITemporaryPower</c>: a temporary debuff survives an illusion owner's death.</summary>
    public virtual bool IsTemporaryPower => false;

    /// <summary>
    /// Native <c>ITemporaryPower.InternallyAppliedPower</c> as a Flash native id, used by Misery's
    /// cloned-temporary merge. A null value means the temporary power has no represented internal
    /// stat (no clone call site needs the merge).
    /// </summary>
    public virtual string? InternallyAppliedPowerId => null;

    /// <summary>
    /// Native <c>IllusionPower.ShouldPowerBeRemovedOnDeath</c>: while the dead owner has this power,
    /// the solver keeps its buffs (and temporary debuffs) and removes the other debuffs
    /// (<c>SimulatedCombatState.DeathLifecycle.cs</c>, <c>RemovePowersAfterDeath</c>).
    /// </summary>
    public virtual bool KeepsOwnerBuffsAfterDeath => false;

    /// <summary>
    /// <c>DeathPowerSupport.SpawnsPrimaryEnemyOnDeath</c>: a positive amount means the owner's death
    /// spawns new primary enemies, so <c>SimulatedCombatState.HasUnresolvedSpawningDeath</c> keeps the
    /// combat from ending until the deferred death pass runs (<c>InfestedPower</c>).
    /// </summary>
    public virtual bool SpawnsPrimaryEnemyOnDeath => false;

    /// <summary>
    /// Case in <c>DeathPowerSupport.Trigger</c> for one newly dead enemy, over every positive power in
    /// listener order before that death's power cleanup (<c>FlashKernel.ApplyEnemyDeathPowers</c>). This
    /// covers the deaths the existing engine settles in that pass (Reattach, Infested, Surrounded's
    /// facing update); the immediate <see cref="AfterDeath"/> hook keeps the native cases that pass
    /// does not move (Ravenous, Illusion).
    /// </summary>
    public virtual void DeathTrigger(ref FlashExec x, FlashListener self, byte dead)
    {
    }

    // Turn phases. Each method is the power's case in the named existing-engine pass; the kernel
    // visits only powers with a positive amount, in the pass's order (FlashKernel.TurnPhases).

    /// <summary>Case in <c>TurnStartPowerSupport.ApplyBeforeSideTurnStartPower</c>.</summary>
    public virtual void BeforeSideTurnStart(ref FlashExec x, FlashListener self, bool participates)
    {
    }

    /// <summary>Case in <c>SimulatedCombatState.TriggerBaseSideTurnStart</c>.</summary>
    public virtual void BaseSideTurnStart(ref FlashExec x, FlashListener self, bool decrementPlating)
    {
    }

    /// <summary>Case in <c>PersistentPowerSupport.TriggerOwnerAfterSideTurnStart</c>.</summary>
    public virtual void OwnerAfterSideTurnStart(ref FlashExec x, FlashListener self)
    {
    }

    /// <summary>Case in <c>TurnStartPowerSupport.TriggerAfterSideTurnStart</c>.</summary>
    public virtual void GlobalAfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
    }

    /// <summary>Case in <c>CorePowerSupport.TriggerAfterBlockCleared</c>.</summary>
    public virtual void AfterBlockCleared(ref FlashExec x, FlashListener self)
    {
    }

    /// <summary>Case in <c>EndTurnPowerSupport.TriggerRegular</c>.</summary>
    public virtual void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
    }

    /// <summary>Case in <c>EndTurnPowerSupport.TriggerBatch048</c>.</summary>
    public virtual void EndTurnBatch048(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
    }

    /// <summary>Case in <c>CorePowerSupport.TriggerPlayerRegularSideTurnEndEffects</c> before <c>TriggerRegular</c>.</summary>
    public virtual void PlayerSideTurnEnd(ref FlashExec x, FlashListener self)
    {
    }

    /// <summary>Per-enemy case in <c>CorePowerSupport.TriggerEnemySideTurnEndEffects</c> before <c>TriggerRegular</c>.</summary>
    public virtual void EnemySideTurnEnd(ref FlashExec x, FlashListener self)
    {
    }

    /// <summary>Per-player case in <c>CorePowerSupport.CompletePlayerEarlySideTurnEndEffects</c> (end of turn-end phase one).</summary>
    public virtual void CompletePlayerEarlySideTurnEnd(ref FlashExec x, FlashListener self)
    {
    }

    /// <summary>Case in <c>PersistentPowerSupport.TriggerAfterEnergyReset</c>.</summary>
    public virtual void AfterEnergyReset(ref FlashExec x, FlashListener self)
    {
    }

    /// <summary>Case in <c>TurnStartPowerSupport.ContinueBeforeHandDraw</c>.</summary>
    public virtual void BeforeHandDraw(ref FlashExec x, FlashListener self)
    {
    }

    /// <summary>Case in <c>TurnStartPowerSupport.ContinueAfterPlayerTurnStart</c>.</summary>
    public virtual void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
    }

    /// <summary>
    /// Case in <c>CorePowerSupport.ApplyEnemyDeathPowers</c> after a newly dead enemy's death powers:
    /// this power's applier is that enemy (Constrict, Hex and Shrink on player creatures, then Magic
    /// Bomb). Called whatever the sign of the amount, as the existing engine reads the power directly.
    /// </summary>
    public virtual void ApplierDied(ref FlashExec x, FlashListener self)
    {
    }
}

/// <summary>A relic. Turn-phase methods are its cases in the named <c>TurnStartRelicSupport</c>/<c>PersistentRelicSupport</c> batches.</summary>
public abstract class FlashRelicDef : FlashListenerDef
{
    /// <summary>This relic can create a card with per-object identity (Toolbox's colourless choice).</summary>
    public virtual bool MayGenerateIdentityContent => false;

    /// <summary>This relic has per-object identity state (Vambrace/UnsettlingLamp).</summary>
    public virtual bool IsIdentityContent => false;

    /// <summary>Native <c>UnsettlingLamp</c> (the only listener of the two power-amount ext hooks).</summary>
    public virtual bool IsUnsettlingLamp => false;

    public virtual void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
    }

    public virtual void AfterEnergyReset(ref FlashExec x, FlashListener self)
    {
    }

    public virtual void AfterEnergyResetLate(ref FlashExec x, FlashListener self)
    {
    }

    public virtual void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
    }

    public virtual void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
    }

    /// <summary>
    /// Case in <c>TurnStartRelicSupport.TriggerBeforeSideTurnEnd</c> (<c>PrepareRelicsBeforeSideTurnEnd</c>), the
    /// relic batch at the start of the player's turn end; not the <c>BeforeSideTurnEnd</c> hook passes.
    /// </summary>
    public virtual void PrepareBeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side)
    {
    }

    public virtual void AfterSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side)
    {
    }

    public virtual void AfterBlockCleared(ref FlashExec x, FlashListener self)
    {
    }

}

/// <summary>
/// A potion. Its target type and variables come from <see cref="FlashRules.Potion"/>; targets are
/// enumerated as <c>CombatBeamSolver.TargetsForPotion</c> does.
/// </summary>
public abstract class FlashPotionDef : FlashListenerDef
{
    /// <summary>
    /// <c>PotionOnUseSupport.CanSearch</c>: whether the existing search offers explicit use. A potion it does not
    /// offer still needs a definition to be held in a slot, but produces no action.
    /// </summary>
    public virtual bool Searchable => true;

    /// <summary>
    /// True when this potion can create a random potion with
    /// <c>PotionFactory.CreateRandomPotionOutOfCombat</c> (EntropicBrew): the rule compiler freezes the
    /// unfiltered character + shared potion pool, including potions that cannot be generated in combat.
    /// </summary>
    public virtual bool GeneratesOutOfCombatPotion => false;

    /// <summary>
    /// The potion's case in <c>PotionOnUseSupport.Use</c>. <see cref="FlashExec.Target"/> holds the chosen
    /// enemy, or <see cref="FlashLimits.NoOwner"/> for self, all-enemy and untargeted potions.
    /// </summary>
    public abstract void OnUse(ref FlashExec x);
}

/// <summary>
/// A native orb (<c>OrbModel</c>). The orb queue is the player's, in queue order. <see cref="FlashOrb.Aux"/>
/// carries the one private decimal each vanilla orb can accumulate (Dark's <c>_evokeVal</c>, Glass's
/// <c>_passiveVal</c>); the other types ignore it. Base values live in the definition because they are
/// native code constants, not model data.
/// </summary>
public abstract class FlashOrbDef : FlashListenerDef
{
    /// <summary>
    /// <c>OrbQueue.BeforeTurnEnd</c> → <c>OrbModel.BeforeTurnEndOrbTrigger</c>. The default triggers the
    /// passive through <c>TriggerPassive</c> (which applies <c>ModifyOrbPassiveTriggerCounts</c>);
    /// Plasma overrides it with an empty trigger.
    /// </summary>
    public virtual void BeforeTurnEndOrbTrigger(ref FlashExec x, int orbIndex)
        => FlashKernel.TriggerOrbPassive(ref x, orbIndex);

    /// <summary><c>OrbQueue.AfterTurnStart</c> → <c>OrbModel.AfterTurnStartOrbTrigger</c> (Plasma).</summary>
    public virtual void AfterTurnStartOrbTrigger(ref FlashExec x, int orbIndex)
    {
    }

    /// <summary>
    /// Value of <see cref="FlashOrb.Aux"/> for a newly created orb: Dark starts its evoke accumulation
    /// at 6, Glass its passive at 4 (native <c>DarkOrb._evokeVal</c> / <c>GlassOrb._passiveVal</c>).
    /// </summary>
    public virtual int InitialAux => 0;

    /// <summary><c>OrbModel.PassiveVal</c> before the <c>ModifyOrbValue</c> listeners.</summary>
    public virtual decimal BasePassiveValue(in FlashBattle battle, in FlashOrb orb) => 0m;

    /// <summary><c>OrbModel.EvokeVal</c> before the <c>ModifyOrbValue</c> listeners.</summary>
    public virtual decimal BaseEvokeValue(in FlashBattle battle, in FlashOrb orb) => 0m;

    /// <summary>
    /// Whether <c>EvokeVal</c> runs the <c>ModifyOrbValue</c> listeners. DarkOrb.EvokeVal returns the
    /// raw <c>_evokeVal</c> (Focus only affects its passive accumulation), while the other four evoke
    /// values are modified.
    /// </summary>
    public virtual bool EvokeUsesModifyOrbValue => true;

    /// <summary>
    /// <c>OrbModel.Passive</c>: mutate the battle (or the orb's private value at
    /// <paramref name="orbIndex"/>) for one trigger. The kernel verifies the slot still holds the same
    /// value before calling; a queue change during a passive is an explicit boundary.
    /// </summary>
    public virtual void Passive(ref FlashExec x, int orbIndex, byte target)
    {
    }

    /// <summary>
    /// <c>OrbModel.Evoke</c>: return the number of owner indexes written to <paramref name="targets"/>
    /// (capacity <see cref="FlashLimits.Enemies"/> + 1). The caller dispatches
    /// <c>AfterOrbEvoked</c> over them afterwards.
    /// </summary>
    public virtual int Evoke(ref FlashExec x, in FlashOrb orb, scoped Span<byte> targets) => 0;

    /// <summary>
    /// <c>OrbRetentionValue</c>'s per-orb multiplier: Glass counts once per living enemy, the rest once.
    /// </summary>
    public virtual int RetentionMultiplier(in FlashBattle battle, int aliveEnemyCount) => 1;

    /// <summary>
    /// <c>OrbMirrors.GetPassiveValue</c> runs <c>ModifyOrbValue</c> for Lightning, Frost, Dark and
    /// Glass, but returns <c>PlasmaOrb.PassiveVal</c> (1) directly. The evaluation's orb retention
    /// value uses this query, so Plasma must not receive Focus.
    /// </summary>
    public virtual bool PassiveUsesModifyOrbValue => true;
}

/// <summary>
/// A card enchantment. The modification methods mirror <c>EnchantmentModel.EnchantDamage*</c> and
/// <c>EnchantBlock*</c>, which the existing engine calls before every other damage/block modifier
/// (<c>HookMirrors.ModifyDamage</c>/<c>ModifyBlock</c>), and <c>EnchantPlayCount</c>, which
/// <c>CardModel.GetEnchantedReplayCount</c> applies before the play-count hooks. They are pure
/// queries over the card value: prediction and evaluation call them read-only.
/// </summary>
public abstract class FlashEnchantmentDef : FlashListenerDef
{
    public virtual decimal EnchantDamageAdditive(in FlashCard card, decimal damage, FlashValueProps props) => 0m;

    public virtual decimal EnchantDamageMultiplicative(in FlashCard card, decimal damage, FlashValueProps props) => 1m;

    public virtual decimal EnchantBlockAdditive(in FlashCard card, decimal block) => 0m;

    public virtual decimal EnchantBlockMultiplicative(in FlashCard card, decimal block) => 1m;

    /// <summary><c>CardModel.GetEnchantedReplayCount</c>: the base replay count with this enchantment's modifier.</summary>
    public virtual int EnchantPlayCount(in FlashCard card, int replayCount) => replayCount;

    /// <summary>
    /// <c>EnchantmentModel.OnPlay</c> in the card-play tail, before the affliction's OnPlay and before
    /// <c>AfterCardPlayed</c> (<c>CombatPredictionSimulator.CardTailContinuation</c>).
    /// </summary>
    public virtual void OnPlay(ref FlashExec x, in FlashCard card, byte target)
    {
    }
}

/// <summary>
/// A card affliction. Vanilla afflictions are markers: their card-type/stacking rules govern
/// <c>CardCmd.Afflict</c>, and the powers that apply them carry the behavior. <c>OnPlay</c> is the
/// empty base callback of <c>AfflictionModel.OnPlay</c>.
/// </summary>
public abstract class FlashAfflictionDef : FlashListenerDef
{
    /// <summary>Native <c>AfflictionModel.IsStackable</c>: a second application of the same type adds its amount.</summary>
    public virtual bool IsStackable => false;

    /// <summary>Native <c>AfflictionModel.CanAfflictUnplayableCards</c>.</summary>
    public virtual bool CanAfflictUnplayableCards => true;

    /// <summary>Native <c>AfflictionModel.CanAfflictCardType</c>.</summary>
    public virtual bool CanAfflictCardType(FlashCardType type) => true;

    /// <summary><c>AfflictionModel.OnPlay</c> in the card-play tail, after the enchantment's OnPlay.</summary>
    public virtual void OnPlay(ref FlashExec x, in FlashCard card, byte target)
    {
    }
}

/// <summary>
/// A monster: its non-attack move effects and the special cases the existing engine applies when
/// advancing its state machine.
/// </summary>
public abstract class FlashMonsterDef : FlashListenerDef
{
    /// <summary>Static integers read from the native monster, compiled into <see cref="FlashMonsterRules.StaticInts"/>.</summary>
    public virtual IReadOnlyList<string> StaticIntNames => [];

    /// <summary>
    /// Root-frozen power amounts (native power ids) compiled into
    /// <see cref="FlashMonsterRules.RootPowerAmounts"/>, for dynamic move damage whose baseline is the
    /// amount at root import (<c>SimulatedCombatState._rootPowerAmounts</c>).
    /// </summary>
    public virtual IReadOnlyList<string> RootPowerAmountNames => [];

    /// <summary>
    /// Native ids of the monster types this monster can summon. The rule compiler closes over them
    /// transitively and compiles one rule entry per type.
    /// </summary>
    public virtual IReadOnlyList<string> SpawnedMonsterIds => [];

    /// <summary>
    /// Monster-private integer names (<c>SimulatedCombatState._monsterIntStates</c>) this monster
    /// reads. Each name maps to a <see cref="FlashEnemy.Ai"/> register in declaration order; the
    /// bridge imports the captured value and the definition keeps the register up to date.
    /// </summary>
    public virtual IReadOnlyList<string> AiRegisterNames => [];

    /// <summary>
    /// Monster-private integer names that exist in the existing engine's fingerprint or continuation
    /// text but that no resolution path reads. The bridge accepts their captured entries without
    /// projecting them; any other unclaimed entry rejects the root.
    /// </summary>
    public virtual IReadOnlyList<string> InertMonsterIntNames => [];

    /// <summary>
    /// True when this monster carries an illusion revive: the rule compiler appends the synthetic
    /// REVIVE_MOVE node, and <see cref="FlashKernel.BeginIllusionRevive"/> may force it.
    /// </summary>
    public virtual bool IllusionRevive => false;

    /// <summary>
    /// Powers a newly summoned monster receives before its first action, mirroring
    /// <c>MonsterSpawnSupport.ApplyNativeEntrancePowers</c>.
    /// </summary>
    public virtual void ApplyEntrancePowers(ref FlashExec x, byte owner)
    {
    }

    /// <summary>
    /// <see cref="ApplyEntrancePowers(ref FlashExec, byte)"/> with the summoning owner, for entrance
    /// values that depend on the source (Axebot's replacement StockPower).
    /// </summary>
    public virtual void ApplyEntrancePowers(ref FlashExec x, byte owner, byte source)
        => ApplyEntrancePowers(ref x, owner);

    /// <summary>
    /// Native <c>MonsterModel.StockAmount</c> used when the rule compiler creates this type's spawn
    /// rule entry, or -1 to use the canonical model. Axebot's replacement starts from BOOT_UP_MOVE
    /// and a +10 HP range, which requires a non-null StockAmount at compile time.
    /// </summary>
    public virtual int SpawnStockAmount => -1;

    /// <summary>
    /// The part of a move the existing engine runs before its attacks
    /// (<c>MonsterMoveEffects.ApplyBeforeAttack</c>), for example Living Fog's BLOAT_MOVE summon.
    /// </summary>
    public virtual void BeforeMove(ref FlashExec x, byte owner, int moveNode)
    {
    }

    /// <summary>
    /// Non-attack part of a move, run after its attack hits, mirroring
    /// <c>MonsterMoveEffects.Apply</c>. Moves without effects do nothing.
    /// </summary>
    public virtual void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
    }

    /// <summary>
    /// Test Subject style forms: the scorer's effective HP adds the later forms' HP from the AI
    /// registers while AdaptablePower keeps the body in combat.
    /// </summary>
    public virtual bool HasAdaptableFormHp => false;

    /// <summary>
    /// Special attack damage (<c>SimulatedCombatState.AdjustMonsterMoveDamage</c>): the compiled base
    /// damage plus the move's dynamic adjustment, clamped to zero by the damage pipeline.
    /// </summary>
    public virtual int ModifyAttackDamage(in FlashBattle battle, FlashRules rules, byte owner, int moveNode,
        int attackIndex, int damage) => damage;

    /// <summary>Special attack hit count (<c>BranchMonsterAi.CurrentMove</c>, Test Subject's MULTI_CLAW).</summary>
    public virtual int ModifyAttackRepeats(in FlashBattle battle, FlashRules rules, byte owner, int moveNode,
        int attackIndex, int repeats) => repeats;

    /// <summary>
    /// Special conditional-branch resolution (<c>BranchMonsterAi.ResolveConditional</c>); return -1
    /// to use the root-resolved target.
    /// </summary>
    public virtual int ResolveConditional(ref FlashExec x, byte owner, int branchNode) => -1;

    /// <summary>Special random-branch weights (<c>BranchMonsterAi.GetBranchWeight</c>).</summary>
    public virtual float BranchWeight(ref FlashExec x, byte owner, int branchNode, int option, float baseWeight) => baseWeight;

    /// <summary>
    /// The encounter slot the bridge uses when compiling this type's spawn rule entry, or null to use
    /// the first free slot. A monster whose native initial state depends on its slot name (Wriggler's
    /// INIT_MOVE) needs a valid fixed name so the private compile never sees an unmatched conditional.
    /// </summary>
    public virtual string? SpawnSlotName => null;

    /// <summary>
    /// The native model is created with <c>StartStunned = true</c>: its initial state machine node is
    /// the SPAWNED_MOVE move state instead of the INIT_MOVE conditional. The bridge applies this while
    /// compiling the spawn rule entry; the kernel spawns from that entry unchanged.
    /// </summary>
    public virtual bool SpawnedStunned => false;

    /// <summary>
    /// The native <c>ResolveReviveMove</c> case of a move, run before <see cref="ApplyMoveEffects"/>
    /// (<c>MonsterMoveEffects.Apply</c> starts with it). The default REVIVE_MOVE case is handled by the
    /// kernel; Reattach and Test Subject override this.
    /// </summary>
    public virtual void ResolveReviveMove(ref FlashExec x, byte owner, int moveNode)
    {
    }

    /// <summary>
    /// The move that <c>ForceStunnedMove</c> uses after a fully blocked attack while the owner has
    /// ImbalancedPower. <c>MonsterMoveSemantics</c> passes <c>HEADBUTT_MOVE</c> for BowlbugRock and the
    /// native default (most recent logged move) otherwise; -1 selects that default.
    /// </summary>
    public virtual int ImbalancedStunFollowUp(in FlashMonsterRules rules) => -1;
}

/// <summary>
/// Registry of all Flash content definitions in this assembly. Ids are assigned per kind in
/// ordinal order of native ids.
/// </summary>
public static class FlashRegistry
{
    private static readonly Lazy<Tables> Data = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    public static int CardCount => Data.Value.Cards.Length;

    /// <summary>Union of the hooks any registered definition overrides; dispatch skips the rest outright.</summary>
    public static FlashHookMask AnyHooks => Data.Value.AnyHooks;

    /// <summary>Union of the <see cref="FlashHookMaskExt"/> hooks any registered definition overrides.</summary>
    public static FlashHookMaskExt AnyHooksExt => Data.Value.AnyHooksExt;

    /// <summary>Union of the hooks implemented by card, affliction and enchantment definitions.</summary>
    public static FlashHookMask CardHooks => Data.Value.CardHooks;

    public static FlashHookMaskExt CardHooksExt => Data.Value.CardHooksExt;

    public static int PowerCount => Data.Value.Powers.Length;

    public static int RelicCount => Data.Value.Relics.Length;

    public static int MonsterCount => Data.Value.Monsters.Length;

    public static int PotionCount => Data.Value.Potions.Length;

    public static FlashCardDef Card(ushort id) => Data.Value.Cards[id];

    public static FlashPowerDef Power(ushort id) => Data.Value.Powers[id];

    public static FlashRelicDef Relic(ushort id) => Data.Value.Relics[id];

    public static FlashPotionDef Potion(ushort id) => Data.Value.Potions[id];

    public static FlashMonsterDef Monster(ushort id) => Data.Value.Monsters[id];

    public static FlashOrbDef Orb(ushort id) => Data.Value.Orbs[id];

    public static FlashEnchantmentDef Enchantment(ushort id) => Data.Value.Enchantments[id];

    public static FlashAfflictionDef Affliction(ushort id) => Data.Value.Afflictions[id];

    public static bool TryCard(string nativeId, out ushort id) => Data.Value.CardIds.TryGetValue(nativeId, out id);

    public static bool TryPower(string nativeId, out ushort id) => Data.Value.PowerIds.TryGetValue(nativeId, out id);

    public static bool TryRelic(string nativeId, out ushort id) => Data.Value.RelicIds.TryGetValue(nativeId, out id);

    public static bool TryPotion(string nativeId, out ushort id) => Data.Value.PotionIds.TryGetValue(nativeId, out id);

    public static bool TryMonster(string nativeId, out ushort id) => Data.Value.MonsterIds.TryGetValue(nativeId, out id);

    public static bool TryOrb(string nativeId, out ushort id) => Data.Value.OrbIds.TryGetValue(nativeId, out id);

    public static bool TryEnchantment(string nativeId, out ushort id) => Data.Value.EnchantmentIds.TryGetValue(nativeId, out id);

    public static bool TryAffliction(string nativeId, out ushort id) => Data.Value.AfflictionIds.TryGetValue(nativeId, out id);

    public static ushort PowerId<T>() where T : FlashPowerDef => PowerIdCache<T>.Id;

    public static ushort CardId<T>() where T : FlashCardDef => CardIdCache<T>.Id;

    public static ushort MonsterId<T>() where T : FlashMonsterDef => MonsterIdCache<T>.Id;

    public static ushort AfflictionId<T>() where T : FlashAfflictionDef => AfflictionIdCache<T>.Id;

    public static ushort EnchantmentId<T>() where T : FlashEnchantmentDef => EnchantmentIdCache<T>.Id;

    private static class PowerIdCache<T> where T : FlashPowerDef
    {
        public static readonly ushort Id = Data.Value.ByType.TryGetValue(typeof(T), out FlashContentDef? def)
            ? def.Id
            : throw new InvalidOperationException($"{typeof(T).Name} 没有登记为 Flash Power。");
    }

    private static class CardIdCache<T> where T : FlashCardDef
    {
        public static readonly ushort Id = Data.Value.ByType.TryGetValue(typeof(T), out FlashContentDef? def)
            ? def.Id
            : throw new InvalidOperationException($"{typeof(T).Name} 没有登记为 Flash 卡牌。");
    }

    private static class MonsterIdCache<T> where T : FlashMonsterDef
    {
        public static readonly ushort Id = Data.Value.ByType.TryGetValue(typeof(T), out FlashContentDef? def)
            ? def.Id
            : throw new InvalidOperationException($"{typeof(T).Name} 没有登记为 Flash 怪物。");
    }

    private static class AfflictionIdCache<T> where T : FlashAfflictionDef
    {
        public static readonly ushort Id = Data.Value.ByType.TryGetValue(typeof(T), out FlashContentDef? def)
            ? def.Id
            : throw new InvalidOperationException($"{typeof(T).Name} 没有登记为 Flash 苦难。");
    }

    private static class EnchantmentIdCache<T> where T : FlashEnchantmentDef
    {
        public static readonly ushort Id = Data.Value.ByType.TryGetValue(typeof(T), out FlashContentDef? def)
            ? def.Id
            : throw new InvalidOperationException($"{typeof(T).Name} 没有登记为 Flash 附魔。");
    }

    private sealed record Tables(
        FlashCardDef[] Cards, Dictionary<string, ushort> CardIds,
        FlashPowerDef[] Powers, Dictionary<string, ushort> PowerIds,
        FlashRelicDef[] Relics, Dictionary<string, ushort> RelicIds,
        FlashPotionDef[] Potions, Dictionary<string, ushort> PotionIds,
        FlashMonsterDef[] Monsters, Dictionary<string, ushort> MonsterIds,
        FlashOrbDef[] Orbs, Dictionary<string, ushort> OrbIds,
        FlashEnchantmentDef[] Enchantments, Dictionary<string, ushort> EnchantmentIds,
        FlashAfflictionDef[] Afflictions, Dictionary<string, ushort> AfflictionIds,
        Dictionary<Type, FlashContentDef> ByType)
    {
        public FlashHookMask AnyHooks { get; } = ByType.Values.Aggregate(FlashHookMask.None, (mask, def) => mask | def.Hooks);

        public FlashHookMaskExt AnyHooksExt { get; } = ByType.Values.Aggregate(FlashHookMaskExt.None, (mask, def) => mask | def.HooksExt);

        /// <summary>
        /// Hooks any card, affliction or enchantment definition can answer. The listener scan only
        /// visits the card zone when the requested hook intersects this union; the per-card
        /// <c>Has</c> test would reject every card otherwise.
        /// </summary>
        public FlashHookMask CardHooks { get; } = Cards.Aggregate(FlashHookMask.None, (mask, def) => mask | def.Hooks)
            | Afflictions.Aggregate(FlashHookMask.None, (mask, def) => mask | def.Hooks)
            | Enchantments.Aggregate(FlashHookMask.None, (mask, def) => mask | def.Hooks);

        public FlashHookMaskExt CardHooksExt { get; } =
            Cards.Aggregate(FlashHookMaskExt.None, (mask, def) => mask | def.HooksExt)
            | Afflictions.Aggregate(FlashHookMaskExt.None, (mask, def) => mask | def.HooksExt)
            | Enchantments.Aggregate(FlashHookMaskExt.None, (mask, def) => mask | def.HooksExt);
    }

    private static Tables Build()
    {
        Type[] types = typeof(FlashRegistry).Assembly.GetTypes();
        Dictionary<Type, FlashContentDef> byType = [];
        var cards = Collect<FlashCardDef>(types, byType, ushort.MaxValue);
        var powers = Collect<FlashPowerDef>(types, byType, ushort.MaxValue);
        var relics = Collect<FlashRelicDef>(types, byType, ushort.MaxValue);
        var potions = Collect<FlashPotionDef>(types, byType, ushort.MaxValue);
        var monsters = Collect<FlashMonsterDef>(types, byType, ushort.MaxValue);
        var orbs = Collect<FlashOrbDef>(types, byType, byte.MaxValue);
        var enchantments = Collect<FlashEnchantmentDef>(types, byType, byte.MaxValue);
        var afflictions = Collect<FlashAfflictionDef>(types, byType, byte.MaxValue);
        return new Tables(
            cards.Defs, cards.Ids, powers.Defs, powers.Ids, relics.Defs, relics.Ids,
            potions.Defs, potions.Ids, monsters.Defs, monsters.Ids, orbs.Defs, orbs.Ids,
            enchantments.Defs, enchantments.Ids, afflictions.Defs, afflictions.Ids, byType);
    }

    private static (T[] Defs, Dictionary<string, ushort> Ids) Collect<T>(
        Type[] types, Dictionary<Type, FlashContentDef> byType, int maxCount)
        where T : FlashContentDef
    {
        List<(string NativeId, Type Type)> found = [];
        foreach (Type type in types)
        {
            if (!typeof(T).IsAssignableFrom(type) || type.IsAbstract)
                continue;
            FlashContentAttribute attribute = type.GetCustomAttribute<FlashContentAttribute>()
                ?? throw new InvalidOperationException($"Flash 定义 {type.FullName} 缺少 [FlashContent]。");
            found.Add((attribute.NativeId, type));
        }
        found.Sort((left, right) => string.CompareOrdinal(left.NativeId, right.NativeId));
        if (found.Count > maxCount)
            throw new FlashCapacityException(typeof(T).Name, maxCount);
        T[] defs = new T[found.Count];
        Dictionary<string, ushort> ids = new(found.Count, StringComparer.Ordinal);
        for (int index = 0; index < found.Count; index++)
        {
            (string nativeId, Type type) = found[index];
            T def = (T)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"无法创建 Flash 定义 {type.FullName}。"));
            def.NativeId = nativeId;
            def.Id = (ushort)index;
            def.Hooks = (FlashHookMask)ComputeHooks<FlashHookMask>(type);
            def.HooksExt = (FlashHookMaskExt)ComputeHooks<FlashHookMaskExt>(type);
            if (!ids.TryAdd(nativeId, (ushort)index))
                throw new InvalidOperationException($"原版 Id {nativeId} 有多个 Flash {typeof(T).Name} 定义。");
            defs[index] = def;
            byType.Add(type, def);
        }
        return (defs, ids);
    }

    /// <summary>
    /// Hooks <paramref name="type"/> overrides: for each mask name, the virtual declared on
    /// <see cref="FlashListenerDef"/> and whether the type overrides exactly that method (overloads with
    /// the same name on derived bases, such as phase methods, do not count).
    /// </summary>
    private static ulong ComputeHooks<TMask>(Type type) where TMask : struct, Enum
    {
        ulong mask = 0;
        MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        foreach (TMask hook in Enum.GetValues<TMask>())
        {
            ulong bit = Convert.ToUInt64(hook);
            // Composite masks (RunListenerHooks and similar) name no single hook.
            if (System.Numerics.BitOperations.PopCount(bit) != 1)
                continue;
            MethodInfo baseMethod = typeof(FlashListenerDef).GetMethod(hook.ToString(), BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"FlashListenerDef 没有与 Hook {hook} 同名的方法。");
            foreach (MethodInfo method in methods)
            {
                if (method.DeclaringType != typeof(FlashListenerDef) && method.GetBaseDefinition() == baseMethod)
                {
                    mask |= bit;
                    break;
                }
            }
        }
        return mask;
    }
}
