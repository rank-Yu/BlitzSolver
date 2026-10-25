// Ported from CombatSolver src/Flash/Bridge/FlashStateImporter.cs at commit a62f1850
// (MIT, Copyright (c) 2026 Torch1230 and contributors). The existing engine accessors are replaced
// by direct reads of the native combat state and CombatManager.History; the field mapping and the
// all-or-nothing rejection strategy are preserved. Engine method names referenced in comments are
// read-only references, not copied implementation.
using BlitzSolver.Engine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Orbs;
using NativeCardType = MegaCrit.Sts2.Core.Entities.Cards.CardType;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Runs;

using Creature = MegaCrit.Sts2.Core.Entities.Creatures.Creature;

namespace BlitzSolver.Import;

/// <summary>
/// Imports one native <see cref="CombatState"/> root directly into <see cref="Battle"/>, without
/// building the old simulacrum state. The caller supplies the frozen <see cref="Rules"/> object for
/// this root; rule compilation is out of scope for M2a-1.
/// </summary>
public static class BattleImporter
{
    /// <summary><c>Kernel.FetchPlayedBit</c>; card-private identity bit, not a layout constant.</summary>
    private const short FetchPlayedBit = 1;

    public static Battle Import(CombatState state, Player player, Rules rules, ImportOptions? options = null)
    {
        options ??= ImportOptions.Default;
        if (state.Players.Count != 1)
            throw new UnsupportedImportException(ImportReason.MultiPlayer, $"玩家数 {state.Players.Count}");
        PlayerCombatState playerState = player.PlayerCombatState
            ?? throw new UnsupportedImportException(ImportReason.Unrepresentable, "玩家没有 PlayerCombatState");
        if (options.RequirePlayPhase
            && (state.CurrentSide != CombatSide.Player || playerState.Phase != PlayerTurnPhase.Play))
        {
            throw new UnsupportedImportException(ImportReason.RootPhase,
                $"根不在玩家出牌阶段：side={state.CurrentSide} phase={playerState.Phase}");
        }
        if (!player.Creature.IsAlive)
            throw new UnsupportedImportException(ImportReason.Unrepresentable, "玩家已死亡");
        if (state.EscapedCreatures.Count != 0)
            throw new UnsupportedImportException(ImportReason.KnownEnemyHistory,
                $"有 {state.EscapedCreatures.Count} 个已逃离生物；原生 Enemies 已不是 KnownEnemies");
        IReadOnlyList<Creature> known = state.Enemies;
        if (known.Count == 0)
            throw new UnsupportedImportException(ImportReason.KnownEnemyHistory, "根没有敌人");
        foreach (Creature enemy in known)
        {
            if (enemy.Monster == null)
                throw new UnsupportedImportException(ImportReason.UnsupportedMonster, $"{enemy.Name} 没有怪物模型");
            if (!enemy.IsAlive)
                throw new UnsupportedImportException(ImportReason.KnownEnemyHistory,
                    $"敌人 {enemy.Monster.Id.Entry} 已死亡，原生 Enemies 不再保留 KnownEnemies 的先后顺序");
            NativeAccess.RequireOriginal(enemy.Monster);
        }
        if (known.Count != rules.RootEnemyCount)
            throw new UnsupportedImportException(ImportReason.RulesMismatch,
                $"原生敌人数 {known.Count} 与规则根敌人数 {rules.RootEnemyCount} 不符");
        if (rules.HasEncounter != (state.Encounter != null))
            throw new UnsupportedImportException(ImportReason.RulesMismatch, "HasEncounter 与原生遭遇不一致");
        if (state.Encounter != null)
        {
            IReadOnlyList<string> nativeSlots = state.Encounter.Slots;
            if (nativeSlots.Count != rules.EncounterSlots.Count)
                throw new UnsupportedImportException(ImportReason.RulesMismatch,
                    $"遭遇槽 {nativeSlots.Count} 与规则 {rules.EncounterSlots.Count} 不符");
            for (int index = 0; index < nativeSlots.Count; index++)
            {
                if (!string.Equals(nativeSlots[index], rules.EncounterSlots[index], StringComparison.Ordinal))
                    throw new UnsupportedImportException(ImportReason.RulesMismatch,
                        $"遭遇槽 {index} 与规则不一致");
            }
        }
        if (NativeAccess.HasNonVanillaModels(player, new NativeAccess.CombatStateView(state.Creatures, state.Enemies, state.Allies)))
            throw new UnsupportedImportException(ImportReason.ThirdPartyContent, "战斗中含非原版程序集的模型");
        NativeAccess.RequireOriginal(player.Character);
        if (state.Encounter != null)
            NativeAccess.RequireOriginal(state.Encounter);
        foreach (ModifierModel modifier in state.Modifiers)
            NativeAccess.RequireOriginal(modifier);

        NativeHistory history = NativeHistory.Capture(state);
        PrivateStateLedger ledger = PrivateStateLedger.Capture(state, player);
        BattleProjections projections = new(history, ledger);
        Battle battle = default;

        battle.Round = state.RoundNumber;
        battle.PlayerTurn = playerState.TurnNumber;
        // The simulator is created at this root, so its own event counters start at zero.
        battle.ShuffleEventCount = 0;
        battle.ShufflesCrossed = options.ShufflesCrossed;
        battle.NextCombatId = state._nextCreatureId;
        battle.Energy = playerState.Energy;
        battle.Stars = playerState.Stars;
        battle.Gold = player.Gold;
        battle.Side = (Side)(byte)state.CurrentSide;
        battle.Phase = (PlayerPhase)(byte)playerState.Phase;
        battle.Outcome = options.EventDefeatBoundary ? Outcome.EventDefeat : Outcome.None;
        if (CombatManager.Instance.IsAboutToLose)
            battle.Flags |= BattleFlags.PendingLoss;
        if (player.IsActiveForHooks)
            battle.Flags |= BattleFlags.PlayerHooksActive;
        if (options.HasRisk)
            battle.Flags |= BattleFlags.HasRisk;
        battle.MaxHandSize = checked((byte)CardPile.MaxCardsInHand);

        battle.Player = Vitals(player.Creature, history.HasLostHpThisTurn(player.Creature));
        Creature? osty = player.Osty;
        if (osty != null)
        {
            NativeAccess.RequireOriginalType(osty.GetType());
            if (osty.Monster == null)
                throw new UnsupportedImportException(ImportReason.UnsupportedMonster, "Osty 没有怪物模型");
            battle.Osty = Vitals(osty, history.HasLostHpThisTurn(osty));
            battle.Flags |= BattleFlags.OstyPresent;
            battle.Flags |= BattleFlags.OstyWasRootPet;
        }

        ImportCounters(ref battle, state, player, known, osty, history, options);
        ImportRng(ref battle, state.RunState.Rng);
        ImportEnemies(ref battle, player, known, rules, history, projections);
        ImportPowers(ref battle, player, known, osty, rules, history, projections);
        ImportCards(ref battle, player, playerState, rules, history, options);
        RejectRunListenerDeckCards(player);
        ImportRelics(ref battle, player, projections);
        ImportPotions(ref battle, player, rules);
        ImportOrbs(ref battle, playerState);
        if (rules.HasDecimalCardVars && !DecimalVars.TryCanonicalize(ref battle, out _))
            throw new UnsupportedImportException(ImportReason.Capacity, "卡牌小数变量表");
        projections.RequireAllMonsterIntsClaimed([]);
        ledger.RequireAllClaimed();
        return battle;
    }

