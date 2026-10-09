namespace StoneForge;

/// <summary>What a mod uses to hook into the game. Handlers that throw are logged and skipped - they don't
/// take the game (or other mods) down.</summary>
public sealed class ModContext
{
    internal ModContext(ModManifest manifest, string? folder = null)
    {
        Manifest = manifest;
        Id = manifest.Id;
        Name = manifest.Name;
        _files = folder == null ? null : new ModFiles(folder);
        Items = new ModItems(this);
        Buffs = new ModBuffs(this);
        Skills = new ModSkills(this);
        Objects = new GameObjects(this);
        Quests = new ModQuests(this);
        Contracts = new ModContracts(this);
        Dialogues = new ModDialogues(this);
    }

    private readonly ModFiles? _files;

    // The loader's own parts and tests: known by an ID that isn't a mod's (no mod.json).
    internal ModContext(string id, string? folder = null)
        : this(new ModManifest(new ManifestData(id, id, "0", "", "", null)), folder) { }

    /// <summary>The mod's mod.json.</summary>
    public ModManifest Manifest { get; }

    /// <summary>Whether the current Steam account is listed in this mod's Contributors.
    /// False for an absent or empty list, or unavailable Steam identity. Read on the game thread;
    /// this reports eligibility for dev tools, independently of the dialogue editor's Dev toggle.</summary>
    public bool IsContributor => Manifest.IsContributor(Steam.AccountId);

    /// <summary>The mod's permanent ID (<see cref="ModManifest.Id"/>): what everything it registers is known by.</summary>
    public string Id { get; }

    /// <summary>The mod's name, as shown (<see cref="ModManifest.Name"/>).</summary>
    public string Name { get; }

    /// <summary>The full ID of this mod's content keyed <paramref name="key"/>: "examplemod:key". Items, consumables,
    /// buffs and skills get theirs when added; other mods refer to them by it.</summary>
    public string ContentId(string key) => ModIdentity.FullId(Id, key);

    // The content's name in the game's data: "examplemod__key".
    internal string GameKey(string key) => ModIdentity.GameKey(Id, key);

    /// <summary>Item registration for this mod, plus item giving and table queries.</summary>
    public ModItems Items { get; }

    /// <summary>Buff registration for this mod, plus applying and querying effects.</summary>
    public ModBuffs Buffs { get; }

    /// <summary>Skill registration for this mod.</summary>
    public ModSkills Skills { get; }

    /// <summary>The mod's own game objects (<see cref="GameObject"/>).</summary>
    public GameObjects Objects { get; }

    /// <summary>Custom saved quests owned by this mod.</summary>
    public ModQuests Quests { get; }
    /// <summary>Custom contracts owned by this mod.</summary>
    public ModContracts Contracts { get; }
    /// <summary>Branching NPC conversations owned by this mod.</summary>
    public ModDialogues Dialogues { get; }

    /// <summary>The other mods running now, to find one by its ID and use what it offers (<see cref="ModList"/>; its
    /// mod.json's <c>"requires"</c> lets a mod use another's types).</summary>
    public ModList Mods { get; } = new();

    /// <summary>The mod's files: its own folder, the game's and Stoneshard's data folder (see <see cref="ModFiles"/>).</summary>
    public ModFiles Files => _files ?? throw new InvalidOperationException("No mod folder");
    internal ModFiles? OptionalFiles => _files;

