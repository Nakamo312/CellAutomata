namespace RoomGenerate;

public sealed record RoomMetrics(
    int ConnectedDoors,
    double DoorDistance,
    double ConnectionProgress,
    double DisconnectedRoadFraction,
    double RoadFraction,
    double ObstacleFraction,
    int ClusterCount,
    double SmallClusterFraction,
    double ClusterCompactness)
{
    private static readonly Vector2<int>[] Directions = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    public static RoomMetrics Measure(CellType[,] grid, RoomScenario scenario, int minimumClusterArea = 3)
    {
        scenario.Validate();
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumClusterArea, 1);
        if (grid.GetLength(0) != scenario.Width || grid.GetLength(1) != scenario.Height)
            throw new ArgumentException("Grid dimensions do not match the scenario.");
        bool IsRoad(Vector2<int> p) => scenario.Inside(p) &&
            grid[p.x, p.y] is CellType.Road or CellType.Hub;
        bool IsObstacle(Vector2<int> p) => scenario.Interior(p) && grid[p.x, p.y] == CellType.Obstacle;

        var reached = new bool[scenario.Width, scenario.Height];
        var hubComponent = new List<Vector2<int>>();
        var queue = new Queue<Vector2<int>>();
        if (IsRoad(scenario.Hub))
        {
            queue.Enqueue(scenario.Hub);
            reached[scenario.Hub.x, scenario.Hub.y] = true;
        }
        while (queue.TryDequeue(out var p))
        {
            hubComponent.Add(p);
            foreach (var d in Directions)
            {
                var next = new Vector2<int>(p.x + d.x, p.y + d.y);
                if (IsRoad(next) && !reached[next.x, next.y])
                {
                    reached[next.x, next.y] = true;
                    queue.Enqueue(next);
                }
            }
        }

        int connected = 0;
        double distance = 0, progress = 0;
        foreach (var door in scenario.Doors)
        {
            if (reached[door.x, door.y]) connected++;
            int initialDistance = Manhattan(door, scenario.Hub);
            int remaining = hubComponent.Count == 0 ? initialDistance : hubComponent.Min(p => Manhattan(door, p));
            distance += remaining;
            progress += Math.Clamp(1.0 - (double)remaining / initialDistance, 0, 1);
        }

        int totalRoad = 0, disconnectedRoad = 0, interiorRoad = 0, obstacleCount = 0;
        for (int x = 0; x < scenario.Width; x++)
        for (int y = 0; y < scenario.Height; y++)
        {
            if (grid[x, y] == CellType.Road)
            {
                totalRoad++;
                if (!reached[x, y]) disconnectedRoad++;
                if (scenario.Interior(new(x, y))) interiorRoad++;
            }
            if (IsObstacle(new(x, y))) obstacleCount++;
        }

        var visited = new bool[scenario.Width, scenario.Height];
        int clusters = 0, smallCells = 0;
        double weightedCompactness = 0;
        for (int x = 1; x < scenario.Width - 1; x++)
        for (int y = 1; y < scenario.Height - 1; y++)
        {
            if (!IsObstacle(new(x, y)) || visited[x, y]) continue;
            clusters++;
            int area = 0, perimeter = 0;
            visited[x, y] = true;
            queue.Enqueue(new(x, y));
            while (queue.TryDequeue(out var p))
            {
                area++;
                foreach (var d in Directions)
                {
                    var next = new Vector2<int>(p.x + d.x, p.y + d.y);
                    if (!IsObstacle(next)) perimeter++;
                    else if (!visited[next.x, next.y])
                    {
                        visited[next.x, next.y] = true;
                        queue.Enqueue(next);
                    }
                }
            }
            if (area < minimumClusterArea) smallCells += area;
            weightedCompactness += area * (16.0 * area / ((double)perimeter * perimeter));
        }

        double interiorArea = (double)(scenario.Width - 2) * (scenario.Height - 2);
        return new(connected, distance / scenario.Doors.Length, progress / scenario.Doors.Length,
            totalRoad == 0 ? 0 : (double)disconnectedRoad / totalRoad,
            interiorRoad / interiorArea, obstacleCount / interiorArea, clusters,
            obstacleCount == 0 ? 0 : (double)smallCells / obstacleCount,
            obstacleCount == 0 ? 0 : weightedCompactness / obstacleCount);
    }

    private static int Manhattan(Vector2<int> a, Vector2<int> b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
}
