using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Native <c>StandardActMap</c> generation, including <c>MapPathPruning</c> and
/// <c>MapPostProcessing</c>. The geometry, restriction sets, point-type counts and the act's two
/// count draws come from <see cref="ContentTable"/>; the algorithm structure mirrors the native
/// members named below.
/// <para>
/// The map RNG is <c>new Rng(seed, "act_N_map")</c>, recreated per act from
/// <c>RunState.Rng.Map[actIndex]</c>; the consumed state is returned so a comparison can check the
/// draw count.
/// </para>
/// </summary>
public static class RunMapGenerator
{
    /// <summary>Internal node array bound: the three special points plus the map capacity.</summary>
    private const int NodeCapacity = RunLimits.MapPoints + 3;

    private const int Relations = 8;

    /// <summary>Generates the map of <paramref name="actIndex"/> from the run's stored act map RNG.</summary>
    public static RunMap Generate(ContentTable content, in RunState state, int actIndex,
        out RngState finalRng)
    {
        if ((uint)actIndex >= (uint)state.Acts.Count)
            throw new UnsupportedException($"幕序号 {actIndex} 超出 {state.Acts.Count} 幕。");
        RunAct actState = state.Acts.Items[actIndex];
        ContentAct act = content.Act(actState.Id);
        RngState rng = state.Rng.Map[actIndex];
        Builder builder = new(content, act, state.Ascension, actState.HasSecondBoss != 0, rng);
        RunMap map = builder.Build(out finalRng);
        return map;
    }

    private sealed class Builder
    {
        private readonly ContentAct _act;
        private readonly ContentMapRules _rules;
        private readonly int _ascension;
        private readonly int _mapLength;
        private readonly int[] _grid;
        private readonly MapNode[] _nodes;
        private readonly int[] _starts;
        private int _nodeCount;
        private int _startCount;
        private RngState _rng;
        private readonly int _starting;
        private readonly int _boss;
        private readonly int _secondBoss;
        private int _restTarget;
        private int _unknownTarget;

        public Builder(ContentTable content, ContentAct act, int ascension, bool hasSecondBoss,
            RngState rng)
        {
            _act = act;
            _rules = content.MapRules;
            _ascension = ascension;
            _rng = rng;
            _mapLength = act.BaseRooms + 1;
            _grid = new int[_rules.Width * _mapLength];
            _nodes = new MapNode[NodeCapacity];
            _starts = new int[_rules.StartPaths + 8];
            _nodeCount = 0;
            _startCount = 0;

            _starting = AddNode(new RunCoord((byte)(_rules.Width / 2), 0));
            _boss = AddNode(new RunCoord((byte)(_rules.Width / 2), (byte)_mapLength));
            _secondBoss = hasSecondBoss
                ? AddNode(new RunCoord((byte)(_rules.Width / 2), (byte)(_mapLength + 1)))
                : -1;

            _restTarget = _act.RestDraw.Draw(ref _rng);
            _unknownTarget = _act.UnknownDraw.Draw(ref _rng);
            BuildGrid();
        }

        private int GridIndex(int col, int row) => col * _mapLength + row;

        private int GridAt(int col, int row)
        {
            if (col < 0 || col >= _rules.Width || row < 0 || row >= _mapLength)
                return -1;
            int encoded = _grid[GridIndex(col, row)];
            return encoded == 0 ? -1 : encoded - 1;
        }

        private void SetGrid(int col, int row, int node)
        {
            if (col < 0 || col >= _rules.Width || row < 0 || row >= _mapLength)
                throw new UnsupportedException($"地图格子 ({col},{row}) 越界。");
            _grid[GridIndex(col, row)] = node < 0 ? 0 : node + 1;
        }

        private int AddNode(RunCoord coord)
        {
            if (_nodeCount >= NodeCapacity)
                throw new CapacityException("RunMap.Nodes", NodeCapacity);
            _nodes[_nodeCount] = new MapNode
            {
                Coord = coord,
                Type = RunMapPointType.Unassigned,
                CanBeModified = true,
            };
            return _nodeCount++;
        }

