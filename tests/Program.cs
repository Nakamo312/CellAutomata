using System.Text.Json;
using RoomGenerate;

var checks = new (string Name, Action Check)[]
{
    ("known connectivity, density, clusters and lattice perimeter", KnownMetrics),
    ("diagonal contact is not four-way connectivity", DiagonalContact),
    ("distance supplies progress before a door connects", ConnectionProgress),
    ("fixed walls, door seeds and hub survive destructive rules", FixedCells),
    ("seeded stochastic simulation and reset reproduce every frame", Reproducibility),
    ("unchanged pictures can precede lifetime-triggered growth", DelayedGrowth),
    ("gene priority controls competing transitions", RulePriority),
    ("metric bands and fitness priorities", Scoring),
    ("training, JSON round trip and verified replay are deterministic", TrainingReplay),
    ("invalid configurations are rejected", InvalidConfiguration)
};
int failures = 0;
foreach (var (name, check) in checks)
{
    try { check(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
return failures == 0 ? 0 : 1;

static RoomScenario Scenario() => new(9, 9, [new(0, 4), new(8, 4)], new(4, 4), []);
static string Picture(CellType[,] grid) => string.Join("\n", SimulationRunner.ToRows(grid));
static void Assert(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new Exception(message);
}
static void Near(double actual, double expected) => Assert(Math.Abs(actual - expected) < 1e-12, $"Expected {expected}, got {actual}");
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

static void KnownMetrics()
{
    var scenario = Scenario();
    var grid = scenario.CreateInitialGrid();
    for (int x = 1; x < 8; x++) if (x != 4) grid[x, 4] = CellType.Road;
    for (int x = 1; x <= 2; x++)
    for (int y = 1; y <= 2; y++) grid[x, y] = CellType.Obstacle;
    grid[7, 7] = CellType.Obstacle;
    var metrics = RoomMetrics.Measure(grid, scenario);
    Assert(metrics.ConnectedDoors == 2);
    Near(metrics.ConnectionProgress, 1);
    Near(metrics.DoorDistance, 0);
    Near(metrics.DisconnectedRoadFraction, 0);
    Near(metrics.RoadFraction, 6.0 / 49);
    Near(metrics.ObstacleFraction, 5.0 / 49);
    Assert(metrics.ClusterCount == 2, "Boundary walls must not count as a cluster");
    Near(metrics.SmallClusterFraction, .2);
    Near(metrics.ClusterCompactness, 1);
}

static void DiagonalContact()
{
    var scenario = Scenario();
    var grid = scenario.CreateInitialGrid();
    grid[1, 1] = grid[2, 2] = CellType.Obstacle;
    grid[3, 3] = CellType.Road;
    var metrics = RoomMetrics.Measure(grid, scenario);
    Assert(metrics.ClusterCount == 2);
    Near(metrics.DisconnectedRoadFraction, 1);
    Near(metrics.ConnectionProgress, 0);
}

static void ConnectionProgress()
{
    var scenario = Scenario();
    var initial = RoomMetrics.Measure(scenario.CreateInitialGrid(), scenario);
    var grid = scenario.CreateInitialGrid();
    grid[3, 4] = CellType.Road;
    var grown = RoomMetrics.Measure(grid, scenario);
    Assert(grown.ConnectedDoors == 0);
    Assert(grown.DoorDistance < initial.DoorDistance);
    Assert(grown.ConnectionProgress > initial.ConnectionProgress);
}

static void FixedCells()
{
    var scenario = Scenario();
    var rules = new Dictionary<CellType, CellRule>();
    foreach (var type in Enum.GetValues<CellType>())
    {
        var rule = new CellRule();
        rule.Transitions.Add(new(CellType.Empty, 0, 8, CellType.Empty));
        rules[type] = rule;
    }
    var final = SimulationRunner.Run(scenario, rules, 3, 42).Grid;
    var initial = scenario.CreateInitialGrid();
    var mask = scenario.CreateFixedCells();
    for (int x = 0; x < 9; x++)
    for (int y = 0; y < 9; y++)
        if (mask[x, y]) Assert(initial[x, y] == final[x, y]);
}

static void Reproducibility()
{
    var scenario = Scenario();
    var automata = new CellAutomata(20, new(9, 9), scenario.CreateInitialGrid(),
        RuleTrainer.Compile(RuleTrainer.InitialRules()), 123, scenario.CreateFixedCells());
    string initial = Picture(automata.Current);
    var frames = new List<string>();
    while (automata.MoveNext()) frames.Add(Picture(automata.Current));
    automata.Reset();
    Assert(Picture(automata.Current) == initial);
    int i = 0;
    while (automata.MoveNext()) Assert(Picture(automata.Current) == frames[i++]);
    Assert(i == 20);
    var second = SimulationRunner.Run(scenario, RuleTrainer.Compile(RuleTrainer.InitialRules()), 20, 123);
    Assert(Picture(second.Grid) == frames[^1]);
}

static void DelayedGrowth()
{
    var scenario = Scenario();
    var rules = RuleTrainer.Compile([new(CellType.Empty, CellType.Hub, CellType.Road, 1, 3, 1)]);
    string initial = Picture(scenario.CreateInitialGrid());
    var frames = new List<string>();
    SimulationRunner.Run(scenario, rules, 4, 42, (_, grid) => frames.Add(Picture(grid)));
    Assert(frames.Take(3).All(frame => frame == initial));
    Assert(frames[3] != initial, "Stopping after an unchanged picture would truncate growth");
}

static void RulePriority()
{
    var scenario = Scenario();
    RuleGene road = new(CellType.Empty, CellType.Road, CellType.Road, 1, 0, 1, 10);
    RuleGene obstacle = new(CellType.Empty, CellType.Obstacle, CellType.Obstacle, 1, 0, 1, 0);
    var first = SimulationRunner.Run(scenario, RuleTrainer.Compile([road, obstacle]), 1, 42);
    var second = SimulationRunner.Run(scenario, RuleTrainer.Compile([road with { Priority = -1 }, obstacle]), 1, 42);
    Assert(first.Grid[1, 4] == CellType.Obstacle);
    Assert(second.Grid[1, 4] == CellType.Road);
}

static void Scoring()
{
    var band = new MetricBand(.2, .4, .2);
    Near(band.Score(.3), 1);
    Near(band.Score(.5), .5);
    Near(band.Score(.8), 0);
    var defaults = new EvaluationOptions();
    Assert(defaults.RoadFraction.Score(.5) > defaults.RoadFraction.Score(.7),
        "Default scoring must distinguish moderately and heavily overgrown roads");
    Assert(new Fitness(1, 0, 0).CompareTo(new(0, 1, 1)) > 0);
    Assert(new Fitness(1, .6, 0).CompareTo(new(1, .5, 1)) > 0);
    var scenario = Scenario();
    var score = RoomEvaluator.Evaluate(RoomMetrics.Measure(scenario.CreateInitialGrid(), scenario), new());
    Assert(double.IsFinite(score.ShapeScore) && score.ShapeScore is >= 0 and <= 1);
}

static void TrainingReplay()
{
    var scenario = Scenario();
    var options = new TrainingOptions { Generations = 4, PopulationSize = 8, Steps = 20 };
    var bestScores = new List<Fitness>();
    var first = RuleTrainer.Train(scenario, options, new(), (_, fitness, _) => bestScores.Add(fitness));
    var second = RuleTrainer.Train(scenario, options, new());
    Assert(JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second), "Training must repeat with the same seeds");
    Assert(bestScores.Zip(bestScores.Skip(1)).All(pair => pair.Second.CompareTo(pair.First) >= 0));
    string path = Path.Combine(Path.GetTempPath(), $"room-training-check-{Guid.NewGuid():N}.json");
    try
    {
        first.Save(path);
        var loaded = TrainingArtifact.Load(path);
        Assert(Picture(loaded.Replay().Grid) == string.Join("\n", first.FinalGrid));
        loaded.FinalGrid[0] = "corrupt";
        Throws<InvalidOperationException>(() => loaded.Replay());
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void InvalidConfiguration()
{
    Throws<ArgumentException>(() => RoomScenario.Create(3, 3));
    Throws<ArgumentException>(() => (Scenario() with { Doors = [new(0, 0)] }).Validate());
    Throws<ArgumentException>(() => new TrainingOptions { Steps = 0 }.Validate());
    Throws<ArgumentException>(() => new TrainingOptions { PopulationSize = 2 }.Validate());
    Throws<ArgumentException>(() => new MetricBand(1, 0, .1).Validate());
    Throws<ArgumentException>(() => RuleTrainer.Compile([new(CellType.Obstacle, CellType.Road, CellType.Road, 1, 0, 1)]));
    Throws<ArgumentException>(() => RuleTrainer.Compile([RuleTrainer.InitialRules()[0] with { Count = 10 }]));
}
