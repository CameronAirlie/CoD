using System.Numerics;
using PlutoGE.ScriptCore;
using PlutoGE.ScriptCore.Networking;

namespace CoD.Scripts;

/// <summary>Relays rendered first-person presentation only to eliminated teammates.</summary>
public sealed partial class MultiplayerSession
{
    private const ushort SpectatorViewChannel = 29;
    public bool IsNeutralSpectator { get; private set; }
    public PlayerTeam SpectatorTeam { get; private set; } = PlayerTeam.Alpha;
    private int CombatHumanCount => _playerStates.Values.Count(s => !s.IsBot && TeamIdentity.IsCombatTeam(s.Team));
    private void ApplyLocalSpectatorRole(bool spectator)
    {
        IsNeutralSpectator = spectator;
        _playerController?.SetNeutralSpectator(spectator);
    }
    public void SetSpectatorTeam(PlayerTeam team)
    { if (IsNeutralSpectator && TeamIdentity.IsCombatTeam(team)) SpectatorTeam = team; }

    [SerializedField] private float spectatorUpdatesPerSecond = 60;
    [SerializedField] private float spectatorInterpolationDelay = .1f;
    private readonly Dictionary<int, SpectatorViewBuffer> _spectatorViewBuffers = new();
    private readonly Dictionary<int, (SpectatorViewState View, float Received)> _spectatorViews = new();
    private float _nextSpectatorSendAt;
    private long _spectatorSequence;

    public void PublishLocalSpectatorView()
    {
        if (IsDedicated || IsNeutralSpectator || !UsesServerAuthority || _localPeerId < 0 || _playerHealth?.IsDead != false ||
            _time < _nextSpectatorSendAt || _playerController is null) return;
        var rate = float.IsFinite(spectatorUpdatesPerSecond) ? Math.Clamp(spectatorUpdatesPerSecond, 1, 60) : 60;
        _nextSpectatorSendAt = _time + 1 / rate;
        var view = _playerController.CaptureSpectatorView(_localPeerId, ++_spectatorSequence);
        if (view is null || !view.IsValid()) return;
        view = view with { SampleTime = _time };
        if (_server is not null) RelaySpectatorView(0, view);
        else if (_client?.IsConnected == true) _client.SendJson(SpectatorViewChannel, view);
    }

    private void RelaySpectatorView(int peer, SpectatorViewState view)
    {
        if (!view.IsValid() || !_humans.TryGetValue(peer, out var human) || view.Life != human.Life ||
            !_playerStates.TryGetValue(peer, out var source) || source.Health <= 0 ||
            GetParticipantObject(peer) is not { } actor || Vector3.DistanceSquared(actor.WorldPosition, view.Position) > 16 ||
            (_spectatorViews.TryGetValue(peer, out var previous) && previous.View.Life == view.Life && previous.View.Sequence >= view.Sequence)) return;
        // Transport owns the peer id. Camera data cannot impersonate another participant.
        view = view with { PeerId = peer };
        if (_spectatorViews.TryGetValue(peer, out var last) && _time - last.Received < 1f / 120) return;
        DistributeSpectatorView(view);
    }

    private void DistributeSpectatorView(SpectatorViewState view)
    {
        var peer = view.PeerId;
        if (!_playerStates.TryGetValue(peer, out var source) || source.Health <= 0) return;
        if (!StoreSpectatorView(view)) return;
        var sourcePlayer = new MatchPlayer(peer, "", source.Team, 0, 0, source.IsBot, Alive: source.Health > 0);
        foreach (var recipient in _playerStates)
        {
            var target = new MatchPlayer(recipient.Key, "", recipient.Value.Team, 0, 0, recipient.Value.IsBot, Alive: recipient.Value.Health > 0);
            if (!SpectatorRules.CanReceiveView(sourcePlayer, target)) continue;
            if (recipient.Key == 0) continue; // The listen host reads its local cache.
            _server?.SendJson(recipient.Key, SpectatorViewChannel, view);
        }
    }

