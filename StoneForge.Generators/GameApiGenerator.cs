using Microsoft.CodeAnalysis;

namespace StoneForge.Generators;

// The typed, game-specific part of StoneForge's API, from the game's data as StoneForge.DataDump dumped it
// (StoneForge.API\obj\GameData\*.tsv / *.txt, given to the compiler as AdditionalFiles). A pipeline per file, so editing one
// regenerates only what's made from it:
//   assets.tsv  -> Assets.g.cs       (AssetsSource):      enums GameObject, Sprite, Sound, Room.
//   scripts.tsv -> Scripts.g.cs      (ScriptsSource):     Scripts.* - every GML script.
//   objects.tsv -> Objects.g.cs      (ObjectsSource):     StoneForge.Objects - a class per object.
//   events.tsv  -> Events.g.cs       (EventsSource):      Events.<object>.<event>.
//   damage_types.tsv -> DamageTypes.g.cs (DamageTypesSource): StoneForge.GameDamageTypes - each kind of damage, to
//               deal or inherit; DamageType.Shock...
//   weapons.txt / armor.txt (ItemTable)
//               -> ItemColumns.g.cs  (ItemColumnsSource): WeaponColumn / ArmorColumn.
//               -> GameItems.g.cs    (GameItemsSource):   StoneForge.GameItems - every item, to inherit.
[Generator]
public sealed class GameApiGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider
            .Select((file, ct) => (Name: System.IO.Path.GetFileName(file.Path).ToLowerInvariant(), Text: file.GetText(ct)?.ToString() ?? ""));

        // A data file's text (null: it isn't there).
        IncrementalValueProvider<string?> File(string name) => files.Where(f => f.Name == name).Select((f, _) => f.Text).Collect()
            .Select((texts, _) => texts.IsEmpty ? null : texts[0]);

        void Emit(string data, string output, System.Func<string, string> make) =>
            context.RegisterSourceOutput(File(data), (spc, text) =>
            {
                if (text != null)
                    spc.AddSource(output, make(text));
            });

        Emit("assets.tsv", "Assets.g.cs", AssetsSource.Make);
        Emit("scripts.tsv", "Scripts.g.cs", ScriptsSource.Make);
        Emit("objects.tsv", "Objects.g.cs", ObjectsSource.Make);
        Emit("events.tsv", "Events.g.cs", EventsSource.Make);
        Emit("damage_types.tsv", "DamageTypes.g.cs", DamageTypesSource.Make);

        context.RegisterSourceOutput(File("weapons.txt").Combine(File("armor.txt")).Combine(File("consumables.txt")).Combine(File("objects.tsv")), (spc, t) =>
        {
            var weapons = new ItemTable(t.Left.Left.Left, "Weapon", "weapon");
            var armor = new ItemTable(t.Left.Left.Right, "Armor", "armour");
            var consumables = new ConsumableTable(t.Left.Right, t.Right);
            spc.AddSource("ItemColumns.g.cs", ItemColumnsSource.Make(weapons, armor, consumables));
            spc.AddSource("GameItems.g.cs", GameItemsSource.Make(weapons, armor, consumables));
        });

        context.RegisterSourceOutput(File("skills.tsv").Combine(File("skills_stats.txt")), (spc, t) =>
            spc.AddSource("GameSkills.g.cs", SkillsSource.Make(new SkillTable(t.Left, t.Right))));
    }
}
