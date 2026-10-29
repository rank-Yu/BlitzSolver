// Adapted from CombatSolver src/Search/BeamWidthPortfolio.cs at a62f1850
// (MIT License, Copyright (c) 2026 Torch1230 and contributors).
namespace Sts2Solver.Ai.Combat;

internal static class PortfolioBuilder
{
    internal static SearchMember[] Build(SearchPlan plan, bool power, bool forced)
    {
        if (!plan.CustomMembers.IsDefaultOrEmpty)
        {
            SearchMember[] custom = [.. plan.CustomMembers];
            bool seenAllowed = false, broad = false, baseline = false;
            foreach (SearchMember member in custom)
            {
                if (member.Width <= 0 || !Enum.IsDefined(member.Evaluation)
                    || !Enum.IsDefined(member.Retention) || !Enum.IsDefined(member.Potions))
                    throw new InvalidDataException("自定义组合成员无效。");
                if (forced && member.Potions != SearchPotionLimit.Allowed)
                    throw new InvalidDataException("强制药水指令不能被成员限制绕过。");
                if (!forced && member.Potions == SearchPotionLimit.None && seenAllowed)
                    throw new InvalidDataException("无药基线必须先于用药阶段。");
                baseline |= member.Potions == SearchPotionLimit.None;
                seenAllowed |= member.Potions != SearchPotionLimit.None;
                broad |= member.Potions == SearchPotionLimit.Allowed;
            }
            if (!forced && (!baseline || !broad))
                throw new InvalidDataException("Smart 组合需要无药基线及允许用药的成员。");
            return custom;
        }
        SearchPotionLimit primary = forced ? SearchPotionLimit.Allowed : SearchPotionLimit.None;
        List<SearchMember> members = [];
        if (power)
            members.Add(new(plan.BaselineWidth, SearchEvaluation.Standard, SearchRetention.Power, primary));
        members.Add(new(plan.NarrowWidth, SearchEvaluation.Standard, SearchRetention.Standard, primary));
        members.Add(new(plan.WideWidth, SearchEvaluation.Standard, SearchRetention.Standard, primary));
        members.Add(new(plan.BaselineWidth, SearchEvaluation.Standard, SearchRetention.SecondBand, primary));
        members.Add(new(plan.BaselineWidth, SearchEvaluation.Base, SearchRetention.Standard, primary));
        members.Add(new(plan.OffensiveWidth, SearchEvaluation.Offensive, SearchRetention.Standard, primary));
        if (!forced)
        {
            members.Add(new(plan.NarrowWidth, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.One));
            members.Add(new(plan.BaselineWidth, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.Allowed));
        }
        return [.. members];
    }
}
