using RoomGenerate;

namespace RoomGenerate.Checks;

internal static class LifeRulesChecks
{
    private static int _comparisons;

    private static CellAutomata Life(CellType[,] grid) => new(20,
        new(grid.GetLength(0), grid.GetLength(1)), grid, global::Program.CreateLifeRules());

    private static void Equal(CellType[,] actual, CellType[,] expected)
    {
        for (int x = 0; x < expected.GetLength(0); x++)
        for (int y = 0; y < expected.GetLength(1); y++)
        {
            if (actual[x, y] != expected[x, y])
                throw new Exception($"Cell ({x}, {y}): expected {expected[x, y]}, got {actual[x, y]}.");
            _comparisons++;
        }
    }

    private static CellType[,] Step(CellType[,] before)
    {
        int width = before.GetLength(0), height = before.GetLength(1);
        var result = new CellType[width, height];
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            int neighbors = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if ((dx != 0 || dy != 0) && nx >= 0 && ny >= 0 && nx < width && ny < height &&
                    before[nx, ny] == CellType.Road) neighbors++;
            }
            bool alive = neighbors == 3 || before[x, y] == CellType.Road && neighbors == 2;
            result[x, y] = alive ? CellType.Road : CellType.Empty;
        }
        return result;
    }

    private static CellType[,] Pattern(params (int x, int y)[] cells)
    {
        var grid = new CellType[12, 12];
        foreach (var (x, y) in cells) grid[x, y] = CellType.Road;
        return grid;
    }

    public static void Main()
    {
        // Every possible 3×3 state, including the center: birth and survival thresholds.
        for (int mask = 0; mask < 512; mask++)
        {
            var grid = new CellType[3, 3];
            for (int bit = 0; bit < 9; bit++)
                if ((mask & (1 << bit)) != 0) grid[bit / 3, bit % 3] = CellType.Road;
            var life = Life(grid);
            life.NextIteration();
            Equal(life.Current, Step(grid));
        }

        var random = new Random(42);
        for (int sample = 0; sample < 100; sample++)
        {
            var grid = new CellType[random.Next(1, 13), random.Next(1, 13)];
            for (int x = 0; x < grid.GetLength(0); x++)
            for (int y = 0; y < grid.GetLength(1); y++)
                grid[x, y] = random.Next(2) == 0 ? CellType.Empty : CellType.Road;
            var life = Life(grid);
            var expected = grid;
            for (int step = 0; step < 8; step++)
            {
                expected = Step(expected);
                life.NextIteration();
                Equal(life.Current, expected);
            }
            life.Reset();
            Equal(life.Current, grid);
        }

        var block = Pattern((3, 3), (3, 4), (4, 3), (4, 4));
        var stillLife = Life(block);
        for (int step = 0; step < 4; step++) { stillLife.NextIteration(); Equal(stillLife.Current, block); }

        var blinker = Pattern((4, 3), (4, 4), (4, 5));
        var oscillator = Life(blinker);
        oscillator.NextIteration();
        Equal(oscillator.Current, Pattern((3, 4), (4, 4), (5, 4)));
        oscillator.NextIteration();
        Equal(oscillator.Current, blinker);

        var glider = Life(Pattern((2, 3), (3, 4), (4, 2), (4, 3), (4, 4)));
        for (int step = 0; step < 4; step++) glider.NextIteration();
        Equal(glider.Current, Pattern((3, 4), (4, 5), (5, 3), (5, 4), (5, 5)));

        foreach (int lifetime in new[] { 0, 1, 2, 5 })
        {
            var rule = new CellRule { Transitions = { new(CellType.Empty, 0, 0, CellType.Road, lifetime) } };
            var delayed = new CellAutomata(10, new(1, 1), new CellType[1, 1],
                new() { [CellType.Empty] = rule });
            for (int turn = 0; turn < lifetime; turn++)
            {
                delayed.NextIteration();
                Equal(delayed.Current, new CellType[1, 1]);
            }
            delayed.NextIteration();
            Equal(delayed.Current, new[,] { { CellType.Road } });
            delayed.Reset();
            Equal(delayed.Current, new CellType[1, 1]);
        }

        // Two rules for the same neighbor type must not share their waiting counters.
        var separateTimers = new CellRule
        {
            Transitions =
            {
                new(CellType.Empty, 0, 0, CellType.Obstacle, Lifetime: 5),
                new(CellType.Empty, 0, 0, CellType.Road, Lifetime: 1)
            }
        };
        var independent = new CellAutomata(2, new(1, 1), new CellType[1, 1],
            new() { [CellType.Empty] = separateTimers });
        independent.NextIteration(); Equal(independent.Current, new CellType[1, 1]);
        independent.NextIteration(); Equal(independent.Current, new[,] { { CellType.Road } });

        // Rule counts are not constrained by Vector256's eight lanes.
        var manyRules = new CellRule();
        for (int i = 0; i < 9; i++)
            manyRules.Transitions.Add(new(CellType.Empty, 0, 0, CellType.Obstacle, Chance: 0f));
        manyRules.Transitions.Add(new(CellType.Empty, 0, 0, CellType.Road, Lifetime: 1));
        var many = new CellAutomata(2, new(1, 1), new CellType[1, 1],
            new() { [CellType.Empty] = manyRules });
        many.NextIteration(); Equal(many.Current, new CellType[1, 1]);
        many.NextIteration(); Equal(many.Current, new[,] { { CellType.Road } });

        // With overlapping successful rules, list order defines priority.
        var priority = new CellAutomata(1, new(1, 1), new CellType[1, 1], new()
        {
            [CellType.Empty] = new CellRule { Transitions =
            {
                new(CellType.Empty, 0, 0, CellType.Road),
                new(CellType.Empty, 0, 0, CellType.Obstacle)
            } }
        });
        priority.NextIteration(); Equal(priority.Current, new[,] { { CellType.Road } });

        // Waiting is cumulative: a non-matching turn preserves an existing counter.
        var intermittent = new CellAutomata(3, new(2, 1), new[,] { { CellType.Empty }, { CellType.Road } }, new()
        {
            [CellType.Empty] = new CellRule { Transitions =
                { new(CellType.Road, 1, 1, CellType.Special, Lifetime: 1) } },
            [CellType.Road] = new CellRule { Transitions =
                { new(CellType.Empty, 1, 1, CellType.Obstacle) } },
            [CellType.Obstacle] = new CellRule { Transitions =
                { new(CellType.Empty, 1, 1, CellType.Road) } }
        });
        intermittent.NextIteration();
        Equal(intermittent.Current, new[,] { { CellType.Empty }, { CellType.Obstacle } });
        intermittent.NextIteration();
        Equal(intermittent.Current, new[,] { { CellType.Empty }, { CellType.Road } });
        intermittent.NextIteration();
        Equal(intermittent.Current, new[,] { { CellType.Special }, { CellType.Obstacle } });

        var fixedCell = new CellAutomata(1, new(1, 1), new CellType[1, 1],
            new() { [CellType.Empty] = manyRules }, fixedCells: new[,] { { true } });
        fixedCell.NextIteration(); Equal(fixedCell.Current, new CellType[1, 1]);

        Console.WriteLine($"PASS: {_comparisons} cell comparisons; B3/S23, block, blinker, glider, boundaries, reset, timers and rule priority.");
    }
}
