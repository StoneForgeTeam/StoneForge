# StoneForge changes

## Unreleased

- **A hook on a script that isn't hookable fails at once.** A mod's `Scripts.x.Before(...)` or `OnScript("x", ...)` on a script the game data doesn't hook would never be called, so it throws. The mod's `Load` fails, in the log and on its Mods page, with the attribute to add: `[assembly: HookScript(nameof(Scripts.x))]`.
  - The patcher records the scripts it made hookable (the loader's, and every mod's `[assembly: HookScript]`) in `dotnet\stoneforge-hooks.txt`, which the loader checks against. The game data is rebuilt once to write it.
  - StoneForge's own scripts aren't checked, and neither is anything without that file (an older install).
- **Arrays and structs in C#.** A GameMaker array or struct now reaches C# as itself - `GmValue.AsArray` (`GmArray`) or `AsStruct` (`GmStruct`) - from a built-in's or script's result, an instance or global variable, or a hooked script's arguments. It's the game's own value, by reference: changes through it change the game's, and passing it back passes that one.
  - `GmArray`: `Length`, `array[i]` (get and set), `Push`, `Insert`, `Delete`, `ToArray()`; a new one with `GmArray.Create(length, fill)`, `GmArray.From(values)` or `GmArray.FromJson(text)`.
  - `GmStruct`: `strukt["name"]` (get and set), `Has`, `Remove`, `Names`, `Count`; a new one with `GmStruct.Create()` or `GmStruct.FromJson(text)`.
  - Both have `ToJson()`, and compare equal when they're the same game value.
  - Kept alive for the game's garbage collector while C# holds them (the bridge roots them in a global struct, `__stoneforge_refs`), and let go of once C# doesn't (or at once with `Dispose()`).
  - **Breaking:** an array used to arrive as its text (a string), and a struct as a temporary `Instance`.
  - The native bridge's API is version 3: install the matching StoneForge.Bridge.dll and loader together.
- **Alarms from C#:** `instance.Alarm[n]` (or a typed instance's `Alarm[n]`) reads and sets GameMaker's `alarm[0]` to `alarm[11]` - steps until it goes off, -1 when it's off - through the engine's own accessor. An index outside 0-11 throws.
  - The native bridge's API is version 4 (`GetVarAt` / `SetVarAt`: an element of an instance's indexed engine variable).