        private int FindPoint(int col, int row)
        {
            if (col == _nodes[_boss].Coord.Col && row == _nodes[_boss].Coord.Row)
                return _boss;
            if (_secondBoss >= 0 && col == _nodes[_secondBoss].Coord.Col
                && row == _nodes[_secondBoss].Coord.Row)
                return _secondBoss;
            if (col == _nodes[_starting].Coord.Col && row == _nodes[_starting].Coord.Row)
                return _starting;
            return GridAt(col, row);
        }

        private int GetOrCreatePoint(int col, int row)
        {
            int existing = FindPoint(col, row);
            if (existing >= 0)
                return existing;
            int node = AddNode(new RunCoord((byte)col, (byte)row));
            SetGrid(col, row, node);
            return node;
        }

        private void BuildGrid()
        {
            // StandardActMap.GenerateMap
            for (int path = 0; path < _rules.StartPaths; path++)
            {
                int point = GetOrCreatePoint(_rng.NextInt(0, _rules.Width), 1);
                if (path == 1)
                {
                    while (ContainsStart(point))
                        point = GetOrCreatePoint(_rng.NextInt(0, _rules.Width), 1);
                }
                AddStart(point);
                PathGenerate(point);
            }
            ForEachInRow(_mapLength - 1, last => AddChild(last, _boss));
            if (_secondBoss >= 0)
                AddChild(_boss, _secondBoss);
            ForEachInRow(1, rowOne => AddChild(_starting, rowOne));
            AssignPointTypes();
        }

        private bool ContainsStart(int node)
        {
            for (int index = 0; index < _startCount; index++)
            {
                if (_starts[index] == node)
                    return true;
            }
            return false;
        }

        private void AddStart(int node)
        {
            if (ContainsStart(node))
                return;
            if (_startCount >= _starts.Length)
                throw new CapacityException("RunMap.Starts", _starts.Length);
            _starts[_startCount++] = node;
        }

        private void ForEachInRow(int row, Action<int> action)
        {
            for (int col = 0; col < _rules.Width; col++)
            {
                int node = GridAt(col, row);
                if (node >= 0)
                    action(node);
            }
        }

        private void PathGenerate(int startingPoint)
        {
            int point = startingPoint;
            while (_nodes[point].Coord.Row < _mapLength - 1)
            {
                RunCoord next = GenerateNextCoord(point);
                int child = GetOrCreatePoint(next.Col, next.Row);
                AddChild(point, child);
                point = child;
            }
        }

        private RunCoord GenerateNextCoord(int current)
        {
            int col = _nodes[current].Coord.Col;
            int left = Math.Max(0, col - 1);
            int right = Math.Min(col + 1, _rules.Width - 1);
            Span<int> order = stackalloc int[3];
            order[0] = -1;
            order[1] = 0;
            order[2] = 1;
            RunRandom.UnstableShuffle(ref _rng, order);
            for (int index = 0; index < order.Length; index++)
            {
                int target = order[index] switch
                {
                    -1 => left,
                    0 => col,
                    1 => right,
                    _ => throw new UnsupportedException("地图路径步进非法。"),
                };
                if (!HasInvalidCrossover(current, target))
                    return new RunCoord((byte)target, (byte)(_nodes[current].Coord.Row + 1));
            }
            throw new InvalidOperationException("无法生成地图下一个节点。");
        }

        private bool HasInvalidCrossover(int current, int targetCol)
        {
            int delta = targetCol - _nodes[current].Coord.Col;
            if (delta == 0)
                return false;
            int above = GridAt(targetCol, _nodes[current].Coord.Row);
            if (above < 0)
                return false;
            MapNode parent = _nodes[above];
            for (int index = 0; index < parent.ChildCount; index++)
            {
                int child = parent.Children[index];
                if (_nodes[child].Coord.Col - parent.Coord.Col == -delta)
                    return true;
            }
            return false;
        }

