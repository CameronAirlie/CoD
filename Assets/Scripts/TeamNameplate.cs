namespace CoD.Scripts;

internal static class TeamNameplate
{
    public static string Format(string username)
    {
        var escaped = username.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;");
        return escaped;
    }
}
