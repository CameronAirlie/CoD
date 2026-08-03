using System;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Authoritative player-owned quantities consumed by gameplay systems.</summary>
public sealed class PlayerInventory : ScriptBehaviour
{
    [SerializedField] private int startingReserveAmmo = 90;
    [SerializedField] private int startingHealthKits = 2;
    [SerializedField] private int startingArmourPlates = 1;
    [SerializedField] private float healthKitHealingPercent = 0.30f;
    [SerializedField] private float healthKitUseTime = 1.0f;
    [SerializedField] private float armourUseTime = 0.5f;

    private PlayerHealth? _health;

    public int ReserveAmmo { get; private set; }
    public int HealthKits { get; private set; }
    public int ArmourPlates { get; private set; }
    public event Action? Changed;
    private UseAction _activeUse;
    private float _useTimeRemaining;

    public override void OnCreate()
    {
        ReserveAmmo = Math.Max(0, startingReserveAmmo);
        HealthKits = Math.Max(0, startingHealthKits);
        ArmourPlates = Math.Max(0, startingArmourPlates);
        _health = GameObject.GetComponent<PlayerHealth>();
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_activeUse == UseAction.None || deltaTime <= 0.0f)
            return;
        _useTimeRemaining -= deltaTime;
        if (_useTimeRemaining > 0.0f)
            return;

        if (_activeUse == UseAction.HealthKit && HealthKits > 0 &&
            _health is not null && !_health.IsDead && !_health.IsFullHealth)
        {
            HealthKits--;
            _health.Heal(_health.MaximumHealth * Math.Clamp(healthKitHealingPercent, 0.0f, 1.0f));
        }
        else if (_activeUse == UseAction.ArmourPlate && ArmourPlates > 0 &&
            _health?.AddArmourSlot() == true)
        {
            ArmourPlates--;
            Debug.Log($"Armour equipped: {_health.ArmourSlots}/{_health.MaximumArmourSlots} slots.");
        }
        _activeUse = UseAction.None;
        Changed?.Invoke();
    }

    public void AddReserveAmmo(int amount)
    {
        if (amount <= 0)
            return;
        ReserveAmmo = Math.Min(int.MaxValue - amount, ReserveAmmo) + amount;
        Changed?.Invoke();
    }

    public int TakeAmmo(int requested)
    {
        var amount = Math.Min(Math.Max(0, requested), ReserveAmmo);
        if (amount == 0)
            return 0;
        ReserveAmmo -= amount;
        Changed?.Invoke();
        return amount;
    }

    public void AddHealthKit(int amount = 1)
    {
        if (amount <= 0)
            return;
        HealthKits = Math.Min(int.MaxValue - amount, HealthKits) + amount;
        Changed?.Invoke();
    }

    public void AddArmourPlate(int amount = 1)
    {
        if (amount <= 0)
            return;
        ArmourPlates = Math.Min(int.MaxValue - amount, ArmourPlates) + amount;
        Changed?.Invoke();
    }

    public bool BeginUseHealthKit()
    {
        _health ??= GameObject.GetComponent<PlayerHealth>();
        if (_activeUse != UseAction.None || HealthKits <= 0 ||
            _health is null || _health.IsDead || _health.IsFullHealth)
            return false;
        _activeUse = UseAction.HealthKit;
        _useTimeRemaining = MathF.Max(0.0f, healthKitUseTime);
        return true;
    }

    public bool BeginUseArmourPlate()
    {
        _health ??= GameObject.GetComponent<PlayerHealth>();
        if (_activeUse != UseAction.None || ArmourPlates <= 0 || _health is null ||
            _health.IsDead || _health.ArmourSlots >= _health.MaximumArmourSlots)
            return false;
        _activeUse = UseAction.ArmourPlate;
        _useTimeRemaining = MathF.Max(0.0f, armourUseTime);
        Debug.Log("Applying armour plate...");
        return true;
    }

    private enum UseAction { None, HealthKit, ArmourPlate }
}
