using CoD.Scripts;
using System.Numerics;

internal static class TacticalUiTests
{
    public static void Run()
    {
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        Require(TacticalUi.Heading(-Vector3.UnitZ) == 0 && TacticalUi.Heading(Vector3.UnitX) == 90 &&
            TacticalUi.Heading(Vector3.UnitZ) == 180 && TacticalUi.Heading(-Vector3.UnitX) == 270, "Compass cardinal axes are wrong.");
        Require(TacticalUi.Heading(new(float.NaN, 0, 1)) == 0, "Invalid compass direction propagated.");
        var ticks = TacticalUi.CompassTicks(359);
        Require(ticks.Contains(">N<") && ticks.Contains(">330<") && ticks.Contains(">030<"), "Compass failed to wrap across north.");
        Console.WriteLine("PASS: tactical compass cardinal directions, invalid vectors and north wraparound.");
    }
}
