using CoD.Scripts;
using System.Numerics;

internal static class EconomyTests
{
    public static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var account = new EconomyAccount(); var supply = new GrenadeSupply();
        Check(account.Money == 800 && account.Owns(1) && !account.Owns(0), "Starting wallet/sidearm incorrect.");
        Check(!account.TryBuy("ar", supply, out _) && account.Money == 800, "Unaffordable purchase charged wallet.");
        Check(!account.TryBuy("not-an-item", supply, out _) && account.Money == 800, "Unknown item accepted.");
        Check(!account.TryBuy("pistol", supply, out _), "Duplicate weapon purchase accepted.");
        Check(account.TryBuy("flash", supply, out _) && account.Money == 600 && supply.Count("flash") == 1, "Flash purchase not atomic.");
        Check(account.TryBuy("smoke", supply, out _) && account.Money == 300 && supply.Remaining == 4, "Smoke purchase failed.");
        Check(!account.TryBuy("flash", supply, out _) && account.Money == 300, "Full inventory charged wallet.");
        account.Credit(int.MaxValue); account.Credit(int.MaxValue);
        Check(account.Money == 16000, "Wallet overflow/cap failed.");
        account.Credit(-500); Check(account.Money == 16000, "Negative reward reduced money.");
        Check(account.TryBuy("ar", supply, out _) && account.Owns(0) && account.Money == 13300, "Weapon ownership purchase failed.");
        account.LoseEquipment(); Check(!account.Owns(0) && account.Owns(1) && account.Money == 13300, "Death reset currency or retained purchased weapons.");
        Check(supply.TrySpend("flash", 0) && !supply.TrySpend("smoke", .1f) && supply.Count("smoke") == 1, "Cross-type throw cooldown bypass.");
        Check(!supply.TrySpend("molotov", 1), "Unowned throwable accepted.");
        Check(supply.TrySpend("smoke", 1) && supply.TryAdd("molotov"), "Freed throwable slots not reusable.");
        supply.Reset(); Check(supply.Count("frag") == 2 && supply.Count("molotov") == 0, "Respawn retained purchased throwables.");
        Check(GrenadeRules.IntersectsSmoke(new(-10, 0, 0), new(10, 0, 0), Vector3.Zero), "Smoke fails to block crossing sightline.");
        Check(!GrenadeRules.IntersectsSmoke(new(-10, 5, 0), new(10, 5, 0), Vector3.Zero), "Smoke blocks outside its radius.");
        Check(GrenadeRules.IntersectsSmoke(Vector3.Zero, Vector3.Zero, Vector3.Zero), "Inside smoke zero-length ray missed.");
        Check(GrenadeRules.FlashSeconds(2, 1) > GrenadeRules.FlashSeconds(2, -1) && GrenadeRules.FlashSeconds(18, 1) == 0,
            "Flash facing/distance attenuation failed.");
        Check(GrenadeRules.FlashSeconds(float.NaN, 1) == 0, "Invalid flash exposure accepted.");
        var fire = new GrenadeProjectile(GrenadeRules.Molotov, Vector3.Zero, Vector3.UnitX);
        fire.Tick(.02f, (_, end) => new(end, Vector3.UnitY));
        Check(fire.Exploded && fire.Age < fire.Definition.Fuse, "Molotov did not ignite on impact.");
        Console.WriteLine("PASS: economy transactions, ownership, wallet cap, inventory limits, throw cooldown, smoke geometry, flash exposure and molotov impact.");
    }
}