        private void AssignPointTypes()
        {
            // StandardActMap.AssignPointTypes
            ForEachInRow(_mapLength - 1, node =>
            {
                _nodes[node].Type = RunMapPointType.RestSite;
                _nodes[node].CanBeModified = false;
            });
            ForEachInRow(_mapLength - _rules.TreasureRowFromEnd, node =>
            {
                _nodes[node].Type = RunMapPointType.Treasure;
                _nodes[node].CanBeModified = false;
            });
            ForEachInRow(1, node =>
            {
                _nodes[node].Type = RunMapPointType.Monster;
                _nodes[node].CanBeModified = false;
            });

            int elites = _rules.ElitesByAscension[Math.Clamp(_ascension, 0,
                ContentTable.AscensionCount - 1)];
            int queued = _restTarget + _rules.Shops + elites + _unknownTarget;
            Span<RunMapPointType> queue = stackalloc RunMapPointType[queued];
            int count = 0;
            for (int index = 0; index < _restTarget; index++)
                queue[count++] = RunMapPointType.RestSite;
            for (int index = 0; index < _rules.Shops; index++)
                queue[count++] = RunMapPointType.Shop;
            for (int index = 0; index < elites; index++)
                queue[count++] = RunMapPointType.Elite;
            for (int index = 0; index < _unknownTarget; index++)
                queue[count++] = RunMapPointType.Unknown;

            int head = 0;
            for (int pass = 0; pass < _rules.TypeAssignIterations; pass++)
            {
                if (count <= 0)
                    break;
                int[] candidates = UnassignedPoints();
                SortByCoord(candidates);
                RunRandom.UnstableShuffle(ref _rng, candidates);
                for (int index = 0; index < candidates.Length; index++)
                {
                    if (count == 0)
                        break;
                    _nodes[candidates[index]].Type =
                        NextValidPointType(queue, ref head, ref count, candidates[index]);
                }
            }
            foreach (int node in AllGridPoints())
            {
                if (_nodes[node].Type == RunMapPointType.Unassigned)
                    _nodes[node].Type = RunMapPointType.Monster;
            }
            _nodes[_boss].Type = RunMapPointType.Boss;
            _nodes[_starting].Type = RunMapPointType.Ancient;
            if (_secondBoss >= 0)
                _nodes[_secondBoss].Type = RunMapPointType.Boss;
        }

        private int[] UnassignedPoints()
        {
            int[] result = new int[_nodeCount];
            int count = 0;
            foreach (int node in AllGridPoints())
            {
                if (_nodes[node].Type == RunMapPointType.Unassigned)
                    result[count++] = node;
            }
            Array.Resize(ref result, count);
            return result;
        }

        private IEnumerable<int> AllGridPoints()
        {
            for (int col = 0; col < _rules.Width; col++)
            {
                for (int row = 0; row < _mapLength; row++)
                {
                    int node = GridAt(col, row);
                    if (node >= 0)
                        yield return node;
                }
            }
        }

        private void SortByCoord(Span<int> nodes)
        {
            for (int index = 1; index < nodes.Length; index++)
            {
                int value = nodes[index];
                int position = index - 1;
                while (position >= 0
                       && CompareCoord(_nodes[nodes[position]].Coord, _nodes[value].Coord) > 0)
                {
                    nodes[position + 1] = nodes[position];
                    position--;
                }
                nodes[position + 1] = value;
            }
        }

        private static int CompareCoord(RunCoord left, RunCoord right)
            => left.Col != right.Col ? left.Col - right.Col : left.Row - right.Row;

        private RunMapPointType NextValidPointType(Span<RunMapPointType> queue, ref int head,
            ref int count, int point)
        {
            for (int attempt = 0; attempt < count; attempt++)
            {
                RunMapPointType type = queue[head];
                if (IsValidPointType(type, point))
                {
                    for (int move = head; move < count - 1; move++)
                        queue[move] = queue[move + 1];
                    count--;
                    if (head >= count)
                        head = 0;
                    return type;
                }
                head = (head + 1) % count;
            }
            return RunMapPointType.Unassigned;
        }

