using System.Globalization;
using StoneForge;

[Collection("MainMenu")]
public sealed class LocalizationTests
{
    [Fact]
    public void Built_in_English_is_embedded_and_missing_languages_fall_back()
    {
        Assert.Equal("en-US", Localization.DefaultLanguage);
        Assert.Equal("en-US", Localization.GameLanguage(2));
        Assert.Equal("ru-RU", Localization.GameLanguage(1));
        Assert.Equal("en-US", Localization.GameLanguage(999));
        string previous = Localization.Language;
        try
        {
            Localization.SetLanguage("ja-JP");
            Assert.Equal("Mods", Localization.Get("mods.title"));
            Assert.Equal("Author: Tiny Monster", Localization.Get("mods.author", "Tiny Monster"));
            Assert.Equal("unknown.key", Localization.Get("unknown.key"));
        }
        finally { Localization.SetLanguage(previous); }
    }

    [Fact]
    public void Catalog_falls_back_per_key_through_parent_language_and_English()
    {
        var files = new Dictionary<string, string>
        {
            ["en-US"] = "{\"greeting\":\"Hello {0}\",\"onlyEnglish\":\"English\"}",
            ["fr"] = "{\"greeting\":\"Bonjour {0}\"}",
        };
        var catalog = new TextCatalog(locale => files.GetValueOrDefault(locale), _ => { });
        Assert.Equal("Bonjour Sam", catalog.Get("fr-CA", "greeting", "Sam"));
        Assert.Equal("Bonjour {0}", catalog.Template("fr-CA", "greeting"));
        Assert.Equal("Hello {0}", catalog.Template("de-DE", "greeting"));
        Assert.Equal("English", catalog.Get("fr-CA", "onlyEnglish"));
        Assert.Equal("Hello Sam", catalog.Get("de-DE", "greeting", "Sam"));
        Assert.Equal("missing", catalog.Get("fr-CA", "missing"));
    }

