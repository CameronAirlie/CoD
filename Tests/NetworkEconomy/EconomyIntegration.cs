using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CoD.Scripts;
using PlutoGE.ScriptCore.Networking;

internal static class EconomyIntegration
{
    public static async Task Run(string[] args)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var start = new ProcessStartInfo(Path.GetFullPath(args[0])) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["COD_SERVER_PORT"] = port.ToString();
        start.Environment["COD_SERVER_BOTS"] = "0"; start.Environment["COD_SERVER_MODE"] = "TeamDeathmatch";
        foreach (var value in new[] { "--project", Path.GetFullPath(args[1]), "--scene",
            Path.Combine(AppContext.BaseDirectory, "arena.plutoscene"), "--ticks", "1800" }) start.ArgumentList.Add(value);
        foreach (var script in new[] { "MultiplayerSession", "NetworkParticipantHitbox" })
        { start.ArgumentList.Add("--script"); start.ArgumentList.Add("CoD.Scripts." + script); }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var first = new NetworkClient(); using var second = new NetworkClient();
        JsonElement owner = default, economy = default, secondEconomy = default, supply = default, frame = default, secondFrame = default, match = default;
        var peer = -1; var sequence = 0L; var responses = 0;
        first.Connected += () => first.SendJson(1, new { ProtocolVersion = 19, Username = "Buyer", Mode = 0 });
        second.Connected += () => second.SendJson(1, new { ProtocolVersion = 19, Username = "Observer", Mode = 0 });
        first.MessageReceived += message => {
            var json = message.GetJson<JsonElement>();
            if (message.Channel == 1) peer = json.GetProperty("PeerId").GetInt32();
            if (message.Channel == 7) match = json;
            if (message.Channel == 22) owner = json;
            if (message.Channel == 28) { economy = json; responses++; }
            if (message.Channel == 19) supply = json;
            if (message.Channel == 17) frame = json;
        };
        second.MessageReceived += message => {
            if (message.Channel == 28) secondEconomy = message.GetJson<JsonElement>();
            if (message.Channel == 17) secondFrame = message.GetJson<JsonElement>();
        };
        async Task Pump(double seconds, Func<bool>? until = null)
        {
            var elapsed = Stopwatch.StartNew(); var nextInput = 0.0;
            do {
                if (process.HasExited) throw new Exception("Server exited: " + await errors);
                first.Poll(); second.Poll();
                if (peer >= 0 && owner.ValueKind != JsonValueKind.Undefined && elapsed.Elapsed.TotalSeconds >= nextInput)
                {
                    nextInput += 1.0 / 60;
                    first.SendJson(21, PlayerCommand.Idle with { Sequence = ++sequence, Slot = 0, Life = owner.GetProperty("Life").GetInt32() });
                    first.Send(25, Array.Empty<byte>()); second.Send(25, Array.Empty<byte>());
                }
                if (until?.Invoke() == true) return;
                await Task.Delay(4);
            } while (elapsed.Elapsed.TotalSeconds < seconds);
            if (until is not null) throw new Exception($"Timed out: owner={owner}, supply={supply}, frame={frame}, observer={secondFrame}, match={match}");
        }
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        async Task Buy(string id)
        { var before = responses; first.SendJson(27, new { Id = id }); await Pump(2, () => responses > before); }
        try {
            for (var attempt = 0; ; attempt++)
                try { await first.ConnectAsync("127.0.0.1", (ushort)port); break; }
                catch (SocketException) when (attempt < 200 && !process.HasExited) { await Task.Delay(100); }
            await second.ConnectAsync("127.0.0.1", (ushort)port);
            await Pump(15, () => owner.ValueKind != JsonValueKind.Undefined && economy.ValueKind != JsonValueKind.Undefined &&
                secondEconomy.ValueKind != JsonValueKind.Undefined && match.ValueKind != JsonValueKind.Undefined && match.GetProperty("Phase").GetInt32() == 2);
            Check(economy.GetProperty("Money").GetInt32() == 800 && owner.GetProperty("Slot").GetInt32() == 1, "Starting wallet/sidearm not replicated.");
            await Buy("ar"); Check(economy.GetProperty("Money").GetInt32() == 800 && economy.GetProperty("WeaponMask").GetInt32() == 2, "Unaffordable remote rifle accepted.");
            await Buy("flash"); await Buy("smoke");
            await Pump(.1);
            Check(economy.GetProperty("Money").GetInt32() == 300 && supply.GetProperty("Counts")[1].GetInt32() == 1 && supply.GetProperty("Counts")[2].GetInt32() == 1,
                "Remote purchases failed to replicate quantities/balance.");
            Check(secondEconomy.GetProperty("Money").GetInt32() == 800, "Purchase modified another client's wallet.");
            await Buy("flash"); Check(economy.GetProperty("Money").GetInt32() == 300, "Full remote inventory was charged.");
            first.SendJson(16, new { X = 0, Y = -1, Z = 0, Kind = "molotov" }); await Pump(.2);
            Check(frame.GetProperty("Items").GetArrayLength() == 0, "Unowned remote throwable spawned.");
            first.SendJson(16, new { X = 0, Y = -1, Z = 0, Kind = "flash" });
            await Pump(2, () => supply.GetProperty("Counts")[1].GetInt32() == 0);
            await Pump(.9);
            first.SendJson(16, new { X = 0, Y = -1, Z = 0, Kind = "smoke" });
            await Pump(4, () => secondFrame.ValueKind != JsonValueKind.Undefined && secondFrame.GetProperty("Areas").EnumerateArray().Any(area => area.GetProperty("Kind").GetString() == "smoke"));
            Check(supply.GetProperty("Counts")[2].GetInt32() == 0 && owner.GetProperty("Slot").GetInt32() == 1, "Host inventory/weapon validation failed.");
            Console.WriteLine("PASS: two-client wallets, private purchases, ownership rejection, throwable supply, host throw validation and persistent smoke replication.");
        }
        finally {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            var log = await errors;
            if (log.Contains("Exception", StringComparison.OrdinalIgnoreCase)) Console.Error.WriteLine(log);
        }
    }
}
