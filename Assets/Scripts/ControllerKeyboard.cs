using System.Net;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

public sealed class MenuTextBuffer
{
    public string Text { get; private set; }
    public int Caret { get; private set; }
    public int Limit { get; }
    public MenuTextBuffer(string value, int limit)
    {
        Limit = Math.Max(0, limit);
        Text = value.Length > Limit ? value[..Limit] : value;
        Caret = Text.Length;
    }
    public void Move(int direction) => Caret = Math.Clamp(Caret + direction, 0, Text.Length);
    public void Insert(string value)
    {
        var room = Limit - Text.Length;
        if (room <= 0) return;
        value = value[..Math.Min(value.Length, room)];
        Text = Text.Insert(Caret, value); Caret += value.Length;
    }
    public void Backspace()
    {
        if (Caret <= 0) return;
        Text = Text.Remove(--Caret, 1);
    }
    public void Clear() { Text = ""; Caret = 0; }
}

/// <summary>Small controller keyboard for callsigns and IPv4/IPv6 host addresses.</summary>
public sealed class ControllerKeyboard
{
    private static readonly string[] Rows = ["1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm", ".:-_[]/@"];
    private readonly RmlDocument _document;
    private readonly ControllerMenu _menu;
    private MenuTextBuffer _buffer = new("", 20);
    private Action<string>? _commit;
    private bool _built, _upper, _armed;
    public bool IsOpen { get; private set; }
    public ControllerKeyboard(RmlDocument document)
    {
        _document = document;
        _menu = new(document) { Back = Cancel, Tab = direction => { _buffer.Move(direction); Refresh(); } };
    }
    public void Open(string label, string value, int limit, Action<string> commit)
    {
        IsOpen = true; _armed = false; _upper = false;
        _buffer = new(value, limit); _commit = commit;
        _document.Element("keyboard-overlay").SetStyle("display", "flex");
        _document.Element("keyboard-title").Markup = WebUtility.HtmlEncode(label);
        Build(); Refresh(); _menu.Open("keyboard-key-0-0");
    }
    private void Build()
    {
        if (_built) return;
        var markup = new System.Text.StringBuilder();
        for (var row = 0; row < Rows.Length; row++)
        {
            markup.Append("<div class=\"keyboard-row\">");
            for (var column = 0; column < Rows[row].Length; column++)
                markup.Append($"<button id=\"keyboard-key-{row}-{column}\" class=\"keyboard-key\">{WebUtility.HtmlEncode(Rows[row][column].ToString())}</button>");
            markup.Append("</div>");
        }
        _document.Element("keyboard-keys").Markup = markup.ToString();
        for (var row = 0; row < Rows.Length; row++)
            for (var column = 0; column < Rows[row].Length; column++)
            {
                var character = Rows[row][column].ToString();
                _menu.Add(new($"keyboard-key-{row}-{column}", row, column,
                    () => { _buffer.Insert(_upper ? character.ToUpperInvariant() : character); Refresh(); }));
            }
        _menu.Add(new("keyboard-space", 5, 0, Space));
        _menu.Add(new("keyboard-shift", 5, 2, () => { _upper = !_upper; Refresh(); }));
        _menu.Add(new("keyboard-delete", 5, 4, Delete));
        _menu.Add(new("keyboard-clear", 5, 6, () => { _buffer.Clear(); Refresh(); }));
        _menu.Add(new("keyboard-done", 6, 4, Commit));
        _menu.Add(new("keyboard-cancel", 6, 0, Cancel));
        _built = true;
    }
    public void Update(MenuFrame frame)
    {
        if (!IsOpen) return;
        if (!_armed) { _armed = frame.Neutral; _menu.Update(frame); return; }
        if (frame.Done) { Commit(); return; }
        if (frame.Secondary) Delete();
        if (frame.Space) Space();
        _menu.Update(frame);
    }
    private void Space() { _buffer.Insert(" "); Refresh(); }
    private void Delete() { _buffer.Backspace(); Refresh(); }
    private void Refresh()
    {
        _document.Element("keyboard-value").Markup = WebUtility.HtmlEncode(_buffer.Text[.._buffer.Caret]) +
            "<span class=\"accent\">|</span>" + WebUtility.HtmlEncode(_buffer.Text[_buffer.Caret..]);
        _document.Element("keyboard-length").Markup = $"{_buffer.Text.Length}/{_buffer.Limit}";
        _document.Element("keyboard-shift").SetClass("selected", _upper);
        for (var row = 0; row < Rows.Length; row++)
            for (var column = 0; column < Rows[row].Length; column++)
            {
                var value = Rows[row][column].ToString();
                _document.Element($"keyboard-key-{row}-{column}").Markup = WebUtility.HtmlEncode(_upper ? value.ToUpperInvariant() : value);
            }
    }
    private void Commit() { var commit = _commit; Close(); commit?.Invoke(_buffer.Text); }
    public void Cancel() => Close();
    private void Close()
    {
        IsOpen = false; _commit = null; _menu.Close();
        _document.Element("keyboard-overlay").SetStyle("display", "none");
    }
}
