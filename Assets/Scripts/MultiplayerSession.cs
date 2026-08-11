using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Numerics;
using PlutoGE.ScriptCore;
using PlutoGE.ScriptCore.Networking;

namespace CoD.Scripts;

public enum MatchPhase { Waiting, Warmup, Playing, Results }
public enum PlayerTeam { Alpha, Bravo }

public sealed record MatchPlayer(
    int PeerId, string Username, PlayerTeam Team, int Kills, int Deaths, bool IsBot);

public sealed record MatchSnapshot(
    MatchPhase Phase, float SecondsRemaining, int ScoreLimit,
    int AlphaScore, int BravoScore, int LocalPeerId, MatchPlayer[] Players);

public sealed record KillFeedEntry(
    string Killer, string Victim, PlayerTeam KillerTeam);

/// <summary>
/// Owns a multiplayer session and replicates player transforms. Attach one
/// instance to the local Player and select Offline, Host, or Client.
/// </summary>
public sealed class MultiplayerSession : ScriptBehaviour
{
    private static MultiplayerSession? _activeSession;

    private const int ProtocolVersion = 6;
    private const ushort HandshakeChannel = 1;
    private const ushort TransformChannel = 2;
    private const ushort PeerLeftChannel = 3;
    private const ushort ShootChannel = 4;
    private const ushort DamageChannel = 5;
    private const ushort PeerJoinedChannel = 6;
    private const ushort MatchStateChannel = 7;
    private const ushort KillFeedChannel = 8;
    private const ushort ShotEffectChannel = 9;
    private const ushort HitEffectChannel = 10;
    private const ushort LeaveChannel = 11;

    public event Action<MatchSnapshot>? MatchUpdated;
    public event Action<KillFeedEntry>? KillFeedReceived;
    public MatchSnapshot? CurrentMatch { get; private set; }

    public bool IsNetworkParticipant(GameObject entity) => FindPeerForEntity(entity) != -1;

    public bool IsFriendlyNetworkParticipant(GameObject entity)
    {
        var peerId = FindPeerForEntity(entity);
        return peerId != -1 &&
            _knownPlayers.TryGetValue(_localPeerId, out var localPlayer) &&
            _knownPlayers.TryGetValue(peerId, out var targetPlayer) &&
            localPlayer.Team == targetPlayer.Team;
    }

    [SerializedField] private string mode = "Offline";
    [SerializedField] private string serverAddress = "127.0.0.1";
    [SerializedField] private int serverPort = 7777;
    [SerializedField] private float updatesPerSecond = 20.0f;
    [SerializedField] private float disconnectTimeout = 3.0f;
    [SerializedField] private string titleScene = "Title";
    [SerializedField] private string remotePlayerPrefab =
        "project://Prefabs/RemotePlayer.plutoprefab";
    [SerializedField] private string hostBotPrefab =
        "project://Prefabs/Enemy.plutoprefab";
    [SerializedField] private float interpolationSharpness = 22.0f;
    [SerializedField] private GameObject? aimingCamera = null;
    [SerializedField] private float weaponDamage = 30.0f;
    [SerializedField] private float multiplayerHeadshotMultiplier = 1.5f;
    [SerializedField] private float weaponRange = 180.0f;
    [SerializedField] private float roundsPerMinute = 720.0f;
    [SerializedField] private float warmupDuration = 5.0f;
    [SerializedField] private float matchDuration = 300.0f;
    [SerializedField] private float resultsDuration = 10.0f;
    [SerializedField] private int scoreLimit = 50;
    [SerializedField] private float multiplayerMaximumHealth = 100.0f;
    [SerializedField] private float combatRespawnDelay = 1.5f;
    [SerializedField] private float multiplayerRegenerationDelay = 5.0f;
    [SerializedField] private float multiplayerRegenerationPerSecond = 6.0f;
    [SerializedField] private bool fillWithBots = true;
    [SerializedField] private int minimumParticipants = 6;
    [SerializedField] private int maximumBots = 5;
    [SerializedField] private float botPreferredRange = 13.0f;
    [SerializedField] private float botNavigationRefreshInterval = 0.75f;
    [SerializedField] private float botNavigationArrivalDistance = 1.25f;
    [SerializedField] private float botNavigationMoveTimeout = 8.0f;
    [SerializedField] private float botTargetReplanDistance = 6.0f;
    [SerializedField] private float botTargetSelectionInterval = 0.5f;
    [SerializedField] private float botPerceptionInterval = 0.15f;
    [SerializedField] private float botLostSightGrace = 0.5f;
    [SerializedField] private int botThinkBudgetPerFrame = 1;
    [SerializedField] private float botAttackRange = 22.0f;
    [SerializedField] private float botRoundsPerMinute = 360.0f;
    [SerializedField] private float botDamage = 18.0f;
    [SerializedField] private float botAccuracyDegrees = 3.0f;
    [SerializedField] private float botStationaryFireSpeed = 0.2f;
    [SerializedField] private float botTurnSharpness = 10.0f;
    [SerializedField] private float botFiringAngle = 8.0f;
    [SerializedField] private int botTacticalPositionSamples = 12;
    [SerializedField] private float botCentrePositionWeight = 1.25f;
    [SerializedField] private float botTargetRetentionBonus = 10.0f;
    [SerializedField] private float botTargetLineOfSightBonus = 6.0f;
    [SerializedField] private GameObject? navigationMesh = null;
    [SerializedField] private float botNavigationAgentRadius = 0.5f;
    [SerializedField] private float botNavigationAgentHeight = 2.0f;

