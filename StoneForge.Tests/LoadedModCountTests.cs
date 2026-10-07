using StoneForge;
using StoneForge.Loader;

public sealed class LoadedModCountTests
{
    [Fact]
    public void Counts_applied_packages_but_not_disabled_packages_or_discovered_CSharp_mods()
    {
        int before = ModManager.LoadedCount;
        var applied = new ModInfo("count-applied", "Applied", "", "", "", true, "", IsSml: true);
        var disabled = new ModInfo("count-disabled", "Disabled", "", "", "", false, "", IsSml: true);
        var discovered = new ModInfo("count-csharp", "Discovered", "", "", "", true, "");
        try
        {
            ModRegistry.All.Add(applied);
            ModRegistry.All.Add(disabled);
            ModRegistry.All.Add(discovered);
            Assert.Equal(before + 1, ModManager.LoadedCount);
        }
        finally
        {
            ModRegistry.All.Remove(applied);
            ModRegistry.All.Remove(disabled);
            ModRegistry.All.Remove(discovered);
        }
    }
}
