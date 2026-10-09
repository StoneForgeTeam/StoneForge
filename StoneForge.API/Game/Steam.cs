namespace StoneForge;

/// <summary>Steam, as the game sees it.</summary>
public static class Steam
{
    /// <summary>The logged-in player's 32-bit Steam account ID; zero when Steam is unavailable.</summary>
    public static uint AccountId
    {
        get
        {
            try
            {
                if (!Game.CallBuiltinTrusted("steam_initialised", default, default).AsBool) return 0;
                var value = Game.CallBuiltinTrusted("steam_get_user_account_id", default, default);
                double id = value.AsReal;
                return value.Kind == GmKind.Real && id > 0 && id <= uint.MaxValue && id == Math.Truncate(id) ? (uint)id : 0;
            }
            catch (Exception) { return 0; }
        }
    }
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
