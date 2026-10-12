using System.Reflection;
using CoD.Scripts;
internal static class DefusalRoundPresentationTests
{
    public static void Run()
    {
        var session = new MultiplayerSession();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(MultiplayerSession).GetField("mode", flags)!.SetValue(session, "Client");
        var dead = (Dictionary<int, float>)typeof(MultiplayerSession).GetField("_deadRemotePlayers", flags)!.GetValue(session)!;
        var update = typeof(MultiplayerSession).GetMethod("UpdateLocalRound", flags)!;
        var players = new[] {
            new MatchPlayer(-1000, "Alpha bot", PlayerTeam.Alpha, 0, 1, true),
            new MatchPlayer(-1001, "Bravo bot", PlayerTeam.Bravo, 0, 1, true),
            new MatchPlayer(2, "Remote human", PlayerTeam.Bravo, 0, 1, false) };
        for (var round = 1; round <= 8; round++)
        {
            foreach (var p in players) dead[p.PeerId] = float.PositiveInfinity;
            var snapshot = new MatchSnapshot(MatchPhase.Results, 1, 7, 0, 0, 1,
                players.Select(p => p with { Alive = false }).ToArray(), MatchMode.Defusal);
            update.Invoke(session, new object[] { snapshot });
            if (dead.Count != players.Length) throw new Exception("Dead participants were released before the server revived them.");
            update.Invoke(session, new object[] { snapshot with { Phase = MatchPhase.Warmup, Players = players } });
            if (dead.Count != 0) throw new Exception("Defusal preparation did not release respawn suppression.");
            foreach (var p in players) dead[p.PeerId] = float.PositiveInfinity;
            update.Invoke(session, new object[] { snapshot with { Phase = MatchPhase.Playing, Players = players } });
            if (dead.Count != 0) throw new Exception("Next-round alive roster left participants blocked from respawning.");
        }
        Console.WriteLine("PASS: authoritative Defusal clients restore both teams across preparation, rounds and halftime");
    }
}
