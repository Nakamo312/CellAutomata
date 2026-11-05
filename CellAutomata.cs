using System.Collections.Generic;
using System;
using System.Collections;
using System.Text;
using System.Runtime.Intrinsics;


namespace RoomGenerate;

public enum CellType
{
    Empty,
    Obstacle,
    Road,
    Hub,
    Monster,
    Special
}

public readonly record struct Vector2<T>(T x, T y);

public sealed record CellTransition(
    CellType NeighborType,
    int MinCount,
    int MaxCount,
    CellType Target,
    int Lifetime = 0,
    float Chance = 1f);

public class CellRule
{
    // Transitions are checked in list order; the first successful one wins.
    public List<CellTransition> Transitions = new();
}

public class Cell
{
    public CellType value = CellType.Empty;
    public int[] currentLifeTime = Array.Empty<int>();
    public Vector256<int> neighboors = new();
}



public class CellAutomata : IEnumerator<CellType[,]>, IEnumerable<CellType[,]>
{
    private Cell[,] _layerGrid;
    private readonly CellType[,] _initialGrid;
    private Dictionary<CellType, CellRule> _rules;
    private int _maxIterations;
    private int _currentIteration = 0;
    private Vector2<int> _size;
    public int MaxIterations { get => _maxIterations; }
    public Vector2<int> Size { get => _size; }
    private System.Random _random = new();
    private readonly int? _randomSeed;
    private readonly bool[,]? _fixedCells;

    public CellType[,] Current
    {
        get {
            var res = new CellType[_size.x, _size.y];
            for (int i = 0; i < _size.x; i++)
            {
                for (int j = 0; j < _size.y; j++)
                {
                    res[i, j] = _layerGrid[i, j].value;
                }
            }
            return res;
        }
    }

    object IEnumerator.Current => Current;

    public CellAutomata(int maxIterations, Vector2<int> size, CellType[,] grid, Dictionary<CellType, CellRule> rules,
        int? randomSeed = null, bool[,]? fixedCells = null)
    {
        _maxIterations = maxIterations;
        _size = size;
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfNegative(maxIterations);
        if (size.x <= 0 || size.y <= 0 || grid.GetLength(0) != size.x || grid.GetLength(1) != size.y)
        {
            throw new ArgumentException("Invalid grid size");
        }

        if (fixedCells is not null &&
            (fixedCells.GetLength(0) != size.x || fixedCells.GetLength(1) != size.y))
            throw new ArgumentException("Invalid fixed-cell mask size", nameof(fixedCells));

        _randomSeed = randomSeed;
        _random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        _fixedCells = fixedCells is null ? null : (bool[,])fixedCells.Clone();

        _rules = rules;
        _initialGrid = (CellType[,])grid.Clone();
        _layerGrid = CreateLayer();
        UpdateNeighbors();
    }

    private Cell[,] CreateLayer()
    {
        var layer = new Cell[_size.x, _size.y];
        for (int i = 0; i < _size.x; i++)
        {
            for (int j = 0; j < _size.y; j++)
            {
                var value = _initialGrid[i, j];
                layer[i, j] = new Cell
                {
                    value = value,
                    currentLifeTime = CreateRuleTimers(value)
                };
            }
        }
        return layer;
    }
    public bool MoveNext()
    {
        if (_currentIteration >= _maxIterations)
            return false;

        NextIteration();
        _currentIteration++;
        return true;
    }

    private static Vector256<int> TypeVector(CellType value) =>
        Vector256<int>.Zero.WithElement((int)value, 1);

    private int[] CreateRuleTimers(CellType value) =>
        _rules.TryGetValue(value, out var rule)
            ? new int[rule.Transitions.Count]
            : Array.Empty<int>();

    private void UpdateNeighbors()
    {
        var sum = new Vector256<int>[_size.x, _size.y];
        for (int i = 0; i < _size.x; i++)
        {
            for (int j = 0; j < _size.y; j++)
            {
                sum[i, j] =
                    TypeVector(_layerGrid[i, j].value) +
                    (i == 0 ? Vector256<int>.Zero : TypeVector(_layerGrid[i - 1, j].value)) +
                    (i == _size.x - 1 ? Vector256<int>.Zero : TypeVector(_layerGrid[i + 1, j].value));
            }
        }



        for (int i = 0; i < _size.x; i++)
        {
            for (int j = 0; j < _size.y; j++)
            {
                _layerGrid[i, j].neighboors = sum[i, j] +
                    (j == 0 ? Vector256<int>.Zero : sum[i, j - 1]) +
                    (j == _size.y - 1 ? Vector256<int>.Zero : sum[i , j + 1]) - TypeVector(_layerGrid[i, j].value);
            }
        }
    }

    public void NextIteration()
    {
        UpdateNeighbors();

        for (int i = 0; i < _size.x; i++)
        {
            for (int j = 0; j < _size.y; j++)
            {
                var current = _layerGrid[i, j];
                if (_fixedCells?[i, j] == true) continue;
                if (!_rules.ContainsKey(current.value)) continue;
                var rule = _rules[current.value];

                for (int index = 0; index < rule.Transitions.Count; index++)
                {
                    var transition = rule.Transitions[index];
                    var count = current.neighboors[(int)transition.NeighborType];
                    if (count >= transition.MinCount && count <= transition.MaxCount)
                    {
                        if (current.currentLifeTime[index] >= transition.Lifetime)
                        {
                            if (_random.NextDouble() < transition.Chance)
                            {
                                current.value = transition.Target;
                                current.currentLifeTime = CreateRuleTimers(current.value);
                                break;
                            }
                        }
                        if (current.currentLifeTime[index] < int.MaxValue)
                            current.currentLifeTime[index]++;
                    }
                }

            }
        }

        // for (int i = 0; i < _size.x; i++)
        // {
        //     for (int j = 0; j < _size.y; j++)
        //     {
        //         Console.Write($"{res[i, j][k]} ");
        //     }
        //     Console.WriteLine();
        // }
        // Console.WriteLine();
    }

    public void Reset()
    {
        _layerGrid = CreateLayer();
        UpdateNeighbors();
        _currentIteration = 0;
        if (_randomSeed.HasValue) _random = new Random(_randomSeed.Value);
    }
    void IDisposable.Dispose()
    {

    }

    public IEnumerator<CellType[,]> GetEnumerator()
    {
        return this;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }


public override string ToString() => Render(Current);

public static string Render(CellType[,] grid)
{
    var result = new StringBuilder();

    for (int i = 0; i < grid.GetLength(0); i++)
    {
        for (int j = 0; j < grid.GetLength(1); j++)
        {
            result.Append(GetColoredCell(grid[i, j]));
        }

        if (i < grid.GetLength(0) - 1)
            result.AppendLine();
    }

    return result.ToString();
}

private static string GetColoredCell(CellType cellType)
{
    var (red, green, blue) = cellType switch
    {
        CellType.Empty => (20, 20, 20),
        CellType.Obstacle => (100, 100, 100),
        CellType.Road => (40, 130, 240),
        CellType.Hub => (40, 200, 80),
        CellType.Monster => (220, 50, 50),
        CellType.Special => (180, 70, 220),
        _ => (0, 0, 0)
    };

    return $"\u001b[48;2;{red};{green};{blue}m  \u001b[0m";
}

}
