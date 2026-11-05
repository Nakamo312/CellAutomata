using System.Text;

namespace RoomGenerate;

public sealed record SimulationResult(CellType[,] Grid, int Steps);

public static class SimulationRunner
{
    public static SimulationResult Run(RoomScenario scenario, Dictionary<CellType, CellRule> rules,
        int steps, int simulationSeed, Action<int, CellType[,]>? onStep = null)
    {
        var automata = new CellAutomata(steps, new(scenario.Width, scenario.Height),
            scenario.CreateInitialGrid(), rules, simulationSeed, scenario.CreateFixedCells());
        int completed = 0;
        while (automata.MoveNext())
        {
            completed++;
            if (onStep is not null) onStep(completed, automata.Current);
        }
        return new(automata.Current, completed);
    }

    public static string[] ToRows(CellType[,] grid)
    {
        var rows = new string[grid.GetLength(1)];
        for (int y = 0; y < rows.Length; y++)
        {
            var row = new StringBuilder();
            for (int x = 0; x < grid.GetLength(0); x++)
                row.Append(grid[x, y] switch
                {
                    CellType.Empty => '.', CellType.Obstacle => '#', CellType.Road => '+',
                    CellType.Hub => 'H', CellType.Monster => 'M', CellType.Special => 'S',
                    _ => throw new ArgumentException("Unknown cell type.")
                });
            rows[y] = row.ToString();
        }
        return rows;
    }
}
