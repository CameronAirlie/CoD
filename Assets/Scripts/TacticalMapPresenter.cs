using System.Globalization;
using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>
/// Draws the same contact list into the HUD minimap (centred on the viewer) and the
/// full tactical map (whole layout). Markers are pooled DOM elements; only changed
/// styles are written. Gameplay owns the contacts; this class owns presentation only.
/// </summary>
public sealed class TacticalMapPresenter
{
    private const int PoolSize = 24;
    private readonly RmlDocument _document;
    private readonly MinimapLayout? _layout;
    private readonly float _minimapWorldSpan;
    private readonly MapView _minimap;
    private readonly MapView _fullMap;
    private readonly List<MapContact> _contacts = [];
    private bool _initialized;
    private bool _fullMapOpen;
    private string _mapName = "";
    private string _caption = "";
    private string _hint = "";

    public TacticalMapPresenter(RmlDocument document, string scene, float minimapWorldSpan)
    {
        _document = document;
        _layout = MinimapLayouts.For(scene);
        _minimapWorldSpan = MathF.Max(8, minimapWorldSpan);
        _minimap = new(document, "minimap");
        _fullMap = new(document, "tactical-map");
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
            if (showSelf) _contacts.Add(new(-1, MapContactKind.Self, viewPosition, viewHeading));
        }
        _contacts.Sort((a, b) => TacticalMap.Priority(a).CompareTo(TacticalMap.Priority(b)));

        // Layer size and offset are percentages of the square frame, so RCSS may resize the frame freely.
        var layerPercent = TacticalMap.LayerSize(_layout, 100, _minimapWorldSpan);
        var offset = TacticalMap.FocusOffset(_layout.Normalize(viewPosition), layerPercent, 100);
        if (!_minimap.Render(_contacts, _layout, layerPercent, offset)) { _initialized = false; return; }
        if (_fullMapOpen && !_fullMap.Render(_contacts, _layout, 100, Vector2.Zero)) _initialized = false;

        // Prompts follow the active input device and any rebinding.
        var caption = Escape($"{_mapName} / {GameInput.FormatPrompt("{ShowMap} MAP")}");
        if (caption != _caption) { _document.Element("minimap-caption").Markup = caption; _caption = caption; }
        var hint = Escape(GameInput.FormatPrompt("{ShowMap} CLOSE MAP"));
        if (hint != _hint) { _document.Element("tactical-map-hint").Markup = hint; _hint = hint; }
    }

    private bool Initialize()
    {
        if (!_document.Element("minimap").SetClass("hidden", _layout is null)) return false;
        if (_layout is null) { _initialized = true; return true; }
        _document.Element("minimap-image")["src"] = _layout.Image;
        _document.Element("tactical-map-image")["src"] = _layout.Image;
        _mapName = MapCatalog.Find(MatchMode.Hardpoint, _layout.Scene)?.DisplayName
            ?? MapCatalog.Find(MatchMode.Defusal, _layout.Scene)?.DisplayName ?? _layout.Scene.ToUpperInvariant();
        _document.Element("tactical-map-name").Markup = Escape(_mapName);
        _document.Element("tactical-map").SetClass("hidden", !_fullMapOpen);
        _caption = _hint = "";
        if (!_minimap.Build(PoolSize) || !_fullMap.Build(PoolSize)) return false;
        _initialized = true;
        return true;
    }

    private static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private sealed class MapView(RmlDocument document, string prefix)
    {
        private RmlElement[] _markers = [];
        private string[] _kind = [], _left = [], _top = [], _rotation = [], _opacity = [];
        private string _layerWidth = "", _layerHeight = "", _layerLeft = "", _layerTop = "";
        private RmlElement? _layer;

        public bool Build(int count)
        {
            var markup = new System.Text.StringBuilder();
            for (var i = 0; i < count; i++)
                markup.Append($"<div id=\"{prefix}-c{i}\" class=\"map-contact hidden\"><img class=\"map-arrow\" src=\"Icons/map-arrow.tga\"/><div class=\"map-dot\"></div></div>");
            document.Element($"{prefix}-contacts").Markup = markup.ToString();
            _layer = document.Element($"{prefix}-layer");
            _markers = new RmlElement[count];
            for (var i = 0; i < count; i++) _markers[i] = document.Element($"{prefix}-c{i}");
            Invalidate();
            return _markers.Length == 0 || _markers[0].SetClass("hidden", true);
        }

        public void Invalidate()
        {
            var count = _markers.Length;
            _kind = new string[count]; _left = new string[count]; _top = new string[count];
            _rotation = new string[count]; _opacity = new string[count];
            _layerWidth = _layerHeight = _layerLeft = _layerTop = "";
        }

        /// <summary>Returns false when the native DOM was recreated and the pool must be rebuilt.</summary>
        public bool Render(List<MapContact> contacts, MinimapLayout layout, float layerPercent, Vector2 offset)
        {
            if (_layer is null) return false;
            var size = Percent(layerPercent);
            if (!Write(_layer, "width", size, ref _layerWidth)) return false;
            Write(_layer, "height", size, ref _layerHeight);
            Write(_layer, "left", Percent(offset.X), ref _layerLeft);
            Write(_layer, "top", Percent(offset.Y), ref _layerTop);
            for (var i = 0; i < _markers.Length; i++)
            {
                var marker = _markers[i];
                if (i >= contacts.Count)
                {
                    if (_kind[i] != "hidden" && !marker.SetClass("hidden", true)) return false;
                    _kind[i] = "hidden";
                    continue;
                }
                var contact = contacts[i];
                var kind = contact.Kind switch { MapContactKind.Self => "self", MapContactKind.Enemy => "enemy", _ => "friendly" };
                if (_kind[i] != kind)
                {
                    if (!marker.SetClass("hidden", false)) return false;
                    marker.SetClass("self", kind == "self");
                    marker.SetClass("friendly", kind == "friendly");
                    marker.SetClass("enemy", kind == "enemy");
                    _kind[i] = kind;
                }
                var point = layout.Normalize(contact.Position) * 100;
                Write(marker, "left", Percent(point.X), ref _left[i]);
                Write(marker, "top", Percent(point.Y), ref _top[i]);
                // Enemy facing is deliberately never presented.
                var rotation = contact.ShowsHeading ? $"rotate({contact.Heading.ToString("0", CultureInfo.InvariantCulture)}deg)" : "none";
                Write(marker, "transform", rotation, ref _rotation[i]);
                Write(marker, "opacity", Math.Clamp(contact.Opacity, 0, 1).ToString("0.##", CultureInfo.InvariantCulture), ref _opacity[i]);
            }
            return true;
        }

        private static string Percent(float value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "%";

        private static bool Write(RmlElement element, string property, string value, ref string cache)
        {
            if (cache == value) return true;
            if (!element.SetStyle(property, value)) return false;
            cache = value;
            return true;
        }
    }
}
