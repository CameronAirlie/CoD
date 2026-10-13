using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using CoD.Scripts;
using PlutoGE.ScriptCore.Networking;

internal static class BotIntegration
{
    public static async Task Run(string[] args)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var scene = Path.Combine(AppContext.BaseDirectory, "bot-arena.plutoscene");
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "arena.plutoscene"));
        source = source.Replace("PROPERTY\twarmupDuration\t0\t0.2", "PROPERTY\twarmupDuration\t0\t1");
        source = source.Replace("PROPERTY\tmode\t2\tHost\t0", "PROPERTY\tmode\t2\tHost\t0\nPROPERTY\tbotDamage\t0\t0\t0\nPROPERTY\tbotFallbackSpawnGroundClearance\t0\t0.02\t0");
        source = source.Replace("-5,1,0", "-20,1,0").Replace("5,1,0", "20,1,0").Replace("5,1,-10", "20,1,-20").Replace("-5,1,-10", "-20,1,-20");
        File.WriteAllText(scene, source);
        var start = new ProcessStartInfo(Path.GetFullPath(args[0])) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["COD_SERVER_PORT"] = port.ToString(); start.Environment["COD_SERVER_BOTS"] = "2"; start.Environment["COD_SERVER_MODE"] = "TeamDeathmatch";
        foreach (var value in new[] { "--project", Path.GetFullPath(args[1]), "--scene", scene, "--ticks", "2400" }) start.ArgumentList.Add(value);
        foreach (var script in new[] { "MultiplayerSession", "EnemySoldierBot", "NetworkParticipantHitbox" }) { start.ArgumentList.Add("--script"); start.ArgumentList.Add("CoD.Scripts." + script); }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var client = new NetworkClient();
        JsonElement owner = default, match = default;
        var transforms = new Dictionary<int, JsonElement>();
        var peer = -1; var hitCount = 0; var deaths = new HashSet<int>();
        client.Connected += () => client.SendJson(1, new { ProtocolVersion = 19, Username = "BotDamageTest", Mode = 0 });
        client.MessageReceived += message =>
        {
            var json = message.GetJson<JsonElement>();
            if (message.Channel == 1) peer = json.GetProperty("PeerId").GetInt32();
            if (message.Channel == 22) owner = json;
            if (message.Channel == 7) match = json;
            if (message.Channel == 2) transforms[json.GetProperty("PeerId").GetInt32()] = json;
            if (message.Channel == 12) hitCount++;
            if (message.Channel == 13) deaths.Add(json.GetProperty("PeerId").GetInt32());
        };
        var sequence = 0L; var intent = PlayerCommand.Idle;
        async Task Pump(double seconds, Func<bool>? until = null)
        {
            var clock = Stopwatch.StartNew(); var nextInput = 0.0;
            do
            {
                if (process.HasExited) throw new Exception("Server exited: " + await errors);
                client.Poll();
                while (clock.Elapsed.TotalSeconds >= nextInput)
                {
                    nextInput += 1.0 / 60;
                    if (peer > 0 && owner.ValueKind != JsonValueKind.Undefined)
                        client.SendJson(21, intent with { Sequence = ++sequence, Life = owner.GetProperty("Life").GetInt32() });
                }
                if (until?.Invoke() == true) return;
                await Task.Delay(4);
            } while (clock.Elapsed.TotalSeconds < seconds);
            if (until is not null) throw new Exception($"Timed out: bot damage/elimination; hits={hitCount}, deaths={string.Join(',', deaths)}, magazine={(owner.ValueKind == JsonValueKind.Undefined ? -1 : owner.GetProperty("Magazine").GetInt32())}");
        }
        static Vector3 Position(JsonElement j) => new(j.GetProperty("X").GetSingle(), j.GetProperty("Y").GetSingle(), j.GetProperty("Z").GetSingle());
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try { await client.ConnectAsync("127.0.0.1", (ushort)port); break; }
                catch (SocketException) when (attempt < 200 && !process.HasExited) { await Task.Delay(100); }
            }
            await Pump(20, () => owner.ValueKind != JsonValueKind.Undefined && match.ValueKind != JsonValueKind.Undefined && match.GetProperty("Phase").GetInt32() == 2 && transforms.Keys.Any(id => id < -1));
            await Pump(1);
            var bot = match.GetProperty("Players").EnumerateArray().First(p => p.GetProperty("IsBot").GetBoolean());
            var botId = bot.GetProperty("PeerId").GetInt32();
            var local = match.GetProperty("Players").EnumerateArray().First(p => p.GetProperty("PeerId").GetInt32() == peer);
            if (local.GetProperty("Team").GetInt32() == bot.GetProperty("Team").GetInt32()) { client.SendJson(14, new { }); await Pump(.3); }
            for (var elimination = 1; elimination <= 2; elimination++)
            {
                // Exercise both torso and head-height aim against the actual bot
                // prefab. Human-only tests cannot cover its child hitbox mapping.
                var aimHeight = elimination == 1 ? .2f : .65f;
                var target = Position(transforms[botId]) + Vector3.UnitY * aimHeight;
                var origin = Position(owner) + Vector3.UnitY * ServerMotor.EyeHeight(false);
                var direction = Vector3.Normalize(target - origin);
                var previousHits = hitCount;
                intent = intent with { Aim = true, Fire = true, Yaw = MathF.Atan2(-direction.X, -direction.Z) * 180 / MathF.PI, Pitch = MathF.Asin(direction.Y) * 180 / MathF.PI };
                await Pump(3, () => match.GetProperty("Players").EnumerateArray().Any(p => p.GetProperty("PeerId").GetInt32() == botId && p.GetProperty("Deaths").GetInt32() >= elimination));
                if (hitCount <= previousHits || !deaths.Contains(botId)) throw new Exception("Bot elimination lacked confirmed damage/death events");
                intent = intent with { Fire = false };
                await Pump(3, () => match.GetProperty("Players").EnumerateArray().Any(p => p.GetProperty("PeerId").GetInt32() == botId && p.GetProperty("Alive").GetBoolean()));
                await Pump(.3);
            }
            var finalLocal = match.GetProperty("Players").EnumerateArray().First(p => p.GetProperty("PeerId").GetInt32() == peer);
            if (finalLocal.GetProperty("Kills").GetInt32() != 2) throw new Exception("Bot eliminations did not credit the player");
            Console.WriteLine("PASS: real Enemy prefab torso/head-height damage, hit confirmations, two bot eliminations, kill credit and respawn");
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            var log = await errors; if (log.Contains("Exception")) Console.Error.WriteLine(log);
        }
    }
}
