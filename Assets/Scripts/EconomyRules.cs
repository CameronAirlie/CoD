namespace CoD.Scripts;

public sealed record ShopItem(string Id, string Name, int Price, int WeaponSlot = -1, string? Throwable = null);

/// <summary>Host-owned prices and transaction rules; requests contain only a catalog id.</summary>
public static class EconomyRules
{
    public const int StartingMoney = 800, MaximumMoney = 16000, KillReward = 300;
    public const int WinReward = 3250, LossReward = 1400;
    public const float BuySeconds = 20;
    public static readonly IReadOnlyList<ShopItem> Catalog = Array.AsReadOnly(new[] {
        new ShopItem("pistol", "P-12 PISTOL", 200, 1),
        new ShopItem("ar", "AR-24 ASSAULT RIFLE", 2700, 0),
        new ShopItem("lmg", "MG-60 MACHINE GUN", 3200, 2),
        new ShopItem("frag", "FRAG GRENADE", 300, Throwable: "frag"),
        new ShopItem("flash", "FLASHBANG", 200, Throwable: "flash"),
        new ShopItem("smoke", "SMOKE GRENADE", 300, Throwable: "smoke"),
        new ShopItem("molotov", "MOLOTOV", 400, Throwable: "molotov") });
    public static ShopItem? Find(string? id) => Catalog.FirstOrDefault(item => item.Id == id);
}

public sealed class EconomyAccount
{
    public int Money { get; private set; } = EconomyRules.StartingMoney;
    public int WeaponMask { get; private set; } = 1 << 1; // Free starting sidearm.
    public bool Owns(int slot) => slot is >= 0 and < WeaponCatalog.SlotCount && (WeaponMask & (1 << slot)) != 0;
    public void Credit(int amount) => Money = (int)Math.Clamp((long)Money + Math.Max(0, amount), 0, EconomyRules.MaximumMoney);
    public void LoseEquipment() => WeaponMask = 1 << 1;
    public bool TryBuy(string? id, GrenadeSupply supply, out string result)
    {
        var item = EconomyRules.Find(id);
        if (item is null) { result = "Unknown item."; return false; }
        if (item.WeaponSlot >= 0 && Owns(item.WeaponSlot)) { result = "Already owned."; return false; }
        if (Money < item.Price) { result = "Not enough money."; return false; }
        if (item.Throwable is { } kind && !supply.TryAdd(kind)) { result = "Throwable inventory full."; return false; }
        if (item.WeaponSlot >= 0) WeaponMask |= 1 << item.WeaponSlot;
        Money -= item.Price; result = $"Purchased {item.Name}."; return true;
    }
}
