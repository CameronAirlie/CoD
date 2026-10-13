using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Engine/transport adapter around server-owned input, movement and resources.</summary>
public sealed partial class MultiplayerSession
{
    private const ushort InputChannel = 21, OwnerStateChannel = 22, PickupChannel = 23, SupplyStateChannel = 24;
    private readonly Dictionary<int, HumanSimulation> _humans = new();
    private long _commandSequence, _ownerTick, _lastOwnerTick;
    private int _localLife, _queuedUseItem;
    private readonly ServerInput _pendingLocalInput = new();
    private readonly MovementPrediction _prediction = new();
    private float _predictionAccumulator;
    private bool _ownerAlive;
    public MotorState? PredictedMotorState => _server is null && _localLife > 0 ? _prediction.Motor.Capture() : null;
    private float _movementAccumulator;
    private readonly MovementPresentation _localPresentation = new();
    public Vector3 LocalPresentationOffset(float elapsed) => !IsDedicated && UsesServerAuthority
        ? _localPresentation.Offset(GameObject.WorldPosition, _server is null ? _predictionAccumulator : _movementAccumulator, elapsed)
        : Vector3.Zero;
    public bool UsesServerAuthority => !mode.Equals("Offline", StringComparison.OrdinalIgnoreCase);
    public bool IsDedicated => DedicatedServerSettings.Enabled;

    internal void ConfigureDedicated(int port, int players, int bots, string rules)
    {
        mode = "Host"; serverPort = port; maximumParticipants = players;
        gameMode = rules;
        // Bot fill is a total population target, bounded by the participant limit.
        minimumParticipants = Math.Min(players, bots); maximumBots = bots; fillWithBots = bots > 0;
        if (GameObject.GetComponent<ColliderComponent>() is { } collider) collider.Enabled = false;
        if (GameObject.GetComponent<RigidbodyComponent>() is { } body) body.Enabled = false;
    }

    private sealed class HumanSimulation
    {
        public int Life { get; set; }
        public float MovementBudget { get; set; }
        public ServerInput Input { get; } = new();
        public MovementCommands Movement { get; } = new();
        public ServerMotor Motor { get; } = new();
        public ServerEquipment Equipment { get; } = new();
        public CombatLoadout Loadout { get; set; }
        public PlayerCommand Command { get; set; } = PlayerCommand.Idle;
        public int Armour { get; set; }
        public float RecoilPitch { get; set; }
        public float RecoilYaw { get; set; }
        public float Spread { get; set; }
        public Vector3 Spawn { get; set; }
        public HumanSimulation() => Equipment.Reset(CombatLoadout.Assault);
    }
    public sealed record OwnerState(long Tick, long AcknowledgedCommand, float X, float Y, float Z,
        float Health, int Armour, int Magazine, int Reserve, int Kits, int Plates, int Slot,
        bool Reloading, bool Grounded, bool Crouching, bool Sprinting, bool Sliding, int UsingItem,
        int Life, float Yaw, float VelocityX, float VelocityY, float VelocityZ, float SlideRemaining, bool WasCrouching);

    public void QueueItemUse(int item)
    { if (item is 1 or 2) _queuedUseItem = item; }

    public void SubmitLocalInput(float moveX, float moveY, float yaw, float pitch, bool jump,
        bool sprint, bool crouch, bool aim, bool fire, bool reload, int slot, int useItem, float elapsed)
    {
        if (!UsesServerAuthority || IsDedicated || _localPeerId < 0) return;
        if (useItem == 0) useItem = _queuedUseItem;
        _queuedUseItem = 0;
        var command = new PlayerCommand(1, moveX, moveY, yaw % 360, Math.Clamp(pitch, -89, 89),
            jump, sprint, crouch, aim, fire, reload, slot, useItem, SelectedLoadout, _localLife);
        if (_server is not null)
        {
            if (_humans.TryGetValue(0, out var host)) host.Input.Accept(command with { Sequence = ++_commandSequence }, _time);
            return;
        }
        if (_client?.IsConnected != true || !_ownerAlive) return;
        _pendingLocalInput.Accept(command with { Sequence = ++_commandSequence }, _time);
        _predictionAccumulator = MathF.Min(_predictionAccumulator + elapsed, .1f);
        while (_predictionAccumulator >= ServerMotor.Step)
        {
            _predictionAccumulator -= ServerMotor.Step;
            var tick = _pendingLocalInput.Consume(_time) with { Sequence = ++_commandSequence };
            var before = GameObject.WorldPosition;
            if (!_prediction.Predict(tick, SimulatePrediction)) break;
            _localPresentation.Advance(before, GameObject.WorldPosition);
            _client.SendJson(InputChannel, tick);
        }
        _playerController?.ApplyPredictedMovement(_prediction.Motor.Capture());
    }

