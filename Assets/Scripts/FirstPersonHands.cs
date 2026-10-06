using System;
using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Owns exactly one visible hand/weapon rig. Does not own ammunition or weapon damage.</summary>
public sealed class FirstPersonHands : IDisposable
{
    private readonly HandMotion _motion = new();
    private WeaponHandBinding? _binding;
    private HandMotionProfile? _profile;
    private Vector3 _restRotation;
    private bool _visible = true;
    public WeaponHandBinding? Equipped => _binding;
    public event Action<AnimationEvent>? AnimationEventRaised;

    public void Equip(WeaponHandBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (_binding == binding) return;
        Unequip();
        _binding = binding;
        _profile = binding.CreateMotionProfile();
        _restRotation = binding.Root.Rotation;
        _motion.Reset(_profile);
        binding.Root.Position = _profile.Hip;
        binding.ResetAnimation();
        binding.Root.Active = _visible;
        if (_visible) binding.Draw();
        binding.AnimationEventRaised += OnAnimationEvent;
    }

    private void OnAnimationEvent(WeaponHandBinding source, AnimationEvent animationEvent)
    {
        if (source == _binding && _visible) AnimationEventRaised?.Invoke(animationEvent);
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_binding is null) return;
        _binding.Root.Active = visible;
        _binding.ResetAnimation();
        if (_profile is not null) _motion.Reset(_profile);
    }

    public void Tick(float deltaTime, Vector2 look, bool moving, bool aiming, bool sprinting, float speed)
    {
        if (!_visible || _binding is null || _profile is null) return;
        var pose = _motion.Tick(_profile, deltaTime, look, moving, aiming, sprinting);
        _binding.Root.Position = pose.Position;
        _binding.Root.Rotation = _restRotation + pose.RotationOffset;
        _binding.SetMovement(aiming, sprinting, speed);
    }

    public void Fire() => _binding?.Fire();
    public void SetReload(bool active) => _binding?.SetReload(active);
    private void Unequip()
    {
        if (_binding is null) return;
        _binding.AnimationEventRaised -= OnAnimationEvent;
        _binding.ResetAnimation();
        _binding.Root.Rotation = _restRotation;
        _binding.Root.Active = false;
        _binding = null;
        _profile = null;
    }
    public void Dispose() { Unequip(); AnimationEventRaised = null; }
}
