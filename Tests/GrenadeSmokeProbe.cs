using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

// Compiled only by the isolated smoke runner; never included in the shipped assembly.
public sealed partial class MultiplayerSession
{
    internal void GrenadeSmokeStart()
    {
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        _matchPhase = MatchPhase.Playing;
        foreach (var (id, name, team) in new[] { (7, "Enemy", PlayerTeam.Bravo), (8, "Friendly", PlayerTeam.Alpha), (9, "Covered", PlayerTeam.Bravo) })
        {
            var actor = PlutoGE.ScriptCore.GameObject.Find(name)!;
            _remotePlayers[id] = new(actor, actor.WorldPosition, 0);
            _bots[id] = new(actor, actor.WorldPosition, 1, 30);
            _playerStates[id] = new(team, true) { Health = 200 };
            _peerNames[id] = name;
        }
        _playerStates[0].Team = PlayerTeam.Alpha;
        AcceptGrenade(0, new(float.NaN, 0, 0));
        Check(_grenades.Count == 0 && RemainingGrenades == 2, "Invalid direction accepted.");
        AcceptGrenade(0, new(1, 0, 0));
        Check(_grenades.Count == 1 && RemainingGrenades == 1, "Host throw failed.");
        AcceptGrenade(0, new(1, 0, 0));
        Check(_grenades.Count == 1, "Host cooldown failed.");
        ApplyGrenadeFrame(new([new(1, 0, .7f, 0)]));
        Check(_grenadeVisuals.Count == 1 && _grenadeVisuals[1].GetComponent<MeshComponent>() is not null, "Grenade prefab did not instantiate.");
        ApplyGrenadeFrame(new([new(3, 0, .7f, 0, "flash"), new(4, 1, .7f, 0, "smoke"), new(5, 2, .7f, 0, "molotov")]));
        Check(_grenadeVisuals.Count == 3, "New throwable projectile prefabs failed to instantiate.");
        _grenades.Clear();
        // Short stationary fuse exercises the real explosion path without waiting on wall-clock time.
        var tuning = GrenadeRules.Frag with { Fuse = .025f, ThrowSpeed = 0, UpwardSpeed = 0, Gravity = 0 };
        var grenade = new ActiveGrenade(0, PlayerTeam.Alpha, new(tuning, new(0, .7f, 0), Vector3.UnitX));
        _grenades[2] = grenade;
        UpdateGrenades(.05f);
        Check(_grenades.Count == 0, "Fused grenade did not detonate.");
        Check(_playerStates[7].Health < 200, "Exposed enemy did not take grenade damage.");
        Check(_playerStates[8].Health == 200, "Friendly took grenade damage.");
        Check(_playerStates[9].Health == 200, "Cover failed to block grenade damage.");
        Check(_grenadeEffects.Count == 3, "Explosion prefabs did not instantiate.");
        _playerStates[7].Health = 1;
        DamageGrenadeTargets(grenade, grenade.Projectile.Position);
        Check(_playerStates[7].Health == 0 && _playerStates[7].Deaths == 1 && _playerStates[0].Kills == 1,
            "Grenade elimination did not update scoring.");
        _bots.Clear(); // Keep fixture actors out of normal host population removal.
        _matchPhase = MatchPhase.Waiting; // Prevent normal host bot combat during the probe.
    }

