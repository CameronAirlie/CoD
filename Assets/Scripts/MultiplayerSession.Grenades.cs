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
    public string SelectedThrowable { get; private set; } = "frag";
    private int[] _throwableCounts = [GrenadeRules.GrenadesPerLife, 0, 0, 0];
    public int ThrowableCount(string kind) => GrenadeRules.Types.IndexOfKind(kind) is var i && i >= 0 ? _throwableCounts[i] : 0;
    public void SelectThrowable(string kind)
    { if (GrenadeRules.Find(kind) is not null) SelectedThrowable = kind; }

    private bool GrenadesOffline => _server is null && _client is null && mode.Equals("Offline", StringComparison.OrdinalIgnoreCase);

    private static void PreloadGrenadePrefabs()
    {
        foreach (var name in new[] { "Frag", "Flash", "Sparks", "Smoke", "SmokeCloud", "Fire", "Flashbang", "SmokeGrenade", "Molotov" })
            if (!Prefab.Preload(GrenadeEffectPrefab(name)))
                Debug.LogWarning($"Could not preload grenade prefab: {name}.");
    }

    private static string GrenadeEffectPrefab(string name) => name switch
    {
        "Frag" => "project://Prefabs/Grenades/Frag.plutoprefab",
        "Flash" => "project://Prefabs/Grenades/Flash.plutoprefab",
        "Sparks" => "project://Prefabs/Grenades/Sparks.plutoprefab",
        "Smoke" => "project://Prefabs/Grenades/Smoke.plutoprefab",
        "SmokeCloud" => "project://Prefabs/Grenades/SmokeCloud.plutoprefab",
        "Fire" => "project://Prefabs/Grenades/Fire.plutoprefab",
        "Flashbang" => "project://Prefabs/Grenades/Flashbang.plutoprefab",
        "SmokeGrenade" => "project://Prefabs/Grenades/SmokeGrenade.plutoprefab",
        "Molotov" => "project://Prefabs/Grenades/Molotov.plutoprefab",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown grenade effect.")
    };

    private void UpdateGrenades(float deltaTime)
    {
        if (_shutDown) return;
        if (!IsDedicated && Input.CursorLocked && Input.IsKeyPressed(KeyCode.T))
        {
            var index = GrenadeRules.Types.IndexOfKind(SelectedThrowable);
            for (var step = 1; step <= GrenadeRules.Types.Count; step++)
            {
                var kind = GrenadeRules.Types[(index + step) % GrenadeRules.Types.Count];
                if (ThrowableCount(kind) > 0) { SelectedThrowable = kind; break; }
            }
        }
        if (!IsDedicated && Input.CursorLocked && !GamePause.IsPaused && Input.IsKeyPressed(KeyCode.G) &&
            _playerController is not null && _playerHealth?.IsDead != true &&
            (GrenadesOffline || CanFight) && !IsUsingObjective && _inventory?.IsUsing != true && _time >= _nextLocalThrowAt && ThrowableCount(SelectedThrowable) > 0)
        {
            var aim = _playerController.GrenadeAim;
            var request = GrenadeThrow.From(aim.Direction, SelectedThrowable);
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
                    var explosion = new GrenadeExplosion(point.X, point.Y, point.Z, grenade.Projectile.Definition.Id);
                    DetonateThrowable(grenade, point);
                    _server?.BroadcastJson(GrenadeExplosionChannel, explosion);
                }
            }
            var frame = new GrenadeFrame(_grenades.Select(p => new GrenadePosition(p.Key,
                p.Value.Projectile.Position.X, p.Value.Projectile.Position.Y, p.Value.Projectile.Position.Z, p.Value.Projectile.Definition.Id)).ToArray(), CaptureAreas());
            ApplyGrenadeFrame(frame);
            if (_server is not null && _time >= _nextGrenadeFrameAt)
            {
                _server.BroadcastJson(GrenadeFrameChannel, frame);
                _nextGrenadeFrameAt = _time + .05f;
            }
        }
        UpdateThrowableAreas();
        for (var i = _grenadeEffects.Count - 1; i >= 0; i--)
            if (_time >= _grenadeEffects[i].Expires)
            { _grenadeEffects[i].Entity.Destroy(); _grenadeEffects.RemoveAt(i); }
    }

    private void AcceptGrenade(int owner, GrenadeThrow request)
    {
        if ((!GrenadesOffline && (_server is null || _matchPhase != MatchPhase.Playing)) ||
            !GrenadeRules.IsFinite(request.Direction) || request.Direction.LengthSquared() < .5f ||
            request.Direction.LengthSquared() > 2 || _grenades.Count >= 64) return;
        var definition = GrenadeRules.Find(request.Kind);
        if (definition is null) return;
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
        if (!supply.TrySpend(request.Kind, _time)) { SendGrenadeSupply(owner, supply.Remaining); return; }
        var direction = Vector3.Normalize(request.Direction);
        // The client supplies direction only. The host chooses the launch position.
        var origin = owner == 0 && _playerController is not null
            ? _playerController.GrenadeAim.Origin : ParticipantAimPoint(actor);
        var launch = origin + direction * .4f;
        if (Physics.Raycast(origin, direction, .5f, actor, out var obstruction))
            launch = obstruction.Point + obstruction.Normal * .09f;
        _grenades.Add(++_nextGrenadeId, new(owner, team, new(definition, launch, direction)));
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
            var armour = _humans.TryGetValue(pair.Key, out var human) && human.Armour > 0;
            state.Health = MathF.Max(0, state.Health - ApplyHumanArmour(pair.Key, damage));
            state.LastDamagedAt = _time; state.LastHitDirection = target - point;
            PublishHitEffect(pair.Key);
            ConfirmShooterHit(grenade.Owner, damage, false, state.Health <= 0, armour);
            if (state.Health <= 0) RegisterKill(grenade.Owner, pair.Key);
        }
    }

    private void SendGrenadeSupply(int owner, int count)
    {
        var supply = GrenadesOffline ? _offlineGrenades : _playerStates.GetValueOrDefault(owner)?.Grenades;
        var counts = supply?.Snapshot() ?? new int[4];
        if (owner == 0) { _remainingGrenades = count; _throwableCounts = counts; }
        else if (!_bots.ContainsKey(owner)) _server?.SendJson(owner, GrenadeSupplyChannel, new GrenadeInventory(count, counts));
    }
    private void ResetGrenadeSupply(int owner)
    {
        if (_playerStates.TryGetValue(owner, out var state)) { state.Grenades.Reset(); SendGrenadeSupply(owner, state.Grenades.Remaining); }
    }
    private void ResetLocalGrenades()
    {
        if (GrenadesOffline) { _offlineGrenades.Reset(); _offlineAccount.LoseEquipment(); SendGrenadeSupply(0, _offlineGrenades.Remaining); SendEconomy(0); }
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
            if (explosion is not null && GrenadeRules.IsFinite(explosion.Position)) PresentThrowableExplosion(explosion.Position, explosion.Kind);
        }
        else if (message.Channel == GrenadeSupplyChannel)
        {
            var inventory = message.GetJson<GrenadeInventory>();
            if (inventory?.Counts is { Length: 4 } counts && counts.All(n => n >= 0 && n <= 2) && counts.Sum() <= 4)
            { _throwableCounts = counts; _remainingGrenades = counts.Sum(); }
        }
        else return false;
        return true;
    }

    private void ApplyGrenadeFrame(GrenadeFrame frame)
    {
        ApplyThrowableAreas(frame.Areas);
        if (IsDedicated) return;
        if (frame.Items is null || frame.Items.Length > 64 || frame.Items.Any(p => !GrenadeRules.IsFinite(p.Position) || GrenadeRules.Find(p.Kind) is null)) return;
        var present = frame.Items.Select(p => p.Id).ToHashSet();
        foreach (var id in _grenadeVisuals.Keys.ToArray())
            if (!present.Contains(id)) { _grenadeVisuals[id].Destroy(); _grenadeVisuals.Remove(id); }
        foreach (var item in frame.Items)
        {
            if (!_grenadeVisuals.TryGetValue(item.Id, out var visual))
            {
                var prefab = item.Kind switch {
                    "flash" => GrenadeEffectPrefab("Flashbang"), "smoke" => GrenadeEffectPrefab("SmokeGrenade"),
                    "molotov" => GrenadeEffectPrefab("Molotov"), _ => GrenadePrefab };
                visual = Prefab.Instantiate(prefab, item.Position);
                if (visual is null) continue;
                _grenadeVisuals.Add(item.Id, visual);
            }
            visual.WorldPosition = item.Position;
            visual.Rotation = new(_time * 160, _time * 90, 0);
        }
    }
    private void PlayGrenadeExplosion(Vector3 position)
    {
        if (IsDedicated) return;
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
        ClearThrowableAreas();
        _grenades.Clear();
        foreach (var entity in _grenadeVisuals.Values) entity.Destroy();
        _grenadeVisuals.Clear();
        foreach (var effect in _grenadeEffects) effect.Entity.Destroy();
        _grenadeEffects.Clear();
    }

    private sealed record ActiveGrenade(int Owner, PlayerTeam? Team, GrenadeProjectile Projectile);
    private sealed record GrenadeThrow(float X, float Y, float Z, string Kind = "frag")
    {
        public Vector3 Direction => new(X, Y, Z);
        public static GrenadeThrow From(Vector3 direction, string kind = "frag") => new(direction.X, direction.Y, direction.Z, kind);
    }
    private sealed record GrenadePosition(int Id, float X, float Y, float Z, string Kind = "frag") { public Vector3 Position => new(X, Y, Z); }
    private sealed record GrenadeFrame(GrenadePosition[] Items, ThrowableAreaFrame[]? Areas = null);
    private sealed record GrenadeExplosion(float X, float Y, float Z, string Kind = "frag") { public Vector3 Position => new(X, Y, Z); }
    private sealed record GrenadeInventory(int Count, int[]? Counts = null);
}
