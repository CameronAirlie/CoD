namespace CoD.Scripts;

/// <summary>Process-local configuration supplied by the dedicated launcher, never by peers.</summary>
internal static class DedicatedServerSettings
{
    public static bool Enabled => Environment.GetEnvironmentVariable("PLUTOGE_HEADLESS") == "1";
    public static string Mode => NormalizeMode(Environment.GetEnvironmentVariable("PLUTOGE_SERVER_MODE") ?? Environment.GetEnvironmentVariable("COD_SERVER_MODE") ?? "Hardpoint");
    public static string NormalizeMode(string value) => value.ToLowerInvariant() switch
    {
        "defusal" => "Defusal", "hardpoint" => "Hardpoint", "tdm" or "teamdeathmatch" => "TeamDeathmatch",
        _ => throw new ArgumentException("Modes: Hardpoint, Defusal, TeamDeathmatch (tdm).")
    };
    public static string MapFor(string mode, string? map = null)
    {
        var maps = MapCatalog.For(mode);
        if (string.IsNullOrWhiteSpace(map)) return maps[0].Scene;
        return MapCatalog.Find(MapCatalog.ParseMode(mode), map.Trim())?.Scene
            ?? throw new ArgumentException($"{mode} supports maps: {MapCatalog.Describe(MapCatalog.ParseMode(mode))}.");
    }
    public static string Map => MapFor(Mode, Environment.GetEnvironmentVariable("PLUTOGE_SERVER_MAP") ?? Environment.GetEnvironmentVariable("COD_SERVER_MAP"));
    public static bool NeedsMap => Enabled && !PlutoGE.ScriptCore.SceneManager.GetActiveScene().Name.Equals(Map, StringComparison.OrdinalIgnoreCase);
    public static void Apply(MultiplayerSession session)
    {
        if (!Enabled) return;
        session.ConfigureDedicated(
            Read("COD_SERVER_PORT", 7777, 1, 65535),
            Read("COD_SERVER_PLAYERS", 12, 1, 128),
            Read("COD_SERVER_BOTS", 8, 0, 128),
            Mode);
    }
    private static int Read(string key, int fallback, int min, int max)
    {
        var text = Environment.GetEnvironmentVariable(key);
        if (text is null) return fallback;
        if (!int.TryParse(text, out var value) || value < min || value > max)
            throw new ArgumentException($"{key} must be between {min} and {max}.");
        return value;
    }
}

