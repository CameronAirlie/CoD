using System;
using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>An interactable supply that grants health and/or reserve ammunition.</summary>
public sealed class SupplyPickup : ScriptBehaviour
{
    [SerializedField] private int ammoAmount = 0;
    [SerializedField] private int healthKitAmount = 0;
    [SerializedField] private float healthAmount = 0.0f;
    [SerializedField] private int armourPlateAmount = 0;
    [SerializedField] private string ammoMethod = "AddAmmo";
    [SerializedField] private string healthMethod = "Heal";
    [SerializedField] private string armourMethod = "AddArmourPlate";
    [SerializedField] private string respawnPrefab = "";
    [SerializedField] private float rotationSpeed = 45.0f;

    private bool _consumed;
    internal static readonly HashSet<SupplyPickup> Live = new();
    public override void OnCreate() => Live.Add(this);
    public override void OnDestroy() => Live.Remove(this);
    internal Vector3 SupplyPosition => GameObject.WorldPosition;
    internal void SetNetworkVisible(bool visible) => GameObject.Active = visible;
    internal bool CollectOnServer(ServerEquipment equipment, float health, float maximum, out float healed)
    {
        healed = 0;
        if (_consumed) return false;
        var granted = equipment.AddSupply(ammoAmount, healthKitAmount, armourPlateAmount);
        healed = MathF.Min(MathF.Max(0, healthAmount), maximum - health);
        if (!granted && healed <= 0) return false;
        Consume(); return true;
    }
    private void Consume()
    {
        _consumed = true;
        var spawner = GameObject.Find("SupplySpawner");
        if (spawner is not null && !string.IsNullOrWhiteSpace(respawnPrefab))
            spawner.TryInvoke("PickupConsumed", respawnPrefab, GameObject.WorldPosition, GameObject.WorldRotation);
        GameObject.Destroy();
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_consumed || rotationSpeed == 0.0f)
            return;

        var rotation = Rotation;
        rotation.Y = (rotation.Y + rotationSpeed * MathF.Max(0.0f, deltaTime)) % 360.0f;
        Rotation = rotation;
    }

    public void Interact(GameObject interactor)
    {
        if (_consumed || interactor is null || !interactor.IsValid)
            return;

        if (interactor.GetComponent<MultiplayerSession>() is { UsesServerAuthority: true } session)
        { session.RequestSupply(SupplyPosition); return; }

        var inventory = interactor.GetComponent<PlayerInventory>();
        var health = interactor.GetComponent<PlayerHealth>();
        var oldAmmo = inventory?.ReserveAmmo ?? 0;
        var oldKits = inventory?.HealthKits ?? 0;
        var oldPlates = inventory?.ArmourPlates ?? 0;
        var oldHealth = health?.CurrentHealth ?? 0;
        var granted = false;
        if (healthKitAmount > 0 && inventory is not null) inventory.AddHealthKit(healthKitAmount);
        if (ammoAmount > 0 && !string.IsNullOrWhiteSpace(ammoMethod))
            granted |= interactor.TryInvoke(ammoMethod, ammoAmount);
        if (healthAmount > 0.0f && !string.IsNullOrWhiteSpace(healthMethod))
            granted |= interactor.TryInvoke(healthMethod, healthAmount);
        if (armourPlateAmount > 0 && !string.IsNullOrWhiteSpace(armourMethod))
            granted |= interactor.TryInvoke(armourMethod, armourPlateAmount);

        if (inventory is not null || health is not null)
            granted = oldAmmo != (inventory?.ReserveAmmo ?? 0) || oldKits != (inventory?.HealthKits ?? 0) ||
                oldPlates != (inventory?.ArmourPlates ?? 0) || oldHealth != (health?.CurrentHealth ?? 0);
        if (!granted)
        {
            Debug.Log("Supplies full; use or spend supplies before collecting more.");
            return;
        }

        interactor.GetComponent<PlayerController>()?.PlayGameplayCue("Misc/item_pickup_ufx_1.ogg");
        Consume();
    }
}
