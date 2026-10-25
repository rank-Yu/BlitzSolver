// Shared native-model lookup used while constructing a fixture root.
using MegaCrit.Sts2.Core.Models;

namespace BlitzSolver.TestHost;

internal static class NativeLookup
{
    internal static TModel ResolveUnique<TModel>(IEnumerable<TModel> candidates, string input, string kind)
        where TModel : AbstractModel
    {
        TModel[] matches = candidates
            .Where(candidate => candidate.Id.ToString().Equals(input, StringComparison.OrdinalIgnoreCase)
                || candidate.Id.Entry.Equals(input, StringComparison.OrdinalIgnoreCase)
                || candidate.GetType().Name.Equals(input, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(candidate => candidate.Id)
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"找不到{kind} {input}。"),
            _ => throw new InvalidOperationException($"{kind} {input} 不唯一。"),
        };
    }
}
