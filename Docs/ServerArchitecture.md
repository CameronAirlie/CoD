# CoD authoritative multiplayer and dedicated hosting

## Phase 1: server authority

Clients submit bounded, sequenced input commands, never positions, health, damage,
ammunition counts, or elapsed simulation time. The server advances movement through
engine collision queries, owns spawn/respawn, weapons, reloads, inventory use, health,
and match rules. Each owner receives authoritative state; remote actors receive
presentation snapshots. Listen hosts use the same command path as remote clients.
Protocol versions reject older peers. Independent simulation rules have executable
tests for malformed/replayed input, movement budgets, and combat resource limits.

Owner movement uses client prediction and acknowledged-command replay with the same
60 Hz motor and capsule on both sides. Lag compensation remains future work. TCP transport
is retained for compatibility; changing transport is a separate project.

## Phase 2: dedicated hosting

A console PlutoGE host initializes assets, scripting, scene collision and navigation
without GLFW, a graphics device, or audio output. It runs a paced fixed timestep,
handles graceful termination, and loads the requested gameplay scene. CoD dedicated
mode owns no player ID 0 and reserves no local participant slot. Client presentation
scripts are disabled before scene activation; session and bot scripts keep running.
Use the same authoritative simulation and protocol as listen hosts.

## Acceptance

Build native hosts and managed assemblies together. Run simulation/transport tests,
headless startup/shutdown checks, then verify two clients joining, moving, shooting,
reloading, dying, respawning, switching teams and using objectives. A client opening
menus or disconnecting must not interrupt the server. Verify bots progress without
rendering. A live gameplay check remains necessary beyond isolated rule tests.

## Build and run

### From the editor

Open File > Project Settings and scroll to Dedicated Server. CoD is configured with
Main, 60 Hz, the simulation script allow list and defaults for port 7777, 12 players,
8 bots and Hardpoint. Save, then use File > Build Dedicated Server or Build and Run
Dedicated Server. Output is Build/DedicatedServer/CoDServer.exe and its adjacent
pack/dependencies. Run Dedicated Server reuses that build. Stop it with Ctrl+C.
For Defusal, select Foundry and set COD_SERVER_MODE to Defusal before building.

The exported executable needs no launcher or arguments. Deployment overrides work:

```powershell
./Build/DedicatedServer/CoDServer.exe --env COD_SERVER_PORT=7780 --env COD_SERVER_BOTS=4
```

The editor builds scripts and validates content before exporting. Game and server
share build inclusion settings; the source graphical startup scene stays Title.
Environment defaults are stored in the package; configure secrets externally.
Changing Project Settings affects the next build, not a server already running.

### Optional project launcher

From the CoD project in PowerShell:

```powershell
./Tools/BuildServer.ps1
cd Build/Server
./CoDServer.exe --map Main --mode Hardpoint --port 7777 --max-players 12 --bots 8
```

The package contains the console launcher, PlutoGEServer, native dependencies and
CoDGame.plutopack. Copy the whole directory to the server machine. It requires a
.NET 8 runtime, like the game. Ctrl+C shuts the server down. `--ticks N` runs a
finite number of simulation ticks for automation (zero runs continuously).

For development without exporting:

```powershell
dotnet run --project Server/CoD.Server.csproj -c Release -- --engine C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc-nvidia/runtime/RelWithDebInfo/PlutoGEServer.exe --project D:/PlutoProjects/CoD/CoDplutoproject.plutoproject --map Foundry --mode Defusal --bots 4
```

The launcher accepts --engine, --project (a source project or exported pack),
--map Main|Foundry, --mode Hardpoint|TeamDeathmatch|Defusal, --port, --max-players,
--bots and --ticks. The default map is Main, or Foundry for Defusal. Bots fill a
population target: --bots 8 starts eight bots with no humans, then replaces bots
as humans join. Total humans are limited by --max-players; --bots cannot exceed it.

Clients use JOIN with the server address, optionally including a port such as
127.0.0.1:7777 or [::1]:7777. The welcome message selects the server's match mode
and loads its map before reconnecting if needed. The current transport is TCP;
allow inbound traffic on the selected port. Client and server builds must both
use protocol 16; older builds are rejected and their sockets are closed.

## Boundaries and current limits

ServerSimulation contains native-independent command validation, edge buffering,
movement and equipment state. MultiplayerSession.Authority adapts these rules to
engine collision queries and networking. Commands are sequenced and tagged with
a life generation; stale commands cannot carry over into a respawn. Input is sent
at up to 60 Hz, snapshots at the session's configured rate (normally 20 Hz), and
heartbeats continue independently of local player input and death state.