    /// <summary>An image in the mod's Assets folder (a .png, by path relative to it: "icon.png",
    /// "items/sword.png") as a game sprite, for drawing or giving to the game's objects - a strip of
    /// <paramref name="frames"/> frames side by side. Imports are owned by the mod and retired when it unloads.
    /// Repeated imports of the same path/options reuse the sprite. -1 if the file is missing or loading fails.</summary>
    public int LoadSprite(string relativePath, int frames = 1, int xOrigin = 0, int yOrigin = 0)
    {
        string full = Files.Resolve(Files.AssetPath(relativePath), write: false);
        if (!full.StartsWith(Files.AssetsFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("LoadSprite: only images in the mod's Assets folder");
        if (!File.Exists(full))
            return -1;
        return ModContent.LoadSprite(Id, full, frames, xOrigin, yOrigin);
    }

    /// <summary>Loads an OGG stream from Assets. Stops and releases it when the mod unloads. -1 if missing.</summary>
    public int LoadSound(string relativePath)
    {
        string full = Files.Resolve(Files.AssetPath(relativePath), write: false);
        if (!full.StartsWith(Files.AssetsFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(full), ".ogg", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("LoadSound: only OGG files in the mod's Assets folder");
        return File.Exists(full) ? ModContent.LoadSound(Id, full) : -1;
    }

    /// <summary>Ticks <paramref name="tickable"/> every frame (see <see cref="ITickable"/>) until it's removed, or
    /// the mod is switched off. Adding the same one twice ticks it once.</summary>
    public void AddTickable(ITickable tickable)
    {
        if (!Hooks.Tickables.Any(t => ReferenceEquals(t.Tickable, tickable)))
            Hooks.Tickables.Add((Id, tickable));
    }

    /// <summary>Stops ticking <paramref name="tickable"/>.</summary>
    public void RemoveTickable(ITickable tickable) => Hooks.Tickables.RemoveAll(t => ReferenceEquals(t.Tickable, tickable));

    // Every frame - for the loader's own parts (items, buffs, the main menu). Mods use ITickable instead.
    internal event Action? Frame
    {
        add { if (value != null) Hooks.FrameHandlers.Add((Id, value)); }
        remove { Hooks.FrameHandlers.RemoveAll(h => h.Handler == value); }
    }

    private ModSettings? _settings;
    private ModLocalization? _localization;

    /// <summary>This mod's JSON translation catalog, following the game's language with US English fallback.</summary>
    public ModLocalization Localization => _localization ??= new ModLocalization(this, _files);

    /// <summary>The mod's settings: declare them in Load (Toggle, Slider, Choice, Text) - they're saved for it,
    /// and shown on its page in the Mods window for the player to change.</summary>
    public ModSettings Settings => _settings ??= new ModSettings(Id);

    private ModUI? _ui;

    /// <summary>The mod's UI, on a screen for where it's shown: add <see cref="UIElement"/>s (panels, buttons,
    /// your own subclasses) to <c>UI.MainMenu</c>, <c>UI.InGame</c> or <c>UI.Always</c>, and the loader draws them
    /// and routes the mouse to them, every frame they're in their place.</summary>
    public ModUI UI => _ui ??= new ModUI(this);

    /// <summary>Every frame, in the game's Draw GUI pass - over everything, in GUI coordinates: draw with
    /// <see cref="Draw"/> here (only here: drawing outside a draw pass does nothing).</summary>
    public event Action? DrawGui
    {
        add { if (value != null) Hooks.DrawGuiHandlers.Add((Id, value)); }
        remove { Hooks.DrawGuiHandlers.RemoveAll(h => h.Handler == value); }
    }

    /// <summary>Every frame, with the game's HUD - under its windows (inventory, map, dialogue...) and its bottom
    /// panel, which draw over what's drawn here - in GUI coordinates, as <see cref="DrawGui"/>: for what belongs with
    /// the HUD (frames, markers) rather than over everything. Draw with <see cref="Draw"/>.</summary>
    public event Action? DrawHud
    {
        add { if (value != null) Hooks.DrawHudHandlers.Add((Id, value)); }
        remove { Hooks.DrawHudHandlers.RemoveAll(h => h.Handler == value); }
    }

    /// <summary>Runs around one of the game's code entries - an object event such as
    /// "gml_Object_o_player_Step_0" (object o_player, Step), with that event's self and other.
    /// <paramref name="before"/> runs first; returning true skips the game's own code (and later mods'
    /// before-handlers still run). <paramref name="after"/> runs once it's done. Every mod's run by
    /// <paramref name="order"/> (<see cref="HookOrder"/>), then in load order; two mods' before-handlers both skipping
    /// the same call is a conflict, logged and shown in the Mods window.</summary>
    public void OnCode(string codeName, Func<Instance, Instance, bool>? before = null, Action<Instance, Instance>? after = null,
        int order = HookOrder.Normal)
        => Hooks.Add(Id, codeName, before, after, order);

    /// <summary>Runs when the game calls GML script <paramref name="scriptName"/> ("scr_cast_knockback"), before
    /// its code: you get its name, self, other and arguments. Return true (after setting
    /// <see cref="ScriptCall.Result"/>) to replace the call - the script's own code doesn't run and the
    /// caller gets Result. The mod also declares the script with <see cref="HookScriptAttribute"/>, so the
    /// patcher makes it hookable.
    /// <para><paramref name="after"/> runs once the call is done, with what it returned in <see cref="ScriptCall.Result"/>
    /// (set it to change what the caller gets).</para>
    /// <para>Every mod's hooks run by <paramref name="order"/> (<see cref="HookOrder"/>), then in load order. When two mods'
    /// before hooks both replace a call, the later one's Result is used - a conflict, logged and shown in the Mods window
    /// on both: hook <see cref="HookOrder.Last"/> to be the one that wins.</para></summary>
    public void OnScript(string scriptName, Func<ScriptCall, bool>? before = null, Action<ScriptCall>? after = null,
        int order = HookOrder.Normal)
        => Hooks.AddScript(Id, scriptName, before, after, order);

    /// <summary>Writes a line to the loader's log, tagged with the mod's name.</summary>
    public void Log(string text) => Game.Log($"[{Name}] {text}");
}
