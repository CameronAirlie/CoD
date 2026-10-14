using PlutoGE.ScriptCore;

public sealed class UIManager : ScriptBehaviour
{
    [SerializedField] private GameObject? PauseMenu = null;
    [SerializedField] private GameObject? HUD = null;
    [SerializedField] private GameObject? Inventory = null;
    [SerializedField] private string PauseDocumentPath = "UI/pause-menu.rml";
    [SerializedField] private string HUDDocumentPath = "UI/hud.rml";
    [SerializedField] private string InventoryDocumentPath = "UI/inventory.rml";
    [SerializedField] private string MainMenuScene = "Title";

    private RmlWidgetComponent? _pauseWidget;
    private RmlWidgetComponent? _hudWidget;
    private RmlWidgetComponent? _inventoryWidget;
    private RmlDocument? _pauseDocument;
    private RmlDocument? _hudDocument;
    private CoD.Scripts.ControllerMenu? _controllerMenu;
    private CoD.Scripts.SettingsMenu? _settings;
    private CoD.Scripts.ControllerConfirmation? _confirmation;
    private bool _settingsWasOpen;
    private CoD.Scripts.MultiplayerSession? _session;
    private string _switchTeamLabel = "";
    private CoD.Scripts.MultiplayerSession? Session => _session ??= GameObject.Find("Player")?.GetComponent<CoD.Scripts.MultiplayerSession>();
    private bool _isPaused;
    private bool _isInventoryOpen;
    private bool _needsInitialPresentation = true;
    private float _previousTimeScale = 1.0f;
    private bool _ownsTimePause;

    public bool IsPaused => _isPaused;

    public override void OnCreate()
    {
        _pauseWidget = PauseMenu?.GetComponent<RmlWidgetComponent>();
        _hudWidget = HUD?.GetComponent<RmlWidgetComponent>();
        _inventoryWidget = Inventory?.GetComponent<RmlWidgetComponent>();

        if (_pauseWidget == null)
            Debug.LogError("UIManager: PauseMenu must reference an RmlWidgetComponent.");
        if (_hudWidget == null)
            Debug.LogError("UIManager: HUD must reference an RmlWidgetComponent.");
        if (_inventoryWidget == null)
            Debug.LogError("UIManager: Inventory must reference an RmlWidgetComponent.");

        if (!string.IsNullOrWhiteSpace(PauseDocumentPath))
        {
            _pauseDocument = new RmlDocument(PauseDocumentPath);
            _settings = CoD.Scripts.GameGraphicsSettings.Bind(_pauseDocument);
            _confirmation = new(_pauseDocument);
            _controllerMenu = new(_pauseDocument) { Back = Resume };
            _controllerMenu.Add(new("resume", 0, 0, Resume));
            _controllerMenu.Add(new("graphics-preset", 1, 0, _settings.Open), false);
            _controllerMenu.Add(new("switch-team", 2, 0, SwitchTeam, () => Session is not null));
            _controllerMenu.Add(new("restart", 3, 0, () => _confirmation.Open("RESTART?", "Restart this scene and leave the current match?", Restart)));
            _controllerMenu.Add(new("main-menu", 4, 0, () => _confirmation.Open("LEAVE MATCH?", "Disconnect and return to the main menu?", ReturnToMainMenu)));
            _controllerMenu.Add(new("quit", 5, 0, () => _confirmation.Open("QUIT GAME?", "Disconnect and close the game?", Quit)));
        }
        else
        {
            Debug.LogError("UIManager: PauseDocumentPath must be configured.");
        }

        if (!string.IsNullOrWhiteSpace(HUDDocumentPath))
            _hudDocument = new RmlDocument(HUDDocumentPath);
        else
            Debug.LogError("UIManager: HUDDocumentPath must be configured.");

        if (string.IsNullOrWhiteSpace(InventoryDocumentPath))
            Debug.LogError("UIManager: InventoryDocumentPath must be configured.");

        ApplyPresentation();
    }

