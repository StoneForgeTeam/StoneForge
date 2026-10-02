using System.Reflection;

namespace StoneForge.Loader;

/// <summary>StoneForge's version, as shown: "0.1.0" (Directory.Build.props' Version, without the build's
/// "+commit" suffix).</summary>
internal static class LoaderVersion
{
    public static string Text { get; } =
        typeof(LoaderVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(LoaderVersion).Assembly.GetName().Version?.ToString(3) ?? "?";
}
