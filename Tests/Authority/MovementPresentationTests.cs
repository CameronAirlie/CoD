using CoD.Scripts;
using System.Numerics;

internal static class MovementPresentationTests
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    public static void Run()
    {
        foreach (var fps in new[] { 60, 120, 144, 240 })
        {
            var view = new MovementPresentation();
            var stepDistance = 5.4f * ServerMotor.Step;
            var position = new Vector3(stepDistance, 0, 0);
            view.Reset(Vector3.Zero); view.Advance(Vector3.Zero, position);
            var accumulator = 0f;
            var previousView = Vector3.Zero;
            var maximumError = 0f;
            for (var frame = 0; frame < fps * 2; frame++)
            {
                var elapsed = 1f / fps;
                accumulator += elapsed;
                while (accumulator >= ServerMotor.Step)
                {
                    accumulator -= ServerMotor.Step;
                    var before = position; position.X += stepDistance;
                    view.Advance(before, position);
                }
                var presented = position + view.Offset(position, accumulator, elapsed);
                maximumError = MathF.Max(maximumError, MathF.Abs(presented.X - previousView.X - 5.4f * elapsed));
                previousView = presented;
            }
            Check(maximumError < .00001f, $"{fps} Hz camera retains fixed-tick movement jitter: {maximumError}");
            Console.WriteLine($"PASS: smooth 60 Hz movement presented at {fps} FPS (maximum step error {maximumError:0.000000})");
        }
        var corrected = new MovementPresentation();
        corrected.Reset(Vector3.Zero); corrected.Advance(new(.9f, 0, 0), Vector3.UnitX);
        var beforeCorrection = Vector3.UnitX + corrected.Offset(Vector3.UnitX, ServerMotor.Step / 2, 0);
        var authoritative = new Vector3(1.01f, 0, 0);
        corrected.Correct(authoritative);
        var afterCorrection = authoritative + corrected.Offset(authoritative, ServerMotor.Step / 2, 0);
        Check(Vector3.Distance(beforeCorrection, afterCorrection) < .000001f, "Small reconciliation snapped the camera.");
        var resting = authoritative + corrected.Offset(authoritative, ServerMotor.Step / 2, 1);
        Check(MathF.Abs(resting.X - .96f) < .000001f, "Correction smoothing never converged.");
        corrected.Correct(new(50, 2, 30));
        Check(corrected.Offset(new(50, 2, 30), 0, .01f) == Vector3.Zero, "Teleport retained old camera history.");
        corrected.Reset(new(2, 1, 4));
        Check(corrected.Offset(new(2, 1, 4), .01f, .01f) == Vector3.Zero, "New life retained old camera history.");
        Console.WriteLine("PASS: reconciliation continuity/convergence and teleport/respawn reset");
    }
}