    private void SimulatePrediction(ServerMotor motor, PlayerCommand command)
    {
        var grounded = Physics.Raycast(GameObject.WorldPosition + Vector3.UnitY * ServerMotor.GroundProbeOffset,
            -Vector3.UnitY, ServerMotor.GroundProbeDistance, GameObject, out var ground) && ground.Normal.Y > .55f;
        // Stance must match the replayed tick before its collision sweep.
        _playerController?.ApplyPredictedMovement(motor.Capture());
        motor.Tick(command, ServerMotor.Step, grounded,
            displacement => Physics.MoveKinematic(GameObject, displacement, ServerMotor.SkinWidth),
            CurrentMatch?.Phase != MatchPhase.Playing || IsUsingObjective);
    }

    private void AcceptInput(int peer, PlayerCommand command)
    {
        if (_humans.TryGetValue(peer, out var human) && command.Life == human.Life && human.Movement.Accept(command))
        {
            if (_matchPhase is MatchPhase.Warmup or MatchPhase.Results) human.Loadout = command.Loadout;
        }
    }
    private void AddHuman(int peer)
    {
        var human = new HumanSimulation(); _humans[peer] = human;
        var state = _playerStates[peer];
        human.Spawn = IsDefusal ? DefusalSpawn(state.Team, peer) :
            TryBotSpawnPosition(peer, out var spawn) ? spawn : GameObject.WorldPosition;
        if (peer == 0 && !IsDedicated && !IsDefusal) human.Spawn = GameObject.WorldPosition;
        else if (Physics.Raycast(human.Spawn + Vector3.UnitY * 256, -Vector3.UnitY, 512, out var ground))
            human.Spawn = new Vector3(human.Spawn.X, ground.Point.Y + 1.02f, human.Spawn.Z);
        SpawnHuman(peer);
    }
    private void SpawnHuman(int peer)
    {
        if (!_humans.TryGetValue(peer, out var human)) return;
        var state = _playerStates[peer];
        human.Movement.Clear(); human.MovementBudget = 0; human.Motor.Reset(); human.Equipment.Reset(human.Loadout); human.Armour = 0; human.Life++;
        human.Equipment.Weapons.Select(1);
        SendEconomy(peer);
        human.Input.Clear(IsDefusal ? DefusalRules.SpawnYaw(state.Team == AttackingTeam) : 0, human.Equipment.Weapons.SelectedSlot);
        human.RecoilPitch = human.RecoilYaw = 0; human.Spread = human.Equipment.Weapons.Equipped.HipSpread;
        if (IsDefusal) human.Spawn = DefusalSpawn(state.Team, peer);
        _deadRemotePlayers.Remove(peer);
        if (peer != 0) ApplyRemoteTransform(new PlayerTransform(peer, human.Spawn.X, human.Spawn.Y, human.Spawn.Z, 0, 0, 0, 0, 0, 0, 1));
        if (GetParticipantObject(peer) is { } actor)
        {
            actor.Active = true; actor.WorldPosition = human.Spawn;
            if (actor.GetComponent<RigidbodyComponent>() is { } body) body.Velocity = Vector3.Zero;
            if (_remotePlayers.TryGetValue(peer, out var proxy)) proxy.TargetPosition = human.Spawn;
            actor.TryInvoke("ResetExternalNavigation", human.Spawn.X, human.Spawn.Y, human.Spawn.Z);
            if (peer != 0) actor.TryInvoke("SetRemoteProxyMode");
        }
        human.Command = PlayerCommand.Idle with { Yaw = IsDefusal ? DefusalRules.SpawnYaw(state.Team == AttackingTeam) : 0 };
    }
    private void ResetHumansForRound()
    { foreach (var peer in _humans.Keys) SpawnHuman(peer); }

