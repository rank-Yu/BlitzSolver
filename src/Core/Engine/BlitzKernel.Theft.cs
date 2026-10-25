namespace BlitzSolver.Engine;

public static partial class BlitzKernel
{
    /// <summary>AI register of Waterfall Giant's <c>_pressureGunBonus</c> (definition declaration order).</summary>
    public const int PressureGunBonusRegister = 0;

    /// <summary>AI register of Waterfall Giant's <c>_steamEruptionDamage</c>.</summary>
    public const int SteamEruptionDamageRegister = 1;

    /// <summary>Mirrors <c>SimulatedCombatState.OutstandingStolenResource</c>.</summary>
    public static int OutstandingStolenResource(in BlitzBattle battle)
        => checked(battle.Counter(BlitzCounter.OutstandingStolenGold)
            + battle.Counter(BlitzCounter.OutstandingStolenCards));

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.MinimumOutstandingStolenResource</c>: every resource held by a
    /// present living enemy is still recoverable by killing it; only irrecoverable loot remains.
    /// </summary>
    public static int MinimumOutstandingStolenResource(in BlitzBattle battle)
    {
        int outstanding = OutstandingStolenResource(battle);
        long recoverable = 0;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly BlitzPower power = ref battle.Powers[index];
            if (power.Amount <= 0 || !IsEnemyOwner(power.Owner))
                continue;
            if (!InRoster(battle, power.Owner) || !battle.Creature(power.Owner).IsAlive)
                continue;
            string nativeId = BlitzRegistry.Power(power.Id).NativeId;
            if (nativeId == "SWIPE_POWER")
            {
                if (power.Aux0 != 0)
                    recoverable++;
            }
            else if (nativeId == "HEIST_POWER")
            {
                recoverable += Math.Max(0, power.Amount);
            }
            else if (nativeId == "THIEVERY_POWER")
            {
                recoverable += Math.Max(0, power.Aux0);
            }
        }
        return (int)Math.Max(0, outstanding - recoverable);
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.RecordThievery</c> (GremlinMerc's GIMME/DOUBLE_SMASH/HEHE):
    /// the first <c>ThieveryPower</c> instance of <paramref name="owner"/> steals
    /// <c>min(power.Amount, player gold)</c>, adds it to its private gold ledger
    /// (<see cref="BlitzPower.Aux0"/>) and to the outstanding resource counter, and removes it from
    /// the player's gold. A missing target, a dead player or zero gold is a no-op, like the existing
    /// engine.
    /// </summary>
    public static void RecordThievery(ref BlitzExec x, byte owner)
    {
        if (!BlitzRegistry.TryPower("THIEVERY_POWER", out ushort id))
            throw new BlitzUnsupportedException("偷窃需要 THIEVERY_POWER 的 Blitz 定义。");
        int index = FindPower(x.B, owner, id);
        if (index < 0)
            return;
        ref BlitzPower power = ref x.B.Powers[index];
        if (power.Target != BlitzLimits.PlayerOwner || x.B.Player.IsDead)
            return;
        int stolen = Math.Min(power.Amount, x.B.Gold);
        if (stolen <= 0)
            return;
        x.B.AddCounter(BlitzCounter.OutstandingStolenGold, stolen);
        power.Aux0 = Clamp((long)power.Aux0 + stolen);
        x.B.Gold = Math.Max(0, x.B.Gold - stolen);
    }

    /// <summary>
    /// The THIEVERY move's card theft (<c>MonsterMoveEffects.ApplyBeforeAttack</c>): draws one card
    /// from the draw pile then discard pile, preferring the native rarity order, removes it from
    /// combat and stores its complete value in <see cref="BlitzBattle.DetachedCards"/> behind a new
    /// <c>SwipePower</c> row (<see cref="BlitzPower.Aux0"/> = slot + 1). The card is recovered from
    /// the detached table when the thief dies; escape drops the power without returning it.
    /// </summary>
    public static void StealCardForThievery(ref BlitzExec x, byte owner)
    {
        ushort imbued = BlitzRegistry.TryEnchantment("IMBUED", out ushort imbuedId)
            ? (ushort)(imbuedId + 1)
            : (ushort)0;
        int count = ThieveryCandidates(x.B, x.R, -1, imbued);
        if (count == 0)
            return;
        int selectedPriority = -1;
        for (int priority = 0; priority < 4; priority++)
        {
            int preferred = ThieveryCandidates(x.B, x.R, priority, imbued);
            if (preferred == 0)
                continue;
            count = preferred;
            selectedPriority = priority;
            break;
        }
        int chosen = x.B.RngStream(BlitzRngStream.CombatCardGeneration).NextInt(0, count);
        BlitzPile pile = BlitzPile.Draw;
        int position = -1;
        for (int absolute = x.B.HandCount; absolute < x.B.HandCount + x.B.DrawCount + x.B.DiscardCount; absolute++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[absolute];
            if ((card.Flags & BlitzCardFlags.DeckCard) == 0)
                continue;
            if (!MatchesThieveryPriority(card, x.R, selectedPriority, imbued))
                continue;
            if (chosen-- != 0)
                continue;
            pile = absolute < x.B.HandCount + x.B.DrawCount ? BlitzPile.Draw : BlitzPile.Discard;
            position = absolute - BlitzPiles.Start(x.B, pile);
            break;
        }
        if (position < 0)
            throw new InvalidOperationException("偷牌候选非空但没有定位到牌。");
        BlitzCard stolen = BlitzPiles.At(ref x.B, pile, position);
        // CardPileCmd.RemoveFromCombat marks the card's preview HasBeenRemovedFromState; the detached
        // value keeps that flag because the existing importer projects it from the stored preview.
        stolen.Flags |= BlitzCardFlags.RemovedFromState;
        int slot = AllocateDetachedCard(ref x.B, stolen);
        BlitzPiles.RemoveAt(ref x.B, pile, position);
        ushort swipe = BlitzRegistry.TryPower("SWIPE_POWER", out ushort swipeId)
            ? swipeId
            : throw new BlitzUnsupportedException("偷牌需要 SWIPE_POWER 的 Blitz 定义。");
        int applied = ApplyPowerTargeted(ref x, owner, BlitzLimits.PlayerOwner, swipe, 1, owner);
        if (applied == 0)
            throw new InvalidOperationException("飞贼的 SwipePower 未能施加。");
        int row = FindLastPower(x.B, owner, swipe);
        if (row < 0)
            throw new InvalidOperationException("飞贼的 SwipePower 行缺失。");
        x.B.Powers[row].Aux0 = slot + 1;
        x.B.AddCounter(BlitzCounter.OutstandingStolenCards, 1);
    }

    /// <summary>
    /// Number of theft candidates in the draw/discard piles; <paramref name="priority"/> -1 counts
    /// every deck card, otherwise the native <c>ThievingHopper._stealPriorities</c> predicate.
    /// </summary>
    private static int ThieveryCandidates(in BlitzBattle battle, BlitzRules rules, int priority, ushort imbued)
    {
        int count = 0;
        int end = battle.HandCount + battle.DrawCount + battle.DiscardCount;
        for (int index = battle.HandCount; index < end; index++)
        {
            ref readonly BlitzCard card = ref battle.Cards[index];
            if ((card.Flags & BlitzCardFlags.DeckCard) == 0)
                continue;
            if (priority < 0 || MatchesThieveryPriority(card, rules, priority, imbued))
                count++;
        }
        return count;
    }

    private static bool MatchesThieveryPriority(in BlitzCard card, BlitzRules rules, int priority, ushort imbued)
    {
        // ThievingHopper._stealPriorities: Uncommon; Common/Rare/Event; Basic/Quest; Ancient or Imbued.
        // The first three exclude Imbued cards; the last selects them.
        bool isImbued = imbued != 0 && card.EnchantmentId == imbued;
        BlitzCardRarity rarity = rules.Card(card.Id, card.Upgrade).Rarity;
        return priority switch
        {
            0 => !isImbued && rarity == BlitzCardRarity.Uncommon,
            1 => !isImbued && rarity is BlitzCardRarity.Common or BlitzCardRarity.Rare or BlitzCardRarity.Event,
            2 => !isImbued && rarity is BlitzCardRarity.Basic or BlitzCardRarity.Quest,
            3 => rarity == BlitzCardRarity.Ancient || isImbued,
            _ => true,
        };
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.CreatureEscaped</c>: drop every power of the escaped creature,
    /// then remove it from the roster and the known-enemy array. Known enemies are stored as a compact
    /// array, so later rows and every owner reference shift down by one, matching the existing engine's
    /// <c>_knownEnemies.Remove</c>. The escaped list itself is not part of the prediction state (only
    /// the encounter's post-combat gold proportion reads it).
    /// </summary>
    public static void EscapeEnemy(ref BlitzExec x, byte owner)
    {
        int enemyIndex = owner - BlitzLimits.FirstEnemyOwner;
        if ((uint)enemyIndex >= (uint)x.B.EnemyCount || !InRoster(x.B, owner))
            throw new InvalidOperationException("逃跑的生物不在当前模拟战斗中。");
        RemovePowersOf(ref x.B, owner);
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly BlitzPower power = ref x.B.Powers[index];
            if (power.Applier == owner || power.Target == owner)
                throw new BlitzUnsupportedException("逃跑生物仍被其他 Power 的施法者/目标引用，Blitz 无法表示。");
        }
        int roster = 0;
        for (int index = 0; index < x.B.RosterCount; index++)
        {
            int current = x.B.Roster[index];
            if (current == enemyIndex)
                continue;
            x.B.Roster[roster++] = checked((byte)(current > enemyIndex ? current - 1 : current));
        }
        for (int index = roster; index < x.B.RosterCount; index++)
            x.B.Roster[index] = 0;
        x.B.RosterCount = checked((byte)roster);
        for (int index = enemyIndex; index + 1 < x.B.EnemyCount; index++)
            x.B.Enemies[index] = x.B.Enemies[index + 1];
        x.B.Enemies[x.B.EnemyCount - 1] = default;
        x.B.EnemyCount--;
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref BlitzPower power = ref x.B.Powers[index];
            if (power.Owner > owner)
                power.Owner--;
            if (power.Applier > owner && power.Applier != BlitzLimits.NoOwner)
                power.Applier--;
            if (power.Target > owner && power.Target != BlitzLimits.NoOwner)
                power.Target--;
        }
    }

