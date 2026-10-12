namespace CoD.Scripts;

/// <summary>Launch settings carried from the title screen into the game scene.</summary>
public static class MultiplayerLaunch
{
    public static string Mode { get; private set; } = "Offline";
    public static string Username { get; private set; } = "Player";
    internal static MatchMode? ServerMatchMode { get; private set; }
    internal static void SetServerMatch(MatchMode mode) => ServerMatchMode = mode;
    public static int ServerPort { get; private set; } = 7777;
    public static string ServerAddress { get; private set; } = "127.0.0.1";

    public static bool Configure(string mode, string username, string serverAddress)
    {
        var address = string.IsNullOrWhiteSpace(serverAddress) ? "127.0.0.1" : serverAddress.Trim();
        var port = 7777;
        if (mode == "Client")
        {
            if (!Uri.TryCreate("tcp://" + address, UriKind.Absolute, out var endpoint) ||
                endpoint.Host.Length == 0 || endpoint.UserInfo.Length != 0 || endpoint.AbsolutePath != "/" ||
                endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.Port == 0) return false;
            address = endpoint.DnsSafeHost;
            if (endpoint.Port > 0) port = endpoint.Port;
        }
        ServerMatchMode = null;
        Mode = mode;
        Username = string.IsNullOrWhiteSpace(username) ? "Player" : username.Trim();
        ServerAddress = address; ServerPort = port;
        return true;
    }
}
