using System.Numerics;
using PlutoGE.ScriptCore;
using PlutoGE.ScriptCore.Gameplay;

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
    private RevealTracker? _mapNoiseTracker;
    private RevealTracker MapNoise => _mapNoiseTracker ??= new(mapNoiseRevealSeconds, mapNoiseFadeSeconds);

    /// <summary>Fills contacts for the local viewer. Self is centred on the spectated teammate when observing.</summary>
    public void CollectMapContacts(List<MapContact> contacts, Vector3 viewPosition, float viewHeading, bool includeSelf)
    {
        contacts.Clear();
        if (IsDedicated) return;
        if (includeSelf) contacts.Add(TacticalMap.Self(_localPeerId, viewPosition, viewHeading));
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
                    contacts.Add(TacticalMap.Friendly(pair.Key, actor.WorldPosition, MapMath.Heading(actor.Forward)));
            }
            else if (TeamIdentity.IsCombatTeam(team) && MapNoise.TryGet(pair.Key, RevealTracker.Everyone, _time, out var heard))
                contacts.Add(TacticalMap.Enemy(pair.Key, heard.Position, heard.Opacity));
        }
    }

    /// <summary>Unsuppressed shots reveal the participant where they are now.</summary>
    private void ReportMapNoise(int peer)
    {
        if (IsDedicated || !_remotePlayers.TryGetValue(peer, out var remote) || !remote.GameObject.IsValid) return;
        MapNoise.Reveal(peer, remote.GameObject.WorldPosition, _time);
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
