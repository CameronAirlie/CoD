using System.Numerics;

namespace CoD.Scripts;

/// <summary>Conversions explicitly matching the engine's column-vector Rx * Ry * Rz convention.</summary>
public static class SpectatorCameraMath
{
    private const float Radians = MathF.PI / 180;
    public static Quaternion FromEngineEuler(Vector3 degrees) => Quaternion.Normalize(
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, degrees.X * Radians) *
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees.Y * Radians) *
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, degrees.Z * Radians));

    // Heading is applied before camera-local pitch/roll, just like the live player hierarchy.
    public static Quaternion FromHeading(float pitch, float yaw, float roll = 0) =>
        Quaternion.CreateFromYawPitchRoll(yaw * Radians, pitch * Radians, roll * Radians);

    public static Quaternion FromBasis(Vector3 forward, Vector3 right)
    {
        forward = Vector3.Normalize(forward);
        right = Vector3.Normalize(right - forward * Vector3.Dot(right, forward));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0,
            -forward.X, -forward.Y, -forward.Z, 0, 0, 0, 0, 1)));
    }

    public static Vector3 ToEngineEuler(Quaternion orientation)
    {
        var m = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(orientation));
        var y = MathF.Asin(Math.Clamp(m.M31, -1, 1));
        float x, z;
        if (MathF.Abs(m.M31) < .999999f)
        { x = MathF.Atan2(-m.M32, m.M33); z = MathF.Atan2(-m.M21, m.M11); }
        else
        { x = MathF.Atan2(m.M12, m.M22); z = 0; }
        return new Vector3(x, y, z) / Radians;
    }
}

/// <summary>Small delayed playback buffer using source time, independent of packet arrival jitter.</summary>
public sealed class SpectatorViewBuffer
{
    private readonly List<(SpectatorViewState View, double Time)> _samples = new();
    private double _clockOffset;
    private double _lastPlayback = double.NegativeInfinity;
    public bool Add(SpectatorViewState view, double receivedAt)
    {
        if (!view.IsValid() || !double.IsFinite(receivedAt)) return false;
        if (_samples.Count > 0 && !view.IsNewerThan(_samples[^1].View)) return false;
        var time = view.SampleTime > 0 ? view.SampleTime : receivedAt;
        if (_samples.Count > 0 && (view.Life != _samples[^1].View.Life ||
            Vector3.DistanceSquared(view.Position, _samples[^1].View.Position) > 25))
            _samples.Clear(); // Never blend a respawn/teleport through the map.
        if (_samples.Count == 0)
        { _clockOffset = receivedAt - time; _lastPlayback = double.NegativeInfinity; }
        else if (time <= _samples[^1].Time) return false;
        _samples.Add((view, time));
        if (_samples.Count > 64) _samples.RemoveAt(0);
        return true;
    }

    public bool TrySample(double now, float delay, out SpectatorViewState? view)
    {
        view = null;
        if (_samples.Count == 0 || !double.IsFinite(now)) return false;
        delay = float.IsFinite(delay) ? Math.Clamp(delay, 0, .25f) : .1f;
        var time = Math.Max(_lastPlayback, now - _clockOffset - delay);
        _lastPlayback = time;
        if (time <= _samples[0].Time) { view = _samples[0].View; return true; }
        var index = 0;
        while (index + 1 < _samples.Count && _samples[index + 1].Time <= time) index++;
        if (index > 0) _samples.RemoveRange(0, index);
        if (_samples.Count == 1) { view = _samples[0].View; return true; }
        var a = _samples[0]; var b = _samples[1];
        var t = (float)Math.Clamp((time - a.Time) / (b.Time - a.Time), 0, 1);
        var position = Vector3.Lerp(a.View.Position, b.View.Position, t);
        var orientation = Quaternion.Normalize(Quaternion.Slerp(a.View.Orientation, b.View.Orientation, t));
        // Bot cameras have no authored roll. Keep their horizon upright even when
        // pitch and yaw change together along a quaternion interpolation arc.
        if (a.View.Life == 0 && b.View.Life == 0)
        {
            var forward = Vector3.Transform(-Vector3.UnitZ, orientation);
            var right = Vector3.Cross(forward, Vector3.UnitY);
            if (right.LengthSquared() > .000001f) orientation = SpectatorCameraMath.FromBasis(forward, right);
        }
        var euler = SpectatorCameraMath.ToEngineEuler(orientation);
        var hp = a.View.HandPosition; var hr = a.View.HandRotation;
        if (a.View.Slot == b.View.Slot && a.View.HasHands && b.View.HasHands)
        {
            hp = Vector3.Lerp(hp, b.View.HandPosition, t);
            hr = SpectatorCameraMath.ToEngineEuler(Quaternion.Slerp(
                SpectatorCameraMath.FromEngineEuler(hr), SpectatorCameraMath.FromEngineEuler(b.View.HandRotation), t));
        }
        // Discrete HUD/weapon events stay with the left sample until their timeline reaches the next one.
        view = a.View with { X = position.X, Y = position.Y, Z = position.Z, Pitch = euler.X, Yaw = euler.Y, Roll = euler.Z,
            OrientationX = orientation.X, OrientationY = orientation.Y, OrientationZ = orientation.Z, OrientationW = orientation.W,
            Fov = a.View.Fov + (b.View.Fov - a.View.Fov) * t,
            HandX = hp.X, HandY = hp.Y, HandZ = hp.Z, HandPitch = hr.X, HandYaw = hr.Y, HandRoll = hr.Z };
        return true;
    }
}
