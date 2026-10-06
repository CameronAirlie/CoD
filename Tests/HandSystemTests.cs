using System.Numerics;
using CoD.Scripts;

internal static class HandSystemTests
{
    public static void Run()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        var profile = new HandMotionProfile(new(.1f, -.2f, 0), new(0, -.1f, 0), new(.2f, -.3f, 0));
        var motion = new HandMotion();
        motion.Reset(profile);
        var pose = motion.Tick(profile, 10, Vector2.Zero, false, true, false);
        Check(Vector3.Distance(pose.Position, profile.Aim) < .0001f, "ADS did not converge to the weapon pose.");
        pose = motion.Tick(profile, 10, Vector2.Zero, false, true, true);
        Check(Vector3.Distance(pose.Position, profile.Sprint) < .0001f, "Sprint must take precedence over ADS.");
        var second = profile with { Hip = new(-.2f, -.4f, -.1f) };
        motion.Reset(second);
        Check(motion.Tick(second, 0, Vector2.Zero, false, false, false).Position == second.Hip,
            "Equipping retained the previous weapon's position.");
        Check(motion.Tick(second, float.NaN, Vector2.Zero, false, false, false).Position == second.Hip,
            "Invalid elapsed time corrupted the pose.");
        pose = motion.Tick(second, 1, new(float.NaN, float.PositiveInfinity), false, false, false);
        Check(pose.Position == second.Hip && pose.RotationOffset == Vector3.Zero, "Invalid input poisoned hand transforms.");
        pose = motion.Tick(second, 1, new(100000, -100000), false, false, false);
        Check(Vector3.Distance(pose.Position, second.Hip) < .3f, "Cursor spike displaced hands out of view.");
        var a = new HandMotion(); var b = new HandMotion();
        a.Reset(profile); b.Reset(profile);
        var once = a.Tick(profile, 1, Vector2.Zero, false, true, false);
        HandPose many = default;
        for (var i = 0; i < 120; i++) many = b.Tick(profile, 1f / 120, Vector2.Zero, false, true, false);
        Check(Vector3.Distance(once.Position, many.Position) < .00001f, "Stationary pose smoothing depends on frame rate.");
        foreach (var file in new[] { "Scenes/Main.plutoscene", "Scenes/Foundry.plutoscene", "Prefabs/Player.plutoprefab" })
        {
            var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)).Replace("\r\n", "\n");
            var weapon = file.StartsWith("Prefabs") ? "25" : "22272";
            Check(text.Contains($"COMPONENT\t{weapon}\tScriptComponent\t1\nPROPERTY\tSource\t2\tCoD.Scripts.WeaponHandBinding\t0"),
                $"{file} has no binding on its animated hand rig.");
        }
        Console.WriteLine("PASS: hand poses, sprint priority, equip reset, invalid input, bounded sway, frame-rate independence, scene bindings.");
    }
}
