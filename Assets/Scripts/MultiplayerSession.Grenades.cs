using System.Numerics;
using PlutoGE.ScriptCore;
using PlutoGE.ScriptCore.Networking;

namespace CoD.Scripts;

/// <summary>Grenade transport and engine adapter. Only the host simulates and applies damage.</summary>
public sealed partial class MultiplayerSession
{
    private const ushort GrenadeThrowChannel = 16;
    private const ushort GrenadeFrameChannel = 17;
    private const ushort GrenadeExplosionChannel = 18;
    private const ushort GrenadeSupplyChannel = 19;
    private const string GrenadePrefab = "project://Prefabs/Grenades/Frag.plutoprefab";
    private readonly Dictionary<int, ActiveGrenade> _grenades = new();
    private readonly Dictionary<int, GameObject> _grenadeVisuals = new();
    private readonly List<(GameObject Entity, float Expires)> _grenadeEffects = new();
    private readonly GrenadeSupply _offlineGrenades = new();
    private int _nextGrenadeId;
    private float _nextGrenadeFrameAt;
    private float _nextLocalThrowAt;
    private int _remainingGrenades = GrenadeRules.GrenadesPerLife;
    public int RemainingGrenades => _remainingGrenades;

    private bool GrenadesOffline => _server is null && _client is null && mode.Equals("Offline", StringComparison.OrdinalIgnoreCase);

    private static void PreloadGrenadePrefabs()
    {
        foreach (var name in new[] { "Frag", "Flash", "Sparks", "Smoke" })
            if (!Prefab.Preload(GrenadeEffectPrefab(name)))
                Debug.LogWarning($"Could not preload grenade prefab: {name}.");
    }

    private static string GrenadeEffectPrefab(string name) => name switch
    {
        "Frag" => "project://Prefabs/Grenades/Frag.plutoprefab",
        "Flash" => "project://Prefabs/Grenades/Flash.plutoprefab",
        "Sparks" => "project://Prefabs/Grenades/Sparks.plutoprefab",
        "Smoke" => "project://Prefabs/Grenades/Smoke.plutoprefab",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown grenade effect.")
    };

    private void UpdateGrenades(float deltaTime)
    {
        if (_shutDown) return;
        if (Input.CursorLocked && !GamePause.IsPaused && Input.IsKeyPressed(KeyCode.F) &&
            _playerController is not null && _playerHealth?.IsDead != true &&
            (GrenadesOffline || CanFight) && !IsUsingObjective && _time >= _nextLocalThrowAt && RemainingGrenades > 0)
        {
            var aim = _playerController.GrenadeAim;
            var request = GrenadeThrow.From(aim.Direction);
            _nextLocalThrowAt = _time + GrenadeRules.ThrowCooldown;
            _playerController.PlayGameplayCue("Melee/Throw/throw_heavy_ufx_1.ogg", .45f);
            if (_server is not null || GrenadesOffline) AcceptGrenade(0, request);
            else if (_localPeerId >= 0) _client?.SendJson(GrenadeThrowChannel, request);
        }

        if (_server is not null || GrenadesOffline)
        {
            if (!GrenadesOffline && _matchPhase != MatchPhase.Playing)
            {
                _grenades.Clear();
            }
            foreach (var pair in _grenades.ToArray())
            {
                var grenade = pair.Value;
                grenade.Projectile.Tick(deltaTime, (start, end) => SweepGrenade(grenade.Owner, start, end));
                if (grenade.Projectile.Exploded)
                {
                    _grenades.Remove(pair.Key);
                    var point = grenade.Projectile.Position;
                    var explosion = new GrenadeExplosion(point.X, point.Y, point.Z);
                    PlayGrenadeExplosion(point);
                    _server?.BroadcastJson(GrenadeExplosionChannel, explosion);
                    if (GrenadesOffline || _matchPhase == MatchPhase.Playing) DamageGrenadeTargets(grenade, point);
                }
            }
            var frame = new GrenadeFrame(_grenades.Select(p => new GrenadePosition(p.Key,
                p.Value.Projectile.Position.X, p.Value.Projectile.Position.Y, p.Value.Projectile.Position.Z)).ToArray());
            ApplyGrenadeFrame(frame);
            if (_server is not null && _time >= _nextGrenadeFrameAt)
            {
                _server.BroadcastJson(GrenadeFrameChannel, frame);
                _nextGrenadeFrameAt = _time + .05f;
            }
        }
        for (var i = _grenadeEffects.Count - 1; i >= 0; i--)
            if (_time >= _grenadeEffects[i].Expires)
            { _grenadeEffects[i].Entity.Destroy(); _grenadeEffects.RemoveAt(i); }
    }

