using StoneForge.Patcher;

public sealed class RuntimePreflightTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sf-runtime-" + Guid.NewGuid().ToString("N"));

    public RuntimePreflightTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("10.0.11", null, false)]
    [InlineData(null, "10.0.11", false)]
    [InlineData("10.0.11", "9.0.9", false)]
    [InlineData("10.0.11", "10.0.0-preview.1", false)]
    [InlineData("10.0.11", "10.0.11", true)]
    public void Msl_requires_both_core_and_desktop_version_ten(string? core, string? desktop, bool supported)
    {
        if (core != null) Directory.CreateDirectory(Path.Combine(_root, "shared", "Microsoft.NETCore.App", core));
        if (desktop != null) Directory.CreateDirectory(Path.Combine(_root, "shared", "Microsoft.WindowsDesktop.App", desktop));
        if (supported) Preflight.RequireMslRuntime(_root);
        else
        {
            var error = Assert.Throws<InvalidOperationException>(() => Preflight.RequireMslRuntime(_root));
            Assert.Contains("Windows Desktop Runtime (x64)", error.Message);
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
