using System.Globalization;
using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>
/// Lap timing, live split deltas and checkpoint respawn for circuit scenes.
/// Checkpoints are scene entities named <c>checkpointPrefix + index</c>; index
/// 0 is the start/finish line. Each gate's forward axis points along the
/// racing direction, and a crossing only counts within the gate's width and
/// height so stacked sections (bridges) cannot trigger each other.
/// </summary>
public sealed class RaceLapTimer : ScriptBehaviour
{
    [SerializedField] private GameObject? vehicle = null;
    [SerializedField] private string checkpointPrefix = "Checkpoint_";
    [SerializedField] private int checkpointCount = 8;
    [SerializedField] private float gateHalfWidth = 24.0f;
    [SerializedField] private float gateHalfHeight = 6.0f;
    [SerializedField] private float respawnHeight = 1.6f;
    [SerializedField] private string documentPath = "UI/race-hud.rml";
    [SerializedField, InputMappingAsset] private string inputMappingAsset = "";
    [SerializedField] private float messageDuration = 2.5f;

    private GameObject[] _gates = [];
    private float[] _currentSplits = [];
    private float[] _bestSplits = [];
    private InputActionMap? _inputActions;
    private RmlDocument? _document;
    private RmlElement? _lapNumber;
    private RmlElement? _currentTime;
    private RmlElement? _bestTime;
    private RmlElement? _lastTime;
    private RmlElement? _delta;
    private RmlElement? _message;

    private int _nextGate;
    private int _lastGatePassed = -1;
    private float _previousGateDistance = float.NaN;
    private bool _lapRunning;
    private int _lap;
    private float _lapClock;
    private float _bestLap = float.PositiveInfinity;
    private float _lastLap = float.PositiveInfinity;
    private float _messageTimer;
    private float _textTimer;
    private float _frameTime;
    private bool _domReady;

    public override void OnCreate()
    {
        if (!string.IsNullOrWhiteSpace(inputMappingAsset))
        {
            try { _inputActions = InputActionMap.Load(inputMappingAsset); }
            catch (Exception exception) { Debug.LogError($"Unable to load race input map '{inputMappingAsset}': {exception.Message}"); }
        }

        var gates = new List<GameObject>();
        for (var index = 0; index < Math.Max(checkpointCount, 1); ++index)
        {
            var gate = GameObject.Find(checkpointPrefix + index.ToString(CultureInfo.InvariantCulture));
            if (gate is null)
            {
                Debug.LogError($"RaceLapTimer: missing checkpoint '{checkpointPrefix}{index}'.");
                break;
            }
            gates.Add(gate);
        }
        _gates = gates.ToArray();
        _currentSplits = new float[_gates.Length];
        _bestSplits = Enumerable.Repeat(float.NaN, _gates.Length).ToArray();

        _document = new RmlDocument(documentPath);
        _lapNumber = _document.Element("lap-number");
        _currentTime = _document.Element("current-time");
        _bestTime = _document.Element("best-time");
        _lastTime = _document.Element("last-time");
        _delta = _document.Element("split-delta");
        _message = _document.Element("race-message");
    }

    public override void OnDestroy()
    {
        _document?.Dispose();
        _document = null;
    }

    public override void OnLateUpdate(float deltaTime)
    {
        if (vehicle is null || _gates.Length == 0)
            return;

        // Rml documents become live after their first context update.
        if (!_domReady)
        {
            _domReady = _message?.SetClass("hidden", true) == true;
            if (_domReady)
                RefreshBoard();
        }

        if (GamePause.IsPaused)
            return;

        var elapsed = MathF.Max(deltaTime, 0.0f);
        _frameTime = elapsed;
        if (_lapRunning)
            _lapClock += elapsed;

        if (_inputActions?.WasPressed("Respawn") ?? Input.IsKeyPressed(KeyCode.T))
            RespawnAtCheckpoint();
        else
            TrackGateCrossing();

        if (_messageTimer > 0.0f)
        {
            _messageTimer -= elapsed;
            if (_messageTimer <= 0.0f)
            {
                _message?.SetClass("hidden", true);
                _delta?.SetClass("hidden", true);
            }
        }

        // The running clock is the only per-frame text; 20 Hz reads as live.
        _textTimer += elapsed;
        if (_textTimer >= 0.05f)
        {
            _textTimer = 0.0f;
            if (_currentTime is not null)
                _currentTime.Markup = _lapRunning ? FormatTime(_lapClock) : "0:00.000";
        }
    }