    private static BlitzSolver.Engine.Creature Vitals(Creature creature, bool lostHpThisTurn) => new()
    {
        CurrentHp = creature.CurrentHp,
        MaxHp = creature.MaxHp,
        Block = creature.Block,
        Flags = CreatureFlags.Present | (lostHpThisTurn ? CreatureFlags.LostHpThisTurn : CreatureFlags.None),
    };

    private static void ImportCounters(ref Battle battle, CombatState state, Player player,
        IReadOnlyList<Creature> known, Creature? osty, NativeHistory history, ImportOptions options)
    {
        Creature owner = player.Creature;
        battle.SetCounter(Counter.DampenCasterMask, DampenCasterMask(player, known));
        battle.SetCounter(Counter.CardsPlayedThisTurn, history.CardsPlayedThisTurn(owner));
        battle.SetCounter(Counter.ManualCardsPlayedThisTurn, history.ManualCardsPlayedThisTurn(owner));
        battle.SetCounter(Counter.CardPlayStartsThisTurn, history.CardPlayStartsThisTurn(owner));
        battle.SetCounter(Counter.AttackPlayStartsThisTurn, history.AttackPlayStartsThisTurn(owner));
        battle.SetCounter(Counter.AttackSkillStartsThisTurn, history.AttackSkillStartsThisTurn(owner));
        battle.SetCounter(Counter.ZeroCostAttackStartsThisTurn, history.ZeroCostAttackStartsThisTurn(owner));
        battle.SetCounter(Counter.CardPlaySeriesStartedThisTurn, history.CardPlaySeriesStartedThisTurn(owner));
        battle.SetCounter(Counter.AttacksPlayedThisTurn, history.AttacksPlayedThisTurn(owner));
        battle.SetCounter(Counter.ShivsPlayedThisTurn, history.ShivsPlayedThisTurn(owner));
        battle.SetCounter(Counter.SkillCardsPlayedThisTurn, history.SkillCardsPlayedThisTurn(owner));
        battle.SetCounter(Counter.BlockCardsPlayedThisTurn, history.BlockCardsPlayedThisTurn(owner));
        battle.SetCounter(Counter.CardsExhaustedThisTurn, history.CardsExhaustedThisTurn(owner));
        battle.SetCounter(Counter.CardsDiscardedThisTurn, history.CardsDiscardedThisTurn(owner));
        battle.SetCounter(Counter.PlayerAttacksThisTurn, history.CreatureAttacksThisTurn(owner));
        battle.SetCounter(Counter.NonHandDrawsThisTurn, history.NonHandDrawsThisTurn(player));
        battle.SetCounter(Counter.StatusCardsDrawnThisTurn, history.StatusCardsDrawnThisTurn(player));
        battle.SetCounter(Counter.EnergySpentThisTurn, history.EnergySpentThisTurn(player));
        battle.SetCounter(Counter.StarsGainedThisTurn, history.StarsGainedThisTurn(player));
        // The old simulator's cumulative ledgers start at zero on a fresh root; only its per-turn
        // root-history getters are seeded. The same holds for growth/death/theft ledgers.
        battle.SetCounter(Counter.CumulativeHpLost, 0);
        battle.SetCounter(Counter.RecoveredHp, 0);
        battle.SetCounter(Counter.PlayerUnblockedDamageEvents, history.UnblockedDamageEvents(owner));
        battle.SetCounter(Counter.LongTermResourceValue, 0);
        battle.SetCounter(Counter.AngerCopiesGenerated, 0);
        battle.SetCounter(Counter.DeathSaveRelicHpRestored, 0);
        battle.SetCounter(Counter.DeathSavePotionHpRestored, 0);
        battle.SetCounter(Counter.DeathSaveUseCount, 0);
        battle.SetCounter(Counter.AutomaticPotionUseCount, 0);
        battle.SetCounter(Counter.VitalSparkNormalizedAmount, options.TurnSetupRoot ? 0 : int.MinValue);
        // SimulatedCombatState._skillsPlayedThisTurn starts empty at a root; the historical
        // skill count above is a separate counter and does not seed this flag.
        battle.SetCounter(Counter.SkillPlayedThisSideTurn, 0);
        battle.SetCounter(Counter.FinishedCardPlays, history.FinishedCardPlays);
        battle.SetCounter(Counter.CardsDrawn, history.CardsDrawn(player));
        battle.SetCounter(Counter.LightningChannels, history.LightningChannels(player));
        battle.SetCounter(Counter.OstyAttacksThisTurn, osty != null ? history.CreatureAttacksThisTurn(osty) : 0);
        battle.SetCounter(Counter.DoomAppliedThisTurn, history.WasDoomAppliedThisTurn(owner) ? 1 : 0);
        battle.SetCounter(Counter.EtherealPlays, history.EtherealPlays(player));
        battle.SetCounter(Counter.CardsGeneratedThisCombat, history.CardsGenerated(player));
        battle.SetCounter(Counter.OrbChannels, 0);
        (int stolenGold, int stolenCards) = ScanHeldLoot(known, state);
        battle.SetCounter(Counter.OutstandingStolenGold, stolenGold);
        battle.SetCounter(Counter.OutstandingStolenCards, stolenCards);
        battle.SetCounter(Counter.EntropicFreePotionMask, 0);
        // Growth counters start zero. The enum values are the existing engine's GrowthSource order.
        battle.SetCounter(Counter.GrowthRewardsHandOfGreedTheHunt, 0);
        battle.SetCounter(Counter.GrowthRewardsFeedRoyalties, 0);
        battle.SetCounter(Counter.GrowthRewardsAlchemizeGeneticAlgorithm, 0);
        battle.SetCounter(Counter.GrowthRewardsTheScytheGoopy, 0);
        battle.SetCounter(Counter.GrowthRewardsForbiddenGrimoireMadScience, 0);
    }

