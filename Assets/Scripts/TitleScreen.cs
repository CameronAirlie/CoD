using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Title screen controller backed by native RML form controls.</summary>
public sealed class TitleScreen : ScriptBehaviour
{
    [SerializedField] private string documentPath = "UI/title.rml";
    [SerializedField] private string gameScene = "Main";
    [SerializedField] private string username = "Player";
    [SerializedField] private string serverAddress = "127.0.0.1";

    private RmlDocument? _document;
    private RmlElement? _usernameInput;
    private RmlElement? _addressInput;
    private RmlElement? _statusLabel;
    private bool _loading;
    private bool _defusalSelected;
    private bool _spectatorSelected;
    private ControllerMenu? _menu;
    private SettingsMenu? _settings;
    private ControllerKeyboard? _keyboard;
    private bool _inSubmenu;
    [SerializedField] private string defusalScene = "Terminal";

    public override void OnCreate()
    {
        Input.CursorLocked = false;
        GameInput.SetMenuCapture(true);
        var widget = GameObject.GetComponent<RmlWidgetComponent>();
        if (widget is null)
        {
            Debug.LogError("TitleScreen requires an RmlWidgetComponent.");
            return;
        }

        if (string.IsNullOrWhiteSpace(widget.Source))
            widget.Source = documentPath;
        _document = widget.Document;
        _settings = GameGraphicsSettings.Bind(_document);
        _keyboard = new(_document);
        _menu = new(_document);
        _usernameInput = _document.Element("username");
        _addressInput = _document.Element("address");
        _statusLabel = _document.Element("status");
        _usernameInput["value"] = username;
        _addressInput["value"] = serverAddress;
        _usernameInput.On("input", SyncFields);
        _addressInput.On("input", SyncFields);
        _usernameInput.On("change", SyncFields);
        _addressInput.On("change", SyncFields);
        _menu.Add(new("mode-hardpoint", 0, 0, () => SelectMode(false)));
        _menu.Add(new("mode-defusal", 1, 0, () => SelectMode(true)));
        _menu.Add(new("username", 2, 0, () => EditField("CALLSIGN", _usernameInput, 20)), false);
        _menu.Add(new("address", 3, 0, () => EditField("HOST ADDRESS", _addressInput, 253)), false);
        _menu.Add(new("role-player", 4, 0, () => SelectRole(false)));
        _menu.Add(new("role-spectator", 4, 1, () => SelectRole(true)));
        _menu.Add(new("host", 5, 0, () => Launch("Host")));
        _menu.Add(new("join", 5, 1, () => Launch("Client")));
        _menu.Add(new("graphics-preset", 6, 1, _settings.Open), false);
        _menu.Open("mode-hardpoint");
        SetStatus("Enter a username, then host or join a game.");
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_loading)
            return;

        var frame = MenuFrame.Read();
        _menu?.RefreshHints();
        var submenu = _settings?.IsOpen == true || _keyboard?.IsOpen == true;
        if (submenu && !_inSubmenu) _menu?.Close();
        if (!submenu && _inSubmenu) _menu?.Open();
        _inSubmenu = submenu;
        if (_keyboard?.IsOpen == true) _keyboard.Update(frame);
        else if (_settings?.IsOpen == true) _settings.Update(frame);
        else _menu?.Update(frame);
    }

    private void EditField(string label, RmlElement? field, int limit)
    {
        if (field is null) return;
        _keyboard?.Open(label, field["value"], limit, value => { field["value"] = value; SyncFields(); });
    }

    public override void OnDestroy()
    {
        // The RmlWidgetComponent owns the document handle.
        _document = null;
    }

    private void SyncFields()
    {
        if (_usernameInput is not null)
            username = _usernameInput["value"];
        if (_addressInput is not null)
            serverAddress = _addressInput["value"];
    }

    private void Launch(string launchMode)
    {
        SyncFields();
        var cleanName = username.Trim();
        if (cleanName.Length == 0)
        {
            SetStatus("Please enter a username.");
            return;
        }
        if (launchMode == "Client" && serverAddress.Trim().Length == 0)
        {
            SetStatus("Please enter the host IP address.");
            return;
        }

        if (!MultiplayerLaunch.Configure(launchMode, cleanName, serverAddress, _spectatorSelected))
        {
            SetStatus("Enter a server address such as 127.0.0.1 or 127.0.0.1:7777.");
            return;
        }
        _loading = SceneManager.LoadScene(_defusalSelected ? defusalScene : gameScene);
        SetStatus(_loading
            ? launchMode == "Host" ? "Starting host..." : "Joining game..."
            : "Could not load the game scene.");
    }

    private void SelectRole(bool spectator)
    {
        if (_loading) return;
        _spectatorSelected = spectator;
        _document?.Element("role-player").SetClass("selected", !spectator);
        _document?.Element("role-spectator").SetClass("selected", spectator);
        SetStatus(spectator ? "NEUTRAL SPECTATOR: Watch both teams. F1 / F2 choose a side; Q / E cycle players." : "PLAYER: Join a team and play the match.");
    }

    private void SelectMode(bool defusal)
    {
        if (_loading) return;
        _defusalSelected = defusal;
        _document?.Element("mode-hardpoint").SetClass("selected", !defusal);
        _document?.Element("mode-defusal").SetClass("selected", defusal);
        if (_document is not null)
        {
            _document.Element("mission-location").Markup = defusal ? defusalScene.ToUpperInvariant() : "ARENA";
            _document.Element("mission-title").Markup = defusal ? "ONE LIFE. TWO SITES." : "HOLD THE LINE";
            _document.Element("mission-description").Markup = defusal ? "Plant or defuse the bomb. Protect your squad. First to seven rounds wins." : "Capture the rotating hardpoint. Deny the enemy. Every second counts.";
            _document.Element("mission-rule").Markup = defusal ? "PLANT / DEFUSE" : "CAPTURE / HOLD";
            _document.Element("mission-life").Markup = defusal ? "ONE LIFE" : "ENABLED";
        }
        SetStatus(defusal ? $"{defusalScene.ToUpperInvariant()}: Two bomb sites. One life. First to 7 rounds. Select the same mode as your host." : "ARENA: Capture and hold the rotating hardpoint.");
    }
    private void SetStatus(string message)
    {
        if (_statusLabel is not null)
            _statusLabel.Markup = message;
    }
}
