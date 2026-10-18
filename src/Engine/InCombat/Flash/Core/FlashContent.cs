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
    ShouldClearBlock = 1UL << 14,
    ModifyMaxEnergy = 1UL << 15,
    ModifyHandDraw = 1UL << 16,
    ModifyHandDrawLate = 1UL << 17,
}

public abstract class FlashContentDef
{
    public string NativeId { get; internal set; } = "";

    public ushort Id { get; internal set; }

    public FlashHookMask Hooks { get; internal set; }

    /// <summary>Native dynamic-variable names compiled into the rule table, in order.</summary>
    public virtual IReadOnlyList<string> VarNames => [];

    /// <summary>
    /// Native card ids (with upgrade level) this content can put into combat. The rule compiler adds
    /// their canonical stats, and admission rejects the root when any of them has no Flash definition.
    /// </summary>
    public virtual IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [];
}

/// <summary>
/// Base of every definition that can listen to hooks. Hook methods are named after the native
/// hook and receive the listener identity so the definition can read its own state.
/// Definitions are stateless; every mutable value lives in <see cref="FlashBattle"/>.
/// </summary>
public abstract class FlashListenerDef : FlashContentDef
{
    public virtual decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self, in FlashDamageContext damage, decimal amount) => 0m;

    public virtual decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self, in FlashDamageContext damage, decimal amount) => 1m;

    public virtual decimal ModifyDamageCap(ref FlashExec x, FlashListener self, in FlashDamageContext damage) => decimal.MaxValue;

    public virtual decimal ModifyBlockAdditive(ref FlashExec x, FlashListener self, in FlashBlockContext block, decimal amount) => 0m;

    public virtual decimal ModifyBlockMultiplicative(ref FlashExec x, FlashListener self, in FlashBlockContext block, decimal amount) => 1m;

    // Action hooks. Each dispatch point in the kernel names the existing-engine call it mirrors.

    public virtual void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
    }

    public virtual void AfterDamageReceivedLate(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
    }

    public virtual void AfterDamageGiven(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
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

    public virtual void AfterBlockGained(ref FlashExec x, FlashListener self, byte owner, decimal amount, FlashValueProps props, bool fromCard)
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

    public virtual decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount) => amount;

    public virtual decimal ModifyHandDrawLate(ref FlashExec x, FlashListener self, decimal amount) => amount;
}

public abstract class FlashCardDef : FlashListenerDef
{
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

    /// <summary>Card-specific playability (<c>CardIsPlayableMirrors</c>); most cards are always playable.</summary>
    public virtual bool IsPlayable(in FlashBattle battle, FlashRules rules, in FlashCard card) => true;

    /// <summary>
    /// Card-specific adjustment when this card enters combat from outside the combat piles
    /// (<c>SimulatedCombatState.AfterCardEnteredCombat</c>, for example Stomp or Pinpoint cost changes).
    /// </summary>
    public virtual void AfterEnteredCombat(ref FlashExec x, FlashPile pile, int index)
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
    public abstract FlashPowerType Type { get; }

    public abstract FlashPowerStackType StackType { get; }

    public virtual bool AllowNegative => false;

    /// <summary>Native instanced powers get a new instance per application instead of stacking.</summary>
    public virtual bool IsInstanced => false;

    /// <summary>Native <c>GetTypeForAmount</c>: a negative amount of a buff that allows negatives is a debuff.</summary>
    public virtual FlashPowerType TypeForAmount(int amount)
        => AllowNegative && amount < 0 && Type == FlashPowerType.Buff ? FlashPowerType.Debuff : Type;

    /// <summary>A newly applied player-side instance skips its first duration tick (Weak, Vulnerable, Frail).</summary>
    public virtual bool UsesNativeDurationSkip => false;

    /// <summary>Ticks down at the end of the enemy side in <c>CorePowerSupport.TickDurations</c>.</summary>
    public virtual bool TicksAtEnemySideEnd => false;

    /// <summary>Native <c>PowerModel.OwnerIsSecondaryEnemy</c>: a positive amount makes its owner a secondary enemy.</summary>
    public virtual bool OwnerIsSecondaryEnemy => false;

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
}

/// <summary>A relic. Turn-phase methods are its cases in the named <c>TurnStartRelicSupport</c>/<c>PersistentRelicSupport</c> batches.</summary>
public abstract class FlashRelicDef : FlashListenerDef
{
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

    public virtual void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side)
    {
    }

    public virtual void AfterSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side)
    {
    }

    public virtual void AfterBlockCleared(ref FlashExec x, FlashListener self)
    {
    }
}

public abstract class FlashPotionDef : FlashListenerDef;

public abstract class FlashOrbDef : FlashListenerDef;

public abstract class FlashEnchantmentDef : FlashListenerDef;

public abstract class FlashAfflictionDef : FlashListenerDef;

/// <summary>
/// A monster: its non-attack move effects and the special cases the existing engine applies when
/// advancing its state machine.
/// </summary>
public abstract class FlashMonsterDef : FlashListenerDef
{
    /// <summary>Static integers read from the native monster, compiled into <see cref="FlashMonsterRules.StaticInts"/>.</summary>
    public virtual IReadOnlyList<string> StaticIntNames => [];

    /// <summary>
    /// Non-attack part of a move, run after its attack hits, mirroring
    /// <c>MonsterMoveEffects.Apply</c>. Moves without effects do nothing.
    /// </summary>
    public virtual void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
    }

    /// <summary>
    /// Special conditional-branch resolution (<c>BranchMonsterAi.ResolveConditional</c>); return -1
    /// to use the root-resolved target.
    /// </summary>
    public virtual int ResolveConditional(ref FlashExec x, byte owner, int branchNode) => -1;

    /// <summary>Special random-branch weights (<c>BranchMonsterAi.GetBranchWeight</c>).</summary>
    public virtual float BranchWeight(ref FlashExec x, byte owner, int branchNode, int option, float baseWeight) => baseWeight;
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

    public static int PowerCount => Data.Value.Powers.Length;

    public static int RelicCount => Data.Value.Relics.Length;

    public static int MonsterCount => Data.Value.Monsters.Length;

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
            def.Hooks = ComputeHooks(type);
            if (!ids.TryAdd(nativeId, (ushort)index))
                throw new InvalidOperationException($"原版 Id {nativeId} 有多个 Flash {typeof(T).Name} 定义。");
            defs[index] = def;
            byType.Add(type, def);
        }
        return (defs, ids);
    }

    private static FlashHookMask ComputeHooks(Type type)
    {
        FlashHookMask mask = FlashHookMask.None;
        foreach (FlashHookMask hook in Enum.GetValues<FlashHookMask>())
        {
            if (hook == FlashHookMask.None)
                continue;
            MethodInfo? method = type.GetMethod(hook.ToString(), BindingFlags.Public | BindingFlags.Instance);
            if (method != null && method.GetBaseDefinition().DeclaringType != method.DeclaringType)
                mask |= hook;
        }
        return mask;
    }
}
