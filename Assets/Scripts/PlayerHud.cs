using System.Globalization;
using PlutoGE.ScriptCore;
using PlutoGE.ScriptCore.Gameplay;

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
    [SerializedField] private float damageOverlayMaximumOpacity = 0.85f;
    [SerializedField] private float deathFadeDelay = 0.45f;
    [SerializedField] private float deathFadeDuration = 0.75f;
    [SerializedField] private float minimapWorldSpan = 48.0f;

    private PlayerController? _controller;
    private PlayerHealth? _healthController;
    private MultiplayerSession? _multiplayer;
    private RmlDocument? _document;
    private TacticalMapPresenter? _map;
    private RmlElement? _ammo;
    private RmlElement? _reserve;
    private RmlElement? _weaponName;
    private string _promptSignature = "";
    private GameObject? _interactionTarget;
    private RmlElement? _weaponSlots;
    private RmlElement? _weaponState;
    private RmlElement? _compassTicks;
    private RmlElement? _compassHeading;
    private int _renderedHeading = -1;
    private RmlElement? _grenades;
    private string _renderedThrowableStatus = "";
    private RmlElement? _interaction;
    private RmlElement? _health;
    private RmlElement? _healthValue;
    private RmlElement? _damageOverlay;
    private RmlElement? _deathRed;
    private RmlElement? _deathBlack;
    private RmlElement? _crosshair;
    private RmlElement? _hitmarker;
    private RmlElement? _matchPhase;
    private RmlElement? _matchClock;
    private RmlElement? _alphaScore;
    private RmlElement? _bravoScore;
    private RmlElement? _scoreboard;
    private RmlElement? _scoreboardRows;
    private RmlElement? _killFeed;
    private RmlElement[] _armour = [];
    private RmlElement[] _arms = [];
    private string _spectatorPresentation = "";
    private bool? _renderedDead;
    private SpectatorViewState? _renderedSpectatorView;
    private float _hitTime;
    private float _deathTime;
    private float _renderedGap = float.NaN;
    private bool _hitVisible;
    private bool _domReady;
    private bool _dead;
    private bool _scoreboardVisible;
    private bool _scoreboardVisibilityInitialized;
    private readonly System.Collections.Generic.List<(string Text, float ExpiresAt)> _feed = [];
    private float _hudTime;
    private readonly DamageFeedbackState _damageFeedback = new();
    private float _healthFraction = 1;
    private float _renderedDamageOpacity = -1;

    private void OnDamageTaken() { _damageFeedback.Hit(); RenderDamageOverlay(); }
    private void RenderDamageOverlay()
    {
        if (_damageOverlay is null) return;
        var opacity = _dead ? 0 : _damageFeedback.Opacity(_healthFraction, damageOverlayMaximumOpacity);
        if (MathF.Abs(opacity - _renderedDamageOpacity) < .005f && (opacity > 0 || _renderedDamageOpacity == 0)) return;
        if (!_damageOverlay.SetClass("hidden", opacity <= .001f)) return;
        _damageOverlay.SetStyle("opacity", opacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _renderedDamageOpacity = opacity;
    }

    public override void OnCreate()
    {
        _controller = player?.GetComponent<PlayerController>();
        _healthController = player?.GetComponent<PlayerHealth>();
        _multiplayer = player?.GetComponent<MultiplayerSession>();
        if (_controller is null)
        {
            Debug.LogError("PlayerHud requires a PlayerController.");
            return;
        }

        _document = new RmlDocument(documentPath);
        _map = new TacticalMapPresenter(_document, SceneManager.GetActiveScene().Name, minimapWorldSpan);
        _ammo = _document.Element("ammo");
        _reserve = _document.Element("reserve");
        _weaponName = _document.Element("weapon-name");
        _weaponSlots = _document.Element("weapon-slots");
        _weaponState = _document.Element("weapon-state");
        _compassTicks = _document.Element("compass-ticks");
        _compassHeading = _document.Element("compass-heading");
        _grenades = _document.Element("grenades");
        _interaction = _document.Element("interaction");
        _health = _document.Element("health");
        _healthValue = _document.Element("health-value");
        _damageOverlay = _document.Element("damage-overlay");
        _deathRed = _document.Element("death-red");
        _deathBlack = _document.Element("death-black");
        _crosshair = _document.Element("crosshair");
        _hitmarker = _document.Element("hitmarker");
        _matchPhase = _document.Element("match-phase");
        _matchClock = _document.Element("match-clock");
        _alphaScore = _document.Element("alpha-score");
        _bravoScore = _document.Element("bravo-score");
        _scoreboard = _document.Element("scoreboard");
        _scoreboardRows = _document.Element("scoreboard-rows");
        _killFeed = _document.Element("kill-feed");
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
        _controller.WeaponChanged += RenderWeapon;
        RenderWeapon();
        _controller.MovementStateChanged += OnMovementChanged;
        _controller.HitConfirmed += OnHit;
        _controller.InteractionTargetChanged += OnInteractionChanged;
        if (_healthController is not null)
        {
            _healthController.StatusChanged += OnHealthChanged;
            _healthController.DamageTaken += OnDamageTaken;
            _healthController.Died += OnDied;
            _healthController.Respawned += OnRespawned;
        }
        if (_multiplayer is not null)
        {
            _multiplayer.MatchUpdated += OnMatchUpdated;
            _multiplayer.KillFeedReceived += OnKillFeed;
            if (_multiplayer.CurrentMatch is not null)
                OnMatchUpdated(_multiplayer.CurrentMatch);
        }

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
            _controller.WeaponChanged -= RenderWeapon;
            _controller.MovementStateChanged -= OnMovementChanged;
            _controller.HitConfirmed -= OnHit;
            _controller.InteractionTargetChanged -= OnInteractionChanged;
        }
        if (_healthController is not null)
        {
            _healthController.StatusChanged -= OnHealthChanged;
            _healthController.DamageTaken -= OnDamageTaken;
            _healthController.Died -= OnDied;
            _healthController.Respawned -= OnRespawned;
        }
        if (_multiplayer is not null)
        {
            _multiplayer.MatchUpdated -= OnMatchUpdated;
            _multiplayer.KillFeedReceived -= OnKillFeed;
        }
        _document?.Dispose();
        _document = null;
    }

    public override void OnUpdate(float deltaTime)
    {
        var promptSignature = GameInput.FormatPrompt("{Interact}|{WeaponPrimary}|{WeaponSecondary}|{NextWeapon}|{Reload}|{LoadoutAssault}|{LoadoutMedic}|{LoadoutDefender}");
        if (_domReady && promptSignature != _promptSignature)
        {
            _promptSignature = promptSignature;
            RenderWeapon();
            OnInteractionChanged(_interactionTarget);
        }

        _hudTime += MathF.Max(0.0f, deltaTime);
        if (_domReady && player is not null)
        {
            var heading = _controller?.SpectatorView is { } compassView
                ? ((int)MathF.Round(compassView.Heading) % 360 + 360) % 360 : TacticalUi.Heading(player.Forward);
            if (heading != _renderedHeading)
            {
                if (_compassHeading is not null) _compassHeading.Markup = heading.ToString("000");
                if (_compassTicks is not null) _compassTicks.Markup = TacticalUi.CompassTicks(heading);
                _renderedHeading = heading;
            }
        }
        if (_domReady && _document is not null)
        {
            var neutral = _multiplayer?.IsNeutralSpectator == true;
            var viewedTeam = _controller?.SpectatorView is { } targetView
                ? _multiplayer?.CurrentMatch?.Players.FirstOrDefault(p => p.PeerId == targetView.PeerId)?.Team ?? PlayerTeam.Spectator
                : neutral ? _multiplayer!.SpectatorTeam : PlayerTeam.Spectator;
            var teamLabel = $"<span class=\"{TeamIdentity.CssClass(viewedTeam)}\">{TeamIdentity.EmblemMarkup(viewedTeam)} {TeamIdentity.Name(viewedTeam)}</span>";
            var spectator = !_dead && !neutral ? "" : _controller?.IsSpectating == true
                ? $"{(neutral ? "NEUTRAL SPECTATOR / " : "")} {teamLabel} / {EscapeMarkup(_controller.SpectatorName)}<br/>{GameInput.Prompt("SpectatePrevious")} PREVIOUS / {GameInput.Prompt("SpectateNext")} NEXT{(neutral ? $" / {GameInput.Prompt("SpectateAlpha")} ALPHA / {GameInput.Prompt("SpectateBravo")} BRAVO" : " TEAMMATE")}" : neutral
                ? $"NEUTRAL SPECTATOR / {teamLabel}<br/>NO LIVE PLAYERS ON THIS SIDE / {GameInput.Prompt("SpectateAlpha")} ALPHA / {GameInput.Prompt("SpectateBravo")} BRAVO"
                : "NO TEAMMATES AVAILABLE<br/>WAITING TO RESPAWN";
            if (spectator != _spectatorPresentation)
            {
                _document.Element("spectator-status").Markup = spectator;
                _spectatorPresentation = spectator;
            }
            var hideVitals = (_dead || neutral) && _controller?.IsSpectating != true;
            if (_renderedDead != hideVitals)
            {
                _document.Element("weapon").SetClass("hidden", hideVitals);
                _document.Element("vitals").SetClass("hidden", hideVitals);
                _document.Element("interaction").SetClass("hidden", _dead || neutral);
                _renderedDead = hideVitals;
                if (hideVitals) _crosshair?.SetClass("hidden", true);
            }
        }
        UpdateTacticalMap();
        UpdateDeathEffect(deltaTime);
        _damageFeedback.Update(deltaTime);
        RenderDamageOverlay();
        var scoreboardVisible = GameInput.Down("Scoreboard");
        var pov = _controller?.SpectatorView;
        if (_domReady && pov is not null && !SameSpectatorHud(pov, _renderedSpectatorView))
        {
            var weapon = WeaponCatalog.At(pov.Slot);
            if (_weaponName is not null) _weaponName.Markup = weapon.Name;
            if (_weaponSlots is not null) _weaponSlots.Markup = "TEAMMATE LOADOUT";
            OnAmmoChanged(new(pov.Magazine, pov.Reserve, weapon.MagazineSize, pov.Reloading));
            if (_weaponState is not null) _weaponState.Markup = pov.Reloading ? "RELOADING" : weapon.Automatic ? "AUTO / READY" : "SEMI / READY";
            OnHealthChanged(pov.Health, pov.MaximumHealth, pov.Armour, _armour.Length);
            _crosshair?.SetClass("hidden", pov.Aiming || pov.Sprinting);
            _renderedSpectatorView = pov;
        }
        else if (pov is null) _renderedSpectatorView = null;
        var kind = pov?.SelectedThrowable ?? _multiplayer?.SelectedThrowable ?? "frag";
        var count = pov is null ? _multiplayer?.ThrowableCount(kind) ?? 0 : kind switch
        { "flash" => pov.Flashbang, "smoke" => pov.Smoke, "molotov" => pov.Molotov, _ => pov.Frag };
        var status = _multiplayer?.UsesPresetLoadouts == true
            ? pov is null ? $"{GameInput.Prompt("ThrowGrenade")} {GrenadeRules.Name(kind)} / {count} / {GameInput.Prompt("CycleThrowable")} CYCLE / {GameInput.Prompt("Inventory")} LOADOUTS" : $"{GrenadeRules.Name(kind)} / {count}"
            : pov is null ? $"{GameInput.Prompt("ThrowGrenade")} {GrenadeRules.Name(kind)} / {count} / {GameInput.Prompt("CycleThrowable")} CYCLE / ${_multiplayer?.Money ?? 0} / {GameInput.Prompt("Inventory")} BUY"
            : $"{GrenadeRules.Name(kind)} / {count} / ${pov.Money}";
        if (_domReady && _grenades is not null && status != _renderedThrowableStatus)
        { _grenades.Markup = status; _renderedThrowableStatus = status; }
        if (_domReady && _document is not null)
            _document.Element("flash-overlay").SetStyle("opacity", (pov?.Flash ?? _multiplayer?.FlashOpacity ?? 0).ToString(CultureInfo.InvariantCulture));
        if (!_scoreboardVisibilityInitialized || scoreboardVisible != _scoreboardVisible)
        {
            _scoreboardVisibilityInitialized = true;
            _scoreboardVisible = scoreboardVisible;
            _scoreboard?.SetClass("hidden", !scoreboardVisible);
        }
        if (_feed.Count > 0 && _feed[0].ExpiresAt <= _hudTime)
        {
            _feed.RemoveAt(0);
            RenderKillFeed();
        }
        if (!_domReady && _document is not null && _controller is not null &&
            _crosshair is not null && _crosshair.SetClass("headshot", false))
        {
            _domReady = true;
            RenderWeapon();
            OnAmmoChanged(new FpsAmmoState(
                _controller.Ammo, _controller.ReserveAmmo,
                _controller.MagazineSize, _controller.IsReloading));
            OnMovementChanged(new FpsMovementState(
                _controller.IsGrounded, _controller.IsAiming,
                _controller.IsSprinting, _controller.IsCrouching,
                _controller.IsSliding));
            OnInteractionChanged(null);
            if (_multiplayer?.CurrentMatch is not null)
                OnMatchUpdated(_multiplayer.CurrentMatch);
        }
        if (_arms.Length != 4 || !_domReady || _controller is null) return;
        // Place each arm's inner edge where the shot cone meets the screen. With a
        // vertical FOV, the cone edge sits tan(spread)/tan(fov/2) of the half-height
        // from centre; 50vh is that half-height. Margins in hud.rcss add arm length.
        var halfFov = Math.Clamp(_controller.CameraFov, 1.0f, 170.0f) * 0.5f * MathF.PI / 180.0f;
        var spread = Math.Clamp(pov?.Spread ?? _controller.SpreadDegrees, 0.0f, 45.0f) * MathF.PI / 180.0f;
        var gap = 50.0f * MathF.Tan(spread) / MathF.Tan(halfFov);
        if (float.IsNaN(_renderedGap) || MathF.Abs(gap - _renderedGap) >= 0.005f)
        {
            _renderedGap = gap;
            var outward = (-gap).ToString("0.###", CultureInfo.InvariantCulture) + "vh";
            var inward = gap.ToString("0.###", CultureInfo.InvariantCulture) + "vh";
            _arms[0].SetStyle("top", outward);
            _arms[1].SetStyle("left", inward);
            _arms[2].SetStyle("top", inward);
            _arms[3].SetStyle("left", outward);
        }

        if (_hitTime <= 0.0f) return;
        _hitTime = MathF.Max(0.0f, _hitTime - MathF.Max(deltaTime, 0.0f));
        _hitmarker?.SetStyle("opacity", Math.Clamp(_hitTime / .08f, 0, 1).ToString(CultureInfo.InvariantCulture));
        if (_hitTime <= 0.0f && _hitVisible)
        {
            _hitVisible = false;
            _hitmarker?.SetClass("hidden", true);
        }
    }

    private void UpdateTacticalMap()
    {
        if (!_domReady || _map is null || player is null) return;
        if (!GameInput.GameplayAllowed) _map.SetFullMapOpen(false);
        else if (GameInput.Pressed("ShowMap")) _map.SetFullMapOpen(!_map.FullMapOpen);
        // Observers centre the map on the teammate whose view they share.
        if (_controller?.SpectatorView is { } view)
            _map.Update(_multiplayer, view.Position, MapMath.Heading(view.Forward), true);
        else
            _map.Update(_multiplayer, player.WorldPosition, MapMath.Heading(player.Forward),
                !_dead && _multiplayer?.IsNeutralSpectator != true);
    }

    private static bool SameSpectatorHud(SpectatorViewState a, SpectatorViewState? b) => b is not null &&
        a.Slot == b.Slot && a.Magazine == b.Magazine && a.Reserve == b.Reserve && a.Reloading == b.Reloading &&
        a.Health == b.Health && a.MaximumHealth == b.MaximumHealth && a.Armour == b.Armour &&
        a.Aiming == b.Aiming && a.Sprinting == b.Sprinting;

    private void RenderWeapon()
    {
        if (_controller?.SpectatorView is { } view)
        {
            if (_weaponName is not null) _weaponName.Markup = WeaponCatalog.At(view.Slot).Name;
            if (_weaponSlots is not null) _weaponSlots.Markup = "TEAMMATE LOADOUT";
            return;
        }
        if (_weaponName is not null) _weaponName.Markup = _controller?.EquippedWeapon?.Name ?? "VAL";
        if (_weaponSlots is not null)
        {
            if (_multiplayer?.UsesPresetLoadouts != true && _multiplayer?.CurrentMatch?.Phase is (MatchPhase.Warmup or MatchPhase.Results))
            {
                _weaponSlots.Markup = GameInput.FormatPrompt("{LoadoutAssault} ASSAULT / {LoadoutMedic} MEDIC / {LoadoutDefender} DEFENDER") + "<br/>WEAPON SWITCHING DURING PLAY";
                return;
            }
            var selected = _controller?.EquippedWeaponSlot ?? 0;
            var primary = _multiplayer?.OwnsWeapon(0) == true ? "AR" : _multiplayer?.OwnsWeapon(2) == true ? "MG" : "EMPTY";
            // Direct weapon slots may have no controller binding; retain the weapon
            // label and show the mapped cycle action instead of a phantom slot key.
            static string SlotLabel(string action, string label)
            {
                var key = GameInput.Prompt(action);
                return key == "UNBOUND" ? label : $"{key} {label}";
            }
            var primaryLabel = SlotLabel("WeaponPrimary", primary);
            var pistolLabel = SlotLabel("WeaponSecondary", "PISTOL");
            _weaponSlots.Markup = $"{(selected != 1 ? $"[{primaryLabel}]" : primaryLabel)} &nbsp; {(selected == 1 ? $"[{pistolLabel}]" : pistolLabel)}<br/>{GameInput.Prompt("NextWeapon")} SWITCH / {GameInput.Prompt("Reload")} RELOAD";
        }
    }

    private void OnAmmoChanged(FpsAmmoState state)
    {
        if (_controller?.SpectatorView is { } view)
            state = new(view.Magazine, view.Reserve, WeaponCatalog.At(view.Slot).MagazineSize, view.Reloading);
        if (_ammo is not null)
        {
            _ammo.Markup = state.Magazine.ToString();
            _ammo.SetClass("low-ammo", state.Magazine <= Math.Max(1, state.MagazineSize / 5));
        }
        if (_weaponState is not null)
        {
            _weaponState.Markup = state.IsReloading ? "RELOADING" : (_controller?.SpectatorView is { } weaponView ? WeaponCatalog.At(weaponView.Slot).Automatic : _controller?.EquippedWeapon?.Automatic) == false ? "SEMI / READY" : "AUTO / READY";
            _weaponState.SetClass("reloading", state.IsReloading);
        }
        if (_reserve is not null) _reserve.Markup = $" / {state.Reserve}";
    }

    private void OnMovementChanged(FpsMovementState state)
    {
        var view = _controller?.SpectatorView;
        _crosshair?.SetClass("hidden", view is not null ? view.Aiming || view.Sprinting : state.IsAiming || state.IsSprinting || _dead);
    }

    private void OnHit(FpsHitEvent hit)
    {
        if (_dead || _multiplayer?.IsNeutralSpectator == true) return;
        _hitTime = hit.IsKill ? .32f : hit.IsHeadshot ? .24f : MathF.Max(.18f, hitFlashDuration);
        _hitVisible = true;
        _hitmarker?.SetClass("hidden", false);
        _hitmarker?.SetStyle("opacity", "1");
        _hitmarker?.SetClass("headshot", hit.IsHeadshot);
        _hitmarker?.SetClass("elimination", hit.IsKill);
    }

    private void OnInteractionChanged(GameObject? target)
    {
        _interactionTarget = target;
        if (_interaction is not null)
            _interaction.Markup = target is null ? string.Empty : $"<span class=\"ui-key\">{GameInput.Prompt("Interact")}</span>{EscapeMarkup(target.Name)}";
    }

    private void OnHealthChanged(float current, float maximum, int armour, int maximumArmour)
    {
        if (_controller?.SpectatorView is { } view)
        { current = view.Health; maximum = view.MaximumHealth; armour = view.Armour; maximumArmour = _armour.Length; }
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

        _healthFraction = current / MathF.Max(1, maximum);
        RenderDamageOverlay();
    }

    private void OnDied()
    {
        _dead = true;
        _hitTime = 0;
        _hitVisible = false;
        _hitmarker?.SetClass("hidden", true);
        _deathTime = 0.0f;
        _damageOverlay?.SetClass("hidden", true);
        _deathRed?.SetClass("hidden", false);
        _deathBlack?.SetClass("hidden", false);
        _deathRed?.SetStyle("opacity", "0.72");
        _deathBlack?.SetStyle("opacity", "0");
        _crosshair?.SetClass("hidden", true);
    }

    private void OnRespawned()
    {
        _dead = false;
        _damageFeedback.Reset();
        _renderedDamageOpacity = -1;
        _deathTime = 0.0f;
        _deathRed?.SetClass("hidden", true);
        _deathBlack?.SetClass("hidden", true);
        _crosshair?.SetClass("hidden", false);
        RenderWeapon();
        if (_controller is not null) OnAmmoChanged(new(_controller.Ammo, _controller.ReserveAmmo, _controller.MagazineSize, _controller.IsReloading));
        if (_healthController is not null)
            OnHealthChanged(_healthController.CurrentHealth, _healthController.MaximumHealth,
                _healthController.ArmourSlots, _healthController.MaximumArmourSlots);
    }

    private void UpdateDeathEffect(float deltaTime)
    {
        if (!_dead)
            return;

        _deathTime += MathF.Max(0.0f, deltaTime);
        var fadeDuration = MathF.Max(0.01f, deathFadeDuration);
        var black = Math.Clamp((_deathTime - MathF.Max(0.0f, deathFadeDelay)) / fadeDuration, 0.0f, 1.0f);
        if (_controller?.IsSpectating == true || _multiplayer?.RulesMode == MatchMode.Defusal) black = 0;
        var red = _controller?.IsSpectating == true ? 0 : _multiplayer?.RulesMode == MatchMode.Defusal ? .12f : 0.72f * (1.0f - black);
        _deathRed?.SetStyle("opacity", red.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _deathBlack?.SetStyle("opacity", black.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void OnMatchUpdated(MatchSnapshot match)
    {
        if (_controller?.IsSpectating != true) RenderWeapon();
        var seconds = Math.Max(0, (int)MathF.Ceiling(match.SecondsRemaining));
        if (_matchClock is not null) _matchClock.Markup = $"{seconds / 60:00}:{seconds % 60:00}";
        if (_alphaScore is not null) _alphaScore.Markup = match.AlphaScore.ToString();
        if (_bravoScore is not null) _bravoScore.Markup = match.BravoScore.ToString();
        if (_matchPhase is not null)
        {
            _matchPhase.Markup = match.Phase switch
            {
                MatchPhase.Warmup => match.Mode == MatchMode.Defusal ? "PREPARATION" : "MATCH STARTING",
                MatchPhase.Playing => match.Mode == MatchMode.Defusal ? $"ROUND {match.Defusal?.Round}" : DescribeMatch(match),
                MatchPhase.Results when match.Mode == MatchMode.Defusal => $"{match.Defusal?.Winner?.ToString().ToUpperInvariant()} WINS ROUND",
                MatchPhase.Results => match.AlphaScore == match.BravoScore ? "DRAW" :
                    match.AlphaScore > match.BravoScore ? "ALPHA WINS" : "BRAVO WINS",
                _ => "WAITING FOR PLAYERS"
            };
        }

        if (_scoreboardRows is null) return;
        var rows = new System.Text.StringBuilder();
        foreach (var playerState in match.Players)
        {
            var teamClass = TeamIdentity.CssClass(playerState.Team);
            var localClass = playerState.PeerId == match.LocalPeerId ? " local" : string.Empty;
            rows.Append($"<div class=\"score-row {teamClass}{localClass}\"><span class=\"player-name\">{TeamIdentity.EmblemMarkup(playerState.Team)} {TeamIdentity.Name(playerState.Team)} / {EscapeMarkup(playerState.Username)}</span><span class=\"stat\">{playerState.Kills}</span><span class=\"stat\">{playerState.Deaths}</span><span class=\"stat\">{(playerState.Team == PlayerTeam.Spectator ? "OBS" : match.Mode == MatchMode.Defusal ? playerState.Alive ? "IN" : "OUT" : ((int)playerState.ObjectiveSeconds).ToString())}</span></div>");
        }
        _scoreboardRows.Markup = rows.ToString();
    }

    private static string DescribeMatch(MatchSnapshot match)
    {
        var local = Array.Find(match.Players, player => player.PeerId == match.LocalPeerId);
        if (local is null || local.Team == PlayerTeam.Spectator) return $"NEUTRAL SPECTATOR | FIRST TO {match.ScoreLimit}";
        var own = local.Team == PlayerTeam.Alpha ? match.AlphaScore : match.BravoScore;
        var enemy = local.Team == PlayerTeam.Alpha ? match.BravoScore : match.AlphaScore;
        var status = own == enemy ? "TIED" : own > enemy ? "WINNING" : "LOSING";
        return $"{local.Team.ToString().ToUpperInvariant()} | {status} | FIRST TO {match.ScoreLimit}";
    }

    private void OnKillFeed(KillFeedEntry entry)
    {
        _feed.Add(($"<span class=\"{TeamIdentity.CssClass(entry.KillerTeam)}\">{TeamIdentity.Badge(entry.KillerTeam)}</span> {EscapeMarkup(entry.Killer)} eliminated {EscapeMarkup(entry.Victim)}", _hudTime + 5.0f));
        while (_feed.Count > 4) _feed.RemoveAt(0);
        RenderKillFeed();
    }

    private void RenderKillFeed()
    {
        if (_killFeed is null) return;
        var markup = new System.Text.StringBuilder();
        foreach (var entry in _feed)
            markup.Append($"<div class=\"feed-entry\">{entry.Text}</div>");
        _killFeed.Markup = markup.ToString();
    }

    private static string EscapeMarkup(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
