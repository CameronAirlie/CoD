using CoD.Scripts;
using System.Numerics;

if (args.Length > 0)
{
    try { if (args.Length == 3 && args[2] == "--defusal-movement") await DefusalMovementIntegration.Run(args); else if (args.Length == 3 && args[2] == "--bots") await BotIntegration.Run(args); else await ServerIntegration.Run(args); }
    catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    return;
}

MovementPresentationTests.Run();
DefusalRoundPresentationTests.Run();

static void Check(bool condition, string message)
{ if (!condition) throw new InvalidOperationException(message); }
var input = new ServerInput();
var command = PlayerCommand.Idle with { Sequence = 2, MoveY = 1, Jump = true, Reload = true };
Check(input.Accept(command, 0), "Valid input rejected");
Check(!input.Accept(command, .01f), "Replay accepted");
Check(!input.Accept(command with { Sequence = 1 }, .01f), "Reordered input accepted");
Check(!input.Accept(command with { Sequence = 3, Yaw = float.NaN }, .01f), "NaN accepted");
Check(!input.Accept(command with { Sequence = 3, MoveX = 2 }, .01f), "Unbounded axes accepted");
Check(!input.Accept(command with { Sequence = 3, Slot = 99 }, .01f), "Unknown weapon slot accepted");
Check(!input.Accept(command with { Sequence = 3, Loadout = (CombatLoadout)999 }, .01f), "Unknown loadout accepted");
Check(input.Accept(command with { Sequence = 3, Jump = false, Reload = false }, .01f), "Held input rejected");
var consumed = input.Consume(.02f);
Check(consumed.Jump && consumed.Reload, "Edges lost between server ticks");
Check(!input.Consume(.03f).Jump, "Jump edge replayed");
Check(input.Consume(1).MoveY == 0, "Stale input kept moving");
Console.WriteLine("PASS: validation, replay rejection, latched edges and input timeout");

var motor = new ServerMotor();
var moved = Vector3.Zero;
Vector3 Move(Vector3 displacement) { moved += displacement; return displacement; }
for (var i = 0; i < 60; i++) motor.Tick(command with { Jump = false }, 1f / 60, true, Move, false);
Check(-moved.Z > 4.5f && -moved.Z <= 5.4f, "Server movement budget failed");
Check(MathF.Abs(moved.X) < .001f, "Movement direction changed");
var before = moved;
for (var i = 0; i < 10000; i++) input.Accept(command with { Sequence = 4 + i }, 1);
Check(moved == before, "Packet flood advanced simulation");
motor.Tick(command, float.NaN, true, Move, false);
Check(moved == before, "Invalid timestep advanced simulation");
var diagonal = new ServerMotor(); var diagonalDistance = Vector3.Zero;
for (var i = 0; i < 60; i++) diagonal.Tick(command with { MoveX = 1, Jump = false }, 1f / 60, true,
    d => { diagonalDistance += d; return d; }, false);
Check(new Vector2(diagonalDistance.X, diagonalDistance.Z).Length() <= 5.4f, "Diagonal speed exploit");
Console.WriteLine("PASS: server-time movement, packet flood isolation, invalid timestep and diagonal budget");

var equipment = new ServerEquipment(); equipment.Reset(CombatLoadout.Assault);
Check(equipment.TryFire(true, 0, true), "First shot rejected");
Check(!equipment.TryFire(true, 0, true), "Duplicate tick bypassed fire rate");
for (var i = 1; i < 30; i++) Check(equipment.TryFire(true, i, true), "Magazine depleted early");
Check(!equipment.TryFire(true, 30, true), "Empty magazine fired");
var reserve = equipment.Reserve;
equipment.Tick(PlayerCommand.Idle with { Reload = true }, 31, true);
Check(equipment.Reloading && !equipment.TryFire(true, 32, true), "Reload did not block firing");
equipment.Tick(PlayerCommand.Idle, 33.2f, true);
Check(equipment.Weapons.Magazine == 30 && equipment.Reserve == reserve - 30, "Reload did not conserve ammunition");
equipment.Tick(PlayerCommand.Idle with { Slot = 1 }, 34, true);
Check(!equipment.TryFire(true, 34, true), "Weapon switch bypassed draw time");
equipment.TryFire(false, 35, true);
Check(equipment.TryFire(true, 35, true), "Pistol trigger rejected");
Check(!equipment.TryFire(true, 36, true), "Pistol held trigger fired twice");
equipment.Tick(PlayerCommand.Idle with { UseItem = 1, Slot = 1 }, 37, true);
Check(equipment.UsingItem == 1, "Kit use failed");
equipment.CancelUse();
Check(equipment.Tick(PlayerCommand.Idle with { Slot = 1 }, 39, true) == 0, "Cancelled kit healed");
Check(!equipment.TryFire(true, 40, false), "Dead/inactive player fired");
Console.WriteLine("PASS: ammunition, cadence, reload conservation, draw delay, semi-auto and cancelled use");

