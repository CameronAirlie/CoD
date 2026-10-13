using CoD.Scripts;
using System.Text.Json;
using System.Numerics;

internal static class SpectatorViewTests
{
    public static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var view = new SpectatorViewState(2, 3, 10, 1.25f, .12f, -8, -65, 179, .3f, 60,
            1, 8, 50, true, true, false, .2f, 75, 100, 2, .4f, 900,
            .1f, -.09f, -.2f, 5, -3, 1, true, 6, 1, 2, 0, 0, "flash", 2, 1, true, .5f);
        Check(view.IsValid(), "Valid rendered first-person view rejected.");
        var copy = JsonSerializer.Deserialize<SpectatorViewState>(JsonSerializer.Serialize(view))!;
        Check(copy == view && copy.Position == view.Position && copy.Rotation == view.Rotation && copy.HandPosition == view.HandPosition,
            "Transport lost camera, recoil/roll, FOV, hands or HUD state.");
        Check(!(view with { X = float.NaN }).IsValid() && !(view with { Pitch = float.PositiveInfinity }).IsValid(), "Invalid camera accepted.");
        Check(!(view with { Fov = 0 }).IsValid() && !(view with { HandX = float.NaN }).IsValid(), "Invalid view model accepted.");
        Check(!(view with { Slot = 9 }).IsValid() && !(view with { Flash = 2 }).IsValid() && !(view with { Health = 101 }).IsValid(), "Invalid HUD state accepted.");
        Check(!(view with { Sequence = 0 }).IsValid() && !(view with { UseProgress = float.NaN }).IsValid(), "Invalid presentation metadata accepted.");
        Check((view with { Sequence = 11 }).IsNewerThan(view) && !(view with { Sequence = 9 }).IsNewerThan(view), "Out-of-order view accepted.");
        Check((view with { Life = 4, Sequence = 1 }).IsNewerThan(view) && !(view with { Life = 2, Sequence = 999 }).IsNewerThan(view), "Old-life replay accepted.");
        var root = new System.Numerics.Vector3(10, 1.2f, -3);
        Check(CombatTargeting.EyePoint(root, .5f) == root + System.Numerics.Vector3.UnitY * .5f, "Bot eye must be above capsule centre, not at the chest aim point.");
        Check(CombatTargeting.EyePoint(root, 1.1f) == root + System.Numerics.Vector3.UnitY * 1.1f, "Authored bot eye height ignored.");
        Check(CombatTargeting.EyePoint(root, float.NaN) == CombatTargeting.EyePoint(root, ServerMotor.StandingEyeHeight), "Invalid eye height poisoned camera.");
        RotationAndInterpolation(view);
        var source = new MatchPlayer(2, "Source", PlayerTeam.Alpha, 0, 0, false);
        var deadAlly = new MatchPlayer(3, "Observer", PlayerTeam.Alpha, 0, 0, false, Alive: false);
        Check(SpectatorRules.CanReceiveView(source, deadAlly), "Dead teammate cannot receive POV.");
        Check(!SpectatorRules.CanReceiveView(source, deadAlly with { Team = PlayerTeam.Bravo }), "POV exposed to enemy.");
        Check(!SpectatorRules.CanReceiveView(source, deadAlly with { Alive = true }), "POV exposed to living player.");
        Check(!SpectatorRules.CanReceiveView(source with { Alive = false }, deadAlly), "Dead source exposed stale POV.");
        Check(!SpectatorRules.CanReceiveView(source, deadAlly with { PeerId = 2 }), "Self-spectating allowed.");
        Console.WriteLine("PASS: first-person camera/HUD serialization, invalid snapshots, packet ordering/life and teammate-only routing.");
    }
    private static void RotationAndInterpolation(SpectatorViewState template)
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        foreach (var yaw in new[] { -180f, -135, -91, -90, -89, -45, 0, 45, 89, 90, 91, 135, 180 })
        foreach (var pitch in new[] { -75f, -30, 0, 30, 75 })
        {
            var orientation = SpectatorCameraMath.FromHeading(pitch, yaw);
            var engineEuler = SpectatorCameraMath.ToEngineEuler(orientation);
            var rendered = SpectatorCameraMath.FromEngineEuler(engineEuler);
            Check(MathF.Abs(Quaternion.Dot(orientation, rendered)) > .99999f, "Engine XYZ conversion changed camera orientation.");
            var right = Vector3.Transform(Vector3.UnitX, rendered);
            Check(MathF.Abs(right.Y) < .0001f, $"Horizon tilted at yaw {yaw}, pitch {pitch}.");
            var basisCopy = SpectatorCameraMath.FromBasis(Vector3.Transform(-Vector3.UnitZ, orientation), right);
            Check(MathF.Abs(Quaternion.Dot(basisCopy, orientation)) > .99999f, "Rendered owner basis was not preserved.");
        }
        var rolled = SpectatorCameraMath.FromHeading(30, 135, 5);
        Check(MathF.Abs(Quaternion.Dot(rolled, SpectatorCameraMath.FromEngineEuler(SpectatorCameraMath.ToEngineEuler(rolled)))) > .99999f,
            "Intentional owner camera roll was lost.");
        var buffer = new SpectatorViewBuffer();
        var packets = Enumerable.Range(0, 40).Select(i => (
            Arrival: 10.2 + i * .05 + new[] { 0, .015, .003, .02, .008 }[i % 5],
            View: template with { Life = 1, Sequence = i + 1, SampleTime = 10 + i * .05, X = i * .2f, Y = 1.7f, Z = 0,
                Pitch = 0, Yaw = 0, Roll = 0, HasHands = false })).ToArray();
        var next = 0; var previousX = 0f;
        for (var frame = 0; frame < 90; frame++)
        {
            var now = 10.2 + frame / 60d;
            while (next < packets.Length && packets[next].Arrival <= now + 1e-8)
            { Check(buffer.Add(packets[next].View, packets[next].Arrival), "Valid source sample rejected."); next++; }
            Check(buffer.TrySample(now, .1f, out var output), "Buffered camera missing.");
            var expected = (float)Math.Max(0, (now - 10.3) * 4);
            Check(MathF.Abs(output!.X - expected) < .0001f, "Packet arrival jitter changed source-time playback speed.");
            if (frame > 8) Check(MathF.Abs(output.X - previousX - 4f/60) < .0001f, "20 Hz snapshots stuttered at 60 Hz rendering.");
            previousX = output.X;
        }
        var last = packets[next - 1].View;
        Check(!buffer.Add(last, 12), "Duplicate packet entered interpolation buffer.");
        var teleport = last with { Sequence = last.Sequence + 1, SampleTime = 12, X = 100 };
        Check(buffer.Add(teleport, 12.2) && buffer.TrySample(12.2, .1f, out var snapped) && snapped!.X == 100, "Teleport interpolated through world geometry.");
        var respawn = teleport with { Life = 2, Sequence = 1, SampleTime = 13, X = 20 };
        Check(buffer.Add(respawn, 13.2) && buffer.TrySample(13.2, .1f, out snapped) && snapped!.X == 20, "New life retained old camera history.");
        var wrap = new SpectatorViewBuffer();
        var a = SpectatorCameraMath.FromHeading(25, 179); var b = SpectatorCameraMath.FromHeading(25, -179);
        var first = template with { Sequence = 1, SampleTime = 20, X = 0, OrientationX = a.X, OrientationY = a.Y, OrientationZ = a.Z, OrientationW = a.W };
        var second = first with { Sequence = 2, SampleTime = 20.1, OrientationX = b.X, OrientationY = b.Y, OrientationZ = b.Z, OrientationW = b.W };
        wrap.Add(first, 21); wrap.Add(second, 21.1);
        Check(wrap.TrySample(21.15, .1f, out var middle) && MathF.Abs(middle!.Heading) > 179.9f,
            "Yaw wrap took the long rotation path.");
        var botBuffer = new SpectatorViewBuffer();
        var botA = SpectatorCameraMath.FromHeading(-35, 15); var botB = SpectatorCameraMath.FromHeading(50, 125);
        botBuffer.Add(first with { Life = 0, OrientationX = botA.X, OrientationY = botA.Y, OrientationZ = botA.Z, OrientationW = botA.W }, 21);
        botBuffer.Add(second with { Life = 0, OrientationX = botB.X, OrientationY = botB.Y, OrientationZ = botB.Z, OrientationW = botB.W }, 21.1);
        Check(botBuffer.TrySample(21.15, .1f, out var botMiddle) && MathF.Abs(Vector3.Transform(Vector3.UnitX, botMiddle!.Orientation).Y) < .0001f,
            "Bot horizon tilted while interpolating simultaneous pitch/yaw changes.");
        Check(!(template with { OrientationW = float.NaN }).IsValid() && !(template with { SampleTime = double.NaN }).IsValid(), "Invalid quaternion/time accepted.");
        Console.WriteLine("PASS: pitched yaw sweep, owner basis, quaternion wrap, jittered source-time playback, teleports and life resets.");
    }
}