    private static int DampenCasterMask(Player player, IReadOnlyList<Creature> known)
    {
        int mask = 0;
        foreach (DampenPower dampen in player.Creature.Powers.OfType<DampenPower>())
        {
            if (dampen.Amount <= 0)
                continue;
            (HashSet<Creature> casters, _) = NativeAccess.DampenData(dampen);
            if (!ReferenceEquals(dampen.Owner, player.Creature))
                throw new UnsupportedImportException(ImportReason.Unrepresentable, "Dampen 目标不是玩家");
            if (casters.Count == 0)
            {
                if (dampen.Applier == null || dampen.Applier.CurrentHp <= 0)
                    throw new UnsupportedImportException(ImportReason.Unrepresentable, "压制存在但没有存活的施法者");
                casters = [dampen.Applier];
            }
            foreach (Creature caster in casters)
            {
                int index = IndexOf(known, caster);
                if (index < 0 || index >= 32)
                    throw new UnsupportedImportException(ImportReason.Unrepresentable, "Dampen 施法者不在敌人名单");
                mask |= 1 << index;
            }
        }
        return mask;
    }

    private static (int Gold, int Cards) ScanHeldLoot(IReadOnlyList<Creature> known, CombatState state)
    {
        int gold = 0, cards = 0;
        foreach (Creature creature in known)
        {
            if (!creature.IsAlive || !ReferenceEquals(creature.CombatState, state))
                continue;
            foreach (PowerModel power in creature.Powers)
            {
                switch (power)
                {
                    case SwipePower { StolenCard: not null }:
                        cards++;
                        break;
                    case ThieveryPower thievery:
                        gold += Math.Max(0, thievery.DynamicVars.Gold.IntValue);
                        break;
                    case HeistPower heist:
                        gold += Math.Max(0, heist.Amount);
                        break;
                }
            }
        }
        return (gold, cards);
    }

    private static void ImportRng(ref Battle battle, MegaCrit.Sts2.Core.Runs.RunRngSet rng)
    {
        Set(ref battle, RngStream.Shuffle, rng.Shuffle);
        Set(ref battle, RngStream.CombatCardGeneration, rng.CombatCardGeneration);
        Set(ref battle, RngStream.CombatPotionGeneration, rng.CombatPotionGeneration);
        Set(ref battle, RngStream.CombatCardSelection, rng.CombatCardSelection);
        Set(ref battle, RngStream.CombatEnergyCosts, rng.CombatEnergyCosts);
        Set(ref battle, RngStream.CombatTargets, rng.CombatTargets);
        Set(ref battle, RngStream.CombatOrbGeneration, rng.CombatOrbGeneration);
        Set(ref battle, RngStream.MonsterAi, rng.MonsterAi);
        Set(ref battle, RngStream.Niche, rng.Niche);

        static void Set(ref Battle target, RngStream stream, MegaCrit.Sts2.Core.Random.Rng source)
        {
            MegaCrit.Sts2.Core.Saves.SerializableRng data = source.ToSerializable();
            target.RngStream(stream) = RngState.FromSerializable(data.counter, data.state0, data.state1, data.state2, data.state3);
        }
    }

