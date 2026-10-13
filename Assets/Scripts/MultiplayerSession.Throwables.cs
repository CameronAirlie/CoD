using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Persistent effects are host simulated; snapshots reproduce presentation on clients.</summary>
public sealed partial class MultiplayerSession
{
    private sealed class ThrowableArea(int id, string kind, Vector3 position, float expires, ActiveGrenade? source)
    {
        public int Id = id;
        public string Kind = kind;
        public Vector3 Position = position;
        public float Expires = expires, NextDamage, NextParticles;
        public ActiveGrenade? Source = source;
        public GameObject? Visual;
    }
    private sealed record ThrowableAreaFrame(int Id, string Kind, float X, float Y, float Z, float Remaining);
    private readonly Dictionary<int, ThrowableArea> _throwableAreas = new();
    private readonly Dictionary<uint, float> _flashedActors = new();
    private int _nextAreaId;
    private float _flashEndsAt;
    public float FlashOpacity => Math.Clamp((_flashEndsAt - _time) / 1.5f, 0, 1);

    public static bool SightBlocked(Vector3 start, Vector3 end) => _activeSession is { } session &&
        session._throwableAreas.Values.Any(area => area.Kind == "smoke" && area.Expires > session._time &&
            GrenadeRules.IntersectsSmoke(start, end, area.Position));
    public static bool IsBlinded(GameObject actor) => _activeSession is { } session &&
        session._flashedActors.GetValueOrDefault(actor.EntityId) > session._time;

    private void DetonateThrowable(ActiveGrenade grenade, Vector3 position)
    {
        var kind = grenade.Projectile.Definition.Id;
        PresentThrowableExplosion(position, kind);
        if (kind == "frag") DamageGrenadeTargets(grenade, position);
        else if (kind == "flash")
        {
            var actors = GrenadesOffline ? GameObject.FindByTag("Enemy") :
                _playerStates.Keys.Select(GetParticipantObject).Where(actor => actor is not null).Select(actor => actor!);
            foreach (var actor in actors)
            {
                var seconds = FlashExposure(actor, position, actor.WorldPosition + Vector3.UnitY * .65f, actor.Forward);
                if (seconds > 0) _flashedActors[actor.EntityId] = MathF.Max(_flashedActors.GetValueOrDefault(actor.EntityId), _time + seconds);
            }
        }
        else if (kind is "smoke" or "molotov")
        {
            // Ground the area after the projectile fuse rather than leaving fire in mid-air.
            if (Physics.Raycast(position + Vector3.UnitY * .2f, -Vector3.UnitY, 20, out var ground))
                position = ground.Point + Vector3.UnitY * (kind == "smoke" ? 1.3f : .12f);
            if (_throwableAreas.Count >= 16) RemoveArea(_throwableAreas.Values.MinBy(area => area.Expires)!.Id);
            _throwableAreas.Add(++_nextAreaId, new(_nextAreaId, kind, position, _time + (kind == "smoke" ? 18 : 7), grenade));
        }
    }
    private float FlashExposure(GameObject actor, Vector3 position, Vector3 eye, Vector3 forward)
    {
        var ray = position - eye;
        if (!BlastReaches(position, eye, actor) || SightBlocked(eye, position)) return 0;
        return GrenadeRules.FlashSeconds(ray.Length(), ray.LengthSquared() < .001f ? 1 : Vector3.Dot(forward, Vector3.Normalize(ray)));
    }
    private void PresentThrowableExplosion(Vector3 position, string kind)
    {
        if (IsDedicated) return;
        if (kind == "frag") { PlayGrenadeExplosion(position); return; }
        if (kind == "flash" && _playerController is not null)
        {
            var aim = _playerController.GrenadeAim;
            _flashEndsAt = MathF.Max(_flashEndsAt, _time + FlashExposure(GameObject, position, aim.Origin, aim.Direction));
            _playerController.PlayGameplayCueAt("Explosions/grenade_ufx_1.ogg", position, .35f);
        }
    }
    private ThrowableAreaFrame[] CaptureAreas() => _throwableAreas.Values.Where(area => area.Expires > _time)
        .Select(area => new ThrowableAreaFrame(area.Id, area.Kind, area.Position.X, area.Position.Y, area.Position.Z, area.Expires - _time)).ToArray();
    private void ApplyThrowableAreas(ThrowableAreaFrame[]? frames)
    {
        if (_server is not null || GrenadesOffline || frames is null || frames.Length > 16) return;
        var present = new HashSet<int>();
        foreach (var frame in frames)
        {
            var position = new Vector3(frame.X, frame.Y, frame.Z);
            if (frame.Kind is not ("smoke" or "molotov") || !GrenadeRules.IsFinite(position) ||
                !float.IsFinite(frame.Remaining) || frame.Remaining <= 0 || frame.Remaining > 18) continue;
            present.Add(frame.Id);
            if (_throwableAreas.TryGetValue(frame.Id, out var area)) area.Expires = _time + frame.Remaining;
            else _throwableAreas.Add(frame.Id, new(frame.Id, frame.Kind, position, _time + frame.Remaining, null));
        }
        foreach (var id in _throwableAreas.Keys.ToArray()) if (!present.Contains(id)) RemoveArea(id);
    }
    private void UpdateThrowableAreas()
    {
        if (_server is not null && _matchPhase != MatchPhase.Playing) { ClearThrowableAreas(); return; }
        foreach (var area in _throwableAreas.Values.ToArray())
        {
            if (_time >= area.Expires || area.Kind == "molotov" && SightBlocked(area.Position, area.Position))
            { RemoveArea(area.Id); continue; }
            if ((_server is not null || GrenadesOffline) && area.Source is { } source && area.Kind == "molotov" && _time >= area.NextDamage)
            { area.NextDamage = _time + .25f; DamageGrenadeTargets(source, area.Position); }
            if (!IsDedicated && _time >= area.NextParticles)
            {
                area.NextParticles = _time + .25f;
                area.Visual ??= Prefab.Instantiate(area.Kind == "smoke" ? "project://Prefabs/Grenades/SmokeCloud.plutoprefab" :
                    "project://Prefabs/Grenades/Fire.plutoprefab", area.Position);
                area.Visual?.GetComponent<ParticleSystemComponent>()?.EmitAt(area.Position, area.Kind == "smoke" ? 24 : 12);
            }
        }
        foreach (var id in _flashedActors.Where(pair => pair.Value <= _time).Select(pair => pair.Key).ToArray()) _flashedActors.Remove(id);
    }
    private void RemoveArea(int id)
    { if (_throwableAreas.Remove(id, out var area)) area.Visual?.Destroy(); }
    private void ClearThrowableAreas()
    {
        foreach (var area in _throwableAreas.Values) area.Visual?.Destroy();
        _throwableAreas.Clear(); _flashedActors.Clear(); _flashEndsAt = 0;
    }
}
