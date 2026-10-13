# Economy and buying

Open **B** or **I** for the combined buy/inventory menu. Buy buttons purchase
catalog items; the existing weapon cards equip owned weapons. Newly purchased
weapons equip automatically. Carry two weapons: the pistol and one primary (AR-24 or MG-60). Buying another
primary replaces the previous primary. Every spawn starts with the pistol equipped.
Press 1 for the owned primary, 2 for the pistol, or Q to switch between them. Throwable buttons select utility, T cycles stocked types,
and G throws. J replaces the former G armour binding.

Each participant starts with $800, a P-12 pistol and two frag grenades. Cash is
session-local, capped at $16,000; it is not persisted between launches. Enemy
kills award $300, round wins award $3,250 and losses $1,400. Tied timed matches
award $3,250 to both teams. Currency survives death; purchased weapons are lost
on death, and throwable supplies reset to two frags on respawn/new round.
Survivors retain purchased weapon ownership between rounds.

| Item | Price |
| --- | ---: |
| AR-24 | $2,700 |
| MG-60 | $3,200 |
| Frag | $300 |
| Flashbang | $200 |
| Smoke | $300 |
| Molotov | $400 |

Buy during warmup or the first 20 seconds of a live round, anywhere on the map.
Offline play permits buying whenever alive. Dead players and bomb operators
cannot buy. Carry at most four throwables, with at most one purchased frag,
smoke or molotov, or two flashes; the two starter frags are retained for
compatibility. Duplicate owned weapons and full inventories are rejected
without charging money.

`EconomyRules.cs` defines the catalog and atomic transaction rules without engine
dependencies. `MultiplayerSession.Economy.cs` validates purchases against life,
phase, timing, ownership and server-owned balances. Clients send item IDs only,
never prices, rewards or inventory quantities. Private balance/ownership updates
use channel 28; purchase requests use 27 (channel 25 remains the heartbeat). Protocol 17 requires both peers to be
rebuilt. Authoritative equipment simulation rejects unowned slots, even when a
client bypasses the UI. `GrenadeSupply` owns per-type quantities and a shared
throw cooldown. Persistent throwable areas replicate to every client at 20 Hz.

Generate/register assets and build:

```powershell
python Tools/install_economy_assets.py
dotnet build CoD.Scripts.csproj -c Release
dotnet run --project Tests/CoD.GameplayTests.csproj -c Release -- --economy-grenades
python Tools/run_grenade_smoke.py C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc-nvidia/runtime/RelWithDebInfo
dotnet run --project Tests/NetworkEconomy/CoD.NetworkEconomyTests.csproj -c Release -- C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc-nvidia/runtime/RelWithDebInfo/PlutoGEServer.exe D:/PlutoProjects/CoD/CoDplutoproject.plutoproject
```

The focused tests cover wallet/ownership rules, invalid and unaffordable requests,
inventory capacity, cooldowns, smoke geometry, flash attenuation and molotov
impact. The native fixture verifies real particles, buy UI loading, damage and
cleanup. The network fixture uses two real clients to verify private wallets,
purchase rejection, throw consumption, ownership enforcement and smoke replication.

The full gameplay suite currently stops at an outdated hard-coded Main scene
hand-rig ID. The settings test project referenced by AGENTS.md is absent. These
unrelated checks do not replace the focused feature tests above.

## Bots

Multiplayer bots and offline CoD enemies use the same starting $800, free pistol,
two-weapon ownership limit, catalog prices, wallet cap, kill rewards and throwable
supplies. Multiplayer bots receive the same round payouts and obey the same buy
windows. Offline enemies can shop while alive, matching offline player rules.
The AI saves towards a primary and buys utility from remaining funds. Purchases
and throws pass through the same validators as player requests.

`BotEquipmentRules.cs` chooses intent; `MultiplayerSession.BotEquipment.cs`
adapts it to the host simulation and offline actors. Bot guns use `ServerEquipment`
and `WeaponCatalog`: per-weapon magazines, finite reserve, reload durations, draw
delays and fire rates. Dry primaries fall back to the pistol. Host bot hits use
the player damage, headshot and armour path. Accuracy, reaction and burst decisions
remain AI behavior. Legacy bot-only damage/magazine/fire-rate/reload overrides
and the `botdamage` admin setting are retired; tune shared weapon definitions.

Bots throw stocked frags/molotovs at visible opponents, use smoke when wounded,
and use flashes at safe distance. Decisions check self/ally distance and sample
the launch arc for obstructions. Multiplayer arc planning shares the fair think
budget. Bot throws have a six-second tactical pause and still consume the shared
per-type inventory and throw cooldown. Humans can hold the right mouse button
while pressing G for a short throw. All launch strengths are validated against
the same 0.2–1 range; full throws remain the default. Weapon poses travel with
normal bot transform frames.

The native fixture additionally tests bot buying, pistol switching, finite
reserve consumption, buy-time rejection, grenade cooldown and offline inventory
isolation. Run the live bot networking check with:

```powershell
dotnet run --project Tests/NetworkEconomy/CoD.NetworkEconomyTests.csproj -c Release -- C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc-nvidia/runtime/RelWithDebInfo/PlutoGEServer.exe D:/PlutoProjects/CoD/CoDplutoproject.plutoproject --bots
```