    private void AcceptGrenade(int owner, GrenadeThrow request)
    {
        if ((!GrenadesOffline && (_server is null || _matchPhase != MatchPhase.Playing)) ||
            !GrenadeRules.IsFinite(request.Direction) || request.Direction.LengthSquared() < .5f ||
            request.Direction.LengthSquared() > 2 || _grenades.Count >= 64) return;
        var actor = GetParticipantObject(owner);
        if (actor is null || !actor.IsValid) return;
        GrenadeSupply supply;
        PlayerTeam? team = null;
        if (GrenadesOffline)
        {
            if (_playerHealth?.IsDead == true) return;
            supply = _offlineGrenades;
        }
        else
        {
            if (!_playerStates.TryGetValue(owner, out var state) || state.Health <= 0 ||
                (owner == 0 && _playerHealth?.IsDead == true) ||
                (IsDefusal && _defusal?.Snapshot().Operator == owner)) return;
            supply = state.Grenades; team = state.Team;
        }
        if (!supply.TrySpend(_time)) { SendGrenadeSupply(owner, supply.Remaining); return; }
        var direction = Vector3.Normalize(request.Direction);
        // The client supplies direction only. The host chooses the launch position.
        var origin = owner == 0 && _playerController is not null
            ? _playerController.GrenadeAim.Origin : ParticipantAimPoint(actor);
        var launch = origin + direction * .4f;
        if (Physics.Raycast(origin, direction, .5f, actor, out var obstruction))
            launch = obstruction.Point + obstruction.Normal * .09f;
        _grenades.Add(++_nextGrenadeId, new(owner, team, new(GrenadeRules.Frag, launch, direction)));
        SendGrenadeSupply(owner, supply.Remaining);
    }

    private GrenadeCollision? SweepGrenade(int owner, Vector3 start, Vector3 end)
    {
        var ray = end - start;
        var length = ray.Length();
        if (length < .00001f) return null;
        // Sweep the grenade centre with a radius margin to avoid tunnelling through walls.
        return Physics.Raycast(start, ray / length, length + .08f, GetParticipantObject(owner), out var hit)
            ? new(hit.Point, hit.Normal) : null;
    }

    private bool BlastReaches(Vector3 point, Vector3 target, GameObject actor)
    {
        var ray = target - point;
        var length = ray.Length();
        return length < .001f || !Physics.Raycast(point, ray / length, length, out var hit) ||
            hit.Entity.EntityId == actor.EntityId ||
            (FindPeerForEntity(actor) != -1 && FindPeerForEntity(hit.Entity) == FindPeerForEntity(actor));
    }

    private void DamageGrenadeTargets(ActiveGrenade grenade, Vector3 point)
    {
        if (GrenadesOffline)
        {
            foreach (var actor in GameObject.FindByTag("Enemy"))
            {
                var target = actor.WorldPosition + Vector3.UnitY * .6f;
                var damage = GrenadeRules.Damage(grenade.Projectile.Definition, Vector3.Distance(point, target));
                if (damage > 0 && BlastReaches(point, target, actor)) actor.TryInvoke("TakeDamage", damage);
            }
            return;
        }
        foreach (var pair in _playerStates.ToArray())
        {
            var state = pair.Value;
            if (state.Health <= 0 || state.Team == grenade.Team) continue;
            var actor = GetParticipantObject(pair.Key);
            if (actor is null) continue;
            var target = ParticipantAimPoint(actor);
            var damage = GrenadeRules.Damage(grenade.Projectile.Definition, Vector3.Distance(point, target));
            if (damage <= 0 || !BlastReaches(point, target, actor)) continue;
            var armour = pair.Key == 0 && _playerHealth?.ArmourSlots > 0;
            state.Health = MathF.Max(0, state.Health - damage);
            state.LastDamagedAt = _time; state.LastHitDirection = target - point;
            PublishHitEffect(pair.Key);
            if (pair.Key == 0)
            {
                _playerHealth?.TakeDamageFrom(damage, point.X, point.Y, point.Z);
                if (_playerHealth is not null) state.Health = MathF.Max(0, _playerHealth.CurrentHealth);
            }
            else if (!_bots.ContainsKey(pair.Key))
                _server?.SendJson(pair.Key, DamageChannel, new PlayerDamage(damage, point.X, point.Y, point.Z));
            ConfirmShooterHit(grenade.Owner, damage, false, state.Health <= 0, armour);
            if (state.Health <= 0) RegisterKill(grenade.Owner, pair.Key);
        }
    }

