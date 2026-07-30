using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Numerics;
using PlutoGE.ScriptCore;
using PlutoGE.ScriptCore.Networking;

namespace CoD.Scripts;

/// <summary>
/// Owns a multiplayer session and replicates player transforms. Attach one
/// instance to the local Player and select Offline, Host, or Client.
/// </summary>
public sealed class MultiplayerSession : ScriptBehaviour
{
    private static MultiplayerSession? _activeSession;

    private const int ProtocolVersion = 3;
    private const ushort HandshakeChannel = 1;
    private const ushort TransformChannel = 2;
    private const ushort PeerLeftChannel = 3;
    private const ushort ShootChannel = 4;
    private const ushort DamageChannel = 5;
    private const ushort PeerJoinedChannel = 6;

    [SerializedField] private string mode = "Offline";
    [SerializedField] private string serverAddress = "127.0.0.1";
    [SerializedField] private int serverPort = 7777;
    [SerializedField] private float updatesPerSecond = 20.0f;
    [SerializedField] private string remotePlayerPrefab =
        "project://Prefabs/RemotePlayer.plutoprefab";
    [SerializedField] private float interpolationSharpness = 14.0f;
    [SerializedField] private GameObject? aimingCamera = null;
    [SerializedField] private float weaponDamage = 30.0f;
    [SerializedField] private float weaponRange = 180.0f;
    [SerializedField] private float roundsPerMinute = 720.0f;

    private readonly Dictionary<int, RemotePlayer> _remotePlayers = new();
    private readonly HashSet<int> _authenticatedPeers = new();
    private readonly Dictionary<int, float> _lastAcceptedShotAt = new();
    private readonly Dictionary<int, string> _peerNames = new();
    private NetworkServer? _server;
    private NetworkClient? _client;
    private float _time;
    private float _nextSendAt;
    private int _localPeerId = -1;
    private bool _shutDown;
    private float _nextLocalShotAt;
    private int _hostStartAttempts;
    private float _nextHostStartAttemptAt;
    private string _username = "Player";
    private PlayerHealth? _playerHealth;

    public override void OnCreate()
    {
        if (_activeSession is not null && !ReferenceEquals(_activeSession, this))
            _activeSession.Shutdown();
        _activeSession = this;
        _playerHealth = GameObject.GetComponent<PlayerHealth>();

        if (!MultiplayerLaunch.Mode.Equals("Offline", StringComparison.OrdinalIgnoreCase))
        {
            mode = MultiplayerLaunch.Mode;
            serverAddress = MultiplayerLaunch.ServerAddress;
            _username = MultiplayerLaunch.Username;
        }

        if (mode.Equals("Host", StringComparison.OrdinalIgnoreCase))
            StartHost();
        else if (mode.Equals("Client", StringComparison.OrdinalIgnoreCase))
            StartClient();
        else
            Debug.Log("Multiplayer is offline. Set MultiplayerSession.mode to Host or Client.");
    }

    public override void OnUpdate(float deltaTime)
    {
        if (Input.QuitRequested)
        {
            Shutdown();
            return;
        }

        var safeDeltaTime = MathF.Max(0.0f, deltaTime);
        _time += safeDeltaTime;

        if (_server is null &&
            mode.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
            _hostStartAttempts is > 0 and < 5 &&
            _time >= _nextHostStartAttemptAt)
        {
            StartHost();
        }

        _server?.Poll();
        _client?.Poll();

        if (_time >= _nextSendAt)
        {
            SendLocalTransform();
            _nextSendAt = _time + 1.0f / MathF.Max(1.0f, updatesPerSecond);
        }

        UpdateShooting();

        var blend = 1.0f - MathF.Exp(-MathF.Max(0.0f, interpolationSharpness) * safeDeltaTime);
        foreach (var remote in _remotePlayers.Values)
            remote.Interpolate(blend);
    }

    /// <summary>Stops sockets before a scene transition or application exit.</summary>
    public void Shutdown()
    {
        if (_shutDown)
            return;
        _shutDown = true;

        _server?.Dispose();
        _server = null;
        _client?.Dispose();
        _client = null;
        _authenticatedPeers.Clear();
        _lastAcceptedShotAt.Clear();
        _peerNames.Clear();
        ClearRemotePlayers();
        _localPeerId = -1;
        if (ReferenceEquals(_activeSession, this))
            _activeSession = null;
    }

