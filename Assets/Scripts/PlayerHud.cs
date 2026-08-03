using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>
/// RmlUi replacement for FpsHudController. The document remains presentation;
/// this controller translates gameplay events into DOM state.
/// </summary>
public sealed class PlayerHud : ScriptBehaviour
{
    [SerializedField] private GameObject? player = null;
    [SerializedField] private string documentPath = "UI/hud.rml";
    [SerializedField] private float hitFlashDuration = 0.12f;

    private PlayerController? _controller;
    private PlayerHealth? _healthController;
    private RmlDocument? _document;
    private RmlElement? _ammo;
    private RmlElement? _reserve;
    private RmlElement? _interaction;
    private RmlElement? _health;
    private RmlElement? _healthValue;
    private RmlElement[] _armour = [];
    private RmlElement[] _arms = [];
    private UITween _spread = new(7.0f, 0.07f, UIEase.EaseOut);
    private float _hitTime;
    private bool _domReady;

    public override void OnCreate()
    {
        _controller = player?.GetComponent<PlayerController>();
        _healthController = player?.GetComponent<PlayerHealth>();
        if (_controller is null)
        {
            Debug.LogError("PlayerHud requires a PlayerController.");
            return;
        }

        _document = new RmlDocument(documentPath);
        _ammo = _document.Element("ammo");
        _reserve = _document.Element("reserve");
        _interaction = _document.Element("interaction");
        _health = _document.Element("health");
        _healthValue = _document.Element("health-value");
        _armour =
        [
            _document.Element("armour-0"),
            _document.Element("armour-1"),
            _document.Element("armour-2"),
        ];
        _arms =
        [
            _document.Element("crosshair-top"),
            _document.Element("crosshair-right"),
            _document.Element("crosshair-bottom"),
            _document.Element("crosshair-left"),
        ];

        _controller.AmmoChanged += OnAmmoChanged;
        _controller.MovementStateChanged += OnMovementChanged;
        _controller.HitConfirmed += OnHit;
        _controller.InteractionTargetChanged += OnInteractionChanged;
        if (_healthController is not null)
            _healthController.StatusChanged += OnHealthChanged;

        OnAmmoChanged(new FpsAmmoState(
            _controller.Ammo, _controller.ReserveAmmo,
            _controller.MagazineSize, _controller.IsReloading));
        OnMovementChanged(new FpsMovementState(
            _controller.IsGrounded, _controller.IsAiming,
            _controller.IsSprinting, _controller.IsCrouching,
            _controller.IsSliding));
        if (_healthController is not null)
            OnHealthChanged(_healthController.CurrentHealth, _healthController.MaximumHealth,
                _healthController.ArmourSlots, _healthController.MaximumArmourSlots);
    }

    public override void OnDestroy()
    {
        if (_controller is not null)
        {
            _controller.AmmoChanged -= OnAmmoChanged;
            _controller.MovementStateChanged -= OnMovementChanged;
            _controller.HitConfirmed -= OnHit;
            _controller.InteractionTargetChanged -= OnInteractionChanged;
        }
        if (_healthController is not null)
            _healthController.StatusChanged -= OnHealthChanged;
        _document?.Dispose();
        _document = null;
    }

    public override void OnUpdate(float deltaTime)
    {
        if (!_domReady && _document is not null && _controller is not null &&
            _document.Element("crosshair").SetClass("headshot", false))
        {
            _domReady = true;
            OnAmmoChanged(new FpsAmmoState(
                _controller.Ammo, _controller.ReserveAmmo,
                _controller.MagazineSize, _controller.IsReloading));
            OnMovementChanged(new FpsMovementState(
                _controller.IsGrounded, _controller.IsAiming,
                _controller.IsSprinting, _controller.IsCrouching,
                _controller.IsSliding));
            OnInteractionChanged(null);
        }
        if (_arms.Length != 4) return;
        var gap = _spread.Update(deltaTime);
        _arms[0].SetStyle("top", -gap);
        _arms[1].SetStyle("left", gap);
        _arms[2].SetStyle("top", gap);
        _arms[3].SetStyle("left", -gap);

        _hitTime = MathF.Max(0.0f, _hitTime - MathF.Max(deltaTime, 0.0f));
        foreach (var arm in _arms) arm.SetClass("hit", _hitTime > 0.0f);
    }

    private void OnAmmoChanged(FpsAmmoState state)
    {
        if (_ammo is not null) _ammo.Markup = state.IsReloading ? "RELOAD" : state.Magazine.ToString();
        if (_reserve is not null) _reserve.Markup = $" / {state.Reserve}";
    }

    private void OnMovementChanged(FpsMovementState state)
    {
        var gap = state.IsSprinting || state.IsSliding ? 18.0f
            : state.IsAiming ? 3.0f
            : state.IsCrouching ? 5.0f
            : state.IsGrounded ? 7.0f : 14.0f;
        _spread.SetTarget(gap);
        _document?.Element("crosshair").SetClass("hidden", state.IsSprinting);
    }

    private void OnHit(FpsHitEvent hit)
    {
        _hitTime = MathF.Max(0.01f, hitFlashDuration);
        _document?.Element("crosshair").SetClass("headshot", hit.IsHeadshot);
    }

    private void OnInteractionChanged(GameObject? target)
    {
        if (_interaction is not null)
            _interaction.Markup = target is null ? string.Empty : $"[E] {target.Name}";
    }

    private void OnHealthChanged(float current, float maximum, int armour, int maximumArmour)
    {
        if (_health is not null)
        {
            _health["max"] = MathF.Max(1.0f, maximum).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            _health["value"] = Math.Clamp(current, 0.0f, maximum).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }
        if (_healthValue is not null)
            _healthValue.Markup = MathF.Ceiling(current).ToString();
        for (var index = 0; index < _armour.Length; index++)
            _armour[index].SetClass("filled", index < armour && index < maximumArmour);
    }
}
