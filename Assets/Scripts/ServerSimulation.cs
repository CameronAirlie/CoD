using System.Numerics;

namespace CoD.Scripts;

/// <summary>Intent only. Simulation time, positions and resources are never supplied by clients.</summary>
public sealed record PlayerCommand(long Sequence, float MoveX, float MoveY, float Yaw, float Pitch,
    bool Jump, bool Sprint, bool Crouch, bool Aim, bool Fire, bool Reload, int Slot,
    int UseItem = 0, CombatLoadout Loadout = CombatLoadout.Assault, int Life = 0)
{
    public bool IsValid() => Sequence > 0 && float.IsFinite(MoveX) && float.IsFinite(MoveY) &&
        MathF.Abs(MoveX) <= 1 && MathF.Abs(MoveY) <= 1 && float.IsFinite(Yaw) &&
        MathF.Abs(Yaw) <= 36000 && float.IsFinite(Pitch) && MathF.Abs(Pitch) <= 89 &&
        Slot is >= 0 and < WeaponCatalog.SlotCount && UseItem is >= 0 and <= 2 &&
        Enum.IsDefined(Loadout);
    public static PlayerCommand Idle { get; } = new(1, 0, 0, 0, 0, false, false, false, false, false, false, 0);
}

/// <summary>Latest held intent with latched edges; packet floods cannot advance simulation.</summary>
public sealed class ServerInput
{
    public long LastSequence { get; private set; }
    public float ReceivedAt { get; private set; } = float.NegativeInfinity;
    private PlayerCommand _latest = PlayerCommand.Idle;
    private bool _jump, _reload, _fire;
    private int _use;
    public bool Accept(PlayerCommand command, float now)
    {
        if (!command.IsValid() || command.Sequence <= LastSequence || !float.IsFinite(now)) return false;
        LastSequence = command.Sequence; ReceivedAt = now; _latest = command;
        _jump |= command.Jump; _reload |= command.Reload; _fire |= command.Fire;
        if (command.UseItem != 0) _use = command.UseItem;
        return true;
    }
    public PlayerCommand Consume(float now)
    {
        var command = now - ReceivedAt <= .25f ? _latest with
        { Jump = _jump, Reload = _reload, Fire = _fire, UseItem = _use } :
            PlayerCommand.Idle with { Sequence = Math.Max(1, LastSequence), Yaw = _latest.Yaw, Pitch = _latest.Pitch, Slot = _latest.Slot };
        _jump = _reload = _fire = false; _use = 0;
        return command;
    }
    public void Clear(float yaw = 0, int slot = 0)
    {
        _jump = _reload = _fire = false; _use = 0; ReceivedAt = float.NegativeInfinity;
        _latest = PlayerCommand.Idle with { Yaw = yaw, Slot = slot };
    }
}

/// <summary>Server-owned movement. Collision is supplied by the engine adapter.</summary>
public sealed record MotorState(Vector3 Velocity, bool Grounded, bool Crouching, bool Sprinting,
    bool Sliding, float SlideRemaining, bool WasCrouching);

