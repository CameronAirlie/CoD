using System.Numerics;
using PlutoGE.ScriptCore.Gameplay;

namespace CoD.Scripts;

/// <summary>
/// CoD map policy on top of the engine's Gameplay map types: contact styles and priorities,
/// which weapons are loud, and the rule that enemy facing is never shown.
/// </summary>
public static class TacticalMap
{
    public const string SelfStyle = "self", FriendlyStyle = "friendly", EnemyStyle = "enemy";

    /// <summary>Unsuppressed gunfire reveals its shooter on the enemy team's map.</summary>
    public static bool RevealsShooter(WeaponDefinition? weapon) => weapon?.Suppressed != true;

    public static MapContact Self(int id, Vector3 position, float heading) => new(id, SelfStyle, position, heading, 1, 0);
    public static MapContact Friendly(int id, Vector3 position, float heading) => new(id, FriendlyStyle, position, heading, 1, 1);

    /// <summary>Heard enemies carry no heading, whatever the caller knows.</summary>
    public static MapContact Enemy(int id, Vector3 position, float opacity) => new(id, EnemyStyle, position, float.NaN, opacity, 2);
}
