namespace RoomGenerate;

// Coordinates describe the experiment; they introduce no new cell types.
public sealed record RoomScenario(
    int Width,
    int Height,
    Vector2<int>[] Doors,
    Vector2<int> Hub,
    Vector2<int>[] ObstacleSeeds)
{
    public static RoomScenario Create(int width = 30, int height = 20)
    {
        if (width < 7 || height < 7)
            throw new ArgumentException("Room dimensions must be at least 7.");
        var hub = new Vector2<int>(width / 2, height / 2);
        var doors = new[]
        {
            new Vector2<int>(0, height / 2), new(width - 1, height / 2),
            new(width / 2, 0), new(width / 2, height - 1)
        };
        var seeds = new[]
        {
            new Vector2<int>(width / 3, height / 3),
            new(2 * width / 3, 2 * height / 3)
        }.Where(p => p != hub).Distinct().ToArray();
        return new(width, height, doors, hub, seeds);
    }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Doors);
        ArgumentNullException.ThrowIfNull(ObstacleSeeds);
        if (Width < 7 || Height < 7 || !Interior(Hub))
            throw new ArgumentException("Invalid room dimensions or hub position.");
        if (Doors.Length == 0 || Doors.Distinct().Count() != Doors.Length ||
            Doors.Any(p => !Inside(p) || Interior(p) ||
                ((p.x == 0 || p.x == Width - 1) && (p.y == 0 || p.y == Height - 1))))
            throw new ArgumentException("Doors must be distinct non-corner boundary cells.");
        if (ObstacleSeeds.Distinct().Count() != ObstacleSeeds.Length ||
            ObstacleSeeds.Any(p => !Interior(p) || p == Hub))
            throw new ArgumentException("Obstacle seeds must be distinct interior cells outside the hub.");
    }

    public bool Inside(Vector2<int> p) => (uint)p.x < (uint)Width && (uint)p.y < (uint)Height;
    public bool Interior(Vector2<int> p) => p.x > 0 && p.x < Width - 1 && p.y > 0 && p.y < Height - 1;

    public CellType[,] CreateInitialGrid()
    {
        Validate();
        var grid = new CellType[Width, Height];
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            if (!Interior(new(x, y))) grid[x, y] = CellType.Obstacle;
        foreach (var p in Doors) grid[p.x, p.y] = CellType.Road;
        grid[Hub.x, Hub.y] = CellType.Hub;
        foreach (var p in ObstacleSeeds) grid[p.x, p.y] = CellType.Obstacle;
        return grid;
    }

    public bool[,] CreateFixedCells()
    {
        Validate();
        var mask = new bool[Width, Height];
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            mask[x, y] = !Interior(new(x, y));
        mask[Hub.x, Hub.y] = true;
        return mask;
    }
}
