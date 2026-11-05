
using System;
using System.Collections.Generic;
using RoomGenerate;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && !int.TryParse(args[0], out _))
        {
            Environment.ExitCode = TrainingCli.Run(args);
            return;
        }

        var maxIterations = args.Length > 0 ? int.Parse(args[0]) : 25;
        var sizeX = args.Length > 1 ? int.Parse(args[1]) : 30;
        var sizeY = args.Length > 2 ? int.Parse(args[2]) : 20;

        ArgumentOutOfRangeException.ThrowIfNegative(maxIterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeY);

        var grid = new CellType[sizeX, sizeY];

        void Put(int x, int y, CellType type)
        {
            if ((uint)x < (uint)sizeX && (uint)y < (uint)sizeY)
                grid[x, y] = type;
        }

        int centerX = sizeX / 2;
        int centerY = sizeY / 2;

        // Empty — мёртвая клетка, Road — живая. За границей поля соседей нет.
        // Планер.
        Put(2, 3, CellType.Road);
        Put(3, 4, CellType.Road);
        Put(4, 2, CellType.Road);
        Put(4, 3, CellType.Road);
        Put(4, 4, CellType.Road);

        // Мигалка: период два хода.
        Put(centerX, centerY - 1, CellType.Road);
        Put(centerX, centerY, CellType.Road);
        Put(centerX, centerY + 1, CellType.Road);

        // Неподвижный блок 2×2.
        Put(centerX + 5, centerY, CellType.Road);
        Put(centerX + 5, centerY + 1, CellType.Road);
        Put(centerX + 6, centerY, CellType.Road);
        Put(centerX + 6, centerY + 1, CellType.Road);

        var rules = CreateLifeRules();

        var size = new Vector2<int>(sizeX, sizeY);
        var automata = new CellAutomata(maxIterations, size, grid, rules);

        Console.WriteLine(automata);
        foreach (var _ in automata)
        {
            if (!Console.IsOutputRedirected) Console.Clear();
            Console.WriteLine(automata);
            System.Threading.Thread.Sleep(20);
        }
    }

    public static Dictionary<CellType, CellRule> CreateLifeRules() => new()
    {
        [CellType.Empty] = new CellRule
        {
            Transitions =
            {
                // B3: рождение при ровно трёх живых соседях.
                new(CellType.Road, MinCount: 3, MaxCount: 3, Target: CellType.Road)
            }
        },
        [CellType.Road] = new CellRule
        {
            Transitions =
            {
                // S23: при двух или трёх соседях перехода нет, клетка живёт.
                new(CellType.Road, MinCount: 0, MaxCount: 1, Target: CellType.Empty),
                new(CellType.Road, MinCount: 4, MaxCount: 8, Target: CellType.Empty)
            }
        }
    };
}
