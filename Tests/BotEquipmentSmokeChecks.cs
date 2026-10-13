using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

// Test-only access to the native host adapter; excluded from shipped scripts.
public sealed partial class MultiplayerSession
{
    internal void CheckNativeBotEquipment()
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        _matchPhase = MatchPhase.Playing; _buyEndsAt = _time + 20;
        var actor = _remotePlayers[7].GameObject;
        var bot = new BotController(actor, actor.WorldPosition, 1);
        _bots.Add(7, bot); _playerStates[7].Health = 200; _playerStates[7].Grenades.Reset();
        Check(bot.Loadout.Equipment.Weapons.SelectedSlot == 1, "Bot did not spawn with a pistol.");
        var account = Account(7);
        TickBotEquipment(7, bot, true);
        Check(!account.Owns(0) && account.Money >= 500 && _playerStates[7].Grenades.Count("flash") == 1,
            "Bot starting purchase bypassed money/inventory rules.");
        account.Credit(3000); _time += 1.1f; TickBotEquipment(7, bot, true);
        var equipment = bot.Loadout.Equipment;
        Check(account.Owns(0) && equipment.Weapons.SelectedSlot == 0, "Bot failed to buy/equip a primary.");
        equipment.Weapons.Magazine = 0; _time += .5f; TickBotEquipment(7, bot, true);
        Check(equipment.Weapons.SelectedSlot == 1 && equipment.Weapons.Magazine == 12, "Bot did not switch to its sidearm.");
        equipment.Weapons.Magazine = 0; _time += .5f; TickBotEquipment(7, bot, true);
        _time += .5f; TickBotEquipment(7, bot, true);
        Check(equipment.Reloading, "Bot failed to reload empty weapons.");
        var reserve = equipment.Reserve;
        _time += WeaponCatalog.AssaultRifle.ReloadSeconds + .1f; TickBotEquipment(7, bot, true);
        Check(!equipment.Reloading && equipment.Weapons.Magazine == 30 && equipment.Reserve == reserve - 30,
            "Bot reload did not consume finite reserve ammo.");
        var oldMoney = account.Money;
        _buyEndsAt = _time - 1; account.Credit(3200); oldMoney = account.Money;
        AcceptPurchase(7, "lmg");
        Check(account.Money == oldMoney && !account.Owns(2), "Bot bought after buy time.");
        bot.TargetPeerId = 0;
        var before = _playerStates[7].Grenades.Remaining;
        Check(TryBotThrowable(7, bot, GameObject.WorldPosition, true) && _playerStates[7].Grenades.Remaining == before - 1,
            "Bot AI failed to launch an owned grenade through the host validator.");
        Check(!TryBotThrowable(7, bot, GameObject.WorldPosition, true), "Bot AI bypassed throw cooldown.");
        var grenade = _grenades.Values.Single(item => item.Owner == 7);
        Check(grenade.Projectile.Definition == GrenadeRules.Frag, "Bot changed throwable tuning.");
        _time += 7; _playerStates[7].Grenades.TrySpend("frag", _time); bot.Loadout.NextThrow = 0;
        _time += GrenadeRules.ThrowCooldown;
        Check(TryBotThrowable(7, bot, GameObject.WorldPosition, true) && _grenades.Values.Any(item => item.Owner == 7 && item.Projectile.Definition.Id == "molotov"),
            "Bot did not use its purchased molotov.");
        _time += 7; bot.Loadout.NextThrow = 0;
        Check(!TryBotThrowable(7, bot, GameObject.WorldPosition, true), "Bot used a flash at an unsafe distance.");
        bot.Loadout.NextThrow = 0;
        Check(TryBotThrowable(7, bot, new Vector3(28, .7f, 0), true) && _grenades.Values.Any(item => item.Owner == 7 && item.Projectile.Definition.Id == "flash"),
            "Bot did not use its purchased flash at safe range.");
        _time += 7; _buyEndsAt = _time + 1; AcceptPurchase(7, "smoke"); _playerStates[7].Health = 20; bot.Loadout.NextThrow = 0;
        Check(TryBotThrowable(7, bot, GameObject.WorldPosition, true) && _grenades.Values.Any(item => item.Owner == 7 && item.Projectile.Definition.Id == "smoke"),
            "Wounded bot did not use its purchased smoke.");
        var transform = PlayerTransform.From(7, actor) with { WeaponSlot = equipment.Weapons.SelectedSlot };
        Check(transform.WeaponSlot == 0, "Bot weapon pose was not included in replication.");
        ClearGrenades(); _bots.Remove(7);

        // Exercise the offline adapter without changing the actual shipped scene.
        var savedMode = mode; var savedServer = _server; _server = null; mode = "Offline";
        try
        {
            var offline = OfflineBot(actor, 100);
            var offlineSupply = _playerStates[offline].Grenades;
            var humanSupply = _offlineGrenades.Remaining;
            AcceptPurchase(offline, "smoke");
            Check(offlineSupply.Count("smoke") == 1 && _offlineAccount.Money == 800 && _offlineGrenades.Remaining == humanSupply,
                "Offline bot purchase spent the human wallet or inventory.");
            AcceptGrenade(offline, GrenadeThrow.From(Vector3.UnitY, "smoke"));
            Check(offlineSupply.Count("smoke") == 0 && _offlineGrenades.Remaining == humanSupply, "Offline bot throw spent human inventory.");
            UnregisterOfflineBot(actor); ClearGrenades();
            Check(!_offlineBotIds.ContainsKey(actor.EntityId), "Offline bot state leaked after removal.");
        }
        finally { mode = savedMode; _server = savedServer; }
    }
}
