using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoomGenerate;

public sealed record RuleGene(CellType Source, CellType Neighbor, CellType Target,
    int Count, int Lifetime, float Chance, int Priority = 0);

public sealed record TrainingOptions
{
    public int Generations { get; init; } = 50;
    public int PopulationSize { get; init; } = 32;
    public int Steps { get; init; } = 60;
    public int SimulationSeed { get; init; } = 42;
    public int SearchSeed { get; init; } = 1337;
    public int EliteCount { get; init; } = 2;
    public int TournamentSize { get; init; } = 3;
    public int Patience { get; init; } = 30;
    public int MaxLifetime { get; init; } = 8;
    public double MutationRate { get; init; } = .2;

    public void Validate()
    {
        if (Generations < 1 || PopulationSize < 2 || Steps < 1 || EliteCount < 1 ||
            EliteCount >= PopulationSize || TournamentSize < 1 || Patience < 1 || MaxLifetime < 4 ||
            !double.IsFinite(MutationRate) || MutationRate <= 0 || MutationRate > 1)
            throw new ArgumentException("Invalid training options (population > elite >= 1; steps, generations, patience > 0; maxLifetime >= 4; mutationRate in (0,1]).");
    }
}

public sealed record TrainingArtifact(
    int SchemaVersion,
    RoomScenario Scenario,
    TrainingOptions Training,
    EvaluationOptions Evaluation,
    RuleGene[] Rules,
    int FoundAtGeneration,
    RoomMetrics Metrics,
    Fitness Fitness,
    string[] FinalGrid)
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public void Save(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static TrainingArtifact Load(string path)
    {
        var artifact = JsonSerializer.Deserialize<TrainingArtifact>(File.ReadAllText(path), JsonOptions)
            ?? throw new ArgumentException("Empty training artifact.");
        if (artifact.SchemaVersion != 1) throw new ArgumentException("Unsupported artifact version.");
        ArgumentNullException.ThrowIfNull(artifact.Scenario);
        ArgumentNullException.ThrowIfNull(artifact.Training);
        ArgumentNullException.ThrowIfNull(artifact.Evaluation);
        ArgumentNullException.ThrowIfNull(artifact.Metrics);
        ArgumentNullException.ThrowIfNull(artifact.FinalGrid);
        artifact.Scenario.Validate();
        artifact.Training.Validate();
        artifact.Evaluation.Validate();
        RuleTrainer.Compile(artifact.Rules);
        return artifact;
    }

    public SimulationResult Replay(Action<int, CellType[,]>? onStep = null)
    {
        var result = SimulationRunner.Run(Scenario, RuleTrainer.Compile(Rules), Training.Steps,
            Training.SimulationSeed, onStep);
        var metrics = RoomMetrics.Measure(result.Grid, Scenario, Evaluation.MinimumClusterArea);
        if (!SimulationRunner.ToRows(result.Grid).SequenceEqual(FinalGrid) ||
            metrics != Metrics || RoomEvaluator.Evaluate(metrics, Evaluation) != Fitness)
            throw new InvalidOperationException("Replay differs from the saved grid or evaluation.");
        return result;
    }
}

public static class RuleTrainer
{
    // Only these transitions are searched. Road and obstacle never directly overwrite each other.
    public static RuleGene[] InitialRules() =>
    [
        new(CellType.Empty, CellType.Hub, CellType.Road, 1, 0, 1f, 0),
        new(CellType.Empty, CellType.Road, CellType.Road, 1, 1, .65f, 10),
        new(CellType.Empty, CellType.Obstacle, CellType.Obstacle, 3, 2, .6f, 20),
        new(CellType.Road, CellType.Road, CellType.Empty, 7, 4, .5f),
        new(CellType.Obstacle, CellType.Empty, CellType.Empty, 5, 4, .2f)
    ];

