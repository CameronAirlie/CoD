using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using CoD.Scripts;
using PlutoGE.ScriptCore.Networking;

internal static class DefusalMovementIntegration
{
    public static async Task Run(string[] args)
    {
        if (args.Length != 2 && !(args.Length == 3 && args[2] == "--defusal-movement"))
            throw new ArgumentException("Expected native server executable and CoD project paths [--defaults]");
        var useDefaults = false;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var start = new ProcessStartInfo(Path.GetFullPath(args[0])) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.WorkingDirectory = Path.GetTempPath(); // Pack discovery must not depend on the launch directory.
        start.Environment["COD_SERVER_PORT"] = port.ToString(); start.Environment["COD_SERVER_BOTS"] = "0";
        start.Environment["COD_SERVER_MODE"] = "Defusal";
        if (!useDefaults) { start.ArgumentList.Add("--project"); start.ArgumentList.Add(Path.GetFullPath(args[1])); }
        foreach (var value in new[] { "--ticks", "1800" }) start.ArgumentList.Add(value);
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
        PlayerCommand secondIntent = PlayerCommand.Idle;
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
                    if (secondPeer >= 0) second.SendJson(21, secondIntent with { Sequence = sequence + 1, Life = secondOwner.ValueKind == JsonValueKind.Undefined ? 0 : secondOwner.GetProperty("Life").GetInt32() });
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
            await Pump(.3);
            var initial=Position(owner);
            var secondInitial = Position(secondOwner);
            secondIntent = PlayerCommand.Idle with { MoveY = 1, Sprint = true, Yaw = secondInitial.Z < 0 ? 180 : 0 };
            Console.WriteLine($"Initial {owner}");
            intent=PlayerCommand.Idle with { MoveY=1, Sprint=true, Yaw=initial.Z<0?180:0 };
            await Pump(1);
            Console.WriteLine($"After movement {owner}");
            Check(Vector3.Distance(initial,Position(owner))>2, "Defusal spawn prevents movement");
            Check(Vector3.Distance(secondInitial, Position(secondOwner)) > 2, $"Defender spawn prevents movement: {secondOwner}");
            Console.WriteLine("PASS: both teams move out of Foundry Defusal spawns");

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

