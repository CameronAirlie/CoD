using CoD.Scripts;
using System.Numerics;

internal static class GrenadeTests
{
    public static void Run()
    {
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        var definition = GrenadeRules.Frag;
        Require(GrenadeRules.Damage(definition, 0) == 120 && GrenadeRules.Damage(definition, 2) == 120, "Inner blast must deal full damage.");
        Require(GrenadeRules.Damage(definition, 4.5f) == 60, "Blast damage must fall off with distance.");
        Require(GrenadeRules.Damage(definition, 7) == 0 && GrenadeRules.Damage(definition, float.NaN) == 0, "Invalid or out-of-range blast dealt damage.");
        var supply = new GrenadeSupply();
        Require(!supply.TrySpend(float.NaN), "Invalid throw time accepted.");
        Require(supply.TrySpend(0) && !supply.TrySpend(.1f) && supply.Remaining == 1, "Throw cooldown failed.");
        Require(supply.TrySpend(1) && !supply.TrySpend(2) && supply.Remaining == 0, "Grenade inventory exceeded its limit.");
        supply.Reset();
        Require(supply.Remaining == 2 && supply.TrySpend(0), "Respawn did not restore grenade budget.");

        static GrenadeCollision? Empty(Vector3 start, Vector3 end) => null;
        static GrenadeProjectile Simulate(int rate)
        {
            var projectile = new GrenadeProjectile(GrenadeRules.Frag, new(0, 10, 0), Vector3.UnitX);
            for (var i = 0; i < rate * 2; i++) projectile.Tick(1f / rate, Empty);
            return projectile;
        }
        var slow = Simulate(30); var fast = Simulate(144);
        Require(Vector3.Distance(slow.Position, fast.Position) < .001f && slow.Age == fast.Age, "Trajectory depends on render frame rate.");
        Require(!slow.Exploded, "Grenade exploded before the fuse elapsed.");
        slow.Tick(1.1f, Empty);
        Require(slow.Exploded, "Grenade failed to explode after the fuse.");
        var final = slow.Position; slow.Tick(1, Empty);
        Require(slow.Position == final, "Exploded projectile kept moving.");
        var bounce = new GrenadeProjectile(definition, new(0, .1f, 0), -Vector3.UnitY);
        bounce.Tick(.02f, (start, end) => end.Y <= 0 ? new(end with { Y = 0 }, Vector3.UnitY) : null);
        Require(bounce.Velocity.Y > 0 && bounce.Position.Y > 0, "Floor collision failed to bounce.");
        var invalid = bounce.Position; bounce.Tick(float.NaN, Empty);
        Require(bounce.Position == invalid, "Invalid delta corrupted projectile state.");
        Console.WriteLine("PASS: grenade supply limits, cooldown, reset, damage falloff, fixed-step flight, fuse, bounce and invalid delta.");
    }
}
