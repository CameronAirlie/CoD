using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using CoD.Scripts;
using PlutoGE.ScriptCore.Networking;

internal static class SpectatorIntegration
{
    public static async Task Run(string[] args)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var start = new ProcessStartInfo(Path.GetFullPath(args[0])) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["COD_SERVER_PORT"] = port.ToString();
        start.Environment["COD_SERVER_BOTS"] = "0"; start.Environment["COD_SERVER_MODE"] = "TeamDeathmatch";
        foreach (var value in new[] { "--project", Path.GetFullPath(args[1]), "--scene", Path.Combine(AppContext.BaseDirectory, "arena.plutoscene"),
            "--ticks", "1800", "--script", "CoD.Scripts.MultiplayerSession", "--script", "CoD.Scripts.NetworkParticipantHitbox" }) start.ArgumentList.Add(value);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var source = new NetworkClient(); using var enemy = new NetworkClient(); using var observer = new NetworkClient();
        NetworkClient[] clients = [source, enemy, observer];
        MultiplayerSessionState?[] owners = new MultiplayerSessionState?[3];
        var peers = new int[] { -1, -1, -1 }; long[] sequences = new long[3];
        var received = new List<SpectatorViewState>(); var enemyViews = 0; var sourceViews = 0; var viewSequence = 0L;
        for (var i = 0; i < clients.Length; i++)
        {
            var index = i;
            clients[i].Connected += () => clients[index].SendJson(1, new { ProtocolVersion = 19, Username = "SpectatorTest" + index, Mode = 0 });
            clients[i].MessageReceived += message => {
                if (message.Channel == 1) peers[index] = message.GetJson<System.Text.Json.JsonElement>().GetProperty("PeerId").GetInt32();
                if (message.Channel == 22) owners[index] = message.GetJson<MultiplayerSessionState>();
                if (message.Channel == 29)
                {
                    if (index == 0) sourceViews++;
                    else if (index == 1) enemyViews++;
                    else if (message.GetJson<SpectatorViewState>() is { } view) received.Add(view);
                }
            };
        }
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        SpectatorViewState MakeView() {
            var state = owners[0]!;
            var orientation = SpectatorCameraMath.FromHeading(-37,123,.35f);
            return new(999, state.Life, ++viewSequence, state.X, state.Y + ServerMotor.EyeHeight(false), state.Z,
                -37, 123, .35f, 60, 1, 8, 50, false, true, false, .2f, state.Health, 100, 0, .4f, 800,
                .1f, -.09f, -.2f, 5, -3, 1, true, 6, 1, 1, 0, 0, "flash", OrientationX: orientation.X, OrientationY: orientation.Y,
                OrientationZ: orientation.Z, OrientationW: orientation.W, SampleTime: viewSequence / 60d);
        }
        var clock = Stopwatch.StartNew(); var nextSend = 0d; var shoot = false;
        async Task Pump(double seconds, Func<bool>? until = null)
        {
            var deadline = clock.Elapsed.TotalSeconds + seconds;
            while (clock.Elapsed.TotalSeconds < deadline)
            {
                if (process.HasExited) throw new Exception("Server exited: " + await errors);
                foreach (var client in clients) client.Poll();
                if (clock.Elapsed.TotalSeconds >= nextSend)
                {
                    nextSend = clock.Elapsed.TotalSeconds + 1.0 / 60;
                    for (var i = 0; i < clients.Length; i++)
                    {
                        if (peers[i] < 0 || owners[i] is not { } state) continue;
                        var command = PlayerCommand.Idle with { Sequence = ++sequences[i], Life = state.Life, Slot = 1 };
                        if (i == 1 && shoot && owners[2] is { } target)
                        {
                            var ray = new Vector3(target.X - state.X, target.Y - state.Y - .25f, target.Z - state.Z);
                            command = command with { Yaw = MathF.Atan2(-ray.X, -ray.Z) * 180 / MathF.PI,
                                Pitch = MathF.Atan2(ray.Y, new Vector2(ray.X, ray.Z).Length()) * 180 / MathF.PI,
                                Aim = true, Fire = ((int)(clock.Elapsed.TotalSeconds * 5) % 2) == 0 };
                        }
                        clients[i].SendJson(21, command); clients[i].Send(25, Array.Empty<byte>());
                    }
                    if (owners[0] is not null) source.SendJson(29, MakeView());
                }
                if (until?.Invoke() == true) return;
                await Task.Delay(4);
            }
            if (until is not null) throw new Exception("Spectator test timed out; owners=" + System.Text.Json.JsonSerializer.Serialize(owners));
        }
        try
        {
            for (var attempt = 0; ; attempt++)
                try { await source.ConnectAsync("127.0.0.1", port); break; }
                catch (SocketException) when (attempt < 200 && !process.HasExited) { await Task.Delay(100); }
            await Pump(10, () => owners[0] is not null);
            await enemy.ConnectAsync("127.0.0.1", port); await Pump(10, () => owners[1] is not null);
            await observer.ConnectAsync("127.0.0.1", port); await Pump(10, () => owners[2] is not null);
            await Pump(.3);
            Check(received.Count == 0 && enemyViews == 0 && sourceViews == 0, "Living players received private spectator frames.");
            shoot = true;
            await Pump(12, () => owners[2]?.Health <= 0);
            shoot = false;
            await Pump(1, () => received.Count > 0);
            var view = received[^1];
            Check(view.PeerId == peers[0], "Server trusted spoofed peer id.");
            Check(view.Pitch == -37 && view.Yaw == 123 && view.Roll == .35f && view.Fov == 60 && view.Magazine == 8 && view.HandX == .1f,
                "Server relay changed rendered POV or HUD state.");
            Check(view.SampleTime > 0 && MathF.Abs(Quaternion.Dot(view.Orientation, SpectatorCameraMath.FromHeading(-37,123,.35f))) > .99999f,
                "Transport lost source time or quaternion orientation.");
            Check(enemyViews == 0 && sourceViews == 0, "Spectator frames leaked to enemy/source.");
            await Pump(8, () => owners[2]?.Health > 0);
            await Pump(.2); var afterRespawn = received.Count;
            await Pump(.3);
            Check(received.Count == afterRespawn, "Respawned observer kept receiving POV.");
            Console.WriteLine("PASS: real server routes exact POV to dead teammate, stamps transport identity, excludes living/enemy peers and stops on respawn.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(); await output;
            var log = await errors;
            if (log.Contains("Exception", StringComparison.OrdinalIgnoreCase)) Console.Error.WriteLine(log);
        }
    }
    private sealed record MultiplayerSessionState(float X, float Y, float Z, float Health, int Life);
}
