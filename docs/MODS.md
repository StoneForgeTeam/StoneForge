# Writing a mod (0.1.0)

A mod is a folder in `Stoneshard\mods`: its `mod.json`, its C# source (`*.cs`, in any subfolders), its pictures and sounds in `Assets\`, and optionally its GML in `GML\` (see [GML.md](GML.md)). StoneForge compiles and checks the source when the game starts. A folder holds one mod.

## mod.json

```json
{
  "id": "examplemod",
  "name": "Example Mod",
  "version": "1.0.0",
  "author": "you",
  "description": "What it does, in a sentence or two.",
  "stoneforge": "0.1.0"
}
```

| Key | | |
|---|---|---|
| `id` | required | The mod's permanent ID: lowercase letters and digits, single underscores between them (`examplemod`, `yourname_examplemod`). Its settings, whether it's switched on, and its content are known by it - don't change it once players have the mod. |
| `name` | required | Shown in the Mods window, its log lines and "Mod: ..." on its items. |
| `version` | required | Your mod's version. |
| `author`, `description` | optional | Shown in the Mods window. |
| `stoneforge` | optional | The StoneForge version it needs, at least (`"0.1.0"`). An older StoneForge doesn't load it and says so. |

Without a valid `mod.json` the folder isn't loaded; the Mods window says what's wrong. Two folders with the same `id`: the second isn't loaded. Code reads the manifest as `context.Manifest`.

## The mod class

Mod editor projects target `net10.0` and reference the installed `dotnet/StoneForge.API.dll`. The standalone [ExampleMod repository](https://github.com/StoneForgeTeam/ExampleMod) also loads the installed GML generator. Set `STONESHARD_DIR` to your game folder or pass `-p:StoneForgeSdkDir="C:\path\to\Stoneshard\dotnet"` when building it.

```csharp
using StoneForge;

namespace ExampleMod;

public class ExampleMod : IStoneMod
{
    public void Load(ModContext context) => context.Log("Hello from " + context.Name);
    public void Unload() { }
}
```

One public `IStoneMod` class per folder. Its name, version and so on come from `mod.json`.

## Content IDs

Items, consumables, buffs and skills are created with a short key, and StoneForge puts the mod's ID in front of it, so two mods can use the same key without clashing:

```csharp
public class Tonic : Wine
{
    public Tonic() : base("tonic") { DisplayName = "Example Tonic"; }
}
// context.Items.Add(tonic): tonic.Id is "examplemod:tonic"
```

| | |
|---|---|
| `Key` | `"tonic"`: what you wrote. Keys can't start with `_` or contain `:`. |
| `Id` | `"examplemod:tonic"`: how code refers to it, including other mods (`Items.Give("examplemod:Example Blade")`, `RequireSkill("othermod:bolt")`). |
| in the game's data | `examplemod__tonic`: its objects (`o_inv_examplemod__tonic`), table rows and saves. |

A mod's ID has no `__` and keys don't start with `_`, so no two mods' content can ever get the same name in the game. Keys (and the mod's ID) are what saves refer to: changing them loses that content from existing saves.
