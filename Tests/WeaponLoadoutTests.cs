using CoD.Scripts;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class WeaponLoadoutTests
{
    public static void Run()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        var loadout = new WeaponLoadout();
        Check(loadout.Equipped == WeaponCatalog.AssaultRifle && loadout.Magazine == 30, "Default loadout is not the rifle.");
        loadout.Magazine -= 7;
        Check(loadout.Select(1) && loadout.Magazine == 12 && !loadout.Equipped.Automatic, "Pistol state/trigger mode is wrong.");
        loadout.Magazine -= 4;
        Check(loadout.Select(2) && loadout.Magazine == 60 && loadout.Equipped.Automatic, "Machine gun did not equip its magazine.");
        loadout.Magazine -= 11;
        Check(loadout.Select(0) && loadout.Magazine == 23, "Switching refilled the rifle magazine.");
        Check(loadout.Select(1) && loadout.Magazine == 8, "Switching lost the pistol magazine.");
        Check(!loadout.Select(-1) && !loadout.Select(3) && !loadout.Select(1) && loadout.Magazine == 8,
            "Invalid or repeated selection changed gameplay state.");
        loadout.Magazine = int.MaxValue;
        Check(loadout.Magazine == 12, "Reload overfilled a magazine.");
        loadout.Magazine = -10;
        Check(loadout.Magazine == 0, "Ammo became negative.");
        loadout.Reset(CombatLoadout.Defender);
        Check(loadout.Equipped == WeaponCatalog.MachineGun && loadout.Magazine == 60 && loadout.NextSlot == 0,
            "Defender respawn/default weapon or slot wraparound is wrong.");
        loadout.Reset(CombatLoadout.Medic);
        Check(loadout.Equipped == WeaponCatalog.Pistol && loadout.Magazine == 12, "Medic did not reset to pistol.");
        loadout.Select(0);
        Check(loadout.Magazine == 30, "Round reset left a stale unequipped magazine.");
        Check(!WeaponCatalog.TryFind(null, out _) && !WeaponCatalog.TryFind("admin-rifle", out _) &&
            WeaponCatalog.TryFind("lmg", out var lmg) && lmg == WeaponCatalog.MachineGun, "Host catalog accepted an unknown weapon.");
        Check(WeaponCatalog.MinimumShotInterval(WeaponCatalog.Pistol, "pistol") == WeaponCatalog.Pistol.ShotInterval,
            "Pistol rate does not match its local fire rate.");
        Check(WeaponCatalog.MinimumShotInterval(WeaponCatalog.MachineGun, "pistol") >= WeaponCatalog.MachineGun.DrawSeconds,
            "Alternating slots bypassed host switch cooldown.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Weapons/manifest.json")));
        for (var slot = 0; slot < WeaponCatalog.SlotCount; slot++)
        {
            var weapon = WeaponCatalog.At(slot);
            var clips = manifest.RootElement.GetProperty(weapon.Id).GetProperty("animations");
            Check(Math.Abs(clips.GetProperty("Reload").GetSingle() - weapon.ReloadSeconds) < .001f &&
                Math.Abs(clips.GetProperty("Draw").GetSingle() - weapon.DrawSeconds) < .001f,
                "Gameplay timing drifted from the authored clips for " + weapon.Id);
            Check(clips.GetProperty("Fire").GetSingle() <= weapon.ShotInterval, "Fire clip cannot finish between shots.");
        }
        foreach (var file in new[] { "Scenes/Main.plutoscene", "Scenes/Foundry.plutoscene", "Prefabs/Player.plutoprefab" })
        {
            var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)).Replace("\r\n", "\n");
            string[] fields = ["assaultRifleRig", "pistolRig", "machineGunRig"];
            for (var slot = 0; slot < fields.Length; slot++)
            {
                var field = Regex.Match(text, @"^PROPERTY\t" + fields[slot] + @"\t9\t(\d+)\t0$", RegexOptions.Multiline);
                Check(field.Success, file + " is missing a weapon slot.");
                var id = field.Groups[1].Value;
                Check(Regex.Matches(text, @"^ENTITY\t" + id + @"\t", RegexOptions.Multiline).Count == 1,
                    file + " references a missing/duplicate weapon entity.");
                var binding = Regex.Match(text, @"COMPONENT\t" + id + @"\tScriptComponent\t1\n(.*?)END_COMPONENT", RegexOptions.Singleline).Value;
                Check(binding.Contains("CoD.Scripts.WeaponHandBinding") && binding.Contains($"PROPERTY\tweaponId\t2\t{WeaponCatalog.At(slot).Id}\t0") &&
                    binding.Contains("PROPERTY\towner\t9\t1\t0"), file + " has an unowned or mismatched weapon binding.");
            }
        }
        Console.WriteLine("PASS: weapon identities, per-slot ammo preservation, trigger modes, invalid selection, reset, wraparound, host catalog and switch cadence.");
    }
}
