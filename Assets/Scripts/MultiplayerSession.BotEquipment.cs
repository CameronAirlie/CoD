using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

public sealed partial class MultiplayerSession
{
    private readonly Dictionary<uint, int> _offlineBotIds = new();
    private int OfflineBot(GameObject actor, float health)
    {
        if (!_offlineBotIds.TryGetValue(actor.EntityId, out var peer))
        {
            peer = _nextBotId--;
            _offlineBotIds.Add(actor.EntityId, peer);
            _playerStates.Add(peer, new(PlayerTeam.Bravo, true));
            _remotePlayers.Add(peer, new(actor, actor.WorldPosition, 0));
            _bots.Add(peer, new(actor, actor.WorldPosition, actor.EntityId));
            EquipHardpointBot(peer, _bots[peer]);
            PresentBotWeapon(_bots[peer]);
        }
        _playerStates[peer].Health = health;
        return peer;
    }
    public static void UpdateOfflineBotEquipment(GameObject actor, float health)
    {
        if (_activeSession is not { GrenadesOffline: true } session || !actor.IsValid) return;
        var peer = session.OfflineBot(actor, health);
        session.TickBotEquipment(peer, session._bots[peer], health > 0 && !GamePause.IsPaused && !IsBlinded(actor));
    }
    public static bool TryOfflineBotCombat(GameObject actor, GameObject target, float health)
    {
        if (_activeSession is not { GrenadesOffline: true } session) return false;
        var peer = session.OfflineBot(actor, health);
        var bot = session._bots[peer];
        if (GamePause.IsPaused || health <= 0 || IsBlinded(actor) || session._playerHealth?.IsDead == true || session._time < bot.NextShotAt) return true;
        if (session.TryBotThrowable(peer, bot, target.WorldPosition, true)) return true;
        var equipment = bot.Loadout.Equipment;
        if (!equipment.TryFire(true, session._time, true)) return true;
        var origin = ParticipantAimPoint(actor);
        var ray = ParticipantAimPoint(target) - origin;
        if (ray.LengthSquared() < .001f) return true;
        var direction = ApplyBotSpread(Vector3.Normalize(ray), session.botAccuracyDegrees, bot);
        session.PublishShotEffect(peer);
        if (Physics.Raycast(origin, direction, equipment.Weapons.Equipped.Range, actor, out var hit) &&
            (hit.Entity.EntityId == target.EntityId || hit.Entity.HasTag("Player")))
            session.DamageOfflinePlayer(peer, equipment.Weapons.Equipped.Damage *
                (hit.Entity.HasTag("Head") ? MathF.Max(1, session.multiplayerHeadshotMultiplier) : 1));
        bot.NextShotAt = session._time + equipment.Weapons.Equipped.ShotInterval;
        return true;
    }
    private void DamageOfflinePlayer(int owner, float damage)
    {
        if (_playerHealth is null || _playerHealth.IsDead || damage <= 0) return;
        _playerHealth.TakeDamage(damage);
        if (_playerHealth.IsDead && _accounts.TryGetValue(owner, out var account)) account.Credit(EconomyRules.KillReward);
    }
    public static void OfflineBotDied(GameObject actor)
    {
        if (_activeSession is not { GrenadesOffline: true } session || !session._offlineBotIds.TryGetValue(actor.EntityId, out var peer)) return;
        session._playerStates[peer].Health = 0;
        if (!session.UsesPresetLoadouts) session.Account(peer).LoseEquipment();
    }
    public static void UnregisterOfflineBot(GameObject actor)
    {
        if (_activeSession is not { GrenadesOffline: true } session || !session._offlineBotIds.Remove(actor.EntityId, out var peer)) return;
        session._bots.Remove(peer); session._playerStates.Remove(peer);
        session._remotePlayers.Remove(peer); session._accounts.Remove(peer);
    }
    private void PresentBotWeapon(BotController bot)
    {
        var slot = bot.Loadout.Equipment.Weapons.SelectedSlot;
        if (slot == bot.PresentedWeapon) return;
        bot.PresentedWeapon = slot;
        bot.GameObject.TryInvoke("SetWeaponPose", slot == 1 ? 1 : 0);
    }
    private void EquipHardpointBot(int peer, BotController bot)
    {
        if (!UsesPresetLoadouts) return;
        var choice = HardpointLoadouts.ForBot(peer);
        var preset = HardpointLoadouts.Find(choice);
        bot.Loadout.Equipment.Reset(choice);
        Account(peer).SetWeaponLoadout(preset.WeaponMask);
        if (_playerStates.TryGetValue(peer, out var state))
        { state.Grenades.SetLoadout(preset.Throwables); SendGrenadeSupply(peer, state.Grenades.Remaining); }
    }
    private void BuyBotEquipment(int peer, BotController bot)
    {
        if (UsesPresetLoadouts) return;
        if (_time < bot.Loadout.NextBuy) return;
        bot.Loadout.NextBuy = _time + 1;
        if (!_playerStates.TryGetValue(peer, out var state)) return;
        foreach (var id in BotEquipmentRules.Purchases(Account(peer), state.Grenades, (Math.Abs(peer) & 1) == 0).ToArray())
            AcceptPurchase(peer, id);
    }
    private void TickBotEquipment(int peer, BotController bot, bool canAct)
    {
        BuyBotEquipment(peer, bot);
        var equipment = bot.Loadout.Equipment;
        var wasReloading = equipment.Reloading;
        var slot = canAct ? BotEquipmentRules.WeaponSlot(Account(peer), equipment.Weapons) : equipment.Weapons.SelectedSlot;
        equipment.Tick(PlayerCommand.Idle with { Slot = slot, Reload = equipment.Weapons.Magazine == 0 }, _time, canAct);
        if (!wasReloading && equipment.Reloading) PublishBotReload(peer, bot);
        PresentBotWeapon(bot);
        // Release semi-automatic triggers between AI shot decisions.
        equipment.TryFire(false, _time, canAct);
    }
    private bool TryBotThrowable(int peer, BotController bot, Vector3 target, bool visible)
    {
        if (_time < bot.Loadout.NextThrow || bot.Loadout.Equipment.Reloading || IsBlinded(bot.GameObject) ||
            IsDefusal && _defusal?.Snapshot().Operator == peer) return false;
        bot.Loadout.NextThrow = _time + .75f;
        var state = _playerStates[peer];
        var kind = BotEquipmentRules.Throwable(state.Grenades, Vector3.Distance(bot.GameObject.WorldPosition, target),
            state.Health / MathF.Max(1, multiplayerMaximumHealth), visible);
        if (kind is null) return false;
        var definition = GrenadeRules.Find(kind)!;
        var origin = ParticipantAimPoint(bot.GameObject);
        // Aim for the ground so persistent effects cover the opponent's feet.
        if (Physics.Raycast(target + Vector3.UnitY, -Vector3.UnitY, 5, out var ground)) target = ground.Point + Vector3.UnitY * .15f;
        if (!BotEquipmentRules.Aim(definition, origin, target, out var direction, out var strength, out var flight)) return false;
        var safeRadius = kind == "flash" ? 18 : kind == "smoke" ? 0 : definition.BlastRadius + 1;
        if (Vector3.Distance(origin, target) < safeRadius) return false;
        foreach (var ally in _playerStates)
            if (ally.Key != peer && ally.Value.Health > 0 && ally.Value.Team == state.Team &&
                GetParticipantObject(ally.Key) is { } actor && Vector3.Distance(actor.WorldPosition, target) < safeRadius) return false;
        // A bounded arc test avoids throwing into ceilings and nearby cover.
        var velocity = direction * definition.ThrowSpeed * strength + Vector3.UnitY * definition.UpwardSpeed * strength;
        var previous = origin;
        for (var sample = 1; sample <= 12; sample++)
        {
            var t = flight * sample / 12;
            var next = origin + velocity * t - Vector3.UnitY * (.5f * definition.Gravity * t * t);
            var ray = next - previous;
            if (ray.LengthSquared() > .001f && Physics.Raycast(previous, Vector3.Normalize(ray), ray.Length(), bot.GameObject, out var hit) &&
                sample < 12 && hit.Entity.EntityId != GetParticipantObject(bot.TargetPeerId)?.EntityId) return false;
            previous = next;
        }
        var before = state.Grenades.Remaining;
        AcceptGrenade(peer, GrenadeThrow.From(direction, kind, strength));
        if (state.Grenades.Remaining == before) return false;
        bot.Loadout.NextThrow = _time + 6;
        bot.NextShotAt = MathF.Max(bot.NextShotAt, _time + GrenadeRules.ThrowCooldown);
        return true;
    }
}
