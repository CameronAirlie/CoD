using System.Globalization;
using PlutoGE.ScriptCore;
using PlutoGE.ScriptCore.Examples;

namespace CoD.Scripts;

/// <summary>Forza-inspired analogue vehicle HUD driven by RaycastVehicleController telemetry.</summary>
public sealed class VehicleSpeedometer : ScriptBehaviour
{
    [SerializedField] private GameObject? vehicle = null;
    [SerializedField] private string documentPath = "UI/vehicle-speedometer.rml";
    [SerializedField] private bool useMph = true;
    [SerializedField] private float maximumDisplaySpeed = 200.0f;
    [SerializedField] private float needleResponse = 12.0f;
    [SerializedField] private float uiUpdateRate = 30.0f;
    [SerializedField] private float textUpdateRate = 15.0f;

    private RmlDocument? _document;
    private RmlElement? _speed;
    private RmlElement? _unit;
    private RmlElement? _gear;
    private RmlElement? _rpm;
    private RmlElement? _speedNeedle;
    private RmlElement? _rpmNeedle;
    private RmlElement? _revArc;
    private RmlElement? _shiftLight;
    private float _displaySpeed;
    private float _displayRpm01;
    private int _lastSpeed = -1;
    private int _lastRpm = -1;
    private int _lastGear = int.MinValue;
    private bool? _lastUseMph;
    private bool? _lastNearRedline;
    private float _uiUpdateTimer;
    private float _textUpdateTimer;
    private int _lastSpeedNeedleStep = int.MinValue;
    private int _lastRpmNeedleStep = int.MinValue;
    private int _lastRevArcStep = int.MinValue;
    private bool _domReady;

    public override void OnCreate()
    {
        vehicle ??= GameObject;
        _document = new RmlDocument(documentPath);
        _speed = _document.Element("vehicle-speed");
        _unit = _document.Element("speed-unit");
        _gear = _document.Element("vehicle-gear");
        _rpm = _document.Element("engine-rpm");
        _speedNeedle = _document.Element("speed-needle");
        _rpmNeedle = _document.Element("rpm-needle");
        _revArc = _document.Element("rev-arc-fill");
        _shiftLight = _document.Element("shift-light");
    }

    public override void OnDestroy()
    {
        _document?.Dispose();
        _document = null;
    }

    public override void OnUpdate(float deltaTime)
    {
        if (!RaycastVehicleController.TryGetTelemetry(vehicle, out var telemetry))
            return;

        // Telemetry speed is metres per second. Convert to the driver's chosen road unit.
        var roadSpeed = MathF.Abs(telemetry.Speed) * (useMph ? 2.2369363f : 3.6f);
        var blend = 1.0f - MathF.Exp(-MathF.Max(0.01f, needleResponse) * MathF.Max(0.0f, deltaTime));
        _displaySpeed += (roadSpeed - _displaySpeed) * blend;
        _displayRpm01 += (Math.Clamp(telemetry.EngineRpm01, 0.0f, 1.0f) - _displayRpm01) * blend;

        // Rml documents become live after their first context update; retry until styling succeeds.
        if (!_domReady)
        {
            _domReady = _speedNeedle?.SetStyle("transform", "rotate(-130deg)") == true;
            if (!_domReady)
                return;
        }

        // Keep simulation-rate smoothing, but cross the managed/native boundary
        // only at the cadence each visual actually needs. Numeric readouts can
        // update less often than the analogue motion without appearing stale.
        var elapsed = MathF.Max(0.0f, deltaTime);
        _uiUpdateTimer += elapsed;
        _textUpdateTimer += elapsed;
        var updateInterval = 1.0f / Math.Clamp(uiUpdateRate, 1.0f, 120.0f);
        if (_uiUpdateTimer >= updateInterval)
        {
            _uiUpdateTimer %= updateInterval;
            PublishAnalogueVisuals();
        }

        var textInterval = 1.0f / Math.Clamp(textUpdateRate, 1.0f, 60.0f);
        if (_textUpdateTimer >= textInterval)
        {
            _textUpdateTimer %= textInterval;
            var speedValue = Math.Max(0, (int)MathF.Round(_displaySpeed));
            var rpmValue = Math.Max(0, (int)MathF.Round(telemetry.EngineRpm / 100.0f) * 100);
            if (speedValue != _lastSpeed)
            {
                _lastSpeed = speedValue;
                if (_speed is not null) _speed.Markup = speedValue.ToString("000", CultureInfo.InvariantCulture);
            }
            if (rpmValue != _lastRpm)
            {
                _lastRpm = rpmValue;
                if (_rpm is not null) _rpm.Markup = rpmValue.ToString("N0", CultureInfo.InvariantCulture);
            }
            if (telemetry.Gear != _lastGear)
            {
                _lastGear = telemetry.Gear;
                if (_gear is not null) _gear.Markup = telemetry.Gear < 0 ? "R" : telemetry.Gear == 0 ? "N" : telemetry.Gear.ToString(CultureInfo.InvariantCulture);
            }

            if (useMph != _lastUseMph)
            {
                _lastUseMph = useMph;
                if (_unit is not null) _unit.Markup = useMph ? "MPH" : "KM/H";
            }
        }

        var nearRedline = _displayRpm01 >= 0.88f;
        if (nearRedline != _lastNearRedline)
        {
            _lastNearRedline = nearRedline;
            _shiftLight?.SetClass("active", nearRedline);
            _rpm?.SetClass("redline", nearRedline);
        }
    }

    private void PublishAnalogueVisuals()
    {
        var speed01 = Math.Clamp(_displaySpeed / MathF.Max(1.0f, maximumDisplaySpeed), 0.0f, 1.0f);
        var speedNeedleStep = (int)MathF.Round((-130.0f + speed01 * 260.0f) * 10.0f);
        var rpmNeedleStep = (int)MathF.Round((-130.0f + _displayRpm01 * 260.0f) * 10.0f);
        var revArcStep = (int)MathF.Round(_displayRpm01 * 200.0f);

        if (speedNeedleStep != _lastSpeedNeedleStep)
        {
            _lastSpeedNeedleStep = speedNeedleStep;
            _speedNeedle?.SetStyle("transform", $"rotate({F(speedNeedleStep * 0.1f)}deg)");
        }
        if (rpmNeedleStep != _lastRpmNeedleStep)
        {
            _lastRpmNeedleStep = rpmNeedleStep;
            _rpmNeedle?.SetStyle("transform", $"rotate({F(rpmNeedleStep * 0.1f)}deg)");
        }
        if (revArcStep != _lastRevArcStep)
        {
            _lastRevArcStep = revArcStep;
            _revArc?.SetStyle("transform", $"scaleX({F(revArcStep * 0.005f)})");
        }
    }

    private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
