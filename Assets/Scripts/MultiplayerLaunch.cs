namespace CoD.Scripts;

/// <summary>Launch settings carried from the title screen into the game scene.</summary>
public static class MultiplayerLaunch
{
    public static string Mode { get; private set; } = "Offline";
    public static string Username { get; private set; } = "Player";
    public static string ServerAddress { get; private set; } = "127.0.0.1";

    public static void Configure(string mode, string username, string serverAddress)
    {
        Mode = mode;
        Username = string.IsNullOrWhiteSpace(username) ? "Player" : username.Trim();
        ServerAddress = string.IsNullOrWhiteSpace(serverAddress)
            ? "127.0.0.1"
            : serverAddress.Trim();
    }
}
