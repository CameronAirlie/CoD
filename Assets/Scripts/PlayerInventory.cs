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
    public bool IsUsing => _activeUse != UseAction.None;
    public int SlotsUsed => InventoryRules.Slots(ReserveAmmo, HealthKits, ArmourPlates);
    public float UseProgress => !IsUsing ? 0 : 1 - Math.Clamp(_useTimeRemaining /
        MathF.Max(.01f, _activeUse == UseAction.HealthKit ? healthKitUseTime : armourUseTime), 0, 1);
    public void CancelUse() { _activeUse = UseAction.None; _useTimeRemaining = 0; }
    public void ApplyLoadout(CombatLoadout loadout)
    {
        CancelUse();
        var supplies = InventoryRules.Loadout(loadout);
        ReserveAmmo = supplies.Ammo; HealthKits = supplies.Kits; ArmourPlates = supplies.Plates;
        Changed?.Invoke();
    }
    public override void OnDestroy()
    {
        if (_health is not null) { _health.DamageTaken -= CancelUse; _health.Died -= CancelUse; }
    }
    private UseAction _activeUse;
    private float _useTimeRemaining;

    public override void OnCreate()
    {
        ReserveAmmo = InventoryRules.AddAmmo(0, 0, 0, startingReserveAmmo);
        HealthKits = InventoryRules.AddItems(ReserveAmmo, 0, 0, startingHealthKits, true);
        ArmourPlates = InventoryRules.AddItems(ReserveAmmo, HealthKits, 0, startingArmourPlates, false);
        _health = GameObject.GetComponent<PlayerHealth>();
        if (_health is not null) { _health.DamageTaken += CancelUse; _health.Died += CancelUse; }
    }

    /// <summary>Advances an active use action. Called by the owning player controller.</summary>
    public void TickUse(float deltaTime)
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
            // AddArmourSlot already equips protection; do not remove it again.
        }
        _activeUse = UseAction.None;
        Changed?.Invoke();
    }

    public void AddReserveAmmo(int amount)
    {
        if (amount <= 0)
            return;
        var accepted = InventoryRules.AddAmmo(ReserveAmmo, HealthKits, ArmourPlates, amount);
        if (accepted == 0) return;
        ReserveAmmo += accepted;
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
        var accepted = InventoryRules.AddItems(ReserveAmmo, HealthKits, ArmourPlates, amount, true);
        if (accepted == 0) return;
        HealthKits += accepted;
        Changed?.Invoke();
    }

    public void AddArmourPlate(int amount = 1)
    {
        if (amount <= 0)
            return;
        var accepted = InventoryRules.AddItems(ReserveAmmo, HealthKits, ArmourPlates, amount, false);
        if (accepted == 0) return;
        ArmourPlates += accepted;
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
