using System;
using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Put one binding on each camera-child weapon rig. Its graph owns bone animation.</summary>
public sealed class WeaponHandBinding : ScriptBehaviour
{
    [SerializedField] private string weaponId = "val";
    [SerializedField] private AnimationComponent? animator = null;
    [SerializedField] private GameObject? owner = null;
    [SerializedField] private ParticleSystemComponent? muzzleFlash = null;
    [SerializedField] private SoundEmitterComponent? shotAudio = null;
    [SerializedField] private Vector3 hipPosition = new(.07f, -.13f, .02f);
    [SerializedField] private Vector3 aimPosition = new(0, -.09f, 0);
    [SerializedField] private Vector3 sprintPosition = new(.10f, -.20f, .06f);
    [SerializedField] private float motionSharpness = 18;
    [SerializedField] private float sway = .0018f;
    [SerializedField] private float bobFrequency = 9;
    [SerializedField] private float bobAmount = .018f;
    [SerializedField] private string idleState = "Idle";
    [SerializedField] private string fireTrigger = "Fire";
    [SerializedField] private string drawTrigger = "";
    [SerializedField] private string reloadParameter = "Reload";
    [SerializedField] private string reloadCommitEvent = "ReloadFinish";
    // Optional graph parameters: blank means the graph does not implement that channel.
    [SerializedField] private string sprintParameter = "";
    [SerializedField] private string aimParameter = "";
    [SerializedField] private string speedParameter = "Speed";

    public string WeaponId => weaponId;
    public string ReloadCommitEvent => reloadCommitEvent;
    public GameObject Root => GameObject;
    public event Action<WeaponHandBinding, AnimationEvent>? AnimationEventRaised;
    private AnimationComponent? Animator => animator ??= GameObject.GetComponent<AnimationComponent>();
    public override void OnCreate() => owner?.GetComponent<PlayerController>()?.RegisterWeaponBinding(this);
    public void PlayShot(float pitch)
    {
        GameplaySounds.Play(shotAudio ??= GameObject.GetComponent<SoundEmitterComponent>(), GameplaySounds.Shot(weaponId), 1, .97f + Random.Shared.NextSingle() * .06f);
        muzzleFlash?.Emit(1);
    }

    public HandMotionProfile CreateMotionProfile() => new(hipPosition, aimPosition, sprintPosition,
        Bounded(motionSharpness, 18, .01f, 100), Bounded(sway, .0018f, 0, .02f),
        Bounded(bobFrequency, 9, 0, 30), Bounded(bobAmount, .018f, 0, .1f));

    private static float Bounded(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public void ResetAnimation()
    {
        SetReload(false);
        if (!string.IsNullOrEmpty(fireTrigger)) Animator?.ResetTrigger(fireTrigger);
        if (!string.IsNullOrEmpty(drawTrigger)) Animator?.ResetTrigger(drawTrigger);
        SetMovement(false, false, 0);
        if (!string.IsNullOrEmpty(idleState)) Animator?.PlayState(idleState);
    }

    public void Fire() { if (!string.IsNullOrEmpty(fireTrigger)) Animator?.SetTrigger(fireTrigger); }
    public void Draw() { if (!string.IsNullOrEmpty(drawTrigger)) Animator?.SetTrigger(drawTrigger); }
    public void SetReload(bool active) { if (!string.IsNullOrEmpty(reloadParameter)) Animator?.SetBool(reloadParameter, active); }
    public void SetMovement(bool aiming, bool sprinting, float speed)
    {
        if (!string.IsNullOrEmpty(aimParameter)) Animator?.SetBool(aimParameter, aiming);
        if (!string.IsNullOrEmpty(sprintParameter)) Animator?.SetBool(sprintParameter, sprinting);
        if (!string.IsNullOrEmpty(speedParameter)) Animator?.SetFloat(speedParameter, speed);
    }

    public override void OnAnimationEvent(AnimationEvent animationEvent) => AnimationEventRaised?.Invoke(this, animationEvent);
}
