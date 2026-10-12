using System.Diagnostics;

// Deployment launcher: native engine owns simulation; this process owns configuration and lifetime.
try
{
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] is not ("--engine" or "--project" or "--map" or "--port" or "--max-players" or "--bots" or "--mode" or "--ticks"))
            throw new ArgumentException($"Unknown option: {args[i]}");
        var key = args[i];
        if (++i >= args.Length || !values.TryAdd(key, args[i])) throw new ArgumentException($"Missing or duplicate option: {key}");
    }
    string Get(string key, string fallback) => values.GetValueOrDefault(key, fallback);
    int Number(string key, int fallback, int min, int max)
    {
        if (!int.TryParse(Get(key, fallback.ToString()), out var value) || value < min || value > max)
            throw new ArgumentException($"{key} must be between {min} and {max}.");
        return value;
    }
    var engine = Path.GetFullPath(Get("--engine", Path.Combine(AppContext.BaseDirectory, "PlutoGEServer.exe")));
    var project = Path.GetFullPath(Get("--project", Path.Combine(AppContext.BaseDirectory, File.Exists(Path.Combine(AppContext.BaseDirectory, "CoDGame.plutopack")) ? "CoDGame.plutopack" : "CoDplutoproject.plutoproject")));
    if (!File.Exists(engine) || !File.Exists(project)) throw new ArgumentException("Specify existing --engine and --project paths.");
    var mode = Get("--mode", "Hardpoint");
    if (mode is not ("Hardpoint" or "TeamDeathmatch" or "Defusal")) throw new ArgumentException("Unknown match mode.");
    var port = Number("--port", 7777, 1, 65535);
    var players = Number("--max-players", 12, 1, 128);
    var bots = Number("--bots", 8, 0, players);
    var ticks = Number("--ticks", 0, 0, int.MaxValue);
    var map = Get("--map", mode == "Defusal" ? "Foundry" : "Main");
    if (map is not ("Foundry" or "Main")) throw new ArgumentException("Map must be Foundry or Main.");
    var start = new ProcessStartInfo(engine) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(engine)! };
    start.Environment["COD_SERVER_PORT"] = port.ToString();
    start.Environment["COD_SERVER_PLAYERS"] = players.ToString();
    start.Environment["COD_SERVER_BOTS"] = bots.ToString();
    start.Environment["COD_SERVER_MODE"] = mode;
    foreach (var value in new[] { "--project", project, "--scene", $"project://Scenes/{map}.plutoscene", "--tick-rate", "60", "--ticks", ticks.ToString() })
        start.ArgumentList.Add(value);
    foreach (var script in new[] { "MultiplayerSession", "EnemySoldierBot", "NetworkParticipantHitbox", "SupplySpawner", "SupplyPickup" })
    { start.ArgumentList.Add("--script"); start.ArgumentList.Add("CoD.Scripts." + script); }
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start headless engine.");
    // Ctrl+C is delivered to the child console process too; let its signal handler shut sockets down.
    Console.CancelKeyPress += (_, e) => e.Cancel = true;
    Console.WriteLine($"CoD server: {map}, {mode}, port {port}, population {bots}/{players}. Ctrl+C stops the server.");
    process.WaitForExit();
    return process.ExitCode;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    Console.Error.WriteLine("Usage: CoDServer --engine <PlutoGEServer.exe> --project <CoD.plutoproject> [--map Foundry] [--port 7777] [--max-players 12] [--bots 8] [--mode Hardpoint]");
    return 1;
}
