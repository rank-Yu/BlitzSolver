using BlitzSolver.Engine;
using BlitzSolver.Search.Scoring;

namespace BlitzSolver.Search;

/// <summary>
/// The novelty fact list of a projected state (<c>the existing search's CaptureNoveltyFacts</c>).
/// Novelty facts are route/search metadata: they are derived only from K4-checked evaluation fields,
/// pile contents and powers, and never enter <see cref="Battle"/>, its fingerprint or a state key.
/// </summary>
internal static class NoveltyFacts
{
    /// <summary>The Blitz side of the existing capture, in the same fact shape and insertion order.</summary>
    public static void Capture(in Battle battle, in ScoreFeatures features, List<BfwsFact> facts)
    {
        ReadOnlySpan<int> values = [features.Energy, features.Stars, features.HandCount,
            features.ZeroCostPlayableCount, features.ReachableHandValue, features.PersistentBuffValue,
            features.LatentSetupValue, features.FutureResourceValue, features.RetainedAttackValue,
            features.ReplayPotentialValue, features.DelayedDamageValue, features.ReactiveDamageValue,
            features.StrategicDamagePotential, features.StrategicPreventionPotential,
            features.StrategicResourcePotential, features.StrategicCardAccessPotential,
            features.StrategicScalingPotential, features.EnemyStrengthSuppression,
            features.EnemyWeakTurns, features.EnemyVulnerableTurns];
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] <= 0)
                continue;
            facts.Add(new(BfwsFactKind.Scalar, i, Value: i < 4 || i >= 18
                ? Math.Min(7, values[i])
                : Math.Min(7, 1 + System.Numerics.BitOperations.Log2((uint)values[i]))));
        }
        facts.Add(new(BfwsFactKind.Energy, Value: features.Energy));
        facts.Add(new(BfwsFactKind.Block, Value: features.PlayerBlock / 5));
        facts.Add(new(BfwsFactKind.Hp, Value: features.PlayerHp / 5));
        for (int zone = 0; zone < 4; zone++)
        {
            Pile pile = (Pile)zone;
            int start = Piles.Start(battle, pile);
            int count = Piles.Count(battle, pile);
            Dictionary<(ushort Id, int Upgrade), int> counts = [];
            List<(ushort Id, int Upgrade)> order = [];
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly Card card = ref battle.Cards[start + offset];
                (ushort Id, int Upgrade) key = (card.Id, card.Upgrade);
                if (counts.TryGetValue(key, out int prior))
                    counts[key] = prior + 1;
                else
                {
                    counts.Add(key, 1);
                    order.Add(key);
                }
            }
            foreach ((ushort id, int upgrade) in order)
            {
                facts.Add(new(BfwsFactKind.Zone, zone, Registry.Card(id).NativeId, upgrade,
                    Math.Min(3, counts[(id, upgrade)])));
            }
        }
        int drawStart = Piles.Start(battle, Pile.Draw);
        for (int i = 0; i < Math.Min(3, (int)battle.DrawCount); i++)
            facts.Add(new(BfwsFactKind.Draw, i, Registry.Card(battle.Cards[drawStart + i].Id).NativeId));
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly Power power = ref battle.Powers[index];
            facts.Add(new(BfwsFactKind.Power, power.Owner == Limits.PlayerOwner ? 0 : 1,
                Registry.Power(power.Id).NativeId, power.Amount));
        }
    }

    /// <summary>An order-independent representation of the fact multiset.</summary>
    public static string Canonical(IReadOnlyList<BfwsFact> facts)
    {
        string[] lines = new string[facts.Count];
        for (int index = 0; index < facts.Count; index++)
        {
            BfwsFact fact = facts[index];
            lines[index] = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{fact.Kind}:{fact.Slot}:{fact.Key}:{fact.Value}:{fact.Detail}");
        }
        Array.Sort(lines, StringComparer.Ordinal);
        return string.Join('\n', lines);
    }

    /// <summary>One line per fact that differs, or an empty list when the two multisets are equal.</summary>
    public static List<string> Diff(IReadOnlyList<BfwsFact> expected, IReadOnlyList<BfwsFact> actual)
    {
        string[] left = Canonical(expected).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string[] right = Canonical(actual).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        List<string> lines = [];
        int i = 0, j = 0;
        while (i < left.Length || j < right.Length)
        {
            int comparison = i >= left.Length ? 1
                : j >= right.Length ? -1
                : string.CompareOrdinal(left[i], right[j]);
            if (comparison == 0)
            {
                i++;
                j++;
                continue;
            }
            lines.Add(comparison < 0 ? $"novelty-fact expected-only {left[i++]}" : $"novelty-fact actual-only {right[j++]}");
            if (lines.Count == 6)
                break;
        }
        return lines;
    }
}