    public static Dictionary<CellType, CellRule> Compile(RuleGene[] genes)
    {
        ArgumentNullException.ThrowIfNull(genes);
        if (genes.Length == 0) throw new ArgumentException("Rules cannot be empty.");
        if (genes.Any(g => g is null)) throw new ArgumentException("Rule genes cannot be null.");
        var rules = new Dictionary<CellType, CellRule>();
        foreach (var gene in genes.OrderBy(g => g.Priority))
        {
            bool allowed = gene.Source == CellType.Empty && gene.Target is CellType.Road or CellType.Obstacle ||
                gene.Source is CellType.Road or CellType.Obstacle && gene.Target == CellType.Empty;
            if (!allowed || !Enum.IsDefined(gene.Neighbor) || gene.Count < 1 || gene.Count > 9 ||
                gene.Lifetime < 0 || !float.IsFinite(gene.Chance) || gene.Chance < 0 || gene.Chance > 1)
                throw new ArgumentException("Invalid rule gene.");
            if (!rules.TryGetValue(gene.Source, out var rule)) rules[gene.Source] = rule = new();
            rule.Transitions.Add(new CellTransition(gene.Neighbor, gene.Count, int.MaxValue,
                gene.Target, gene.Lifetime, gene.Chance));
        }
        return rules;
    }

    private sealed record Candidate(RuleGene[] Genes, RoomMetrics Metrics, Fitness Fitness, CellType[,] Grid);

    public static TrainingArtifact Train(RoomScenario scenario, TrainingOptions options,
        EvaluationOptions evaluation, Action<int, Fitness, RoomMetrics>? onGeneration = null,
        string? outputPath = null)
    {
        scenario.Validate();
        options.Validate();
        evaluation.Validate();
        var random = new Random(options.SearchSeed);
        var initial = InitialRules();
        var population = new RuleGene[options.PopulationSize][];
        population[0] = initial;
        for (int i = 1; i < population.Length; i++) population[i] = Mutate(initial, random, options, 1);
        Candidate? best = null;
        TrainingArtifact? artifact = null;
        int stale = 0;

        for (int generation = 1; generation <= options.Generations; generation++)
        {
            var candidates = population.Select(genes =>
            {
                var result = SimulationRunner.Run(scenario, Compile(genes), options.Steps, options.SimulationSeed);
                var metrics = RoomMetrics.Measure(result.Grid, scenario, evaluation.MinimumClusterArea);
                return new Candidate(genes, metrics, RoomEvaluator.Evaluate(metrics, evaluation), result.Grid);
            }).OrderByDescending(c => c.Fitness).ToArray();

            if (best is null || candidates[0].Fitness.CompareTo(best.Fitness) > 0)
            {
                best = candidates[0];
                stale = 0;
                artifact = new(1, scenario, options, evaluation, best.Genes, generation, best.Metrics,
                    best.Fitness, SimulationRunner.ToRows(best.Grid));
                if (outputPath is not null) artifact.Save(outputPath);
            }
            else stale++;
            onGeneration?.Invoke(generation, best!.Fitness, best.Metrics);
            if (stale >= options.Patience || generation == options.Generations) break;

            for (int i = 0; i < options.EliteCount; i++) population[i] = candidates[i].Genes;
            for (int i = options.EliteCount; i < population.Length; i++)
            {
                var first = Select(candidates, random, options.TournamentSize);
                var second = Select(candidates, random, options.TournamentSize);
                int split = random.Next(1, initial.Length);
                var child = first.Genes.Take(split).Concat(second.Genes.Skip(split)).ToArray();
                population[i] = Mutate(child, random, options, options.MutationRate);
            }
        }
        return artifact!;
    }

    private static Candidate Select(Candidate[] candidates, Random random, int tournamentSize)
    {
        var winner = candidates[random.Next(candidates.Length)];
        for (int i = 1; i < tournamentSize; i++)
        {
            var contender = candidates[random.Next(candidates.Length)];
            if (contender.Fitness.CompareTo(winner.Fitness) > 0) winner = contender;
        }
        return winner;
    }

    private static RuleGene[] Mutate(RuleGene[] parent, Random random, TrainingOptions options, double rate)
    {
        return parent.Select(g => g with
        {
            Count = random.NextDouble() < rate ? Math.Clamp(g.Count + random.Next(-2, 3), 1, 9) : g.Count,
            Lifetime = random.NextDouble() < rate ? Math.Clamp(g.Lifetime + random.Next(-2, 3), 0, options.MaxLifetime) : g.Lifetime,
            Chance = random.NextDouble() < rate ? Math.Clamp(g.Chance + (float)(random.NextDouble() * .5 - .25), 0, 1) : g.Chance,
            Priority = random.NextDouble() < rate ? Math.Clamp(g.Priority + random.Next(-15, 16), 0, 100) : g.Priority
        }).ToArray();
    }
}