    private static void ImportEnemies(ref Battle battle, Player player, IReadOnlyList<Creature> known,
        Rules rules, NativeHistory history, BattleProjections projections)
    {
        if (known.Count > Limits.Enemies)
            throw new UnsupportedImportException(ImportReason.Capacity, $"敌人数 {known.Count}");
        battle.EnemyCount = checked((byte)known.Count);
        for (int index = 0; index < known.Count; index++)
        {
            Creature creature = known[index];
            MonsterModel monster = creature.Monster
                ?? throw new UnsupportedImportException(ImportReason.UnsupportedMonster, "敌人没有怪物模型");
            if (!Registry.TryMonster(monster.Id.Entry, out ushort monsterId))
                throw UnsupportedContent.Monster(monster);
            int rulesId = index < rules.RootEnemyCount
                ? index
                : rules.TrySpawnRulesId(monsterId, out int spawnRulesId)
                    ? spawnRulesId
                    : throw new UnsupportedImportException(ImportReason.Unrepresentable,
                        $"{monster.Id.Entry} 不在根的可生成怪物闭包中");
            MonsterRules machine = rules.MonsterRule(checked((byte)rulesId));
            if (machine.MonsterId != monsterId)
                throw new UnsupportedImportException(ImportReason.RulesMismatch,
                    $"敌人 {index} 的规则类型 {machine.MonsterId} 与原生 {monsterId} 不符");
            ref Enemy enemy = ref battle.Enemies[index];
            enemy.Creature = Vitals(creature, history.HasLostHpThisTurn(creature));
            enemy.MonsterId = monsterId;
            enemy.Rules = checked((byte)rulesId);
            enemy.Slot = SlotFor(rules, creature);
            enemy.DeathPhase = DeathPhase.None;
            enemy.CombatId = creature.CombatId
                ?? throw new UnsupportedImportException(ImportReason.Unrepresentable, "敌人没有 CombatId");
            string current = NativeAccess.CurrentMoveId(monster);
            if (current == "STUNNED")
            {
                string followUp = NativeAccess.MoveFollowUpId(monster, current)
                    ?? throw new UnsupportedImportException(ImportReason.Unrepresentable,
                        $"{monster.Id.Entry} 的 STUNNED 没有后继行动");
                if (followUp == "STUNNED")
                    throw new UnsupportedImportException(ImportReason.Unrepresentable,
                        $"{monster.Id.Entry} 的 STUNNED 后继仍是 STUNNED");
                enemy.Move = checked((byte)machine.StunnedNode);
                enemy.ForcedMove = checked((byte)NodeIndex(machine, followUp, monster));
            }
            else if (current == "REVIVE_MOVE")
            {
                if (machine.ReviveNode < 0)
                    throw new UnsupportedImportException(ImportReason.Unrepresentable,
                        $"{monster.Id.Entry} 的复活行动没有节点");
                string followUp = NativeAccess.MoveFollowUpId(monster, current)
                    ?? throw new UnsupportedImportException(ImportReason.Unrepresentable,
                        $"{monster.Id.Entry} 的 REVIVE_MOVE 没有后继行动");
                if (followUp is "STUNNED" or "REVIVE_MOVE")
                    throw new UnsupportedImportException(ImportReason.Unrepresentable,
                        $"{monster.Id.Entry} 的 REVIVE_MOVE 后继是合成节点");
                enemy.Move = checked((byte)machine.ReviveNode);
                enemy.ForcedMove = checked((byte)NodeIndex(machine, followUp, monster));
            }
            else
            {
                enemy.Move = checked((byte)NodeIndex(machine, current, monster));
                enemy.ForcedMove = byte.MaxValue;
            }
            foreach (string logged in NativeAccess.StateLog(monster))
            {
                int node = NodeIndex(machine, logged, monster);
                if (node >= 64)
                    throw new UnsupportedImportException(ImportReason.Capacity, $"行动节点 {node}");
                Kernel.LogMonsterMove(ref enemy, node);
            }
            int poweredHits = history.PoweredAttackHitsThisTurn(player.Creature, creature);
            if (poweredHits > byte.MaxValue)
                throw new UnsupportedImportException(ImportReason.Unrepresentable,
                    $"{monster.Id.Entry} 的本回合强化攻击命中数 {poweredHits} 超过 255");
            enemy.PoweredAttackHitsThisTurn = checked((byte)poweredHits);
            projections.ProjectMonster(creature, Registry.Monster(monsterId), ref enemy);
        }
        battle.RosterCount = checked((byte)known.Count);
        for (int position = 0; position < known.Count; position++)
        {
            battle.Roster[position] = checked((byte)position);
            battle.Enemies[position].Flags |= EnemyFlags.InCombat;
        }
    }

    private static int NodeIndex(MonsterRules machine, string nativeId, MonsterModel monster)
    {
        for (int node = 0; node < machine.Nodes.Length; node++)
        {
            if (machine.Nodes[node].NativeId == nativeId)
                return node;
        }
        throw new UnsupportedImportException(ImportReason.Unrepresentable,
            $"{monster.Id.Entry} 的行动 {nativeId} 不在状态机中");
    }