    private readonly Dictionary<int, RemotePlayer> _remotePlayers = new();
    private readonly HashSet<int> _authenticatedPeers = new();
    private readonly Dictionary<int, float> _lastAcceptedShotAt = new();
    private readonly Dictionary<int, string> _peerNames = new();
    private readonly Dictionary<int, float> _peerLastSeenAt = new();
    private readonly Dictionary<int, PlayerMatchState> _playerStates = new();
    private readonly Dictionary<int, BotController> _bots = new();
    private readonly Dictionary<int, MatchPlayer> _knownPlayers = new();
    private NetworkServer? _server;
    private NetworkClient? _client;
    private float _time;
    private float _nextSendAt;
    private int _localPeerId = -1;
    private bool _shutDown;
    private float _nextLocalShotAt;
    private float _nextMatchBroadcastAt;
    private float _phaseEndsAt;
    private MatchPhase _matchPhase = MatchPhase.Waiting;
    private int _alphaScore;
    private int _bravoScore;
    private int _nextBotId = -1000;
    private int _hostStartAttempts;
    private float _nextHostStartAttemptAt;
    private string _username = "Player";
    private PlayerHealth? _playerHealth;
    private float _lastHostMessageAt;
    private bool _returnToTitle;

    public override void OnCreate()
    {
        if (_activeSession is not null && !ReferenceEquals(_activeSession, this))
            _activeSession.Shutdown();
        _activeSession = this;
        _playerHealth = GameObject.GetComponent<PlayerHealth>();
        navigationMesh ??= GameObject.Find("Navmesh");

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

        if (_server is not null)
        {
            UpdateMatch(safeDeltaTime);
            UpdateBots(safeDeltaTime);
        }

        _server?.Poll();
        _client?.Poll();

        if (_server is not null)
            RemoveTimedOutPeers();
        else if (_client?.IsConnected == true && _localPeerId >= 0 &&
                 _time - _lastHostMessageAt > MathF.Max(1.0f, disconnectTimeout))
        {
            Debug.LogWarning("Multiplayer host timed out.");
            _returnToTitle = true;
        }

        if (_returnToTitle)
        {
            ReturnToTitle();
            return;
        }

        if (_time >= _nextSendAt)
        {
            SendLocalTransform();
            SendBotTransforms();
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

        if (_client?.IsConnected == true && _localPeerId >= 0)
        {
            _client.SendJson(LeaveChannel, new PeerLeft(_localPeerId));
            // Network sends are queued on a background writer. Give the final
            // leave frame a brief opportunity to reach the host before Dispose
            // cancels that writer and closes the process socket.
            System.Threading.Thread.Sleep(25);
        }

        _server?.Dispose();
        _server = null;
        _client?.Dispose();
        _client = null;
        _authenticatedPeers.Clear();
        _lastAcceptedShotAt.Clear();
        _peerLastSeenAt.Clear();
        _peerNames.Clear();
        _playerStates.Clear();
        _bots.Clear();
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
            if (_playerHealth is not null)
            {
                multiplayerMaximumHealth = MathF.Max(1.0f, _playerHealth.MaximumHealth);
                _playerHealth.ConfigureMultiplayerHealthRules(
                    multiplayerMaximumHealth,
                    multiplayerRegenerationDelay,
                    multiplayerRegenerationPerSecond);
            }
            _localPeerId = 0;
            _peerNames[0] = _username;
            _playerStates[0] = new PlayerMatchState(PlayerTeam.Alpha, false)
            {
                Health = MathF.Max(1.0f, multiplayerMaximumHealth)
            };
            _server = new NetworkServer();
            _server.ClientConnected += peerId =>
                Debug.Log($"Network peer {peerId} connected; awaiting handshake.");
            _server.ClientDisconnected += OnServerPeerDisconnected;
            _server.MessageReceived += OnServerMessage;
            _server.Error += exception => Debug.LogError($"Network server: {exception.Message}");
            _server.Start(CheckedPort());
            BeginPhase(MatchPhase.Warmup, warmupDuration);
            try
            {
                EnsureBotFill();
            }
            catch (Exception exception)
            {
                Debug.LogError($"TDM bot fill failed without stopping the match: {exception.Message}");
            }
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
            _lastHostMessageAt = _time;
            Debug.Log($"Connected to {serverAddress}:{serverPort}.");
            _client.SendJson(HandshakeChannel, new ClientHello(ProtocolVersion, _username));
        };
        _client.Disconnected += () =>
        {
            if (_shutDown)
                return;
            Debug.LogWarning("Disconnected from multiplayer host.");
            ClearRemotePlayers();
            _localPeerId = -1;
            _returnToTitle = true;
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
            _returnToTitle = true;
        }
    }

