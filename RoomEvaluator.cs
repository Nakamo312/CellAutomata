namespace RoomGenerate;

public sealed record MetricBand(double Minimum, double Maximum, double Falloff)
{
    public void Validate()
    {
        if (!double.IsFinite(Minimum) || !double.IsFinite(Maximum) || !double.IsFinite(Falloff) ||
            Minimum > Maximum || Falloff <= 0)
            throw new ArgumentException("A metric band requires finite bounds, minimum <= maximum and falloff > 0.");
    }

    public double Score(double value)
    {
        double distance = Math.Max(Minimum - value, Math.Max(value - Maximum, 0));
        return Math.Clamp(1.0 - distance / Falloff, 0, 1);
    }
}

public sealed record EvaluationOptions
{
    // Starting hypotheses about the desired composition, saved with each experiment.
    public MetricBand RoadFraction { get; init; } = new(.08, .22, .8);
    public MetricBand ObstacleFraction { get; init; } = new(.15, .35, .35);
    public MetricBand ClusterCount { get; init; } = new(2, 6, 6);
    public MetricBand ClusterCompactness { get; init; } = new(.25, 1, .25);
    public int MinimumClusterArea { get; init; } = 3;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(RoadFraction);
        ArgumentNullException.ThrowIfNull(ObstacleFraction);
        ArgumentNullException.ThrowIfNull(ClusterCount);
        ArgumentNullException.ThrowIfNull(ClusterCompactness);
        RoadFraction.Validate();
        ObstacleFraction.Validate();
        ClusterCount.Validate();
        ClusterCompactness.Validate();
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumClusterArea, 1);
    }
}

// Lexicographic ordering: composition cannot compensate for disconnected doors.
public readonly record struct Fitness(int ConnectedDoors, double ConnectionProgress, double ShapeScore)
    : IComparable<Fitness>
{
    public int CompareTo(Fitness other)
    {
        int result = ConnectedDoors.CompareTo(other.ConnectedDoors);
        if (result == 0) result = ConnectionProgress.CompareTo(other.ConnectionProgress);
        return result == 0 ? ShapeScore.CompareTo(other.ShapeScore) : result;
    }
}

public static class RoomEvaluator
{
    public static Fitness Evaluate(RoomMetrics metrics, EvaluationOptions options)
    {
        options.Validate();
        double shape = (options.RoadFraction.Score(metrics.RoadFraction) +
            options.ObstacleFraction.Score(metrics.ObstacleFraction) +
            options.ClusterCount.Score(metrics.ClusterCount) +
            options.ClusterCompactness.Score(metrics.ClusterCompactness) +
            1 - metrics.SmallClusterFraction + 1 - metrics.DisconnectedRoadFraction) / 6;
        return new(metrics.ConnectedDoors, metrics.ConnectionProgress, shape);
    }
}
