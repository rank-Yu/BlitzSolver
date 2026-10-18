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
}

public abstract class FlashContentDef
{
    public string NativeId { get; internal set; } = "";

    public ushort Id { get; internal set; }

    public FlashHookMask Hooks { get; internal set; }

    /// <summary>Native dynamic-variable names compiled into the rule table, in order.</summary>
    public virtual IReadOnlyList<string> VarNames => [];
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
}

public abstract class FlashCardDef : FlashListenerDef
{
    /// <summary>
    /// Card effect in the existing engine's order: mirrored attack/block/draw first, then the
    /// compensated power/resource/generation effects.
    /// </summary>
    public abstract void OnPlay(ref FlashExec x);

    /// <summary>Card-specific playability (<c>CardIsPlayableMirrors</c>); most cards are always playable.</summary>
    public virtual bool IsPlayable(in FlashBattle battle, FlashRules rules, in FlashCard card) => true;
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
}

public abstract class FlashRelicDef : FlashListenerDef;

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

    public static int PowerCount => Data.Value.Powers.Length;

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
        Dictionary<Type, FlashContentDef> ByType);

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