    private void OnServerMessage(NetworkMessage message)
    {
        try
        {
            if (_authenticatedPeers.Contains(message.PeerId))
                _peerLastSeenAt[message.PeerId] = _time;

            if (message.Channel == LeaveChannel)
            {
                OnServerPeerDisconnected(message.PeerId);
                return;
            }

            if (message.Channel == HandshakeChannel)
            {
                var hello = message.GetJson<ClientHello>();
                if (hello is null || hello.ProtocolVersion != ProtocolVersion)
                {
                    Debug.LogWarning($"Peer {message.PeerId} uses an incompatible protocol.");
                    return;
                }

                var username = SanitizeUsername(hello.Username, message.PeerId);
                RemoveOneBot();
                _authenticatedPeers.Add(message.PeerId);
                _peerLastSeenAt[message.PeerId] = _time;
                _peerNames[message.PeerId] = username;
                _playerStates[message.PeerId] = new PlayerMatchState(ChooseTeam(), false)
                {
                    Health = MathF.Max(1.0f, multiplayerMaximumHealth)
                };
                _server!.SendJson(
                    message.PeerId,
                    HandshakeChannel,
                    new ServerWelcome(
                        ProtocolVersion,
                        message.PeerId,
                        multiplayerMaximumHealth,
                        multiplayerRegenerationDelay,
                        multiplayerRegenerationPerSecond));
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
                BroadcastMatchState();
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
            _lastHostMessageAt = _time;
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
                if (float.IsFinite(welcome.MaximumHealth) && welcome.MaximumHealth > 0.0f)
                    _playerHealth?.ConfigureMultiplayerHealthRules(
                        welcome.MaximumHealth,
                        welcome.RegenerationDelay,
                        welcome.RegenerationPerSecond);
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
            else if (message.Channel == MatchStateChannel)
            {
                var snapshot = message.GetJson<MatchSnapshot>();
                if (snapshot is not null)
                    PublishMatch(snapshot with { LocalPeerId = _localPeerId });
            }
            else if (message.Channel == KillFeedChannel)
            {
                var entry = message.GetJson<KillFeedEntry>();
                if (entry is not null)
                    KillFeedReceived?.Invoke(entry);
            }
            else if (message.Channel == ShotEffectChannel)
            {
                var effect = message.GetJson<ShotEffect>();
                if (effect is not null)
                    PlayRemoteShotEffect(effect.ShooterPeerId);
            }
            else if (message.Channel == HitEffectChannel)
            {
                var effect = message.GetJson<HitEffect>();
                if (effect is not null)
                    PlayRemoteHitEffect(effect.VictimPeerId);
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
        if (_server is null || _matchPhase != MatchPhase.Playing || !shot.IsFinite())
            return;

        if (!_playerStates.TryGetValue(shooterPeerId, out var shooterState) ||
            shooterState.Health <= 0.0f)
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
        PublishShotEffect(shooterPeerId);
        if (!Physics.Raycast(
                origin,
                direction,
                MathF.Max(1.0f, weaponRange),
                shooterObject,
                out var hit))
            return;

        var targetPeerId = FindPeerForEntity(hit.Entity);
        if (targetPeerId == -1 || targetPeerId == shooterPeerId)
            return;

        if (!_playerStates.TryGetValue(targetPeerId, out var targetState) ||
            targetState.Team == shooterState.Team || targetState.Health <= 0.0f)
            return;

        var damage = MathF.Max(0.0f, weaponDamage) *
            (hit.Entity.HasTag("Head") ? MathF.Max(1.0f, multiplayerHeadshotMultiplier) : 1.0f);
        targetState.Health = MathF.Max(0.0f, targetState.Health - damage);
        targetState.LastDamagedAt = _time;
        PublishHitEffect(targetPeerId);
        if (targetPeerId == 0)
        {
            GameObject.TryInvoke("TakeDamage", damage);
            // The host's actual health component is authoritative for the host.
            // This also accounts for locally configured health and armour.
            if (_playerHealth is not null)
                targetState.Health = MathF.Max(0.0f, _playerHealth.CurrentHealth);
        }
        else if (!_bots.ContainsKey(targetPeerId))
            _server.SendJson(targetPeerId, DamageChannel, new PlayerDamage(damage));

        if (targetState.Health <= 0.0f)
            RegisterKill(shooterPeerId, targetPeerId);
    }

    private int FindPeerForEntity(GameObject entity)
    {
        var entityId = entity.EntityId;
        if (GameObject.EntityId == entityId)
            return 0;

        var hitbox = entity.GetComponent<NetworkParticipantHitbox>();
        if (hitbox is not null)
            entityId = hitbox.ParticipantEntityId;

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
                remotePlayerPrefab, transform.Position, Vector3.Zero);
            if (instance is null)
            {
                Debug.LogWarning($"Could not spawn proxy for peer {transform.PeerId}.");
                return;
            }

            remote = new RemotePlayer(instance, transform.Position, transform.Yaw);
            instance.TryInvoke("SetRemoteProxyMode");
            _remotePlayers.Add(transform.PeerId, remote);
            ConfigureNameplate(transform.PeerId, instance);
        }
        remote.TargetPosition = transform.Position;
        remote.TargetYaw = transform.Yaw;
    }

    private void OnServerPeerDisconnected(int peerId)
    {
        var wasParticipant = _authenticatedPeers.Remove(peerId);
        _peerLastSeenAt.Remove(peerId);
        _lastAcceptedShotAt.Remove(peerId);
        var username = _peerNames.Remove(peerId, out var peerName)
            ? peerName
            : $"Peer {peerId}";
        _playerStates.Remove(peerId);
        RemoveRemotePlayer(peerId);
        if (!wasParticipant)
            return;
        _server?.BroadcastJson(PeerLeftChannel, new PeerLeft(peerId));
        EnsureBotFill();
        BroadcastMatchState();
        Debug.Log($"{username} left the game.");
    }

    private void RemoveTimedOutPeers()
    {
        var timeout = MathF.Max(1.0f, disconnectTimeout);
        List<int>? timedOut = null;
        foreach (var peer in _peerLastSeenAt)
        {
            if (_time - peer.Value <= timeout)
                continue;
            timedOut ??= new List<int>();
            timedOut.Add(peer.Key);
        }

        if (timedOut is null)
            return;
        foreach (var peerId in timedOut)
        {
            Debug.LogWarning($"Peer {peerId} timed out.");
            OnServerPeerDisconnected(peerId);
        }
    }

    private void ReturnToTitle()
    {
        _returnToTitle = false;
        Shutdown();
        Input.CursorLocked = false;
        if (!SceneManager.LoadScene(titleScene))
            Debug.LogError($"Could not load title scene '{titleScene}'.");
    }

    private void RemoveRemotePlayer(int peerId)
    {
        if (_remotePlayers.Remove(peerId, out var remote))
        {
            // Native destruction is deferred until the end of the frame. Hide
            // the proxy immediately so a departed player cannot remain visible.
            remote.GameObject.Active = false;
            if (!remote.GameObject.Destroy())
                Debug.LogWarning($"Could not destroy remote proxy for peer {peerId}.");
        }
    }

    private void ClearRemotePlayers()
    {
        foreach (var remote in _remotePlayers.Values)
        {
            remote.GameObject.Active = false;
            remote.GameObject.Destroy();
        }
        _remotePlayers.Clear();
    }

    private void EnsureBotFill()
    {
        if (_server is null || !fillWithBots)
        {
            while (_bots.Count > 0) RemoveOneBot();
            return;
        }

        var humanCount = _playerStates.Count - _bots.Count;
        var desired = Math.Clamp(
            Math.Max(0, minimumParticipants) - humanCount,
            0,
            Math.Max(0, maximumBots));
        while (_bots.Count > desired) RemoveOneBot();
        while (_bots.Count < desired && AddBot()) { }
        BroadcastMatchState();
    }

    private bool AddBot()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hostBotPrefab))
                return false;

            var peerId = _nextBotId--;
            var spawn = BotSpawnPosition(peerId);
            var instance = Prefab.Instantiate(hostBotPrefab, spawn, Vector3.Zero);
            if (instance is null)
            {
                Debug.LogWarning($"Could not instantiate TDM bot prefab '{hostBotPrefab}'.");
                return false;
            }

            var team = ChooseTeam();
            _peerNames[peerId] = $"[BOT] Operator {Math.Abs(peerId + 999)}";
            _playerStates[peerId] = new PlayerMatchState(team, true)
            {
                Health = MathF.Max(1.0f, multiplayerMaximumHealth)
            };
            _remotePlayers[peerId] = new RemotePlayer(instance, spawn, 0.0f);
            _bots[peerId] = new BotController(instance, spawn, unchecked((uint)peerId * 747796405u));
            instance.TryInvoke("ResetExternalNavigation", spawn.X, spawn.Y, spawn.Z);
            Debug.Log($"Added {_peerNames[peerId]} to {team}.");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Could not add TDM bot: {exception.Message}");
            return false;
        }
    }

    private void RemoveOneBot()
    {
        if (_bots.Count == 0) return;
        var peerId = 0;
        foreach (var id in _bots.Keys)
        {
            peerId = id;
            break;
        }
        _bots.Remove(peerId);
        _playerStates.Remove(peerId);
        _peerNames.Remove(peerId);
        RemoveRemotePlayer(peerId);
        _server?.BroadcastJson(PeerLeftChannel, new PeerLeft(peerId));
    }

    private void SendBotTransforms()
    {
        if (_server is null) return;
        foreach (var pair in _bots)
            _server.BroadcastJson(TransformChannel, PlayerTransform.From(pair.Key, pair.Value.GameObject));
    }

    private void UpdateBots(float deltaTime)
    {
        if (_matchPhase != MatchPhase.Playing) return;
        var thinkBudget = Math.Max(1, botThinkBudgetPerFrame);
        foreach (var pair in _bots)
        {
            var botId = pair.Key;
            var bot = pair.Value;
            if (!_playerStates.TryGetValue(botId, out var botState) || botState.Health <= 0.0f)
                continue;

            var canThink = thinkBudget > 0;
            var thought = false;
            var currentTargetValid = IsValidBotTarget(botId, bot.TargetPeerId);
            var currentTarget = currentTargetValid
                ? GetParticipantObject(bot.TargetPeerId)
                : null;
            var currentTargetInCombatRange = currentTarget is not null &&
                HorizontalDistance(bot.GameObject.WorldPosition, currentTarget.WorldPosition) <=
                MathF.Max(1.0f, botAttackRange);
            var retainCombatTarget = currentTargetValid &&
                (bot.IsEngaging ||
                 (bot.CachedLineOfSight && currentTargetInCombatRange));
            if (canThink && !retainCombatTarget &&
                (_time >= bot.NextTargetSelectionAt || !currentTargetValid))
            {
                var selectedTarget = FindBestOpponent(botId, bot);
                bot.NextTargetSelectionAt = _time + MathF.Max(0.1f, botTargetSelectionInterval);
                thought = true;
                if (bot.TargetPeerId != selectedTarget)
                {
                    bot.TargetPeerId = selectedTarget;
                    bot.IsEngaging = false;
                    bot.CachedLineOfSight = false;
                    bot.NextPerceptionAt = 0.0f;
                    bot.NextNavigationAt = 0.0f;
                    bot.HasNavigationDestination = false;
                }
            }

            var targetId = bot.TargetPeerId;
            var targetObject = GetParticipantObject(targetId);
            if (targetObject is null || !targetObject.IsValid)
            {
                if (thought) thinkBudget--;
                continue;
            }

            var offset = targetObject.WorldPosition - bot.GameObject.WorldPosition;
            offset.Y = 0.0f;
            var distance = offset.Length();
            if (distance < 0.001f) continue;
            var direction = offset / distance;
            var measuredTravel = bot.GameObject.WorldPosition - bot.PreviousPosition;
            measuredTravel.Y = 0.0f;
            bot.PreviousPosition = bot.GameObject.WorldPosition;
            var travel = bot.Body is not null && !bot.Body.IsKinematic
                ? bot.Body.Velocity
                : (deltaTime > 0.0001f ? measuredTravel / deltaTime : Vector3.Zero);
            travel.Y = 0.0f;
            var movementSpeed = travel.Length();
            var isStationary = movementSpeed <= MathF.Max(0.01f, botStationaryFireSpeed);
            if (canThink && _time >= bot.NextPerceptionAt)
            {
                bot.CachedLineOfSight = HasBotLineOfSight(
                    botId, targetId, bot.GameObject, targetObject);
                if (bot.CachedLineOfSight)
                    bot.LastLineOfSightAt = _time;
                bot.NextPerceptionAt = _time + MathF.Max(0.05f, botPerceptionInterval);
                thought = true;
            }
            var hasLineOfSight = bot.CachedLineOfSight;
            var canEngage = hasLineOfSight && distance <= MathF.Max(1.0f, botAttackRange);

            if (!bot.IsEngaging && canEngage)
            {
                bot.IsEngaging = true;
                bot.HasNavigationDestination = false;
                var stop = bot.GameObject.WorldPosition;
                bot.GameObject.TryInvoke("SetExternalNavigationDestination", stop.X, stop.Y, stop.Z);
                if (bot.Body is not null)
                {
                    var velocity = bot.Body.Velocity;
                    bot.Body.Velocity = new Vector3(0.0f, velocity.Y, 0.0f);
                }
            }
            else if (bot.IsEngaging &&
                     ((_time > bot.LastLineOfSightAt + MathF.Max(0.0f, botLostSightGrace)) ||
                      distance > MathF.Max(1.0f, botAttackRange) * 1.1f))
            {
                bot.IsEngaging = false;
                bot.NextNavigationAt = 0.0f;
                bot.HasNavigationDestination = false;
            }

            var arrived = bot.HasNavigationDestination &&
                HorizontalDistance(bot.GameObject.WorldPosition, bot.NavigationDestination) <=
                MathF.Max(0.1f, botNavigationArrivalDistance);
            var targetMovedSincePlan = bot.HasNavigationDestination &&
                HorizontalDistance(targetObject.WorldPosition, bot.TargetPositionAtPlan) >=
                MathF.Max(1.0f, botTargetReplanDistance);
            if (arrived)
                bot.HasNavigationDestination = false;

            if (canThink && !bot.IsEngaging &&
                (!bot.HasNavigationDestination || targetMovedSincePlan ||
                 _time >= bot.NavigationMoveDeadline) &&
                _time >= bot.NextNavigationAt)
            {
                if (TryChooseBotTacticalDestination(
                        bot, targetId, targetObject, out var destination))
                {
                    bot.GameObject.TryInvoke(
                        "SetExternalNavigationDestination",
                        destination.X, destination.Y, destination.Z);
                    bot.NavigationDestination = destination;
                    bot.TargetPositionAtPlan = targetObject.WorldPosition;
                    bot.HasNavigationDestination = true;
                    bot.NavigationMoveDeadline = _time +
                        MathF.Max(1.0f, botNavigationMoveTimeout);
                }
                bot.NextNavigationAt = _time + MathF.Max(0.05f, botNavigationRefreshInterval);
                thought = true;
            }
            // The native NavAgent owns heading while travelling. Writing a
            // dynamic rigidbody's rotation from script at the same time can
            // repeatedly resynchronise its physics transform and cause pulsing.
            if (isStationary)
                TurnBotTowards(bot, direction, deltaTime);
            var facingTarget = IsBotFacing(bot.GameObject, direction, botFiringAngle);
            bot.GameObject.TryInvoke(
                "SetExternalAiming", bot.IsEngaging && isStationary && facingTarget);

            if (_remotePlayers.TryGetValue(botId, out var remote))
            {
                remote.TargetPosition = bot.GameObject.WorldPosition;
                remote.TargetYaw = bot.GameObject.Rotation.Y;
            }

            if (bot.IsEngaging && isStationary && facingTarget && hasLineOfSight &&
                _time >= bot.NextShotAt)
            {
                BotFire(botId, targetObject, bot);
                bot.NextShotAt = _time + 60.0f / MathF.Max(1.0f, botRoundsPerMinute);
            }
            if (thought) thinkBudget--;
        }
    }

    private bool IsValidBotTarget(int botId, int targetId)
    {
        if (!_playerStates.TryGetValue(botId, out var botState) ||
            !_playerStates.TryGetValue(targetId, out var targetState) ||
            targetId == botId || targetState.Health <= 0.0f ||
            targetState.Team == botState.Team)
            return false;
        var target = GetParticipantObject(targetId);
        return target is not null && target.IsValid;
    }

    private int FindBestOpponent(int peerId, BotController bot)
    {
        if (!_playerStates.TryGetValue(peerId, out var source)) return int.MinValue;
        var bestId = int.MinValue;
        var bestScore = float.MaxValue;
        foreach (var pair in _playerStates)
        {
            if (pair.Key == peerId || pair.Value.Team == source.Team || pair.Value.Health <= 0.0f)
                continue;
            var candidate = GetParticipantObject(pair.Key);
            if (candidate is null || !candidate.IsValid) continue;
            var score = Vector3.Distance(bot.GameObject.WorldPosition, candidate.WorldPosition);
            if (pair.Key == bot.TargetPeerId)
                score -= MathF.Max(0.0f, botTargetRetentionBonus);
            if (HasBotLineOfSight(peerId, pair.Key, bot.GameObject, candidate))
                score -= MathF.Max(0.0f, botTargetLineOfSightBonus);
            if (score < bestScore)
            {
                bestScore = score;
                bestId = pair.Key;
            }
        }
        return bestId;
    }

    private bool TryChooseBotTacticalDestination(
        BotController bot, int targetId, GameObject target, out Vector3 destination)
    {
        destination = bot.GameObject.WorldPosition;
        var samples = Math.Clamp(botTacticalPositionSamples, 4, 32);
        var radius = MathF.Max(2.0f, botPreferredRange);
        var angleOffset = bot.StrafeSign < 0.0f ? 180.0f / samples : 0.0f;
        var bestScore = float.MaxValue;
        var found = false;

        for (var index = 0; index < samples; index++)
        {
            var angle = (angleOffset + index * (360.0f / samples)) * MathF.PI / 180.0f;
            var desired = target.WorldPosition + new Vector3(
                MathF.Sin(angle) * radius, 0.0f, MathF.Cos(angle) * radius);
            if (!TryProjectBotNavigationPosition(desired, out var candidate))
                continue;

            var targetDistance = HorizontalDistance(candidate, target.WorldPosition);
            if (targetDistance > MathF.Max(radius, botAttackRange))
                continue;
            var rangeError = MathF.Abs(targetDistance - radius);
            var travelDistance = Vector3.Distance(bot.GameObject.WorldPosition, candidate);
            var centreDistance = navigationMesh is null
                ? 0.0f
                : HorizontalDistance(candidate, navigationMesh.WorldPosition);
            var score = rangeError * 4.0f + travelDistance * 0.35f +
                centreDistance * MathF.Max(0.0f, botCentrePositionWeight);
            if (HasLineOfSightFrom(candidate, targetId, target))
                score -= 40.0f;
            if (score >= bestScore) continue;
            bestScore = score;
            destination = candidate;
            found = true;
        }
        return found && TryValidateBotNavigationDestination(
            bot.GameObject.WorldPosition, destination);
    }

    private bool HasLineOfSightFrom(Vector3 position, int targetId, GameObject target)
    {
        var origin = position + Vector3.UnitY * 0.65f;
        var aimPoint = target.WorldPosition + Vector3.UnitY * 0.65f;
        var ray = aimPoint - origin;
        var length = ray.Length();
        return length < 0.001f ||
            (Physics.Raycast(origin, ray / length, length + 0.2f, out var hit) &&
             FindPeerForEntity(hit.Entity) == targetId);
    }

    private static float HorizontalDistance(Vector3 first, Vector3 second)
    {
        var offset = first - second;
        offset.Y = 0.0f;
        return offset.Length();
    }

    private void TurnBotTowards(BotController controller, Vector3 direction, float deltaTime)
    {
        if (direction.LengthSquared() < 0.0001f) return;
        direction = Vector3.Normalize(direction);
        var desiredYaw = MathF.Atan2(-direction.X, -direction.Z) * 180.0f / MathF.PI;
        var rotation = controller.GameObject.Rotation;
        var difference = (desiredYaw - rotation.Y + 540.0f) % 360.0f - 180.0f;
        if (MathF.Abs(MathF.Abs(difference) - 180.0f) < 0.1f)
            difference = 180.0f * controller.TurnDirection;
        else if (MathF.Abs(difference) > 0.1f)
            controller.TurnDirection = MathF.Sign(difference);
        var blend = 1.0f - MathF.Exp(-MathF.Max(0.0f, botTurnSharpness) * MathF.Max(0.0f, deltaTime));
        rotation.Y += difference * blend;
        controller.GameObject.Rotation = rotation;
    }

    private bool HasBotLineOfSight(
        int botId, int targetId, GameObject bot, GameObject target)
    {
        var origin = bot.WorldPosition + Vector3.UnitY * 0.65f;
        var aimPoint = target.WorldPosition + Vector3.UnitY * 0.65f;
        var ray = aimPoint - origin;
        var length = ray.Length();
        if (length < 0.001f) return true;
        return Physics.Raycast(origin, ray / length, length + 0.2f, bot, out var hit) &&
            FindPeerForEntity(hit.Entity) == targetId && targetId != botId;
    }

    private static bool IsBotFacing(GameObject bot, Vector3 direction, float maximumAngle)
    {
        var forward = bot.Forward;
        forward.Y = 0.0f;
        direction.Y = 0.0f;
        if (forward.LengthSquared() < 0.0001f || direction.LengthSquared() < 0.0001f)
            return false;
        var minimumDot = MathF.Cos(Math.Clamp(maximumAngle, 0.0f, 180.0f) * MathF.PI / 180.0f);
        return Vector3.Dot(Vector3.Normalize(forward), Vector3.Normalize(direction)) >= minimumDot;
    }

    private GameObject? GetParticipantObject(int peerId)
        => peerId == 0 ? GameObject : _remotePlayers.GetValueOrDefault(peerId)?.GameObject;

    private void BotFire(int botId, GameObject intendedTarget, BotController bot)
    {
        PublishShotEffect(botId);
        var origin = bot.GameObject.WorldPosition + Vector3.UnitY * 0.65f;
        var aimPoint = intendedTarget.WorldPosition + Vector3.UnitY * 0.65f;
        var direction = aimPoint - origin;
        if (direction.LengthSquared() < 0.001f) return;
        direction = ApplyBotSpread(Vector3.Normalize(direction), botAccuracyDegrees, bot);
        if (!Physics.Raycast(origin, direction, MathF.Max(1.0f, botAttackRange), bot.GameObject, out var hit))
            return;
        var victimId = FindPeerForEntity(hit.Entity);
        if (victimId == -1 || victimId == botId) return;
        if (!_playerStates.TryGetValue(botId, out var shooter) ||
            !_playerStates.TryGetValue(victimId, out var victim) ||
            shooter.Team == victim.Team || victim.Health <= 0.0f)
            return;

        var damage = MathF.Max(0.0f, botDamage);
        victim.Health = MathF.Max(0.0f, victim.Health - damage);
        victim.LastDamagedAt = _time;
        PublishHitEffect(victimId);
        if (victimId == 0)
        {
            GameObject.TryInvoke("TakeDamage", damage);
            if (_playerHealth is not null)
                victim.Health = MathF.Max(0.0f, _playerHealth.CurrentHealth);
        }
        else if (!_bots.ContainsKey(victimId)) _server?.SendJson(victimId, DamageChannel, new PlayerDamage(damage));
        if (victim.Health <= 0.0f) RegisterKill(botId, victimId);
    }

    private void PublishShotEffect(int shooterPeerId)
    {
        PlayRemoteShotEffect(shooterPeerId);
        _server?.BroadcastJson(ShotEffectChannel, new ShotEffect(shooterPeerId));
    }

    private void PlayRemoteShotEffect(int shooterPeerId)
    {
        if (_remotePlayers.TryGetValue(shooterPeerId, out var remote))
            remote.GameObject.TryInvoke("PlayExternalShootAnimation");
    }

    private void PublishHitEffect(int victimPeerId)
    {
        PlayRemoteHitEffect(victimPeerId);
        _server?.BroadcastJson(HitEffectChannel, new HitEffect(victimPeerId));
    }

    private void PlayRemoteHitEffect(int victimPeerId)
    {
        if (_remotePlayers.TryGetValue(victimPeerId, out var remote))
            remote.GameObject.TryInvoke("PlayExternalHitAnimation");
    }

    private void ConfigureAllNameplates()
    {
        foreach (var pair in _remotePlayers)
            ConfigureNameplate(pair.Key, pair.Value.GameObject);
    }

    private void ConfigureNameplate(int peerId, GameObject proxy)
    {
        var friendly = _knownPlayers.TryGetValue(_localPeerId, out var localPlayer) &&
            _knownPlayers.TryGetValue(peerId, out var remotePlayer) &&
            localPlayer.Team == remotePlayer.Team;
        var username = _knownPlayers.TryGetValue(peerId, out var player)
            ? player.Username
            : string.Empty;
        proxy.TryInvoke("ConfigureNetworkNameplate", username, friendly);
    }

    private static Vector3 ApplyBotSpread(Vector3 direction, float degrees, BotController bot)
    {
        float Next()
        {
            bot.RandomState = bot.RandomState * 1664525u + 1013904223u;
            return (bot.RandomState & 0x00ffffffu) / 16777216.0f;
        }
        var tangent = MathF.Tan(MathF.Max(0.0f, degrees) * MathF.PI / 180.0f);
        var right = Vector3.Cross(direction, Vector3.UnitY);
        if (right.LengthSquared() < 0.001f) right = Vector3.UnitX;
        else right = Vector3.Normalize(right);
        return Vector3.Normalize(direction + right * ((Next() * 2.0f - 1.0f) * tangent) +
            Vector3.UnitY * ((Next() * 2.0f - 1.0f) * tangent));
    }

    private Vector3 BotSpawnPosition(int peerId)
    {
        var index = Math.Abs(peerId + 1000);
        string[] spawnNames =
        [
            "Spawn North West",
            "Spawn North East",
            "Spawn South East",
            "Spawn South West"
        ];
        var spawn = GameObject.Find(spawnNames[index % spawnNames.Length]);
        if (spawn is not null && spawn.IsValid)
            return spawn.WorldPosition;

        Debug.LogWarning("TDM bot spawn markers were not found; using the host spawn position.");
        return GameObject.WorldPosition;
    }

    private bool TryProjectBotNavigationPosition(
        Vector3 desiredPosition, out Vector3 destination)
    {
        destination = desiredPosition;
        if (navigationMesh is null || !navigationMesh.IsValid ||
            !Navigation.ProjectPoint(
                navigationMesh, desiredPosition, out var projected,
                botNavigationAgentRadius, botNavigationAgentHeight))
            return false;

        destination = projected;
        return true;
    }

    private bool TryValidateBotNavigationDestination(
        Vector3 currentPosition, Vector3 destination)
    {
        if (navigationMesh is null || !navigationMesh.IsValid)
            return false;
        var path = Navigation.FindPath(
            navigationMesh, currentPosition, destination,
            botNavigationAgentRadius, botNavigationAgentHeight);
        return path.Complete && path.Points.Count > 0;
    }

    private void UpdateMatch(float deltaTime)
    {
        foreach (var pair in _playerStates)
        {
            var state = pair.Value;
            if (state.Health <= 0.0f && _time >= state.RespawnAt)
            {
                state.Health = MathF.Max(1.0f, multiplayerMaximumHealth);
                if (_bots.TryGetValue(pair.Key, out var bot))
                {
                    bot.GameObject.TryInvoke(
                        "ResetExternalNavigation",
                        bot.SpawnPosition.X, bot.SpawnPosition.Y, bot.SpawnPosition.Z);
                    bot.NextShotAt = _time + 0.5f;
                    bot.NextNavigationAt = _time + 0.5f;
                    bot.PreviousPosition = bot.SpawnPosition;
                    bot.IsEngaging = false;
                    bot.TargetPeerId = int.MinValue;
                }
            }
            else if (state.Health > 0.0f &&
                     state.Health < multiplayerMaximumHealth &&
                     _time >= state.LastDamagedAt + MathF.Max(0.0f, multiplayerRegenerationDelay))
                state.Health = MathF.Min(
                    multiplayerMaximumHealth,
                    state.Health + MathF.Max(0.0f, multiplayerRegenerationPerSecond) * deltaTime);
        }

        // Keep the host's authoritative match state aligned with the component
        // that drives its HUD, death, armour, and regeneration.
        if (_playerHealth is not null && _playerStates.TryGetValue(0, out var hostState))
            hostState.Health = MathF.Max(0.0f, _playerHealth.CurrentHealth);

        if (_time >= _phaseEndsAt)
        {
            if (_matchPhase is MatchPhase.Waiting or MatchPhase.Warmup)
                BeginPlaying();
            else if (_matchPhase == MatchPhase.Playing)
                BeginPhase(MatchPhase.Results, resultsDuration);
            else
                BeginPhase(MatchPhase.Warmup, warmupDuration);
        }

        if (_time >= _nextMatchBroadcastAt)
        {
            BroadcastMatchState();
            _nextMatchBroadcastAt = _time + 0.25f;
        }
    }

    private void BeginPlaying()
    {
        _alphaScore = 0;
        _bravoScore = 0;
        foreach (var state in _playerStates.Values)
        {
            state.Kills = 0;
            state.Deaths = 0;
            state.Health = MathF.Max(1.0f, multiplayerMaximumHealth);
        }
        foreach (var bot in _bots.Values)
        {
            bot.GameObject.TryInvoke(
                "ResetExternalNavigation",
                bot.SpawnPosition.X, bot.SpawnPosition.Y, bot.SpawnPosition.Z);
            bot.NextShotAt = _time + 0.5f;
            bot.NextNavigationAt = _time + 0.5f;
            bot.PreviousPosition = bot.SpawnPosition;
            bot.IsEngaging = false;
            bot.TargetPeerId = int.MinValue;
        }
        BeginPhase(MatchPhase.Playing, matchDuration);
    }

    private void BeginPhase(MatchPhase phase, float duration)
    {
        _matchPhase = phase;
        _phaseEndsAt = _time + MathF.Max(0.1f, duration);
        BroadcastMatchState();
    }

    private void RegisterKill(int killerPeerId, int victimPeerId)
    {
        if (!_playerStates.TryGetValue(killerPeerId, out var killer) ||
            !_playerStates.TryGetValue(victimPeerId, out var victim))
            return;

        killer.Kills++;
        victim.Deaths++;
        victim.RespawnAt = _time + MathF.Max(0.1f, combatRespawnDelay);
        if (_bots.TryGetValue(victimPeerId, out var victimBot))
        {
            var hidden = victimBot.GameObject.WorldPosition;
            hidden.Y -= 100.0f;
            victimBot.GameObject.WorldPosition = hidden;
            victimBot.GameObject.TryInvoke(
                "SetExternalNavigationDestination", hidden.X, hidden.Y, hidden.Z);
            if (_remotePlayers.TryGetValue(victimPeerId, out var remote))
                remote.TargetPosition = hidden;
        }
        if (killer.Team == PlayerTeam.Alpha) _alphaScore++;
        else _bravoScore++;

        var entry = new KillFeedEntry(
            _peerNames.GetValueOrDefault(killerPeerId, $"Player{killerPeerId}"),
            _peerNames.GetValueOrDefault(victimPeerId, $"Player{victimPeerId}"),
            killer.Team);
        _server?.BroadcastJson(KillFeedChannel, entry);
        KillFeedReceived?.Invoke(entry);
        BroadcastMatchState();

        if (_alphaScore >= Math.Max(1, scoreLimit) || _bravoScore >= Math.Max(1, scoreLimit))
            BeginPhase(MatchPhase.Results, resultsDuration);
    }

    private PlayerTeam ChooseTeam()
    {
        var alpha = 0;
        var bravo = 0;
        foreach (var state in _playerStates.Values)
        {
            if (state.Team == PlayerTeam.Alpha) alpha++;
            else bravo++;
        }
        return alpha <= bravo ? PlayerTeam.Alpha : PlayerTeam.Bravo;
    }

    private void BroadcastMatchState()
    {
        var players = new MatchPlayer[_playerStates.Count];
        var index = 0;
        foreach (var pair in _playerStates)
        {
            var state = pair.Value;
            players[index++] = new MatchPlayer(
                pair.Key,
                _peerNames.GetValueOrDefault(pair.Key, $"Player{pair.Key}"),
                state.Team,
                state.Kills,
                state.Deaths,
                state.IsBot);
        }

        Array.Sort(players, static (left, right) =>
        {
            var team = left.Team.CompareTo(right.Team);
            if (team != 0) return team;
            var kills = right.Kills.CompareTo(left.Kills);
            return kills != 0 ? kills : left.Deaths.CompareTo(right.Deaths);
        });
        var snapshot = new MatchSnapshot(
            _matchPhase,
            MathF.Max(0.0f, _phaseEndsAt - _time),
            Math.Max(1, scoreLimit),
            _alphaScore,
            _bravoScore,
            _localPeerId,
            players);
        PublishMatch(snapshot);
        _server?.BroadcastJson(MatchStateChannel, snapshot);
    }

    private void PublishMatch(MatchSnapshot snapshot)
    {
        CurrentMatch = snapshot;
        _knownPlayers.Clear();
        foreach (var player in snapshot.Players)
            _knownPlayers[player.PeerId] = player;
        ConfigureAllNameplates();
        MatchUpdated?.Invoke(snapshot);
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
    private sealed record ServerWelcome(
        int ProtocolVersion,
        int PeerId,
        float MaximumHealth = 100.0f,
        float RegenerationDelay = 5.0f,
        float RegenerationPerSecond = 6.0f);
    private sealed record PeerLeft(int PeerId);
    private sealed record PeerJoined(int PeerId, string Username);
    private sealed record PlayerDamage(float Amount);
    private sealed record ShotEffect(int ShooterPeerId);
    private sealed record HitEffect(int VictimPeerId);

    private sealed class PlayerMatchState(PlayerTeam team, bool isBot)
    {
        public PlayerTeam Team { get; } = team;
        public bool IsBot { get; } = isBot;
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public float Health { get; set; } = 100.0f;
        public float RespawnAt { get; set; }
        public float LastDamagedAt { get; set; }
    }

    private sealed class BotController(GameObject gameObject, Vector3 spawnPosition, uint seed)
    {
        public GameObject GameObject { get; } = gameObject;
        public RigidbodyComponent? Body { get; } = gameObject.GetComponent<RigidbodyComponent>();
        public Vector3 SpawnPosition { get; } = spawnPosition;
        public float NextShotAt { get; set; }
        public float NextNavigationAt { get; set; }
        public Vector3 PreviousPosition { get; set; } = spawnPosition;
        public int TargetPeerId { get; set; } = int.MinValue;
        public float NextTargetSelectionAt { get; set; } = (seed & 7u) * 0.06f;
        public float NextPerceptionAt { get; set; } = ((seed >> 3) & 7u) * 0.02f;
        public bool CachedLineOfSight { get; set; }
        public float LastLineOfSightAt { get; set; } = float.MinValue;
        public bool HasNavigationDestination { get; set; }
        public Vector3 NavigationDestination { get; set; } = spawnPosition;
        public Vector3 TargetPositionAtPlan { get; set; } = spawnPosition;
        public float NavigationMoveDeadline { get; set; }
        public bool IsEngaging { get; set; }
        public float TurnDirection { get; set; } = 1.0f;
        public float StrafeSign { get; set; } = (seed & 1u) == 0 ? -1.0f : 1.0f;
        public uint RandomState { get; set; } = seed;
    }

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
        float Pitch, float Yaw, float Roll,
        float RotationX, float RotationY, float RotationZ, float RotationW)
    {
        public Vector3 Position => new(X, Y, Z);
        public bool HasQuaternion =>
            RotationX * RotationX + RotationY * RotationY +
            RotationZ * RotationZ + RotationW * RotationW > 0.000001f;
        public Quaternion Rotation => HasQuaternion
            ? Quaternion.Normalize(new(RotationX, RotationY, RotationZ, RotationW))
            : Quaternion.CreateFromAxisAngle(Vector3.UnitY, Yaw * MathF.PI / 180.0f);

        public static PlayerTransform From(int peerId, GameObject player)
        {
            var position = player.WorldPosition;
            var euler = player.Rotation;
            var quaternion = player.RotationQuaternion;
            return new PlayerTransform(
                peerId, position.X, position.Y, position.Z,
                euler.X, euler.Y, euler.Z,
                quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);
        }

        public bool IsFinite() =>
            float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) &&
            float.IsFinite(Pitch) && float.IsFinite(Yaw) && float.IsFinite(Roll) &&
            float.IsFinite(RotationX) && float.IsFinite(RotationY) &&
            float.IsFinite(RotationZ) && float.IsFinite(RotationW);
    }

    private sealed class RemotePlayer(
        GameObject gameObject, Vector3 position, float yaw)
    {
        public GameObject GameObject { get; } = gameObject;
        public Vector3 TargetPosition { get; set; } = position;
        public float TargetYaw { get; set; } = yaw;

        public void Interpolate(float blend)
        {
            if (!GameObject.IsValid)
                return;
            GameObject.WorldPosition = Vector3.Lerp(GameObject.WorldPosition, TargetPosition, blend);
            // Character roots must remain upright. Applying the complete
            // quaternion through XYZ Euler decomposition can express yaw past
            // 90 degrees as equivalent 180-degree pitch/roll values. The local
            // player authors yaw directly, so mirror that value exactly.
            GameObject.Rotation = new Vector3(0.0f, TargetYaw, 0.0f);
        }
    }
}
