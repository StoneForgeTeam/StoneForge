using System.Text.Json.Nodes;

namespace StoneForge;

/// <summary>A ground item's hop through the air (<see cref="GroundItem.Flight"/>, <see cref="GroundItem.Fly"/>): where it
/// is, the tile it's landing on, its speeds, gravity, the height it's drawn at (a weapon's) and its spin.</summary>
public readonly record struct ItemFlight(double X, double Y, double TargetX, double TargetY, double HSpeed, double VSpeed,
    double Gravity, double DrawY, double Angle)
{
    public string ToJson() => new JsonObject
    {
        ["x"] = X, ["y"] = Y, ["tx"] = TargetX, ["ty"] = TargetY,
        ["hs"] = HSpeed, ["vs"] = VSpeed, ["g"] = Gravity, ["yy"] = DrawY, ["a"] = Angle,
    }.ToJsonString();

    /// <summary>A flight from <see cref="ToJson"/>'s text; null if it isn't one.</summary>
    public static ItemFlight? FromJson(string json)
    {
        if (GmJson.Parse(json) is not JsonObject o)
            return null;
        double? N(string name) => o[name] is JsonValue v && v.TryGetValue(out double d) ? d : null;
        if (N("x") is not { } x || N("y") is not { } y || N("tx") is not { } tx || N("ty") is not { } ty
            || N("hs") is not { } hs || N("vs") is not { } vs || N("g") is not { } g)
            return null;
        return new ItemFlight(x, y, tx, ty, hs, vs, g, N("yy") ?? y, N("a") ?? 0);
    }
}
