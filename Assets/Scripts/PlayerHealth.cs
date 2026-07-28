using System;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Receives damage from enemy bots and optionally drives a health label.</summary>
public sealed class PlayerHealth : ScriptBehaviour
{
    [SerializedField] private float maximumHealth = 100.0f;
    [SerializedField] private GameObject? healthText = null;
    [SerializedField] private bool restartSceneOnDeath = true;
    [SerializedField] private float restartDelay = 1.5f;
    [SerializedField] private string sceneToRestart = "Main";

    private UITextComponent? _label;
    private float _health;
    private float _time;
    private float _restartAt;
    private bool _dead;

    public override void OnCreate()
    {
        _health = MathF.Max(1.0f, maximumHealth);
        _label = healthText?.GetComponent<UITextComponent>();
        RefreshLabel();
    }

    public override void OnUpdate(float deltaTime)
    {
        _time += MathF.Max(0.0f, deltaTime);
        if (_dead && restartSceneOnDeath && _time >= _restartAt)
            SceneManager.LoadScene(sceneToRestart);
    }

    public void TakeDamage(float amount)
    {
        if (_dead || amount <= 0.0f)
            return;

        _health = MathF.Max(0.0f, _health - amount);
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
        if (_label is not null)
            _label.Text = $"HP {MathF.Ceiling(_health):0}";
    }
}
