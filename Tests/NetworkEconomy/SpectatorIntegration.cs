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
        var defusal = args[2] == "--neutral-defusal";
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var start = new ProcessStartInfo(Path.GetFullPath(args[0])) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        start.Environment["COD_SERVER_PORT"] = port.ToString();
        start.Environment["COD_SERVER_BOTS"] = "0"; start.Environment["COD_SERVER_MODE"] = defusal ? "Defusal" : "TeamDeathmatch";
        foreach (var value in new[] { "--project", Path.GetFullPath(args[1]), "--scene", Path.Combine(AppContext.BaseDirectory, "arena.plutoscene"),
            "--ticks", "1800", "--script", "CoD.Scripts.MultiplayerSession", "--script", "CoD.Scripts.NetworkParticipantHitbox" }) start.ArgumentList.Add(value);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var source = new NetworkClient(); using var enemy = new NetworkClient(); using var observer = new NetworkClient(); using var neutral = new NetworkClient();
        NetworkClient[] clients = [source, enemy, observer, neutral];
        MultiplayerSessionState?[] owners = new MultiplayerSessionState?[4];
        var peers = new int[] { -1, -1, -1, -1 }; long[] sequences = new long[4];
        var received = new List<SpectatorViewState>(); var enemyViews = 0; var sourceViews = 0; var viewSequence = 0L;
        var neutralViews = new List<SpectatorViewState>(); MatchSnapshot? roster = null; var neutralEconomyMessages = 0;
        for (var i = 0; i < clients.Length; i++)
        {
            var index = i;
            clients[i].Connected += () => clients[index].SendJson(1, new { ProtocolVersion = 20, Username = "SpectatorTest" + index, Mode = 0, Spectator = index == 3 });
            clients[i].MessageReceived += message => {
                if (message.Channel == 1) peers[index] = message.GetJson<System.Text.Json.JsonElement>().GetProperty("PeerId").GetInt32();
                if (index == 3 && message.Channel == 7) roster = message.GetJson<MatchSnapshot>();
                if (index == 3 && message.Channel == 28) neutralEconomyMessages++;
                if (message.Channel == 22) owners[index] = message.GetJson<MultiplayerSessionState>();
                if (message.Channel == 29)
                {
                    if (index == 0) sourceViews++;
                    else if (index == 1) enemyViews++;
                    else if (message.GetJson<SpectatorViewState>() is { } view) { if (index == 3) neutralViews.Add(view); else received.Add(view); }
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
                        if (peers[i] >= 0) clients[i].Send(25, Array.Empty<byte>());
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
                    if (owners[1] is { } enemyState)
                        enemy.SendJson(29, MakeView() with { Life = enemyState.Life, X = enemyState.X, Y = enemyState.Y + ServerMotor.EyeHeight(false), Z = enemyState.Z });
                }
                if (until?.Invoke() == true) return;
                await Task.Delay(4);
            }
            if (until is not null) throw new Exception("Spectator test timed out; owners=" + System.Text.Json.JsonSerializer.Serialize(owners));
        }
        async Task CheckObserverAcrossRounds()
        {
            await Pump(10, () => roster?.Phase == MatchPhase.Playing);
            await process.StandardInput.WriteLineAsync("round end"); await process.StandardInput.FlushAsync();
            await Pump(3, () => roster?.Phase == MatchPhase.Results);
            Check(neutralEconomyMessages == 0 && owners[3] is null && roster!.Players.Single(p => p.PeerId == peers[3]).Alive == false,
                "Observer earned a round reward or spawned during results.");
            await process.StandardInput.WriteLineAsync("round restart"); await process.StandardInput.FlushAsync();
            await Pump(3, () => roster?.Phase == MatchPhase.Playing);
            Check(roster!.Players.Single(p => p.PeerId == peers[3]).Team == PlayerTeam.Spectator && !roster.Players.Single(p => p.PeerId == peers[3]).Alive && owners[3] is null,
                "Observer joined a team or spawned on round restart.");
            var before = neutralViews.Count;
            await Pump(2, () => neutralViews.Count > before);
        }
        try
        {
            for (var attempt = 0; ; attempt++)
                try { await source.ConnectAsync("127.0.0.1", port); break; }
                catch (SocketException) when (attempt < 200 && !process.HasExited) { await Task.Delay(100); }
            await Pump(10, () => owners[0] is not null);
            await enemy.ConnectAsync("127.0.0.1", port); await Pump(10, () => owners[1] is not null);
            await observer.ConnectAsync("127.0.0.1", port); await Pump(10, () => owners[2] is not null);
            await neutral.ConnectAsync("127.0.0.1", port);
            await Pump(10, () => roster?.Players.Any(p => p.PeerId == peers[3] && p.Team == PlayerTeam.Spectator) == true && neutralViews.Select(v => v.PeerId).Distinct().Count() >= 2);
            Check(owners[3] is null, "Neutral observer received a simulated player body.");
            Check(roster!.Players.Count(p => TeamIdentity.IsCombatTeam(p.Team)) == 3 && roster.Players.Single(p => p.PeerId == peers[3]).Alive == false,
                "Neutral observer changed the combat roster.");
            neutral.SendJson(14, new { });
            neutral.SendJson(27, new { Id = "flash" });
            neutral.SendJson(21, PlayerCommand.Idle with { Sequence = 1, Life = 1, Fire = true, MoveY = 1 });
            await Pump(.4);
            Check(neutralEconomyMessages == 0 && owners[3] is null && roster.Players.Single(p => p.PeerId == peers[3]).Team == PlayerTeam.Spectator,
                "Neutral gameplay requests granted a team, economy or simulated body.");
            Check(received.Count == 0 && enemyViews == 0 && sourceViews == 0, "Living players received private spectator frames.");
            if (defusal)
            {
                neutral.SendJson(15, new { Held = true }); await Pump(.2);
                Check(roster?.Defusal?.Operator != peers[3] && roster?.Defusal?.Carrier != peers[3], "Neutral observer operated/carried the bomb.");
                await CheckObserverAcrossRounds();
                Console.WriteLine("PASS: Defusal neutral observer receives both teams, cannot operate/carry the bomb, and stays outside the roster/rewards across round restart.");
                return;
            }
            shoot = true;
            await Pump(12, () => owners[2]?.Health <= 0);
            shoot = false;
            await Pump(1, () => received.Count > 0);
            var view = received[^1];
            Check(received.All(v => v.PeerId == peers[0]), "Dead team player received opposing POV.");
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
            Check(neutralViews.Any(v => v.PeerId == peers[0]) && neutralViews.Any(v => v.PeerId == peers[1]) && roster!.Players.Single(p => p.PeerId == peers[3]).Alive == false, "Neutral observer lost a team or respawned into combat.");
            await CheckObserverAcrossRounds();
            Console.WriteLine("PASS: neutral observer receives both teams, cannot join combat/buy/move, and remains neutral across player respawns.");
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