    /// <summary>
    /// <c>SimulatedCombatState.PrepareSteamEruption</c>: record the current Steam Eruption amount as
    /// the explosion damage and remove the power. The damage lives in the AI register projected from
    /// <c>_steamEruptionDamage</c>.
    /// </summary>
    public static void PrepareSteamEruption(ref BlitzExec x, byte owner)
    {
        if (!BlitzRegistry.TryPower("STEAM_ERUPTION_POWER", out ushort steam))
            throw new BlitzUnsupportedException("蒸汽爆发需要 STEAM_ERUPTION_POWER 的 Blitz 定义。");
        int amount = Math.Max(0, GetAmount(x.B, owner, steam));
        x.B.Enemy(owner).Ai[SteamEruptionDamageRegister] = amount;
        SetPowerAmount(ref x.B, owner, steam, 0);
    }

    /// <summary>
    /// <c>SimulatedCombatState.TryTriggerSteamEruptionDeath</c>: a dead Waterfall Giant that still
    /// holds Steam Eruption is rewritten to its forced about-to-blow phase instead of dying. HP and
    /// max HP become the value clamp, the current move is forced to <c>ABOUT_TO_BLOW_MOVE</c>, the
    /// phase is armed and the death power cleanup runs (Steam Eruption itself is kept by
    /// <c>ShouldPowerBeRemovedAfterOwnerDeath</c>).
    /// </summary>
    public static bool TryTriggerSteamEruptionDeath(ref BlitzExec x, byte owner)
    {
        if (!IsEnemyOwner(owner))
            return false;
        ref BlitzEnemy enemy = ref x.B.Enemy(owner);
        if (BlitzRegistry.Monster(enemy.MonsterId).NativeId != "WATERFALL_GIANT")
            return false;
        if (enemy.SteamPhase != BlitzSteamPhase.None || x.B.Creature(owner).IsAlive)
            return false;
        if (!BlitzRegistry.TryPower("STEAM_ERUPTION_POWER", out ushort steam)
            || GetAmount(x.B, owner, steam) <= 0)
        {
            return false;
        }
        ref BlitzCreature creature = ref x.B.Creature(owner);
        creature.MaxHp = BlitzLimits.ValueClamp;
        creature.CurrentHp = BlitzLimits.ValueClamp;
        BlitzMonsterRules rules = MonsterRules(ref x, owner);
        ForceMonsterMove(ref x, owner, rules.NodeIndex("ABOUT_TO_BLOW_MOVE"));
        enemy.SteamPhase = BlitzSteamPhase.AboutToBlow;
        RemovePowersAfterDeath(ref x, owner);
        return true;
    }

