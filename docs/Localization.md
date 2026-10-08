# Localization

StoneForge ships its interface text as an embedded US English (`en-US`) catalog.
The Mods window, settings controls, warnings, conflicts and loading status use text
keys. The language follows Stoneshard's selection; when a built-in translation is
missing, the interface stays in English. Logs and compiler diagnostics remain in
English for troubleshooting. Only US English is shipped in this first version.

## Translations for C# mods

Put translation files in your mod's `Localization` folder:

```text
mods/MyMod/Localization/en-US.json
mods/MyMod/Localization/fr.json
mods/MyMod/Localization/fr-CA.json
```

Each file is a UTF-8 JSON object of stable keys and strings:

```json
{
  "challenge.reward": "Reward: {0:N0} crowns",
  "settings.title": "Challenge settings"
}
```

Access text through your context:

```csharp
string reward = context.Localization.Get("challenge.reward", 100);
string language = context.Localization.Language; // e.g. en-US
```

Lookups try the selected culture, its parent culture (for example `fr-CA` then
`fr`), `en-US`, and `en`. If a key is missing everywhere, its key is returned.
Every mod owns its catalog, so two mods can use the same key without overriding
one another. A .NET file watcher detects catalog saves, additions, deletions and renames,
including replacement of the Localization folder. Notifications are combined after
150 milliseconds of quiet, then processed on the game thread. A ten-second fallback
check covers missed notifications; a short retry handles files still being saved.
Saved edits appear without a mod reload. Malformed JSON during editing keeps the last
valid catalog until the file is corrected.

Use whole sentences rather than translated fragments. Placeholders are numbered
(`{0}`, `{1}`) and may be reordered in translations. Use `{{` and `}}` for literal
braces. Number formatting uses the selected culture without changing the process's
global culture. Translation placeholders must match the English entry's indices.
Malformed JSON, duplicate keys, invalid format strings and mismatched placeholders
are logged and fall back to English. A catalog is limited to 1 MB. Reading files
uses the existing mod file policy, including its restrictions on links and junctions.

Text already assigned to a UI element is a snapshot. Refresh your UI when the
language or translation files change:

```csharp
context.Localization.TranslationsChanged += () =>
{
    rewardLabel.Text = context.Localization.Get("challenge.reward", reward);
};
```

For controls that should keep their current state, bind their text directly:

```csharp
context.Localization.Bind(rewardLabel,
    label => label.Text = context.Localization.Get("challenge.reward", reward));
MainMenu.AddButton(context,
    () => context.Localization.Get("settings.title"), OpenSettings);
```

Bindings run immediately and after translation changes. They hold a weak reference
to the target; avoid capturing the target in the callback. `LanguageChanged`
continues to report game language changes only; `TranslationsChanged` covers both.

The callback runs on the game's frame thread and is removed with the mod. Settings
labels, item names and skill descriptions are not automatically translated: pass
localized strings when defining them and refresh them through their supported APIs.
MSL/MSLE mods retain their own localization; this does not rewrite their text.

## Built-in catalogs

StoneForge's catalog is `StoneForge.API/Localization/en-US.json`. Additional built-in
catalogs can be added beside it using culture filenames. They are embedded in the
API DLL; no separate language folder is required in the game installation. The Mods
menu button, open Mods window and loading status refresh when the game language changes.

Currently recognized game languages are Russian, English, Chinese, German, Spanish
(Latin America), French, Italian, Portuguese, Polish, Turkish, Japanese and Korean.
Only English has been translated here. Other scripts' glyph coverage and longer text
layouts should be checked in-game before shipping more languages.