Check(MultiplayerLaunch.Configure("Client", "Test", "127.0.0.1:27777") && MultiplayerLaunch.ServerPort == 27777, "Custom server port not parsed");
Check(MultiplayerLaunch.Configure("Client", "Test", "localhost") && MultiplayerLaunch.ServerPort == 7777, "Default server port changed");
Check(MultiplayerLaunch.Configure("Client", "Test", "[::1]:27777") && MultiplayerLaunch.ServerAddress == "::1", "IPv6 server address not parsed");
Check(!MultiplayerLaunch.Configure("Client", "Test", "localhost:0"), "Invalid server port accepted");
Check(!MultiplayerLaunch.Configure("Client", "Test", "localhost:999999"), "Out-of-range server port accepted");
Check(!MultiplayerLaunch.Configure("Client", "Test", "localhost/path"), "Unexpected server path accepted");
Console.WriteLine("PASS: server endpoint parsing, custom/default ports and IPv6");


// Simulate 200 ms of unacknowledged input, then reconcile to an older server tick.
var prediction = new MovementPrediction();
var serverMotor = new ServerMotor();
var clientPosition = Vector3.Zero; var serverPosition = Vector3.Zero;
void PredictStep(ServerMotor m, PlayerCommand c) => m.Tick(c, ServerMotor.Step, true,
    d => { clientPosition += d; return d; }, false);
MotorState? acknowledgedState = null; var acknowledgedPosition = Vector3.Zero;
for (var tick = 1; tick <= 30; tick++)
{
    var c = PlayerCommand.Idle with { Sequence = tick, MoveY = tick <= 22 ? 1 : 0, Sprint = true };
    Check(prediction.Predict(c, PredictStep), "Prediction rejected bounded history");
    if (tick <= 18)
    {
        serverMotor.Tick(c, ServerMotor.Step, true, d => { serverPosition += d; return d; }, false);
        acknowledgedState = serverMotor.Capture(); acknowledgedPosition = serverPosition;
    }
}
var predictedPosition = clientPosition;
Check(predictedPosition.Length() > serverPosition.Length(), "Movement waited for acknowledgement");
clientPosition = acknowledgedPosition;
prediction.Reconcile(18, acknowledgedState!, PredictStep);
Check(Vector3.Distance(clientPosition, predictedPosition) < .0001f, "Delayed acknowledgement caused rubber banding");
Check(prediction.PendingCount == 12, "Acknowledgement removed unprocessed commands");
// Server corrections become the new base, with only unacknowledged movement replayed.
clientPosition = acknowledgedPosition + Vector3.UnitX;
prediction.Reconcile(18, acknowledgedState!, PredictStep);
Check(Vector3.Distance(clientPosition, predictedPosition + Vector3.UnitX) < .0001f, "Server correction was ignored");
prediction.Clear();
Check(prediction.PendingCount == 0 && prediction.Motor.Velocity == Vector3.Zero, "Old life prediction survived death/respawn");
for (var tick = 1; tick <= 120; tick++) Check(prediction.Predict(PlayerCommand.Idle with { Sequence = tick }, PredictStep), "History capacity incorrect");
Check(!prediction.Predict(PlayerCommand.Idle with { Sequence = 121 }, PredictStep), "Prediction history grew without bound");
var commands = new MovementCommands();
Check(commands.Accept(PlayerCommand.Idle with { Sequence = 1, Jump = true }), "Queued command rejected");
Check(commands.Accept(PlayerCommand.Idle with { Sequence = 2 }), "Second queued command rejected");
Check(commands.LastProcessed == 0, "Receipt incorrectly acknowledged simulation");
Check(commands.Consume()!.Jump && commands.LastProcessed == 1, "Queued jump was overwritten by later held input");
Check(!commands.Accept(PlayerCommand.Idle), "Replay entered movement queue");
commands.Clear();
Check(commands.Consume() is null, "Old life movement survived respawn");
var slidingMotor = new ServerMotor();
for (var tick = 0; tick < 30; tick++) slidingMotor.Tick(PlayerCommand.Idle with { MoveY = 1, Sprint = true }, ServerMotor.Step, true, d => d, false);
slidingMotor.Tick(PlayerCommand.Idle with { MoveY = 1, Crouch = true }, ServerMotor.Step, true, d => d, false);
var slideState = slidingMotor.Capture(); var restored = new ServerMotor(); restored.Restore(slideState);
Check(slideState.Sliding, "Slide setup failed");
for (var tick = 0; tick < 10; tick++)
{
    slidingMotor.Tick(PlayerCommand.Idle with { Crouch = true }, ServerMotor.Step, true, d => d, false);
    restored.Tick(PlayerCommand.Idle with { Crouch = true }, ServerMotor.Step, true, d => d, false);
}
Check(slidingMotor.Capture() == restored.Capture(), "Slide timer/edge state was not restored for replay");
Console.WriteLine("PASS: immediate prediction, delayed ACK replay, authoritative correction, bounded history, life reset, processed ACKs and slide restoration");

