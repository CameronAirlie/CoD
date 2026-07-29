using System;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Title screen controller with keyboard-driven username and address fields.</summary>
public sealed class TitleScreen : ScriptBehaviour
{
    [SerializedField] private GameObject? usernameField = null;
    [SerializedField] private GameObject? addressField = null;
    [SerializedField] private GameObject? hostButton = null;
    [SerializedField] private GameObject? joinButton = null;
    [SerializedField] private GameObject? statusText = null;
    [SerializedField] private string gameScene = "Main";
    [SerializedField] private string username = "Player";
    [SerializedField] private string serverAddress = "127.0.0.1";

    private UITextComponent? _usernameLabel;
    private UITextComponent? _addressLabel;
    private UITextComponent? _statusLabel;
    private UIButtonComponent? _usernameButton;
    private UIButtonComponent? _addressButton;
    private UIButtonComponent? _hostButton;
    private UIButtonComponent? _joinButton;
    private bool _editingAddress;
    private bool _loading;

    public override void OnCreate()
    {
        Input.CursorLocked = false;
        _usernameLabel = usernameField?.GetComponent<UITextComponent>();
        _addressLabel = addressField?.GetComponent<UITextComponent>();
        _statusLabel = statusText?.GetComponent<UITextComponent>();
        _usernameButton = usernameField?.GetComponent<UIButtonComponent>();
        _addressButton = addressField?.GetComponent<UIButtonComponent>();
        _hostButton = hostButton?.GetComponent<UIButtonComponent>();
        _joinButton = joinButton?.GetComponent<UIButtonComponent>();
        RefreshFields();
        SetStatus("Enter a username, then host or join a game.");
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_loading)
            return;

        if (_usernameButton?.WasClicked == true)
            _editingAddress = false;
        if (_addressButton?.WasClicked == true)
            _editingAddress = true;
        if (Input.IsKeyPressed(KeyCode.Tab))
            _editingAddress = !_editingAddress;

        ReadTextInput();

        if (_hostButton?.WasClicked == true)
            Launch("Host");
        else if (_joinButton?.WasClicked == true || Input.IsKeyPressed(KeyCode.Enter))
            Launch("Client");
    }

    private void ReadTextInput()
    {
        var value = _editingAddress ? serverAddress : username;
        var maximumLength = _editingAddress ? 253 : 20;

        if (Input.IsKeyPressed(KeyCode.Backspace) && value.Length > 0)
            value = value[..^1];

        if (value.Length < maximumLength)
        {
            var character = ReadCharacter(_editingAddress);
            if (character is not null)
                value += character.Value;
        }

        if (_editingAddress)
            serverAddress = value;
        else
            username = value;
        RefreshFields();
    }

    private static char? ReadCharacter(bool address)
    {
        for (var code = (int)KeyCode.A; code <= (int)KeyCode.Z; code++)
        {
            var key = (KeyCode)code;
            if (!Input.IsKeyPressed(key))
                continue;
            if (address)
                return char.ToLowerInvariant((char)code);
            var shift = Input.IsKeyDown(KeyCode.LeftShift) || Input.IsKeyDown(KeyCode.RightShift);
            return shift ? (char)code : char.ToLowerInvariant((char)code);
        }

        for (var code = (int)KeyCode.D0; code <= (int)KeyCode.D9; code++)
        {
            if (Input.IsKeyPressed((KeyCode)code))
                return (char)code;
        }

        if (Input.IsKeyPressed(KeyCode.Period))
            return '.';
        if (Input.IsKeyPressed(KeyCode.Minus))
            return '-';
        if (address && Input.IsKeyPressed(KeyCode.Semicolon) &&
            (Input.IsKeyDown(KeyCode.LeftShift) || Input.IsKeyDown(KeyCode.RightShift)))
            return ':';
        if (!address && Input.IsKeyPressed(KeyCode.Space))
            return ' ';
        return null;
    }

    private void Launch(string launchMode)
    {
        var cleanName = username.Trim();
        if (cleanName.Length == 0)
        {
            SetStatus("Please enter a username.");
            _editingAddress = false;
            return;
        }
        if (launchMode == "Client" && serverAddress.Trim().Length == 0)
        {
            SetStatus("Please enter the host IP address.");
            _editingAddress = true;
            return;
        }

        MultiplayerLaunch.Configure(launchMode, cleanName, serverAddress);
        _loading = SceneManager.LoadScene(gameScene);
        SetStatus(_loading
            ? launchMode == "Host" ? "Starting host..." : "Joining game..."
            : "Could not load the game scene.");
    }

    private void RefreshFields()
    {
        if (_usernameLabel is not null)
            _usernameLabel.Text = $"{(!_editingAddress ? "> " : string.Empty)}Username: {username}_";
        if (_addressLabel is not null)
            _addressLabel.Text = $"{(_editingAddress ? "> " : string.Empty)}Host IP: {serverAddress}_";
    }

    private void SetStatus(string message)
    {
        if (_statusLabel is not null)
            _statusLabel.Text = message;
    }
}
