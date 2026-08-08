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

    public override void OnCreate()
    {
        Input.CursorLocked = false;
        var widget = GameObject.GetComponent<RmlWidgetComponent>();
        if (widget is null)
        {
            Debug.LogError("TitleScreen requires an RmlWidgetComponent.");
            return;
        }

        if (string.IsNullOrWhiteSpace(widget.Source))
            widget.Source = documentPath;
        _document = widget.Document;
        _usernameInput = _document.Element("username");
        _addressInput = _document.Element("address");
        _statusLabel = _document.Element("status");
        _usernameInput["value"] = username;
        _addressInput["value"] = serverAddress;
        _usernameInput.On("input", SyncFields);
        _addressInput.On("input", SyncFields);
        _usernameInput.On("change", SyncFields);
        _addressInput.On("change", SyncFields);
        _document.OnClick("host", () => Launch("Host"));
        _document.OnClick("join", () => Launch("Client"));
        SetStatus("Enter a username, then host or join a game.");
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_loading)
            return;

        // Text entry, caret movement, and Tab focus traversal are handled by
        // RmlUi while a form control has keyboard focus.
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

        MultiplayerLaunch.Configure(launchMode, cleanName, serverAddress);
        _loading = SceneManager.LoadScene(gameScene);
        SetStatus(_loading
            ? launchMode == "Host" ? "Starting host..." : "Joining game..."
            : "Could not load the game scene.");
    }

    private void SetStatus(string message)
    {
        if (_statusLabel is not null)
            _statusLabel.Markup = message;
    }
}
