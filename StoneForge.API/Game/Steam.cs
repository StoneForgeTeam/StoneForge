namespace StoneForge;

/// <summary>Steam, as the game sees it.</summary>
public static class Steam
{
    /// <summary>The player's Steam name ("" without Steam).</summary>
    public static string PersonaName
    {
        get
        {
            try { return Game.CallBuiltinTrusted("steam_get_persona_name", default, default).AsString; }
            catch (Exception) { return ""; }
        }
    }
}
