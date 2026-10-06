using PlutoGE.ScriptCore;

namespace CoD.Scripts;

// Included only by the isolated native smoke runner.
public sealed partial class MultiplayerSession
{
    internal void CheckDefusalRouting()
    {
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        Check(navigationMesh is not null && navigationMesh.IsValid, "Foundry navmesh missing.");
        foreach (var role in new[] { "Attack", "Defend" })
        {
            var spawn = PlutoGE.ScriptCore.GameObject.Find(role == "Attack" ? "Attacker Spawn" : "Defender Spawn")!.WorldPosition;
            foreach (var lane in new[] { "West", "Mid", "East" })
            {
                var name = $"Defusal {role} {lane}";
                var marker = PlutoGE.ScriptCore.GameObject.Find(name)!;
                Check(marker is not null && TryProjectBotNavigationPosition(marker.WorldPosition, out _), $"Cannot project {name}.");
                TryProjectBotNavigationPosition(marker!.WorldPosition, out var waypoint);
                var path = Navigation.FindPath(navigationMesh!, spawn, waypoint, botNavigationAgentRadius, botNavigationAgentHeight);
                Check(path.Complete && path.Points.Count > 0, $"Unreachable {name}.");
            }
        }
        for (int i = 0; i < 6; i++) EnsureBotFill();
        Check(_bots.Count >= 2, "Bot roster failed to spawn.");
        var previous = new Dictionary<int, int>();
        for (int round = 0; round < 8; round++)
        {
            StartDefusalRound();
            foreach (var pair in _bots)
            {
                var bot = pair.Value;
                var plan = _defusalTactics.PlanFor(pair.Key);
                Check(!previous.TryGetValue(pair.Key, out var lane) || lane != plan.Lane, "Repeated lane after round reset.");
                previous[pair.Key] = plan.Lane;
                bot.TargetPeerId = int.MinValue;
                bot.CachedLineOfSight = false;
                bot.NextNavigationAt = 0;
                Check(TryDefusalBotMovement(pair.Key, bot, _playerStates[pair.Key], true), "Objective movement rejected.");
                Check(bot.DefusalWaypoint.HasValue && !bot.DefusalApproach.Complete, "Lane fell back to direct objective.");
                Check(bot.NavigationDestination == bot.DefusalWaypoint!.Value, "Adapter did not issue the lane destination.");
            }
        }
    }
}

public sealed class DefusalRoutingSmokeProbe : ScriptBehaviour
{
    [SerializedField] private string resultPath = "";
    private int _frames;
    public override void OnUpdate(float deltaTime)
    {
        if (++_frames != 15) return;
        try
        {
            GameObject.GetComponent<MultiplayerSession>()!.CheckDefusalRouting();
            File.WriteAllText(resultPath, "PASS: six reachable Foundry lanes and native bot route destinations across eight round resets.");
        }
        catch (Exception error) { File.WriteAllText(resultPath, "FAIL: " + error); }
    }
}
