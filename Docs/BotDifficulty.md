# Bot difficulty

The host owns all bot decisions. The shipped maps and player prefab use a
0.22-second reaction delay, 0.1-second visibility checks, 480 RPM, 18 damage
and 2.25-degree spread. Visibility, aim alignment, stationary firing, cover
and friendly-fire rules still gate attacks; bots do not shoot through walls.

Each spawned life receives a fresh seed. Bots vary their objective approach
angle and preferred engagement distance, choose new candidate angles on each
tactical search, hold for 1.4–2.6 seconds, and choose either side for a 2–5 metre
reposition. Bursts vary from 4–7 shots with 0.225–0.375-second pauses. Searches
still evaluate two candidates per planner slot, retaining the existing frame
budget and ally separation scoring. Capture roles and navigation projection
remain shared with the existing objective rules.

Tune serialized MultiplayerSession fields in scenes as well as C# defaults:
saved component properties override script initializers. Gameplay tests check
capture bounds, varied approaches, movement windows and planner fairness.
Match balance and navigation feel should also be assessed in live play.

## Defusal routes

The host creates a fresh round plan: attackers share a randomly selected site,
while shuffled defenders split coverage between both sites. Each bot selects a
West, Mid or East lane different from its previous round and receives a new
cover angle. Plans remain stable throughout the round.

Attackers continue to their planned site after the lane approach, even when a
human bomb carrier stays at spawn. They clear and cover the site independently;
the carrier still needs to bring the bomb to a site and plant it. Defenders
retain split site coverage, and nearby visible threats take combat priority.

Foundry supplies separate attack and defence lane markers. The serialized
`defusalAttackRouteNames` and `defusalDefendRouteNames` fields list marker names
separated by `|`; custom maps can place their own markers. The adapter validates
projection and a complete navmesh path once per life, trying another lane if
necessary. After reaching the waypoint, bots advance to their objective. An
18-second timeout bounds blocked approaches; missing routes fall back to direct
objective movement. Nearby threats retain combat priority, and dropped or
planted bombs bypass the approach route immediately.

Run the gameplay tests for round-plan and route-progression checks. For native
Foundry validation, run `python Tools/run_defusal_routing_smoke.py <runtime-dir>`;
it checks all six lane paths and actual bot destinations over eight round resets.