    private static byte SlotFor(Rules rules, Creature creature)
    {
        if (creature.SlotName == null)
            return checked((byte)Kernel.NoSlot);
        for (int slot = 0; slot < rules.EncounterSlots.Count; slot++)
        {
            if (string.Equals(rules.EncounterSlots[slot], creature.SlotName, StringComparison.Ordinal))
                return checked((byte)slot);
        }
        return checked((byte)Kernel.NoSlot);
    }

    private static void ImportPowers(ref Battle battle, Player player, IReadOnlyList<Creature> known,
        Creature? osty, Rules rules, NativeHistory history, BattleProjections projections)
    {
        List<(byte Owner, int Order, PowerModel Power)> ordered = [];
        AddPowers(ordered, player.Creature, Limits.PlayerOwner);
        if (osty != null)
            AddPowers(ordered, osty, Limits.OstyOwner);
        for (int index = 0; index < known.Count; index++)
            AddPowers(ordered, known[index], checked((byte)(Limits.FirstEnemyOwner + index)));
        ordered.Sort((left, right) => left.Owner != right.Owner
            ? left.Owner.CompareTo(right.Owner)
            : left.Order.CompareTo(right.Order));
        if (ordered.Count > Limits.Powers)
            throw new UnsupportedImportException(ImportReason.Capacity, $"Power 数 {ordered.Count}");
        battle.PowerCount = checked((byte)ordered.Count);
        for (int index = 0; index < ordered.Count; index++)
        {
            (byte owner, _, PowerModel power) = ordered[index];
            NativeAccess.RequireOriginal(power);
            if (!Registry.TryPower(power.Id.Entry, out ushort id))
                throw UnsupportedContent.Power(power);
            PowerDef def = Registry.Power(id);
            PowerFlags flags = PowerFlags.None;
            if (def.UsesNativeDurationSkip && power.SkipNextDurationTick)
                flags |= PowerFlags.SkipNextDurationTick;
            if (def.MarksJustAppliedByEnemy && power is RitualPower { _wasJustAppliedByEnemy: true })
                flags |= PowerFlags.WasJustAppliedByEnemy;
            battle.Powers[index] = new Power
            {
                Id = id,
                Owner = owner,
                Applier = power.Applier == null ? Limits.NoOwner : Owner(power.Applier, player, known, osty),
                Target = power.Target == null ? Limits.NoOwner : Owner(power.Target, player, known, osty),
                Flags = flags,
                Amount = power.Amount,
                AmountOnTurnStart = def.UsesAmountOnTurnStart ? power.AmountOnTurnStart : 0,
            };
            projections.ProjectPower(power, ref battle.Powers[index]);
            if (power is NightmarePower nightmare)
                ImportNightmareSelection(ref battle, nightmare, rules, index);
            if (power is SwipePower swipe)
                ImportSwipeSelection(ref battle, swipe, rules, index);
        }
    }

    private static void AddPowers(List<(byte Owner, int Order, PowerModel Power)> target, Creature owner, byte ownerIndex)
    {
        IReadOnlyList<PowerModel> powers = owner.Powers;
        for (int order = 0; order < powers.Count; order++)
        {
            PowerModel power = powers[order];
            if (power.Amount == 0)
                continue;
            target.Add((ownerIndex, order, power));
        }
    }

    private static void ImportNightmareSelection(ref Battle battle, NightmarePower power, Rules rules, int row)
    {
        CardModel selected = NativeAccess.NightmareSelection(power)
            ?? throw new UnsupportedImportException(ImportReason.Unrepresentable, "Nightmare 选定卡缺失");
        Card value = Card(ref battle, selected, rules, rootCard: false, dampenOriginalUpgrade: -1);
        for (int slot = 0; slot < Limits.DetachedCards; slot++)
        {
            if (battle.DetachedCards[slot].Id != 0)
                continue;
            battle.DetachedCards[slot] = value;
            battle.Powers[row].Aux0 = slot + 1;
            return;
        }
        throw new UnsupportedImportException(ImportReason.Capacity, $"Nightmare 选定卡 {Limits.DetachedCards}");
    }

    private static void ImportSwipeSelection(ref Battle battle, SwipePower power, Rules rules, int row)
    {
        if (power.StolenCard == null)
            return;
        Card value = Card(ref battle, power.StolenCard, rules, rootCard: true, dampenOriginalUpgrade: -1);
        for (int slot = 0; slot < Limits.DetachedCards; slot++)
        {
            if (battle.DetachedCards[slot].Id != 0)
                continue;
            battle.DetachedCards[slot] = value;
            battle.Powers[row].Aux0 = slot + 1;
            return;
        }
        throw new UnsupportedImportException(ImportReason.Capacity, $"SwipePower 被偷卡 {Limits.DetachedCards}");
    }

