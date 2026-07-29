using System;
using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Receives damage from enemy bots and optionally drives a health label.</summary>
public sealed class PlayerHealth : ScriptBehaviour
{
    [SerializedField] private float maximumHealth = 100.0f;
    [SerializedField] private GameObject? healthText = null;
    [SerializedField] private GameObject? damageOverlay = null;
    [SerializedField] private float damageFlashAlpha = 0.32f;
    [SerializedField] private float damageFlashFadeSpeed = 2.4f;
    [SerializedField] private float regenerationDelay = 5.0f;
    [SerializedField] private float regenerationPerSecond = 6.0f;
    [SerializedField] private bool restartSceneOnDeath = true;
    [SerializedField] private float restartDelay = 1.5f;
    [SerializedField] private string sceneToRestart = "Main";

    private UITextComponent? _label;
    private UIImageComponent? _damageImage;
    private float _health;
    private float _time;
    private float _lastDamageAt;
    private float _restartAt;
    private int _displayedHealth = -1;
    private bool _dead;

    public override void OnCreate()
    {
        _health = MathF.Max(1.0f, maximumHealth);
        _lastDamageAt = _time;
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

        if (_dead && restartSceneOnDeath && _time >= _restartAt)
            SceneManager.LoadScene(sceneToRestart);

        if (!_dead &&
            _health < maximumHealth &&
            regenerationPerSecond > 0.0f &&
            _time >= _lastDamageAt + MathF.Max(0.0f, regenerationDelay))
        {
            _health = MathF.Min(
                MathF.Max(1.0f, maximumHealth),
                _health + regenerationPerSecond * safeDeltaTime);
            RefreshLabel();
        }
    }

    public void TakeDamage(float amount)
    {
        if (_dead || amount <= 0.0f)
            return;

        _lastDamageAt = _time;
        _health = MathF.Max(0.0f, _health - amount);
        if (_damageImage is not null)
            _damageImage.Alpha = MathF.Max(
                _damageImage.Alpha,
                Math.Clamp(damageFlashAlpha, 0.0f, 1.0f));
        RefreshLabel();
        if (_health <= 0.0f)
        {
            _dead = true;
            _restartAt = _time + MathF.Max(0.0f, restartDelay);
            Debug.Log("Player killed by enemy bot.");
        }
    }

    public void Heal(float amount)
    {
        if (_dead || amount <= 0.0f)
            return;
        _health = MathF.Min(maximumHealth, _health + amount);
        RefreshLabel();
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
}
