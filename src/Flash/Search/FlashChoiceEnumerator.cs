using CombatSolver.Flash.Kernel;
using CombatSolver.Flash.Search.Scoring;

namespace CombatSolver.Flash.Search;

/// <summary>
/// Enumerates the selections a <see cref="FlashChoiceRequest"/> allows: sizes from MinCount to
/// MaxCount, and for each size the option-index combinations in lexicographic order, up to the
/// output capacity. Callers that truncate say so; every emitted selection is legal.
/// </summary>
internal static class FlashChoiceEnumerator
{
    public static int Enumerate(in FlashChoiceRequest request, Span<ulong> output)
    {
        int options = request.OptionCount;
        int count = 0;
        Span<int> indexes = stackalloc int[FlashLimits.MaxChoiceOptions];
        for (int size = request.MinCount; size <= request.MaxCount && count < output.Length; size++)
        {
            for (int index = 0; index < size; index++)
                indexes[index] = index;
            while (count < output.Length)
            {
                ulong bits = 0;
                for (int index = 0; index < size; index++)
                    bits |= 1UL << indexes[index];
                output[count++] = bits;
                int position = size - 1;
                while (position >= 0 && indexes[position] == options - size + position)
                    position--;
                if (position < 0)
                    break;
                indexes[position]++;
                for (int index = position + 1; index < size; index++)
                    indexes[index] = indexes[index - 1] + 1;
            }
        }
        return count;
    }

    /// <summary>Number of legal selections, saturating at <paramref name="cap"/>.</summary>
    public static int Count(in FlashChoiceRequest request, int cap)
    {
        long total = 0;
        for (int size = request.MinCount; size <= request.MaxCount; size++)
        {
            long combinations = 1;
            for (int index = 0; index < size; index++)
            {
                combinations = combinations * (request.OptionCount - index) / (index + 1);
                if (combinations > cap)
                    return cap;
            }
            total += combinations;
            if (total >= cap)
                return cap;
        }
        return (int)total;
    }
}