    private static void ImportCards(ref Battle battle, Player player, PlayerCombatState playerState, Rules rules,
        NativeHistory history, ImportOptions options)
    {
        Dictionary<CardModel, int> dampen = [];
        foreach (DampenPower power in player.Creature.Powers.OfType<DampenPower>())
        {
            if (power.Amount <= 0)
                continue;
            (_, Dictionary<CardModel, int> upgrades) = NativeAccess.DampenData(power);
            foreach ((CardModel card, int level) in upgrades)
                dampen[card] = level;
        }
        HashSet<CardModel> imported = new(ReferenceEqualityComparer.Instance);
        (CardPile Pile, Pile Target)[] piles =
        [
            (playerState.Hand, Pile.Hand),
            (playerState.DrawPile, Pile.Draw),
            (playerState.DiscardPile, Pile.Discard),
            (playerState.ExhaustPile, Pile.Exhaust),
            (playerState.PlayPile, Pile.Play),
        ];
        foreach ((CardPile pile, Pile target) in piles)
        {
            foreach (CardModel card in pile.Cards)
            {
                imported.Add(card);
                int originalUpgrade = dampen.TryGetValue(card, out int level) ? level : -1;
                // A turn-setup root reports the exact card objects captured at the Start snapshot;
                // cards created during setup (Toolbox's chosen card) are not in that set and therefore
                // carry CreatedAfterRoot, matching CombatSolver's _liveCardsAtSnapshot. Older callers
                // without the set keep the run-deck-source approximation for setup roots.
                bool rootCard = options.RootCards is { } rootCards
                    ? rootCards.Contains(card)
                    : !options.TurnSetupRoot || card.DeckVersion != null;
                Card value = Card(ref battle, card, rules, rootCard, originalUpgrade);
                if (card is SovereignBlade)
                    value.Private = checked((short)rules.RootSwordSageAmount);
                else if (history.FetchPlayedThisTurn(card))
                    value.Private |= FetchPlayedBit;
                if (history.ReturnsToHand(card))
                    value.ReturnToHandNextTurn = true;
                Piles.Append(ref battle, target, value);
            }
        }
        foreach (CardModel card in dampen.Keys)
        {
            if (imported.Contains(card))
                continue;
            if (card.HasBeenRemovedFromState)
                continue;
            throw new UnsupportedImportException(ImportReason.Unrepresentable,
                $"Dampen 记录不在牌堆中：{card.Id.Entry}");
        }
    }

