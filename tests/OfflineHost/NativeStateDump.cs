// Shared dump code for BlitzSolver/tests/OfflineHost and the reference hook in
// CombatSolver/tools/search/OfflineSearchHarness. Reads native CombatState only; never changes it.
using System.Globalization;
using System.Text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace OfflineHost;

internal static class NativeStateDump
{
    public const int FormatVersion = 1;

    public static string Capture(CombatState state)
    {
        Player player = LocalContext.GetMe(state)
            ?? throw new InvalidOperationException("找不到本地玩家。");
        PlayerCombatState combat = player.PlayerCombatState
            ?? throw new InvalidOperationException("玩家没有 PlayerCombatState。");

        StringBuilder text = new();
        void Line(string key, FormattableString value)
            => text.Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        void Raw(string key, string value)
            => text.Append(key).Append('=').Append(value).Append('\n');

        Line("format.version", $"{FormatVersion}");
        Line("combat.round", $"{state.RoundNumber}");
        Line("combat.turn", $"{combat.TurnNumber}");
        Raw("combat.side", state.CurrentSide.ToString());
        Raw("combat.phase", combat.Phase.ToString());
        Raw("combat.encounter", state.Encounter?.Id.Entry ?? "-");
        Line("combat.nextCombatId", $"{state._nextCreatureId}");
        Raw("combat.inProgress", CombatManager.Instance.IsInProgress ? "true" : "false");

        AppendCreature(text, "player", player.Creature);
        Line("player.energy", $"{combat.Energy}");
        Line("player.stars", $"{combat.Stars}");
        Line("player.gold", $"{player.Gold}");
        AppendPowers(text, "player.power", player.Creature.Powers);

        Line("player.relic.count", $"{player.Relics.Count}");
        for (int index = 0; index < player.Relics.Count; index++)
        {
            RelicModel relic = player.Relics[index];
            Raw($"player.relic.{index}", $"{relic.Id.Entry}:{relic.DisplayAmount}");
        }

        Line("player.potion.count", $"{player.PotionSlots.Count}");
        for (int index = 0; index < player.PotionSlots.Count; index++)
            Raw($"player.potion.{index}", player.GetPotionAtSlotIndex(index)?.Id.Entry ?? "-");

        Line("player.orb.capacity", $"{combat.OrbQueue.Capacity}");
        Line("player.orb.count", $"{combat.OrbQueue.Orbs.Count}");
        for (int index = 0; index < combat.OrbQueue.Orbs.Count; index++)
        {
            OrbModel orb = combat.OrbQueue.Orbs[index];
            Raw($"player.orb.{index}",
                $"{orb.Id.Entry}:{Decimal(orb.PassiveVal)}:{Decimal(orb.EvokeVal)}");
        }

        AppendPile(text, "player.pile.hand", combat.Hand);
        AppendPile(text, "player.pile.draw", combat.DrawPile);
        AppendPile(text, "player.pile.discard", combat.DiscardPile);
        AppendPile(text, "player.pile.exhaust", combat.ExhaustPile);
        AppendPile(text, "player.pile.play", combat.PlayPile);

        Creature? osty = player.Osty;
        Raw("osty.present", osty == null ? "false" : "true");
        if (osty != null)
        {
            AppendCreature(text, "osty", osty);
            AppendPowers(text, "osty.power", osty.Powers);
        }

        Line("enemy.count", $"{state.Enemies.Count}");
        for (int index = 0; index < state.Enemies.Count; index++)
        {
            Creature enemy = state.Enemies[index];
            Raw($"enemy.{index}.id", enemy.Monster?.Id.Entry ?? "-");
            Raw($"enemy.{index}.combatId", enemy.CombatId?.ToString(CultureInfo.InvariantCulture) ?? "-");
            Line($"enemy.{index}.hp", $"{enemy.CurrentHp}");
            Line($"enemy.{index}.maxHp", $"{enemy.MaxHp}");
            Line($"enemy.{index}.block", $"{enemy.Block}");
            Raw($"enemy.{index}.move", enemy.Monster?.NextMove?.Id ?? "-");
            AppendPowers(text, $"enemy.{index}.power", enemy.Powers);
        }

        AppendRng(text, "Shuffle", state.RunState.Rng.Shuffle);
        AppendRng(text, "CombatCardGeneration", state.RunState.Rng.CombatCardGeneration);
        AppendRng(text, "CombatPotionGeneration", state.RunState.Rng.CombatPotionGeneration);
        AppendRng(text, "CombatCardSelection", state.RunState.Rng.CombatCardSelection);
        AppendRng(text, "CombatEnergyCosts", state.RunState.Rng.CombatEnergyCosts);
        AppendRng(text, "CombatTargets", state.RunState.Rng.CombatTargets);
        AppendRng(text, "CombatOrbGeneration", state.RunState.Rng.CombatOrbGeneration);
        AppendRng(text, "MonsterAi", state.RunState.Rng.MonsterAi);
        AppendRng(text, "Niche", state.RunState.Rng.Niche);

        return text.ToString();
    }