    private void StartHost()
    {
        _hostStartAttempts++;
        try
        {
            _localPeerId = 0;
            _peerNames[0] = _username;
            _server = new NetworkServer();
            _server.ClientConnected += peerId =>
                Debug.Log($"Network peer {peerId} connected; awaiting handshake.");
            _server.ClientDisconnected += OnServerPeerDisconnected;
            _server.MessageReceived += OnServerMessage;
            _server.Error += exception => Debug.LogError($"Network server: {exception.Message}");
            _server.Start(CheckedPort());
            _hostStartAttempts = 0;
            Debug.Log($"Hosting multiplayer on port {serverPort}.");
        }
        catch (SocketException exception) when (
            exception.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            _server?.Dispose();
            _server = null;
            _localPeerId = -1;
            if (_hostStartAttempts < 5)
            {
                _nextHostStartAttemptAt = _time + 0.75f;
                Debug.LogWarning(
                    $"Port {serverPort} is still closing; retrying host start " +
                    $"({_hostStartAttempts}/5).");
            }
            else
            {
                Debug.LogError(
                    $"Could not host on port {serverPort}: it is already used by another " +
                    "game/editor instance. Stop that host or choose a different serverPort.");
            }
        }
        catch (Exception exception)
        {
            _hostStartAttempts = 5;
            Debug.LogError($"Could not start multiplayer host: {exception.Message}");
            _server?.Dispose();
            _server = null;
        }
    }

    private void StartClient()
    {
        _client = new NetworkClient();
        _client.Connected += () =>
        {
            Debug.Log($"Connected to {serverAddress}:{serverPort}.");
            _client.SendJson(HandshakeChannel, new ClientHello(ProtocolVersion, _username));
        };
        _client.Disconnected += () =>
        {
            Debug.LogWarning("Disconnected from multiplayer host.");
            ClearRemotePlayers();
            _localPeerId = -1;
        };
        _client.MessageReceived += OnClientMessage;
        _client.Error += exception => Debug.LogError($"Network client: {exception.Message}");
        _ = ConnectClientAsync();
    }

    private async System.Threading.Tasks.Task ConnectClientAsync()
    {
        try
        {
            await _client!.ConnectAsync(serverAddress, CheckedPort());
        }
        catch (Exception exception)
        {
            Debug.LogError($"Could not connect to multiplayer host: {exception.Message}");
        }
    }

    private void OnServerMessage(NetworkMessage message)
    {
        try
        {
            if (message.Channel == HandshakeChannel)
            {
                var hello = message.GetJson<ClientHello>();
                if (hello is null || hello.ProtocolVersion != ProtocolVersion)
                {
                    Debug.LogWarning($"Peer {message.PeerId} uses an incompatible protocol.");
                    return;
                }

                var username = SanitizeUsername(hello.Username, message.PeerId);
                _authenticatedPeers.Add(message.PeerId);
                _peerNames[message.PeerId] = username;
                _server!.SendJson(
                    message.PeerId,
                    HandshakeChannel,
                    new ServerWelcome(ProtocolVersion, message.PeerId));
                foreach (var peer in _peerNames)
                {
                    _server.SendJson(
                        message.PeerId,
                        PeerJoinedChannel,
                        new PeerJoined(peer.Key, peer.Value));
                }
                _server.BroadcastJson(
                    PeerJoinedChannel,
                    new PeerJoined(message.PeerId, username),
                    message.PeerId);
                Debug.Log($"{username} joined the game as peer {message.PeerId}.");
                return;
            }

            if (message.Channel != TransformChannel ||
                !_authenticatedPeers.Contains(message.PeerId))
            {
                if (message.Channel == ShootChannel &&
                    _authenticatedPeers.Contains(message.PeerId))
                {
                    var shot = message.GetJson<ShotRequest>();
                    if (shot is not null)
                        ResolveShot(message.PeerId, shot);
                }
                return;
            }

            var incoming = message.GetJson<PlayerTransform>();
            if (incoming is null || !incoming.IsFinite())
                return;

            var authoritative = incoming with { PeerId = message.PeerId };
            ApplyRemoteTransform(authoritative);
            _server!.BroadcastJson(TransformChannel, authoritative, message.PeerId);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Ignored invalid message from peer {message.PeerId}: {exception.Message}");
        }
    }

