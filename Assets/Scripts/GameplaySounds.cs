using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

public static class GameplaySounds
{
    public const string Root = "project://Sounds/SFX Library/Sound Effects/";
    public static string RuntimeClip(string clip) => clip.Replace(Root, "project://Sounds/Gameplay/").Replace(".ogg", ".wav");
    public static string Shot(string? id) => Root + "Firearms/Fire/" + (id == "pistol" ? "pistol_fire_ufx_1.ogg" : id == "lmg" ? "machinegun_heavy_fire_ufx_1.ogg" : "assault_rifle_fire_ufx_1.ogg");
    public static string Empty(string? id) => Root + "Firearms/Misc/" + (id == "pistol" ? "pistol" : "rifle") + "_universal_empty_barrel_ufx_1.ogg";
    public static string? Reload(string? id, string name) => name switch
    {
        "ReloadMagOut" => Root + "Firearms/Reloading/" + (id == "pistol" ? "pistol" : "assault_rifle") + "_reloading_mag_out_ufx_1.ogg",
        "ReloadMagIn" => Root + "Firearms/Reloading/" + (id == "pistol" ? "pistol" : "assault_rifle") + "_reloading_mag_in_ufx_1.ogg",
        "ReloadRack" => Root + "Firearms/Racking/" + (id == "pistol" ? "pistol" : "assault_rifle") + "_racking_ufx_1.ogg",
        _ => null
    };
    public static void Play(SoundEmitterComponent? emitter, string clip, float volume = 1, float pitch = 1)
    {
        if (emitter is null) return;
        emitter.Clip = RuntimeClip(clip);
        emitter.PlayOneShot(PlayerSettings.EffectsGain * volume, pitch);
    }
}

/// <summary>Reusable scene-owned emitters for local cues and positional world effects.</summary>
public sealed class GameplayAudio : IDisposable
{
    private readonly GameObject?[] _sources = new GameObject?[17];
    private readonly SoundEmitterComponent?[] _emitters = new SoundEmitterComponent?[17];
    private int _nextWorld;
    private int _nextExplosion;
    private readonly Dictionary<string, (GameObject Source, SoundEmitterComponent Emitter)> _local = new();
    public GameplayAudio()
    {
        Prefab.Preload("project://Prefabs/Audio/GameplayCue.plutoprefab");
        for (var i = 0; i < _sources.Length; i++)
        {
            _sources[i] = Prefab.Instantiate("project://Prefabs/Audio/GameplayCue.plutoprefab", Vector3.Zero);
            _emitters[i] = _sources[i]?.GetComponent<SoundEmitterComponent>();
            if (_emitters[i] is { } emitter) emitter.Spatialized = i != 0;
        }
        Audio.PrewarmVoices(48);
        foreach (var clip in new[] { "Misc/inventory_open_ufx_1.wav", "Misc/inventory_close_ufx_1.wav", "Misc/new_objective_ufx_1.ogg" }) PrepareLocal(clip);
        foreach (var id in new[] { "ar", "pistol", "lmg" })
        {
            Audio.PreloadClip(GameplaySounds.RuntimeClip(GameplaySounds.Shot(id)));
            Audio.PreloadClip(GameplaySounds.RuntimeClip(GameplaySounds.Empty(id)));
            foreach (var name in new[] { "ReloadMagOut", "ReloadMagIn", "ReloadRack" })
                PrepareLocal(GameplaySounds.Reload(id, name)![GameplaySounds.Root.Length..]);
        }
        foreach (var clip in new[] { "Misc/item_pickup_ufx_1.ogg", "Misc/medkit_use_ufx_1.ogg", "Misc/item_equip_ufx_1.ogg", "Melee/Throw/throw_heavy_ufx_1.ogg", "Explosions/grenade_ufx_1.ogg", "Impact & Break/Concrete/impact_concrete_ufx_1.ogg" }) PrepareLocal(clip);
        for (var i = 1; i <= 4; i++) PrepareLocal($"Player/Footsteps/Concrete/footstep_concrete_ufx_{i}.ogg");
        for (var i = 1; i <= 2; i++) PrepareLocal($"Impact & Break/Body/impact_body_ufx_{i}.ogg");
    }
    private void PrepareLocal(string relative)
    {
        if (_local.ContainsKey(relative)) return;
        Audio.PreloadClip(GameplaySounds.RuntimeClip(GameplaySounds.Root + relative));
        var source = Prefab.Instantiate("project://Prefabs/Audio/GameplayCue.plutoprefab", Vector3.Zero);
        if (source?.GetComponent<SoundEmitterComponent>() is { } emitter) _local.Add(relative, (source, emitter));
    }
    public void Play(string relative, float volume = .65f)
    {
        PrepareLocal(relative);
        if (_local.TryGetValue(relative, out var cue)) GameplaySounds.Play(cue.Emitter, GameplaySounds.Root + relative, volume);
    }
    public void PlayAt(string relative, Vector3 position, float volume = .7f)
    {
        // Give explosions separate capacity so rapid bullet impacts cannot cut their tails.
        var explosion = relative.StartsWith("Explosions/", StringComparison.Ordinal);
        var slot = explosion ? 9 + _nextExplosion : 1 + _nextWorld;
        if (explosion) _nextExplosion = (_nextExplosion + 1) % 8;
        else _nextWorld = (_nextWorld + 1) % 8;
        _emitters[slot]?.Stop();
        if (_sources[slot] is { IsValid: true } source) source.WorldPosition = position;
        GameplaySounds.Play(_emitters[slot], GameplaySounds.Root + relative, volume);
    }
    public void Dispose()
    {
        foreach (var source in _sources) if (source?.IsValid == true) source.Destroy();
        foreach (var cue in _local.Values) if (cue.Source.IsValid) cue.Source.Destroy();
    }
}
