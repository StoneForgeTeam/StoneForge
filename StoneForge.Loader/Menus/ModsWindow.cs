namespace StoneForge.Loader;

/// <summary>The Mods window (a <see cref="UISettingsWindow"/>, on the loader's main menu screen): a tab per mod and its
/// page (its name, Assets\icon.png, any errors, version, author, description, an Enabled checkbox that switches
/// it on or off at once, and its settings - SettingsPage), with Mods folder, Enable all, Disable all and Close along the bottom. It opens
/// on the mod open last time.</summary>
internal sealed class ModsWindow : UISettingsWindow
{
    private const string EnabledTooltip = "Switched on or off straight away. Switched off, its items are taken out of the game - load a save from before to get them back once it's on again.";
    private static readonly int ErrorColour = Draw.Rgb(200, 70, 60);
    // (The game's yellow, as its tooltips highlight with.)
    private static readonly int WarningColour = Draw.Rgb(232, 196, 82);
    internal const string TrustedWarning = "This mod asks for full access: it runs outside StoneForge's security, with its own DLLs, and can do anything a program on your PC can - files, the network, other programs. Only allow mods you trust. Ticking Enabled allows it.";
    internal const string GmlWarning = "This mod uses GML bindings and can bypass StoneForge's security. Its GML can't be hot-reloaded: changes need a restart of the game. Use at your own discretion.";

    private List<ModInfo> _mods = new();
    private UICheckbox? _enabled;
    private int _lastTab;
    // Icons, loaded once (-1: none).
    private readonly Dictionary<string, int> _icons = new();
    // Mods whose every possibly conflicting hook is listed (Possible Conflicts' toggle; collapsed until opened).
    private readonly HashSet<string> _hooksShown = new();

    public ModsWindow() : base("Mods") { }

    protected override void OnOpen()
    {
        _mods = ModRegistry.All.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        SetTabs(_mods.Select(m => m.Name));
        if (_mods.Count > 0)
            Tabs.Tabs[Math.Clamp(_lastTab, 0, _mods.Count - 1)].Open();
        else
            Page.AddText("No mods installed.\n\nA mod goes in its own folder, Stoneshard\\mods\\<mod>\\: its C# source, and its pictures in Assets\\.", Draw.Muted);
        AddButton("Mods folder").Clicked += _ => OpenFolder();
        AddButton("Enable all").Clicked += _ => SetAll(true);
        AddButton("Disable all").Clicked += _ => SetAll(false);
        AddCloseButton();
    }

    // A mod's page: as a settings tab's options.
    protected override void OnTabOpened(UITab tab)
    {
        _lastTab = tab.Index;
        var mod = ModRegistry.All.FirstOrDefault(m => m.Id == _mods[tab.Index].Id) ?? _mods[tab.Index];
        Page.Clear();
        Page.AddHeader(mod.Name);
        if (mod.Trusted)
            Page.AddText(TrustedWarning, ErrorColour);
        if (mod.ContainsGml)
        {
            Page.AddText(GmlWarning, WarningColour);
        }
        int icon = mod.Error == null ? Icon(mod) : -1;
        if (icon >= 0)
            Page.AddImage(icon);
        // (A mod that didn't compile, or uses what mods aren't allowed: why, in red.)
        if (!string.IsNullOrEmpty(mod.Error))
            Page.AddText(mod.Error, ErrorColour);
        if (!string.IsNullOrEmpty(mod.RuntimeError))
        {
            Page.AddText("Paused: " + mod.RuntimeError, ErrorColour);
            Page.Add(new UIButton("Retry", 5, 0, onClick: () => ModManager.Request(mod.Id, true)));
        }
        Page.AddText("Version " + mod.Version + (mod.Author.Length > 0 ? "  -  by " + mod.Author : ""), Draw.Muted);
        if (mod.Description.Length > 0)
            Page.AddText(mod.Description);
        _enabled = Page.AddCheckbox("Enabled", IsEnabled(mod), EnabledTooltip);
        _enabled.Changed += on => SetEnabled(mod, on);
        // (Its settings, if it's loaded and has some.)
        SettingsPage.Add(Page, mod.Id, () => tab.Open());
        Conflicts(mod.Id, () => tab.Open());
    }