    private void OnClientMessage(NetworkMessage message)
    {
        try
        {
            if (message.Channel == HandshakeChannel)
            {
                var welcome = message.GetJson<ServerWelcome>();
                if (welcome is null || welcome.ProtocolVersion != ProtocolVersion)
                {
                    Debug.LogError("The host uses an incompatible multiplayer protocol.");
                    _ = _client!.DisconnectAsync();
                    return;
                }

                _localPeerId = welcome.PeerId;
                Debug.Log($"{_username} joined multiplayer as peer {_localPeerId}.");
                return;
            }

            if (message.Channel == TransformChannel)
            {
                var transform = message.GetJson<PlayerTransform>();
                if (transform is not null && transform.PeerId != _localPeerId && transform.IsFinite())
                    ApplyRemoteTransform(transform);
            }
            else if (message.Channel == PeerLeftChannel)
            {
                var peer = message.GetJson<PeerLeft>();
                if (peer is not null)
                    RemoveRemotePlayer(peer.PeerId);
            }
            else if (message.Channel == DamageChannel)
            {
                var damage = message.GetJson<PlayerDamage>();
                if (damage is not null && damage.Amount > 0.0f && float.IsFinite(damage.Amount))
                    GameObject.TryInvoke("TakeDamage", damage.Amount);
            }
            else if (message.Channel == PeerJoinedChannel)
            {
                var peer = message.GetJson<PeerJoined>();
                if (peer is not null)
                    Debug.Log($"{peer.Username} is peer {peer.PeerId}.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Ignored invalid server message: {exception.Message}");
        }
    }

    private void SendLocalTransform()
    {
        var transform = PlayerTransform.From(_localPeerId, GameObject);
        if (_server is not null)
            _server.BroadcastJson(TransformChannel, transform);
        else if (_client?.IsConnected == true && _localPeerId >= 0)
            _client.SendJson(TransformChannel, transform);
    }

    private void UpdateShooting()
    {
        if (_localPeerId < 0 ||
            _playerHealth?.IsDead == true ||
            !Input.IsMouseButtonDown(MouseButton.Left) ||
            _time < _nextLocalShotAt)
            return;

        var camera = aimingCamera ?? GameObject;
        if (!camera.IsValid)
            return;

        var direction = camera.Forward;
        if (direction.LengthSquared() < 0.001f)
            return;

        var shot = ShotRequest.From(camera.WorldPosition, Vector3.Normalize(direction));
        _nextLocalShotAt = _time + 60.0f / MathF.Max(1.0f, roundsPerMinute);
        if (_server is not null)
            ResolveShot(0, shot);
        else
            _client?.SendJson(ShootChannel, shot);
    }

    private void ResolveShot(int shooterPeerId, ShotRequest shot)
    {
        if (_server is null || !shot.IsFinite())
            return;

        var minimumInterval = 60.0f / MathF.Max(1.0f, roundsPerMinute);
        if (_lastAcceptedShotAt.TryGetValue(shooterPeerId, out var lastShotAt) &&
            _time < lastShotAt + minimumInterval * 0.9f)
            return;

        var origin = shot.Origin;
        var direction = shot.Direction;
        if (direction.LengthSquared() < 0.5f)
            return;
        direction = Vector3.Normalize(direction);

        var shooterObject = shooterPeerId == 0
            ? GameObject
            : _remotePlayers.GetValueOrDefault(shooterPeerId)?.GameObject;
        if (shooterObject is null || !shooterObject.IsValid ||
            Vector3.DistanceSquared(origin, shooterObject.WorldPosition) > 16.0f)
            return;

        _lastAcceptedShotAt[shooterPeerId] = _time;
        if (!Physics.Raycast(
                origin,
                direction,
                MathF.Max(1.0f, weaponRange),
                shooterObject,
                out var hit))
            return;

        var targetPeerId = FindPeerForEntity(hit.Entity.EntityId);
        if (targetPeerId < 0 || targetPeerId == shooterPeerId)
            return;

        var damage = MathF.Max(0.0f, weaponDamage);
        if (targetPeerId == 0)
            GameObject.TryInvoke("TakeDamage", damage);
        else
            _server.SendJson(targetPeerId, DamageChannel, new PlayerDamage(damage));
    }

    private int FindPeerForEntity(uint entityId)
    {
        if (GameObject.EntityId == entityId)
            return 0;

        foreach (var pair in _remotePlayers)
        {
            if (pair.Value.GameObject.EntityId == entityId)
                return pair.Key;
        }
        return -1;
    }

    private void ApplyRemoteTransform(PlayerTransform transform)
    {
        if (!_remotePlayers.TryGetValue(transform.PeerId, out var remote))
        {
            if (string.IsNullOrWhiteSpace(remotePlayerPrefab))
                return;

            var instance = Prefab.Instantiate(
                remotePlayerPrefab, transform.Position, transform.Rotation);
            if (instance is null)
            {
                Debug.LogWarning($"Could not spawn proxy for peer {transform.PeerId}.");
                return;
            }

            remote = new RemotePlayer(instance, transform.Position, transform.Rotation);
            _remotePlayers.Add(transform.PeerId, remote);
        }
        remote.TargetPosition = transform.Position;
        remote.TargetRotation = transform.Rotation;
    }

    private void OnServerPeerDisconnected(int peerId)
    {
        _authenticatedPeers.Remove(peerId);
        _lastAcceptedShotAt.Remove(peerId);
        var username = _peerNames.Remove(peerId, out var peerName)
            ? peerName
            : $"Peer {peerId}";
        RemoveRemotePlayer(peerId);
        _server?.BroadcastJson(PeerLeftChannel, new PeerLeft(peerId));
        Debug.Log($"{username} left the game.");
    }

    private void RemoveRemotePlayer(int peerId)
    {
        if (_remotePlayers.Remove(peerId, out var remote))
            remote.GameObject.Destroy();
    }

    private void ClearRemotePlayers()
    {
        foreach (var remote in _remotePlayers.Values)
            remote.GameObject.Destroy();
        _remotePlayers.Clear();
    }

    private ushort CheckedPort()
    {
        if (serverPort is < 1 or > 65535)
            throw new InvalidOperationException("serverPort must be between 1 and 65535.");
        return (ushort)serverPort;
    }

    private static string SanitizeUsername(string? value, int peerId)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0)
            return $"Player{peerId}";
        return clean.Length <= 20 ? clean : clean[..20];
    }

    private sealed record ClientHello(int ProtocolVersion, string Username);
    private sealed record ServerWelcome(int ProtocolVersion, int PeerId);
    private sealed record PeerLeft(int PeerId);
    private sealed record PeerJoined(int PeerId, string Username);
    private sealed record PlayerDamage(float Amount);

    private sealed record ShotRequest(
        float OriginX, float OriginY, float OriginZ,
        float DirectionX, float DirectionY, float DirectionZ)
    {
        public Vector3 Origin => new(OriginX, OriginY, OriginZ);
        public Vector3 Direction => new(DirectionX, DirectionY, DirectionZ);

        public static ShotRequest From(Vector3 origin, Vector3 direction) =>
            new(origin.X, origin.Y, origin.Z, direction.X, direction.Y, direction.Z);

        public bool IsFinite() =>
            float.IsFinite(OriginX) && float.IsFinite(OriginY) && float.IsFinite(OriginZ) &&
            float.IsFinite(DirectionX) && float.IsFinite(DirectionY) && float.IsFinite(DirectionZ);
    }

    private sealed record PlayerTransform(
        int PeerId,
        float X, float Y, float Z,
        float Pitch, float Yaw, float Roll)
    {
        public Vector3 Position => new(X, Y, Z);
        public Vector3 Rotation => new(Pitch, Yaw, Roll);

        public static PlayerTransform From(int peerId, GameObject player)
        {
            var position = player.WorldPosition;
            var rotation = player.WorldRotation;
            return new PlayerTransform(
                peerId, position.X, position.Y, position.Z,
                rotation.X, rotation.Y, rotation.Z);
        }

        public bool IsFinite() =>
            float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) &&
            float.IsFinite(Pitch) && float.IsFinite(Yaw) && float.IsFinite(Roll);
    }

    private sealed class RemotePlayer(
        GameObject gameObject, Vector3 position, Vector3 rotation)
    {
        public GameObject GameObject { get; } = gameObject;
        public Vector3 TargetPosition { get; set; } = position;
        public Vector3 TargetRotation { get; set; } = rotation;

        public void Interpolate(float blend)
        {
            if (!GameObject.IsValid)
                return;
            GameObject.WorldPosition = Vector3.Lerp(GameObject.WorldPosition, TargetPosition, blend);
            var rotation = GameObject.WorldRotation;
            GameObject.WorldRotation = new Vector3(
                0.0f,
                LerpAngle(rotation.Y, TargetRotation.Y, blend),
                0.0f);
        }

        private static float LerpAngle(float current, float target, float blend)
        {
            var difference = (target - current + 180.0f) % 360.0f;
            if (difference < 0.0f)
                difference += 360.0f;
            difference -= 180.0f;
            return NormalizeAngle(current + difference * Math.Clamp(blend, 0.0f, 1.0f));
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360.0f;
            return angle < 0.0f ? angle + 360.0f : angle;
        }
    }
}