    public override void OnUpdate(float deltaTime)
    {
        // Scene and RML initialization can change cursor capture after OnCreate.
        // Reapply the initial gameplay state once the scene is fully live.
        var label = Session?.IsNeutralSpectator == true ? "SWITCH VIEWED TEAM" : "SWITCH TEAM";
        if (_isPaused && _pauseDocument is not null && label != _switchTeamLabel && _pauseDocument.Element("switch-team").SetClass("ready", true))
        { _pauseDocument.Element("switch-team").Markup = label; _switchTeamLabel = label; }
        if (_needsInitialPresentation)
        {
            _needsInitialPresentation = false;
            ApplyPresentation();
        }

        // D-pad right opens inventory in gameplay. Once open it navigates cards;
        // B backs out, while the existing I/B keyboard shortcut still toggles it.
        if (CoD.Scripts.GameInput.Pressed("Inventory") && !_isPaused &&
            (!_isInventoryOpen || !CoD.Scripts.GameInput.ControllerPressed("Inventory")))
        {
            SetInventoryOpen(!_isInventoryOpen);
            return;
        }

        if (CoD.Scripts.GameInput.Pressed("Pause"))
        {
            if (_confirmation?.IsOpen == true) { _confirmation.Close(); return; }
            if (_settings?.IsOpen == true && !CoD.Scripts.GameInput.ControllerPressed("Pause"))
            { _settings.Close(); return; }
            if (_isInventoryOpen)
                SetInventoryOpen(false);
            else
                SetPaused(!_isPaused);
            return;
        }
        if (!_isPaused) return;
        _controllerMenu?.RefreshHints();
        var settingsOpen = _settings?.IsOpen == true || _confirmation?.IsOpen == true;
        if (settingsOpen && !_settingsWasOpen) _controllerMenu?.Close();
        if (!settingsOpen && _settingsWasOpen) _controllerMenu?.Open();
        _settingsWasOpen = settingsOpen;
        var frame = CoD.Scripts.MenuFrame.Read();
        if (_confirmation?.IsOpen == true) _confirmation.Update(frame);
        else if (settingsOpen) _settings?.Update(frame);
        else _controllerMenu?.Update(frame);
    }

    public void SetPaused(bool isPaused)
    {
        if (_isPaused == isPaused)
        {
            ApplyPresentation();
            return;
        }

        if (isPaused)
        {
            _isInventoryOpen = false;
            // Multiplayer must keep simulation and network polling running.
            // The menu still releases the cursor, which blocks player input.
            if (!CoD.Scripts.MultiplayerSession.IsMultiplayerActive)
            {
                _previousTimeScale = GamePause.TimeScale;
                GamePause.TimeScale = 0.0f;
                _ownsTimePause = true;
            }
        }
        else
        {
            RestoreTimeScale();
        }

        _isPaused = isPaused;
        _settingsWasOpen = false;
        if (isPaused) _controllerMenu?.Open("resume");
        else { _settings?.Close(); _confirmation?.Close(); _controllerMenu?.Close(); }
        ApplyPresentation();
    }

    public void SetInventoryOpen(bool isOpen)
    {
        if (isOpen && Session?.IsNeutralSpectator == true) return;
        if (_isPaused || _isInventoryOpen == isOpen)
            return;

        // Keep UI/input updates running normally while the inventory is open.
        // Player input is already disabled by the unlocked cursor.
        _isInventoryOpen = isOpen;
        GameObject.Find("Player")?.GetComponent<CoD.Scripts.PlayerController>()?.PlayGameplayCue(
            isOpen ? "Misc/inventory_open_ufx_1.wav" : "Misc/inventory_close_ufx_1.wav", .45f);
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        SetWidgetVisible(_pauseWidget, _isPaused);
        if (_isPaused)
            _pauseDocument?.Show();
        else
        {
            _pauseDocument?.Element("settings-overlay").SetStyle("display", "none");
            _pauseDocument?.Hide();
        }

        SetWidgetVisible(_inventoryWidget, _isInventoryOpen);

        var gameplayVisible = !_isPaused && !_isInventoryOpen;
        CoD.Scripts.GameInput.SetMenuCapture(!gameplayVisible);
        SetWidgetVisible(_hudWidget, gameplayVisible);
        if (!gameplayVisible)
            _hudDocument?.Hide();
        else
            _hudDocument?.Show();

        Input.CursorLocked = gameplayVisible;
    }

    public override void OnDestroy()
    {
        RestoreTimeScale();

        _pauseDocument?.Dispose();
        _pauseDocument = null;
        _hudDocument?.Dispose();
        _hudDocument = null;
    }

    private void RestoreTimeScale()
    {
        if (!_ownsTimePause)
            return;

        GamePause.TimeScale = _previousTimeScale;
        _ownsTimePause = false;
    }

    private static void SetWidgetVisible(RmlWidgetComponent? widget, bool visible)
    {
        if (widget == null)
            return;

        widget.Enabled = true;
        widget.Visible = visible;

        if (visible)
            widget.Document.Show();
        else
            widget.Document.Hide();
    }

    public void Resume()
    {
        SetPaused(false);
    }

    public void Restart()
    {
        SetPaused(false);
        ShutdownMultiplayer();
        SceneManager.LoadScene(SceneManager.GetActiveScene().Name);
    }

    public void SwitchTeam()
    {
        var session = GameObject.Find("Player")?.GetComponent<CoD.Scripts.MultiplayerSession>();
        if (session is null)
        {
            Debug.LogWarning("Cannot switch teams outside a multiplayer match.");
            return;
        }

        session.RequestTeamSwitch();
        SetPaused(false);
    }

    public void ReturnToMainMenu()
    {
        SetPaused(false);
        ShutdownMultiplayer();
        SceneManager.LoadScene(MainMenuScene);
    }

    public void Quit()
    {
        SetPaused(false);
        ShutdownMultiplayer();
        Application.Quit();
    }

    private static void ShutdownMultiplayer()
        => GameObject.Find("Player")
            ?.GetComponent<CoD.Scripts.MultiplayerSession>()
            ?.Shutdown();
}
