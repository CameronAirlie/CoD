using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Projected world marker; hidden through DOM state so its script remains active across rotations.</summary>
public sealed class HardpointMarker : ScriptBehaviour
{
    [SerializedField] private GameObject? player = null;
    [SerializedField] private int siteIndex = 0;
    private MultiplayerSession? _session;
    private RmlDocument? _document;
    private RmlElement? _label;
    private float _nextRefresh;
    private string _lastText = "";
    public override void OnCreate()
    {
        _session = player?.GetComponent<MultiplayerSession>();
        _document = new($"UI/hardpoint-marker.rml#entity:{EntityId}");
        _label = _document.Element("marker");
    }
    public override void OnDestroy() => _document?.Dispose();
    public override void OnUpdate(float deltaTime)
    {
        _nextRefresh -= MathF.Max(0, deltaTime);
        if (_nextRefresh > 0) return;
        _nextRefresh = .1f;
        var match = _session?.CurrentMatch;
        var objective = match?.Objective;
        var visible = match?.Phase == MatchPhase.Playing && objective?.Index == siteIndex;
        if (_label?.SetClass("hidden", !visible) != true || !visible || objective is null) return;
        var distance = player is null ? 0 : (int)System.Numerics.Vector3.Distance(player.WorldPosition, objective.Position);
        var local = Array.Find(match!.Players, p => p.PeerId == match.LocalPeerId);
        var action = objective.Contested ? "CONTESTED" : objective.Owner is null ? "CAPTURE" : objective.Owner == local?.Team ? "DEFEND" : "ATTACK";
        var text = $"{action}: {objective.Name.ToUpperInvariant()} | {distance}m | {(int)MathF.Ceiling(objective.SecondsRemaining)}s";
        if (_lastText != text) { _label.Markup = "<img class=\"marker-icon\" src=\"UI/Icons/objective.tga\"/>" + text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"); _lastText = text; }
        _label.SetClass("friendly", objective.Owner is not null && objective.Owner == local?.Team);
        _label.SetClass("enemy", objective.Owner is not null && objective.Owner != local?.Team);
        _label.SetClass("contested", objective.Contested);
    }
}
