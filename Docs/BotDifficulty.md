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
