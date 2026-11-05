using System.Globalization;
using System.Text.Json;

namespace RoomGenerate;

public static class TrainingCli
{
    public static int Run(string[] args)
    {
        try
        {
            switch (args[0])
            {
                case "train": Train(args.Skip(1).ToArray()); break;
                case "play": Play(args.Skip(1).ToArray()); break;
                case "help" or "--help" or "-h": PrintHelp(); break;
                default: throw new ArgumentException($"Unknown command: {args[0]}. Use --help.");
            }
            return 0;
        }
        catch (Exception error) when (error is ArgumentException or IOException or JsonException or
            InvalidOperationException or FormatException or OverflowException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void Train(string[] args)
    {
        var flags = Parse(args, ["generations", "population", "steps", "simulation-seed", "search-seed",
            "elite", "tournament", "patience", "mutation-rate", "width", "height", "output", "evaluation", "scenario"]);
        var options = new TrainingOptions
        {
            Generations = Int(flags, "generations", 50), PopulationSize = Int(flags, "population", 32),
            Steps = Int(flags, "steps", 60), SimulationSeed = Int(flags, "simulation-seed", 42),
            SearchSeed = Int(flags, "search-seed", 1337), EliteCount = Int(flags, "elite", 2),
            TournamentSize = Int(flags, "tournament", 3), Patience = Int(flags, "patience", 30),
            MutationRate = flags.TryGetValue("mutation-rate", out var rate) ? double.Parse(rate, CultureInfo.InvariantCulture) : .2
        };
        var evaluation = flags.TryGetValue("evaluation", out var evaluationPath)
            ? ReadJson<EvaluationOptions>(evaluationPath) : new EvaluationOptions();
        if (flags.ContainsKey("scenario") && (flags.ContainsKey("width") || flags.ContainsKey("height")))
            throw new ArgumentException("Use either --scenario or --width/--height.");
        var scenario = flags.TryGetValue("scenario", out var scenarioPath)
            ? ReadJson<RoomScenario>(scenarioPath)
            : RoomScenario.Create(Int(flags, "width", 30), Int(flags, "height", 20));
        var output = flags.GetValueOrDefault("output", "best-room.json");
        var artifact = RuleTrainer.Train(scenario, options, evaluation, (generation, fitness, metrics) =>
            Console.WriteLine(FormattableString.Invariant(
                $"generation={generation} doors={fitness.ConnectedDoors}/{scenario.Doors.Length} progress={fitness.ConnectionProgress:F4} shape={fitness.ShapeScore:F4} clusters={metrics.ClusterCount}")), output);
        Console.WriteLine($"Saved {Path.GetFullPath(output)} (best found at generation {artifact.FoundAtGeneration}).");
        Console.WriteLine(CellAutomata.Render(artifact.Replay().Grid));
        Console.WriteLine(JsonSerializer.Serialize(artifact.Metrics, TrainingArtifact.JsonOptions));
    }

    private static void Play(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--")) throw new ArgumentException("Usage: play <artifact.json> [--animate] [--delay 40]");
        var flags = Parse(args.Skip(1).ToArray(), ["animate", "delay"], ["animate"]);
        int delay = Int(flags, "delay", 40);
        ArgumentOutOfRangeException.ThrowIfNegative(delay);
        var artifact = TrainingArtifact.Load(args[0]);
        Action<int, CellType[,]>? animate = flags.ContainsKey("animate") ? (step, grid) =>
        {
            if (!Console.IsOutputRedirected) Console.Clear();
            Console.WriteLine($"Step {step}/{artifact.Training.Steps}");
            Console.WriteLine(CellAutomata.Render(grid));
            Thread.Sleep(delay);
        } : null;
        var result = artifact.Replay(animate);
        Console.WriteLine(CellAutomata.Render(result.Grid));
        Console.WriteLine(JsonSerializer.Serialize(artifact.Metrics, TrainingArtifact.JsonOptions));
        Console.WriteLine("Replay verified: grid, metrics and fitness match the saved result.");
    }

    private static T ReadJson<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), TrainingArtifact.JsonOptions)
        ?? throw new ArgumentException($"Empty JSON file: {path}");

    private static int Int(Dictionary<string, string> flags, string name, int fallback) =>
        flags.TryGetValue(name, out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    private static Dictionary<string, string> Parse(string[] args, string[] allowed, string[]? switches = null)
    {
        var result = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--") || !allowed.Contains(args[i][2..]))
                throw new ArgumentException($"Unknown option: {args[i]}");
            string name = args[i][2..];
            bool isSwitch = switches?.Contains(name) == true;
            if (!isSwitch && (i + 1 == args.Length || args[i + 1].StartsWith("--")))
                throw new ArgumentException($"Missing value for --{name}");
            string value = isSwitch ? "true" : args[++i];
            if (!result.TryAdd(name, value)) throw new ArgumentException($"Duplicate option: --{name}");
        }
        return result;
    }

    private static void PrintHelp() => Console.WriteLine("""
        dotnet run -- train [--generations 50] [--population 32] [--steps 60]
            [--width 30] [--height 20] [--simulation-seed 42] [--search-seed 1337]
            [--elite 2] [--tournament 3] [--patience 30] [--mutation-rate 0.2]
            [--output best-room.json] [--evaluation evaluation.json] [--scenario scenario.json]
        dotnet run -- play best-room.json [--animate] [--delay 40]
        dotnet run -- <steps> <width> <height>   Conway's Game of Life demo (B3/S23)
        """);
}