        private bool IsValidPointType(RunMapPointType type, int node)
        {
            int row = _nodes[node].Coord.Row;
            if (row < _rules.LowerRestrictionRow && _rules.Restricted(_rules.LowerRestrictions, type))
                return false;
            if (row >= _mapLength - _rules.UpperRestrictionFromEnd
                && _rules.Restricted(_rules.UpperRestrictions, type))
                return false;
            MapNode current = _nodes[node];
            if (_rules.Restricted(_rules.ParentRestrictions, type))
            {
                for (int index = 0; index < current.ParentCount; index++)
                {
                    if (_nodes[current.Parents[index]].Type == type)
                        return false;
                }
                for (int index = 0; index < current.ChildCount; index++)
                {
                    if (_nodes[current.Children[index]].Type == type)
                        return false;
                }
            }
            if (_rules.Restricted(_rules.ChildRestrictions, type))
            {
                for (int index = 0; index < current.ChildCount; index++)
                {
                    if (_nodes[current.Children[index]].Type == type)
                        return false;
                }
            }
            if (_rules.Restricted(_rules.SiblingRestrictions, type))
            {
                for (int parentIndex = 0; parentIndex < current.ParentCount; parentIndex++)
                {
                    int parent = current.Parents[parentIndex];
                    for (int childIndex = 0; childIndex < _nodes[parent].ChildCount; childIndex++)
                    {
                        int child = _nodes[parent].Children[childIndex];
                        if (child != node && _nodes[child].Type == type)
                            return false;
                    }
                }
            }
            return true;
        }

        private bool AddChild(int parent, int child)
        {
            if (_nodes[parent].ChildCount >= Relations)
                throw new CapacityException("RunMap.Children", Relations);
            for (int index = 0; index < _nodes[parent].ChildCount; index++)
            {
                if (_nodes[parent].Children[index] == child)
                    return false;
            }
            if (_nodes[child].ParentCount >= Relations)
                throw new CapacityException("RunMap.Parents", Relations);
            _nodes[parent].Children[_nodes[parent].ChildCount++] = child;
            _nodes[child].Parents[_nodes[child].ParentCount++] = parent;
            return true;
        }

        private bool RemoveChild(int parent, int child)
        {
            int slot = -1;
            for (int index = 0; index < _nodes[parent].ChildCount; index++)
            {
                if (_nodes[parent].Children[index] == child)
                {
                    slot = index;
                    break;
                }
            }
            if (slot < 0)
                return false;
            for (int move = slot; move < _nodes[parent].ChildCount - 1; move++)
                _nodes[parent].Children[move] = _nodes[parent].Children[move + 1];
            _nodes[parent].ChildCount--;
            for (int index = 0; index < _nodes[child].ParentCount; index++)
            {
                if (_nodes[child].Parents[index] != parent)
                    continue;
                for (int move = index; move < _nodes[child].ParentCount - 1; move++)
                    _nodes[child].Parents[move] = _nodes[child].Parents[move + 1];
                _nodes[child].ParentCount--;
                break;
            }
            return true;
        }

        public RunMap Build(out RngState finalRng)
        {
            PruneAndRepair();
            CenterGrid();
            SpreadAdjacentMapPoints();
            StraightenPaths();
            RunMap map = Export();
            finalRng = _rng;
            return map;
        }

        private void PruneAndRepair()
        {
            // MapPathPruning.PruneAndRepair
            for (int round = 0; round < _rules.PruneRepairIterations; round++)
            {
                PruneDuplicateSegments();
                if (!RepairPrunedPointTypes())
                    break;
            }
        }

        private void PruneDuplicateSegments()
        {
            int iterations = 0;
            List<List<int[]>> matching = FindMatchingSegments();
            while (PrunePaths(matching))
            {
                iterations++;
                if (iterations > _rules.PruneIterationLimit)
                    throw new InvalidOperationException(
                        $"Unable to prune matching segments in {iterations} iterations");
                matching = FindMatchingSegments();
            }
        }

        private bool RepairPrunedPointTypes()
        {
            bool repaired = RepairPointType(RunMapPointType.Shop, _rules.Shops);
            int elites = _rules.ElitesByAscension[Math.Clamp(_ascension, 0,
                ContentTable.AscensionCount - 1)];
            repaired |= RepairPointType(RunMapPointType.Elite, elites);
            repaired |= RepairPointType(RunMapPointType.RestSite, _restTarget);
            repaired |= RepairPointType(RunMapPointType.Unknown, _unknownTarget);
            return repaired;
        }

