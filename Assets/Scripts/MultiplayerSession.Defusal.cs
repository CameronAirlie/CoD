using System.Numerics;
using PlutoGE.ScriptCore;
namespace CoD.Scripts;

/// <summary>Transport and scene adapter for the independent bomb-round rules.</summary>
public sealed partial class MultiplayerSession
{
    private const ushort BombInputChannel = 15;
    private DefusalRules? _defusal;
    private int _defusalRound;
    private readonly DefusalBotTactics _defusalTactics = new();
    [SerializedField] private string defusalAttackRouteNames = "Defusal Attack West|Defusal Attack Mid|Defusal Attack East";
    [SerializedField] private string defusalDefendRouteNames = "Defusal Defend West|Defusal Defend Mid|Defusal Defend East";
    private string[] _defusalAttackRoutes = [];
    private string[] _defusalDefendRoutes = [];
    private readonly Dictionary<int, float> _bombHeldUntil = new();
    private float _nextBombInputAt;
    private readonly Dictionary<int, Vector3> _lastBombPositions = new();
    private readonly List<DefusalAgent> _defusalAgents = new();
    private int _lastLocalDefusalRound;
    private bool IsDefusal => RulesMode == MatchMode.Defusal;
    public bool IsPreparingRound => CurrentMatch?.Mode == MatchMode.Defusal && CurrentMatch.Phase != MatchPhase.Playing;
    public bool IsUsingObjective => CurrentMatch?.Defusal is { Operator: { } actor, Progress: > 0 } && actor == _localPeerId;
    private readonly Dictionary<int, (PlayerTeam Team, int Slot)> _defusalSpawnSlots = new();
    private PlayerTeam? _defusalSpawnAttackers;
    private PlayerTeam AttackingTeam => _defusalSpawnAttackers ?? _defusal?.Attackers ?? DefusalRules.AttackersForRound(_defusalRound);
    private Vector3 SitePosition(int index) => GameObject.Find(index == 0 ? "Bombsite A" : "Bombsite B")?.WorldPosition ?? new Vector3(index == 0 ? -22 : 22, .2f, -16);
    private Vector3 DefusalSpawn(PlayerTeam team, int peer)
    {
        var attackers = team == AttackingTeam;
        var marker = GameObject.Find(attackers ? "Attacker Spawn" : "Defender Spawn");
        var origin = marker?.WorldPosition ?? new Vector3(0, 1.2f, attackers ? 28 : -29);
        if (!_defusalSpawnSlots.TryGetValue(peer, out var assigned) || assigned.Team != team)
        {
            var used = _defusalSpawnSlots.Where(p => p.Key != peer && _playerStates.ContainsKey(p.Key) && p.Value.Team == team)
                .Select(p => p.Value.Slot).ToHashSet();
            var slot = 0;
            while (used.Contains(slot)) slot++;
            assigned = (team, slot);
            _defusalSpawnSlots[peer] = assigned;
        }
        return DefusalRules.SpawnPosition(origin, assigned.Slot, attackers);
    }
    private void PrepareDefusalSpawns()
    {
        // Move both sides back during preparation, rather than leaving them at
        // their previous combat positions until the next round starts.
        var nextRound = _alphaScore >= scoreLimit || _bravoScore >= scoreLimit ? 1 : _defusalRound + 1;
        _defusalSpawnAttackers = DefusalRules.AttackersForRound(nextRound);
        foreach (var state in _playerStates.Values.Where(s => TeamIdentity.IsCombatTeam(s.Team))) state.Health = multiplayerMaximumHealth;
        _deadRemotePlayers.Clear();
        foreach (var id in _bots.Keys.ToArray())
        {
            var bot = RespawnBot(id, _bots[id]);
            ClearBotTarget(bot);
        }
        ResetHumansForRound();
    }
    private void StartDefusalRound()
    {
        if (_alphaScore >= scoreLimit || _bravoScore >= scoreLimit) { _alphaScore = _bravoScore = 0; _defusalRound = 0; foreach (var state in _playerStates.Values) { state.Kills = state.Deaths = 0; } }
        _defusalRound++;
        _defusal = null;
        var attackers = DefusalRules.AttackersForRound(_defusalRound);
        _defusalSpawnAttackers = attackers;
        _defusalTactics.BeginRound(_bots.Keys.Select(id => (id, _playerStates[id].Team == attackers)), Random.Shared.Next());
        _defusalAttackRoutes = defusalAttackRouteNames.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        _defusalDefendRoutes = defusalDefendRouteNames.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        ClearGrenades();
        foreach (var owner in _playerStates.Where(p => TeamIdentity.IsCombatTeam(p.Value.Team)).Select(p => p.Key)) ResetGrenadeSupply(owner);
        foreach (var state in _playerStates.Values.Where(s => TeamIdentity.IsCombatTeam(s.Team))) { state.Health = multiplayerMaximumHealth; state.RespawnAt = float.PositiveInfinity; state.LastDamagedAt = _time; }
        // Assign to a human attacker first; bots can carry when humans defend.
        var carrier = _playerStates.Where(p => p.Value.Team == attackers).OrderBy(p => p.Value.IsBot).ThenBy(p => p.Key).Select(p => p.Key).FirstOrDefault();
        _defusal = new(_defusalRound, attackers, carrier, DefusalSpawn(attackers, carrier), [SitePosition(0), SitePosition(1)], matchDuration);
        foreach (var id in _bots.Keys.ToArray())
        {
            var bot = RespawnBot(id, _bots[id]);
            ClearBotTarget(bot); bot.NextShotAt = _time + .5f;
        }
        ResetHumansForRound();
        _deadRemotePlayers.Clear(); _bombHeldUntil.Clear(); _lastBombPositions.Clear();
        BeginPhase(MatchPhase.Playing, matchDuration);
    }
    private void TickDefusal(float dt)
    {
        if (_matchPhase == MatchPhase.Playing && _defusal is not null)
        {
            _defusalAgents.Clear();
            foreach (var pair in _playerStates)
            {
                if (!TeamIdentity.IsCombatTeam(pair.Value.Team)) continue;
                var actor = GetParticipantObject(pair.Key);
                if (actor is null || !actor.IsValid)
                {
                    // A connected human can be between proxy destruction and
                    // the first transform of the new round. Still count their life.
                    _defusalAgents.Add(new(pair.Key, pair.Value.Team, DefusalSpawn(pair.Value.Team, pair.Key), pair.Value.Health > 0, false));
                    continue;
                }
                var held = pair.Value.IsBot || _bombHeldUntil.GetValueOrDefault(pair.Key) > _time;
                if (_lastBombPositions.TryGetValue(pair.Key, out var previous) && HorizontalDistance(previous, actor.WorldPosition) > MathF.Max(.025f, dt * .25f)) held = false;
                _lastBombPositions[pair.Key] = actor.WorldPosition;
                held &= _time - pair.Value.LastDamagedAt > .2f;
                // Use requires standing still and an unobstructed reach to the bomb/site.
                var goal = _defusal.Bomb == BombState.Planted ? _defusal.Position : SitePosition(_defusalTactics.AttackSite);
                if (_defusal.Bomb == BombState.Carried && _defusal.Carrier == pair.Key)
                    goal = Vector3.DistanceSquared(actor.WorldPosition, SitePosition(0)) < Vector3.DistanceSquared(actor.WorldPosition, SitePosition(1)) ? SitePosition(0) : SitePosition(1);
                var direction = goal + Vector3.UnitY * .6f - (actor.WorldPosition + Vector3.UnitY * .3f);
                if (held && direction.LengthSquared() > .01f && Physics.Raycast(actor.WorldPosition + Vector3.UnitY * .3f, Vector3.Normalize(direction), direction.Length(), actor, out var hit))
                    held = Vector3.Distance(actor.WorldPosition + Vector3.UnitY * .3f, hit.Point) + .15f >= direction.Length();
                _defusalAgents.Add(new(pair.Key, pair.Value.Team, actor.WorldPosition, pair.Value.Health > 0, held));
            }
            _defusal.Tick(dt, _defusalAgents);
            if (_defusal.Winner is { } winner)
            {
                RewardRound(winner);
                if (winner == PlayerTeam.Alpha) _alphaScore++; else _bravoScore++;
                BeginPhase(MatchPhase.Results, resultsDuration);
            }
        }
        else if (_time >= _phaseEndsAt)
        {
            if (_matchPhase is MatchPhase.Warmup or MatchPhase.Waiting) StartDefusalRound();
            else BeginPhase(MatchPhase.Warmup, warmupDuration);
        }
        if (_time >= _nextMatchBroadcastAt) { BroadcastMatchState(); _nextMatchBroadcastAt = _time + .1f; }
    }
    private void UpdateBombInput()
    {
        if (IsDedicated || IsNeutralSpectator || !IsDefusal || _time < _nextBombInputAt) return;
        _nextBombInputAt = _time + .1f;
        var held = Input.CursorLocked && GameInput.Down("Interact") &&
            GameInput.Movement.LengthSquared() < .01f && _playerController?.IsReloading != true && _inventory?.IsUsing != true;
        if (_server is not null) _bombHeldUntil[0] = held ? _time + .25f : 0;
        else _client?.SendJson(BombInputChannel, new BombInput(held));
    }
    private void ApplyDefusalRemoteLives(MatchSnapshot match)
    {
        // Defusal deaths block transform-driven proxy creation indefinitely.
        // The authoritative alive roster releases that block during preparation
        // and the next round, even though owner movement bypasses offline resets.
        foreach (var participant in match.Players)
        {
            if (participant.PeerId == match.LocalPeerId || !TeamIdentity.IsCombatTeam(participant.Team)) continue;
            if (participant.Alive)
                _deadRemotePlayers.Remove(participant.PeerId);
            else if (!_deadRemotePlayers.ContainsKey(participant.PeerId))
                PlayRemoteDeath(participant.PeerId);
        }
    }
    private void ApplyDefusalLocalRound(MatchSnapshot match)
    {
        if (match.Defusal is not { } bomb) return;
        _playerHealth?.SetRoundRespawn(false);
        var local = Array.Find(match.Players, p => p.PeerId == match.LocalPeerId);
        if (local is null) return;
        if ((bomb.Round != _lastLocalDefusalRound || _lastLocalPhase != MatchPhase.Playing) && match.Phase == MatchPhase.Playing)
        {
            _lastLocalDefusalRound = bomb.Round;
            _deadRemotePlayers.Clear();
            _playerHealth?.ResetForRound();
            var marker = GameObject.Find(local.Team == bomb.Attackers ? "Attacker Spawn" : "Defender Spawn");
            if (!UsesServerAuthority && marker is not null)
            {
                var slot = match.Players.Count(p => p.Team == local.Team && p.PeerId < match.LocalPeerId);
                GameObject.WorldPosition = DefusalRules.SpawnPosition(marker.WorldPosition, slot, local.Team == bomb.Attackers);
            }
            _playerController?.SetRoundFacing(DefusalRules.SpawnYaw(local.Team == bomb.Attackers));
            _inventory?.ApplyLoadout(SelectedLoadout); _playerController?.ResetWeaponForRound();
        }
        if (_server is null)
            foreach (var participant in match.Players)
                if (!participant.Alive && participant.PeerId != match.LocalPeerId && !_deadRemotePlayers.ContainsKey(participant.PeerId))
                    PlayRemoteDeath(participant.PeerId);
        _lastLocalPhase = match.Phase;
        if (!local.Alive && _playerHealth?.IsDead == false) _playerHealth.EliminateForRound();
    }
    private bool TryDefusalBotMovement(int id, BotController bot, PlayerMatchState state, bool canThink)
    {
        if (_defusal is null) return false;
        var bomb = _defusal.Snapshot();
        var carrier = bomb.Carrier == id;
        var target = GetParticipantObject(bot.TargetPeerId);
        var threat = target is not null && bot.CachedLineOfSight && HorizontalDistance(bot.GameObject.WorldPosition, target.WorldPosition) <= botAttackRange;
        var plan = _defusalTactics.PlanFor(id);
        var goal = bomb.Bomb == BombState.Planted || bomb.Bomb == BombState.Dropped ? bomb.Position : SitePosition(plan.Site);
        // Clear the planned site independently of the carrier's movement.
        // Following a human carrier here pulled the whole attack back to spawn
        // whenever the player stayed behind after the lane approach.
        var usingBomb = carrier && DefusalRules.Near(bot.GameObject.WorldPosition, goal, DefusalRules.SiteRadius) ||
            bomb.Bomb == BombState.Planted && state.Team != bomb.Attackers && DefusalRules.Near(bot.GameObject.WorldPosition, goal, DefusalRules.UseRadius);
        if (threat && (!usingBomb || (_time - state.LastDamagedAt < .75f && bomb.Seconds > 6))) { bot.GameObject.TryInvoke("SetExternalCombatPaused", false); return false; }
        bot.IsEngaging = false; bot.GameObject.TryInvoke("SetExternalAiming", false);
        if (usingBomb) { bot.GameObject.TryInvoke("SetExternalCombatPaused", true); return true; }
        bot.GameObject.TryInvoke("SetExternalCombatPaused", false);
        if (!carrier && bomb.Bomb != BombState.Dropped && !(bomb.Bomb == BombState.Planted && state.Team != bomb.Attackers))
            goal += DefusalBotTactics.CoverOffset(plan.ApproachAngle);
        if (canThink && _time >= bot.NextNavigationAt)
        {
            if (!bot.DefusalWaypointValidated)
            {
                bot.DefusalWaypointValidated = true;
                var routes = state.Team == bomb.Attackers ? _defusalAttackRoutes : _defusalDefendRoutes;
                for (int offset = 0; offset < routes.Length; offset++)
                {
                    var marker = GameObject.Find(routes[(plan.Lane + offset) % routes.Length]);
                    if (marker is null || !TryProjectBotNavigationPosition(marker.WorldPosition, out var projected)) continue;
                    var path = Navigation.FindPath(navigationMesh!, bot.GameObject.WorldPosition, projected,
                        botNavigationAgentRadius, botNavigationAgentHeight);
                    if (!path.Complete || path.Points.Count == 0) continue;
                    bot.DefusalWaypoint = projected;
                    break;
                }
            }
            var desired = bot.DefusalApproach.Destination(bot.GameObject.WorldPosition, goal,
                bot.DefusalWaypoint, _time, bomb.Bomb is BombState.Planted or BombState.Dropped);
            if (TryProjectBotNavigationPosition(desired, out var destination)) desired = destination;
            SetBotDestination(bot, desired, goal);
            bot.NextNavigationAt = _time + .5f;
        }
        return true;
    }
    private sealed record BombInput(bool Held);
}
