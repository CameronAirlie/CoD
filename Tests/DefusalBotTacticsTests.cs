using CoD.Scripts;
using System.Numerics;

public static class DefusalBotTacticsTests
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        (int Id, bool Attacking)[] roster = [(-1000, true), (-1001, false), (-1002, true), (-1003, false), (-1004, false)];
        var tactics = new DefusalBotTactics();
        var replay = new DefusalBotTactics();
        var previous = new Dictionary<int, DefusalBotPlan>();
        var sites = new HashSet<int>();
        foreach (int round in Enumerable.Range(1, 100))
        {
            tactics.BeginRound(roster, round * 193);
            replay.BeginRound(roster.Reverse(), round * 193);
            sites.Add(tactics.AttackSite);
            foreach (var bot in roster)
            {
                var plan = tactics.PlanFor(bot.Id);
                Check(plan == replay.PlanFor(bot.Id), "Plans depend on roster enumeration order.");
                Check(plan.Site is 0 or 1 && plan.Lane is >= 0 and < 3, "Invalid site/lane.");
                Check(!bot.Attacking || plan.Site == tactics.AttackSite, "Attackers lost their shared objective.");
                Check(!previous.TryGetValue(bot.Id, out var old) || plan.Lane != old.Lane,
                    "Bot repeats its lane in consecutive rounds.");
                Check(MathF.Abs(DefusalBotTactics.CoverOffset(plan.ApproachAngle).Length() - 3) < .001f,
                    "Cover variation changed objective spacing.");
                previous[bot.Id] = plan;
            }
            int defendersAtA = roster.Count(bot => !bot.Attacking && tactics.PlanFor(bot.Id).Site == 0);
            Check(defendersAtA is 1 or 2, "Defenders no longer cover both sites.");
        }
        Check(sites.Count == 2, "Round plans never vary the attack site.");
        tactics.BeginRound([], 7);
        tactics.BeginRound([(-2000, true)], 8);
        Check(tactics.PlanFor(-2000).Site == tactics.AttackSite, "Roster replacement failed.");

        var spawn = new Vector3(0, 1, 28);
        var goal = new Vector3(-22, 0, -16);
        var waypoint = new Vector3(-20, 0, 3);
        var progress = new DefusalApproachProgress();
        Check(progress.Destination(spawn, goal, waypoint, 100, false) == waypoint, "Skipped the selected lane.");
        Check(progress.Destination(spawn, goal, waypoint, 101, false) == waypoint, "Route changed during approach.");
        Check(progress.Destination(waypoint + Vector3.UnitY * 5, goal, waypoint, 102, false) == goal,
            "Horizontal waypoint arrival did not advance to objective.");
        Check(progress.Destination(spawn, goal, waypoint, 103, false) == goal, "Completed route restarted after combat.");
        progress = new();
        Check(progress.Destination(spawn, goal, waypoint, 0, true) == goal && progress.Complete,
            "Dropped/planted bomb did not bypass lane.");
        progress = new();
        Check(progress.Destination(spawn, goal, null, 0, false) == goal, "Missing/unreachable marker blocked objective.");
        progress = new();
        progress.Destination(spawn, goal, waypoint, 0, false);
        Check(progress.Destination(spawn, goal, waypoint, 18, false) == goal, "Blocked lane never timed out.");
        progress = new();
        Check(progress.Destination(goal, goal, waypoint, 0, false) == goal, "Bot left a usable bombsite for a waypoint.");
        var scene = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenes", "Foundry.plutoscene"));
        foreach (var role in new[] { "Attack", "Defend" })
            foreach (var lane in new[] { "West", "Mid", "East" })
                Check(scene.Contains($"\tDefusal {role} {lane}\t"), "Foundry route marker missing.");
        Console.WriteLine("PASS: varied round lanes, coherent attacks, balanced defence, deterministic planning, waypoint arrival, urgency and bounded fallback.");
    }
}
