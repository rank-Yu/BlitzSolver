namespace Sts2Solver.Simulation.Combat;

public static partial class Kernel
{
    /// <summary>
    /// Finds the single combat-pile card carrying <paramref name="mark"/> and its current pile
    /// position. The transient mark is an identity byte, so two cards carrying the same mark is an
    /// explicit unsupported state rather than a choice of copy.
    /// </summary>
    internal static bool TryFindUniqueTransientMark(in Battle battle, byte mark,
        out Pile pile, out int index)
    {
        if (mark == 0)
            throw new InvalidOperationException("瞬态标记 0 不是有效身份。");
        bool found = false;
        pile = Pile.Hand;
        index = -1;
        for (int absolute = 0; absolute < battle.CardCount; absolute++)
        {
            if (battle.Cards[absolute].TransientMark != mark)
                continue;
            if (found)
                throw new UnsupportedException($"瞬态标记 {mark} 在战斗牌堆中出现重复。");
            pile = Piles.PileOf(battle, absolute, out index);
            found = true;
        }
        return found;
    }
}
