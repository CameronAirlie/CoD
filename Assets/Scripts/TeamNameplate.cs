namespace CoD.Scripts;

internal static class TeamNameplate
{
    public static string Format(string username, bool friendly)
    {
        var escaped = username.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;");
        return $"{(friendly ? "FRIENDLY" : "ENEMY")} | {escaped}";
    }
}
