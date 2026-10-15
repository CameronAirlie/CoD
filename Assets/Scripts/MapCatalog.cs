namespace CoD.Scripts;

/// <summary>A playable scene and the presentation used by the title screen.</summary>
public sealed record MapInfo(string Scene, string DisplayName, string Brief);

/// <summary>Single source of truth for which maps each game mode can be hosted on.</summary>
public static class MapCatalog
{
    private static readonly MapInfo Arena = new("Main", "ARENA", "Open courtyard with three rotating zones.");
    private static readonly MapInfo Shipment = new("Shipment", "SHIPMENT", "Tight container yard. Constant contact.");
    private static readonly MapInfo Standoff = new("Standoff", "STANDOFF", "Border village under a burning church tower.");
    private static readonly MapInfo Foundry = new("Foundry", "FOUNDRY", "Industrial compound with two bomb sites.");
    private static readonly MapInfo Terminal = new("Terminal", "TERMINAL", "Airport concourse, apron and aircraft.");

    private static readonly MapInfo[] HardpointMaps = [Arena, Shipment, Standoff];
    private static readonly MapInfo[] DefusalMaps = [Foundry, Terminal];

    /// <summary>Maps available to a mode; the first entry is the mode's default.</summary>
    public static IReadOnlyList<MapInfo> For(MatchMode mode) => mode == MatchMode.Defusal ? DefusalMaps : HardpointMaps;

    public static IReadOnlyList<MapInfo> For(string mode) => For(ParseMode(mode));

    public static MatchMode ParseMode(string mode) =>
        mode.Equals("Defusal", StringComparison.OrdinalIgnoreCase) ? MatchMode.Defusal
        : mode.Equals("TeamDeathmatch", StringComparison.OrdinalIgnoreCase) ? MatchMode.TeamDeathmatch : MatchMode.Hardpoint;

    public static MapInfo? Find(MatchMode mode, string? scene) =>
        For(mode).FirstOrDefault(map => map.Scene.Equals(scene, StringComparison.OrdinalIgnoreCase));

    public static bool Supports(MatchMode mode, string? scene) => Find(mode, scene) is not null;

    /// <summary>Every scene any mode may load; clients reject a host advertising anything else.</summary>
    public static bool IsKnownScene(string? scene) =>
        HardpointMaps.Concat(DefusalMaps).Any(map => map.Scene.Equals(scene, StringComparison.Ordinal));

    public static string Describe(MatchMode mode) => string.Join(", ", For(mode).Select(map => map.Scene));
}