The server owns collision-safe movement, stance, jump/slide/sprint, weapon choice,
magazines, reserve ammunition, timed reloads, shot origin/spread/recoil/cadence,
health/armour, inventory use, supply collection, grenades, objectives, scoring,
team selection and respawning. Clients retain view/input, animation, predicted
weapon effects, UI and remote interpolation. They cannot submit transforms or raw
shot results to the authoritative simulation. Inventory use is cancelled by damage.

Movement is predicted locally; lag-compensated hit rewind remains unimplemented. Server movement
uses shared multiplayer tuning rather than trusting client controller settings.
The headless executable links existing engine libraries but initializes no window,
graphics device or audio output. The package currently reuses cooked game content;
it is not yet a stripped collision/navigation-only content format. The script
allow list excludes first-person, HUD and menu scripts, including on spawned prefabs.

## Validation commands

```powershell
dotnet run --project Tests/Authority/CoD.AuthorityTests.csproj -c Release
dotnet run --project Tests/Authority/CoD.AuthorityTests.csproj -c Release -- C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc-nvidia/runtime/RelWithDebInfo/PlutoGEServer.exe D:/PlutoProjects/CoD/CoDplutoproject.plutoproject
```

The second command starts a real headless host and two transport clients in an
isolated test arena. It checks forged movement/shot rejection, movement bounds,
ammunition/reload conservation, damage, death, scoring, respawn and disconnect.
PlutoGEHeadlessServerTests checks graphics-free initialization, script filtering,
collision queries, packed-content materialization/cleanup and shutdown. Native
networking smoke tests cover targeted disconnection without stopping other peers.
Visual first-person presentation, map handoff and internet latency still warrant
interactive testing with two graphical clients.


## Owner prediction and life transitions (protocol 16)

The owner samples held input and latches jump/reload/use edges, produces commands
at 60 Hz, and immediately runs the shared ServerMotor against local collision.
Each transmitted command represents exactly one fixed movement step. The server
queues commands instead of overwriting them with the latest render input, and
acknowledges only commands it has simulated. A real-time movement budget capped
at 100 ms permits jitter catch-up without granting extra speed to packet floods.
Queues and client replay history are bounded to 120 commands. If history fills,
the client stops predicting/sending until acknowledgements free capacity.

On receipt, the owner restores authoritative position and complete motor state
(including slide timer and crouch edge state), discards acknowledged commands,
and replays only the remainder. Replay performs movement/collision and stance,
never shots, sound, damage, ammunition consumption or inventory use. Remote
players continue using snapshot interpolation. Listen hosts run movement directly
on the server without a second predicted simulation. Server health, resources,
collision and match rules remain authoritative; clients never submit positions.

Death clears prediction and freezes owner movement. A new server life clears
latched input and history, snaps to the authoritative spawn, restores facing and
exits the death camera once. PlayerHealth resolves the session lazily so script
creation order cannot enable offline healing/respawning in a multiplayer match.
The server clears queued commands at spawn and rejects commands from old lives.
Both client and server need rebuilding because protocol 16 adds motor replay state.

Design references: [Valve's latency compensation paper](https://developer.valvesoftware.com/w/index.php?title=Latency_Compensating_Methods_in_Client%2FServer_In-game_Protocol_Design_and_Optimization&uselang=en)
and [Gabriel Gambetta's prediction/reconciliation explanation](https://www.gabrielgambetta.com/client-side-prediction-server-reconciliation.html).
TCP remains the current transport; head-of-line stalls and dynamic-world collision
differences can still cause corrections. Internet-latency graphical testing is
required to assess visible correction quality; hit rewind and UDP transport are
separate follow-up work.


Ground support probes include the capsule half-height, probe origin offset,
collision skin and tolerance. Client and server use the same probe constants;
upward motor velocity suppresses grounding near takeoff. Real-server integration
checks standing support, jump/landing, and full walk/sprint speed, rather than
assuming a grounded flag in isolated motor tests.

Horizontal travel and vertical gravity use separate kinematic sweeps, preventing
floor contact and collision skin from consuming horizontal movement distance.


Player roots are capsule centers. Multiplayer camera and server shot origins
share root-relative eye heights (standing +0.5, crouching +0.0), rather than using
foot-relative 1.62/1.12 offsets above the root. The real-server damage regression
aims level between equal-height players so an origin above the target cannot
silently pass the test by aiming downward.
