using System.Numerics;
using PlutoGE.ScriptCore;
using PlutoGE.ScriptCore.Examples;

namespace CoD.Scripts;

/// <summary>Local race-car feedback using the player's saved vibration strength.</summary>
public sealed class VehicleRumble : ScriptBehaviour
{
    [SerializedField] private float strength = 1;
    private readonly VibrationMixer _pulses = new();
    private RigidbodyComponent? _body;
    private VehicleTelemetry _previous;
    private Vector3 _velocity;
    private bool _hasPrevious;
    private int _pad = -1;
    private float _impactCooldown;
    private bool _active;
    private bool Allowed => !GamePause.IsPaused && Input.CursorLocked && GameInput.GameplayAllowed &&
        Input.UsingController && Input.ActiveGamepad >= 0 && PlayerSettings.Current.ControllerVibration > 0 && strength > 0;

    public override void OnCreate() => _body = GameObject.GetComponent<RigidbodyComponent>();
    public override void OnDestroy() => Stop();
    public override void OnLateUpdate(float deltaTime)
    {
        if (!Allowed || !float.IsFinite(deltaTime) || deltaTime <= 0 ||
            !RaycastVehicleController.TryGetTelemetry(GameObject, out var car)) { Stop(); return; }
        if (_pad >= 0 && _pad != Input.ActiveGamepad) Stop();
        _pad = Input.ActiveGamepad;
        _impactCooldown = MathF.Max(0, _impactCooldown - deltaTime);
        if (_hasPrevious)
        {
            if (car.Gear != _previous.Gear && car.Throttle > .1f)
                _pulses.Pulse(0, new(.28f, .16f), .12f);
            if (!_previous.IsGrounded && car.IsGrounded) Impact(MathF.Max(0, -_velocity.Y));
        }
        _previous = car;
        _velocity = _body?.Velocity ?? Vector3.Zero;
        _hasPrevious = true;
        var engine = Math.Clamp(car.Throttle, 0, 1) * Math.Clamp(car.EngineRpm01, 0, 1);
        var road = car.IsGrounded ? Math.Clamp(car.Speed / 8, 0, 1) : 0;
        var slip = car.IsGrounded ? Math.Clamp(MathF.Max(car.LateralSlip - .18f,
            car.LongitudinalSlip - .15f) / .8f, 0, 1) : 0;
        var brake = road * Math.Clamp(car.Brake, 0, 1);
        var continuous = new Vector2(.10f * engine + .16f * brake + .18f * slip,
            .06f * engine + .38f * slip + .12f * brake);
        var output = Vector2.Clamp(Vector2.Max(continuous, _pulses.Advance(deltaTime)) *
            (PlayerSettings.Current.ControllerVibration * Math.Clamp(strength, 0, 1)), Vector2.Zero, Vector2.One);
        if (output.LengthSquared() < .0001f)
        {
            if (_active) Input.StopGamepadVibration(_pad);
            _active = false;
        }
        else _active = Input.TrySetGamepadVibration(output.X, output.Y, .10f, _pad);
    }

    public override void OnCollisionEnter(GameObject other)
    {
        // Contact callbacks have no impulse payload; measure velocity lost across contact.
        if (Allowed && _hasPrevious && _body is not null) Impact((_velocity - _body.Velocity).Length());
    }
    private void Impact(float speedChange)
    {
        if (_impactCooldown > 0 || !float.IsFinite(speedChange) || speedChange < 2.5f) return;
        var amount = Math.Clamp((speedChange - 2) / 10, 0, 1);
        _pulses.Pulse(1, new Vector2(.9f, .55f) * amount, .25f);
        _impactCooldown = .2f;
    }
    private void Stop()
    {
        if (_pad >= 0) Input.StopGamepadVibration(_pad);
        _pad = -1;
        _active = false;
        _hasPrevious = false;
        _impactCooldown = 0;
        _pulses.Clear();
    }
}
