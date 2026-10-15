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
public sealed partial class MultiplayerSession : ScriptBehaviour
{
    private static MultiplayerSession? _activeSession;
    private readonly Dictionary<int, float> _pendingHandshakes = new();

    // Include connecting clients and hosts awaiting a retry, even without peers.
    public static bool IsMultiplayerActive => _activeSession is { _shutDown: false } session &&
        (session.mode.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
         session.mode.Equals("Client", StringComparison.OrdinalIgnoreCase));

    private const int ProtocolVersion = 20;
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
    private const ushort HitConfirmationChannel = 12;
    private const ushort DeathEffectChannel = 13;
    private const ushort TeamSwitchChannel = 14;
    private const ushort BotReloadEffectChannel = 20;

    public event Action<MatchSnapshot>? MatchUpdated;
    public event Action<KillFeedEntry>? KillFeedReceived;
    public MatchSnapshot? CurrentMatch { get; private set; }

    public void RequestTeamSwitch()
    {
        if (IsNeutralSpectator) { SetSpectatorTeam(SpectatorTeam == PlayerTeam.Alpha ? PlayerTeam.Bravo : PlayerTeam.Alpha); return; }
        if (_server is not null)
        {
            SwitchTeam(0);
            return;
        }

        if (_client is not null && _localPeerId >= 0)
            _client.SendJson(TeamSwitchChannel, new TeamSwitchRequest());
    }

    public bool IsNetworkParticipant(GameObject entity) => FindPeerForEntity(entity) != -1;

    public bool IsFriendlyNetworkParticipant(GameObject entity)
    {
        var peerId = FindPeerForEntity(entity);
        return peerId != -1 && ArePeersFriendly(_localPeerId, peerId);
    }

    private bool ArePeersFriendly(int firstPeerId, int secondPeerId)
    {
        // The host must use its gameplay state directly. Clients use the latest
        // replicated snapshot; keeping this decision here prevents presentation
        // and damage code from developing separate ideas of team allegiance.
        if (_server is not null)
            return _playerStates.TryGetValue(firstPeerId, out var firstState) &&
                _playerStates.TryGetValue(secondPeerId, out var secondState) &&
                firstState.Team == secondState.Team;

        return _knownPlayers.TryGetValue(firstPeerId, out var firstPlayer) &&
            _knownPlayers.TryGetValue(secondPeerId, out var secondPlayer) &&
            firstPlayer.Team == secondPlayer.Team;
    }

    [SerializedField] private string mode = "Offline";
    [SerializedField] private bool joinAsSpectator = false;
    [SerializedField] private int maximumSpectators = 8;
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
    // Zero keeps the general-purpose server's default connection capacity.
    [SerializedField] private int maximumParticipants = 0;
    [SerializedField] private float botSpawnInterval = 0.2f;
    [SerializedField] private float botFallbackSpawnMinimumDistance = 20.0f;
    [SerializedField] private float botFallbackSpawnRadius = 80.0f;
    [SerializedField] private int botFallbackSpawnAttempts = 24;
    [SerializedField] private float botFallbackSpawnGroundClearance = 8.0f;
    [SerializedField] private float botPreferredRange = 16.0f;
    [SerializedField] private float botNavigationRefreshInterval = 0.75f;
    [SerializedField] private float botNavigationArrivalDistance = 1.25f;
    [SerializedField] private float botNavigationMoveTimeout = 8.0f;
    [SerializedField] private float botTargetReplanDistance = 6.0f;
    [SerializedField] private float botTargetSelectionInterval = 0.3f;
    [SerializedField] private float botPerceptionInterval = 0.1f;
    [SerializedField] private float botLostSightGrace = 0.5f;
    [SerializedField] private int botThinkBudgetPerFrame = 1;
    [SerializedField] private float botReactionTime = 0.5f;
    [SerializedField] private int botBurstSize = 5;
    [SerializedField] private float botBurstPause = 0.3f;
    [SerializedField] private float botStuckTimeout = 2.0f;
    [SerializedField] private float botCombatHoldDuration = 2.0f;
    [SerializedField] private float botRepositionDuration = .65f;
    [SerializedField] private float botAttackRange = 22.0f;
    [SerializedField] private float botAccuracyDegrees = 4.5f;
    [SerializedField] private float botStationaryFireSpeed = 0.2f;
    [SerializedField] private float botTurnSharpness = 14.0f;
    [SerializedField] private float botFiringAngle = 8.0f;
    [SerializedField] private int botTacticalPositionSamples = 12;
    [SerializedField] private float botCentrePositionWeight = 1.25f;
    [SerializedField] private float botTargetRetentionBonus = 10.0f;
    [SerializedField] private float botTargetLineOfSightBonus = 6.0f;
    [SerializedField] private GameObject? navigationMesh = null;
    [SerializedField] private float botNavigationAgentRadius = 0.5f;
    [SerializedField] private float botNavigationAgentHeight = 2.0f;

    private readonly Dictionary<int, RemotePlayer> _remotePlayers = new();
    private readonly Dictionary<int, float> _deadRemotePlayers = new();
    private readonly HashSet<int> _authenticatedPeers = new();
    private readonly Dictionary<int, float> _lastAcceptedShotAt = new();
    private readonly Dictionary<int, string> _lastAcceptedWeapon = new();
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
    private PlayerController? _playerController;
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
    private float _nextBotFillAt;
    private bool _botPrefabReady;
    private uint _botSpawnSequence;
    private readonly FairBotPlanner _botPlanner = new();

    public override void OnCreate()
    {
        if (_activeSession is not null && !ReferenceEquals(_activeSession, this))
            _activeSession.Shutdown();
        _activeSession = this;
        DedicatedServerSettings.Apply(this);
        if (DedicatedServerSettings.NeedsMap) return;
        _playerHealth = IsDedicated ? null : GameObject.GetComponent<PlayerHealth>();
        if (IsDefusal) { _playerHealth?.SetRoundRespawn(false); multiplayerRegenerationPerSecond = 0; }
        if (_playerHealth is not null) _playerHealth.Respawned += OnLocalRespawn;
        _playerController = IsDedicated ? null : GameObject.GetComponent<PlayerController>();
        if (navigationMesh is null || !navigationMesh.IsValid)
            navigationMesh = GameObject.Find("Navmesh");
        PreloadNetworkPrefabs();
        PreloadGrenadePrefabs();

        if (!IsDedicated && !MultiplayerLaunch.Mode.Equals("Offline", StringComparison.OrdinalIgnoreCase))
        {
            mode = MultiplayerLaunch.Mode;
            joinAsSpectator = MultiplayerLaunch.JoinAsSpectator;
            serverAddress = MultiplayerLaunch.ServerAddress;
            serverPort = MultiplayerLaunch.ServerPort;
            if (MultiplayerLaunch.ServerMatchMode is { } rules) gameMode = rules.ToString();
            _username = MultiplayerLaunch.Username;
        }

        // Dedicated servers retain explicit launcher/admin population settings.
        if (!IsDedicated && RulesMode == MatchMode.Hardpoint)
        {
            minimumParticipants = maximumParticipants = 12;
            maximumBots = 12;
        }

        ApplyLocalSpectatorRole(!mode.Equals("Offline", StringComparison.OrdinalIgnoreCase) && joinAsSpectator);
        if (mode.Equals("Host", StringComparison.OrdinalIgnoreCase))
            StartHost();
        else if (mode.Equals("Client", StringComparison.OrdinalIgnoreCase))
            StartClient();
        else
            Debug.Log("Multiplayer is offline. Set MultiplayerSession.mode to Host or Client.");
    }

    public override void OnUpdate(float deltaTime)
    {
        if (DedicatedServerSettings.NeedsMap)
        {
            SceneManager.LoadScene(DedicatedServerSettings.Map);
            return;
        }
        if (!IsDedicated && Input.QuitRequested)
        {
            Shutdown();
            return;
        }

        var safeDeltaTime = MathF.Max(0.0f, deltaTime);
        _time += safeDeltaTime;
        if (GrenadesOffline && UsesPresetLoadouts && !_offlineLoadoutInitialized)
        {
            _offlineLoadoutInitialized = true;
            ApplyOfflineSpawnLoadout();
            _inventory ??= GameObject.GetComponent<PlayerInventory>();
            _inventory?.ApplyLoadout(ActiveLoadout);
            _playerController?.ResetWeaponForRound();
        }
        UpdateLoadoutSelection();
        UpdateBombInput();
        UpdateGrenades(safeDeltaTime);

        if (_server is null &&
            mode.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
            _hostStartAttempts is > 0 and < 5 &&
            _time >= _nextHostStartAttemptAt)
        {
            StartHost();
        }

        _server?.Poll();
        _client?.Poll();
        if (_server is not null)
        {
            TickHumans(safeDeltaTime);
            if (_time >= _nextBotFillAt && (!IsDefusal || _matchPhase != MatchPhase.Playing))
            {
                TryEnsureBotFill();
                _nextBotFillAt = _time + MathF.Max(0.05f, botSpawnInterval);
            }
            UpdateMatch(safeDeltaTime);
            UpdateBots(safeDeltaTime);
        }

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
            if (_server is not null) SendAuthoritativeStates();
            else if (_client?.IsConnected == true) _client.Send(25, Array.Empty<byte>());
            SendBotTransforms();
            _nextSendAt = _time + 1.0f / MathF.Max(1.0f, updatesPerSecond);
        }