    // Possible Conflicts, at the bottom, out of the way (only when there are any): a line for each other mod - the calls
    // themselves in its tooltip. Calls both mods' hooks replaced (a conflict that happened: whose result the game got),
    // then calls both hook before they run at the same order (one that might: which runs first is load order).
    private void Conflicts(string id, Action reopen)
    {
        var conflicts = ModRegistry.ConflictsOf(id);
        var overlaps = ModRegistry.OverlapsOf(id);
        if (conflicts.Count == 0 && overlaps.Count == 0)
            return;
        Page.AddHeader("Possible Conflicts");
        foreach (var (with, calls) in conflicts)
            HoverLine($"With {with}: both replaced {Count(calls.Count, "call")}", WarningColour,
                "Both mods' hooks replaced these calls (the game's own code was skipped):", calls);
        foreach (var (with, calls) in overlaps)
            HoverLine($"With {with}: both hook {Count(calls.Count, "call")} at the same order", Draw.Muted,
                "Both mods hook these before they run, at the same order - which runs first is load order. A conflict only if "
                + "both replace one of them (HookOrder sets the order):", calls);
        // Every one of them, a line each - collapsed until asked for (the page made again, open or shut).
        int total = conflicts.Sum(c => c.Calls.Count) + overlaps.Sum(o => o.Calls.Count);
        bool shown = _hooksShown.Contains(id);
        var toggle = Page.AddText(shown ? "[-] Hide the hooks" : $"[+] Show every hook ({total})", Draw.Muted);
        toggle.HitTest = true;
        toggle.Tooltip = shown ? "Collapse the list" : "List every call above, with the mod it's shared with";
        toggle.Clicked += _ =>
        {
            if (!_hooksShown.Remove(id))
                _hooksShown.Add(id);
            reopen();
        };
        if (!shown)
            return;
        foreach (var (with, calls) in conflicts)
            foreach (string call in calls)
                Page.AddText($"    {call}  (with {with})", WarningColour);
        foreach (var (with, calls) in overlaps)
            foreach (string call in calls)
                Page.AddText($"    {call}  (with {with})", Draw.Muted);
    }

    protected override void OnClosed() => _enabled = null;

    // A line with a list in its tooltip (the first few, and how many more).
    private void HoverLine(string text, int colour, string heading, List<string> items)
    {
        const int Shown = 12;
        var label = Page.AddText(text, colour);
        label.HitTest = true;
        label.Tooltip = heading + "\n" + string.Join("\n", items.Take(Shown))
            + (items.Count > Shown ? $"\n... and {items.Count - Shown} more" : "");
    }

    private static string Count(int count, string what) => $"{count} {what}{(count == 1 ? "" : "s")}";

    // Every mod switched on / off, the open page's checkbox with them.
    private void SetAll(bool on)
    {
        // (A trusted mod is never allowed in bulk: only by its own checkbox, under its warning.)
        foreach (var mod in _mods)
            if (IsEnabled(mod) != on && !(on && mod.Trusted))
                SetEnabled(mod, on);
        if (_enabled != null && _mods.Count > 0)
            _enabled.Checked = IsEnabled(_mods[Math.Clamp(_lastTab, 0, _mods.Count - 1)]);
        Gm.AudioPlaySound(on ? Sound.snd_checkbox_on : Sound.snd_checkbox_off, 4);
    }

    private static bool IsEnabled(ModInfo mod) => ModRegistry.MayRun(mod.Id, mod.Trusted);

    // Saved for the next start, and done now (next frame). (A trusted mod switched on is allowed; off, no longer.)
    private static void SetEnabled(ModInfo mod, bool enabled)
    {
        if (mod.Trusted)
            ModRegistry.SetAllowed(mod.Id, enabled);
        ModRegistry.SetEnabled(mod.Id, enabled);
        ModManager.Request(mod.Id, enabled);
    }

    private static void OpenFolder()
    {
        try { System.Diagnostics.Process.Start("explorer.exe", Bridge.ModsDir); }
        catch (Exception e) { Game.Log("Couldn't open the mods folder: " + e.Message); }
    }

    // mods\<mod>\Assets\icon.png as a sprite (-1: none), loaded once.
    private int Icon(ModInfo mod)
    {
        if (_icons.TryGetValue(mod.Folder, out int sprite))
            return sprite;
        string path = Path.Combine(mod.Folder, "Assets", "icon.png");
        sprite = -1;
        if (File.Exists(path))
        {
            sprite = Game.CallBuiltinTrusted("sprite_add", default, default, path, 1, false, false, 0, 0).AsInt;
            if (sprite < 0)
                Game.Log($"{mod.Name}: couldn't load {path}");
        }
        return _icons[mod.Folder] = sprite;
    }
}
