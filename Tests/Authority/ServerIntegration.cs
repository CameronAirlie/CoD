using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using CoD.Scripts;
using PlutoGE.ScriptCore.Networking;

internal static class ServerIntegration
{
    public static async Task Run(string[] args)
    {
        if (args.Length != 2 && !(args.Length == 3 && args[2] == "--defaults"))
            throw new ArgumentException("Expected native server executable and CoD project paths [--defaults]");
        var useDefaults = args.Length == 3;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var start = new ProcessStartInfo(Path.GetFullPath(args[0])) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.WorkingDirectory = Path.GetTempPath(); // Pack discovery must not depend on the launch directory.
        start.Environment["COD_SERVER_PORT"] = port.ToString(); start.Environment["COD_SERVER_BOTS"] = "0";
        start.Environment["COD_SERVER_MODE"] = "TeamDeathmatch";
        if (!useDefaults) { start.ArgumentList.Add("--project"); start.ArgumentList.Add(Path.GetFullPath(args[1])); }
        foreach (var value in new[] { "--scene", Path.Combine(AppContext.BaseDirectory, "arena.plutoscene"), "--ticks", "1800" }) start.ArgumentList.Add(value);
        if (!useDefaults)
            foreach (var script in new[] { "MultiplayerSession", "EnemySoldierBot", "NetworkParticipantHitbox", "SupplySpawner", "SupplyPickup" })
            { start.ArgumentList.Add("--script"); start.ArgumentList.Add("CoD.Scripts." + script); }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var first = new NetworkClient(); using var second = new NetworkClient();
        JsonElement owner = default, secondOwner = default, match = default;
        var firstPeer = -1; var secondPeer = -1;
        var firstShots = 0;
        first.MessageReceived += message =>
        {
            var json = JsonDocument.Parse(message.Payload).RootElement.Clone();
            if (message.Channel == 1) firstPeer = json.GetProperty("PeerId").GetInt32();
            if (message.Channel == 22) owner = json;
            if (message.Channel == 7) match = json;
            if (message.Channel == 9 && json.GetProperty("ShooterPeerId").GetInt32() == firstPeer) firstShots++;
        };
        second.MessageReceived += message =>
        {
            if (message.Channel == 1) secondPeer = message.GetJson<JsonElement>().GetProperty("PeerId").GetInt32();
            if (message.Channel == 22) secondOwner = message.GetJson<JsonElement>();
        };
        first.Connected += () => first.SendJson(1, new { ProtocolVersion = 16, Username = "AuthorityTestA", Mode = 0 });
        second.Connected += () => second.SendJson(1, new { ProtocolVersion = 16, Username = "AuthorityTestB", Mode = 0 });
        var sequence = 0L;
        PlayerCommand intent = PlayerCommand.Idle;
        async Task Pump(double seconds, Func<bool>? until = null)
        {
            var deadline = Stopwatch.StartNew();
            var nextInputAt = 0.0;
            do
            {
                if (process.HasExited) throw new Exception("Server exited: " + await errors);
                first.Poll(); second.Poll();
                while (deadline.Elapsed.TotalSeconds >= nextInputAt)
                {
                    nextInputAt += 1.0 / 60;
                    if (firstPeer >= 0) first.SendJson(21, intent with { Sequence = ++sequence, Life = owner.ValueKind == JsonValueKind.Undefined ? 0 : owner.GetProperty("Life").GetInt32() });
                    if (secondPeer >= 0) second.SendJson(21, PlayerCommand.Idle with { Sequence = sequence + 1, Life = secondOwner.ValueKind == JsonValueKind.Undefined ? 0 : secondOwner.GetProperty("Life").GetInt32() });
                }
                if (until?.Invoke() == true) return;
                await Task.Delay(4);
            } while (deadline.Elapsed.TotalSeconds < seconds);
            if (until is not null) throw new Exception("Timed out waiting for server state");
        }
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static Vector3 Position(JsonElement value) => new(value.GetProperty("X").GetSingle(), value.GetProperty("Y").GetSingle(), value.GetProperty("Z").GetSingle());
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try { await first.ConnectAsync("127.0.0.1", (ushort)port); break; }
                catch (SocketException) when (attempt < 200 && !process.HasExited) { await Task.Delay(100); }
            }
            await second.ConnectAsync("127.0.0.1", (ushort)port);
            await Pump(15, () => firstPeer > 0 && secondPeer > 0 && owner.ValueKind != JsonValueKind.Undefined &&
                match.ValueKind != JsonValueKind.Undefined && match.GetProperty("Phase").GetInt32() == 2);
            Check(!match.GetProperty("Players").EnumerateArray().Any(p => p.GetProperty("PeerId").GetInt32() == 0), "Dedicated server created a host player");
            await Pump(.3);
            Check(owner.GetProperty("Grounded").GetBoolean(), "Standing capsule was not grounded above the collision skin");
            var standing = Position(owner);
            intent = intent with { Jump = true };
            await Pump(.08);
            intent = intent with { Jump = false };
            await Pump(.12);
            Check(Position(owner).Y > standing.Y + .25f, "Authoritative jump did not leave the floor");
            await Pump(1);
            Check(owner.GetProperty("Grounded").GetBoolean(), "Player did not land after jumping");
            var initial = Position(owner);
            first.SendJson(2, new { PeerId = secondPeer, X = 99999, Y = 99999, Z = 99999, Pitch = 0, Yaw = 0, Roll = 0, RotationX = 0, RotationY = 0, RotationZ = 0, RotationW = 1 });
            first.SendJson(4, new { OriginX = 0, OriginY = 0, OriginZ = 0, DirectionX = 0, DirectionY = 0, DirectionZ = -1, WeaponId = "lmg" });
            await Pump(.3);
            Check(Vector3.Distance(Position(owner), initial) < 1, $"Client transform teleported server player: {initial} -> {Position(owner)}");
            Check(owner.GetProperty("Magazine").GetInt32() == 30 && firstShots == 0, "Raw shot request was accepted");
            intent = intent with { MoveX = 1 };
            await Pump(1);
            Check(Vector3.Distance(Position(owner), initial) > 4.5f && Vector3.Distance(Position(owner), initial) < 6.5f, $"Authoritative walk speed incorrect: {initial} -> {Position(owner)}, grounded={owner.GetProperty("Grounded")}");
            intent = intent with { MoveX = 0, MoveY = 1, Sprint = true };
            var sprintStart = Position(owner);
            await Pump(1);
            Check(Vector3.Distance(Position(owner), sprintStart) > 6.5f && Vector3.Distance(Position(owner), sprintStart) < 9,
                $"Grounded sprint did not reach its movement speed: {sprintStart} -> {Position(owner)}, grounded={owner.GetProperty("Grounded")}, sprinting={owner.GetProperty("Sprinting")}");
            intent = intent with { MoveY = 0, Sprint = false, Fire = true, Pitch = -89 };
            await Pump(4);
            Check(owner.GetProperty("Magazine").GetInt32() == 0, "Server did not consume magazine");
            Check(firstShots == 30, "Server accepted more shots than magazine capacity");
            intent = intent with { Fire = false, Reload = true };
            await Pump(.1); intent = intent with { Reload = false };
            await Pump(.4);
            Check(owner.GetProperty("Reloading").GetBoolean() && owner.GetProperty("Magazine").GetInt32() == 0, "Reload completed early");
            await Pump(2);
            Check(owner.GetProperty("Magazine").GetInt32() == 30 && owner.GetProperty("Reserve").GetInt32() == 120, "Reload resources not authoritative");
            var target = Position(secondOwner) + Vector3.UnitY * ServerMotor.EyeHeight(false);
            var origin = Position(owner) + Vector3.UnitY * ServerMotor.EyeHeight(false);
            var direction = Vector3.Normalize(target - origin);
            intent = intent with { Aim = true, Pitch = MathF.Asin(direction.Y) * 180 / MathF.PI,
                Yaw = MathF.Atan2(-direction.X, -direction.Z) * 180 / MathF.PI, Fire = true };
            Check(MathF.Abs(intent.Pitch) < .1f, "Damage regression must aim level at an equal-height player");
            var healthBeforeShot = secondOwner.GetProperty("Health").GetSingle();
            await Pump(.5);
            Check(secondOwner.GetProperty("Health").GetSingle() < healthBeforeShot,
                "Level camera aim did not damage the other player");
            await Pump(2, () => secondOwner.GetProperty("Health").GetSingle() <= 0);
            intent = intent with { Fire = false };
            var deadLife = secondOwner.GetProperty("Life").GetInt32();
            var deathPosition = Position(secondOwner);
            await Pump(.6);
            Check(secondOwner.GetProperty("Life").GetInt32() == deadLife && secondOwner.GetProperty("Health").GetSingle() <= 0,
                "Dead player respawned before the server delay");
            Check(Vector3.Distance(Position(secondOwner), deathPosition) < .01f, "Dead player teleported between spawn points");
            await Pump(3, () => secondOwner.GetProperty("Life").GetInt32() > deadLife && secondOwner.GetProperty("Health").GetSingle() == 100);
            Check(secondOwner.GetProperty("Life").GetInt32() == deadLife + 1, "Death produced multiple respawns");
            var respawnPosition = Position(secondOwner);
            await Pump(.5);
            Check(secondOwner.GetProperty("Life").GetInt32() == deadLife + 1 && Vector3.Distance(Position(secondOwner), respawnPosition) < .1f,
                "Respawn did not remain stable");
            Check(match.GetProperty("Players").EnumerateArray().Any(p => p.GetProperty("PeerId").GetInt32() == firstPeer && p.GetProperty("Kills").GetInt32() == 1), "Server kill was not scored");
            await second.DisconnectAsync();
            await Pump(.5);
            Check(first.IsConnected, "One peer leaving stopped the server");
            Console.WriteLine("PASS: two clients, no host player, forged transform/shot rejection, grounding, jump/landing, walk/sprint speed, ammunition, reload, authoritative damage/death/respawn and independent disconnect");
        }
        finally
        {
            first.Dispose(); second.Dispose();
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            var log = await errors;
            if (log.Contains("Exception", StringComparison.OrdinalIgnoreCase)) Console.Error.WriteLine(log);
        }
    }
}

