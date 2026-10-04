using PlutoGE.ScriptCore;
namespace CoD.Scripts;

/// <summary>Scene marker presentation; the session owns all objective decisions.</summary>
public sealed class DefusalMarker : ScriptBehaviour
{
    [SerializedField] private GameObject? player = null;
    [SerializedField] private int siteIndex = 0;
    private MultiplayerSession? _session;
    private RmlDocument? _document;
    private float _refresh;
    private GameObject? _bombModel;
    public override void OnCreate()
    {
        _session = player?.GetComponent<MultiplayerSession>();
        if (siteIndex < 0) _bombModel = GameObject.Find("Bomb Device");
        _document = new($"UI/defusal-marker.rml#entity:{EntityId}");
    }
    public override void OnDestroy() => _document?.Dispose();
    public override void OnUpdate(float deltaTime)
    {
        _refresh -= MathF.Max(0, deltaTime);
        if (_refresh > 0 || _document is null) return;
        _refresh = .15f;
        var match = _session?.CurrentMatch;
        var label = _document.Element("marker");
        if (siteIndex < 0)
        {
            var bomb = match?.Defusal;
            var visible = match?.Phase == MatchPhase.Playing && bomb?.Bomb is BombState.Dropped or BombState.Planted;
            if (_bombModel is not null) _bombModel.Active = visible;
            if (!label.SetClass("hidden", !visible) || !visible || bomb is null) return;
            var position = bomb.Position - System.Numerics.Vector3.UnitY * .75f;
            if (_bombModel is not null) _bombModel.WorldPosition = position;
            GameObject.WorldPosition = position + System.Numerics.Vector3.UnitY * 1.2f;
            var range = player is null ? 0 : (int)(player.WorldPosition - position).Length();
            var local = Array.Find(match!.Players, p => p.PeerId == match.LocalPeerId);
            var attacker = local?.Team == bomb.Attackers;
            var action = bomb.Bomb == BombState.Planted ? (attacker ? "DEFEND" : "HOLD E TO DEFUSE") : (attacker ? "RECOVER" : "GUARD");
            label.Markup = $"BOMB / {action} / {range}m";
            label.SetClass("contested", bomb.Bomb == BombState.Planted);
            return;
        }
        if (!label.SetClass("hidden", match?.Mode != MatchMode.Defusal)) return;
        var planted = match?.Defusal is { Bomb: BombState.Planted } plantedBomb && plantedBomb.Site == siteIndex;
        var distance = player is null ? 0 : (int)(player.WorldPosition - GameObject.WorldPosition).Length();
        label.Markup = $"{(siteIndex == 0 ? "A / COURTYARD" : "B / FOUNDRY")} | {distance}m{(planted ? " | BOMB" : "")}";
        label.SetClass("contested", planted);
    }
}
