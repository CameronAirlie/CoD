using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

public sealed partial class MultiplayerSession
{
    public bool IsNetworkSession => _server is not null || _client is not null;
    public float ControllerAimAssistAngle(Vector3 origin, Vector3 forward)
    {
        if (IsDedicated || IsNeutralSpectator || CurrentMatch is not { Phase: MatchPhase.Playing } || FlashOpacity > .01f)
            return float.PositiveInfinity;
        var local = CurrentMatch.Players.FirstOrDefault(player => player.PeerId == _localPeerId);
        if (local is null || !local.Alive) return float.PositiveInfinity;
        var localTeam = local.Team;
        if (_server is not null)
        {
            if (!_playerStates.TryGetValue(_localPeerId, out var owner) || owner.Health <= 0) return float.PositiveInfinity;
            localTeam = owner.Team;
        }
        var best = ControllerAimAssist.ConeDegrees;
        foreach (var pair in _remotePlayers)
        {
            if (pair.Key == _localPeerId) continue;
            PlayerTeam team;
            bool alive;
            if (_server is not null)
            {
                if (!_playerStates.TryGetValue(pair.Key, out var state)) continue;
                team = state.Team; alive = state.Health > 0;
            }
            else
            {
                if (!_knownPlayers.TryGetValue(pair.Key, out var state)) continue;
                team = state.Team; alive = state.Alive;
            }
            if (!ControllerAimAssist.IsOpponent(localTeam, team, alive)) continue;
            var target = pair.Value.GameObject;
            if (!target.IsValid || !target.Active) continue;
            var point = ParticipantAimPoint(target);
            var angle = ControllerAimAssist.Angle(origin, forward, point);
            if (angle >= best || SightBlocked(origin, point)) continue;
            var ray = point - origin;
            var length = ray.Length();
            if (Physics.Raycast(origin, ray / length, length + .1f, GameObject, out var hit) && FindPeerForEntity(hit.Entity) == pair.Key)
                best = angle;
        }
        return best;
    }
}
