namespace Sts2Solver.Ai.Combat;

/// <summary>Two complementary members appended to each existing potion stage.</summary>
internal static class QueenPortfolio
{
    internal static bool IsSupplement(SearchMember member)
        => member.Evaluation == SearchEvaluation.QueenSetup || member.Retention == SearchRetention.QueenKillTiming;

    internal static SearchMember[] AppendToStages(SearchMember[] members, SearchPlan plan)
    {
        List<SearchMember> result = new(members.Length + 6);
        for (int i = 0; i < members.Length; i++)
        {
            SearchMember member = members[i];
            result.Add(member);
            if (i + 1 < members.Length && members[i + 1].Potions == member.Potions) continue;
            result.Add(new(plan.BaselineWidth, SearchEvaluation.QueenSetup, SearchRetention.Standard, member.Potions));
            result.Add(new(plan.NarrowWidth, SearchEvaluation.Standard, SearchRetention.QueenKillTiming, member.Potions));
        }
        return [.. result];
    }
}