    [Fact]
    public void Invalid_catalogs_and_mismatched_placeholders_fall_back_without_repeated_logs()
    {
        var logs = new List<string>();
        var files = new Dictionary<string, string>
        {
            ["en-US"] = "{\"reward\":\"{0}: {2:N0}\",\"escaped\":\"{{{0}}}\"}",
            ["fr"] = "{\"reward\":\"{1}: {2:N0}\",\"escaped\":\"{{{0}}}\"}",
            ["de"] = "not JSON",
        };
        var catalog = new TextCatalog(locale => files.GetValueOrDefault(locale), logs.Add);
        Assert.Equal("Sam: " + 1000.ToString("N0", CultureInfo.GetCultureInfo("fr")), catalog.Get("fr", "reward", "Sam", "wrong", 1000));
        Assert.Equal("{Sam}", catalog.Get("fr", "escaped", "Sam"));
        Assert.Equal("{Sam}", catalog.Get("de", "escaped", "Sam"));
        int count = logs.Count;
        catalog.Get("de", "escaped", "Sam");
        catalog.Get("fr", "reward", "Sam", "wrong", 1000);
        Assert.Equal(count, logs.Count);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Duplicate_keys_and_invalid_format_strings_are_rejected()
    {
        var catalog = new TextCatalog(locale => locale switch
        {
            "en-US" => "{\"key\":\"English {0}\"}",
            "fr" => "{\"key\":\"First {0}\",\"key\":\"Second {0}\"}",
            "de" => "{\"key\":\"Broken {0\"}",
            _ => null,
        }, _ => { });
        Assert.Equal("English Sam", catalog.Get("fr", "key", "Sam"));
        Assert.Equal("English Sam", catalog.Get("de", "key", "Sam"));
        Assert.Equal("English {0}", catalog.Get("en-US", "key"));
    }

    [Fact]
    public void Mod_catalogs_are_separate_and_language_callbacks_are_removed_on_unload()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "mods", "sf-localization-" + Guid.NewGuid().ToString("N"));
        string previous = Localization.Language;
        var context = new ModContext("localization_test", folder);
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "Localization"));
            File.WriteAllText(Path.Combine(folder, "Localization", "en-US.json"), "{\"reward\":\"Reward: {0}\"}");
            Localization.SetLanguage("en-US");
            Assert.Equal("Reward: 100", context.Localization.Get("reward", 100));
            var other = new TextCatalog(_ => "{\"reward\":\"Other\"}", _ => { });
            Assert.Equal("Other", other.Get("en-US", "reward"));
            int changes = 0;
            context.Localization.LanguageChanged += () => changes++;
            Localization.SetLanguage("ru-RU");
            var frame = Hooks.FrameHandlers.Single(h => h.Mod == context.Id).Handler;
            frame(); frame();
            Assert.Equal(1, changes);
            Assert.Equal("ru-RU", context.Localization.Language);
            Assert.Equal("Reward: 100", context.Localization.Get("reward", 100));
        }
        finally
        {
            Hooks.RemoveMod(context.Id);
            Localization.SetLanguage(previous);
            Directory.Delete(folder, true);
        }
        Assert.DoesNotContain(Hooks.FrameHandlers, h => h.Mod == context.Id);
    }
    [Fact]
    public void Catalog_refresh_detects_edits_new_files_and_keeps_last_good_JSON()
    {
        var files = new Dictionary<string, string> { ["en-US"] = "{\"key\":\"First\"}" };
        var catalog = new TextCatalog(locale => files.GetValueOrDefault(locale), _ => { });
        Assert.Equal("First", catalog.Get("fr", "key"));
        files["en-US"] = "{\"key\":\"Other\"}";
        Assert.True(catalog.Refresh());
        Assert.Equal("Other", catalog.Get("fr", "key"));
        files["fr"] = "{\"key\":\"Bonjour\"}";
        Assert.True(catalog.Refresh());
        Assert.Equal("Bonjour", catalog.Get("fr", "key"));
        files["fr"] = "{";
        Assert.True(catalog.Refresh());
        Assert.Equal("Bonjour", catalog.Get("fr", "key"));
        Assert.False(catalog.Refresh());
        files.Remove("fr");
        Assert.True(catalog.Refresh());
        Assert.Equal("Other", catalog.Get("fr", "key"));
    }

    [Fact]
    public void Bound_controls_and_menu_buttons_refresh_on_file_edits_and_language_changes()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "mods", "sf-live-" + Guid.NewGuid().ToString("N"));
        string previous = Localization.Language;
        var context = new ModContext("live_localization_test", folder);
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "Localization"));
            string path = Path.Combine(folder, "Localization", "en-US.json");
            File.WriteAllText(path, "{\"button\":\"First\"}");
            Localization.SetLanguage("en-US");
            var label = new UILabel();
            context.Localization.Bind(label, live => live.Text = context.Localization.Get("button"));
            MainMenu.AddButton(context, () => context.Localization.Get("button"), () => { });
            int changes = 0;
            context.Localization.TranslationsChanged += () => changes++;
            File.WriteAllText(path, "{\"button\":\"Other\"}");
            var frame = Hooks.FrameHandlers.Single(h => h.Mod == context.Id).Handler;
            frame();
            Assert.Equal("Other", label.Text);
            Assert.Contains("Other", MainMenu.Describe());
            Assert.Equal(1, changes);
            File.WriteAllText(Path.Combine(folder, "Localization", "fr.json"), "{\"button\":\"Bonjour\"}");
            Localization.SetLanguage("fr");
            frame();
            Assert.Equal("Bonjour", label.Text);
            Assert.Contains("Bonjour", MainMenu.Describe());
            Assert.Equal(2, changes);
        }
        finally
        {
            Hooks.RemoveMod(context.Id);
            MainMenu.RemoveMod(context.Id);
            Localization.SetLanguage(previous);
            Directory.Delete(folder, true);
        }
    }
    [Fact]
    public void File_watcher_queues_saves_and_renames_for_the_game_thread_and_is_released()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "mods", "sf-watch-" + Guid.NewGuid().ToString("N"));
        string previous = Localization.Language;
        var context = new ModContext("watch_localization_test", folder);
        ModLocalization? texts = null;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "Localization"));
            string path = Path.Combine(folder, "Localization", "en-US.json");
            File.WriteAllText(path, "{\"button\":\"First\"}");
            Localization.SetLanguage("en-US");
            texts = context.Localization;
            Assert.True(texts.IsWatching);
            var label = new UILabel();
            texts.Bind(label, live => live.Text = texts.Get("button"));
            int callbacks = 0, callbackThread = 0;
            texts.TranslationsChanged += () => { callbacks++; callbackThread = Environment.CurrentManagedThreadId; };
            texts.ProcessChanges(Environment.TickCount64); // Establish the ten-second fallback deadline.
            File.WriteAllText(path, "{\"button\":\"Other\"}");
            Assert.True(SpinWait.SpinUntil(() => texts.HasPendingChanges, 3000));
            Assert.Equal("First", label.Text); // Worker callbacks must not touch the UI.
            Assert.Equal(0, callbacks);
            texts.ProcessChanges(Environment.TickCount64 + 200);
            Assert.Equal("Other", label.Text);
            Assert.Equal(Environment.CurrentManagedThreadId, callbackThread);
            Assert.Equal(1, callbacks);
            string temporary = Path.Combine(folder, "replacement.tmp");
            File.WriteAllText(temporary, "{\"button\":\"Renamed\"}");
            File.Move(temporary, path, overwrite: true);
            Assert.True(SpinWait.SpinUntil(() =>
            {
                texts.ProcessChanges(Environment.TickCount64 + 200);
                return label.Text == "Renamed";
            }, 3000));
            Assert.Equal(2, callbacks);
        }
        finally
        {
            Hooks.RemoveMod(context.Id);
            Localization.SetLanguage(previous);
            Directory.Delete(folder, true);
        }
        Assert.NotNull(texts);
        Assert.True(texts.IsDisposed);
        Assert.False(texts.IsWatching);
    }

    [Fact]
    public void Watcher_detects_catalogs_added_after_loading_without_a_localization_folder()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "mods", "sf-watch-new-" + Guid.NewGuid().ToString("N"));
        string previous = Localization.Language;
        var context = new ModContext("watch_new_localization_test", folder);
        try
        {
            Directory.CreateDirectory(folder);
            Localization.SetLanguage("en-US");
            var texts = context.Localization;
            var label = new UILabel();
            texts.Bind(label, live => live.Text = texts.Get("button"));
            texts.ProcessChanges(Environment.TickCount64);
            Assert.Equal("button", label.Text);
            Directory.CreateDirectory(Path.Combine(folder, "Localization"));
            File.WriteAllText(Path.Combine(folder, "Localization", "en-US.json"), "{\"button\":\"Added\"}");
            Assert.True(SpinWait.SpinUntil(() =>
            {
                texts.ProcessChanges(Environment.TickCount64 + 200);
                return label.Text == "Added";
            }, 3000));
        }
        finally
        {
            Hooks.RemoveMod(context.Id);
            Localization.SetLanguage(previous);
            Directory.Delete(folder, true);
        }
    }
}
