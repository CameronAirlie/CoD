using System.Globalization;
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
    [SerializedField] private float damageOverlayMaximumOpacity = 0.85f;
    [SerializedField] private float deathFadeDelay = 0.45f;
    [SerializedField] private float deathFadeDuration = 0.75f;

    private PlayerController? _controller;
    private PlayerHealth? _healthController;
    private MultiplayerSession? _multiplayer;
    private RmlDocument? _document;
    private RmlElement? _ammo;
    private RmlElement? _reserve;
    private RmlElement? _interaction;
    private RmlElement? _health;
    private RmlElement? _healthValue;
    private RmlElement? _damageOverlay;
    private RmlElement? _deathRed;
    private RmlElement? _deathBlack;
    private RmlElement? _crosshair;
    private RmlElement? _matchPhase;
    private RmlElement? _matchClock;
    private RmlElement? _alphaScore;
    private RmlElement? _bravoScore;
    private RmlElement? _scoreboard;
    private RmlElement? _scoreboardRows;
    private RmlElement? _killFeed;
    private RmlElement[] _armour = [];
    private RmlElement[] _arms = [];
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
        _ammo = _document.Element("ammo");
        _reserve = _document.Element("reserve");
        _interaction = _document.Element("interaction");
        _health = _document.Element("health");
        _healthValue = _document.Element("health-value");
        _damageOverlay = _document.Element("damage-overlay");
        _deathRed = _document.Element("death-red");
        _deathBlack = _document.Element("death-black");
        _crosshair = _document.Element("crosshair");
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
        _hudTime += MathF.Max(0.0f, deltaTime);
        UpdateDeathEffect(deltaTime);
        _damageFeedback.Update(deltaTime);
        RenderDamageOverlay();
        var scoreboardVisible = Input.IsKeyDown(KeyCode.Tab);
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
        var spread = Math.Clamp(_controller.SpreadDegrees, 0.0f, 45.0f) * MathF.PI / 180.0f;
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
        if (_hitTime <= 0.0f && _hitVisible)
        {
            _hitVisible = false;
            foreach (var arm in _arms) arm.SetClass("hit", false);
            _crosshair?.SetClass("headshot", false);
            _crosshair?.SetClass("elimination", false);
            _crosshair?.SetClass("armour-hit", false);
        }
    }

    private void OnAmmoChanged(FpsAmmoState state)
    {
        if (_ammo is not null) _ammo.Markup = state.IsReloading ? "RELOAD" : state.Magazine.ToString();
        if (_reserve is not null) _reserve.Markup = $" / {state.Reserve}";
    }

    private void OnMovementChanged(FpsMovementState state)
    {
        _crosshair?.SetClass("hidden", state.IsSprinting);
    }

    private void OnHit(FpsHitEvent hit)
    {
        _hitTime = hit.IsKill ? .3f : hit.IsHeadshot ? .18f : MathF.Max(0.01f, hitFlashDuration);
        if (!_hitVisible)
        {
            _hitVisible = true;
            foreach (var arm in _arms) arm.SetClass("hit", true);
        }
        _crosshair?.SetClass("headshot", hit.IsHeadshot);
        _crosshair?.SetClass("elimination", hit.IsKill);
        _crosshair?.SetClass("armour-hit", hit.HitArmour);
    }

    private void OnInteractionChanged(GameObject? target)
    {
        if (_interaction is not null)
            _interaction.Markup = target is null ? string.Empty : $"[E] {EscapeMarkup(target.Name)}";
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

        _healthFraction = current / MathF.Max(1, maximum);
        RenderDamageOverlay();
    }

    private void OnDied()
    {
        _dead = true;
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
        if (_multiplayer?.RulesMode == MatchMode.Defusal) black = 0;
        var red = _multiplayer?.RulesMode == MatchMode.Defusal ? .12f : 0.72f * (1.0f - black);
        _deathRed?.SetStyle("opacity", red.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _deathBlack?.SetStyle("opacity", black.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void OnMatchUpdated(MatchSnapshot match)
    {
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
            var teamClass = playerState.Team == PlayerTeam.Alpha ? "alpha-team" : "bravo-team";
            var localClass = playerState.PeerId == match.LocalPeerId ? " local" : string.Empty;
            rows.Append($"<div class=\"score-row {teamClass}{localClass}\"><span class=\"player-name\">{EscapeMarkup(playerState.Username)}</span><span class=\"stat\">{playerState.Kills}</span><span class=\"stat\">{playerState.Deaths}</span><span class=\"stat\">{(match.Mode == MatchMode.Defusal ? playerState.Alive ? "IN" : "OUT" : ((int)playerState.ObjectiveSeconds).ToString())}</span></div>");
        }
        _scoreboardRows.Markup = rows.ToString();
    }

    private static string DescribeMatch(MatchSnapshot match)
    {
        var local = Array.Find(match.Players, player => player.PeerId == match.LocalPeerId);
        if (local is null) return $"FIRST TO {match.ScoreLimit}";
        var own = local.Team == PlayerTeam.Alpha ? match.AlphaScore : match.BravoScore;
        var enemy = local.Team == PlayerTeam.Alpha ? match.BravoScore : match.AlphaScore;
        var status = own == enemy ? "TIED" : own > enemy ? "WINNING" : "LOSING";
        return $"{local.Team.ToString().ToUpperInvariant()} | {status} | FIRST TO {match.ScoreLimit}";
    }

    private void OnKillFeed(KillFeedEntry entry)
    {
        _feed.Add(($"{EscapeMarkup(entry.Killer)} eliminated {EscapeMarkup(entry.Victim)}", _hudTime + 5.0f));
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