    private void TrackGateCrossing()
    {
        var gate = _gates[_nextGate];
        var offset = vehicle!.WorldPosition - gate.WorldPosition;
        var along = Vector3.Dot(offset, gate.Forward);
        var lateral = Vector3.Dot(offset, gate.Right);
        var previous = _previousGateDistance;
        _previousGateDistance = along;

        var withinGate = MathF.Abs(lateral) <= gateHalfWidth && MathF.Abs(offset.Y) <= gateHalfHeight;
        if (float.IsNaN(previous) || !withinGate || previous >= 0.0f || along < 0.0f)
            return;

        // Interpolate the crossing inside this frame for millisecond timing.
        var stepDistance = along - previous;
        var overshootFraction = stepDistance > 0.0001f ? along / stepDistance : 0.0f;
        PassGate(_nextGate, overshootFraction);
    }

    private void PassGate(int index, float overshootFraction)
    {
        _lastGatePassed = index;
        _nextGate = (index + 1) % _gates.Length;
        _previousGateDistance = float.NaN;

        if (index == 0)
        {
            if (_lapRunning)
            {
                // The clock already advanced through the full frame; remove
                // the part spent beyond the line.
                var lapTime = MathF.Max(_lapClock - overshootFraction * _frameTime, 0.0f);
                CompleteLap(lapTime);
            }
            _lapRunning = true;
            _lapClock = 0.0f;
            _lap++;
            RefreshBoard();
            return;
        }

        if (!_lapRunning)
            return;

        _currentSplits[index] = _lapClock;
        if (!float.IsNaN(_bestSplits[index]))
            ShowDelta(_lapClock - _bestSplits[index]);
    }

    private void CompleteLap(float lapTime)
    {
        _lastLap = lapTime;
        var improved = lapTime < _bestLap;
        if (!float.IsInfinity(_bestLap))
            ShowDelta(lapTime - _bestLap);
        if (improved)
        {
            _bestLap = lapTime;
            Array.Copy(_currentSplits, _bestSplits, _currentSplits.Length);
            ShowMessage(_lap <= 1 ? "LAP SET" : "NEW BEST LAP", "best");
        }
        else
        {
            ShowMessage("LAP " + FormatTime(lapTime), "lap");
        }
        Debug.Log($"[Race] Lap {_lap}: {FormatTime(lapTime)} (best {FormatTime(_bestLap)})");
    }

    private void RespawnAtCheckpoint()
    {
        var gate = _gates[Math.Max(_lastGatePassed, 0)];
        // Place the car a little before the gate so the crossing is not
        // counted twice, facing the racing direction.
        vehicle!.WorldPosition = gate.WorldPosition - gate.Forward * 6.0f + Vector3.UnitY * respawnHeight;
        vehicle.WorldRotation = new Vector3(0.0f, gate.WorldRotation.Y, 0.0f);
        var rigidbody = vehicle.GetComponent<RigidbodyComponent>();
        if (rigidbody is not null)
        {
            rigidbody.Velocity = Vector3.Zero;
            rigidbody.AngularVelocity = Vector3.Zero;
        }
        // Re-crossing the respawn gate itself is ignored; the lap continues
        // towards the following one. The start line restarts the lap.
        _nextGate = _lastGatePassed <= 0 ? 0 : (_lastGatePassed + 1) % _gates.Length;
        _previousGateDistance = float.NaN;
        if (_lastGatePassed == 0)
        {
            // Respawning on the grid restarts the lap from a standing start.
            _lapRunning = false;
            _lapClock = 0.0f;
            _lap = Math.Max(_lap - 1, 0);
            RefreshBoard();
        }
        ShowMessage("RESET TO CHECKPOINT", "reset");
    }

    private void RefreshBoard()
    {
        if (_lapNumber is not null) _lapNumber.Markup = _lap.ToString(CultureInfo.InvariantCulture);
        if (_bestTime is not null) _bestTime.Markup = FormatTime(_bestLap);
        if (_lastTime is not null) _lastTime.Markup = FormatTime(_lastLap);
    }

    private void ShowDelta(float delta)
    {
        if (_delta is null)
            return;
        _delta.Markup = (delta < 0.0f ? "-" : "+") +
                        MathF.Abs(delta).ToString("0.000", CultureInfo.InvariantCulture);
        _delta.SetClass("ahead", delta < 0.0f);
        _delta.SetClass("behind", delta >= 0.0f);
        _delta.SetClass("hidden", false);
        _messageTimer = MathF.Max(_messageTimer, messageDuration);
    }

    private void ShowMessage(string text, string style)
    {
        RefreshBoard();
        if (_message is null)
            return;
        _message.Markup = text;
        _message.SetClass("best", style == "best");
        _message.SetClass("reset", style == "reset");
        _message.SetClass("hidden", false);
        _messageTimer = messageDuration;
    }

    private static string FormatTime(float seconds)
    {
        if (float.IsInfinity(seconds) || float.IsNaN(seconds))
            return "-:--.---";
        var totalMilliseconds = (int)MathF.Round(seconds * 1000.0f);
        var minutes = totalMilliseconds / 60000;
        var remainder = totalMilliseconds % 60000;
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:000}",
            minutes, remainder / 1000, remainder % 1000);
    }
}