/// <summary>Local administration only; commands are never accepted from network messages.</summary>
public sealed partial class MultiplayerSession : PlutoGE.ScriptCore.IServerCommandHandler
{
    public bool HandleServerCommand(string command)
    {
        if (!IsDedicated) return false;
        var args = command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0) return false;
        var verb = args[0].ToLowerInvariant();
        if (verb == "help")
        {
            AdminLog("mode <Hardpoint|Defusal|tdm> [map] | map <name> | maps (switching disconnects players)");
            AdminLog("players | kick <peer-id> | bots [add [n]|remove [n|all]|set <n>] | get [setting] | set <setting> <value> | round <start|restart|end>");
            AdminLog("Settings: bots, scorelimit (1..10000), matchduration (10..86400 seconds), warmup (1..300 seconds), respawndelay (0..60 seconds), botaccuracy (0..90 degrees).");
            AdminLog("Changes last until server restart. bots is a population-fill target; humans replace bots. Port and player capacity are startup settings.");
            return true;
        }
        if (verb is "mode" or "map" or "maps")
        {
            try
            {
                if (verb == "maps") { AdminLog($"Defusal: {MapCatalog.Describe(MatchMode.Defusal)}; Hardpoint / TeamDeathmatch: {MapCatalog.Describe(MatchMode.Hardpoint)}."); return true; }
                Require(verb == "mode" ? args.Length is 2 or 3 : args.Length == 2, "Usage: mode <Hardpoint|Defusal|tdm> [map] | map <name>");
                var nextMode = verb == "mode" ? DedicatedServerSettings.NormalizeMode(args[1]) : DedicatedServerSettings.Mode;
                var nextMap = DedicatedServerSettings.MapFor(nextMode, verb == "map" ? args[1] : args.Length == 3 ? args[2] : null);
                Require(PlutoGE.ScriptCore.SceneManager.LoadScene(nextMap), "Scene transition is already pending; try again shortly.");
                Environment.SetEnvironmentVariable("PLUTOGE_SERVER_MODE", nextMode);
                Environment.SetEnvironmentVariable("PLUTOGE_SERVER_MAP", nextMap);
                AdminLog($"Switching to {nextMode} / {nextMap}. Players must reconnect.");
            }
            catch (ArgumentException error) { AdminLog(error.Message); }
            return true;
        }
        if (verb is not ("players" or "kick" or "bots" or "get" or "set" or "round")) return false;
        if (_server is null) { AdminLog("Server is not ready."); return true; }
        try
        {
            switch (verb)
            {
                case "players":
                    Require(args.Length == 1, "Usage: players");
                    AdminLog($"{_playerStates.Count} participants ({_bots.Count} bots), capacity {maximumParticipants}; {RulesMode}, {_matchPhase}; score {_alphaScore}-{_bravoScore}.");
                    foreach (var pair in _playerStates.OrderBy(p => p.Key))
                        AdminLog($"{pair.Key,6}  {(pair.Value.IsBot ? "BOT" : pair.Value.Team == PlayerTeam.Spectator ? "OBS" : "HUMAN"),5}  {_peerNames.GetValueOrDefault(pair.Key, "Player")}  {pair.Value.Team}  HP {pair.Value.Health:0}  K/D {pair.Value.Kills}/{pair.Value.Deaths}  Pos {GetParticipantObject(pair.Key)?.WorldPosition}");
                    break;
                case "kick":
                    Require(args.Length == 2 && int.TryParse(args[1], out _), "Usage: kick <peer-id> (use players to find IDs)");
                    var id = int.Parse(args[1]);
                    Require(_playerStates.ContainsKey(id), "No participant with that ID.");
                    if (_bots.ContainsKey(id))
                    {
                        SetAdminBotTarget(Math.Max(0, _bots.Count - 1));
                        RemoveOneBot(id);
                        BroadcastMatchState();
                        AdminLog($"Removed bot {id} and lowered the fill target.");
                    }
                    else
                    {
                        Require(_server.Disconnect(id), "Player is already disconnected.");
                        OnServerPeerDisconnected(id);
                        AdminLog($"Kicked player {id}.");
                    }
                    break;
                case "bots":
                    if (args.Length == 1) { ShowAdminSetting("bots"); break; }
                    Require(args.Length is 2 or 3, "Usage: bots add [n] | remove [n|all] | set <n>");
                    var action = args[1].ToLowerInvariant();
                    Require(action is "add" or "remove" or "set", "Expected bots add, remove or set.");
                    Require(action != "set" || args.Length == 3, "Usage: bots set <n>");
                    var target = Math.Clamp(minimumParticipants - (CombatHumanCount), 0, maximumBots);
                    var amount = args.Length == 2 ? 1 : args[2] == "all" && action == "remove" ? target : AdminInteger(args[2], 0, 128);
                    SetAdminBotTarget(action == "set" ? amount : action == "add" ? target + amount : Math.Max(0, target - amount));
                    break;
                case "get":
                    Require(args.Length <= 2, "Usage: get [setting]");
                    if (args.Length == 2) ShowAdminSetting(args[1]);
                    else foreach (var name in new[] { "bots", "maxplayers", "port", "scorelimit", "matchduration", "warmup", "respawndelay", "botaccuracy" }) ShowAdminSetting(name);
                    break;
                case "set":
                    Require(args.Length == 3, "Usage: set <setting> <value>");
                    var key = args[1].ToLowerInvariant();
                    if (key == "bots") { SetAdminBotTarget(AdminInteger(args[2], 0, 128)); break; }
                    if (key == "scorelimit") scoreLimit = AdminInteger(args[2], 1, 10000);
                    else if (key == "matchduration") matchDuration = AdminNumber(args[2], 10, 86400);
                    else if (key == "warmup") warmupDuration = AdminNumber(args[2], 1, 300);
                    else if (key == "respawndelay") combatRespawnDelay = AdminNumber(args[2], 0, 60);
                    else if (key == "botaccuracy") botAccuracyDegrees = AdminNumber(args[2], 0, 90);
                    else throw new ArgumentException("Unknown or read-only setting. Type help for writable settings.");
                    ShowAdminSetting(key);
                    if (key is "matchduration" or "warmup") AdminLog("Duration applies when the next phase starts.");
                    break;
                case "round":
                    Require(args.Length == 2 && args[1] is "start" or "restart" or "end", "Usage: round start|restart|end");
                    if (args[1] == "end")
                    {
                        Require(_matchPhase == MatchPhase.Playing, "There is no active round to end.");
                        BeginPhase(MatchPhase.Results, resultsDuration);
                    }
                    else
                    {
                        Require(args[1] == "restart" || _matchPhase != MatchPhase.Playing, "Round is already playing; use round restart.");
                        if (IsDefusal)
                        {
                            if (args[1] == "restart") _defusalRound = Math.Max(0, _defusalRound - 1);
                            StartDefusalRound();
                        }
                        else BeginPlaying();
                    }
                    AdminLog($"Round phase: {_matchPhase}.");
                    break;
            }
        }
        catch (ArgumentException error) { AdminLog(error.Message); }
        return true;
    }

    private void SetAdminBotTarget(int bots)
    {
        var humans = CombatHumanCount;
        Require(bots >= 0 && bots <= Math.Max(0, maximumParticipants - humans), $"Bot target must be 0..{Math.Max(0, maximumParticipants - humans)} with the current human roster.");
        minimumParticipants = humans + bots;
        maximumBots = bots;
        fillWithBots = bots > 0;
        _nextBotFillAt = 0;
        AdminLog($"Bot target: {bots}; population target: {minimumParticipants}. Fill adjusts gradually as spawn points become available." +
            (IsDefusal && _matchPhase == MatchPhase.Playing ? " Defusal roster changes apply between rounds." : ""));
    }

    private void ShowAdminSetting(string key)
    {
        var value = key.ToLowerInvariant() switch
        {
            "bots" => $"{_bots.Count} active; population target {minimumParticipants}; bot cap {maximumBots}; fill {fillWithBots}",
            "maxplayers" => $"{maximumParticipants} (startup only: COD_SERVER_PLAYERS)",
            "port" => $"{serverPort} (startup only: COD_SERVER_PORT)",
            "scorelimit" => $"{scoreLimit}", "matchduration" => $"{matchDuration}",
            "warmup" => $"{warmupDuration}", "respawndelay" => $"{combatRespawnDelay}",
            "botaccuracy" => $"{botAccuracyDegrees}",
            _ => throw new ArgumentException("Unknown setting. Type get to list settings.")
        };
        AdminLog($"{key} = {value}");
    }
    private static int AdminInteger(string text, int min, int max)
    {
        Require(int.TryParse(text, out var value) && value >= min && value <= max, $"Expected an integer from {min} to {max}.");
        return value;
    }
    private static float AdminNumber(string text, float min, float max)
    {
        Require(float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) &&
            float.IsFinite(value) && value >= min && value <= max, $"Expected a number from {min} to {max}.");
        return value;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
    private static void AdminLog(string text) => PlutoGE.ScriptCore.Debug.Log("[Admin] " + text);
}
