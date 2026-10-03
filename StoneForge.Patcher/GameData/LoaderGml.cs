using System.IO;

namespace StoneForge.Patcher;

/// <summary>The loader's own GML: the GML folder beside the exe (ModsWindow\, Gui\, Items\, Fx\, Buffs\,
/// Input\), as the patches add it to the game data.</summary>
internal static class LoaderGml
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "GML");

    /// <summary>A file's GML, by its path in the folder ("ModsWindow/stonemod_menu_create.gml"). (Its "/" made the
    /// system's separator: a long install path is opened as a \\?\ path, which takes no "/".)</summary>
    public static string Read(string file)
        => File.ReadAllText(Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar))).Replace("\r\n", "\n");

    /// <summary>Every file, by path and content (for the rebuild key: an edit to any means a rebuild).</summary>
    public static string Contents()
        => string.Concat(Directory.GetFiles(Dir, "*.gml", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Dir, f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => f + File.ReadAllText(Path.Combine(Dir, f))));
}