        private bool RepairPointType(RunMapPointType type, int targetCount)
        {
            int current = 0;
            foreach (int node in AllGridPoints())
            {
                if (_nodes[node].Type == type)
                    current++;
            }
            int missing = targetCount - current;
            if (missing <= 0)
                return false;
            int[] candidates = ModifiableMonsters();
            SortByCoord(candidates);
            RunRandom.UnstableShuffle(ref _rng, candidates);
            bool repaired = false;
            for (int index = 0; index < candidates.Length; index++)
            {
                if (missing == 0)
                    break;
                if (IsValidPointType(type, candidates[index]))
                {
                    _nodes[candidates[index]].Type = type;
                    missing--;
                    repaired = true;
                }
            }
            return repaired;
        }

        private int[] ModifiableMonsters()
        {
            int[] result = new int[_nodeCount];
            int count = 0;
            foreach (int node in AllGridPoints())
            {
                if (_nodes[node].Type == RunMapPointType.Monster && _nodes[node].CanBeModified)
                    result[count++] = node;
            }
            Array.Resize(ref result, count);
            return result;
        }

        private List<List<int[]>> FindMatchingSegments()
        {
            // MapPathPruning.FindMatchingSegments
            List<List<int>> paths = [];
            List<int> stack = [];
            FindAllPaths(_starting, stack, paths);
            SortedDictionary<string, List<int[]>> segments = new(StringComparer.Ordinal);
            foreach (List<int> path in paths)
                AddSegmentsToDictionary(path, segments);
            List<List<int[]>> duplicates = [];
            foreach (List<int[]> list in segments.Values)
            {
                if (list.Count > 1)
                    duplicates.Add(list);
            }
            return duplicates;
        }

        private void FindAllPaths(int current, List<int> stack, List<List<int>> paths)
        {
            if (_nodes[current].Type == RunMapPointType.Boss)
            {
                List<int> complete = [.. stack, current];
                paths.Add(complete);
                return;
            }
            stack.Add(current);
            for (int index = 0; index < _nodes[current].ChildCount; index++)
                FindAllPaths(_nodes[current].Children[index], stack, paths);
            stack.RemoveAt(stack.Count - 1);
        }

        private void AddSegmentsToDictionary(List<int> path,
            SortedDictionary<string, List<int[]>> segments)
        {
            for (int start = 0; start < path.Count - 1; start++)
            {
                if (!IsValidSegmentStart(path[start]))
                    continue;
                for (int length = 2; length < path.Count - start; length++)
                {
                    int end = path[start + length];
                    if (!IsValidSegmentEnd(end))
                        continue;
                    int[] segment = path.GetRange(start, length + 1).ToArray();
                    string key = SegmentKey(segment);
                    if (!segments.TryGetValue(key, out List<int[]>? list))
                    {
                        list = [];
                        segments.Add(key, list);
                        list.Add(segment);
                    }
                    else if (!AnyOverlappingSegments(list, segment))
                    {
                        list.Add(segment);
                    }
                }
            }
        }

        private bool IsValidSegmentStart(int node)
            => _nodes[node].ChildCount <= 1 ? _nodes[node].Coord.Row == 0 : true;

        private bool IsValidSegmentEnd(int node) => _nodes[node].ParentCount >= 2;

        private string SegmentKey(int[] segment)
        {
            MapNode start = _nodes[segment[0]];
            MapNode end = _nodes[segment[^1]];
            System.Text.StringBuilder key = new(48);
            if (start.Coord.Row == 0)
            {
                key.Append(start.Coord.Row).Append('-')
                    .Append(end.Coord.Col).Append(',').Append(end.Coord.Row).Append('-');
            }
            else
            {
                key.Append(start.Coord.Col).Append(',').Append(start.Coord.Row).Append('-')
                    .Append(end.Coord.Col).Append(',').Append(end.Coord.Row).Append('-');
            }
            for (int index = 0; index < segment.Length; index++)
            {
                if (index != 0)
                    key.Append(',');
                key.Append((int)_nodes[segment[index]].Type);
            }
            return key.ToString();
        }

