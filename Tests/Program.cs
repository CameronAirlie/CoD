using CoD.Scripts;

HandSystemTests.Run();
WeaponLoadoutTests.Run();
GrenadeTests.Run();
TacticalUiTests.Run();
DefusalBotTacticsTests.Run();

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

Require(TeamNameplate.Format("Operator") == "Operator", "Nameplate should contain only the player name.");
Require(TeamNameplate.Format("<b>A&B</b>") == "&lt;b&gt;A&amp;B&lt;/b&gt;",
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

// Objective scoring comes from living occupants, not eliminations.
var sites = new[] { new HardpointSite("Centre", System.Numerics.Vector3.Zero),
    new HardpointSite("East", new System.Numerics.Vector3(15, 0, 0)) };
var rules = new HardpointRules(sites, 45, 5);
ObjectiveParticipant[] alpha = [new(PlayerTeam.Alpha, new(0, 1.5f, 0), true)];
rules.Tick(.5f, alpha); rules.Tick(.5f, alpha);
Require(rules.AlphaScore == 1 && rules.Owner == PlayerTeam.Alpha, "Solo occupant failed to score one point per second.");
ObjectiveParticipant[] contest = [alpha[0], new(PlayerTeam.Bravo, new(4.9f, 1, 0), true)];
rules.Tick(2, contest);
Require(rules.AlphaScore == 1 && rules.BravoScore == 0 && rules.Contested && rules.Owner is null,
    "Contested zone awarded points.");
rules.Tick(1, [new(PlayerTeam.Bravo, new(0, 1, 0), false)]);
Require(!rules.Contested && rules.Owner is null && rules.BravoScore == 0, "Dead player occupied a zone.");
Require(rules.Contains(new(5, 2, 0)) && !rules.Contains(new(5.01f, 2, 0)) && !rules.Contains(new(0, 4, 0)),
    "Capture cylinder boundaries are incorrect.");
rules.Reset(); rules.Tick(46, alpha);
Require(rules.Index == 1 && rules.AlphaScore == 45 && MathF.Abs(rules.SecondsRemaining - 44) < .001f && rules.Owner is null,
    "A long frame failed to rotate or scored occupants of the previous zone.");
var sliced = new HardpointRules(sites);
for (var i = 0; i < 2760; i++) sliced.Tick(1f / 60, alpha);
Require(sliced.AlphaScore == rules.AlphaScore && sliced.Index == rules.Index && MathF.Abs(sliced.SecondsRemaining - rules.SecondsRemaining) < .01f,
    "Scoring or rotations depend on frame rate.");
rules.Tick(float.NaN, alpha); rules.Tick(float.PositiveInfinity, alpha); rules.Tick(-1, alpha);
Require(rules.Index == 1 && rules.AlphaScore == 45, "Invalid time changed objective state.");
rules.Reset();
Require(rules.AlphaScore == 0 && rules.BravoScore == 0 && rules.Index == 0 && rules.Owner is null && rules.SecondsRemaining == 45,
    "Rematch retained objective state.");
rules.Tick(.9f, alpha); rules.Tick(.1f, []); rules.Tick(.2f, alpha);
Require(rules.AlphaScore == 0, "Fractional capture time leaked across a loss of control.");
var json = System.Text.Json.JsonSerializer.Serialize(rules.Snapshot());
var roundTrip = System.Text.Json.JsonSerializer.Deserialize<HardpointSnapshot>(json);
Require(roundTrip is not null && roundTrip.Position == rules.Site.Position && roundTrip.Radius == 5,
    "Network snapshot lost the objective coordinates.");
Console.WriteLine("PASS: capture/contest/death, cylinder boundaries, rotation overshoot, frame-rate invariance, rematch, invalid input and wire roundtrip.");

foreach (var loadout in Enum.GetValues<CombatLoadout>())
{
    var supplies = InventoryRules.Loadout(loadout);
    Require(InventoryRules.Slots(supplies.Ammo, supplies.Kits, supplies.Plates) == InventoryRules.Capacity,
        "A loadout exceeds or wastes its starting carrying capacity.");
    Require(InventoryRules.AddAmmo(supplies.Ammo, supplies.Kits, supplies.Plates, 30) == 0 &&
        InventoryRules.AddItems(supplies.Ammo, supplies.Kits, supplies.Plates, 1, true) == 0 &&
        InventoryRules.AddItems(supplies.Ammo, supplies.Kits, supplies.Plates, 1, false) == 0,
        "A full inventory accepted supplies.");
}
Require(InventoryRules.AddAmmo(149, 1, 1, 100) == 1, "Partial ammo stack could not fill without spending another slot.");
Require(InventoryRules.AddAmmo(0, 0, 0, int.MaxValue) == 180, "Ammo maximum was bypassed.");
Require(InventoryRules.AddItems(90, 1, 1, 10, true) == 1 && InventoryRules.AddItems(0, 0, 0, 10, false) == 3,
    "Kit slot costs or plate item limit were bypassed.");
Require(InventoryRules.AddAmmo(0, 0, 0, -1) == 0, "Negative pickup removed ammo.");
Console.WriteLine("PASS: balanced loadouts, capacity rejection, partial stacks, item maxima and bounded pickups.");

var objectiveSnapshot = rules.Snapshot() with { Owner = null, Contested = false };
var goals = new List<System.Numerics.Vector3>();
foreach (var id in bots)
{
    var goal = BotObjectiveTactics.Destination(id, objectiveSnapshot, PlayerTeam.Alpha, new(12, 1, 0), false, 0);
    goals.Add(goal);
    Require(rules.Contains(goal), "An attacking bot chose a destination outside capture range.");
}
Require(goals.Distinct().Count() == bots.Length, "Bots all occupy the same tactical point.");
var variedGoal = BotObjectiveTactics.Destination(bots[0], objectiveSnapshot, PlayerTeam.Alpha,
    new(12, 1, 0), false, 0, MathF.PI / 2);
Require(rules.Contains(variedGoal) && System.Numerics.Vector3.Distance(goals[0], variedGoal) > .1f,
    "Per-life approach variation must change the route while preserving capture range.");
var flankerId = bots.First(id => BotObjectiveTactics.Role(id) == BotRole.Flanker);
var flank = BotObjectiveTactics.Destination(flankerId, objectiveSnapshot with { Owner = PlayerTeam.Alpha }, PlayerTeam.Alpha, new(12, 1, 0), false, 0);
Require(!rules.Contains(flank), "Flanker failed to screen an owned zone.");
var retreat = BotObjectiveTactics.Destination(bots[0], objectiveSnapshot, PlayerTeam.Alpha, System.Numerics.Vector3.Zero, true, 0);
Require(float.IsFinite(retreat.X) && !rules.Contains(retreat) && retreat.Length() <= objectiveSnapshot.Radius + 5.01f,
    "Retreat from the centre produced an invalid or unbounded destination.");
Require(MathF.Abs(CombatFeedbackMath.Bearing(System.Numerics.Vector3.UnitZ, System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitX) - 90) < .001f,
    "Incoming right-side damage points left.");
var earned = CombatFeedbackMath.RoundExperience(new(0, "Player", PlayerTeam.Alpha, 3, 2, false, 20), true);
Require(earned == 250, "Round reward omitted objective time, kills or victory.");
Require(CombatFeedbackMath.RoundExperience(new(0, "Player", PlayerTeam.Alpha, -1, 0, false, float.NaN), false) == 50,
    "Invalid result poisoned progression.");
Console.WriteLine("PASS: objective roles, separated capture goals, friendly screening, bounded retreat, damage bearing and objective rewards.");

var chest = CombatTargeting.AimPoint(System.Numerics.Vector3.Zero, new(0, -.25f, 0), 1.5f);
Require(chest.Y < .5f && chest.Y > -1, "AI aimed outside the enemy capsule.");
var defensiveStep = CombatTargeting.CapturePosition(new(20, 1, 0), objectiveSnapshot);
Require(rules.Contains(defensiveStep), "A defender left the objective while repositioning.");
Console.WriteLine("PASS: collision-volume aiming and capture-preserving defensive movement.");

movement.Reset();
Require(movement.WantsRetreat(10, .2f, false), "Wounded bot did not take a short retreat.");
Require(!movement.WantsRetreat(13, .2f, false) && !movement.WantsRetreat(17, .2f, false), "Wounded bot runs forever without firing.");
Require(movement.WantsRetreat(14, 1, true), "Reloading bot ignored retreat.");
var damageFeedback = new DamageFeedbackState();
damageFeedback.Hit();
Require(damageFeedback.Opacity(1, .85f) > .8f, "Armour-absorbed hit produced no damage overlay.");
damageFeedback.Update(.2f);
Require(damageFeedback.Pulse > 0 && damageFeedback.Pulse < 1, "Hit flash did not fade.");
damageFeedback.Hit(); damageFeedback.Update(1);
Require(damageFeedback.Opacity(1, 1) == 0 && damageFeedback.Opacity(.2f, 1) > .4f, "Flash or low-health persistence is incorrect.");
var neutralStatus = ObjectiveStatus.Describe(objectiveSnapshot with { Owner = null }, PlayerTeam.Alpha, new(0, 1, 0), true);
Require(neutralStatus.Inside && neutralStatus.Style == "neutral", "Capture status lost player's zone occupancy.");
Require(ObjectiveStatus.Describe(objectiveSnapshot with { Owner = PlayerTeam.Alpha }, PlayerTeam.Alpha, new(0,1,0), true).Style == "friendly", "Friendly ownership is unclear.");
Require(ObjectiveStatus.Describe(objectiveSnapshot with { Owner = PlayerTeam.Bravo }, PlayerTeam.Alpha, new(0,1,0), true).Style == "enemy", "Enemy ownership is unclear.");
Require(ObjectiveStatus.Describe(objectiveSnapshot with { Contested = true }, PlayerTeam.Alpha, new(0,1,0), true).Style == "contested", "Contest did not override ownership.");
Require(!ObjectiveStatus.Describe(objectiveSnapshot, PlayerTeam.Alpha, new(0,1,0), false).Inside, "Dead player appears to occupy the objective.");
Console.WriteLine("PASS: bounded retreats, hit/armour flashes, fading, low-health feedback, objective ownership and occupancy.");

// Bomb defusal rules run without scene, networking or renderer dependencies.
DefusalRules NewBomb(float duration = 90, float fuse = 35) => new(1, PlayerTeam.Alpha, 10, new(0,1,0), [new(0,0,0), new(20,0,0)], duration, fuse);
DefusalAgent Attacker(bool use = true) => new(10, PlayerTeam.Alpha, new(0,1,0), true, use);
DefusalAgent Defender(bool use = false) => new(20, PlayerTeam.Bravo, new(0,1,0), true, use);
var bombRules = NewBomb();
bombRules.Tick(2, [Attacker(), Defender()]);
Require(bombRules.Bomb == BombState.Carried && bombRules.Snapshot().Progress > .6f, "Plant completed too early.");
bombRules.Tick(.1f, [Attacker(false), Defender()]);
Require(bombRules.Snapshot().Progress == 0, "Releasing use did not interrupt plant.");
bombRules.Tick(3, [Attacker(), Defender()]);
Require(bombRules.Bomb == BombState.Planted && bombRules.Site == 0 && bombRules.Remaining == 35, "Plant failed or incorrectly consumed fuse time.");
bombRules.Tick(4, [Attacker(), Defender(true)]);
Require(bombRules.Winner is null, "Defuse completed too soon.");
bombRules.Tick(1, [Attacker(), Defender(true)]);
Require(bombRules.Winner == PlayerTeam.Bravo && bombRules.Bomb == BombState.Defused, "Defenders could not defuse.");
var droppedBomb = NewBomb();
droppedBomb.Tick(.1f, [Attacker(false), Defender()]);
droppedBomb.Tick(.1f, [Attacker() with { Alive = false }, Defender(), new(11, PlayerTeam.Alpha, new(10,1,0), true, false)]);
Require(droppedBomb.Bomb == BombState.Dropped && droppedBomb.Carrier is null, "Carrier death did not drop the bomb.");
droppedBomb.Tick(.1f, [Defender(true), new(11, PlayerTeam.Alpha, new(0,1,0), true, false)]);
Require(droppedBomb.Carrier == 11 && droppedBomb.Bomb == BombState.Carried, "Dropped bomb could not be recovered by an attacker.");
var postPlant = NewBomb(); postPlant.Tick(3, [Attacker(), Defender()]);
postPlant.Tick(1, [Defender()]);
Require(postPlant.Winner is null, "Dead attackers ended an active bomb fuse.");
postPlant.Tick(34, [Defender()]);
Require(postPlant.Winner == PlayerTeam.Alpha && postPlant.Bomb == BombState.Exploded, "Bomb did not detonate after attacker elimination.");
var eliminated = NewBomb(); eliminated.Tick(.1f, [Defender()]);
Require(eliminated.Winner == PlayerTeam.Bravo, "Pre-plant elimination did not award defenders.");
var defended = NewBomb(); defended.Tick(.1f, [Attacker(false)]);
Require(defended.Winner == PlayerTeam.Alpha, "Defender elimination did not award attackers.");
var expired = NewBomb(10); expired.Tick(10, [Attacker(false), Defender()]);
Require(expired.Winner == PlayerTeam.Bravo && expired.Reason == "Time expired", "Round timeout failed.");
var wrongTeam = NewBomb(); wrongTeam.Tick(4, [Attacker(false), Defender(true)]);
Require(wrongTeam.Bomb == BombState.Carried, "Defender planted the bomb.");
var vertical = NewBomb(); vertical.Tick(4, [Attacker() with { Position = new(0,8,0) }, Defender()]);
Require(vertical.Bomb == BombState.Carried, "Plant worked from another floor.");
var tie = NewBomb(90,10); tie.Tick(3, [Attacker(), Defender()]); tie.Tick(5, [Attacker(false), Defender()]); tie.Tick(5, [Attacker(false), Defender(true)]);
Require(tie.Bomb == BombState.Exploded, "Defuse incorrectly won at the detonation deadline.");
var bigStep = NewBomb(); bigStep.Tick(4, [Attacker(), Defender()]);
Require(bigStep.Bomb == BombState.Planted && MathF.Abs(bigStep.Remaining - 34) < .001f, "Plant transition depends on frame size.");
var smallSteps = NewBomb(); for (var frame=0;frame<400;frame++) smallSteps.Tick(.01f, [Attacker(), Defender()]);
Require(MathF.Abs(smallSteps.Remaining - bigStep.Remaining) < .005f, "Small and large timesteps disagree.");
var serializedBomb = System.Text.Json.JsonSerializer.Deserialize<DefusalSnapshot>(System.Text.Json.JsonSerializer.Serialize(bigStep.Snapshot()));
Require(serializedBomb?.Position == bigStep.Position && serializedBomb.Bomb == BombState.Planted, "Bomb snapshot did not round-trip through transport.");
Console.WriteLine("PASS: defusal plant/interruption, recovery, team permissions, elimination, fuse, defuse, deadline ties, frame independence and wire format.");

Require(DefusalRules.AttackersForRound(6) == PlayerTeam.Alpha && DefusalRules.AttackersForRound(7) == PlayerTeam.Bravo, "Halftime roles changed on the wrong round.");
