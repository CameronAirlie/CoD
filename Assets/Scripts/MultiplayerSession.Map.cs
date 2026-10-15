using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>
/// Tactical map contacts. Friendlies are always known with their facing; enemies appear only
/// where they were last heard (unsuppressed gunfire) and never expose their facing.
/// Presentation only: clients already receive every proxy transform for rendering.
/// </summary>
public sealed partial class MultiplayerSession
{
    [SerializedField] private float mapNoiseRevealSeconds = 3.0f;
    [SerializedField] private float mapNoiseFadeSeconds = 1.0f;
    private MapNoiseMemory? _mapNoiseMemory;
    private MapNoiseMemory MapNoise => _mapNoiseMemory ??= new(mapNoiseRevealSeconds, mapNoiseFadeSeconds);

    /// <summary>Fills contacts for the local viewer. Self is centred on the spectated teammate when observing.</summary>
    public void CollectMapContacts(List<MapContact> contacts, Vector3 viewPosition, float viewHeading, bool includeSelf)
    {
        contacts.Clear();
        if (IsDedicated) return;
        if (includeSelf) contacts.Add(new(_localPeerId, MapContactKind.Self, viewPosition, viewHeading));
        var viewerTeam = MapViewerTeam();
        var observed = _playerController?.SpectatorView?.PeerId;
        foreach (var pair in _remotePlayers)
        {
            if (pair.Key == _localPeerId) continue;
            var actor = pair.Value.GameObject;
            if (!actor.IsValid || !actor.Active || !TryMapParticipant(pair.Key, out var team, out var alive) || !alive)
            {
                MapNoise.Forget(pair.Key);
                continue;
            }
            if (team == viewerTeam)
            {
                // The observed teammate is drawn as the centred self marker.
                if (pair.Key != observed || !includeSelf)
                    contacts.Add(new(pair.Key, MapContactKind.Friendly, actor.WorldPosition, TacticalMap.Heading(actor.Forward)));
            }
            else if (TeamIdentity.IsCombatTeam(team))
            {
                var opacity = MapNoise.Opacity(pair.Key, _time, out var heard);
                if (opacity > 0) contacts.Add(MapContact.Enemy(pair.Key, heard, opacity));
            }
        }
    }

    private PlayerTeam MapViewerTeam()
    {
        if (IsNeutralSpectator) return SpectatorTeam;
        if ((_server is not null || _client is null) && _playerStates.TryGetValue(_localPeerId, out var state)) return state.Team;
        if (_knownPlayers.TryGetValue(_localPeerId, out var known)) return known.Team;
        return PlayerTeam.Alpha; // Offline play: authored enemies are registered as Bravo bots.
    }

    private bool TryMapParticipant(int peer, out PlayerTeam team, out bool alive)
    {
        if (_server is not null || _client is null)
        {
            if (_playerStates.TryGetValue(peer, out var state))
            {
                team = state.Team; alive = state.Health > 0;
                return true;
            }
        }
        else if (_knownPlayers.TryGetValue(peer, out var known))
        {
            team = known.Team; alive = known.Alive && !_deadRemotePlayers.ContainsKey(peer);
            return true;
        }
        team = PlayerTeam.Spectator; alive = false;
        return false;
    }
}
