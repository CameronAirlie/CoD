using CoD.Scripts;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

// Reproduce sustained demand from the first bot: all five must still get slots.
var planner = new FairBotPlanner();
int[] bots = [-1000, -1001, -1002, -1003, -1004];
var counts = bots.ToDictionary(id => id, _ => 0);
for (var frame = 0; frame < 100; frame++)
{
    planner.BeginFrame(bots, 1, _ => true);
    Require(bots.Count(planner.CanPlan) == 1, "Planning exceeded its frame budget.");
    foreach (var id in bots)
        if (planner.CanPlan(id)) counts[id]++;
}
Require(counts.Values.All(count => count == 20), "A bot was starved by stable iteration order.");

// Dead bots cannot spend slots; multiple slots remain fair across wraparound.
counts = bots.ToDictionary(id => id, _ => 0);
for (var frame = 0; frame < 20; frame++)
{
    planner.BeginFrame(bots, 2, id => id != -1002);
    Require(!planner.CanPlan(-1002), "Dead bot received a planning slot.");
    Require(bots.Count(planner.CanPlan) == 2, "Eligible bots did not receive their budget.");
    foreach (var id in bots)
        if (planner.CanPlan(id)) counts[id]++;
}
Require(counts.Where(pair => pair.Key != -1002).All(pair => pair.Value == 10), "Multi-slot planning was unfair.");

// Disconnects, additions, empty rosters and invalid inspector budgets are safe.
planner.BeginFrame([], 1, _ => true);
Require(bots.All(id => !planner.CanPlan(id)), "Empty roster retained stale grants.");
planner.BeginFrame([42], 0, _ => true);
Require(planner.CanPlan(42), "Zero budget left the only bot permanently idle.");
planner.BeginFrame([42, 43], int.MaxValue, _ => true);
Require(planner.CanPlan(42) && planner.CanPlan(43), "Large budget was not clamped to the roster.");
planner.BeginFrame([43], 1, _ => false);
Require(!planner.CanPlan(42) && !planner.CanPlan(43), "Roster changes retained stale grants.");
Console.WriteLine("PASS: sustained-demand fairness, frame budget, dead bots, multi-slot wraparound, roster changes, empty roster, budget bounds.");

// A missing or stale navigation bake must not block every authored spawn.
var marker = new System.Numerics.Vector3(18, 1, -18);
var ground = new System.Numerics.Vector3(18, 2, -18);
Require(BotSpawnResolver.TryResolve(marker, _ => null, _ => ground, _ => true, out var fallback)
    && fallback == ground, "Missing navmesh prevented a grounded marker spawn.");
var projected = new System.Numerics.Vector3(17, 0, -17);
Require(BotSpawnResolver.TryResolve(marker, _ => projected, point => point, _ => true, out var preferred)
    && preferred == projected, "Valid navigation projection was not preferred.");
Require(!BotSpawnResolver.TryResolve(marker, _ => null, _ => null, _ => true, out _),
    "Spawn accepted a marker without a ground surface.");
Require(!BotSpawnResolver.TryResolve(marker, _ => projected, point => point, _ => false, out _),
    "Fallback bypassed occupied-spawn checks.");
Require(BotSpawnResolver.TryResolve(marker, _ => projected,
    point => point == marker ? ground : null, _ => true, out var stale)
    && stale == ground, "Stale projected floor prevented the authored fallback.");
Console.WriteLine("PASS: missing navigation bake, preferred projection, missing ground, occupied marker, stale projected floor.");

var movement = new BotCombatMovement();
Require(movement.CanHold(0), "Fresh bot cannot engage.");
movement.BeginHold(10, 2.5f);
Require(!movement.HoldExpired(12.49f) && movement.HoldExpired(12.5f),
    "Visible target can hold a bot stationary indefinitely.");
movement.BeginReposition(12.5f, 1.25f);
Require(!movement.CanHold(12.51f) && !movement.CanHold(13.74f) && movement.CanHold(13.75f),
    "Visible target immediately cancelled the reposition window.");
movement.BeginHold(13.75f, 2.5f);
Require(movement.HoldExpired(16.25f), "Second firing hold never expires.");
movement.BeginReposition(16.25f, -1);
Require(!movement.CanHold(16.25f) && movement.CanHold(16.5f), "Invalid duration bypassed movement.");
movement.Reset();
Require(movement.CanHold(0) && !movement.HoldExpired(100), "Round reset retained combat timers.");
Console.WriteLine("PASS: bounded firing holds, guaranteed reposition window, repeat cycles, duration bounds, round reset.");

Require(TeamNameplate.Format("Operator", true) == "FRIENDLY | Operator", "Friendly label is ambiguous.");
Require(TeamNameplate.Format("Operator", false) == "ENEMY | Operator", "Enemy label is ambiguous.");
Require(TeamNameplate.Format("<b>A&B</b>", true) == "FRIENDLY | &lt;b&gt;A&amp;B&lt;/b&gt;",
    "Player name injected nameplate markup.");
Console.WriteLine("PASS: friendly/enemy identification and escaped player names.");

// Asset-level regression: textured World Space RML is not rendered by Vulkan.
foreach (var prefab in new[] { "Enemy", "RemotePlayer" })
{
    var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Prefabs", prefab + ".plutoprefab"));
    var nameplate = source[source.IndexOf("Friendly Nameplate", StringComparison.Ordinal)..];
    Require(nameplate.Contains("PROPERTY\tRenderMode\t6\t1\t4\t"),
        prefab + " nameplate uses a canvas mode unsupported by Vulkan.");
    Require(nameplate.Contains("PROPERTY\tScaleMode\t6\t0\t3\t"),
        prefab + " nameplate cannot maintain its authored pixel size.");
    Require(nameplate.Contains("PROPERTY\tDocumentPath\t2\tUI/friendly-nameplate.rml\t0"),
        prefab + " nameplate lost its RML document binding.");
}
Console.WriteLine("PASS: both nameplate prefabs use Vulkan-compatible projected canvases and fixed pixel sizing.");
