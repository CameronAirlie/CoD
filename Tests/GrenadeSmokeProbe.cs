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
                System.IO.File.WriteAllText(resultPath, "PASS: native host throws, inventory, prefab mesh, fuse detonation, radial damage, friendly immunity, cover, kill scoring, particle emission, cleanup and replenishment.");
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
