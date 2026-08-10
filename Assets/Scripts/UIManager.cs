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
    private bool _isPaused;
    private bool _isInventoryOpen;
    private bool _needsInitialPresentation = true;
    private float _previousTimeScale = 1.0f;

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
            _pauseDocument.OnClick("resume", Resume);
            _pauseDocument.OnClick("restart", Restart);
            _pauseDocument.OnClick("main-menu", ReturnToMainMenu);
            _pauseDocument.OnClick("quit", Quit);
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
        if (_needsInitialPresentation)
        {
            _needsInitialPresentation = false;
            ApplyPresentation();
        }

        if (Input.IsKeyPressed(KeyCode.I) && !_isPaused)
            SetInventoryOpen(!_isInventoryOpen);

        if (Input.IsKeyPressed(KeyCode.Escape))
        {
            if (_isInventoryOpen)
                SetInventoryOpen(false);
            else
                SetPaused(!_isPaused);
        }
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
            _previousTimeScale = GamePause.TimeScale;
            GamePause.TimeScale = 0.0f;
        }
        else
        {
            GamePause.TimeScale = _previousTimeScale;
        }

        _isPaused = isPaused;
        ApplyPresentation();
    }

    public void SetInventoryOpen(bool isOpen)
    {
        if (_isPaused || _isInventoryOpen == isOpen)
            return;

        // Keep UI/input updates running normally while the inventory is open.
        // Player input is already disabled by the unlocked cursor.
        _isInventoryOpen = isOpen;
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        SetWidgetVisible(_pauseWidget, _isPaused);
        if (_isPaused)
            _pauseDocument?.Show();
        else
            _pauseDocument?.Hide();

        SetWidgetVisible(_inventoryWidget, _isInventoryOpen);

        var gameplayVisible = !_isPaused && !_isInventoryOpen;
        SetWidgetVisible(_hudWidget, gameplayVisible);
        if (!gameplayVisible)
            _hudDocument?.Hide();
        else
            _hudDocument?.Show();

        Input.CursorLocked = gameplayVisible;
    }

    public override void OnDestroy()
    {
        if (_isPaused)
            GamePause.TimeScale = _previousTimeScale;

        _pauseDocument?.Dispose();
        _pauseDocument = null;
        _hudDocument?.Dispose();
        _hudDocument = null;
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
        SceneManager.LoadScene(SceneManager.GetActiveScene().Name);
    }

    public void ReturnToMainMenu()
    {
        SetPaused(false);
        SceneManager.LoadScene(MainMenuScene);
    }

    public void Quit()
    {
        SetPaused(false);
        Application.Quit();
    }
}