    private static Card Card(ref Battle battle, CardModel card, Rules rules, bool rootCard, int dampenOriginalUpgrade)
    {
        NativeAccess.RequireOriginal(card);
        ushort id = CardId(card);
        int upgrade = card.CurrentUpgradeLevel;
        CardStats stats;
        try
        {
            stats = rules.Card(id, upgrade);
        }
        catch (UnsupportedException error)
        {
            throw new UnsupportedImportException(ImportReason.RulesMismatch, error.Message);
        }
        if (card.IsDupe)
            throw new UnsupportedImportException(ImportReason.UnsupportedCard, $"{card.Id.Entry} 复制品");
        CardDef def = Registry.Card(id);
        Card result = new()
        {
            Id = id,
            Upgrade = checked((byte)upgrade),
            Keywords = MapKeywords(card.Keywords),
            BaseCost = checked((sbyte)card.EnergyCost._base),
            BaseStarCost = checked((sbyte)card.BaseStarCost),
            ReplayCount = checked((byte)card.BaseReplayCount),
            IsClone = card.IsClone,
        };
        if (def.UsesIdentityLedger)
            result.Private = Limits.UninitializedLedger;
        if (dampenOriginalUpgrade > 3)
            throw new UnsupportedImportException(ImportReason.Unrepresentable,
                $"{card.Id.Entry} 压制前升级等级 {dampenOriginalUpgrade}");
        if (dampenOriginalUpgrade >= 0)
            result.DampenOriginalUpgrade = checked((byte)(dampenOriginalUpgrade + 1));
        // Every card present at import has passed the one-time power-affliction entry check.
        result.Flags |= CardFlags.PowerAfflictionChecked;
        if (!rootCard)
            result.Keywords |= Keywords.CreatedAfterRoot;
        if (card.Enchantment != null)
        {
            EnchantmentModel enchantment = card.Enchantment;
            if (!Registry.TryEnchantment(enchantment.Id.Entry, out ushort enchantmentId))
                throw new UnsupportedImportException(ImportReason.UnsupportedEnchantment, enchantment.Id.Entry);
            if (enchantment.Amount is < short.MinValue or > short.MaxValue)
                throw new UnsupportedImportException(ImportReason.Unrepresentable,
                    $"{card.Id.Entry} 附魔层数 {enchantment.Amount}");
            if ((uint)enchantment.Status > 1)
                throw new UnsupportedImportException(ImportReason.Unrepresentable,
                    $"{card.Id.Entry} 附魔状态 {(int)enchantment.Status}");
            result.EnchantmentId = checked((byte)(enchantmentId + 1));
            result.EnchantmentAmount = (short)enchantment.Amount;
            result.EnchantmentStatus = (byte)enchantment.Status;
            switch (enchantment)
            {
                case Glam glam:
                    result.EnchantmentPrivate = (short)(glam._usedThisCombat ? 1 : 0);
                    break;
                case Momentum momentum:
                    result.EnchantmentPrivate = checked((short)momentum._extraDamage);
                    break;
            }
        }
        if (card.Affliction != null)
        {
            AfflictionModel affliction = card.Affliction;
            if (!Registry.TryAffliction(affliction.Id.Entry, out ushort afflictionId))
                throw new UnsupportedImportException(ImportReason.UnsupportedAffliction, affliction.Id.Entry);
            if (affliction.Amount is < short.MinValue or > short.MaxValue)
                throw new UnsupportedImportException(ImportReason.Unrepresentable,
                    $"{card.Id.Entry} 苦难层数 {affliction.Amount}");
            result.AfflictionId = checked((byte)(afflictionId + 1));
            result.AfflictionAmount = (short)affliction.Amount;
        }
        if (card.ExhaustOnNextPlay) result.Flags |= CardFlags.ExhaustOnNextPlay;
        if (card.HasSingleTurnSly) result.Flags |= CardFlags.SlyThisTurn;
        if (card.HasSingleTurnRetain) result.Flags |= CardFlags.RetainThisTurn;
        if (card.DeckVersion != null) result.Flags |= CardFlags.DeckCard;
        if (card.HasBeenRemovedFromState) result.Flags |= CardFlags.RemovedFromState;
        if (card.EnergyCost.CostsX) result.Flags |= CardFlags.CostsX;
        if (card.HasStarCostX) result.Flags |= CardFlags.StarCostsX;

        List<LocalCostModifier> modifiers = card.EnergyCost._localModifiers;
        if (modifiers.Count > Limits.CardEnergyCostModifiers)
            throw new UnsupportedImportException(ImportReason.Capacity,
                $"{card.Id.Entry} 费用修饰 {modifiers.Count}");
        for (int index = 0; index < modifiers.Count; index++)
        {
            LocalCostModifier modifier = modifiers[index];
            result.EnergyModifiers[index] = CostModifier.Create(modifier.Amount, (CostType)(byte)modifier.Type,
                (CostExpiration)(byte)modifier.Expiration, modifier.IsReduceOnly);
        }
        result.EnergyModifierCount = modifiers.Count;
        List<TemporaryCardCost> starCosts = card._temporaryStarCosts;
        if (starCosts.Count > 0)
        {
            int cost = starCosts[0].Cost;
            bool allSame = true;
            for (int index = 1; index < starCosts.Count; index++)
            {
                TemporaryCardCost entry = starCosts[index];
                if (entry.Cost != cost
                    || entry.ClearsWhenTurnEnds != starCosts[0].ClearsWhenTurnEnds
                    || entry.ClearsWhenCardIsPlayed != starCosts[0].ClearsWhenCardIsPlayed)
                {
                    allSame = false;
                    break;
                }
            }
            if (starCosts.Count > 3 && !allSame)
                throw new UnsupportedImportException(ImportReason.Capacity,
                    $"{card.Id.Entry} 临时星能费用条目 {starCosts.Count} 条");
            if (allSame)
            {
                result.SetTemporaryStarCost(cost, starCosts[0].ClearsWhenTurnEnds, starCosts[0].ClearsWhenCardIsPlayed);
            }
            else
            {
                for (int index = 0; index < starCosts.Count; index++)
                {
                    TemporaryCardCost entry = starCosts[index];
                    if (entry.Cost != cost)
                        throw new UnsupportedImportException(ImportReason.Capacity,
                            $"{card.Id.Entry} 临时星能费用条目成本不一致（{starCosts.Count} 条）");
                    result.SetTemporaryStarCost(entry.Cost, entry.ClearsWhenTurnEnds, entry.ClearsWhenCardIsPlayed);
                }
            }
        }

        IReadOnlyList<string> varNames = def.VarNames;
        if (varNames.Count > stats.Vars.Length)
            throw new UnsupportedImportException(ImportReason.RulesMismatch,
                $"{card.Id.Entry} 定义变量数 {varNames.Count} 超过规则 {stats.Vars.Length}");
        for (int index = 0; index < varNames.Count; index++)
        {
            decimal current = card.DynamicVars[varNames[index]].BaseValue;
            decimal delta = current - stats.Vars[index];
            if (delta == 0)
                continue;
            if (index == 0 && def.UsesDecimalDamageVar)
            {
                if (current >= Limits.ValueClamp)
                    throw new UnsupportedImportException(ImportReason.Unrepresentable,
                        $"{card.Id.Entry} 的小数伤害变量已到 999999999 上限");
                if (!DecimalVars.TryAllocate(ref battle, delta, out short encoded))
                    throw new UnsupportedImportException(ImportReason.Capacity,
                        $"{card.Id.Entry} 小数变量 {varNames[index]}");
                result.Private = encoded;
                continue;
            }
            if (index > 1 || delta != decimal.Truncate(delta) || delta < short.MinValue || delta > short.MaxValue)
                throw new UnsupportedImportException(ImportReason.Unrepresentable,
                    $"{card.Id.Entry} 变量 {varNames[index]} 偏移 {delta}");
            if (index == 0) result.Var0 = (short)delta;
            else result.Var1 = (short)delta;
        }
        def.ProjectPrivate(ref result);
        if (card is MadScience madScience)
        {
            if (madScience.TinkerTimeType is not (NativeCardType.Attack or NativeCardType.Skill or NativeCardType.Power))
                throw new UnsupportedImportException(ImportReason.Unrepresentable,
                    $"{card.Id.Entry} 的 TinkerTimeType {(int)madScience.TinkerTimeType}");
            result.Private = BlitzSolver.Content.Cards.Event.MadScience.Encode(
                (int)madScience.TinkerTimeType, (int)madScience.TinkerTimeRider);
        }
        return result;
    }

    private static ushort CardId(CardModel card)
    {
        if (Registry.TryCard(card.Id.Entry, out ushort id))
            return id;
        throw UnsupportedContent.Card(card);
    }

    private static Keywords MapKeywords(IEnumerable<CardKeyword> keywords)
    {
        Keywords result = Keywords.None;
        foreach (CardKeyword keyword in keywords)
        {
            result |= keyword switch
            {
                CardKeyword.Exhaust => Keywords.Exhaust,
                CardKeyword.Ethereal => Keywords.Ethereal,
                CardKeyword.Innate => Keywords.Innate,
                CardKeyword.Unplayable => Keywords.Unplayable,
                CardKeyword.Retain => Keywords.Retain,
                CardKeyword.Sly => Keywords.Sly,
                CardKeyword.Eternal => Keywords.Eternal,
                _ => throw new UnsupportedImportException(ImportReason.UnsupportedCard, $"关键字 {keyword}"),
            };
        }
        return result;
    }

