namespace RustyTales.Core.Grids;

/// <summary>
/// Canonical pure-.NET pathfinding, line-of-sight, and reachability algorithms operating
/// over any <see cref="IGridTopology"/> (edge-wall dungeon maps, 2D tilemaps, and tactical battlefields).
/// </summary>
public static class GridSearch
{
    private static readonly (int Dx, int Dy)[] OrthogonalSteps = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int Dx, int Dy)[] DiagonalSteps = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>Grid distance between <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static int Distance(GridCell a, GridCell b, bool diagonal)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        return diagonal ? Math.Max(dx, dy) : dx + dy;
    }

    /// <summary>Cells one step from <paramref name="cell"/> inside <paramref name="grid"/>, straight steps first.</summary>
    public static IEnumerable<GridCell> Neighbours(IGridTopology grid, GridCell cell)
    {
        (int Dx, int Dy)[] steps = grid.AllowsDiagonal ? DiagonalSteps : OrthogonalSteps;
        for (int i = 0; i < steps.Length; i++)
        {
            GridCell next = new(cell.X + steps[i].Dx, cell.Y + steps[i].Dy);
            if (grid.Contains(next))
            {
                yield return next;
            }
        }
    }

    /// <summary>
    /// Half-cell centre-to-centre line of sight over <paramref name="grid"/>.
    /// Checks both cell-based <see cref="CellTraits.BlocksSight"/> on intermediate cells
    /// and edge-based <see cref="IGridTopology.BlocksSightBetween"/> across crossed cell edges.
    /// Where the ray runs exactly between cells, it is blocked only if every covering cell blocks.
    /// </summary>
    public static bool CanSee(IGridTopology grid, GridCell from, GridCell to)
    {
        if (from == to)
        {
            return true;
        }

        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy)) * 2;
        GridCell previous = from;

        for (int step = 1; step < steps; step++)
        {
            // Points along the line at half-cell spacing, in doubled coordinates so they stay whole.
            int numeratorX = from.X * steps * 2 + dx * step * 2 + steps;
            int numeratorY = from.Y * steps * 2 + dy * step * 2 + steps;
            int denominator = steps * 2;
            List<int> xs = Covering(numeratorX, denominator);
            List<int> ys = Covering(numeratorY, denominator);

            // Check edge-based sight blocking when crossing directly from one cell to an orthogonal neighbour.
            if (xs.Count == 2 && ys.Count == 1)
            {
                GridCell left = new(xs[0], ys[0]);
                GridCell right = new(xs[1], ys[0]);
                if (grid.Contains(left) && grid.Contains(right) && grid.BlocksSightBetween(left, right))
                {
                    return false;
                }
            }
            else if (ys.Count == 2 && xs.Count == 1)
            {
                GridCell top = new(xs[0], ys[0]);
                GridCell bottom = new(xs[0], ys[1]);
                if (grid.Contains(top) && grid.Contains(bottom) && grid.BlocksSightBetween(top, bottom))
                {
                    return false;
                }
            }
            else if (xs.Count == 2 && ys.Count == 2)
            {
                // Passing exactly through a 4-cell corner: blocked if both orthogonal edge paths around the corner block.
                GridCell a = new(xs[0], ys[0]);
                GridCell b = new(xs[1], ys[0]);
                GridCell c = new(xs[0], ys[1]);
                GridCell d = new(xs[1], ys[1]);
                bool upperBlocked = !grid.Contains(a) || !grid.Contains(b) || grid.BlocksSightBetween(a, b)
                    || !grid.Contains(d) || grid.BlocksSightBetween(b, d);
                bool lowerBlocked = !grid.Contains(a) || !grid.Contains(c) || grid.BlocksSightBetween(a, c)
                    || !grid.Contains(d) || grid.BlocksSightBetween(c, d);
                if (upperBlocked && lowerBlocked)
                {
                    return false;
                }
            }

            List<GridCell> cells = [];
            for (int ix = 0; ix < xs.Count; ix++)
            {
                for (int iy = 0; iy < ys.Count; iy++)
                {
                    GridCell cell = new(xs[ix], ys[iy]);
                    if (cell != from && cell != to)
                    {
                        cells.Add(cell);
                    }
                }
            }

            if (cells.Count > 0 && cells.All(cell => !grid.Contains(cell) || grid.TraitsAt(cell).BlocksSight))
            {
                return false;
            }

            if (xs.Count == 1 && ys.Count == 1)
            {
                GridCell current = new(xs[0], ys[0]);
                if (current != previous && Math.Abs(current.X - previous.X) + Math.Abs(current.Y - previous.Y) == 1)
                {
                    if (grid.Contains(previous) && grid.Contains(current) && grid.BlocksSightBetween(previous, current))
                    {
                        return false;
                    }
                }

                previous = current;
            }
        }

        if (Math.Abs(to.X - previous.X) + Math.Abs(to.Y - previous.Y) == 1
            && grid.Contains(previous)
            && grid.Contains(to)
            && grid.BlocksSightBetween(previous, to))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Forward Dijkstra from <paramref name="start"/> within <paramref name="budget"/> movement cost,
    /// returning the cheapest path to every reachable cell.
    /// </summary>
    public static Dictionary<GridCell, (int Cost, IReadOnlyList<GridCell> Path)> ReachableWithin(
        IGridTopology grid,
        GridCell start,
        int budget,
        IReadOnlySet<GridCell>? blocked = null)
    {
        Dictionary<GridCell, (int Cost, IReadOnlyList<GridCell> Path)> paths = new()
        {
            [start] = (0, []),
        };
        PriorityQueue<GridCell, int> frontier = new();
        frontier.Enqueue(start, 0);

        while (frontier.TryDequeue(out GridCell cell, out int cost))
        {
            if (paths[cell].Cost != cost)
            {
                continue;
            }

            foreach (GridCell next in Neighbours(grid, cell))
            {
                if (!grid.CanStep(cell, next) || (blocked is not null && blocked.Contains(next)))
                {
                    continue;
                }

                int stepCost = Math.Max(1, grid.TraitsAt(next).MoveCost);
                int total = cost + stepCost;
                if (total > budget || (paths.TryGetValue(next, out (int Cost, IReadOnlyList<GridCell> Path) known) && known.Cost <= total))
                {
                    continue;
                }

                List<GridCell> path = [.. paths[cell].Path, next];
                paths[next] = (total, path);
                frontier.Enqueue(next, total);
            }
        }

        return paths;
    }

    /// <summary>
    /// Reverse Dijkstra from <paramref name="goals"/> (cost 0 at every goal cell):
    /// returns the minimum movement cost required to reach any goal cell from each cell,
    /// where stepping from <c>neighbour</c> into <c>cell</c> pays <c>cell</c>'s move cost.
    /// </summary>
    public static Dictionary<GridCell, int> CostsToGoals(
        IGridTopology grid,
        IEnumerable<GridCell> goals,
        IReadOnlySet<GridCell>? blocked = null)
    {
        Dictionary<GridCell, int> costs = [];
        PriorityQueue<GridCell, int> frontier = new();

        foreach (GridCell goal in goals)
        {
            if (costs.TryAdd(goal, 0))
            {
                frontier.Enqueue(goal, 0);
            }
        }

        while (frontier.TryDequeue(out GridCell cell, out int cost))
        {
            if (cost > costs[cell])
            {
                continue;
            }

            int enterCellCost = Math.Max(1, grid.TraitsAt(cell).MoveCost);
            foreach (GridCell neighbour in Neighbours(grid, cell))
            {
                if (!grid.TraitsAt(neighbour).Passable
                    || (blocked is not null && blocked.Contains(neighbour))
                    || !grid.CanStep(neighbour, cell))
                {
                    continue;
                }

                int through = cost + enterCellCost;
                if (!costs.TryGetValue(neighbour, out int known) || through < known)
                {
                    costs[neighbour] = through;
                    frontier.Enqueue(neighbour, through);
                }
            }
        }

        return costs;
    }

    /// <summary>BFS flood fill of all cells reachable from <paramref name="start"/>.</summary>
    public static HashSet<GridCell> FloodFill(
        IGridTopology grid,
        GridCell start,
        IReadOnlySet<GridCell>? blocked = null)
    {
        HashSet<GridCell> visited = [];
        if (!grid.Contains(start))
        {
            return visited;
        }

        Queue<GridCell> queue = new();
        visited.Add(start);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            GridCell cell = queue.Dequeue();
            foreach (GridCell next in Neighbours(grid, cell))
            {
                if (visited.Contains(next) || !grid.CanStep(cell, next) || (blocked is not null && blocked.Contains(next)))
                {
                    continue;
                }

                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        return visited;
    }

    private static List<int> Covering(int numerator, int denominator)
    {
        int index = (int)Math.Floor((double)numerator / denominator);
        return numerator % denominator == 0 ? [index - 1, index] : [index];
    }
}