    private void TickHumans(float elapsed)
    {
        _movementAccumulator = MathF.Min(_movementAccumulator + elapsed, .1f);
        const float step = 1f / 60;
        while (_movementAccumulator >= step)
        {
            _movementAccumulator -= step;
            foreach (var pair in _humans)
            {
                var peer = pair.Key; var human = pair.Value;
                if (!_playerStates.TryGetValue(peer, out var state) || GetParticipantObject(peer) is not { } actor || !actor.IsValid) continue;
                // Accumulate a small real-time budget to drain jittered bursts, never
                // allowing client timestamps or packet floods to buy simulation time.
                human.MovementBudget = MathF.Min(.1f, human.MovementBudget + step);
                while (human.MovementBudget >= step)
                {
                    var received = human.Movement.Consume();
                    if (peer != 0 && received is null) break;
                    human.MovementBudget -= step;
                    if (received is not null) human.Input.Accept(received, _time);
                    var command = human.Input.Consume(_time); human.Command = command;
                    var alive = state.Health > 0;
                    var canAct = alive && _matchPhase == MatchPhase.Playing &&
                        (!IsDefusal || _defusal?.Snapshot().Operator != peer);
                    // Local mouse look runs at render cadence; do not overwrite it with the last fixed-tick yaw.
                    if (peer != 0) actor.Rotation = new Vector3(0, command.Yaw, 0);
                    if (command.UseItem == 1 && state.Health >= multiplayerMaximumHealth || command.UseItem == 2 && human.Armour >= 3)
                        command = command with { UseItem = 0 };
                    if (!Account(peer).Owns(command.Slot))
                        command = command with { Slot = Account(peer).Owns(human.Equipment.Weapons.SelectedSlot)
                            ? human.Equipment.Weapons.SelectedSlot : 1 };
                    var used = human.Equipment.Tick(command, _time, canAct);
                    if (used == 1) state.Health = MathF.Min(multiplayerMaximumHealth, state.Health + multiplayerMaximumHealth * .3f);
                    if (used == 2) human.Armour = Math.Min(3, human.Armour + 1);
                    var grounded = Physics.Raycast(actor.WorldPosition + Vector3.UnitY * ServerMotor.GroundProbeOffset,
                        -Vector3.UnitY, ServerMotor.GroundProbeDistance, actor, out var ground) && ground.Normal.Y > .55f;
                    if (alive)
                    {
                        var before = actor.WorldPosition;
                        human.Motor.Tick(command, step, grounded,
                            displacement => Physics.MoveKinematic(actor, displacement, ServerMotor.SkinWidth), !canAct);
                        if (peer == 0 && !IsDedicated) _localPresentation.Advance(before, actor.WorldPosition);
                        if (_remotePlayers.TryGetValue(peer, out var proxy))
                        {
                            proxy.TargetPosition = actor.WorldPosition; proxy.TargetYaw = command.Yaw;
                            ApplyRemoteTransform(PlayerTransform.From(peer, actor) with { Crouching = human.Motor.Crouching });
                        }
                    }
                    var weapon = human.Equipment.Weapons.Equipped;
                    human.RecoilPitch *= MathF.Exp(-9 * step); human.RecoilYaw *= MathF.Exp(-9 * step);
                    var spread = command.Aim ? weapon.AimSpread : weapon.HipSpread;
                    spread += .8f * Math.Clamp(new Vector2(human.Motor.Velocity.X, human.Motor.Velocity.Z).Length() / 5.4f, 0, 1.5f) * (command.Aim ? .4f : 1);
                    if (!human.Motor.Grounded) spread += 2.5f * (command.Aim ? .4f : 1);
                    else if (human.Motor.Crouching) spread *= .75f;
                    human.Spread += (spread - human.Spread) * (1 - MathF.Exp(-(spread > human.Spread ? 14 : 7) * step));
                    if (human.Equipment.TryFire(command.Fire, _time, canAct && !human.Motor.Sprinting && !human.Motor.Sliding))
                    {
                        var pitch = (command.Pitch + human.RecoilPitch) * MathF.PI / 180;
                        var yaw = (command.Yaw + human.RecoilYaw) * MathF.PI / 180;
                        var direction = new Vector3(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
                        var tangent = MathF.Tan(human.Spread * MathF.PI / 180);
                        var right = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitY));
                        var up = Vector3.Cross(right, direction);
                        direction = Vector3.Normalize(direction + right * ((Random.Shared.NextSingle() * 2 - 1) * tangent) +
                            up * ((Random.Shared.NextSingle() * 2 - 1) * tangent));
                        human.RecoilPitch -= weapon.RecoilPitch * (.85f + Random.Shared.NextSingle() * .3f);
                        human.RecoilYaw += (Random.Shared.NextSingle() * 2 - 1) * weapon.RecoilYaw;
                        // Origin, weapon, ammunition and shot cadence all come from server state.
                        ResolveShot(peer, ShotRequest.From(actor.WorldPosition + Vector3.UnitY *
                            ServerMotor.EyeHeight(human.Motor.Crouching), direction, human.Equipment.Weapons.Equipped.Id));
                    }
                }
            }
        }
    }
    private void SendAuthoritativeStates()
    {
        SendSupplyState();
        foreach (var pair in _humans)
        {
            var peer = pair.Key; var human = pair.Value;
            if (GetParticipantObject(peer) is not { } actor || !_playerStates.TryGetValue(peer, out var state)) continue;
            var p = actor.WorldPosition; var equipment = human.Equipment;
            var snapshot = new OwnerState(++_ownerTick, (peer == 0 ? human.Input.LastSequence : human.Movement.LastProcessed), p.X, p.Y, p.Z, state.Health,
                human.Armour, equipment.Weapons.Magazine, equipment.Reserve, equipment.Kits, equipment.Plates,
                equipment.Weapons.SelectedSlot, equipment.Reloading, human.Motor.Grounded, human.Motor.Crouching,
                human.Motor.Sprinting, human.Motor.Sliding, equipment.UsingItem, human.Life, human.Command.Yaw,
                human.Motor.Velocity.X, human.Motor.Velocity.Y, human.Motor.Velocity.Z,
                human.Motor.Capture().SlideRemaining, human.Motor.Capture().WasCrouching);
            if (peer == 0) ApplyOwnerState(snapshot);
            else _server?.SendJson(peer, OwnerStateChannel, snapshot);
            if (state.Health > 0) _server?.BroadcastJson(TransformChannel,
                PlayerTransform.From(peer, actor) with { Crouching = human.Motor.Crouching }, peer);
        }
    }
    private void ApplyOwnerState(OwnerState state)
    {
        if (state.Tick <= _lastOwnerTick || !float.IsFinite(state.X) || !float.IsFinite(state.Y) ||
            !float.IsFinite(state.Z) || !float.IsFinite(state.Health)) return;
        var newLife = _localLife != state.Life;
        if (newLife || state.Health <= 0)
        {
            _pendingLocalInput.Clear(state.Yaw, state.Slot);
            _queuedUseItem = 0; _prediction.Clear(); _predictionAccumulator = 0;
        }
        _lastOwnerTick = state.Tick; _localLife = state.Life;
        _ownerAlive = state.Health > 0;
        GameObject.WorldPosition = new(state.X, state.Y, state.Z);
        if (_server is null)
        {
            var motor = new MotorState(new(state.VelocityX, state.VelocityY, state.VelocityZ),
                state.Grounded, state.Crouching, state.Sprinting, state.Sliding, state.SlideRemaining, state.WasCrouching);
            _prediction.Reconcile(state.AcknowledgedCommand, motor, SimulatePrediction);
        }
        if (newLife || state.Health <= 0) _localPresentation.Reset(GameObject.WorldPosition);
        else if (_server is null) _localPresentation.Correct(GameObject.WorldPosition);
        _playerHealth?.ApplyServerState(state.Health, state.Armour, damageFeedback: !newLife);
        _inventory ??= GameObject.GetComponent<PlayerInventory>();
        _inventory?.ApplyServerState(state.Reserve, state.Kits, state.Plates, state.UsingItem);
        _playerController?.ApplyServerState(state);
    }
    private sealed record SupplyRequest(float X, float Y, float Z);
    private sealed record SupplyPosition(float X, float Y, float Z);
    public void RequestSupply(Vector3 position)
    {
        var request = new SupplyRequest(position.X, position.Y, position.Z);
        if (_server is not null) CollectSupply(_localPeerId, request);
        else _client?.SendJson(PickupChannel, request);
    }
    private void CollectSupply(int peer, SupplyRequest request)
    {
        if (!float.IsFinite(request.X) || !float.IsFinite(request.Y) || !float.IsFinite(request.Z) ||
            _matchPhase != MatchPhase.Playing || !_humans.TryGetValue(peer, out var human) ||
            !_playerStates.TryGetValue(peer, out var state) || state.Health <= 0 ||
            GetParticipantObject(peer) is not { } actor) return;
        var position = new Vector3(request.X, request.Y, request.Z);
        if (Vector3.DistanceSquared(actor.WorldPosition, position) > 9) return;
        foreach (var pickup in SupplyPickup.Live.ToArray())
        {
            if (Vector3.DistanceSquared(pickup.SupplyPosition, position) > .01f) continue;
            var ray = position - actor.WorldPosition;
            if (ray.LengthSquared() > .01f && Physics.Raycast(actor.WorldPosition, Vector3.Normalize(ray), ray.Length(), actor, out var hit) &&
                Vector3.DistanceSquared(hit.Point, position) > .25f) return;
            if (pickup.CollectOnServer(human.Equipment, state.Health, multiplayerMaximumHealth, out var healed)) state.Health += healed;
            return;
        }
    }
    private void SendSupplyState()
    {
        var positions = SupplyPickup.Live.Select(p => p.SupplyPosition).Select(p => new SupplyPosition(p.X, p.Y, p.Z)).ToArray();
        _server?.BroadcastJson(SupplyStateChannel, positions);
    }
    private static void ApplySupplyState(SupplyPosition[] positions)
    {
        foreach (var pickup in SupplyPickup.Live)
            pickup.SetNetworkVisible(positions.Any(p => Vector3.DistanceSquared(pickup.SupplyPosition, new(p.X, p.Y, p.Z)) < .01f));
    }

    private float ApplyHumanArmour(int peer, float damage)
    {
        if (!_humans.TryGetValue(peer, out var human)) return damage;
        human.Equipment.CancelUse();
        while (human.Armour > 0 && damage > 0) { human.Armour--; damage = MathF.Max(0, damage - 25); }
        return damage;
    }
}