        private static bool AnyOverlappingSegments(List<int[]> existing, int[] segment)
        {
            foreach (int[] candidate in existing)
            {
                if (Overlapping(candidate, segment))
                    return true;
            }
            return false;
        }

        private static bool Overlapping(int[] a, int[] b)
        {
            if (a.Length < 3 || b.Length < 3)
                return false;
            for (int index = 1; index <= a.Length - 2; index++)
            {
                if (a[index] == b[index])
                    return true;
            }
            return false;
        }

        private bool PrunePaths(List<List<int[]>> matchingSegments)
        {
            foreach (List<int[]> segments in matchingSegments)
            {
                int[] order = new int[segments.Count];
                for (int index = 0; index < order.Length; index++)
                    order[index] = index;
                RunRandom.UnstableShuffle(ref _rng, order);
                List<int[]> shuffled = new(segments.Count);
                for (int index = 0; index < order.Length; index++)
                    shuffled.Add(segments[order[index]]);
                if (PruneAllButLast(shuffled) != 0)
                    return true;
                if (BreakParentChildRelationship(shuffled))
                    return true;
            }
            return false;
        }

        private int PruneAllButLast(List<int[]> matches)
        {
            int removed = 0;
            for (int index = 0; index < matches.Count; index++)
            {
                if (removed == matches.Count - 1)
                    return removed;
                if (PruneSegment(matches[index]))
                    removed++;
            }
            return removed;
        }

        private bool PruneSegment(int[] segment)
        {
            bool result = false;
            for (int index = 0; index < segment.Length - 1; index++)
            {
                int point = segment[index];
                if (!IsInMap(point))
                    return true;
                MapNode node = _nodes[point];
                if (node.ChildCount > 1 || node.ParentCount > 1)
                    continue;
                bool blocked = false;
                for (int parentIndex = 0; parentIndex < node.ParentCount; parentIndex++)
                {
                    int parent = node.Parents[parentIndex];
                    if (_nodes[parent].ChildCount == 1 && !IsRemoved(parent))
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked)
                    continue;
                bool hasJunction = false;
                for (int tail = index; tail < segment.Length; tail++)
                {
                    MapNode candidate = _nodes[segment[tail]];
                    if (candidate.ChildCount > 1 && candidate.ParentCount == 1)
                    {
                        hasJunction = true;
                        break;
                    }
                }
                if (hasJunction)
                    continue;
                if (_nodes[segment[^1]].ParentCount == 1)
                    return false;
                bool childKeepsParent = false;
                for (int childIndex = 0; childIndex < node.ChildCount; childIndex++)
                {
                    int child = node.Children[childIndex];
                    if (Contains(segment, child))
                        continue;
                    if (_nodes[child].ParentCount == 1)
                    {
                        childKeepsParent = true;
                        break;
                    }
                }
                if (childKeepsParent)
                    continue;
                RemovePoint(point);
                result = true;
            }
            return result;
        }

        private static bool Contains(int[] segment, int node)
        {
            for (int index = 0; index < segment.Length; index++)
            {
                if (segment[index] == node)
                    return true;
            }
            return false;
        }

        private void RemovePoint(int point)
        {
            MapNode node = _nodes[point];
            SetGrid(node.Coord.Col, node.Coord.Row, -1);
            for (int index = 0; index < _startCount; index++)
            {
                if (_starts[index] != point)
                    continue;
                for (int move = index; move < _startCount - 1; move++)
                    _starts[move] = _starts[move + 1];
                _startCount--;
                break;
            }
            while (_nodes[point].ChildCount > 0)
                RemoveChild(point, _nodes[point].Children[0]);
            while (_nodes[point].ParentCount > 0)
                RemoveChild(_nodes[point].Parents[0], point);
        }

        private bool IsInMap(int point)
        {
            MapNode node = _nodes[point];
            if (GridAt(node.Coord.Col, node.Coord.Row) != point
                && node.Type != RunMapPointType.Ancient)
                return node.Type == RunMapPointType.Boss;
            return true;
        }

        private bool IsRemoved(int point)
        {
            MapNode node = _nodes[point];
            return GridAt(node.Coord.Col, node.Coord.Row) != point;
        }

