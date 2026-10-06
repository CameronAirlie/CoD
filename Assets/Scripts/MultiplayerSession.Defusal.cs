using System.Numerics;
using PlutoGE.ScriptCore;
namespace CoD.Scripts;

/// <summary>Transport and scene adapter for the independent bomb-round rules.</summary>
public sealed partial class MultiplayerSession
{
    private const ushort BombInputChannel = 15;
    private DefusalRules? _defusal;
    private int _defusalRound;
    private readonly Dictionary<int, float> _bombHeldUntil = new();
    private float _nextBombInputAt;
    private readonly Dictionary<int, Vector3> _lastBombPositions = new();
    private readonly List<DefusalAgent> _defusalAgents = new();
    private int _lastLocalDefusalRound;
    private bool IsDefusal => RulesMode == MatchMode.Defusal;
    public bool IsPreparingRound => CurrentMatch?.Mode == MatchMode.Defusal && CurrentMatch.Phase != MatchPhase.Playing;
    public bool IsUsingObjective => CurrentMatch?.Defusal is { Operator: { } actor, Progress: > 0 } && actor == _localPeerId;
    private PlayerTeam AttackingTeam => _defusal?.Attackers ?? DefusalRules.AttackersForRound(_defusalRound);
    private Vector3 SitePosition(int index) => GameObject.Find(index == 0 ? "Bombsite A" : "Bombsite B")?.WorldPosition ?? new Vector3(index == 0 ? -22 : 22, .2f, -16);
    private Vector3 DefusalSpawn(PlayerTeam team, int peer)
    {
        var attackers = team == AttackingTeam;
        var marker = GameObject.Find(attackers ? "Attacker Spawn" : "Defender Spawn");
        var origin = marker?.WorldPosition ?? new Vector3(0, 1.2f, attackers ? 28 : -29);
        return origin + new Vector3(((Math.Abs(peer) % 5) - 2) * 2.2f, 0, 0);
    }
    private void StartDefusalRound()
    {
        if (_alphaScore >= scoreLimit || _bravoScore >= scoreLimit) { _alphaScore = _bravoScore = 0; _defusalRound = 0; foreach (var state in _playerStates.Values) { state.Kills = state.Deaths = 0; } }
        _defusalRound++;
        _defusal = null;
        var attackers = DefusalRules.AttackersForRound(_defusalRound);
        ClearGrenades();
        foreach (var owner in _playerStates.Keys) ResetGrenadeSupply(owner);
        foreach (var state in _playerStates.Values) { state.Health = multiplayerMaximumHealth; state.RespawnAt = float.PositiveInfinity; state.LastDamagedAt = _time; }
        // Assign to a human attacker first; bots can carry when humans defend.
        var carrier = _playerStates.Where(p => p.Value.Team == attackers).OrderBy(p => p.Value.IsBot).ThenBy(p => p.Key).Select(p => p.Key).FirstOrDefault();
        _defusal = new(_defusalRound, attackers, carrier, DefusalSpawn(attackers, carrier), [SitePosition(0), SitePosition(1)], matchDuration);
        foreach (var id in _bots.Keys.ToArray())
        {
            var bot = RespawnBot(id, _bots[id]);
            ClearBotTarget(bot); bot.NextShotAt = _time + .5f;
        }
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
                var goal = _defusal.Bomb == BombState.Planted ? _defusal.Position : SitePosition(_defusalRound % 2);
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
        if (!IsDefusal || _time < _nextBombInputAt) return;
        _nextBombInputAt = _time + .1f;
        var held = Input.CursorLocked && Input.IsKeyDown(KeyCode.E) &&
            !Input.IsKeyDown(KeyCode.W) && !Input.IsKeyDown(KeyCode.A) && !Input.IsKeyDown(KeyCode.S) && !Input.IsKeyDown(KeyCode.D) && _playerController?.IsReloading != true && _inventory?.IsUsing != true;
        if (_server is not null) _bombHeldUntil[0] = held ? _time + .25f : 0;
        else _client?.SendJson(BombInputChannel, new BombInput(held));
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
            if (marker is not null) GameObject.WorldPosition = marker.WorldPosition + new Vector3(((Math.Abs(match.LocalPeerId) % 5) - 2) * 2.2f, 0, 0);
            _playerController?.SetRoundFacing(local.Team == bomb.Attackers ? 180 : 0);
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
        var goal = bomb.Bomb == BombState.Planted || bomb.Bomb == BombState.Dropped ? bomb.Position : SitePosition(state.Team == bomb.Attackers ? _defusalRound % 2 : (Math.Abs(id) / 2) % 2);
        if (bomb.Bomb == BombState.Carried && bomb.Carrier is { } holder && !carrier && state.Team == bomb.Attackers)
        {
            var leader = GetParticipantObject(holder);
            if (leader is not null) goal = leader.WorldPosition;
        }
        var usingBomb = carrier && DefusalRules.Near(bot.GameObject.WorldPosition, goal, DefusalRules.SiteRadius) ||
            bomb.Bomb == BombState.Planted && state.Team != bomb.Attackers && DefusalRules.Near(bot.GameObject.WorldPosition, goal, DefusalRules.UseRadius);
        if (threat && (!usingBomb || (_time - state.LastDamagedAt < .75f && bomb.Seconds > 6))) { bot.GameObject.TryInvoke("SetExternalCombatPaused", false); return false; }
        bot.IsEngaging = false; bot.GameObject.TryInvoke("SetExternalAiming", false);
        if (usingBomb) { bot.GameObject.TryInvoke("SetExternalCombatPaused", true); return true; }
        bot.GameObject.TryInvoke("SetExternalCombatPaused", false);
        if (!carrier && bomb.Bomb != BombState.Dropped && !(bomb.Bomb == BombState.Planted && state.Team != bomb.Attackers))
            goal += new Vector3(MathF.Cos(id * 2.4f), 0, MathF.Sin(id * 2.4f)) * 3;
        if (canThink && _time >= bot.NextNavigationAt)
        {
            SetBotDestination(bot, goal, goal);
            bot.NextNavigationAt = _time + .5f;
        }
        return true;
    }
    private sealed record BombInput(bool Held);
}