    /// <summary>
    /// <c>SimulatedCombatState.TryConsumeForcedMonsterMove</c>: the forced phase replaces the normal
    /// action. About-to-blow records the explosion damage and advances to the explode phase without
    /// executing the move; explode reports the damage for the caller to apply before force-killing the
    /// owner.
    /// </summary>
    public static bool TryConsumeSteamForcedMove(ref BlitzExec x, byte owner, out bool explode, out int damage)
    {
        explode = false;
        damage = 0;
        ref BlitzEnemy enemy = ref x.B.Enemy(owner);
        switch (enemy.SteamPhase)
        {
            case BlitzSteamPhase.None:
                return false;
            case BlitzSteamPhase.AboutToBlow:
                PrepareSteamEruption(ref x, owner);
                x.B.Enemy(owner).SteamPhase = BlitzSteamPhase.Explode;
                return true;
            case BlitzSteamPhase.Explode:
                damage = x.B.Enemy(owner).Ai[SteamEruptionDamageRegister];
                x.B.Enemy(owner).SteamPhase = BlitzSteamPhase.None;
                explode = true;
                return true;
            default:
                throw new BlitzUnsupportedException($"未知的蒸汽爆发阶段 {(byte)enemy.SteamPhase}。");
        }
    }

    /// <summary>
    /// <c>SimulatedCombatState.RecoverStolenResources</c>: a dead thief returns the cards behind its
    /// SwipePower rows and the gold of its HeistPower rows. ThieveryPower gold is not returned here
    /// (Surprise transfers it to a Heist carrier first).
    /// </summary>
    private static void RecoverStolenResources(ref BlitzBattle battle, byte dead)
    {
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly BlitzPower power = ref battle.Powers[index];
            if (power.Owner != dead || power.Amount <= 0)
                continue;
            string nativeId = BlitzRegistry.Power(power.Id).NativeId;
            if (nativeId == "SWIPE_POWER" && power.Aux0 != 0)
            {
                battle.SetCounter(BlitzCounter.OutstandingStolenCards,
                    Math.Max(0, battle.Counter(BlitzCounter.OutstandingStolenCards) - 1));
            }
            else if (nativeId == "HEIST_POWER")
            {
                battle.SetCounter(BlitzCounter.OutstandingStolenGold,
                    Math.Max(0, battle.Counter(BlitzCounter.OutstandingStolenGold) - Math.Max(0, power.Amount)));
            }
        }
    }
}
