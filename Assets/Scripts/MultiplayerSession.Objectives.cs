using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Host-authoritative objective adapter. Rules remain independent of scripts, UI and transport.</summary>
public sealed partial class MultiplayerSession
{
    [SerializedField] private string gameMode = "Hardpoint";
    [SerializedField] private float hardpointDuration = 45;
    [SerializedField] private float hardpointRadius = 5;
    [SerializedField] private string hardpointSiteNames = "Hardpoint Courtyard|Hardpoint West|Hardpoint East";
    private HardpointRules? _hardpoint;
    // Gradual pressure within each round: tighter accuracy, bounded to 25%.
    private float BotPressure => 1 + Math.Clamp((matchDuration - MathF.Max(0, _phaseEndsAt - _time)) /
        MathF.Max(1, matchDuration), 0, 1) * .25f;
    private readonly List<ObjectiveParticipant> _objectiveParticipants = new();
    private PlayerInventory? _inventory;
    private MatchPhase? _lastLocalPhase;
    public CombatLoadout SelectedLoadout { get; private set; } = CombatLoadout.Assault;
    public MatchMode RulesMode => gameMode.Equals("TeamDeathmatch", StringComparison.OrdinalIgnoreCase)
        ? MatchMode.TeamDeathmatch : gameMode.Equals("Defusal", StringComparison.OrdinalIgnoreCase) ? MatchMode.Defusal : MatchMode.Hardpoint;
    public bool CanFight => CurrentMatch?.Phase == MatchPhase.Playing;

    private void BeginObjectiveRound()
    {
        if (RulesMode != MatchMode.Hardpoint) { _hardpoint = null; return; }
        var sites = new List<HardpointSite>();
        foreach (var name in hardpointSiteNames.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var marker = GameObject.Find(name);
            if (marker is not null) sites.Add(new(name.Replace("Hardpoint ", ""), marker.WorldPosition));
        }
        if (sites.Count == 0)
        {
            Debug.LogWarning("Hardpoint markers missing; using the default arena layout.");
            sites.Add(new("Courtyard", new(0, .2f, 0)));
            sites.Add(new("West", new(-12, .2f, 10)));
            sites.Add(new("East", new(12, .2f, -10)));
        }
        _hardpoint = new(sites, hardpointDuration, hardpointRadius);
    }

    private void UpdateObjective(float deltaTime)
    {
        if (_hardpoint is null || _matchPhase != MatchPhase.Playing) return;
        _objectiveParticipants.Clear();
        foreach (var pair in _playerStates)
        {
            var participant = GetParticipantObject(pair.Key);
            if (participant is null) continue;
            var state = pair.Value;
            _objectiveParticipants.Add(new(state.Team, participant.WorldPosition, state.Health > 0));
            if (state.Health > 0 && _hardpoint.Contains(participant.WorldPosition))
                state.ObjectiveSeconds += deltaTime;
        }
        var previousIndex = _hardpoint.Index;
        var previousOwner = _hardpoint.Owner;
        var previousContest = _hardpoint.Contested;
        _hardpoint.Tick(deltaTime, _objectiveParticipants);
        if (previousIndex != _hardpoint.Index || previousOwner != _hardpoint.Owner || previousContest != _hardpoint.Contested)
            Debug.Log($"Hardpoint {_hardpoint.Site.Name}: {(_hardpoint.Contested ? "contested" : _hardpoint.Owner?.ToString() ?? "neutral")}; score {_hardpoint.AlphaScore}-{_hardpoint.BravoScore}.");
        _alphaScore = _hardpoint.AlphaScore; _bravoScore = _hardpoint.BravoScore;
        if (_alphaScore >= Math.Max(1, scoreLimit) || _bravoScore >= Math.Max(1, scoreLimit))
            BeginPhase(MatchPhase.Results, resultsDuration);
    }

    private void UpdateLocalRound(MatchSnapshot snapshot)
    {
        _inventory ??= GameObject.GetComponent<PlayerInventory>();
        if (snapshot.Mode == MatchMode.Defusal) { ApplyDefusalLocalRound(snapshot); return; }
        if (_lastLocalPhase != snapshot.Phase && snapshot.Phase == MatchPhase.Playing)
        {
            _playerHealth?.ResetForRound();
            _inventory?.ApplyLoadout(SelectedLoadout);
            _playerController?.ResetWeaponForRound();
        }
        _lastLocalPhase = snapshot.Phase;
    }

    private void OnLocalRespawn()
    {
        _inventory ??= GameObject.GetComponent<PlayerInventory>();
        _inventory?.ApplyLoadout(SelectedLoadout);
        _playerController?.ResetWeaponForRound();
    }
    private void UpdateLoadoutSelection()
    {
        if (CurrentMatch?.Phase is not (MatchPhase.Warmup or MatchPhase.Results)) return;
        if (Input.IsKeyPressed(KeyCode.D1)) SelectedLoadout = CombatLoadout.Assault;
        if (Input.IsKeyPressed(KeyCode.D2)) SelectedLoadout = CombatLoadout.Medic;
        if (Input.IsKeyPressed(KeyCode.D3)) SelectedLoadout = CombatLoadout.Defender;
    }

    private bool TryObjectiveMovement(int botId, BotController bot, PlayerMatchState state, bool canThink)
    {
        if (_hardpoint is null) return false;
        var retreating = bot.CombatMovement.WantsRetreat(_time,
            state.Health / MathF.Max(1, multiplayerMaximumHealth), _time < bot.ReloadCompleteAt);
        var target = GetParticipantObject(bot.TargetPeerId);
        var closeThreat = target is not null && bot.CachedLineOfSight &&
            HorizontalDistance(bot.GameObject.WorldPosition, target.WorldPosition) <= botAttackRange;
        // A visible enemy in weapon range takes priority even on the way to a
        // neutral/enemy point. Bounded holds still provide windows to advance.
        if (!retreating && closeThreat) return false;
        bot.IsEngaging = false; bot.TacticalSearchActive = false;
        bot.GameObject.TryInvoke("SetExternalAiming", false);
        if (canThink && _time >= bot.NextNavigationAt)
        {
            var desired = BotObjectiveTactics.Destination(botId, _hardpoint.Snapshot(), state.Team,
                bot.GameObject.WorldPosition, retreating, _time);
            if (TryProjectBotNavigationPosition(desired, out var projected)) desired = projected;
            SetBotDestination(bot, desired, _hardpoint.Site.Position);
            bot.NextNavigationAt = _time + MathF.Max(.2f, botNavigationRefreshInterval);
        }
        if (_remotePlayers.TryGetValue(botId, out var remote))
        {
            remote.TargetPosition = bot.GameObject.WorldPosition;
            remote.TargetYaw = bot.GameObject.Rotation.Y;
        }
        return true;
    }
}
