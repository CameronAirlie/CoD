using System.Numerics;
using System.Text;

namespace CoD.Scripts;

/// <summary>Small presentation helpers, independent of scene, input and gameplay state.</summary>
public static class TacticalUi
{
    public static int Heading(Vector3 forward)
    {
        if (!float.IsFinite(forward.X) || !float.IsFinite(forward.Z)) return 0;
        return ((int)MathF.Round(MathF.Atan2(forward.X, -forward.Z) * 180 / MathF.PI) + 360) % 360;
    }
    public static string CompassTicks(int heading)
    {
        string[] directions = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        var centre = (int)MathF.Round(heading / 15f) * 15;
        var markup = new StringBuilder();
        for (var offset = -2; offset <= 2; offset++)
        {
            var degrees = (centre + offset * 15 + 360) % 360;
            var label = degrees % 45 == 0 ? directions[degrees / 45] : degrees.ToString("000");
            markup.Append($"<span class=\"compass-tick\">{label}</span>");
        }
        return markup.ToString();
    }
}