var floodQueue = new MovementCommands();
for (var sequence = 1; sequence <= 120; sequence++) Check(floodQueue.Accept(PlayerCommand.Idle with { Sequence = sequence }), "Movement queue capacity incorrect");
Check(!floodQueue.Accept(PlayerCommand.Idle with { Sequence = 121 }), "Packet flood exceeded movement queue bound");
Check(floodQueue.LastProcessed == 0, "Packet flood advanced movement acknowledgement");
var airborne = new ServerMotor(); var airbornePosition = Vector3.Zero;
airborne.Tick(PlayerCommand.Idle with { Jump = true, MoveY = 1 }, ServerMotor.Step, true,
    d => { airbornePosition += d; return d; }, false);
var airState = airborne.Capture(); var replayAir = new ServerMotor(); replayAir.Restore(airState);
var replayPosition = airbornePosition;
for (var tick = 0; tick < 15; tick++)
{
    airborne.Tick(PlayerCommand.Idle with { MoveX = 1 }, ServerMotor.Step, false,
        d => { airbornePosition += d; return d; }, false);
    replayAir.Tick(PlayerCommand.Idle with { MoveX = 1 }, ServerMotor.Step, false,
        d => { replayPosition += d; return d; }, false);
}
Check(Vector3.Distance(airbornePosition, replayPosition) < .0001f && airborne.Capture() == replayAir.Capture(), "Jump/air movement diverged after reconciliation");
Console.WriteLine("PASS: queue flood bound and airborne reconciliation");

var takeoffMotor = new ServerMotor();
takeoffMotor.Tick(PlayerCommand.Idle with { Jump = true }, ServerMotor.Step, true, d => d, false);
var takeoffVelocity = takeoffMotor.Velocity.Y;
Check(takeoffVelocity > 0 && !takeoffMotor.Grounded, "Grounded jump was rejected");
takeoffMotor.Tick(PlayerCommand.Idle with { Jump = true, Sprint = true, MoveY = 1 }, ServerMotor.Step, true, d => d, false);
Check(!takeoffMotor.Grounded && !takeoffMotor.Sprinting && takeoffMotor.Velocity.Y < takeoffVelocity,
    "Near-floor probe reset airborne state or applied a second jump impulse");
Console.WriteLine("PASS: takeoff stays airborne while support probe can still see the floor");

var floorMotor = new ServerMotor(); var floorTravel = Vector3.Zero;
Vector3 FloorSweep(Vector3 displacement)
{
    Check(displacement.Y == 0 || displacement.X == 0 && displacement.Z == 0,
        "Ground travel and gravity were combined into a skin-consuming sweep");
    var applied = displacement.Y < 0 ? Vector3.Zero : displacement;
    floorTravel += applied; return applied;
}
for (var tick = 0; tick < 60; tick++) floorMotor.Tick(PlayerCommand.Idle with { MoveY = 1 }, ServerMotor.Step, true, FloorSweep, false);
Check(-floorTravel.Z > 4.9f && -floorTravel.Z <= 5.4f, "Floor contact reduced full walking speed");
Console.WriteLine("PASS: floor support does not consume horizontal movement distance");

