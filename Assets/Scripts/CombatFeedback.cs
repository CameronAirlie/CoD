using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Local event subscriber. Owns presentation cues, never damage or scoring.</summary>
public sealed class CombatFeedback : ScriptBehaviour
{
    [SerializedField] private SoundEmitterComponent? cueAudio = null;
    [SerializedField] private float cameraImpulseScale = 1;
    [SerializedField] private float soundVolume = .55f;
    private PlayerController? _controller;
    private PlayerHealth? _health;
    private MultiplayerSession? _session;
    private MatchSnapshot? _previousMatch;
    private float _cueCooldown;
    private string _lastCue = "";
    public event Action<string>? Cue;
    public float DamageBearing { get; private set; }
    public float DamageIndicatorTime { get; private set; }
    public string LastCue { get; private set; } = "";
    public float CueTime { get; private set; }

    public override void OnCreate()
    {
        _controller = GameObject.GetComponent<PlayerController>();
        _health = GameObject.GetComponent<PlayerHealth>();
        _session = GameObject.GetComponent<MultiplayerSession>();
        if (_controller is not null) _controller.HitConfirmed += OnHit;
        if (_health is not null) _health.DamageReceived += OnDamage;
        if (_session is not null) _session.MatchUpdated += OnMatch;
    }
    public override void OnUpdate(float deltaTime)
    {
        var dt = float.IsFinite(deltaTime) ? MathF.Max(0, deltaTime) : 0;
        _cueCooldown = MathF.Max(0, _cueCooldown - dt);
        DamageIndicatorTime = MathF.Max(0, DamageIndicatorTime - dt);
        CueTime = MathF.Max(0, CueTime - dt);
    }
    public override void OnDestroy()
    {
        if (_controller is not null) _controller.HitConfirmed -= OnHit;
        if (_health is not null) _health.DamageReceived -= OnDamage;
        if (_session is not null) _session.MatchUpdated -= OnMatch;
    }
    private void OnHit(FpsHitEvent hit)
    {
        if (hit.IsKill) Notify("ELIMINATED", "project://Sounds/feedback/kill.wav");
        else if (hit.HitArmour) Notify("ARMOUR HIT", "project://Sounds/feedback/armour.wav");
        else if (hit.IsHeadshot) Notify("HEADSHOT", "project://Sounds/feedback/headshot.wav");
    }
    private void OnDamage(PlayerDamageEvent damage)
    {
        var direction = damage.Source - GameObject.WorldPosition;
        if (damage.HasSource)
        {
            DamageBearing = CombatFeedbackMath.Bearing(GameObject.Forward, GameObject.Right, direction);
            DamageIndicatorTime = .8f;
        }
        _controller?.AddCameraImpulse(MathF.Min(1.5f, damage.Amount / 25) * cameraImpulseScale);
        if (damage.ArmourAbsorbed > 0) Notify("ARMOUR ABSORBED HIT", "project://Sounds/feedback/armour.wav");
    }
    private void OnMatch(MatchSnapshot match)
    {
        if (_previousMatch is { } previous)
        {
            if (match.Phase != previous.Phase)
                Notify(match.Phase == MatchPhase.Playing ? (match.Mode == MatchMode.Defusal ? "PLANT OR DEFUSE THE BOMB" : match.Mode == MatchMode.Hardpoint ? "CAPTURE THE HARDPOINT" : "ELIMINATE THE ENEMY TEAM") :
                    match.Phase == MatchPhase.Results ? (match.Mode == MatchMode.Defusal && match.Defusal?.MatchOver != true ? "ROUND OVER" : "MATCH COMPLETE") : "CHOOSE LOADOUT: 1 / 2 / 3", "project://Sounds/feedback/objective.wav");
            else if (match.Phase == MatchPhase.Playing && match.Defusal is { } bomb && bomb.Bomb != previous.Defusal?.Bomb)
                Notify($"BOMB {bomb.Bomb.ToString().ToUpperInvariant()}", "project://Sounds/feedback/objective.wav");
            else if (match.Phase == MatchPhase.Playing && match.Objective is { } objective)
            {
                if (objective.Index != previous.Objective?.Index) Notify("HARDPOINT MOVED", "project://Sounds/feedback/objective.wav");
                else if (objective.Contested && previous.Objective?.Contested != true) Notify("HARDPOINT CONTESTED", "project://Sounds/feedback/objective.wav");
                else if (objective.Owner is not null && objective.Owner != previous.Objective?.Owner)
                    Notify($"{objective.Owner.ToString()!.ToUpperInvariant()} CAPTURING", "project://Sounds/feedback/objective.wav");
            }
        }
        _previousMatch = match;
    }
    private void Notify(string text, string clip)
    {
        LastCue = text; CueTime = 1.5f; Cue?.Invoke(text);
        if (cueAudio is null || (_cueCooldown > 0 && text == _lastCue)) return;
        if (clip == "project://Sounds/feedback/objective.wav")
            clip = GameplaySounds.RuntimeClip(GameplaySounds.Root + "Misc/new_objective_ufx_1.ogg");
        cueAudio.Clip = clip; cueAudio.PlayOneShot(Math.Clamp(soundVolume, 0, 1) * PlayerSettings.FeedbackGain, 1);
        _lastCue = text; _cueCooldown = .25f;
    }
}
