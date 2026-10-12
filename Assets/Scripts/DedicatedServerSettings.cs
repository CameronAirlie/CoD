namespace CoD.Scripts;

/// <summary>Process-local configuration supplied by the dedicated launcher, never by peers.</summary>
internal static class DedicatedServerSettings
{
    public static bool Enabled => Environment.GetEnvironmentVariable("PLUTOGE_HEADLESS") == "1";
    public static void Apply(MultiplayerSession session)
    {
        if (!Enabled) return;
        session.ConfigureDedicated(
            Read("COD_SERVER_PORT", 7777, 1, 65535),
            Read("COD_SERVER_PLAYERS", 12, 1, 128),
            Read("COD_SERVER_BOTS", 8, 0, 128),
            Environment.GetEnvironmentVariable("COD_SERVER_MODE") ?? "Hardpoint");
    }
    private static int Read(string key, int fallback, int min, int max)
    {
        var text = Environment.GetEnvironmentVariable(key);
        if (text is null) return fallback;
        if (!int.TryParse(text, out var value) || value < min || value > max)
            throw new ArgumentException($"{key} must be between {min} and {max}.");
        return value;
    }
}
