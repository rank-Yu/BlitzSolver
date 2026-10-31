// Adapted from CombatSolver src/Search/BeamWidthPortfolio.cs at a62f1850
// (MIT License, Copyright (c) 2026 Torch1230 and contributors).
namespace Sts2Solver.Ai.Combat;

internal static class PortfolioBuilder
{
    internal static SearchMember[] Build(SearchPlan plan, bool power, bool forced)
    {
        if (!Enum.IsDefined(plan.DefaultPotionSchedule))
            throw new InvalidDataException("默认用药调度无效。");
        if (!plan.CustomMembers.IsDefaultOrEmpty)
        {
            SearchMember[] custom = [.. plan.CustomMembers];
            bool seenAllowed = false, baseline = false;
            foreach (SearchMember member in custom)
            {
                if (member.Width <= 0 || member.Evaluation is SearchEvaluation.QueenSetup or SearchEvaluation.Setup or SearchEvaluation.FocusedSetup || member.Retention == SearchRetention.QueenKillTiming || !Enum.IsDefined(member.Evaluation)
                    || !Enum.IsDefined(member.Retention) || !Enum.IsDefined(member.Potions))
                    throw new InvalidDataException("自定义组合成员无效。");
                if (forced && member.Potions != SearchPotionLimit.Allowed)
                    throw new InvalidDataException("强制药水指令不能被成员限制绕过。");
                if (!forced && member.Potions == SearchPotionLimit.None && seenAllowed)
                    throw new InvalidDataException("无药基线必须先于用药阶段。");
                baseline |= member.Potions == SearchPotionLimit.None;
                seenAllowed |= member.Potions != SearchPotionLimit.None;
            }
            if (!forced && (!baseline || !seenAllowed))
                throw new InvalidDataException("Smart 组合需要无药基线及允许用药的成员。");
            return custom;
        }
        SearchPotionLimit primary = forced ? SearchPotionLimit.Allowed : SearchPotionLimit.None;
        List<SearchMember> members = [];
        if (power)
            members.Add(new(plan.BaselineWidth, SearchEvaluation.Standard, SearchRetention.Power, primary));
        int ordinaryIndex = members.Count;
        int ordinaryWidth = forced && plan.UsesStagedPortfolio
            ? plan.BaselineWidth : plan.NarrowWidth;
        members.Add(new(ordinaryWidth, SearchEvaluation.Standard, SearchRetention.Standard, primary));
        members.Add(new(plan.WideWidth, SearchEvaluation.Standard, SearchRetention.Standard, primary));
        bool compactAllowed = plan.DefaultPotionSchedule == DefaultPotionSchedule.CompactPotionPortfolio;
        if (!forced || !compactAllowed)
            members.Add(new(plan.BaselineWidth, SearchEvaluation.Standard, SearchRetention.SecondBand, primary));
        members.Add(new(plan.BaselineWidth, SearchEvaluation.Base, SearchRetention.Standard, primary));
        members.Add(new(plan.OffensiveWidth, SearchEvaluation.Offensive, SearchRetention.Standard, primary));
        if (!forced)
        {
            if (plan.UsesStagedPortfolio)
            {
                int primaryCount = members.Count;
                if (plan.UsesCompactOneStage)
                {
                    if (power)
                        members.Add(new(plan.BaselineWidth, SearchEvaluation.Standard, SearchRetention.Power, SearchPotionLimit.One));
                    members.Add(new(plan.WideWidth, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.One));
                }
                else
                    for (int i = 0; i < primaryCount; i++)
                        members.Add(members[i] with { Potions = SearchPotionLimit.One });
                for (int i = 0; i < primaryCount; i++)
                {
                    if (compactAllowed && members[i].Retention == SearchRetention.SecondBand) continue;
                    members.Add(members[i] with { Potions = SearchPotionLimit.Allowed,
                        Width = i == ordinaryIndex ? plan.BaselineWidth : members[i].Width });
                }
                return [.. members];
            }
            members.Add(new(plan.NarrowWidth, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.One));
            if (plan.DefaultPotionSchedule == DefaultPotionSchedule.OneTwoThree)
            {
                members.Add(new(plan.BaselineWidth, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.Two));
                members.Add(new(plan.BaselineWidth, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.Three));
            }
            else
                members.Add(new(plan.BaselineWidth, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.Allowed));
        }
        return [.. members];
    }
}