        private bool BreakParentChildRelationship(List<int[]> matches)
        {
            foreach (int[] segment in matches)
            {
                bool removed = false;
                for (int index = 0; index < segment.Length - 1; index++)
                {
                    int point = segment[index];
                    if (_nodes[point].ChildCount < 2)
                        continue;
                    int next = segment[index + 1];
                    if (_nodes[next].ParentCount == 1)
                        continue;
                    RemoveChild(point, next);
                    removed = true;
                }
                if (removed)
                    return true;
            }
            return false;
        }

        private void CenterGrid()
        {
            int columns = _rules.Width;
            bool leftEmpty = ColumnEmpty(0) && ColumnEmpty(1);
            bool rightEmpty = ColumnEmpty(columns - 1) && ColumnEmpty(columns - 2);
            int shift = 0;
            if (leftEmpty && !rightEmpty)
                shift = -1;
            else if (!leftEmpty && rightEmpty)
                shift = 1;
            if (shift == 0)
                return;
            if (shift > 0)
            {
                for (int row = 0; row < _mapLength; row++)
                {
                    for (int col = columns - 1; col >= 0; col--)
                    {
                        int node = GridAt(col, row);
                        SetGrid(col, row, -1);
                        int target = col + shift;
                        if (target >= columns || node < 0)
                            continue;
                        SetGrid(target, row, node);
                        _nodes[node].Coord.Col = (byte)target;
                    }
                }
            }
            else
            {
                for (int row = 0; row < _mapLength; row++)
                {
                    for (int col = 0; col < columns; col++)
                    {
                        int node = GridAt(col, row);
                        SetGrid(col, row, -1);
                        int target = col + shift;
                        if (target < 0 || node < 0)
                            continue;
                        SetGrid(target, row, node);
                        _nodes[node].Coord.Col = (byte)target;
                    }
                }
            }
        }

        private bool ColumnEmpty(int col)
        {
            for (int row = 0; row < _mapLength; row++)
            {
                if (GridAt(col, row) >= 0)
                    return false;
            }
            return true;
        }

        private void SpreadAdjacentMapPoints()
        {
            int columns = _rules.Width;
            Span<bool> allowed = stackalloc bool[columns];
            for (int row = 0; row < _mapLength; row++)
            {
                int[] points = RowPoints(row);
                bool moved;
                do
                {
                    moved = false;
                    for (int index = 0; index < points.Length; index++)
                    {
                        int point = points[index];
                        int col = _nodes[point].Coord.Col;
                        int best = col;
                        int bestGap = ComputeGap(col, points, point);
                        AllowedPositions(point, allowed);
                        for (int candidate = 0; candidate < columns; candidate++)
                        {
                            if (!allowed[candidate] || candidate == col)
                                continue;
                            int occupant = GridAt(candidate, row);
                            if (occupant >= 0 && occupant != point)
                                continue;
                            int gap = ComputeGap(candidate, points, point);
                            if (gap > bestGap)
                            {
                                best = candidate;
                                bestGap = gap;
                            }
                        }
                        if (best == col)
                            continue;
                        SetGrid(col, row, -1);
                        SetGrid(best, row, point);
                        _nodes[point].Coord.Col = (byte)best;
                        moved = true;
                    }
                }
                while (moved);
            }
        }

        private int[] RowPoints(int row)
        {
            int[] rowPoints = new int[_rules.Width];
            int count = 0;
            for (int col = 0; col < _rules.Width; col++)
            {
                int node = GridAt(col, row);
                if (node >= 0)
                    rowPoints[count++] = node;
            }
            Array.Resize(ref rowPoints, count);
            return rowPoints;
        }

        private void AllowedPositions(int point, Span<bool> allowed)
        {
            int columns = _rules.Width;
            for (int index = 0; index < columns; index++)
                allowed[index] = true;
            MapNode node = _nodes[point];
            for (int index = 0; index < node.ParentCount; index++)
            {
                int parent = node.Parents[index];
                for (int candidate = 0; candidate < columns; candidate++)
                {
                    if (Math.Abs(candidate - _nodes[parent].Coord.Col) > 1)
                        allowed[candidate] = false;
                }
            }
            for (int index = 0; index < node.ChildCount; index++)
            {
                int child = node.Children[index];
                for (int candidate = 0; candidate < columns; candidate++)
                {
                    if (Math.Abs(candidate - _nodes[child].Coord.Col) > 1)
                        allowed[candidate] = false;
                }
            }
        }