var sprintJump = new ServerMotor();
var sprintInput = PlayerCommand.Idle with { MoveY = 1, Sprint = true };
for (var tick = 0; tick < 60; tick++) sprintJump.Tick(sprintInput, ServerMotor.Step, true, d => d, false);
sprintJump.Tick(sprintInput with { Jump = true }, ServerMotor.Step, true, d => d, false);
var takeoffSpeed = new Vector2(sprintJump.Velocity.X, sprintJump.Velocity.Z).Length();
Check(MathF.Abs(takeoffSpeed - 8) < .0001f, "Sprint jump did not start at sprint speed.");
var restoredJump = new ServerMotor(); restoredJump.Restore(sprintJump.Capture());
for (var tick = 0; tick < 30; tick++)
{
    var airInput = sprintInput with { Sprint = false, Yaw = tick * 3 };
    sprintJump.Tick(airInput, ServerMotor.Step, false, d => d, false);
    restoredJump.Tick(airInput, ServerMotor.Step, false, d => d, false);
    Check(MathF.Abs(new Vector2(sprintJump.Velocity.X, sprintJump.Velocity.Z).Length() - takeoffSpeed) < .0001f,
        "Sprinting momentum was lost or increased while steering in the air.");
}
Check(sprintJump.Capture() == restoredJump.Capture(), "Sprint momentum diverged during reconciliation replay.");
var carried = sprintJump.Velocity;
sprintJump.Tick(PlayerCommand.Idle, ServerMotor.Step, false, d => d, false);
Check(new Vector2(sprintJump.Velocity.X, sprintJump.Velocity.Z) == new Vector2(carried.X, carried.Z),
    "Releasing movement midair removed takeoff momentum.");
sprintJump.Tick(PlayerCommand.Idle, ServerMotor.Step, true, d => d, false);
Check(new Vector2(sprintJump.Velocity.X, sprintJump.Velocity.Z).Length() < takeoffSpeed,
    "Ground braking did not resume on landing.");
var standingJump = new ServerMotor();
standingJump.Tick(PlayerCommand.Idle with { Jump = true }, ServerMotor.Step, true, d => d, false);
for (var tick = 0; tick < 60; tick++) standingJump.Tick(sprintInput, ServerMotor.Step, false, d => d, false);
Check(new Vector2(standingJump.Velocity.X, standingJump.Velocity.Z).Length() <= 5.4001f,
    "Airborne sprint input created sprint momentum without a sprinting takeoff.");
Console.WriteLine("PASS: sprint-jump momentum, air steering/coasting, landing braking, replay and airborne speed cap");

var slideJump = new ServerMotor();
for (var tick = 0; tick < 60; tick++) slideJump.Tick(sprintInput, ServerMotor.Step, true, d => d, false);
slideJump.Tick(sprintInput with { Crouch = true }, ServerMotor.Step, true, d => d, false);
Check(slideJump.Sliding, "Sprint crouch did not enter slide.");
var slideSpeed = new Vector2(slideJump.Velocity.X, slideJump.Velocity.Z).Length();
slideJump.Tick(sprintInput with { Crouch = true, Jump = true }, ServerMotor.Step, true, d => d, false);
Check(!slideJump.Sliding && !slideJump.Grounded && slideJump.Velocity.Y > 0 && slideJump.Capture().SlideRemaining == 0,
    "Jump did not cancel slide and launch the player.");
var slideTakeoff = new Vector2(slideJump.Velocity.X, slideJump.Velocity.Z).Length();
Check(MathF.Abs(slideTakeoff - (slideSpeed - 8 * ServerMotor.Step)) < .0001f, "Slide jump lost horizontal momentum.");
var slideReplay = new ServerMotor(); slideReplay.Restore(slideJump.Capture());
for (var tick = 0; tick < 12; tick++)
{
    var slideAirInput = sprintInput with { Crouch = true };
    slideJump.Tick(slideAirInput, ServerMotor.Step, false, d => d, false);
    slideReplay.Tick(slideAirInput, ServerMotor.Step, false, d => d, false);
}
Check(slideJump.Capture() == slideReplay.Capture(), "Slide jump replay diverged.");
Check(MathF.Abs(new Vector2(slideJump.Velocity.X, slideJump.Velocity.Z).Length() - slideTakeoff) < .0001f,
    "Holding crouch in the air lost slide jump momentum.");
Console.WriteLine("PASS: slide jump launch, momentum, slide cancellation and reconciliation replay");

foreach (var heading in new[] { -179f, -135f, -91f, -90f, -45f, 0f, 45f, 90f, 91f, 135f, 179f, 180f })
{
    var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading * MathF.PI / 180);
    var transmittedYaw = CharacterHeading.Yaw(rotation);
    var displayed = Quaternion.CreateFromAxisAngle(Vector3.UnitY, transmittedYaw * MathF.PI / 180);
    var expectedForward = Vector3.Transform(-Vector3.UnitZ, rotation);
    var displayedForward = Vector3.Transform(-Vector3.UnitZ, displayed);
    Check(Vector3.Dot(expectedForward, displayedForward) > .99999f, "Network heading folded and displayed a bot facing away.");
}
Console.WriteLine("PASS: replicated bot facing across all quadrants and the 180-degree seam");