        var blend = 1.0f - MathF.Exp(-MathF.Max(0.0f, interpolationSharpness) * safeDeltaTime);
        foreach (var pair in _remotePlayers)
        {
            // Server entities are driven by authoritative movement/navigation.
            // Interpolation is presentation-only on clients.
            if (_server is not null)
                continue;
            pair.Value.Interpolate(blend);
        }
    }

    public override void OnDestroy()
    {
        if (_playerHealth is not null) _playerHealth.Respawned -= OnLocalRespawn;
        Shutdown();
    }

    /// <summary>Stops sockets before a scene transition or application exit.</summary>
    public void Shutdown()
    {
        if (_shutDown)
            return;
        _shutDown = true;
        ClearGrenades();
        _spectatorViews.Clear();
        _spectatorViewBuffers.Clear();

        if (_playerController is not null)
        {
            _playerController = null;
        }

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
        _pendingHandshakes.Clear();
        _lastAcceptedShotAt.Clear();
        _lastAcceptedWeapon.Clear();
        _peerLastSeenAt.Clear();
        _peerNames.Clear();
        _playerStates.Clear();
        _humans.Clear();
        _deadRemotePlayers.Clear();
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
            _localPeerId = IsDedicated ? -1 : 0;
            if (!IsDedicated)
            {
            _peerNames[0] = _username;
            _playerStates[0] = new PlayerMatchState(IsNeutralSpectator ? PlayerTeam.Spectator : PlayerTeam.Alpha, false)
            {
                Health = IsNeutralSpectator ? 0 : MathF.Max(1.0f, multiplayerMaximumHealth),
                RespawnAt = IsNeutralSpectator ? float.PositiveInfinity : 0
            };
            }
            _server = new NetworkServer
            {
                MaxPayloadSize = 4096,
                MaxClients = maximumParticipants > 0 ? Math.Clamp(maximumParticipants + Math.Clamp(maximumSpectators, 0, 128) - (IsDedicated ? 0 : 1), 1, 4096) : 128
            };
            _server.ClientConnected += peerId =>
            {
                _pendingHandshakes[peerId] = _time;
                Debug.Log($"Network peer {peerId} connected; awaiting handshake.");
            };
            _server.ClientDisconnected += OnServerPeerDisconnected;
            _server.MessageReceived += OnServerMessage;
            _server.Error += exception => Debug.LogError($"Network server: {exception.Message}");
            _server.Start(CheckedPort());
            if (!IsDedicated && !IsNeutralSpectator) AddHuman(0);
            BeginPhase(MatchPhase.Warmup, warmupDuration);
            // Scene-owned prefab and navigation resources may finish becoming
            // available after this component's OnCreate. Try now, then let the
            // host update retry until the requested participant count is met.
            TryEnsureBotFill();
            _nextBotFillAt = _time + MathF.Max(0.05f, botSpawnInterval);
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
            _client.SendJson(HandshakeChannel, new ClientHello(ProtocolVersion, _username, RulesMode, joinAsSpectator));
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

            if (HandleSpectatorServerMessage(message)) return;

            if (message.Channel == LeaveChannel)
            {
                _server?.Disconnect(message.PeerId);
                OnServerPeerDisconnected(message.PeerId);
                return;
            }

            if (message.Channel == HandshakeChannel)
            {
                if (_authenticatedPeers.Contains(message.PeerId)) return;
                var hello = message.GetJson<ClientHello>();
                if (hello is null || hello.ProtocolVersion != ProtocolVersion)
                {
                    Debug.LogWarning($"Peer {message.PeerId} uses an incompatible protocol.");
                    _server?.Disconnect(message.PeerId);
                    return;
                }

                var full = hello.Spectator
                    ? _playerStates.Values.Count(s => s.Team == PlayerTeam.Spectator) >= Math.Clamp(maximumSpectators, 0, 128)
                    : maximumParticipants > 0 && CombatHumanCount >= maximumParticipants;
                if (full) { _server?.Disconnect(message.PeerId); return; }
                _pendingHandshakes.Remove(message.PeerId);
                var username = SanitizeUsername(hello.Username, message.PeerId);
                if (!hello.Spectator && (!IsDefusal || _matchPhase != MatchPhase.Playing)) RemoveOneBot();
                _authenticatedPeers.Add(message.PeerId);
                _peerLastSeenAt[message.PeerId] = _time;
                _peerNames[message.PeerId] = username;
                _playerStates[message.PeerId] = new PlayerMatchState(hello.Spectator ? PlayerTeam.Spectator : ChooseTeam(), false)
                {
                    // A new connection gets its first life even if bots already started the round.
                    // Combat deaths still wait for StartDefusalRound to restore health.
                    Health = hello.Spectator ? 0 : MathF.Max(1.0f, multiplayerMaximumHealth),
                    RespawnAt = hello.Spectator ? float.PositiveInfinity : 0
                };
                if (!hello.Spectator) AddHuman(message.PeerId);
                _server!.SendJson(
                    message.PeerId,
                    HandshakeChannel,
                    new ServerWelcome(
                        ProtocolVersion,
                        message.PeerId,
                        multiplayerMaximumHealth,
                        multiplayerRegenerationDelay,
                        multiplayerRegenerationPerSecond,
                        RulesMode, SceneManager.GetActiveScene().Name, hello.Spectator));
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

            if (message.Channel == LoadoutChannel && _authenticatedPeers.Contains(message.PeerId))
            {
                if (message.GetJson<LoadoutRequest>() is { } request) AcceptLoadout(message.PeerId, request.Loadout);
                return;
            }
            if (message.Channel == BuyChannel && _authenticatedPeers.Contains(message.PeerId))
            {
                if (message.GetJson<PurchaseRequest>() is { } purchase) AcceptPurchase(message.PeerId, purchase.Id);
                return;
            }
            if (message.Channel == GrenadeThrowChannel && _authenticatedPeers.Contains(message.PeerId))
            {
                var request = message.GetJson<GrenadeThrow>();
                if (request is not null) AcceptGrenade(message.PeerId, request);
                return;
            }
            if (message.Channel == BombInputChannel && IsDefusal && _authenticatedPeers.Contains(message.PeerId))
            {
                var input = message.GetJson<BombInput>();
                if (!_playerStates.TryGetValue(message.PeerId, out var bombUser) || !TeamIdentity.IsCombatTeam(bombUser.Team) || bombUser.Health <= 0) return;
                _bombHeldUntil[message.PeerId] = input?.Held == true && _matchPhase == MatchPhase.Playing ? _time + .25f : 0;
                return;
            }

            if (message.Channel == TeamSwitchChannel &&
                _authenticatedPeers.Contains(message.PeerId))
            {
                SwitchTeam(message.PeerId);
                return;
            }

            if (message.Channel == PickupChannel && _authenticatedPeers.Contains(message.PeerId))
            {
                var request = message.GetJson<SupplyRequest>();
                if (request is not null) CollectSupply(message.PeerId, request);
            }
            else if (message.Channel == InputChannel && _authenticatedPeers.Contains(message.PeerId))
            {
                var command = message.GetJson<PlayerCommand>();
                if (command is not null) AcceptInput(message.PeerId, command);
            }
            // Positions and raw shot requests from clients have no authority.

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
            if (HandleGrenadeClientMessage(message) || HandleEconomyClientMessage(message) || HandleSpectatorClientMessage(message)) return;
            if (message.Channel == HandshakeChannel)
            {
                var welcome = message.GetJson<ServerWelcome>();
                if (welcome is null || welcome.ProtocolVersion != ProtocolVersion)
                {
                    Debug.LogError("The host uses an incompatible multiplayer protocol.");
                    _ = _client!.DisconnectAsync();
                    return;
                }

                if (!Enum.IsDefined(welcome.Mode) || !MapCatalog.IsKnownScene(welcome.Scene))
                {
                    Debug.LogError("The server advertised an unsupported map or mode.");
                    _returnToTitle = true;
                    return;
                }
                gameMode = welcome.Mode.ToString();
                MultiplayerLaunch.SetServerMatch(welcome.Mode);
                _localPeerId = welcome.PeerId;
                ApplyLocalSpectatorRole(welcome.Spectator);
                if (SceneManager.GetActiveScene().Name != welcome.Scene)
                {
                    Shutdown();
                    if (!SceneManager.LoadScene(welcome.Scene)) Debug.LogError("Could not load the server map.");
                    return;
                }
                if (float.IsFinite(welcome.MaximumHealth) && welcome.MaximumHealth > 0.0f)
                    _playerHealth?.ConfigureMultiplayerHealthRules(
                        welcome.MaximumHealth,
                        welcome.RegenerationDelay,
                        welcome.RegenerationPerSecond);
                if (!IsNeutralSpectator && UsesPresetLoadouts)
                    _client?.SendJson(LoadoutChannel, new LoadoutRequest(SelectedLoadout));
                Debug.Log($"{_username} joined multiplayer as peer {_localPeerId}.");
                return;
            }

            if (message.Channel == SupplyStateChannel)
            {
                var supplies = message.GetJson<SupplyPosition[]>();
                if (supplies is not null) ApplySupplyState(supplies);
            }
            else if (message.Channel == OwnerStateChannel)
            {
                var state = message.GetJson<OwnerState>();
                if (state is not null) ApplyOwnerState(state);
            }
            else if (message.Channel == TransformChannel)
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
                    _playerHealth?.TakeDamageFrom(damage.Amount, damage.SourceX, damage.SourceY, damage.SourceZ);
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
            else if (message.Channel == BotReloadEffectChannel)
            {
                var effect = message.GetJson<BotReloadEffect>();
                if (effect is not null && float.IsFinite(effect.Duration) && effect.Duration > 0 && effect.Duration <= 30 &&
                    _remotePlayers.TryGetValue(effect.BotId, out var reloadingBot))
                    reloadingBot.GameObject.TryInvoke("PlayExternalReloadAnimation", effect.Duration);
            }
            else if (message.Channel == HitEffectChannel)
            {
                var effect = message.GetJson<HitEffect>();
                if (effect is not null)
                    PlayRemoteHitEffect(effect.VictimPeerId);
            }
            else if (message.Channel == HitConfirmationChannel)
            {
                var confirmation = message.GetJson<HitConfirmation>();
                if (confirmation is not null && confirmation.IsFinite())
                    _playerController?.ConfirmNetworkHit(confirmation.Damage, confirmation.IsHeadshot, confirmation.IsKill, confirmation.HitArmour);
            }
            else if (message.Channel == DeathEffectChannel)
            {
                var effect = message.GetJson<DeathEffect>();
                if (effect is not null)
                    PlayRemoteDeath(effect.PeerId, effect.X, effect.Y, effect.Z);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Ignored invalid server message: {exception.Message}");
        }
    }

    private void ResolveShot(int shooterPeerId, ShotRequest shot)
    {
        if (_server is null || _matchPhase != MatchPhase.Playing || !shot.IsFinite())
            return;

        if (!_playerStates.TryGetValue(shooterPeerId, out var shooterState) ||
            shooterState.Health <= 0.0f)
            return;

        if (IsDefusal && _defusal?.Snapshot().Operator == shooterPeerId) return;
        // Clients send an identity only; combat tuning is always resolved on the host.
        if (!WeaponCatalog.TryFind(shot.WeaponId, out var weapon) &&
            (shot.WeaponId != "val" || _playerController?.HasWeaponLoadout == true)) return;
        var previousId = _lastAcceptedWeapon.GetValueOrDefault(shooterPeerId);
        var minimumInterval = weapon is not null ? WeaponCatalog.MinimumShotInterval(weapon, previousId) :
            60.0f / MathF.Max(1.0f, roundsPerMinute);
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
        _lastAcceptedWeapon[shooterPeerId] = shot.WeaponId;
        PublishShotEffect(shooterPeerId);
        if (!Physics.Raycast(
                origin,
                direction,
                weapon?.Range ?? MathF.Max(1.0f, weaponRange),
                shooterObject,
                out var hit))
            return;

        var targetPeerId = FindPeerForEntity(hit.Entity);
        if (targetPeerId == -1 || targetPeerId == shooterPeerId)
            return;

        if (!_playerStates.TryGetValue(targetPeerId, out var targetState) ||
            targetState.Team == shooterState.Team || targetState.Health <= 0.0f)
            return;

        var isHeadshot = hit.Entity.HasTag("Head");
        var damage = (weapon?.Damage ?? MathF.Max(0.0f, weaponDamage)) *
            (isHeadshot ? MathF.Max(1.0f, multiplayerHeadshotMultiplier) : 1.0f);
        var armourHit = _humans.TryGetValue(targetPeerId, out var targetHuman) && targetHuman.Armour > 0;
        var protectedDamage = ApplyHumanArmour(targetPeerId, damage);
        targetState.Health = MathF.Max(0.0f, targetState.Health - protectedDamage);
        targetState.LastDamagedAt = _time;
        targetState.LastHitDirection = hit.Point - origin;
        PublishHitEffect(targetPeerId);

        ConfirmShooterHit(shooterPeerId, damage, isHeadshot, targetState.Health <= 0, armourHit);

        if (targetState.Health <= 0.0f)
            RegisterKill(shooterPeerId, targetPeerId);
    }

    private int FindPeerForEntity(GameObject entity)
    {
        var entityId = entity.EntityId;
        if (!IsDedicated && GameObject.EntityId == entityId)
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
        if (_deadRemotePlayers.TryGetValue(transform.PeerId, out var respawnAt))
        {
            if (_time < respawnAt)
                return;
            _deadRemotePlayers.Remove(transform.PeerId);
        }

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
        if (transform.WeaponSlot is >= 0 and < WeaponCatalog.SlotCount)
            remote.GameObject.TryInvoke("SetWeaponPose", transform.WeaponSlot == 1 ? 1 : 0);
        remote.TargetPosition = transform.Position;
        remote.TargetYaw = CharacterHeading.Yaw(transform.Rotation);
        if (remote.GameObject.GetComponent<ColliderComponent>() is { } collider)
        {
            if (_server is not null && _humans.ContainsKey(transform.PeerId))
            {
                // Human simulation must use the same capsule as the owning client,
                // rather than the narrower presentation/bot prefab collider.
                collider.Radius = .5f;
                remote.StandingColliderHeight = 2;
                remote.StandingColliderCenter = Vector3.Zero;
            }
            remote.StandingColliderHeight ??= collider.Height;
            remote.StandingColliderCenter ??= collider.Center;
            var height = CombatTargeting.StanceHeight(remote.StandingColliderHeight.Value,
                collider.Radius, .5f, transform.Crouching);
            collider.Height = height;
            collider.Center = remote.StandingColliderCenter.Value - Vector3.UnitY *
                ((remote.StandingColliderHeight.Value - height) * .5f);
        }
    }

    private void OnServerPeerDisconnected(int peerId)
    {
        _pendingHandshakes.Remove(peerId);
        foreach (var id in _grenades.Where(p => p.Value.Owner == peerId).Select(p => p.Key).ToArray())
            _grenades.Remove(id);
        var wasParticipant = _authenticatedPeers.Remove(peerId);
        _peerLastSeenAt.Remove(peerId);
        _lastAcceptedShotAt.Remove(peerId);
        _lastAcceptedWeapon.Remove(peerId);
        var username = _peerNames.Remove(peerId, out var peerName)
            ? peerName
            : $"Peer {peerId}";
        _playerStates.Remove(peerId);
        _humans.Remove(peerId);
        _accounts.Remove(peerId);
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
        foreach (var peer in _pendingHandshakes.ToArray())
            if (_time - peer.Value > 5) _server?.Disconnect(peer.Key);
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
            _server?.Disconnect(peerId);
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
        _spectatorViews.Remove(peerId);
        _spectatorViewBuffers.Remove(peerId);
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
        if (IsDefusal && _matchPhase == MatchPhase.Playing) return;
        if (_server is null || !fillWithBots)
        {
            while (_bots.Count > 0) RemoveOneBot();
            return;
        }

        var humanCount = CombatHumanCount;
        var desired = Math.Clamp(
            Math.Max(0, minimumParticipants) - humanCount,
            0,
            Math.Max(0, maximumBots));
        if (_bots.Count == desired)
            return;
        if (_bots.Count < desired && !EnsureBotPrefabReady())
            return;
        // Prefab construction initializes animation, navigation, physics,
        // scripts, audio, attachments, and UI synchronously. Add or remove at
        // most one bot per update so a full roster does not hitch one frame.
        if (_bots.Count > desired)
            RemoveOneBot();
        else
            AddBot();
        BroadcastMatchState();
    }

    private void TryEnsureBotFill()
    {
        try
        {
            EnsureBotFill();
        }
        catch (Exception exception)
        {
            Debug.LogError($"TDM bot fill failed; the host will retry: {exception.Message}");
        }
    }

    private bool AddBot()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hostBotPrefab))
                return false;
            if (!EnsureBotPrefabReady())
                return false;

            var peerId = _nextBotId--;
            var team = ChooseTeam();
            if (!TryBotSpawnPosition(peerId, out var spawn, team))
            {
                Debug.LogWarning("Could not find a grounded, clear TDM bot spawn; spawn deferred.");
                return false;
            }
            var instance = Prefab.Instantiate(hostBotPrefab, spawn, Vector3.Zero);
            if (instance is null)
            {
                Debug.LogWarning($"Could not instantiate TDM bot prefab '{hostBotPrefab}'.");
                return false;
            }

            _peerNames[peerId] = $"[BOT] Operator {Math.Abs(peerId + 999)}";
            _playerStates[peerId] = new PlayerMatchState(team, true)
            {
                Health = MathF.Max(1.0f, multiplayerMaximumHealth)
            };
            _remotePlayers[peerId] = new RemotePlayer(instance, spawn, 0.0f);
            _bots[peerId] = new BotController(
                instance, spawn, unchecked((uint)Random.Shared.NextInt64(1, 1L << 32)));
            instance.TryInvoke("ResetExternalNavigation", spawn.X, spawn.Y, spawn.Z);
            EquipHardpointBot(peerId, _bots[peerId]);
            PresentBotWeapon(_bots[peerId]);
            Debug.Log($"Added {_peerNames[peerId]} to {team}.");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Could not add TDM bot: {exception.Message}");
            return false;
        }
    }

    private void RemoveOneBot(int? requestedId = null)
    {
        if (_bots.Count == 0) return;
        var peerId = 0;
        foreach (var id in _bots.Keys)
        {
            peerId = id;
            break;
        }
        if (requestedId is { } requested) peerId = requested;
        if (!_bots.ContainsKey(peerId)) return;
        _bots.Remove(peerId, out var removedBot);
        _playerStates.Remove(peerId);
        _humans.Remove(peerId);
        _accounts.Remove(peerId);
        _peerNames.Remove(peerId);
        var proxyTracked = _remotePlayers.ContainsKey(peerId);
        RemoveRemotePlayer(peerId);
        if (!proxyTracked && removedBot is not null && removedBot.GameObject.IsValid)
        {
            removedBot.GameObject.Active = false;
            removedBot.GameObject.Destroy();
        }
        _server?.BroadcastJson(PeerLeftChannel, new PeerLeft(peerId));
    }

    private void SendBotTransforms()
    {
        if (_server is null) return;
        foreach (var pair in _bots)
        {
            _server.BroadcastJson(TransformChannel, PlayerTransform.From(pair.Key, pair.Value.GameObject) with { WeaponSlot = pair.Value.Loadout.Equipment.Weapons.SelectedSlot });
            PublishBotSpectatorView(pair.Key);
        }
    }

    private void UpdateBots(float deltaTime)
    {
        if (_matchPhase != MatchPhase.Playing)
        {
            foreach (var pair in _bots)
            { TickBotEquipment(pair.Key, pair.Value, false); pair.Value.GameObject.TryInvoke("SetExternalCombatPaused", true); }
            return;
        }
        _botPlanner.BeginFrame(_bots.Keys, botThinkBudgetPerFrame,
            id => _playerStates.TryGetValue(id, out var state) && state.Health > 0.0f);
        foreach (var pair in _bots)
        {
            var botId = pair.Key;
            var bot = pair.Value;
            if (!_playerStates.TryGetValue(botId, out var botState) || botState.Health <= 0.0f)
                continue;

            TickBotEquipment(botId, bot, !IsBlinded(bot.GameObject) &&
                (!IsDefusal || _defusal?.Snapshot().Operator != botId));

            var canThink = _botPlanner.CanPlan(botId);
            var currentTargetValid = IsValidBotTarget(botId, bot.TargetPeerId);
            if (!currentTargetValid && bot.TargetPeerId != int.MinValue)
                ClearBotTarget(bot);
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
                if (bot.TargetPeerId != selectedTarget)
                {
                    bot.TargetPeerId = selectedTarget;
                    bot.TacticalSearchActive = false;
                    bot.IsEngaging = false;
                    bot.CachedLineOfSight = false;
                    bot.NextPerceptionAt = 0.0f;
                    bot.NextNavigationAt = 0.0f;
                    bot.HasNavigationDestination = false;
                }
            }

            // Close visible threats take priority over a previously selected
            // chase target. This perception override is intentionally outside
            // the tactical think budget so every bot reacts immediately rather
            // than orbiting another bot while waiting for a selection slot.
            if (_time >= bot.NextPerceptionAt)
            {
                var visibleThreat = FindClosestVisibleOpponentInRange(botId, bot);
                if (visibleThreat != int.MinValue && bot.TargetPeerId != visibleThreat)
                {
                    bot.TargetPeerId = visibleThreat;
                    bot.TacticalSearchActive = false;
                    bot.IsEngaging = false;
                    bot.NextNavigationAt = 0.0f;
                    bot.HasNavigationDestination = false;
                }
                // A visible-threat scan already performed line-of-sight tests.
                // Do not immediately raycast the selected target a second time.
                bot.CachedLineOfSight = visibleThreat != int.MinValue;
                bot.NextPerceptionAt = _time + MathF.Max(0.05f, botPerceptionInterval);
                if (visibleThreat != int.MinValue)
                {
                    bot.CachedLineOfSight = true;
                    bot.LastLineOfSightAt = _time;
                    bot.NextPerceptionAt = _time + MathF.Max(0.05f, botPerceptionInterval);
                }
            }

            // Observe before objective movement can skip combat processing.
            // Stance and burst cooldowns must not bypass target reacquisition.
            bot.TargetReaction.Observe(bot.TargetPeerId, bot.CachedLineOfSight, _time, botReactionTime);

            if (IsDefusal && TryDefusalBotMovement(botId, bot, botState, canThink)) continue;
            if (TryObjectiveMovement(botId, bot, botState, canThink)) continue;
            var targetId = bot.TargetPeerId;
            var targetObject = GetParticipantObject(targetId);
            if (targetObject is null || !targetObject.IsValid)
            {
                continue;
            }

            var botPosition = bot.GameObject.WorldPosition;
            var targetPosition = targetObject.WorldPosition;
            var offset = targetPosition - botPosition;
            offset.Y = 0.0f;
            var distance = offset.Length();
            if (distance < 0.001f) continue;
            var direction = offset / distance;
            var measuredTravel = botPosition - bot.PreviousPosition;
            measuredTravel.Y = 0.0f;
            bot.PreviousPosition = botPosition;
            var travel = bot.Body is not null && !bot.Body.IsKinematic
                ? bot.Body.Velocity
                : (deltaTime > 0.0001f ? measuredTravel / deltaTime : Vector3.Zero);
            travel.Y = 0.0f;
            var movementSpeed = travel.Length();
            var isStationary = movementSpeed <= MathF.Max(0.01f, botStationaryFireSpeed);
            // Perception must not share the tactical path-planning budget.
            // Dictionary iteration is stable, so an early bot could otherwise
            // consume the only think slot every frame and leave later bots with
            // stale visibility while they remain in the aiming pose.
            if (_time >= bot.NextPerceptionAt)
            {
                bot.CachedLineOfSight = HasBotLineOfSight(
                    botId, targetId, bot.GameObject, targetObject);
                if (bot.CachedLineOfSight)
                    bot.LastLineOfSightAt = _time;
                bot.NextPerceptionAt = _time + MathF.Max(0.05f, botPerceptionInterval);
            }
            var hasLineOfSight = bot.CachedLineOfSight;
            if (canThink && bot.TargetReaction.CanFire(_time) && _time >= bot.NextShotAt)
                TryBotThrowable(botId, bot, targetPosition, hasLineOfSight);
            var canEngage = hasLineOfSight && distance <= MathF.Min(bot.Loadout.Equipment.Weapons.Equipped.Range, MathF.Max(1, botAttackRange));

            if (!bot.IsEngaging && canEngage && bot.CombatMovement.CanHold(_time))
            {
                bot.IsEngaging = true;
                bot.CombatMovement.BeginHold(_time,
                    MathF.Max(0.25f, botCombatHoldDuration) * (0.7f + NextBotRandom(bot) * 0.6f));
                bot.ShotsInBurst = 0;
                bot.BurstLimit = Math.Max(1, botBurstSize + (int)(NextBotRandom(bot) * 4) - 1);
                bot.HasNavigationDestination = false;
                // The NavAgent follows a target entity. Moving that target onto
                // the agent does not invalidate its old path because it is
                // already inside stopping distance. A fixed point just beyond
                // that distance forces the circling path to be replaced, after
                // which the bot settles and holds there to fire.
                var holdDistance = MathF.Max(0.8f, botNavigationArrivalDistance * 0.7f);
                bot.CombatHoldPosition = ObjectiveCombatPosition(botId, bot, botPosition + direction * holdDistance);
                var hold = bot.CombatHoldPosition;
                bot.GameObject.TryInvoke(
                    "SetExternalNavigationDestination", hold.X, hold.Y, hold.Z);
                if (bot.Body is not null)
                {
                    var velocity = bot.Body.Velocity;
                    bot.Body.Velocity = new Vector3(0.0f, velocity.Y, 0.0f);
                }
            }
            else if (bot.IsEngaging &&
                     (bot.CombatMovement.HoldExpired(_time) ||
                      (_time > bot.LastLineOfSightAt + MathF.Max(0.0f, botLostSightGrace)) ||
                      distance > MathF.Max(1.0f, botAttackRange) * 1.1f))
            {
                bot.IsEngaging = false;
                bot.NextNavigationAt = 0.0f;
                bot.HasNavigationDestination = false;
                bot.TacticalSearchActive = false;
                bot.CombatMovement.BeginReposition(_time, botRepositionDuration);
                // A short lateral step starts repositioning immediately; the
                // budgeted search can refine it while the native agent moves.
                bot.StrafeSign = NextBotRandom(bot) < 0.5f ? -1.0f : 1.0f;
                var side = new Vector3(-direction.Z, 0.0f, direction.X) * bot.StrafeSign;
                var desired = ObjectiveCombatPosition(botId, bot, botPosition + side * (2.0f + NextBotRandom(bot) * 3.0f));
                var destination = TryProjectBotNavigationPosition(desired, out var projected)
                    ? projected : desired;
                SetBotDestination(bot, destination, targetPosition);
            }

            var navigationDistance = bot.HasNavigationDestination
                ? HorizontalDistance(botPosition, bot.NavigationDestination)
                : float.MaxValue;
            var arrived = bot.HasNavigationDestination &&
                navigationDistance <= MathF.Max(0.1f, botNavigationArrivalDistance);
            // Start the incremental tactical search before the current path is
            // exhausted. With several bots sharing the think budget, waiting
            // until arrival produced a conspicuous move-stop-move cadence.
            var nearingDestination = bot.HasNavigationDestination &&
                navigationDistance <= MathF.Max(
                    MathF.Max(0.1f, botNavigationArrivalDistance),
                    MathF.Max(2.0f, botPreferredRange * 0.35f));
            if (bot.IsEngaging || !bot.HasNavigationDestination || arrived ||
                HorizontalDistance(botPosition, bot.ProgressPosition) >= 0.5f)
            {
                bot.ProgressPosition = botPosition;
                bot.LastProgressAt = _time;
            }
            if (!bot.IsEngaging && bot.HasNavigationDestination && !arrived &&
                _time >= bot.LastProgressAt + MathF.Max(0.5f, botStuckTimeout))
            {
                bot.HasNavigationDestination = false;
                bot.TacticalSearchActive = false;
                bot.NextNavigationAt = 0.0f;
                bot.LastProgressAt = _time;
            }
            var targetMovedSincePlan = bot.HasNavigationDestination &&
                HorizontalDistance(targetPosition, bot.TargetPositionAtPlan) >=
                MathF.Max(1.0f, botTargetReplanDistance);

            if (canThink && !bot.IsEngaging &&
                (!bot.HasNavigationDestination || nearingDestination || targetMovedSincePlan ||
                 _time >= bot.NavigationMoveDeadline) &&
                _time >= bot.NextNavigationAt)
            {
                // Pursue immediately rather than waiting motionless for all
                // tactical samples. Native navigation owns path completeness,
                // partial routes, avoidance, and swept collision movement.
                if (!bot.HasNavigationDestination)
                {
                    var chase = TryProjectBotNavigationPosition(targetPosition, out var projected)
                        ? projected : targetPosition;
                    SetBotDestination(bot, chase, targetPosition);
                }
                var searchResult = AdvanceBotTacticalDestinationSearch(
                    botId, bot, targetId, targetObject, out var destination);
                if (searchResult == TacticalSearchResult.Found)
                    SetBotDestination(bot, destination, targetPosition);
                if (searchResult != TacticalSearchResult.Pending)
                    bot.NextNavigationAt = _time + MathF.Max(0.05f, botNavigationRefreshInterval);
                if (searchResult == TacticalSearchResult.Failed && arrived)
                    bot.HasNavigationDestination = false;
            }
            // Once engaging, the fixed combat hold point owns locomotion and
            // scripted rotation owns aim. Do not wait for an exact zero-speed
            // reading: local avoidance can leave tiny corrections indefinitely.
            if (bot.IsEngaging || isStationary)
                TurnBotTowards(bot, direction, deltaTime);
            var facingTarget = IsBotFacing(bot.GameObject, direction, botFiringAngle);
            // Shared equipment has already advanced reloads before fire decisions.
            var isReloading = bot.Loadout.Equipment.Reloading;
            bot.GameObject.TryInvoke(
                "SetExternalAiming",
                bot.IsEngaging && !isReloading && hasLineOfSight);

            if (_remotePlayers.TryGetValue(botId, out var remote))
            {
                remote.TargetPosition = botPosition;
                remote.TargetYaw = CharacterHeading.Yaw(bot.GameObject.RotationQuaternion);
            }

            if (bot.IsEngaging && !isReloading && facingTarget && hasLineOfSight &&
                bot.TargetReaction.CanFire(_time) && _time >= bot.NextShotAt)
            {
                if (bot.Loadout.Equipment.TryFire(true, _time, !IsBlinded(bot.GameObject)))
                {
                    BotFire(botId, targetObject, bot);
                    bot.ShotsInBurst++;
                    bot.NextShotAt = _time + bot.Loadout.Equipment.Weapons.Equipped.ShotInterval;
                    if (bot.ShotsInBurst >= bot.BurstLimit)
                    {
                        bot.NextShotAt += MathF.Max(0, botBurstPause) * (.75f + NextBotRandom(bot) * .5f);
                        bot.BurstLimit = Math.Max(1, botBurstSize + (int)(NextBotRandom(bot) * 4) - 1);
                        bot.ShotsInBurst = 0;
                    }
                }
            }
        }
    }

    private void SetBotDestination(BotController bot, Vector3 destination, Vector3 targetPosition)
    {
        bot.GameObject.TryInvoke("SetExternalNavigationDestination",
            destination.X, destination.Y, destination.Z);
        bot.NavigationDestination = destination;
        bot.TargetPositionAtPlan = targetPosition;
        // Routine objective refreshes must not erase the stuck watchdog.
        if (!bot.HasNavigationDestination)
        {
            bot.ProgressPosition = bot.GameObject.WorldPosition;
            bot.LastProgressAt = _time;
        }
        bot.HasNavigationDestination = true;
        bot.NavigationMoveDeadline = _time + MathF.Max(1.0f, botNavigationMoveTimeout);
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

    private void PreloadNetworkPrefabs()
    {
        _botPrefabReady = EnsureBotPrefabReady();
        if (!string.IsNullOrWhiteSpace(remotePlayerPrefab) &&
            !Prefab.IsReady(remotePlayerPrefab) &&
            !Prefab.Preload(remotePlayerPrefab))
            Debug.LogWarning($"Could not preload network player prefab '{remotePlayerPrefab}'.");
    }

    private bool EnsureBotPrefabReady()
    {
        if (string.IsNullOrWhiteSpace(hostBotPrefab))
            return false;
        if (_botPrefabReady && Prefab.IsReady(hostBotPrefab))
            return true;

        _botPrefabReady = Prefab.IsReady(hostBotPrefab) || Prefab.Preload(hostBotPrefab);
        if (!_botPrefabReady)
            Debug.LogWarning($"Bot prefab '{hostBotPrefab}' is not ready; spawn deferred.");
        return _botPrefabReady;
    }

    private int FindClosestVisibleOpponentInRange(int botId, BotController bot)
    {
        if (!_playerStates.TryGetValue(botId, out var source))
            return int.MinValue;

        var bestId = int.MinValue;
        var bestDistance = MathF.Max(1.0f, botAttackRange);
        foreach (var pair in _playerStates)
        {
            if (pair.Key == botId || pair.Value.Team == source.Team || pair.Value.Health <= 0.0f)
                continue;
            var candidate = GetParticipantObject(pair.Key);
            if (candidate is null || !candidate.IsValid)
                continue;
            var distance = HorizontalDistance(bot.GameObject.WorldPosition, candidate.WorldPosition);
            if (distance > bestDistance ||
                !HasBotLineOfSight(botId, pair.Key, bot.GameObject, candidate))
                continue;
            bestDistance = distance;
            bestId = pair.Key;
        }
        return bestId;
    }

    private TacticalSearchResult AdvanceBotTacticalDestinationSearch(
        int botId, BotController bot, int targetId, GameObject target, out Vector3 destination)
    {
        destination = bot.GameObject.WorldPosition;
        var samples = Math.Clamp(botTacticalPositionSamples, 4, 32);
        var radius = MathF.Max(2.0f, botPreferredRange * bot.RangeMultiplier);
        var targetPosition = target.WorldPosition;
        if (!bot.TacticalSearchActive || bot.TacticalSearchTargetId != targetId ||
            HorizontalDistance(targetPosition, bot.TacticalSearchTargetPosition) >=
                MathF.Max(1.0f, botTargetReplanDistance))
        {
            bot.TacticalSearchActive = true;
            bot.TacticalSearchTargetId = targetId;
            bot.TacticalSearchTargetPosition = targetPosition;
            bot.TacticalSearchOrigin = bot.GameObject.WorldPosition;
            bot.TacticalSampleIndex = 0;
            bot.TacticalAngleOffset = NextBotRandom(bot) * 360.0f;
            bot.TacticalBestScore = float.MaxValue;
            bot.TacticalFound = false;
        }

        // Projecting and visibility-testing every candidate in one update was
        // the source of millisecond host spikes. Evaluate two positions per
        // think slot and retain the best result across frames.
        var sampleEnd = Math.Min(samples, bot.TacticalSampleIndex + 2);
        for (; bot.TacticalSampleIndex < sampleEnd; bot.TacticalSampleIndex++)
        {
            var index = bot.TacticalSampleIndex;
            var angleOffset = bot.TacticalAngleOffset +
                (bot.StrafeSign < 0.0f ? 180.0f / samples : 0.0f);
            var angle = (angleOffset + index * (360.0f / samples)) * MathF.PI / 180.0f;
            var desired = bot.TacticalSearchTargetPosition + new Vector3(
                MathF.Sin(angle) * radius, 0.0f, MathF.Cos(angle) * radius);
            desired = ObjectiveCombatPosition(botId, bot, desired);
            if (!TryProjectBotNavigationPosition(desired, out var candidate))
                continue;

            var targetDistance = HorizontalDistance(candidate, bot.TacticalSearchTargetPosition);
            if (targetDistance > MathF.Max(radius, botAttackRange))
                continue;
            var rangeError = MathF.Abs(targetDistance - radius);
            var travelDistance = Vector3.Distance(bot.TacticalSearchOrigin, candidate);
            if (travelDistance <= MathF.Max(
                    2.0f, MathF.Max(0.1f, botNavigationArrivalDistance) * 2.0f))
                continue;
            var centreDistance = navigationMesh is null
                ? 0.0f
                : HorizontalDistance(candidate, navigationMesh.WorldPosition);
            var score = rangeError * 4.0f + travelDistance * 0.35f +
                centreDistance * MathF.Max(0.0f, botCentrePositionWeight);
            // Prefer separate firing lanes over stacking allies on one point.
            foreach (var ally in _bots.Values)
            {
                if (ReferenceEquals(ally, bot) ||
                    !ally.HasNavigationDestination || ally.TargetPeerId != targetId) continue;
                var separation = HorizontalDistance(candidate, ally.NavigationDestination);
                score += MathF.Max(0.0f, 4.0f - separation) * 8.0f;
            }
            if (HasLineOfSightFrom(candidate, targetId, target))
                score -= 40.0f;
            if (score >= bot.TacticalBestScore) continue;
            bot.TacticalBestScore = score;
            bot.TacticalBestDestination = candidate;
            bot.TacticalFound = true;
        }

        if (bot.TacticalSampleIndex < samples)
            return TacticalSearchResult.Pending;

        bot.TacticalSearchActive = false;
        if (!bot.TacticalFound)
            return TacticalSearchResult.Failed;
        destination = bot.TacticalBestDestination;
        return TacticalSearchResult.Found;
    }

    private bool HasLineOfSightFrom(Vector3 position, int targetId, GameObject target)
    {
        var origin = position + Vector3.UnitY * 0.65f;
        var aimPoint = ParticipantAimPoint(target);
        if (SightBlocked(origin, aimPoint)) return false;
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
        var blend = 1.0f - MathF.Exp(-MathF.Max(0.0f, botTurnSharpness) * MathF.Max(0.0f, deltaTime));
        // Euler Y folds at +/-90 degrees. Quaternion interpolation can turn
        // through every heading without repeatedly reversing before alignment.
        var desired = Quaternion.CreateFromAxisAngle(Vector3.UnitY, desiredYaw * MathF.PI / 180);
        controller.GameObject.RotationQuaternion = Quaternion.Slerp(controller.GameObject.RotationQuaternion, desired, blend);
    }

    private static Vector3 ParticipantAimPoint(GameObject participant)
    {
        var collider = participant.GetComponent<ColliderComponent>();
        return CombatTargeting.AimPoint(participant.WorldPosition, collider?.Center ?? Vector3.Zero, collider?.Height ?? 1.5f);
    }
    private Vector3 ObjectiveCombatPosition(int botId, BotController bot, Vector3 desired)
    {
        if (_hardpoint is null || !_playerStates.TryGetValue(botId, out var state) ||
            BotObjectiveTactics.Role(botId) == BotRole.Flanker ||
            (!_hardpoint.Contains(bot.GameObject.WorldPosition) && _hardpoint.Owner != state.Team)) return desired;
        return CombatTargeting.CapturePosition(desired, _hardpoint.Snapshot());
    }

    private bool HasBotLineOfSight(
        int botId, int targetId, GameObject bot, GameObject target)
    {
        if (IsBlinded(bot)) return false;
        var origin = bot.WorldPosition + Vector3.UnitY * 0.65f;
        var aimPoint = ParticipantAimPoint(target);
        if (SightBlocked(origin, aimPoint)) return false;
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
        => _playerStates.TryGetValue(peerId, out var participant) && !TeamIdentity.IsCombatTeam(participant.Team) ? null : peerId == 0 ? (IsDedicated ? null : GameObject) : _remotePlayers.GetValueOrDefault(peerId)?.GameObject;

    private void BotFire(int botId, GameObject intendedTarget, BotController bot)
    {
        if (!IsBotFacing(bot.GameObject, intendedTarget.WorldPosition - bot.GameObject.WorldPosition, botFiringAngle) ||
            !HasBotLineOfSight(botId, bot.TargetPeerId, bot.GameObject, intendedTarget)) return;
        var origin = ParticipantAimPoint(bot.GameObject);
        var direction = ParticipantAimPoint(intendedTarget) - origin;
        if (direction.LengthSquared() < .001f) return;
        direction = ApplyBotSpread(Vector3.Normalize(direction), botAccuracyDegrees / BotPressure, bot);
        ResolveShot(botId, ShotRequest.From(origin, direction, bot.Loadout.Equipment.Weapons.Equipped.Id));
    }

    private void PublishShotEffect(int shooterPeerId)
    {
        if (_bots.TryGetValue(shooterPeerId, out var bot)) bot.SpectatorShots++;
        PlayRemoteShotEffect(shooterPeerId);
        _server?.BroadcastJson(ShotEffectChannel, new ShotEffect(shooterPeerId));
    }

    private void PublishBotReload(int botId, BotController bot)
    {
        var duration = bot.Loadout.Equipment.Weapons.Equipped.ReloadSeconds;
        if (!IsDedicated) bot.GameObject.TryInvoke("PlayExternalReloadAnimation", duration);
        _server?.BroadcastJson(BotReloadEffectChannel, new BotReloadEffect(botId, duration));
    }

    private void PlayRemoteShotEffect(int shooterPeerId)
    {
        if (IsDedicated) return;
        if (_remotePlayers.TryGetValue(shooterPeerId, out var remote))
            remote.GameObject.TryInvoke("PlayExternalShootAnimation");
    }

    private void PublishHitEffect(int victimPeerId)
    {
        PlayRemoteHitEffect(victimPeerId);
        _server?.BroadcastJson(HitEffectChannel, new HitEffect(victimPeerId));
    }

    private void ConfirmShooterHit(int shooterPeerId, float damage, bool isHeadshot, bool isKill, bool hitArmour)
    {
        if (shooterPeerId == 0)
        {
            _playerController?.ConfirmNetworkHit(damage, isHeadshot, isKill, hitArmour);
            return;
        }
        if (!_bots.ContainsKey(shooterPeerId))
            _server?.SendJson(
                shooterPeerId,
                HitConfirmationChannel,
                new HitConfirmation(damage, isHeadshot, isKill, hitArmour));
    }

    private void PlayRemoteHitEffect(int victimPeerId)
    {
        if (IsDedicated) return;
        if (_remotePlayers.TryGetValue(victimPeerId, out var remote))
            remote.GameObject.TryInvoke("PlayExternalHitAnimation");
    }

    private void PlayRemoteDeath(int victimPeerId, float x = 0, float y = 0, float z = 0)
    {
        _spectatorViews.Remove(victimPeerId);
        _spectatorViewBuffers.Remove(victimPeerId);
        _deadRemotePlayers[victimPeerId] =
            IsDefusal ? float.PositiveInfinity : _time + MathF.Max(0.1f, combatRespawnDelay);
        if (_server is not null && _remotePlayers.TryGetValue(victimPeerId, out var serverActor))
        {
            if (IsDedicated) serverActor.GameObject.Active = false;
            else serverActor.GameObject.TryInvoke("PlayRetainedExternalDeathWithImpulse", x, y, z);
            return;
        }
        if (_remotePlayers.Remove(victimPeerId, out var remote))
            remote.GameObject.TryInvoke("PlayExternalDeathWithImpulse", x, y, z);
    }

    private void ConfigureAllNameplates()
    {
        foreach (var pair in _remotePlayers)
            ConfigureNameplate(pair.Key, pair.Value.GameObject);
    }

    private void ConfigureNameplate(int peerId, GameObject proxy)
    {
        var friendly = ArePeersFriendly(_localPeerId, peerId);
        var username = _knownPlayers.TryGetValue(peerId, out var player)
            ? player.Username
            : string.Empty;
        proxy.TryInvoke("ConfigureNetworkNameplate", username, friendly, player is null ? -1 : (int)player.Team);
    }

    private static float NextBotRandom(BotController bot)
    {
        bot.RandomState = unchecked(bot.RandomState * 1664525u + 1013904223u);
        return (bot.RandomState & 0x00ffffffu) / 16777216.0f;
    }

    private static Vector3 ApplyBotSpread(Vector3 direction, float degrees, BotController bot)
    {
        float Next()
        {
            return NextBotRandom(bot);
        }
        var tangent = MathF.Tan(MathF.Max(0.0f, degrees) * MathF.PI / 180.0f);
        var right = Vector3.Cross(direction, Vector3.UnitY);
        if (right.LengthSquared() < 0.001f) right = Vector3.UnitX;
        else right = Vector3.Normalize(right);
        return Vector3.Normalize(direction + right * ((Next() * 2.0f - 1.0f) * tangent) +
            Vector3.UnitY * ((Next() * 2.0f - 1.0f) * tangent));
    }

    private bool TryBotSpawnPosition(int peerId, out Vector3 spawnPosition, PlayerTeam? assignedTeam = null)
    {
        if (IsDefusal)
        {
            spawnPosition = DefusalSpawn(_playerStates.TryGetValue(peerId, out var state) ? state.Team : ChooseTeam(), peerId);
            return true;
        }
        spawnPosition = default;
        var index = Math.Abs(peerId + 1000);
        string[] spawnNames =
        [
            "Spawn North West",
            "Spawn North East",
            "Spawn South East",
            "Spawn South West"
        ];
        // Rotate through every arena marker and reject stale or occupied points.
        // The sequence also varies respawns instead of returning to one corner.
        var start = (index + (int)(++_botSpawnSequence % (uint)spawnNames.Length)) % spawnNames.Length;
        for (var offset = 0; offset < spawnNames.Length; offset++)
        {
            var marker = GameObject.Find(spawnNames[(start + offset) % spawnNames.Length]);
            if (marker is null || !marker.IsValid) continue;
            if (BotSpawnResolver.TryResolve(marker.WorldPosition,
                    ProjectBotSpawn, PlaceBotSpawn,
                    position => IsBotSpawnClear(peerId, position, assignedTeam), out spawnPosition))
                return true;
        }

        if (TryRandomBotSpawnPosition(peerId, out var randomSpawn, assignedTeam))
        {
            spawnPosition = randomSpawn;
            return true;
        }

        return false;
    }

    public bool TrySelectLocalRespawn(out Vector3 spawn) => TryBotSpawnPosition(0, out spawn, PlayerTeam.Alpha);

    private Vector3? ProjectBotSpawn(Vector3 position) =>
        TryProjectBotNavigationPosition(position, out var projected) ? projected : null;

    private Vector3? PlaceBotSpawn(Vector3 position) =>
        TryPlaceBotAboveGround(position, out var spawn) ? spawn : null;

    private bool TryRandomBotSpawnPosition(int peerId, out Vector3 spawn, PlayerTeam? assignedTeam = null)
    {
        spawn = default;
        if (navigationMesh is null || !navigationMesh.IsValid)
            return false;

        var origin = GameObject.WorldPosition;
        var minimumDistance = MathF.Max(0.0f, botFallbackSpawnMinimumDistance);
        var radius = MathF.Max(minimumDistance, botFallbackSpawnRadius);
        var attempts = Math.Clamp(botFallbackSpawnAttempts, 1, 128);
        // Include a monotonically changing sequence so a bot does not receive
        // the same deterministic sample every time it respawns.
        var spawnSequence = ++_botSpawnSequence;
        var randomState = unchecked((uint)peerId * 747796405u +
                                    spawnSequence * 2891336453u + 1013904223u);
        float NextRandom()
        {
            randomState = randomState * 1664525u + 1013904223u;
            return (randomState & 0x00ffffffu) / 16777216.0f;
        }

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var angle = NextRandom() * MathF.PI * 2.0f;
            // Uniformly distribute samples by area across the permitted ring.
            var minimumSquared = minimumDistance * minimumDistance;
            var distance = MathF.Sqrt(minimumSquared +
                NextRandom() * (radius * radius - minimumSquared));
            var candidate = origin + new Vector3(
                MathF.Cos(angle) * distance, 0.0f, MathF.Sin(angle) * distance);
            if (!Navigation.ProjectPoint(navigationMesh, candidate, out var projected,
                    botNavigationAgentRadius, botNavigationAgentHeight))
                continue;
            if (HorizontalDistance(origin, projected) < minimumDistance)
                continue;

            if (!IsBotSpawnClear(peerId, projected, assignedTeam))
                continue;

            if (TryPlaceBotAboveGround(projected, out spawn))
                return true;
        }
        return false;
    }

    private bool IsBotSpawnClear(int peerId, Vector3 position, PlayerTeam? assignedTeam = null)
    {
        var team = assignedTeam ?? (_playerStates.TryGetValue(peerId, out var spawning) ? spawning.Team : PlayerTeam.Alpha);
        if (peerId != 0 && !IsDedicated && !_playerStates.ContainsKey(0) &&
            !BotSpawnResolver.HasClearance(position, team, GameObject.WorldPosition, PlayerTeam.Alpha,
                _playerHealth?.CurrentHealth ?? 0)) return false;
        foreach (var pair in _playerStates)
        {
            if (pair.Key == peerId || pair.Value.Health <= 0.0f || !TeamIdentity.IsCombatTeam(pair.Value.Team)) continue;
            var participant = GetParticipantObject(pair.Key);
            if (participant is null || !participant.IsValid) continue;
            if (!BotSpawnResolver.HasClearance(position, team, participant.WorldPosition, pair.Value.Team, pair.Value.Health))
                return false;
        }
        return true;
    }

    private bool TryPlaceBotAboveGround(Vector3 position, out Vector3 spawn)
    {
        spawn = default;
        // A baked navigation point can lag behind edited terrain height. Use
        // physics for the authoritative surface at the selected walkable X/Z.
        const float rayHeight = 256.0f;
        var rayOrigin = new Vector3(position.X, position.Y + rayHeight, position.Z);
        if (!Physics.Raycast(rayOrigin, -Vector3.UnitY, rayHeight * 2.0f, out var ground) ||
            ground.Normal.Y <= 0.1f)
            return false;

        var rootClearance = MathF.Max(0.0f, botNavigationAgentHeight) * 0.5f +
                            MathF.Max(0.0f, botFallbackSpawnGroundClearance);
        // Use the greater of the navigation and collision heights. Terrain
        // edits can temporarily leave either representation stale; spawning
        // above both and letting kinematic gravity settle the bot is safe in
        // either direction and prevents an underground initial frame.
        var surfaceHeight = MathF.Max(position.Y, ground.Point.Y);
        spawn = new Vector3(position.X, surfaceHeight + rootClearance, position.Z);
        return true;
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

    private void UpdateMatch(float deltaTime)
    {
        if (IsDefusal)
        {
            TickDefusal(deltaTime);
            return;
        }
        foreach (var pair in _playerStates)
        {
            var state = pair.Value;
            if (!TeamIdentity.IsCombatTeam(state.Team)) continue;
            if (state.Health <= 0.0f && _time >= state.RespawnAt)
            {
                if (!TryBotSpawnPosition(pair.Key, out var safeSpawn))
                { state.RespawnAt = _time + .25f; continue; }
                state.Health = MathF.Max(1.0f, multiplayerMaximumHealth);
                ResetGrenadeSupply(pair.Key);
                if (_bots.TryGetValue(pair.Key, out var bot))
                {
                    bot = RespawnBot(pair.Key, bot, safeSpawn);
                    bot.NextShotAt = _time + 0.5f;
                    bot.NextNavigationAt = _time + 0.5f;
                    bot.PreviousPosition = bot.SpawnPosition;
                    bot.IsEngaging = false;
                    bot.TargetPeerId = int.MinValue;
                }
                if (_humans.ContainsKey(pair.Key)) SpawnHuman(pair.Key, safeSpawn);
                _deadRemotePlayers.Remove(pair.Key);
            }
            else if (state.Health > 0.0f &&
                     state.Health < multiplayerMaximumHealth &&
                     _time >= state.LastDamagedAt + MathF.Max(0.0f, multiplayerRegenerationDelay))
                state.Health = MathF.Min(
                    multiplayerMaximumHealth,
                    state.Health + MathF.Max(0.0f, multiplayerRegenerationPerSecond) * deltaTime);
        }


        UpdateObjective(MathF.Min(deltaTime, MathF.Max(0, _phaseEndsAt - _time + deltaTime)));

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
        ClearGrenades();
        foreach (var owner in _playerStates.Where(p => TeamIdentity.IsCombatTeam(p.Value.Team)).Select(p => p.Key)) ResetGrenadeSupply(owner);
        _alphaScore = 0;
        _bravoScore = 0;
        foreach (var state in _playerStates.Values.Where(s => TeamIdentity.IsCombatTeam(s.Team)))
        {
            state.Kills = 0;
            state.Deaths = 0;
            state.ObjectiveSeconds = 0;
            state.Health = MathF.Max(1.0f, multiplayerMaximumHealth);
        }
        // Recreate controller state and restore proxy tracking even when a bot
        // died near the round boundary and its corpse has already been removed.
        foreach (var peerId in new List<int>(_bots.Keys))
        {
            var bot = RespawnBot(peerId, _bots[peerId]);
            bot.NextShotAt = _time + 0.5f;
            bot.NextNavigationAt = _time + 0.5f;
            ClearBotTarget(bot);
            _deadRemotePlayers.Remove(peerId);
        }
        ResetHumansForRound();
        BeginObjectiveRound();
        BeginPhase(MatchPhase.Playing, matchDuration);
    }

    private void BeginPhase(MatchPhase phase, float duration)
    {
        if (phase == MatchPhase.Results && _matchPhase != MatchPhase.Results && !IsDefusal)
            RewardRound(_alphaScore == _bravoScore ? null : _alphaScore > _bravoScore ? PlayerTeam.Alpha : PlayerTeam.Bravo);
        if (phase == MatchPhase.Playing) _buyEndsAt = _time + EconomyRules.BuySeconds;
        _matchPhase = phase;
        if (IsDefusal && phase == MatchPhase.Warmup) PrepareDefusalSpawns();
        Debug.Log($"Match phase: {phase}; score {_alphaScore}-{_bravoScore}.");
        foreach (var bot in _bots.Values) bot.GameObject.TryInvoke("SetExternalCombatPaused", phase != MatchPhase.Playing);
        _phaseEndsAt = _time + MathF.Max(0.1f, duration);
        BroadcastMatchState();
    }

    private void RegisterKill(int killerPeerId, int victimPeerId)
    {
        if (!_playerStates.TryGetValue(killerPeerId, out var killer) ||
            !_playerStates.TryGetValue(victimPeerId, out var victim))
            return;

        killer.Kills++;
        if (!UsesPresetLoadouts && killerPeerId != victimPeerId && killer.Team != victim.Team)
        { Account(killerPeerId).Credit(EconomyRules.KillReward); SendEconomy(killerPeerId); }
        if (!UsesPresetLoadouts) { Account(victimPeerId).LoseEquipment(); SendEconomy(victimPeerId); }
        victim.Deaths++;
        victim.RespawnAt = IsDefusal ? float.PositiveInfinity : _time + MathF.Max(0.1f, combatRespawnDelay);
        var impulse = victim.LastHitDirection;
        PlayRemoteDeath(victimPeerId, impulse.X, impulse.Y, impulse.Z);
        _server?.BroadcastJson(DeathEffectChannel, new DeathEffect(victimPeerId, impulse.X, impulse.Y, impulse.Z));
        if (RulesMode == MatchMode.TeamDeathmatch)
        {
            if (killer.Team == PlayerTeam.Alpha) _alphaScore++;
            else _bravoScore++;
        }

        var entry = new KillFeedEntry(
            _peerNames.GetValueOrDefault(killerPeerId, $"Player{killerPeerId}"),
            _peerNames.GetValueOrDefault(victimPeerId, $"Player{victimPeerId}"),
            killer.Team);
        _server?.BroadcastJson(KillFeedChannel, entry);
        KillFeedReceived?.Invoke(entry);
        BroadcastMatchState();

        if (!IsDefusal && (_alphaScore >= Math.Max(1, scoreLimit) || _bravoScore >= Math.Max(1, scoreLimit)))
            BeginPhase(MatchPhase.Results, resultsDuration);
    }

    private PlayerTeam ChooseTeam()
    {
        var alpha = 0;
        var bravo = 0;
        foreach (var state in _playerStates.Values)
        {
            if (state.Team == PlayerTeam.Alpha) alpha++;
            else if (state.Team == PlayerTeam.Bravo) bravo++;
        }
        return alpha <= bravo ? PlayerTeam.Alpha : PlayerTeam.Bravo;
    }

    private void SwitchTeam(int peerId)
    {
        if (IsDefusal) return;
        if (!_playerStates.TryGetValue(peerId, out var state) || state.IsBot || !TeamIdentity.IsCombatTeam(state.Team))
            return;

        state.Team = state.Team == PlayerTeam.Alpha ? PlayerTeam.Bravo : PlayerTeam.Alpha;
        foreach (var bot in _bots.Values)
            ClearBotTarget(bot);
        BroadcastMatchState();
        Debug.Log($"{_peerNames.GetValueOrDefault(peerId, $"Player{peerId}")} switched to {state.Team}.");
    }

    private static void ClearBotTarget(BotController bot)
    {
        bot.TargetPeerId = int.MinValue;
        bot.IsEngaging = false;
        bot.CachedLineOfSight = false;
        bot.NextTargetSelectionAt = 0.0f;
        bot.NextPerceptionAt = 0.0f;
        bot.NextNavigationAt = 0.0f;
        bot.HasNavigationDestination = false;
        bot.TacticalSearchActive = false;
        bot.CombatMovement.Reset();
        bot.TargetReaction.Reset();
        bot.LastLineOfSightAt = float.MinValue;
        bot.GameObject.TryInvoke("SetExternalAiming", false);
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
                state.IsBot, state.ObjectiveSeconds, state.Health > 0);
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
            IsDefusal && _matchPhase == MatchPhase.Playing ? _defusal?.Remaining ?? 0 : MathF.Max(0.0f, _phaseEndsAt - _time),
            Math.Max(1, scoreLimit),
            _alphaScore,
            _bravoScore,
            _localPeerId,
            players, RulesMode, _hardpoint?.Snapshot(), _defusal?.Snapshot(_alphaScore >= scoreLimit || _bravoScore >= scoreLimit));
        PublishMatch(snapshot);
        _server?.BroadcastJson(MatchStateChannel, snapshot);
    }

    public int? SelectSpectatorTarget(int? current, int direction) => SpectatorRules.Select(
        CurrentMatch, current, direction, id => _remotePlayers.TryGetValue(id, out var remote) && remote.GameObject.IsValid &&
            (_spectatorViews.TryGetValue(id, out var cached) && _time - cached.Received < 1), SpectatorTeam);

    public bool TryGetSpectatorActor(int peer, out GameObject? actor)
    {
        actor = null;
        if (SelectSpectatorTarget(peer, 0) != peer || !_remotePlayers.TryGetValue(peer, out var remote)) return false;
        actor = remote.GameObject;
        return true;
    }

    private void PublishMatch(MatchSnapshot snapshot)
    {
        CurrentMatch = snapshot;
        UpdateLocalRound(snapshot);
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

    private sealed record ClientHello(int ProtocolVersion, string Username, MatchMode Mode, bool Spectator = false);
    private sealed record ServerWelcome(
        int ProtocolVersion,
        int PeerId,
        float MaximumHealth = 100.0f,
        float RegenerationDelay = 5.0f,
        float RegenerationPerSecond = 6.0f,
        MatchMode Mode = MatchMode.Hardpoint, string Scene = "Main", bool Spectator = false);
    private sealed record PeerLeft(int PeerId);
    private sealed record PeerJoined(int PeerId, string Username);
    private sealed record PlayerDamage(float Amount, float SourceX = 0, float SourceY = 0, float SourceZ = 0);
    private sealed record ShotEffect(int ShooterPeerId);
    private sealed record BotReloadEffect(int BotId, float Duration);
    private sealed record HitEffect(int VictimPeerId);
    private sealed record DeathEffect(int PeerId, float X = 0, float Y = 0, float Z = 0);
    private sealed record TeamSwitchRequest;
    private sealed record HitConfirmation(float Damage, bool IsHeadshot, bool IsKill, bool HitArmour)
    {
        public bool IsFinite() => float.IsFinite(Damage) && Damage > 0.0f;
    }

    private BotController RespawnBot(int peerId, BotController previous, Vector3? selectedSpawn = null)
    {
        var respawnPosition = selectedSpawn.GetValueOrDefault();
        if (selectedSpawn is null && !TryBotSpawnPosition(peerId, out respawnPosition))
        {
            if (_playerStates.TryGetValue(peerId, out var waiting))
            { waiting.Health = 0; waiting.RespawnAt = _time + .25f; }
            return previous;
        }
        var instance = previous.GameObject.IsValid
            ? previous.GameObject
            : Prefab.Instantiate(hostBotPrefab, respawnPosition, Vector3.Zero);
        if (instance is null || !instance.IsValid)
        {
            Debug.LogWarning($"Could not respawn bot {peerId} from '{hostBotPrefab}'.");
            if (_playerStates.TryGetValue(peerId, out var waiting))
            { waiting.Health = 0; waiting.RespawnAt = _time + .25f; }
            return previous;
        }

        // Reuse the retained corpse instead of reconstructing the heavyweight
        // enemy prefab (animation graph, meshes, ragdoll, UI and audio) on every
        // respawn. Invalid instances still take the instantiate fallback above.
        instance.Active = true;
        instance.WorldPosition = respawnPosition;
        if (IsDefusal && _playerStates.TryGetValue(peerId, out var respawnState))
            instance.Rotation = new Vector3(0, DefusalRules.SpawnYaw(respawnState.Team == AttackingTeam), 0);
        if (instance.GetComponent<RigidbodyComponent>() is { } body) body.Velocity = Vector3.Zero;

        var replacement = new BotController(
            instance, respawnPosition, unchecked((uint)Random.Shared.NextInt64(1, 1L << 32)));
        _bots[peerId] = replacement;
        _remotePlayers[peerId] = new RemotePlayer(instance, respawnPosition, 0.0f);
        instance.TryInvoke(
            "ResetExternalNavigation",
            respawnPosition.X, respawnPosition.Y, respawnPosition.Z);
        ConfigureNameplate(peerId, instance);
        EquipHardpointBot(peerId, replacement);
        PresentBotWeapon(replacement);
        return replacement;
    }

    private sealed class PlayerMatchState(PlayerTeam team, bool isBot)
    {
        public PlayerTeam Team { get; set; } = team;
        public bool IsBot { get; } = isBot;
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public float Health { get; set; } = 100.0f;
        public GrenadeSupply Grenades { get; } = new();
        public float ObjectiveSeconds { get; set; }
        public Vector3 LastHitDirection { get; set; }
        public float RespawnAt { get; set; }
        public float LastDamagedAt { get; set; }
    }

    private sealed class BotController(
        GameObject gameObject, Vector3 spawnPosition, uint seed)
    {
        public GameObject GameObject { get; } = gameObject;
        public RigidbodyComponent? Body { get; } = gameObject.GetComponent<RigidbodyComponent>();
        public Vector3 SpawnPosition { get; } = spawnPosition;
        public BotTargetReaction TargetReaction { get; } = new();
        public float NextShotAt { get; set; }
        public BotEquipmentState Loadout { get; } = new();
        public long SpectatorShots { get; set; }
        public int PresentedWeapon { get; set; } = -1;
        public BotCombatMovement CombatMovement { get; } = new();
        public int ShotsInBurst { get; set; }
        public int BurstLimit { get; set; } = 5;
        public float RangeMultiplier { get; } = 0.8f + ((seed >> 8) & 255u) / 255.0f * 0.4f;
        public float ObjectiveApproachAngle { get; set; } = seed % 360u * MathF.PI / 180.0f;
        public DefusalApproachProgress DefusalApproach { get; } = new();
        public bool DefusalWaypointValidated { get; set; }
        public Vector3? DefusalWaypoint { get; set; }
        public Vector3 ProgressPosition { get; set; } = spawnPosition;
        public float LastProgressAt { get; set; }
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
        public Vector3 CombatHoldPosition { get; set; } = spawnPosition;
        public float TurnDirection { get; set; } = 1.0f;
        public float TacticalAngleOffset { get; set; } = seed % 360u;
        public float StrafeSign { get; set; } = (seed & 1u) == 0 ? -1.0f : 1.0f;
        public uint RandomState { get; set; } = seed;
        public bool TacticalSearchActive { get; set; }
        public int TacticalSearchTargetId { get; set; } = int.MinValue;
        public Vector3 TacticalSearchTargetPosition { get; set; }
        public Vector3 TacticalSearchOrigin { get; set; }
        public int TacticalSampleIndex { get; set; }
        public float TacticalBestScore { get; set; } = float.MaxValue;
        public Vector3 TacticalBestDestination { get; set; }
        public bool TacticalFound { get; set; }
    }

    private enum TacticalSearchResult { Pending, Found, Failed }

    private sealed record ShotRequest(
        float OriginX, float OriginY, float OriginZ,
        float DirectionX, float DirectionY, float DirectionZ, string WeaponId)
    {
        public Vector3 Origin => new(OriginX, OriginY, OriginZ);
        public Vector3 Direction => new(DirectionX, DirectionY, DirectionZ);

        public static ShotRequest From(Vector3 origin, Vector3 direction, string weaponId) =>
            new(origin.X, origin.Y, origin.Z, direction.X, direction.Y, direction.Z, weaponId);

        public bool IsFinite() =>
            float.IsFinite(OriginX) && float.IsFinite(OriginY) && float.IsFinite(OriginZ) &&
            float.IsFinite(DirectionX) && float.IsFinite(DirectionY) && float.IsFinite(DirectionZ);
    }

    private sealed record PlayerTransform(
        int PeerId,
        float X, float Y, float Z,
        float Pitch, float Yaw, float Roll,
        float RotationX, float RotationY, float RotationZ, float RotationW,
        bool Crouching = false, int WeaponSlot = -1)
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
                euler.X, CharacterHeading.Yaw(quaternion), euler.Z,
                quaternion.X, quaternion.Y, quaternion.Z, quaternion.W,
                player.GetComponent<PlayerController>()?.IsCrouching == true);
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
        public float? StandingColliderHeight { get; set; }
        public Vector3? StandingColliderCenter { get; set; }

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
