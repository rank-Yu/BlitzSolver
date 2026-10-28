using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Legal map travel in Core, mirroring the native map screen plus
/// <c>MapTravel.GetTravelablePointsFrom</c>:
/// <list type="bullet">
/// <item>no visited point: only the starting point;</item>
/// <item>last point is the boss and the act has a second boss: only the second boss;</item>
/// <item>last point is on the last normal row: only the boss;</item>
/// <item>otherwise: the current point's children.</item>
/// </list>
/// The first and boss transitions are the two branches of <c>NMapScreen.RecalculateTravelability</c>
/// that call <c>MapTravel.GetTravelablePointsFrom</c> only for the normal case. Native children are
/// a <c>HashSet</c>, so this method emits them in coordinate order; comparisons treat the option
/// set, not the native enumeration order.
/// </summary>
public static class RunTravel
{
    /// <summary>True when the state is waiting for a map selection and has at least one legal point.</summary>
    public static bool CanTravel(in RunState state)
    {
        for (int index = 0; index < state.Rooms.Count; index++)
        {
            RunRoom room = state.Rooms.Items[index];
            if (room.RoomType != RunRoomType.Map && room.IsPreFinished == 0)
                return false;
        }
        Span<RunCoord> points = stackalloc RunCoord[RunLimits.DecisionOptions];
        return Collect(in state, points) > 0;
    }

    /// <summary>
    /// Writes the legal travel points to <paramref name="points"/> in coordinate order and returns
    /// the count. The span must hold at least <see cref="RunLimits.DecisionOptions"/> entries.
    /// </summary>
    public static int Collect(in RunState state, Span<RunCoord> points)
    {
        int count = 0;
        if (state.Visited.CoordCount == 0)
        {
            if (FindPoint(in state, state.Map.Starting, out _))
                points[count++] = state.Map.Starting;
            return count;
        }
        RunCoord last = state.Visited.Coords[state.Visited.CoordCount - 1];
        if (!FindPoint(in state, last, out RunMapPoint current))
            return 0;
        if (state.Map.HasSecondBoss != 0 && last.Equals(state.Map.Boss))
        {
            if (FindPoint(in state, state.Map.SecondBoss, out _))
                points[count++] = state.Map.SecondBoss;
            return count;
        }
        if (last.Row == state.Map.Height - 1)
        {
            if (FindPoint(in state, state.Map.Boss, out _))
                points[count++] = state.Map.Boss;
            return count;
        }
        for (int index = 0; index < current.ChildCount; index++)
        {
            RunCoord child = current.Children[index];
            if (!FindPoint(in state, child, out _))
                continue;
            if (count >= points.Length)
                throw new CapacityException("RunDecision.Options", points.Length);
            points[count++] = child;
        }
        Sort(points[..count]);
        return count;
    }

    /// <summary>True when <paramref name="coord"/> is one of the legal travel points.</summary>
    public static bool IsTravelable(in RunState state, RunCoord coord)
    {
        Span<RunCoord> points = stackalloc RunCoord[RunLimits.DecisionOptions];
        int count = Collect(in state, points);
        for (int index = 0; index < count; index++)
        {
            if (points[index].Equals(coord))
                return true;
        }
        return false;
    }

    /// <summary>The point the player is standing on, or null before the first point.</summary>
    public static bool TryCurrentPoint(in RunState state, out RunMapPoint point)
    {
        if (state.Visited.CoordCount == 0)
        {
            point = default;
            return false;
        }
        return FindPoint(in state, state.Visited.Coords[state.Visited.CoordCount - 1], out point);
    }

    public static bool TryFindPoint(in RunState state, RunCoord coord, out RunMapPoint point)
        => FindPoint(in state, coord, out point);

    private static bool FindPoint(in RunState state, RunCoord coord, out RunMapPoint point)
    {
        for (int index = 0; index < state.Map.NodeCount; index++)
        {
            RunMapPoint candidate = state.Map.Nodes[index];
            if (candidate.Coord.Equals(coord))
            {
                point = candidate;
                return true;
            }
        }
        point = default;
        return false;
    }

    private static void Sort(Span<RunCoord> points)
    {
        for (int index = 1; index < points.Length; index++)
        {
            RunCoord value = points[index];
            int position = index - 1;
            while (position >= 0 && (points[position].Col > value.Col
                   || (points[position].Col == value.Col && points[position].Row > value.Row)))
            {
                points[position + 1] = points[position];
                position--;
            }
            points[position + 1] = value;
        }
    }
}
