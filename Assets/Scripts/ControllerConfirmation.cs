using System.Net;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Leave/restart actions require a deliberate choice; Cancel is selected first.</summary>
public sealed class ControllerConfirmation
{
    private readonly RmlDocument _document;
    private readonly ControllerMenu _menu;
    private Action? _action;
    public bool IsOpen { get; private set; }
    public ControllerConfirmation(RmlDocument document)
    {
        _document = document;
        _menu = new(document) { Back = Close };
        _menu.Add(new("confirm-cancel", 0, 0, Close));
        _menu.Add(new("confirm-accept", 0, 1, Accept));
    }
    public void Open(string title, string message, Action action)
    {
        _action = action; IsOpen = true;
        _document.Element("confirm-title").Markup = WebUtility.HtmlEncode(title);
        _document.Element("confirm-message").Markup = WebUtility.HtmlEncode(message);
        _document.Element("confirm-overlay").SetStyle("display", "flex");
        _menu.Open("confirm-cancel");
    }
    public void Update(MenuFrame frame) { if (IsOpen) _menu.Update(frame); }
    public void Close()
    {
        IsOpen = false; _action = null; _menu.Close();
        _document.Element("confirm-overlay").SetStyle("display", "none");
    }
    private void Accept() { var action = _action; Close(); action?.Invoke(); }
}
