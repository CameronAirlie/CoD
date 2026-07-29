using System;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>An interactable supply that grants health and/or reserve ammunition.</summary>
public sealed class SupplyPickup : ScriptBehaviour
{
    [SerializedField] private int ammoAmount = 0;
    [SerializedField] private float healthAmount = 0.0f;
    [SerializedField] private string ammoMethod = "AddAmmo";
    [SerializedField] private string healthMethod = "Heal";
    [SerializedField] private float rotationSpeed = 45.0f;

    private bool _consumed;

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

        var granted = false;
        if (ammoAmount > 0 && !string.IsNullOrWhiteSpace(ammoMethod))
            granted |= interactor.TryInvoke(ammoMethod, ammoAmount);
        if (healthAmount > 0.0f && !string.IsNullOrWhiteSpace(healthMethod))
            granted |= interactor.TryInvoke(healthMethod, healthAmount);

        if (!granted)
        {
            Debug.LogWarning($"{GameObject.Name} could not find a compatible supply receiver.");
            return;
        }

        _consumed = true;
        GameObject.Destroy();
    }
}