    private bool HandleSpectatorServerMessage(NetworkMessage message)
    {
        if (message.Channel != SpectatorViewChannel) return false;
        if (_authenticatedPeers.Contains(message.PeerId) && message.GetJson<SpectatorViewState>() is { } view)
            RelaySpectatorView(message.PeerId, view);
        return true;
    }

    private bool HandleSpectatorClientMessage(NetworkMessage message)
    {
        if (message.Channel != SpectatorViewChannel) return false;
        if (message.GetJson<SpectatorViewState>() is { } view && view.IsValid() &&
            (!_spectatorViews.TryGetValue(view.PeerId, out var previous) || view.IsNewerThan(previous.View)))
            StoreSpectatorView(view);
        return true;
    }

    private bool StoreSpectatorView(SpectatorViewState view)
    {
        if (!_spectatorViewBuffers.TryGetValue(view.PeerId, out var buffer))
            _spectatorViewBuffers.Add(view.PeerId, buffer = new());
        if (!buffer.Add(view, _time)) return false;
        _spectatorViews[view.PeerId] = (view, _time);
        return true;
    }

    public bool TryGetSpectatorView(int peer, out SpectatorViewState? view)
    {
        view = null;
        if (!IsNeutralSpectator && _playerHealth?.IsDead != true || SelectSpectatorTarget(peer, 0) != peer) return false;
        if (_spectatorViews.TryGetValue(peer, out var cached) && _time - cached.Received < 1)
        {
            if (_spectatorViewBuffers.TryGetValue(peer, out var buffer) && buffer.TrySample(_time, spectatorInterpolationDelay, out view)) return true;
            view = cached.View; return true;
        }
        return false;
    }

    private void PublishBotSpectatorView(int peer)
    {
        if (!_bots.TryGetValue(peer, out var bot) || !_playerStates.TryGetValue(peer, out var state) || state.Health <= 0) return;
        // Bots have no rendered screen. Publish their authoritative eye/aim and equipment.
        var actor = bot.GameObject;
        var equipment = bot.Loadout.Equipment;
        var position = actor.GetComponent<EnemySoldierBot>()?.SpectatorEyePosition ??
            CombatTargeting.EyePoint(actor.WorldPosition, ServerMotor.StandingEyeHeight);
        var yaw = CharacterHeading.Yaw(actor.RotationQuaternion);
        var pitch = 0f;
        if (GetParticipantObject(bot.TargetPeerId) is { } target)
        {
            var direction = ParticipantAimPoint(target) - position;
            pitch = MathF.Atan2(direction.Y, new Vector2(direction.X, direction.Z).Length()) * 180 / MathF.PI;
        }
        var orientation = SpectatorCameraMath.FromHeading(pitch, yaw);
        var euler = SpectatorCameraMath.ToEngineEuler(orientation);
        var view = new SpectatorViewState(peer, 0, ++_spectatorSequence, position.X, position.Y, position.Z, euler.X, euler.Y, euler.Z, PlayerSettings.Current.FieldOfView,
            equipment.Weapons.SelectedSlot, equipment.Weapons.Magazine, equipment.Reserve, equipment.Reloading, false, false,
            equipment.Weapons.Equipped.HipSpread, state.Health, multiplayerMaximumHealth, 0, 0, Account(peer).Money,
            0, 0, 0, 0, 0, 0, false, bot.SpectatorShots, state.Grenades.Count("frag"), state.Grenades.Count("flash"),
            state.Grenades.Count("smoke"), state.Grenades.Count("molotov"), "frag", equipment.Kits, equipment.Plates, OrientationX: orientation.X, OrientationY: orientation.Y,
            OrientationZ: orientation.Z, OrientationW: orientation.W, SampleTime: _time);
        DistributeSpectatorView(view);
    }
}