public sealed class ServerMotor
{
    public const float Step = 1f / 60;
    // Player roots are capsule centers, not foot positions. Match the player
    // camera's root-relative offsets, including its half-unit crouch drop.
    public const float StandingEyeHeight = .5f;
    public const float CrouchingEyeHeight = 0;
    public static float EyeHeight(bool crouching) => crouching ? CrouchingEyeHeight : StandingEyeHeight;
    public const float SkinWidth = .02f;
    public const float GroundProbeOffset = .12f;
    // The capsule bottom is one unit below its root. Include collision skin
    // and a small tolerance; a ray ending exactly at the bottom misses support.
    public const float GroundProbeDistance = GroundProbeOffset + 1 + SkinWidth + .06f;
    public MotorState Capture() => new(Velocity, Grounded, Crouching, Sprinting, Sliding, _slideRemaining, _wasCrouching);
    public void Restore(MotorState state)
    {
        Velocity = state.Velocity; Grounded = state.Grounded; Crouching = state.Crouching;
        Sprinting = state.Sprinting; Sliding = state.Sliding;
        _slideRemaining = state.SlideRemaining; _wasCrouching = state.WasCrouching;
    }
    public Vector3 Velocity { get; private set; }
    public bool Grounded { get; private set; }
    public bool Crouching { get; private set; }
    public bool Sprinting { get; private set; }
    public bool Sliding { get; private set; }
    private float _slideRemaining;
    private bool _wasCrouching;
    public void Reset() { Velocity = Vector3.Zero; Grounded = Sliding = Sprinting = Crouching = _wasCrouching = false; _slideRemaining = 0; }
    public void Tick(PlayerCommand input, float dt, bool grounded, Func<Vector3, Vector3> move, bool blocked)
    {
        if (!float.IsFinite(dt) || dt <= 0 || dt > .1f) return;
        // A longer support probe can still see the floor immediately after
        // takeoff. Upward motion must remain airborne until the jump descends.
        grounded &= Velocity.Y <= 0;
        Grounded = grounded;
        var axes = new Vector2(input.MoveX, input.MoveY);
        if (axes.LengthSquared() > 1) axes = Vector2.Normalize(axes);
        var radians = input.Yaw * MathF.PI / 180;
        var forward = new Vector3(-MathF.Sin(radians), 0, -MathF.Cos(radians));
        var right = new Vector3(MathF.Cos(radians), 0, -MathF.Sin(radians));
        var horizontal = new Vector3(Velocity.X, 0, Velocity.Z);
        if (!blocked && grounded && input.Crouch && !_wasCrouching && Sprinting && horizontal.Length() >= 6.5f)
        { Sliding = true; _slideRemaining = .72f; }
        _wasCrouching = input.Crouch;
        Crouching = input.Crouch || Sliding;
        Sprinting = !blocked && !Sliding && grounded && input.Sprint && axes.Y > .1f && !input.Aim && !Crouching;
        if (Sliding)
        {
            _slideRemaining -= dt;
            horizontal = Approach(horizontal, Vector3.Zero, 8 * dt);
            if (blocked || !grounded || _slideRemaining <= 0 || horizontal.LengthSquared() < 4) Sliding = false;
        }
        else
        {
            var speed = Crouching ? 3.1f : Sprinting ? 8 : 5.4f;
            if (input.Aim) speed *= .72f;
            var desired = blocked ? Vector3.Zero : (right * axes.X + forward * axes.Y) * speed;
            horizontal = !grounded && !blocked
                ? AirVelocity(horizontal, right * axes.X + forward * axes.Y, speed, 9, dt)
                : Approach(horizontal, desired, (grounded ? axes.LengthSquared() > 0 ? 38 : 30 : 9) * dt);
        }
        var vertical = grounded && Velocity.Y < 0 ? -2 : Velocity.Y;
        if (!blocked && grounded && input.Jump)
        {
            vertical = MathF.Sqrt(1.15f * 52); Grounded = false;
            Sliding = false; _slideRemaining = 0; Crouching = input.Crouch;
        }
        vertical -= 26 * dt;
        var requested = new Vector3(horizontal.X, vertical, horizontal.Z) * dt;
        // Sweep travel separately from gravity. A combined diagonal sweep into
        // the floor can spend horizontal distance on collision skin every tick,
        // slowing grounded movement even on a completely flat surface.
        var applied = move(new Vector3(requested.X, 0, requested.Z));
        applied += move(new Vector3(0, requested.Y, 0));
        if (vertical < 0 && MathF.Abs(applied.Y) < MathF.Abs(requested.Y) * .5f) vertical = -2;
        Velocity = new(horizontal.X, vertical, horizontal.Z);
    }
    // Preserve takeoff momentum without allowing sprint-speed acceleration in midair.
    // Air steering changes the direction of carried speed, rather than braking it
    // toward the grounded walking/ADS/crouch limit on every airborne tick.
    public static Vector3 AirVelocity(Vector3 velocity, Vector3 direction, float wishSpeed, float acceleration, float dt)
    {
        if (direction.LengthSquared() < .000001f) return velocity;
        var speed = velocity.Length();
        var targetSpeed = wishSpeed * MathF.Min(1, direction.Length());
        if (speed <= targetSpeed || speed < .000001f)
            return Approach(velocity, Vector3.Normalize(direction) * targetSpeed, acceleration * dt);
        var currentAngle = MathF.Atan2(velocity.Z, velocity.X);
        var desiredAngle = MathF.Atan2(direction.Z, direction.X);
        var difference = MathF.Atan2(MathF.Sin(desiredAngle - currentAngle), MathF.Cos(desiredAngle - currentAngle));
        var turnLimit = acceleration * dt / speed;
        var angle = currentAngle + Math.Clamp(difference, -turnLimit, turnLimit);
        return new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * speed;
    }
    private static Vector3 Approach(Vector3 from, Vector3 to, float amount)
    { var d = to - from; var length = d.Length(); return length <= amount ? to : from + d / length * amount; }
}

