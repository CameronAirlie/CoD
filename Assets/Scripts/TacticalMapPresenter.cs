using System.Numerics;
using PlutoGE.ScriptCore;
using PlutoGE.ScriptCore.Gameplay;

namespace CoD.Scripts;

/// <summary>
/// Draws the same contact list into the HUD minimap (centred on the viewer) and the full
/// tactical map (whole layout) with the engine's RmlMapView. Gameplay owns the contacts;
/// this class owns the frames, captions and open/closed state.
/// </summary>
public sealed class TacticalMapPresenter
{
    private const int PoolSize = 24;
    private readonly RmlDocument _document;
    private readonly MapLayout? _layout;
    private readonly RmlMapView _minimap;
    private readonly RmlMapView _fullMap;
    private readonly List<MapContact> _contacts = [];
    private bool _initialized;
    private bool _fullMapOpen;
    private string _mapName = "";
    private string _caption = "";
    private string _hint = "";

    public TacticalMapPresenter(RmlDocument document, string scene, float minimapWorldSpan)
    {
        _document = document;
        // Baked by File > Bake Top-Down Map or PlutoGEBakeMap into Assets/Maps.
        _layout = MapLayout.ForScene(scene);
        var marker = RmlMapView.ArrowMarker("Icons/map-arrow.tga");
        _minimap = new(document, "minimap", marker, PoolSize) { VisibleWorldSpan = MathF.Max(8, minimapWorldSpan) };
        _fullMap = new(document, "tactical-map", marker, PoolSize);
    }

    public bool HasMap => _layout is not null;
    public bool FullMapOpen => _fullMapOpen;

    public void SetFullMapOpen(bool open)
    {
        if (_layout is null) open = false;
        if (_fullMapOpen == open) return;
        _fullMapOpen = open;
        _document.Element("tactical-map").SetClass("hidden", !open);
        if (open) _fullMap.Invalidate();
    }

    /// <summary>Call from the HUD update after the document is live.</summary>
    public void Update(MultiplayerSession? session, Vector3 viewPosition, float viewHeading, bool showSelf)
    {
        if (!_initialized && !Initialize()) return;
        if (_layout is null) return;
        if (session is not null) session.CollectMapContacts(_contacts, viewPosition, viewHeading, showSelf);
        else
        {
            _contacts.Clear();
            if (showSelf) _contacts.Add(TacticalMap.Self(-1, viewPosition, viewHeading));
        }
        if (!_minimap.Render(_layout, _contacts, viewPosition)) { _initialized = false; return; }
        if (_fullMapOpen && !_fullMap.Render(_layout, _contacts, viewPosition)) { _initialized = false; return; }

        // Prompts follow the active input device and any rebinding.
        var caption = Escape($"{_mapName} / {GameInput.FormatPrompt("{ShowMap} MAP")}");
        if (caption != _caption) { _document.Element("minimap-caption").Markup = caption; _caption = caption; }
        var hint = Escape(GameInput.FormatPrompt("{ShowMap} CLOSE MAP"));
        if (hint != _hint) { _document.Element("tactical-map-hint").Markup = hint; _hint = hint; }
    }

    private bool Initialize()
    {
        if (!_document.Element("minimap").SetClass("hidden", _layout is null)) return false;
        if (_layout is not null)
        {
            _mapName = MapCatalog.Find(MatchMode.Hardpoint, _layout.Name)?.DisplayName
                ?? MapCatalog.Find(MatchMode.Defusal, _layout.Name)?.DisplayName ?? _layout.Name.ToUpperInvariant();
            _document.Element("tactical-map-name").Markup = Escape(_mapName);
            _document.Element("tactical-map").SetClass("hidden", !_fullMapOpen);
            _minimap.Invalidate();
            _fullMap.Invalidate();
        }
        _caption = _hint = "";
        _initialized = true;
        return true;
    }

    private static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
