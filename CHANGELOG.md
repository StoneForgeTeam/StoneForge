# StoneForge changes

## Unreleased

- A mod skill's `Group` can be one of the game's skills-menu sections: `SkillGroup.Weaponry`, `Utility` or `Sorcery`, matched by English name or by the name shown in the game's language. Its tabs then go into that section after the game's own tabs, instead of under a second header of the same name. Other groups still get their own header after the game's, and group names are matched without case.
- `Combat.Damage(target, DamageType.Shock, 12, source)`: damage dealt as the game deals it, through its own damage calculation (its `o_damage_dealer`). The target's protection, armour piercing and resistances apply, the number shows over the target and the combat log records it. With a source, the hit is the source's: who attacked, crimes, kills. Several kinds can be dealt in one hit. `DamageOptions` sets armour piercing, logging and the name in the log. It returns the damage actually done.
- Damage types are classes. The game's 13 are generated from its data (`StoneForge.GameDamageTypes.Shock`..., with `DamageType.Shock` shortcuts and `DamageType.GameTypes`): DataDump writes `damage_types.tsv`, the `<X>_Damage` variables from `scr_damage_init` that have an `<X>_Resistance`. A mod makes its own kind by inheriting one (`class Lightning : Shock`). That kind is dealt as the game's, with its own `Name`, `Modify` (bonuses, its own resistance) and `OnDealt`. `Pure` (the game's `scr_pure_damage`) is the base for kinds the game has no resistance for.
- GML bindings are now a `public static partial class Gml`, generated as one tidily formatted file per GML file (`Add.gml` gives `Add.g.cs`) plus a shared `Gml.g.cs`. A mod can add its own members with a `partial class Gml` of its own.

## 0.1.0 — Initial public release

- C# source mods with manifests, version checks, in-game management and reload support.
- Context APIs for custom items, consumables, buffs, skills, UI and game hooks, with namespaced content IDs.
- Mod-owned GML scripts with generated C# bindings and an explicit in-game warning for GML mods.
- Native game bridge and direct game-data patching without MSL, using pinned UndertaleModLib 0.9.2.0 and Underanalyzer libraries.
- .NET 10 runtime projects, installer/uninstaller and release packaging. Source generators target netstandard2.0 for editor compatibility.
- Game metadata generated from the developer's local Stoneshard installation; game data is not distributed with the source.
- Separate [ExampleMod repository](https://github.com/StoneForgeTeam/ExampleMod) with sample code and assets.

### Known limitations

- Requires Stoneshard's Windows VM modbranch. GML changes require a game restart.
- The ExampleMod reload audit can report a retained assembly. Reload is not yet verified to release all previous mod assemblies.
- Full gameplay, custom skill casting and save/load coverage remains incomplete.