    private static void RejectRunListenerDeckCards(Player player)
    {
        foreach (CardModel card in player.Deck.Cards)
        {
            NativeAccess.RequireOriginal(card);
            if (!Registry.TryCard(card.Id.Entry, out ushort id))
                continue;
            CardDef def = Registry.Card(id);
            if ((def.Hooks & HookMask.RunListenerHooks) != 0 && !def.DeckCopyOutsideCombatIsInert)
                throw new UnsupportedImportException(ImportReason.RunDeckHook, $"牌组卡 {card.Id.Entry} 带有局外监听 Hook");
        }
    }

    private static void ImportRelics(ref Battle battle, Player player, BattleProjections projections)
    {
        IReadOnlyList<RelicModel> relics = player.Relics;
        if (relics.Count > Limits.Relics)
            throw new UnsupportedImportException(ImportReason.Capacity, $"遗物数 {relics.Count}");
        battle.RelicCount = checked((byte)relics.Count);
        for (int index = 0; index < relics.Count; index++)
        {
            RelicModel relic = relics[index];
            NativeAccess.RequireOriginal(relic);
            if (!Registry.TryRelic(relic.Id.Entry, out ushort id))
                throw UnsupportedContent.Relic(relic);
            battle.Relics[index] = new Relic
            {
                Id = id,
                Flags = relic.IsMelted ? RelicFlags.Melted : RelicFlags.None,
            };
            projections.ProjectRelic(relic, ref battle.Relics[index]);
        }
    }

    private static void ImportPotions(ref Battle battle, Player player, Rules rules)
    {
        int slots = player.PotionSlots.Count;
        if (slots > Limits.PotionSlots)
            throw new UnsupportedImportException(ImportReason.Capacity, $"药水槽 {slots}");
        battle.PotionSlotCount = checked((byte)slots);
        for (int slot = 0; slot < slots; slot++)
        {
            PotionModel? potion = player.GetPotionAtSlotIndex(slot);
            if (potion == null)
                continue;
            NativeAccess.RequireOriginal(potion);
            if (!Registry.TryPotion(potion.Id.Entry, out ushort id))
                throw UnsupportedContent.Potion(potion);
            battle.Potions[slot] = (ushort)(id + 1);
        }
        // The simulator's PotionUses list starts empty at the captured root; pre-root uses belong to
        // the fixed prefix and are not generated uses. Entropic free slots have no root projection.
        battle.SetCounter(Counter.EntropicFreePotionMask, 0);
        battle.SetCounter(Counter.GeneratedPotionUseCount, 0);
        battle.SetCounter(Counter.GeneratedPotionStrategicCost, 0);
        _ = rules;
    }

    private static void ImportOrbs(ref Battle battle, PlayerCombatState playerState)
    {
        OrbQueue orbs = playerState.OrbQueue;
        if (orbs.Capacity < 0 || orbs.Capacity > Limits.Orbs)
            throw new UnsupportedImportException(ImportReason.Capacity, $"球槽 {orbs.Capacity}");
        if (orbs.Orbs.Count > orbs.Capacity)
            throw new UnsupportedImportException(ImportReason.Unrepresentable,
                $"球数 {orbs.Orbs.Count} 超过容量 {orbs.Capacity}");
        if (orbs.Orbs.Count > Limits.Orbs)
            throw new UnsupportedImportException(ImportReason.Capacity, $"球数 {orbs.Orbs.Count}");
        battle.OrbCapacity = checked((byte)orbs.Capacity);
        battle.OrbCount = checked((byte)orbs.Orbs.Count);
        for (int index = 0; index < orbs.Orbs.Count; index++)
        {
            OrbModel orb = orbs.Orbs[index];
            NativeAccess.RequireOriginal(orb);
            if (!Registry.TryOrb(orb.Id.Entry, out ushort id))
                throw UnsupportedContent.Orb(orb);
            decimal stored = NativeAccess.OrbPrivateValue(orb);
            if (stored != decimal.Truncate(stored) || stored < int.MinValue || stored > int.MaxValue)
                throw new UnsupportedImportException(ImportReason.Unrepresentable, $"{orb.Id.Entry} 私有值 {stored}");
            battle.Orbs[index] = new Orb { Id = checked((byte)id), Aux = (int)stored };
        }
    }

    private static byte Owner(Creature creature, Player player, IReadOnlyList<Creature> known, Creature? osty)
    {
        if (ReferenceEquals(creature, player.Creature))
            return Limits.PlayerOwner;
        if (osty != null && ReferenceEquals(creature, osty))
            return Limits.OstyOwner;
        int index = IndexOf(known, creature);
        return index >= 0
            ? checked((byte)(Limits.FirstEnemyOwner + index))
            : throw new UnsupportedImportException(ImportReason.Unrepresentable, $"未知生物 {creature.Name}");
    }

    private static int IndexOf(IReadOnlyList<Creature> creatures, Creature creature)
    {
        for (int index = 0; index < creatures.Count; index++)
        {
            if (ReferenceEquals(creatures[index], creature))
                return index;
        }
        return -1;
    }
}
