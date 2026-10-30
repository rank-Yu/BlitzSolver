using Sts2Solver.Ai.Combat.Policy;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Ai.Combat;

/// <summary>Intersect member limits with the request's original slot and count constraints.</summary>
internal static class PotionSearchSchedule
{
    internal static bool HasForcedDirectives(BeamOptions options)
    {
        ForcedPotionDirectives forced = options.ForcedPotionDirectives;
        if (forced.Count != forced.PotionIds.Length || forced.Count != forced.StrategicHpCosts.Length
            || forced.Count > Limits.PotionSlots)
            throw new InvalidDataException("强制药水指令数组不一致。");
        int validMask = (1 << forced.Count) - 1;
        if ((forced.AlreadyUsedMask & ~validMask) != 0 || forced.AlreadyUsedStrategicHpCost < 0)
            throw new InvalidDataException("强制药水使用记录无效。");
        if (forced.Count > 0 && !options.EnforceForcedPotionDirectives)
            throw new InvalidDataException("组合搜索中的强制药水指令必须启用。");
        if (options.Potions.MaximumUses is { } maximum && maximum < forced.Count
            || options.Potions.MaximumUses is { } limit && options.MinimumPotionUses > limit)
            throw new InvalidDataException("强制药水次数超过使用上限。");
        for (int i = 0; i < forced.Count; i++)
        {
            if ((uint)forced.Slots[i] >= Limits.PotionSlots || forced.StrategicHpCosts[i] < 0)
                throw new InvalidDataException("强制药水指令无效。");
            for (int j = 0; j < i; j++)
                if (forced.Slots[j] == forced.Slots[i] && forced.PotionIds[j] == forced.PotionIds[i])
                    throw new InvalidDataException("强制药水指令重复。");
            if ((forced.AlreadyUsedMask & (1 << i)) == 0
                && !options.Potions.AllowsExplicitUse(forced.Slots[i], forced.PotionIds[i]))
                throw new InvalidDataException("强制药水与保护指令冲突。");
        }
        foreach (PotionSlotDirective directive in options.Potions.Strategy.Directives)
        {
            if (directive.Directive != PotionDirective.Force) continue;
            bool found = false;
            for (int i = 0; i < forced.Count; i++)
                if (directive.Slot == forced.Slots[i]
                    && Registry.Potion(forced.PotionIds[i]).NativeId == directive.PotionId)
                    found = true;
            if (!found) throw new InvalidDataException("强制药水策略缺少对应路线指令。");
        }
        return forced.Count > 0;
    }

    internal static bool HasAllowedUse(IReadOnlyList<Battle> roots, Rules rules, BeamOptions options)
    {
        PotionPolicy policy = options.Potions;
        if (policy.ForceAllDisabled || policy.MaximumUses is { } maximum
            && maximum <= options.InitialExplicitPotionUses)
            return false;
        foreach (Battle root in roots)
        {
            for (int slot = 0; slot < root.PotionSlotCount; slot++)
            {
                ushort stored = root.Potions[slot];
                if (stored != 0 && policy.AllowsExplicitUse(slot, (ushort)(stored - 1)))
                    return true;
                foreach (ushort potion in rules.PotionPool)
                    if (policy.AllowsExplicitUse(slot, potion)) return true;
                foreach (ushort potion in rules.OutOfCombatPotionPool)
                    if (policy.AllowsExplicitUse(slot, potion)) return true;
            }
        }
        return false;
    }

    internal static PotionPolicy Limit(PotionPolicy original, SearchPotionLimit limit, int initialUses = 0) => limit switch
    {
        SearchPotionLimit.None => original with { ForceAllDisabled = true, MaximumUses = Math.Min(original.MaximumUses ?? initialUses, initialUses) },
        SearchPotionLimit.One => original with { MaximumUses = Math.Min(original.MaximumUses ?? 1, 1) },
        SearchPotionLimit.Allowed => original,
        SearchPotionLimit.Two => original with { MaximumUses = Math.Min(original.MaximumUses ?? 2, 2) },
        SearchPotionLimit.Three => original with { MaximumUses = Math.Min(original.MaximumUses ?? 3, 3) },
        _ => throw new ArgumentOutOfRangeException(nameof(limit)),
    };
}
