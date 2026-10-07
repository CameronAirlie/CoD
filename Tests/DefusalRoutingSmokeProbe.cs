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
        var crouchedHeight = CombatTargeting.StanceHeight(2, .5f, .5f, true);
        var crouchedCenter = -System.Numerics.Vector3.UnitY * ((2 - crouchedHeight) * .5f);
        Check(crouchedHeight == 1.5f && crouchedCenter.Y - crouchedHeight * .5f == -1,
            "Crouching did not shrink the hitbox while preserving its feet.");
        Check(crouchedCenter.Y + crouchedHeight * .5f == .5f,
            "Crouching left an exposed standing hitbox above cover.");
        Check(CombatTargeting.AimPoint(System.Numerics.Vector3.Zero, crouchedCenter, crouchedHeight).Y <
            CombatTargeting.AimPoint(System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero, 2).Y,
            "Bot aim did not lower with the crouched hitbox.");
        Check(CombatTargeting.StanceHeight(2, .5f, .5f, false) == 2 &&
            CombatTargeting.StanceHeight(2, .5f, 10, true) == 1,
            "Standing restore or minimum capsule diameter failed.");
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
        // Terrain tiers must be real collision surfaces and reachable in both directions.
        foreach (var sample in new[] {
            new System.Numerics.Vector3(-22, 2, -16),
            new System.Numerics.Vector3(22, 4, -16),
            new System.Numerics.Vector3(0, 4, -32),
            new System.Numerics.Vector3(0, 0, 3) })
        {
            Check(Physics.Raycast(sample + System.Numerics.Vector3.UnitY * 10,
                -System.Numerics.Vector3.UnitY, 15, out var floor) &&
                MathF.Abs(floor.Point.Y - sample.Y) < .1f, "Incorrect Foundry tier height.");
        }
        // Representative cross-map views must hit architecture before the far yard.
        foreach (var (from, to) in new[] {
            (new System.Numerics.Vector3(-22, 3.6f, 0), new System.Numerics.Vector3(22, 5.6f, 0)),
            (new System.Numerics.Vector3(-22, 3.6f, -16), new System.Numerics.Vector3(22, 5.6f, -16)),
            (new System.Numerics.Vector3(0, 1.6f, 21), new System.Numerics.Vector3(0, 5.6f, -26)),
            (new System.Numerics.Vector3(-24, 3.6f, 10), new System.Numerics.Vector3(-24, 3.6f, -8)),
            (new System.Numerics.Vector3(24, 5.6f, 10), new System.Numerics.Vector3(24, 5.6f, -8)),
            (new System.Numerics.Vector3(-28, 5.6f, -30), new System.Numerics.Vector3(28, 5.6f, -30)) })
        {
            var ray = to - from;
            Check(Physics.Raycast(from, System.Numerics.Vector3.Normalize(ray), ray.Length(), out var obstruction)
                && obstruction.Distance < ray.Length() - 1, $"Unblocked Foundry sightline {from} -> {to}.");
        }
        var connections = new[] {
            (new System.Numerics.Vector3(-23, 0, 25), new System.Numerics.Vector3(-23, 2, 15)),
            (new System.Numerics.Vector3(23, 0, 29), new System.Numerics.Vector3(23, 4, 15)),
            (new System.Numerics.Vector3(0, 0, -15), new System.Numerics.Vector3(0, 4, -25)),
            (new System.Numerics.Vector3(-24, 2, -17), new System.Numerics.Vector3(-24, 4, -25)),
            (new System.Numerics.Vector3(-6, 0, -15), new System.Numerics.Vector3(-16, 2, -15)),
            (new System.Numerics.Vector3(-6, 0, 2), new System.Numerics.Vector3(-16, 2, 2)),
            (new System.Numerics.Vector3(-6, 0, 13), new System.Numerics.Vector3(-16, 2, 13)),
            (new System.Numerics.Vector3(6, 0, -18), new System.Numerics.Vector3(16, 4, -18)),
            (new System.Numerics.Vector3(6, 0, 0), new System.Numerics.Vector3(16, 4, 0)),
            (new System.Numerics.Vector3(6, 0, 12), new System.Numerics.Vector3(16, 4, 12)) };
        foreach (var (low, high) in connections)
        {
            Check(Navigation.FindPath(navigationMesh!, low, high, botNavigationAgentRadius, botNavigationAgentHeight).Complete,
                $"Unreachable uphill Foundry connection {low} -> {high}.");
            Check(Navigation.FindPath(navigationMesh!, high, low, botNavigationAgentRadius, botNavigationAgentHeight).Complete,
                $"Unreachable downhill Foundry connection {high} -> {low}.");
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
                // Complete the approach while the human carrier remains at spawn.
                // The attack must keep clearing its site rather than return to them.
                bot.DefusalApproach.Destination(bot.GameObject.WorldPosition,
                    SitePosition(plan.Site), null, _time, false);
                bot.NextNavigationAt = 0;
                Check(TryDefusalBotMovement(pair.Key, bot, _playerStates[pair.Key], true), "Site movement rejected.");
                var goal = SitePosition(plan.Site);
                if (_defusal!.Carrier != pair.Key)
                    goal += DefusalBotTactics.CoverOffset(plan.ApproachAngle);
                if (TryProjectBotNavigationPosition(goal, out var projected)) goal = projected;
                Check(HorizontalDistance(bot.NavigationDestination, goal) < .1f,
                    "Bot abandoned its site to follow the stationary carrier.");
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
            File.WriteAllText(resultPath, "PASS: six blocked cross-map sightlines, four terrain heights, ten bidirectional tier connections, six Foundry lanes and native bot routes across eight round resets.");
        }
        catch (Exception error) { File.WriteAllText(resultPath, "FAIL: " + error); }
    }
}