    private void SendGrenadeSupply(int owner, int count)
    {
        if (owner == 0) _remainingGrenades = count;
        else if (!_bots.ContainsKey(owner)) _server?.SendJson(owner, GrenadeSupplyChannel, new GrenadeInventory(count));
    }
    private void ResetGrenadeSupply(int owner)
    {
        if (_playerStates.TryGetValue(owner, out var state)) { state.Grenades.Reset(); SendGrenadeSupply(owner, state.Grenades.Remaining); }
    }
    private void ResetLocalGrenades()
    {
        if (GrenadesOffline) { _offlineGrenades.Reset(); _remainingGrenades = _offlineGrenades.Remaining; }
        else if (_server is not null) ResetGrenadeSupply(0);
        _nextLocalThrowAt = 0;
    }

    private bool HandleGrenadeClientMessage(NetworkMessage message)
    {
        if (message.Channel == GrenadeFrameChannel)
        {
            var frame = message.GetJson<GrenadeFrame>();
            if (frame is not null) ApplyGrenadeFrame(frame);
        }
        else if (message.Channel == GrenadeExplosionChannel)
        {
            var explosion = message.GetJson<GrenadeExplosion>();
            if (explosion is not null && GrenadeRules.IsFinite(explosion.Position)) PlayGrenadeExplosion(explosion.Position);
        }
        else if (message.Channel == GrenadeSupplyChannel)
        {
            var inventory = message.GetJson<GrenadeInventory>();
            if (inventory is not null) _remainingGrenades = Math.Clamp(inventory.Count, 0, GrenadeRules.GrenadesPerLife);
        }
        else return false;
        return true;
    }

    private void ApplyGrenadeFrame(GrenadeFrame frame)
    {
        if (frame.Items is null || frame.Items.Length > 64 || frame.Items.Any(p => !GrenadeRules.IsFinite(p.Position))) return;
        var present = frame.Items.Select(p => p.Id).ToHashSet();
        foreach (var id in _grenadeVisuals.Keys.ToArray())
            if (!present.Contains(id)) { _grenadeVisuals[id].Destroy(); _grenadeVisuals.Remove(id); }
        foreach (var item in frame.Items)
        {
            if (!_grenadeVisuals.TryGetValue(item.Id, out var visual))
            {
                visual = Prefab.Instantiate(GrenadePrefab, item.Position);
                if (visual is null) continue;
                _grenadeVisuals.Add(item.Id, visual);
            }
            visual.WorldPosition = item.Position;
            visual.Rotation = new(_time * 160, _time * 90, 0);
        }
    }
    private void PlayGrenadeExplosion(Vector3 position)
    {
        _playerController?.PlayGameplayCueAt("Explosions/grenade_ufx_1.ogg", position, 1);
        foreach (var (name, count) in new[] { ("Flash", 24), ("Sparks", 70), ("Smoke", 35) })
        {
            var effect = Prefab.Instantiate(GrenadeEffectPrefab(name), position);
            if (effect is null) continue;
            effect.GetComponent<ParticleSystemComponent>()?.EmitAt(position, count);
            _grenadeEffects.Add((effect, _time + 3));
        }
        if (_playerController is not null)
        {
            var strength = MathF.Max(0, 1 - Vector3.Distance(GameObject.WorldPosition, position) / 12);
            _playerController.AddCameraImpulse(strength * 2);
        }
    }
    private void ClearGrenades()
    {
        _grenades.Clear();
        foreach (var entity in _grenadeVisuals.Values) entity.Destroy();
        _grenadeVisuals.Clear();
        foreach (var effect in _grenadeEffects) effect.Entity.Destroy();
        _grenadeEffects.Clear();
    }

    private sealed record ActiveGrenade(int Owner, PlayerTeam? Team, GrenadeProjectile Projectile);
    private sealed record GrenadeThrow(float X, float Y, float Z)
    {
        public Vector3 Direction => new(X, Y, Z);
        public static GrenadeThrow From(Vector3 direction) => new(direction.X, direction.Y, direction.Z);
    }
    private sealed record GrenadePosition(int Id, float X, float Y, float Z) { public Vector3 Position => new(X, Y, Z); }
    private sealed record GrenadeFrame(GrenadePosition[] Items);
    private sealed record GrenadeExplosion(float X, float Y, float Z) { public Vector3 Position => new(X, Y, Z); }
    private sealed record GrenadeInventory(int Count);
}
