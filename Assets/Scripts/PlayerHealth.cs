using System;
using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Receives damage from enemy bots and optionally drives a health label.</summary>
public sealed class PlayerHealth : ScriptBehaviour
{
    [SerializedField] private float maximumHealth = 100.0f;
    [SerializedField] private int maximumArmourSlots = 3;
    [SerializedField] private float armourPerSlot = 25.0f;
    [SerializedField] private GameObject? healthText = null;
    [SerializedField] private GameObject? damageOverlay = null;
    [SerializedField] private float damageFlashAlpha = 0.32f;
    [SerializedField] private float damageFlashFadeSpeed = 2.4f;
    [SerializedField] private float regenerationDelay = 5.0f;
    [SerializedField] private float regenerationPerSecond = 6.0f;
    [SerializedField] private bool respawnOnDeath = true;
    [SerializedField] private float respawnDelay = 1.5f;
    [SerializedField] private float respawnInvulnerability = 1.0f;

    private UITextComponent? _label;
    private UIImageComponent? _damageImage;
    private float _health;
    private int _armourSlots;
    private float _time;
    private float _lastDamageAt;
    private float _restartAt;
    private float _invulnerableUntil;
    private int _displayedHealth = -1;
    private bool _dead;
    private Vector3 _spawnPosition;
    private Vector3 _spawnRotation;
    private RigidbodyComponent? _body;

    public bool IsDead => _dead;
    public bool IsFullHealth => _health >= maximumHealth;
    public float CurrentHealth => _health;
    public float MaximumHealth => maximumHealth;
    public int ArmourSlots => _armourSlots;
    public int MaximumArmourSlots => maximumArmourSlots;
    public event Action<float, float, int, int>? StatusChanged;

    public void ConfigureMultiplayerHealthRules(
        float maximum, float regenerationDelaySeconds, float regenerationRate)
    {
        maximumHealth = MathF.Max(1.0f, maximum);
        regenerationDelay = MathF.Max(0.0f, regenerationDelaySeconds);
        regenerationPerSecond = MathF.Max(0.0f, regenerationRate);
        _health = maximumHealth;
        _dead = false;
        _restartAt = 0.0f;
        _lastDamageAt = _time;
        RefreshLabel();
        PublishStatus();
    }

    public override void OnCreate()
    {
        _health = MathF.Max(1.0f, maximumHealth);
        _lastDamageAt = _time;
        _spawnPosition = GameObject.WorldPosition;
        _spawnRotation = GameObject.WorldRotation;
        _body = GameObject.GetComponent<RigidbodyComponent>();
        _label = healthText?.GetComponent<UITextComponent>();
        _damageImage = damageOverlay?.GetComponent<UIImageComponent>();
        if (_damageImage is not null)
        {
            _damageImage.Color = new Vector3(0.8f, 0.0f, 0.0f);
            _damageImage.Alpha = 0.0f;
        }
        RefreshLabel();
    }

    public override void OnUpdate(float deltaTime)
    {
        var safeDeltaTime = MathF.Max(0.0f, deltaTime);
        _time += safeDeltaTime;
        if (_damageImage is not null && _damageImage.Alpha > 0.0f)
            _damageImage.Alpha = MathF.Max(
                0.0f,
                _damageImage.Alpha - MathF.Max(0.0f, damageFlashFadeSpeed) * safeDeltaTime);

        if (_dead && respawnOnDeath && _time >= _restartAt)
        {
            Respawn();
            return;
        }

        if (!_dead &&
            _health < maximumHealth &&
            regenerationPerSecond > 0.0f &&
            _time >= _lastDamageAt + MathF.Max(0.0f, regenerationDelay))
        {
            _health = MathF.Min(
                MathF.Max(1.0f, maximumHealth),
                _health + regenerationPerSecond * safeDeltaTime);
            RefreshLabel();
            PublishStatus();
        }
    }

    public void TakeDamage(float amount)
    {
        if (_dead || _time < _invulnerableUntil || amount <= 0.0f)
            return;

        _lastDamageAt = _time;
        var remainingDamage = amount;
        var protectionPerSlot = MathF.Max(0.0f, armourPerSlot);
        while (ArmourSlots > 0 && remainingDamage > 0.0f)
        {
            _armourSlots--;
            remainingDamage = MathF.Max(0.0f, remainingDamage - protectionPerSlot);
        }
        _health = MathF.Max(0.0f, _health - remainingDamage);
        if (_damageImage is not null)
            _damageImage.Alpha = MathF.Max(
                _damageImage.Alpha,
                Math.Clamp(damageFlashAlpha, 0.0f, 1.0f));
        RefreshLabel();
        PublishStatus();
        if (_health <= 0.0f)
        {
            _dead = true;
            _restartAt = _time + MathF.Max(0.0f, respawnDelay);
            Debug.Log("Player killed. Respawning...");
        }
    }

    public void Heal(float amount)
    {
        if (_dead || amount <= 0.0f)
            return;
        _health = MathF.Min(maximumHealth, _health + amount);
        RefreshLabel();
        PublishStatus();
    }

    public void EquipArmourSlot()
    {
        if (_dead || ArmourSlots <= 0)
            return;
        _armourSlots--;
        PublishStatus();
    }

    public bool AddArmourSlot()
    {
        var capacity = Math.Max(0, maximumArmourSlots);
        if (_dead || ArmourSlots >= capacity)
            return false;
        _armourSlots++;
        PublishStatus();
        return true;
    }

    private void Respawn()
    {
        GameObject.WorldPosition = _spawnPosition;
        GameObject.WorldRotation = _spawnRotation;
        if (_body is not null)
        {
            _body.Velocity = Vector3.Zero;
            _body.AngularVelocity = Vector3.Zero;
        }

        _health = MathF.Max(1.0f, maximumHealth);
        _armourSlots = 0;
        _dead = false;
        _lastDamageAt = _time;
        _invulnerableUntil = _time + MathF.Max(0.0f, respawnInvulnerability);
        if (_damageImage is not null)
            _damageImage.Alpha = 0.0f;
        RefreshLabel();
        PublishStatus();
        Debug.Log("Player respawned.");
    }

    private void RefreshLabel()
    {
        if (_label is null)
            return;

        var displayedHealth = (int)MathF.Ceiling(_health);
        if (displayedHealth == _displayedHealth)
            return;

        _displayedHealth = displayedHealth;
        _label.Text = $"HP {displayedHealth}";
    }

    private void PublishStatus() => StatusChanged?.Invoke(
        _health, maximumHealth, ArmourSlots, Math.Max(0, maximumArmourSlots));
}