    public static void Write(CombatState state, string path)
        => File.WriteAllText(path, Capture(state), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static void AppendCreature(StringBuilder text, string prefix, Creature creature)
    {
        RawLine(text, $"{prefix}.hp", creature.CurrentHp.ToString(CultureInfo.InvariantCulture));
        RawLine(text, $"{prefix}.maxHp", creature.MaxHp.ToString(CultureInfo.InvariantCulture));
        RawLine(text, $"{prefix}.block", creature.Block.ToString(CultureInfo.InvariantCulture));
        RawLine(text, $"{prefix}.alive", creature.IsAlive ? "true" : "false");
        RawLine(text, $"{prefix}.slot", creature.SlotName ?? "-");
    }

    private static void AppendPowers(StringBuilder text, string prefix, IReadOnlyList<PowerModel> powers)
    {
        RawLine(text, $"{prefix}.count", powers.Count.ToString(CultureInfo.InvariantCulture));
        for (int index = 0; index < powers.Count; index++)
        {
            PowerModel power = powers[index];
            RawLine(text, $"{prefix}.{index}", $"{power.Id.Entry}:{power.Amount}");
        }
    }

    private static void AppendPile(StringBuilder text, string prefix, CardPile pile)
    {
        RawLine(text, $"{prefix}.count", pile.Cards.Count.ToString(CultureInfo.InvariantCulture));
        for (int index = 0; index < pile.Cards.Count; index++)
            RawLine(text, $"{prefix}.{index}", DescribeCard(pile.Cards[index]));
    }

    private static string DescribeCard(CardModel card)
    {
        StringBuilder text = new();
        text.Append(card.Id.Entry)
            .Append(':').Append(card.CurrentUpgradeLevel)
            .Append(":cost=").Append(card.EnergyCost.GetWithModifiers(CostModifiers.Local))
            .Append(":costX=").Append(card.EnergyCost.CostsX ? '1' : '0')
            .Append(":star=").Append(card.CurrentStarCost)
            .Append(":starX=").Append(card.HasStarCostX ? '1' : '0')
            .Append(":ench=").Append(card.Enchantment?.Id.Entry ?? "-")
            .Append(':').Append(card.Enchantment?.Amount ?? 0)
            .Append(":aff=").Append(card.Affliction?.Id.Entry ?? "-")
            .Append(':').Append(card.Affliction?.Amount ?? 0)
            .Append(":vars=");
        bool first = true;
        foreach (var pair in card.DynamicVars.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!first)
                text.Append(',');
            first = false;
            text.Append(pair.Key).Append('=').Append(Decimal(pair.Value.BaseValue));
            if (pair.Value is StringVar stringVar)
                text.Append(':').Append(stringVar.StringValue);
        }
        if (first)
            text.Append('-');
        return text.ToString();
    }

    private static void AppendRng(StringBuilder text, string name, Rng rng)
    {
        var serializable = rng.ToSerializable();
        RawLine(text, $"rng.{name}.counter", serializable.counter.ToString(CultureInfo.InvariantCulture));
        RawLine(text, $"rng.{name}.state0", serializable.state0.ToString(CultureInfo.InvariantCulture));
        RawLine(text, $"rng.{name}.state1", serializable.state1.ToString(CultureInfo.InvariantCulture));
        RawLine(text, $"rng.{name}.state2", serializable.state2.ToString(CultureInfo.InvariantCulture));
        RawLine(text, $"rng.{name}.state3", serializable.state3.ToString(CultureInfo.InvariantCulture));
    }

    private static string Decimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static void RawLine(StringBuilder text, string key, string value)
        => text.Append(key).Append('=').Append(value).Append('\n');
}
