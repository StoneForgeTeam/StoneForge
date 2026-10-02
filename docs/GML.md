# Mod-owned GML and generated C# bindings (0.1.0)

Put GML functions in a mod's `GML` folder (the mod itself: see [MODS.md](MODS.md)). StoneForge compiles them into `data.win` when the game starts, and a Roslyn source generator gives the mod's C# a class to call them with. The same generator works in Visual Studio. GML changes need a full game restart; the mod's C# still hot-reloads.

## Calling your GML from C#

The bindings are named after the mod's folder. A mod in `mods\ExampleMod` with `GML\Add.gml`:

```gml
/// @stoneforge return double
/// @stoneforge param left double
/// @stoneforge param right double
function Add(left, right)
{
    return left + right;
}
```

gets a generated class `ExampleMod.Gml`:

```csharp
namespace ExampleMod;

public class ExampleMod : IStoneMod
{
    public void Load(ModContext context)
    {
        // Inside Load, Tick or another game-thread callback:
        context.Log("20 + 22 = " + Gml.Add(20, 22));
    }
    ...
}
```

Inside the mod's own namespace that's just `Gml.Add(...)`; from another namespace, `ExampleMod.Gml.Add(...)`. A folder name that isn't a C# name is turned into one: `mods\my-cool mod` gives `MyCoolMod.Gml`. Don't name a class of your own `Gml` in that namespace; the generator reports the clash.

There's nothing else to set up: no manifest, no IDs or versions. A mod's GML can only be called from that mod.

## Supported GML

- One named function per `.gml` file, anywhere under `GML\` (subfolders are fine; don't name one `GML`). No top-level code before or after it.
- Plain named parameters, each with a type annotation; a return annotation is required. No optional/default or variadic parameters.
- Types: `int`, `double`, `bool`, `string`, `GmValue`; the return type may also be `void`. The annotations define the C# signature; GML itself is dynamically typed, so return values must match. Conversions follow `GmValue`.
- Function and parameter names must be valid plain C# identifiers. Duplicate function names are rejected.
- Ordinary quoted strings and comments are supported. Verbatim/interpolated strings are rejected.
- Parameters are compiled as `argument0`, `argument1`... This compatibility rewrite is retained with UndertaleModLib 0.9.2.0; write parameters by name as usual and don't also declare a local with a parameter's name. Simplifying the rewrite for the new compiler has not been validated.
- Functions in the same mod can call each other by name, in any file order: StoneForge compiles each after the ones it calls. A function may call itself, but functions that call each other in a loop (`A` calls `B` calls `A`) are rejected. Names are rewritten to a stable internal name derived from the mod folder's name; strings and member access (`obj.Add`) are left alone, so don't look your functions up by name with `asset_get_index`.
- Calls from C# run with global self. Pass positions, IDs and values in explicitly. A function can loop over instances itself to do a batch of work in one call.
- C# calls GML; GML can't call back into C#.

## Visual Studio / editor setup

For a mod at `<game>\mods\<mod>`, its `.csproj`:

Target `net10.0` in the project's `PropertyGroup` and install the .NET 10 SDK for editor builds. The source generator DLL remains `netstandard2.0` for compiler-host compatibility.

```xml
<ItemGroup>
  <Reference Include="StoneForge.API">
    <HintPath>..\..\dotnet\StoneForge.API.dll</HintPath>
    <Private>false</Private>
  </Reference>
  <Analyzer Include="..\..\dotnet\StoneForge.GmlGenerator.dll" />
  <AdditionalFiles Include="GML\**\*.gml" />
</ItemGroup>
```

The generator finds the mod folder from the GML files' paths (the folder holding `GML`), so the IDE and the game name the bindings the same way.

## Warning and switching off

The Mods page shows, in yellow: *This mod uses GML bindings and can bypass StoneForge's security. Its GML can't be hot-reloaded: changes need a restart of the game. Use at your own discretion.* The GML runs directly inside GameMaker, outside StoneForge's C# restrictions. Preparation and loading log the same warning. GML can affect game state and files; the C# source-policy check does not sandbox it.

Switching a mod off makes its `Gml` calls refuse. Its compiled GML stays in the current `data.win` until the next start: switching off is not a security boundary, and can't undo what its GML did. On restart the patcher rebuilds the game data whenever any mod's GML changes. Invalid GML leaves the previous data file in place, and bindings that don't match the GML the game was prepared with refuse to run.

What was prepared is read once when the loader starts. Editing GML (or its annotations) while the game runs needs a restart: a C# reload can't call an old GML body through a new signature.

Renaming a mod folder renames its bindings (and its GML's internal names) at the next start.