/// <summary>Per-life authoritative weapon and inventory state, independent of native/UI code.</summary>
public sealed class ServerEquipment
{
    public WeaponLoadout Weapons { get; } = new();
    public int Reserve { get; private set; }
    public int Kits { get; private set; }
    public int Plates { get; private set; }
    public bool Reloading => _reloadAt > 0;
    public int UsingItem { get; private set; }
    private float _reloadAt, _readyAt, _useAt;
    private bool _fireHeld;
    public void Reset(CombatLoadout loadout)
    {
        Weapons.Reset(loadout);
        var supply = InventoryRules.Loadout(loadout);
        Reserve = supply.Ammo; Kits = supply.Kits; Plates = supply.Plates;
        _reloadAt = _readyAt = _useAt = 0; UsingItem = 0; _fireHeld = false;
    }
    public int Tick(PlayerCommand input, float now, bool canAct)
    {
        if (!canAct) { CancelUse(); _reloadAt = 0; _fireHeld = false; return 0; }
        if (input.Slot != Weapons.SelectedSlot && UsingItem == 0)
        { Weapons.Select(input.Slot); _reloadAt = 0; _readyAt = MathF.Max(_readyAt, now + Weapons.Equipped.DrawSeconds); }
        if (_reloadAt > 0 && now >= _reloadAt)
        { var n = Math.Min(Reserve, Weapons.Equipped.MagazineSize - Weapons.Magazine); Weapons.Magazine += n; Reserve -= n; _reloadAt = 0; }
        var used = 0;
        if (UsingItem != 0 && now >= _useAt)
        { used = UsingItem; if (used == 1) Kits--; else Plates--; UsingItem = 0; }
        if (input.UseItem != 0 && UsingItem == 0 && !Reloading && now >= _readyAt &&
            (input.UseItem == 1 ? Kits > 0 : Plates > 0))
        { UsingItem = input.UseItem; _useAt = now + (UsingItem == 1 ? 1 : .5f); }
        if (input.Reload && !Reloading && UsingItem == 0 && now >= _readyAt &&
            Weapons.Magazine < Weapons.Equipped.MagazineSize && Reserve > 0)
            _reloadAt = now + Weapons.Equipped.ReloadSeconds;
        return used;
    }
    public bool TryFire(bool held, float now, bool canAct)
    {
        var trigger = held && (Weapons.Equipped.Automatic || !_fireHeld); _fireHeld = held;
        if (!trigger || !canAct || Reloading || UsingItem != 0 || now < _readyAt || Weapons.Magazine <= 0) return false;
        Weapons.Magazine--; _readyAt = now + Weapons.Equipped.ShotInterval; return true;
    }
    public void CancelUse() { UsingItem = 0; _useAt = 0; }
    public bool AddSupply(int ammo, int kits, int plates)
    {
        var old = (Reserve, Kits, Plates);
        Reserve += InventoryRules.AddAmmo(Reserve, Kits, Plates, ammo);
        Kits += InventoryRules.AddItems(Reserve, Kits, Plates, kits, true);
        Plates += InventoryRules.AddItems(Reserve, Kits, Plates, plates, false);
        return old != (Reserve, Kits, Plates);
    }
}

/// <summary>One command per server tick; receipt is never a simulation acknowledgement.</summary>
public sealed class MovementCommands
{
    private readonly Queue<PlayerCommand> _commands = new();
    public long LastReceived { get; private set; }
    public long LastProcessed { get; private set; }
    public bool Accept(PlayerCommand command)
    {
        if (!command.IsValid() || command.Sequence <= LastReceived || _commands.Count >= 120) return false;
        LastReceived = command.Sequence; _commands.Enqueue(command); return true;
    }
    public PlayerCommand? Consume()
    {
        if (!_commands.TryDequeue(out var command)) return null;
        LastProcessed = command.Sequence; return command;
    }
    public void Clear() { _commands.Clear(); }
}

/// <summary>Bounded owner history. Replay only movement, never combat or presentation effects.</summary>
public sealed class MovementPrediction
{
    private readonly List<PlayerCommand> _pending = new();
    public ServerMotor Motor { get; } = new();
    public int PendingCount => _pending.Count;
    public bool Predict(PlayerCommand command, Action<ServerMotor, PlayerCommand> simulate)
    {
        if (_pending.Count >= 120) return false;
        _pending.Add(command); simulate(Motor, command); return true;
    }
    public void Reconcile(long acknowledged, MotorState state, Action<ServerMotor, PlayerCommand> simulate)
    {
        _pending.RemoveAll(command => command.Sequence <= acknowledged);
        Motor.Restore(state);
        foreach (var command in _pending) simulate(Motor, command);
    }
    public void Clear() { _pending.Clear(); Motor.Reset(); }
}

/// <summary>Render-only translation between fixed movement ticks and small reconciliation corrections.</summary>
public sealed class MovementPresentation
{
    private Vector3 _previous, _current, _correction;
    private bool _initialized;
    public void Reset(Vector3 position)
    { _previous = _current = position; _correction = Vector3.Zero; _initialized = true; }
    public void Advance(Vector3 before, Vector3 after)
    {
        if (!_initialized) Reset(before);
        _previous = before; _current = after;
    }
    public void Correct(Vector3 position)
    {
        if (!_initialized || Vector3.DistanceSquared(position, _current) > 1)
        { Reset(position); return; }
        var delta = position - _current;
        _previous += delta; _current = position; _correction -= delta;
        if (_correction.LengthSquared() > 1) Reset(position);
    }
    public Vector3 Offset(Vector3 simulationPosition, float remainder, float elapsed)
    {
        if (!_initialized) return Vector3.Zero;
        if (float.IsFinite(elapsed) && elapsed > 0) _correction *= MathF.Exp(-18 * elapsed);
        var alpha = float.IsFinite(remainder) ? Math.Clamp(remainder / ServerMotor.Step, 0, 1) : 1;
        return Vector3.Lerp(_previous, _current, alpha) + _correction - simulationPosition;
    }
}