    internal void GrenadeSmokeFinish()
    {
        if (!_grenadeEffects.Any(p => p.Entity.GetComponent<ParticleSystemComponent>()?.ParticleCount > 0))
            throw new InvalidOperationException("Explosion did not emit native particles.");
        _time += 4;
        UpdateGrenades(0);
        if (_grenadeEffects.Count != 0 || _grenadeVisuals.Count != 0)
            throw new InvalidOperationException("Grenade effects or projectiles leaked.");
        ResetGrenadeSupply(0);
        if (RemainingGrenades != 2) throw new InvalidOperationException("Host grenade replenishment failed.");
        _matchPhase = MatchPhase.Playing; // Empty fixture host otherwise returns to Waiting.
        _buyEndsAt = _time + 20;
        var wallet = Account(0);
        var beforePurchases = wallet.Money;
        AcceptPurchase(0, "flash"); AcceptPurchase(0, "smoke");
        if (wallet.Money != beforePurchases - 500 || _playerStates[0].Grenades.Count("flash") != 1 || _playerStates[0].Grenades.Count("smoke") != 1)
            throw new InvalidOperationException($"Native purchases failed: before={beforePurchases}, money={wallet.Money}, flash={_playerStates[0].Grenades.Count("flash")}, smoke={_playerStates[0].Grenades.Count("smoke")}, phase={_matchPhase}, health={_playerStates[0].Health}, status={ShopStatus}.");
        AcceptPurchase(0, "ar");
        if (wallet.Money != beforePurchases - 500 || wallet.Owns(0)) throw new InvalidOperationException("Unaffordable native weapon request accepted.");
        _buyEndsAt = _time - 1; AcceptPurchase(0, "flash");
        if (wallet.Money != beforePurchases - 500) throw new InvalidOperationException("Closed buy window accepted request.");
        var human = _humans[0];
        human.Input.Accept(PlayerCommand.Idle with { Sequence = human.Input.LastSequence + 1, Slot = 0 }, _time);
        _movementAccumulator = 0; TickHumans(1f / 60);
        if (human.Equipment.Weapons.SelectedSlot != 1) throw new InvalidOperationException("Host equipped an unowned rifle.");
        wallet.Credit(2700); _buyEndsAt = _time + 20; AcceptPurchase(0, "ar");
        human.Input.Accept(PlayerCommand.Idle with { Sequence = human.Input.LastSequence + 1, Slot = 0 }, _time);
        _movementAccumulator = 0; TickHumans(1f / 60);
        if (!wallet.Owns(0) || human.Equipment.Weapons.SelectedSlot != 0) throw new InvalidOperationException("Purchased rifle did not equip on host.");
        var origin = new Vector3(0, .7f, 0);
        _playerStates[7].Health = 200; // Restore the victim killed by the frag assertions.
        DetonateThrowable(new(0, PlayerTeam.Alpha, new(GrenadeRules.Molotov, origin, Vector3.UnitX)), origin);
        UpdateThrowableAreas();
        var fire = _throwableAreas.Values.Single();
        if (fire.Visual?.GetComponent<ParticleSystemComponent>()?.ParticleCount <= 0)
            throw new InvalidOperationException("Persistent fire failed to emit native particles.");
        var health = _playerStates[7].Health;
        _time += .3f; UpdateThrowableAreas();
        if (_playerStates[7].Health >= health) throw new InvalidOperationException("Molotov failed to deal ongoing damage.");
        DetonateThrowable(new(0, PlayerTeam.Alpha, new(GrenadeRules.Smoke, origin, Vector3.UnitX)), origin);
        UpdateThrowableAreas();
        if (!SightBlocked(new(-5, 1, 0), new(5, 1, 0)) || _throwableAreas.Values.Any(area => area.Kind == "molotov"))
            throw new InvalidOperationException("Smoke failed to block sight or extinguish fire.");
        if (!_throwableAreas.Values.Any(area => area.Visual?.GetComponent<ParticleSystemComponent>()?.ParticleCount > 0))
            throw new InvalidOperationException("Persistent smoke failed to emit native particles.");
        ClearThrowableAreas();
        if (SightBlocked(new(-5, 1, 0), new(5, 1, 0))) throw new InvalidOperationException("Smoke sight blocker leaked.");
        DetonateThrowable(new(0, PlayerTeam.Alpha, new(GrenadeRules.Flash, origin, Vector3.UnitX)), origin);
        if (!IsBlinded(_remotePlayers[7].GameObject) || IsBlinded(_remotePlayers[9].GameObject))
            throw new InvalidOperationException("Flash exposure or cover rejection failed.");
        _time += 5; UpdateThrowableAreas();
        if (IsBlinded(_remotePlayers[7].GameObject)) throw new InvalidOperationException("Flash blindness failed to expire.");
    }
}

public sealed class GrenadeSmokeProbe : ScriptBehaviour
{
    [SerializedField] private string resultPath = "";
    private int _frame;
    private bool _done;
    public override void OnUpdate(float deltaTime)
    {
        if (_done) return;
        try
        {
            var session = GameObject.GetComponent<MultiplayerSession>()!;
            if (++_frame == 10) session.GrenadeSmokeStart();
            if (_frame == 13)
            {
                session.GrenadeSmokeFinish();
                System.IO.File.WriteAllText(resultPath, "PASS: native throws, inventory, fuse, damage, cover, kills, purchases, buy window, molotov damage/particles, smoke sight/particles, extinguishing and cleanup.");
                _done = true;
            }
        }
        catch (Exception error)
        {
            System.IO.File.WriteAllText(resultPath, "FAIL: " + error);
            _done = true;
        }
    }
}