        private int ComputeGap(int candidate, int[] rowPoints, int current)
        {
            int gap = int.MaxValue;
            for (int index = 0; index < rowPoints.Length; index++)
            {
                if (rowPoints[index] == current)
                    continue;
                gap = Math.Min(gap, Math.Abs(candidate - _nodes[rowPoints[index]].Coord.Col));
            }
            return gap;
        }

        private void StraightenPaths()
        {
            int columns = _rules.Width;
            for (int row = 0; row < _mapLength; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    int point = GridAt(col, row);
                    if (point < 0)
                        continue;
                    MapNode node = _nodes[point];
                    if (node.ParentCount != 1 || node.ChildCount != 1)
                        continue;
                    int parent = node.Parents[0];
                    int child = node.Children[0];
                    bool left = node.Coord.Col < _nodes[child].Coord.Col
                        && node.Coord.Col < _nodes[parent].Coord.Col;
                    bool right = node.Coord.Col > _nodes[child].Coord.Col
                        && node.Coord.Col > _nodes[parent].Coord.Col;
                    if (left && col < columns - 1)
                    {
                        int target = col + 1;
                        if (GridAt(target, row) >= 0)
                            continue;
                        _nodes[point].Coord.Col = (byte)target;
                        SetGrid(col, row, -1);
                        SetGrid(target, row, point);
                    }
                    if (right && col > 0)
                    {
                        int target = col - 1;
                        if (GridAt(target, row) >= 0)
                            continue;
                        _nodes[point].Coord.Col = (byte)target;
                        SetGrid(col, row, -1);
                        SetGrid(target, row, point);
                    }
                }
            }
        }

        private RunMap Export()
        {
            RunMap map = default;
            map.Width = (byte)_rules.Width;
            map.Height = (byte)_mapLength;
            map.Starting = _nodes[_starting].Coord;
            map.Boss = _nodes[_boss].Coord;
            map.HasSecondBoss = _secondBoss >= 0 ? (byte)1 : (byte)0;
            if (_secondBoss >= 0)
                map.SecondBoss = _nodes[_secondBoss].Coord;
            for (int index = 0; index < _startCount; index++)
                map.Starts[map.StartCount++] = _nodes[_starts[index]].Coord;
            foreach (int node in AllGridPoints())
                AddPoint(ref map, node);
            AddPoint(ref map, _starting);
            AddPoint(ref map, _boss);
            if (_secondBoss >= 0)
                AddPoint(ref map, _secondBoss);
            return map;
        }

        private void AddPoint(ref RunMap map, int node)
        {
            if (map.NodeCount >= RunLimits.MapPoints)
                throw new CapacityException("RunState.Map", RunLimits.MapPoints);
            MapNode source = _nodes[node];
            if (source.ChildCount > RunLimits.MapChildren)
                throw new CapacityException("RunState.MapChildren", RunLimits.MapChildren);
            RunMapPoint point = default;
            point.Coord = source.Coord;
            point.Type = source.Type;
            point.CanBeModified = source.CanBeModified ? (byte)1 : (byte)0;
            point.ChildCount = (byte)source.ChildCount;
            for (int index = 0; index < source.ChildCount; index++)
                point.Children[index] = _nodes[source.Children[index]].Coord;
            map.Nodes[map.NodeCount++] = point;
        }
    }

    private struct MapNode
    {
        public RunCoord Coord;
        public RunMapPointType Type;
        public bool CanBeModified;
        public int ChildCount;
        public int ParentCount;
        public MapNodeChildren Children;
        public MapNodeParents Parents;
    }

    [System.Runtime.CompilerServices.InlineArray(Relations)]
    private struct MapNodeChildren
    {
        private int _first;
    }

    [System.Runtime.CompilerServices.InlineArray(Relations)]
    private struct MapNodeParents
    {
        private int _first;
    }
}
