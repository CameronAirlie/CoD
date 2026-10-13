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
        Check(account.TryBuy("lmg", supply, out _) && !account.Owns(0) && account.Owns(1) && account.Owns(2) && account.Money == 10100,
            "Buying a second primary did not replace the first while preserving the pistol.");
        account.LoseEquipment(); Check(!account.Owns(0) && !account.Owns(2) && account.Owns(1) && account.Money == 10100, "Death reset currency or retained purchased weapons.");
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
        var bot = new BotEquipmentState(); var botWallet = new EconomyAccount(); var botSupply = new GrenadeSupply();
        Check(bot.Equipment.Weapons.Equipped == WeaponCatalog.Pistol && bot.Equipment.Weapons.Magazine == 12, "Bot did not start with a pistol.");
        foreach (var id in BotEquipmentRules.Purchases(botWallet, botSupply, false).ToArray())
            Check(botWallet.TryBuy(id, botSupply, out _), "Bot planned an invalid starting purchase.");
        Check(botWallet.Money >= 500 && !botWallet.Owns(0), "Bot bypassed starting money or spent its primary savings.");
        botWallet.Credit(3000);
        foreach (var id in BotEquipmentRules.Purchases(botWallet, botSupply, false).ToArray())
            botWallet.TryBuy(id, botSupply, out _);
        Check(botWallet.Owns(0) && botWallet.Owns(1) && !botWallet.Owns(2), "Bot did not buy its two-weapon loadout.");
        Check(BotEquipmentRules.WeaponSlot(botWallet, bot.Equipment.Weapons) == 0, "Bot did not choose owned primary.");
        bot.Equipment.Weapons.Select(0); bot.Equipment.Weapons.Magazine = 0;
        Check(BotEquipmentRules.WeaponSlot(botWallet, bot.Equipment.Weapons) == 1, "Dry primary did not fall back to pistol.");
        bot.Equipment.Weapons.Select(1);
        Check(BotEquipmentRules.WeaponSlot(botWallet, bot.Equipment.Weapons) == 1, "Bot oscillates back to dry primary.");
        Check(BotEquipmentRules.Throwable(botSupply, 5, 1, true) is null && BotEquipmentRules.Throwable(botSupply, 20, 1, false) is null,
            "Bot planned an unsafe or unseen throw.");
        foreach (var kind in GrenadeRules.Types)
        {
            var tuning = GrenadeRules.Find(kind)!; var origin = new Vector3(0, 1.5f, 0); var target = new Vector3(20, .2f, 0);
            Check(BotEquipmentRules.Aim(tuning, origin, target, out var aim, out var strength, out var flight), "Bot throw solver failed.");
            var velocity = (aim * tuning.ThrowSpeed + Vector3.UnitY * tuning.UpwardSpeed) * strength;
            var landing = origin + velocity * flight - Vector3.UnitY * (.5f * tuning.Gravity * flight * flight);
            Check(Vector3.Distance(landing, target) < .001f && MathF.Abs(aim.Length() - 1) < .001f && strength is >= .2f and <= 1,
                "Bot throw plan violates shared launch rules or misses its target.");
        }
        Console.WriteLine("PASS: economy transactions, ownership, wallet cap, inventory limits, throw cooldown, smoke geometry, flash exposure and molotov impact.");
    }
}
