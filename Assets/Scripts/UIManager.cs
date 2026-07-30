using PlutoGE.ScriptCore;

public sealed class UIManager : ScriptBehaviour
{
    [SerializedField] private GameObject? PauseMenu = null;
    [SerializedField] private GameObject? HUD = null;
    [SerializedField] private string PauseDocumentPath = "UI/pause-menu.rml";
    [SerializedField] private string HUDDocumentPath = "UI/hud.rml";
    [SerializedField] private string MainMenuScene = "Title";

    private RmlWidgetComponent? _pauseWidget;
    private RmlWidgetComponent? _hudWidget;
    private RmlDocument? _pauseDocument;
    private RmlDocument? _hudDocument;
    private bool _isPaused;
    private float _previousTimeScale = 1.0f;

    public bool IsPaused => _isPaused;

    public override void OnCreate()
    {
        _pauseWidget = PauseMenu?.GetComponent<RmlWidgetComponent>();
        _hudWidget = HUD?.GetComponent<RmlWidgetComponent>();

        if (_pauseWidget == null)
            Debug.LogError("UIManager: PauseMenu must reference an RmlWidgetComponent.");
        if (_hudWidget == null)
            Debug.LogError("UIManager: HUD must reference an RmlWidgetComponent.");

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

        ApplyPresentation();
    }

    public override void OnUpdate(float deltaTime)
    {
        if (Input.IsKeyPressed(KeyCode.Escape))
            SetPaused(!_isPaused);
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

    private void ApplyPresentation()
    {
        SetWidgetVisible(_pauseWidget, _isPaused);
        if (_isPaused)
            _pauseDocument?.Show();
        else
            _pauseDocument?.Hide();

        SetWidgetVisible(_hudWidget, !_isPaused);
        if (_isPaused)
            _hudDocument?.Hide();
        else
            _hudDocument?.Show();

        Input.CursorLocked = !_isPaused;
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
