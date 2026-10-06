using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Presentation adapter for objectives, supplies and progression. Never mutates match rules.</summary>
public sealed class GameModeHud : ScriptBehaviour
{
    [SerializedField] private GameObject? player = null;
    private MultiplayerSession? _session;
    private PlayerInventory? _inventory;
    private PlayerHealth? _health;
    private CombatFeedback? _feedback;
    private RoundProgression? _progression;
    private RmlDocument? _document;
    private readonly Dictionary<string, string> _rendered = new();
    private float _refresh;
    public override void OnCreate()
    {
        _session = player?.GetComponent<MultiplayerSession>();
        _inventory = player?.GetComponent<PlayerInventory>();
        _health = player?.GetComponent<PlayerHealth>();
        _feedback = player?.GetComponent<CombatFeedback>();
        _progression = player?.GetComponent<RoundProgression>();
        _document = new("UI/hud.rml");
    }
    public override void OnDestroy() => _document?.Dispose();
    public override void OnUpdate(float deltaTime)
    {
        _refresh -= MathF.Max(0, deltaTime);
        if (_refresh > 0 || _document is null) return;
        _refresh = .1f;
        var match = _session?.CurrentMatch;
        Set("match-mode", match?.Mode == MatchMode.Defusal ? "DEFUSAL / FOUNDRY" : match?.Mode == MatchMode.Hardpoint ? "HARDPOINT" : "TEAM DEATHMATCH");
        Set("scoreboard-mode", match?.Mode == MatchMode.Defusal ? "DEFUSAL | FIRST TO 7 ROUNDS" : match?.Mode == MatchMode.Hardpoint ? "HARDPOINT | OBJ = SECONDS ON POINT" : "TEAM DEATHMATCH");
        Set("scoreboard-objective-heading", match?.Mode == MatchMode.Defusal ? "LIFE" : "OBJ");
        var local = match is null ? null : Array.Find(match.Players, p => p.PeerId == match.LocalPeerId);
        var objective = match?.Objective;
        if (match?.Mode == MatchMode.Defusal) RenderDefusal(match, local);
        else if (match?.Phase == MatchPhase.Playing && objective is not null && player is not null)
        {
            var direction = objective.Position - player.WorldPosition;
            var bearing = CombatFeedbackMath.Bearing(player.Forward, player.Right, direction);
            var status = ObjectiveStatus.Describe(objective, local?.Team, player.WorldPosition, _health?.IsDead != true);
            Set("objective-status", status.Headline);
            Set("objective-instruction", status.Instruction);
            Set("objective-detail", $"{objective.Name.ToUpperInvariant().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")} | {(status.Inside ? "YOU ARE IN THE ZONE" : $"{(int)direction.Length()}m {Compass(bearing)}")}");
            Set("objective-rotation", $"ZONE MOVES IN {(int)MathF.Ceiling(objective.SecondsRemaining)}s");
            foreach (var style in new[] { "neutral", "friendly", "enemy", "contested" })
                _document.Element("objective").SetClass(style, style == status.Style);
        }
        else
        {
            Set("objective-status", match?.Phase == MatchPhase.Results ? "ROUND COMPLETE" : "PREPARE FOR THE ROUND");
            Set("objective-instruction", match?.Mode == MatchMode.Hardpoint ? "Hold the zone to score. Both teams inside = contested." : "Eliminate the opposing team.");
            Set("objective-detail", match?.Phase == MatchPhase.Results ? "Choose your next loadout below." : "Choose loadout: 1 Assault / 2 Medic / 3 Defender");
            Set("objective-rotation", "");
            foreach (var style in new[] { "neutral", "friendly", "enemy", "contested" }) _document.Element("objective").SetClass(style, false);
        }
        Set("score-goal", match is null ? "" : $"FIRST TO {match.ScoreLimit} | YOU: {local?.Team.ToString().ToUpperInvariant() ?? "SPECTATOR"}");
        if (_inventory is not null)
        {
            Set("supply-counts", $"H MED KITS {_inventory.HealthKits} | G PLATES {_inventory.ArmourPlates} | CAPACITY {_inventory.SlotsUsed}/{InventoryRules.Capacity}");
            Set("supply-use", _inventory.IsUsing ? $"APPLYING SUPPLY {(int)(_inventory.UseProgress * 100)}% - DAMAGE INTERRUPTS" : match?.Mode == MatchMode.Defusal ? "HOLD E PLANT / DEFUSE | I INVENTORY" : "E COLLECT SUPPLIES | I INVENTORY");
        }
        var selecting = match?.Phase is MatchPhase.Warmup or MatchPhase.Results;
        Set("loadout-choice", selecting ? $"1 ASSAULT / START WITH AR: 150 AMMO / 1 KIT / 1 PLATE<br/>2 MEDIC / START WITH PISTOL: 60 AMMO / 3 KITS<br/>3 DEFENDER / START WITH MG: 90 AMMO / 1 KIT / 3 PLATES<br/>ALL CLASSES CARRY ALL THREE WEAPONS<br/>SELECTED: {_session?.SelectedLoadout.ToString().ToUpperInvariant()}" : "");
        Set("combat-cue", _feedback?.CueTime > 0 ? _feedback.LastCue : "");
        Set("round-progress", _progression is null ? "" : match?.Phase == MatchPhase.Results && local is not null && (match.Mode != MatchMode.Defusal || match.Defusal?.MatchOver == true) ?
            $"{local.Kills} ELIMINATIONS | {(int)local.ObjectiveSeconds}s ON OBJECTIVE | +{_progression.LastMatchExperience} XP | LEVEL {_progression.Level}" : $"LEVEL {_progression.Level} | {_progression.TotalExperience} XP");
        var sector = _feedback?.DamageIndicatorTime > 0 ? Sector(_feedback.DamageBearing) : -1;
        string[] names = ["front", "right", "back", "left"];
        for (var i = 0; i < names.Length; i++) _document.Element("incoming-" + names[i]).SetClass("hidden", sector != i);
    }
    private void RenderDefusal(MatchSnapshot match, MatchPlayer? local)
    {
        var bomb = match.Defusal;
        var nextRound = bomb?.MatchOver == true ? 1 : (bomb?.Round ?? 0) + 1;
        var role = match.Phase == MatchPhase.Playing ? bomb?.Attackers ?? PlayerTeam.Alpha : DefusalRules.AttackersForRound(nextRound);
        var attack = local?.Team == role;
        var aliveA = match.Players.Count(p => p.Team == PlayerTeam.Alpha && p.Alive);
        var aliveB = match.Players.Count(p => p.Team == PlayerTeam.Bravo && p.Alive);
        var headline = attack ? "ATTACK / PLANT AT A OR B" : "DEFEND / PROTECT BOTH SITES";
        var instruction = attack ? "Escort the carrier. Clear a site. Hold E to plant." : "Stop the attackers. Hold E beside a planted bomb to defuse.";
        var detail = $"ALIVE: ALPHA {aliveA} / BRAVO {aliveB} | ONE LIFE PER ROUND";
        var clock = "SIDES SWAP AFTER ROUND 6";
        if (match.Phase != MatchPhase.Playing)
        {
            headline = match.Phase == MatchPhase.Results ? (bomb?.MatchOver == true ? "MATCH COMPLETE" : "ROUND COMPLETE") : "PREPARATION / CHOOSE LOADOUT";
            instruction = match.Phase == MatchPhase.Results ? $"{bomb?.Winner?.ToString().ToUpperInvariant()}: {bomb?.Reason}" : $"ROUND {nextRound} / YOU {(attack ? "ATTACK" : "DEFEND")} / 1 Assault / 2 Medic / 3 Defender";
            clock = $"{(match.Phase == MatchPhase.Results ? "PREPARATION" : "ROUND STARTS")} IN {(int)MathF.Ceiling(match.SecondsRemaining)}s";
        }
        else if (bomb is not null)
        {
            if (bomb.Bomb == BombState.Planted)
            {
                headline = $"BOMB PLANTED / {(bomb.Site == 0 ? "A COURTYARD" : "B FOUNDRY")}";
                instruction = attack ? "Protect the bomb until it detonates." : "Reach the bomb. Hold E for 5 seconds to defuse.";
                clock = $"DETONATION IN {(int)MathF.Ceiling(bomb.Seconds)}s";
            }
            else if (bomb.Bomb == BombState.Dropped)
            {
                headline = "BOMB DROPPED";
                instruction = attack ? "Recover the bomb by walking over it." : "Guard the dropped bomb or eliminate the remaining attackers.";
                if (player is not null) detail += $" | BOMB {(int)(player.WorldPosition - bomb.Position).Length()}m {Compass(CombatFeedbackMath.Bearing(player.Forward, player.Right, bomb.Position - player.WorldPosition))}";
            }
            else if (bomb.Carrier == local?.PeerId) instruction = "YOU CARRY THE BOMB / Enter A or B, stop, and hold E for 3 seconds.";
            else if (attack) instruction = "ESCORT YOUR BOMB CARRIER / Clear either site.";
            if (bomb.Operator is { } actor && bomb.Progress > 0)
                instruction = $"{(actor == local?.PeerId ? "YOU ARE" : "PLAYER IS")} {(bomb.Bomb == BombState.Planted ? "DEFUSING" : "PLANTING")} / {(int)(bomb.Progress * 100)}%";
            if (local?.Alive == false) detail = "ELIMINATED / NEXT LIFE AT ROUND START | " + detail;
        }
        Set("objective-status", headline); Set("objective-instruction", instruction);
        Set("objective-detail", detail); Set("objective-rotation", clock);
        foreach (var style in new[] { "neutral", "friendly", "enemy", "contested" })
            _document?.Element("objective").SetClass(style, style == (bomb?.Bomb == BombState.Planted ? "contested" : "neutral"));
    }
    private void Set(string id, string text)
    {
        if (_rendered.GetValueOrDefault(id) == text) return;
        var element = _document?.Element(id);
        if (element?.SetClass("gameplay-ready", true) != true) return;
        element.Markup = text; _rendered[id] = text;
    }
    private static int Sector(float bearing) => ((int)MathF.Floor((bearing + 45 + 360) / 90)) % 4;
    private static string Compass(float bearing) => Sector(bearing) switch { 0 => "AHEAD", 1 => "RIGHT", 2 => "BEHIND", _ => "LEFT" };
}