- **Off-screen instances.** The game culls what's off screen (ground loot, decorations...): o_cullingController deactivates it into its `deactivatedInstancesList`, where GameMaker's `with` and `instance_exists` don't see it.
  - `Instances.All(obj, includeCulled: true)` lists those too, with the active ones (an object's children as well). There are overloads by `GameObjectId`, object index or a mod's own `GameObject`, and typed (`All<T>`). Every instance listed is kept by its id.
  - `instance.IsCulled`: off screen and deactivated, still in the world. `instance.IsGone`: really left it (destroyed, picked up), where `Exists` is also false for one that's only culled.
  - `instance.Destroy(runDestroyEvent = true)` destroys an instance culled or not. A culled one is first taken out of the controller's list, its cached `deactivatedInstancesListSize` kept in step, and activated: destroyed where it was, the controller would read a destroyed instance. Nothing happens to one already gone.
  - A culled instance's built-ins (`x`, `object_index`, alarms...) can be read: the native bridge finds its pointer by id among the room's deactivated instances (the engine's own id lookup only has the active ones), and `Get` gives undefined if it can't be found. Its own variables can't be read (`Get` gives undefined) or set (`Set` throws) until it's back on screen, so keep a mod's data about it by its id.
  - `Instances.All<T>(obj)` now keeps its instances by id rather than by a pointer only good in the current callback, so the list can be held.
  - What's culled comes from the native bridge in one walk of the room's deactivated instances, which gives every one's id and object type. There can be hundreds or thousands after a room change (decorations too), and they're read once per frame. `IsCulled` is then a lookup, and `All` asks about each object type once. The controller's list is only searched to take one out of it (`Destroy`). The bridge's API is version 5 (`InactiveInstances`): install the matching StoneForge.Bridge.dll and loader together.
- The patcher reads its own GML by the system's path separator. Installed deep enough for Windows' long-path form, it failed to read `GML\Items/...`.
- **Breaking: windows reworked to work with any frame.**
  - `UIWindow` is now only the window: open/close, Escape, the dimmed screen and input blocking as before, a frame and a close button.
    - The frame is any sprite (`FrameSprite`; the game's version for the resolution unless `AdaptiveSprite` is off), drawn at its size. Or 9-sliced to `FrameWidth` x `FrameHeight` with `Slice` borders, or a plain panel with no sprite.
    - `Content` is the frame less `ContentInsets`, emptied and sized on each open; everything goes there.
    - `CloseButton` is an element you can move or hide. `Title` has `TitleX`/`TitleY`, and `DimBackground` turns the dimming off.
    - `OnFit()` adjusts insets and positions for the resolution.
  - No tabs, page or button slots any more. They're layout pieces usable anywhere:
    - `UITabStrip`: a column (scrolling when full) or a row of `UITab`s, with `TabOpened` and any tab sprite. `UITab.Window` becomes `UITab.Strip`.
    - `UIButtonRow`: any number of buttons spread across its width, or packed left/right. For frames with button places drawn in, `Positions` sets them and `Pin(button, n)` keeps a button in one.
    - `UIScrollArea`, as before.
  - `UISettingsWindow` is the Settings-menu look built from those pieces: `Tabs`, `Page`, `Buttons`, `SetTabs`, `OnTabOpened`, and `AddButton(text)` / `AddCloseButton()` with no slot number. Its buttons sit in the four places its frame sprite has drawn for them, filling from the left, with Close in the last; more than four are spread along the row. The Mods window uses it.
  - `Draw.SpriteNineSlice` and `UIInsets` are new.
- Main menu layout:
  - `MainMenu.AddBefore` / `AddAfter(context, anchor, text, onClick)` place a button relative to any button. The anchor is a game button (`VanillaButton.Play`, or the names `"Play"`/`"Start"`, `"Settings"`, `"Credits"`, `"Exit"`), the text shown on a button (in the game's language), or another mod's button text.
  - The game's own buttons are added the same way: `AddButton(context, VanillaButton.Exit)`, `AddBefore` / `AddAfter(context, anchor, VanillaButton.Settings)`. One already in the menu is moved.
  - `MainMenu.ClearButtons(context)` empties the menu: the game's buttons and every mod's added so far.
  - `VanillaButton` has every button of the game's menu screens: Play, Settings, Credits, Exit, Continue, NewGame, LoadGame, Prologue, Adventure and Back. They can be named with or without spaces ("New Game").
    - Each one does what it does in the game, with the game's text in its language.
    - Each is greyed out as the game's is: Continue when the last save can't be loaded (left out if there's none), New Game at 10 characters, Load Game with no saves.
  - Back is the game's Back. It goes back one menu, as the game's goes back a screen: the last `ClearButtons` and everything after it are undone, then the game makes the main list again. So mods' menus nest.
  - `MainMenu.RestoreButtons(context)` puts it back as it was at startup: the game's buttons and what every mod did while loading.
  - The menu is laid out from every loaded mod's calls in load order. A change shows at once (the main list is rebuilt the next frame), or when a window over the menu closes. An anchor that isn't there yet is waited for, then falls back to above Exit (or last). A mod switched off takes its changes with it.
  - `AddButton(context, text, onClick)` still adds above Exit, or last if Exit isn't there.

## 0.2.0 — GameObject release

- **Breaking:** the generated enum of the game's objects is now `GameObjectId` (`GameObjectId.o_player`), not `GameObject`. That name is the base class of a mod's own objects.
- Game objects of a mod's own: `class Ghost : GameObject { public Ghost() : base("ghost", "o_enemy") { } }`, added with `context.Objects.Add`.
  - The patcher gives the game `o_<modid>__<key>` as a child of the named game object (or of none). It has Create, Destroy, Clean Up, the three Steps, Draw Begin/Draw/Draw End/Draw GUI, 12 alarms, 16 user events and the mouse's left/right presses and enter/leave.
  - Each event runs the parent's event, then the C# override (`OnCreate`, `OnStep`, `OnDraw`, `OnAlarm(self, n)`...). Destroy and Clean Up run the C# first, and `ReplacesDraw` draws in place of the parent's Draw. Without a parent's Draw, Draw falls back to `draw_self()`.
  - `Sprite`, `Persistent` and `Visible` are set when the game runs. `Create(x, y, depth)` makes an instance, and `Instances` lists them.
  - Instances are destroyed when the mod is switched off. Objects are remembered in `dotnet\stoneforge-objects.txt`, like consumables and skills, so saves with them still load.
- `Game.CallBuiltinUnrestricted(name, self, other, args)` calls any GameMaker built-in, including the ones `CallBuiltin` refuses (`steam_`, `network_`, files). Trusted mods only: the sandbox refuses it.
- Trusted mods: `"trusted": true` in mod.json gives a mod full access. Its own DLLs (any `.dll` in its folder outside `bin`/`obj`) are referenced and loaded with it, read into memory so they can be replaced while the game runs, and native DLLs load from its folder. It compiles against the whole framework, and the sandbox check and the unsafe-code ban are skipped. It doesn't run until the player allows it: the Mods window shows a red warning, ticking Enabled allows it, "Enable all" never does, and the allowances are kept in `dotnet\mods.json` (`Allowed`). For mods that need what the sandbox refuses, such as networking.
- Passive skills: `ModPassive`, always on once learnt with an ability point, on the same tabs as a mod's active skills (same `Tab`, `Group`, requirements and icon as `ModSkill`; both now share `ModSkillBase`). `Set(BuffStat.CRT, 5)` adds to the character's stats the way the game's passives do, so it shows in the character sheet. `OnAttack`, `OnHit`, `OnKill`, `OnAttacked` and `OnHitTaken` react to the player's weapon attacks and to attacks on the player. `IsLearnt` reports whether the player has it. The patcher gives each one an `o_pass_skill_<key>` object, a child of the game's `o_skill_passive`. A passive can be a requirement for another skill (`RequireSkill(passive)`), and game passives can be required by id (`"conduit"`).
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
