using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PlutoGE.ScriptCore.Networking;

internal static class BotNetworkIntegration
{
    public static async Task Run(string[] args)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var scene = Path.Combine(AppContext.BaseDirectory, "bot-equipment-arena.plutoscene");
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "arena.plutoscene"));
        text = text.Replace("-5,1,0", "-11,1,0").Replace("5,1,0", "11,1,0")
            .Replace("5,1,-10", "11,1,-22").Replace("-5,1,-10", "-11,1,-22");
        File.WriteAllText(scene, text);
        var start = new ProcessStartInfo(Path.GetFullPath(args[0])) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["COD_SERVER_PORT"] = port.ToString(); start.Environment["COD_SERVER_BOTS"] = "3";
        start.Environment["COD_SERVER_MODE"] = "TeamDeathmatch";
        foreach (var value in new[] { "--project", Path.GetFullPath(args[1]), "--scene", scene, "--ticks", "2400" }) start.ArgumentList.Add(value);
        foreach (var script in new[] { "MultiplayerSession", "EnemySoldierBot", "NetworkParticipantHitbox" })
        { start.ArgumentList.Add("--script"); start.ArgumentList.Add("CoD.Scripts." + script); }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var client = new NetworkClient();
        var botCount = 0; var pistolBots = new HashSet<int>(); var shots = new HashSet<int>(); var throws = false;
        var money = -1;
        client.Connected += () => client.SendJson(1, new { ProtocolVersion = 20, Username = "BotEquipmentObserver", Mode = 0 });
        client.MessageReceived += message => {
            var json = message.GetJson<JsonElement>();
            if (message.Channel == 7) botCount = json.GetProperty("Players").EnumerateArray().Count(p => p.GetProperty("IsBot").GetBoolean());
            if (message.Channel == 2 && json.GetProperty("PeerId").GetInt32() < 0 && json.TryGetProperty("WeaponSlot", out var slot) && slot.GetInt32() == 1)
                pistolBots.Add(json.GetProperty("PeerId").GetInt32());
            if (message.Channel == 9 && json.GetProperty("ShooterPeerId").GetInt32() < 0) shots.Add(json.GetProperty("ShooterPeerId").GetInt32());
            if (message.Channel == 17 && json.GetProperty("Items").GetArrayLength() > 0) throws = true;
            if (message.Channel == 28) money = json.GetProperty("Money").GetInt32();
        };
        try
        {
            for (var attempt = 0; ; attempt++)
                try { await client.ConnectAsync("127.0.0.1", (ushort)port); break; }
                catch (SocketException) when (attempt < 200 && !process.HasExited) { await Task.Delay(100); }
            var elapsed = Stopwatch.StartNew(); var heartbeat = 0.0;
            while (elapsed.Elapsed.TotalSeconds < 25)
            {
                if (process.HasExited) throw new Exception("Bot server exited: " + await errors);
                client.Poll();
                if (elapsed.Elapsed.TotalSeconds >= heartbeat) { heartbeat += .1; client.Send(25, Array.Empty<byte>()); }
                if (botCount >= 2 && pistolBots.Count >= 2 && shots.Count > 0 && throws && money == 800) break;
                await Task.Delay(4);
            }
            if (botCount < 2 || pistolBots.Count < 2 || shots.Count == 0 || !throws || money != 800)
                throw new Exception($"Bot replication incomplete: bots={botCount}, pistol poses={pistolBots.Count}, shooters={shots.Count}, throws={throws}, human money={money}");
            Console.WriteLine("PASS: real dedicated-server bots spawn with pistols, fire, throw grenades, replicate weapon poses/projectiles and leave the human wallet unchanged.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            var log = await errors;
            if (log.Contains("Exception", StringComparison.OrdinalIgnoreCase)) Console.Error.WriteLine(log);
        }
    }
}
