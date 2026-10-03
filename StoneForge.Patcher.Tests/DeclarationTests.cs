using StoneForge.Patcher;

// The consumables and skills the patcher gives game objects: read from the mods' source, named by their mod's ID.
public class DeclarationTests
{
    [Fact]
    public void Declared_keys_carry_their_mods_ID_and_folders_without_a_manifest_are_skipped()
    {
        string mods = Path.Combine(Path.GetTempPath(), "StoneForgeDeclarations-" + Guid.NewGuid().ToString("N"));
        try
        {
            Mod(mods, "Tonics", """{ "id": "tonics", "name": "Tonics", "version": "1" }""",
                "class Tonic : Consumable { public Tonic() : base(\"my_tonic\", \"wine\") { } }");
            Mod(mods, "MoreTonics", """{ "id": "more_tonics", "name": "More Tonics", "version": "1" }""",
                "class Tonic : Wine { public Tonic() : base(\"my_tonic\") { } }");
            Mod(mods, "NoManifest", null, "class Bolt : ModSkill { public Bolt() : base(\"bolt\", \"jolt\") { } }");
            var declared = ModClassDeclaration.Declared(mods);
            Assert.Equal(new[] { "more_tonics__my_tonic", "tonics__my_tonic" }, declared.Select(d => d.Key).OrderBy(k => k));
            Assert.Equal("wine", declared.Single(d => d.Key == "tonics__my_tonic").BasedOn);
        }
        finally { Directory.Delete(mods, true); }
    }

    [Fact]
    public void A_passive_is_declared_by_its_key_alone()
    {
        string mods = Path.Combine(Path.GetTempPath(), "StoneForgeDeclarations-" + Guid.NewGuid().ToString("N"));
        try
        {
            Mod(mods, "Storm", """{ "id": "storm", "name": "Storm", "version": "1" }""",
                "class Charge : ModPassive { public Charge() : base(\"static_charge\") { } }");
            var passive = Assert.Single(ModClassDeclaration.Declared(mods));
            Assert.Equal(("storm__static_charge", "ModPassive", (string?)null), (passive.Key, passive.BaseType, passive.BasedOn));
        }
        finally { Directory.Delete(mods, true); }
    }

    private static void Mod(string mods, string folder, string? manifest, string source)
    {
        string dir = Path.Combine(mods, folder);
        Directory.CreateDirectory(dir);
        if (manifest != null)
            File.WriteAllText(Path.Combine(dir, "mod.json"), manifest);
        File.WriteAllText(Path.Combine(dir, "Mod.cs"), source);
    }
}
