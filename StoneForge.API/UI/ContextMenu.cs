namespace StoneForge;

/// <summary>A right-click menu as it opens (<see cref="ContextMenus.OnOpen"/>): what it's for, and its options - to add
/// to, remove, rename or grey out. Only while the handler runs: the game builds the menu's buttons from it after.</summary>
public sealed class ContextMenu
{
    private readonly Instance _menu;
    private readonly DsList _names, _descs;
    private readonly string _mod;
    private readonly Dictionary<string, Action<Instance>> _actions;

    internal ContextMenu(Instance menu, Instance target, DsList names, DsList descs, string mod, Dictionary<string, Action<Instance>> actions)
    {
        _menu = menu;
        Target = target;
        _names = names;
        _descs = descs;
        _mod = mod;
        _actions = actions;
    }

    /// <summary>What the menu's for: the instance right-clicked.</summary>
    public Instance Target { get; }

    /// <summary>Its options, top to bottom.</summary>
    public IReadOnlyList<ContextMenuItem> Items
    {
        get
        {
            var items = new List<ContextMenuItem>();
            for (int i = 0; i + 1 < _names.Count; i += 2)
                items.Add(new ContextMenuItem(_names[i].AsString, _names[i + 1].AsString,
                    i < _descs.Count && _descs[i].AsBool, i + 1 < _descs.Count && _descs[i + 1] is { Kind: GmKind.String } h ? h.AsString : ""));
            return items;
        }
    }

    /// <summary>Whether it has an option with this key.</summary>
    public bool Has(string key) => IndexOf(key) >= 0;

    /// <summary>Adds an option: <paramref name="text"/>, running <paramref name="onClick"/> on <see cref="Target"/> when
    /// clicked - at the bottom, or at <paramref name="index"/>; greyed out unless <paramref name="enabled"/>; with the
    /// game's hint <paramref name="hover"/>. Its key is the mod's own (one per text): the key it returns.</summary>
    public string Add(string text, Action<Instance> onClick, int? index = null, bool enabled = true, string? hover = null)
    {
        string key = $"stoneforge:{_mod}:{text}";
        _actions[key] = onClick;
        int at = Math.Clamp(index ?? int.MaxValue, 0, _names.Count / 2) * 2;
        Insert(_names, at, key, text);
        Insert(_descs, at, enabled, hover is null ? (GmValue)0 : hover);
        return key;
    }

    /// <summary>Removes the option with this key (the game's "Attack", or a key <see cref="Add"/> gave); false if
    /// there's none.</summary>
    public bool Remove(string key)
    {
        int i = IndexOf(key);
        if (i < 0)
            return false;
        for (int n = 0; n < 2; n++)
        {
            _names.RemoveAt(i);
            if (i < _descs.Count)
                _descs.RemoveAt(i);
        }
        return true;
    }

    /// <summary>Changes the text an option shows; false if there's none with this key.</summary>
    public bool SetText(string key, string text)
    {
        int i = IndexOf(key);
        if (i < 0)
            return false;
        _names[i + 1] = text;
        return true;
    }

    /// <summary>Greys an option out (or back); false if there's none with this key.</summary>
    public bool SetEnabled(string key, bool enabled)
    {
        int i = IndexOf(key);
        if (i < 0 || i >= _descs.Count)
            return false;
        _descs[i] = enabled;
        return true;
    }

    // The options changed: sized again as the game sizes it (its widest text), or closed if none's left.
    internal void Finish()
    {
        if (!_menu.Exists)
            return;
        if (_names.Count == 0)
        {
            Game.CallBuiltin("instance_destroy", _menu);
            return;
        }
        double width = 44;
        for (int i = 1; i < _names.Count; i += 2)
            width = Math.Max(width, Game.CallBuiltin("string_width", _names[i]).AsReal / 2);
        _menu["width"] = Math.Max(_menu.Get("width").AsReal, width);
    }

    private int IndexOf(string key)
    {
        for (int i = 0; i + 1 < _names.Count; i += 2)
            if (_names[i] is { Kind: GmKind.String } name && name.AsString == key)
                return i;
        return -1;
    }

    private static void Insert(DsList list, int at, GmValue first, GmValue second)
    {
        at = Math.Min(at, list.Count);
        Game.CallBuiltin("ds_list_insert", list.Id, at, first);
        Game.CallBuiltin("ds_list_insert", list.Id, at + 1, second);
    }
}
