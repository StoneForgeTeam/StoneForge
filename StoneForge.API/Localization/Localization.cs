using System.Globalization;

namespace StoneForge;

/// <summary>StoneForge's built-in text and the language selected in Stoneshard. English (US) is the fallback.</summary>
public static class Localization
{
    /// <summary>The default language for StoneForge and mod translations.</summary>
    public const string DefaultLanguage = "en-US";
    /// <summary>The current game language as a culture name, such as en-US or ru-RU.</summary>
    public static string Language { get; private set; } = DefaultLanguage;
    internal static int Revision { get; private set; }
    private static readonly TextCatalog Catalog = new(locale =>
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream("StoneForge.Localization." + locale + ".json");
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }, message => Game.Log("Localization: " + message));

    /// <summary>Gets a built-in translation, formatting numbered placeholders with the supplied arguments.
    /// Missing entries fall back to US English, then the key.</summary>
    public static string Get(string key, params object?[] arguments) => Catalog.Get(Language, key, arguments);

    internal static string GameLanguage(int value) => value switch
    {
        1 => "ru-RU", 2 => "en-US", 3 => "zh-CN", 4 => "de-DE", 5 => "es-419", 6 => "fr-FR",
        7 => "it-IT", 8 => "pt-BR", 9 => "pl-PL", 10 => "tr-TR", 11 => "ja-JP", 12 => "ko-KR",
        _ => DefaultLanguage,
    };

    internal static void RefreshFromGame()
    {
        var value = Game.Global["language"];
        SetLanguage(value.Kind == GmKind.Real ? GameLanguage(value.AsInt) : DefaultLanguage);
    }

    internal static void SetLanguage(string language)
    {
        language = CultureInfo.GetCultureInfo(language).Name;
        if (Language == language) return;
        Language = language;
        Revision++;
    }
}
