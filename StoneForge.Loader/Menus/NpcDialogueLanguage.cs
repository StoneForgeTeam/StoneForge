using System.Globalization;

namespace StoneForge.Loader;

internal sealed partial class NpcDialogueEditing
{
    private static bool PlayerAnswerKey(string key) => key is "continue" or "return" or "next" or "leave" or "back" or "chat" or "trade" or "learn" or "learn_nomoney" or "learn_exit" or "custom_learn" or "custom_learn_exit" or "custom_continue" or "custom_leave" or "rent_room";
    private readonly Dictionary<string, List<string[]>> _nativePreviewRows = new(StringComparer.Ordinal);
    internal int NativePreviewIndexBuilds { get; private set; }
    private string NativePreviewKey(View view, Option? option)
    {
        string key = Normalize(option?.Key ?? view.Panel.Get("text_fragment").AsString);
        using var aliases = view.Panel.Get("options_aliases").AsStruct;
        if (aliases?[key] is { Kind: GmKind.String } alias) key = alias.AsString;
        else
        {
            using var context = view.Panel.Get("dialog_id").AsStruct;
            using var specs = context?["Specs"].AsStruct;
            using var spec = specs?[key].AsStruct;
            using var specAlias = spec?["alias"].AsArray;
            if (specAlias is { Length: > 0 } && specAlias[0].Kind == GmKind.String) key = specAlias[0].AsString;
        }
        return option != null && PlayerAnswerKey(key) && !key.StartsWith("custom_", StringComparison.Ordinal) ? "custom_" + key : key;
    }
    private void IndexNativePreviewRows(View view, string requested)
    {
        using var rows = Game.Global["npc_lines"].AsArray;
        if (rows == null) return;
        var wanted = view.Options.Select(key => NativePreviewKey(view, new(key, "", default, "")))
            .Append(NativePreviewKey(view, null)).Append(requested).Where(key => !_nativePreviewRows.ContainsKey(key)).ToHashSet(StringComparer.Ordinal);
        // Managed strings only: no native arrays/structs survive this call.
        foreach (string key in wanted) _nativePreviewRows[key] = new();
        NativePreviewIndexBuilds++;
        foreach (var value in rows)
        {
            using var row = value.AsArray;
            if (row == null || row.Length < 18 || !wanted.Contains(row[0].AsString)) continue;
            string key = row[0].AsString;
            var text = new string[12];
            for (int language = 1; language <= 12; language++) text[language - 1] = row[5 + language].AsString;
            _nativePreviewRows[key].Add(text);
        }
    }
    internal string VanillaPreviewText(View view, Option? option, string original)
    {
        if (PreviewLanguage == null || PreviewLanguage == Localization.Language || ActiveFor(view) != null || option != null && Authored(view, option.Key) != null) return original;
        string key = NativePreviewKey(view, option);
        int source = Enumerable.Range(1, 12).FirstOrDefault(i => Localization.GameLanguage(i) == Localization.Language);
        int target = Enumerable.Range(1, 12).FirstOrDefault(i => Localization.GameLanguage(i) == PreviewLanguage);
        if (source == 0 || target == 0) return original;
        if (!_nativePreviewRows.ContainsKey(key)) IndexNativePreviewRows(view, key);
        if (!_nativePreviewRows.TryGetValue(key, out var translations)) return original;
        foreach (var text in translations)
        {
            if (text[source - 1] == original && !string.IsNullOrWhiteSpace(text[target - 1])) return text[target - 1];
        }
        return original;
    }
}

internal sealed partial class NpcDialogueEditor
{
    private LanguageStrip? _languageStrip;
    internal UIDropdown? LanguageDropdown => _languageStrip?.Picker;
    private sealed class LanguageStrip : UIGroup
    {
        internal readonly UIDropdown Picker;
        private readonly string[] _locales = Enumerable.Range(1, 12).Select(Localization.GameLanguage).ToArray();
        internal LanguageStrip(NpcDialogueEditor owner)
        {
            Width = 155; Height = 19; Visible = false;
            Picker = Add(new UIDropdown(new[] { L("language_game") }.Concat(_locales.Select(l => CultureInfo.GetCultureInfo(l).NativeName)), 2, 2, 151));
            Picker.Changed += index =>
            {
                if (owner._editing.Current is not { } view || owner.InlineInput != null || owner._dirty) return;
                owner.ClosePopup(); owner._editing.SetPreviewLanguage(view, index == 0 ? null : _locales[index - 1]);
                if (owner.Visible) owner.Build();
            };
        }
        internal void Sync(NpcDialogueEditor owner, NpcDialogueEditing.View view)
        {
            if (Picker.IsOpen) { owner._editing.SelectingLanguage = true; owner._editing.EditingPanel = view.Panel; }
            else if (owner._editing.SelectingLanguage)
            {
                owner._editing.SelectingLanguage = false;
                if (!owner.Visible && owner.ContextPopup == null && owner.InlineInput == null) owner._editing.EditingPanel = default;
            }
            Visible = true; Picker.Enabled = owner.InlineInput == null && !owner._dirty;
            Picker.SelectedIndex = owner._editing.PreviewLanguage == null ? 0 : Array.IndexOf(_locales, owner._editing.PreviewLanguage) + 1;
            var render = ContextMenus.InstanceOf(view.Panel.Get("render"));
            double scale = render.Get("drawScale").AsReal; if (scale <= 0) scale = 1;
            var (kx, ky) = NativeScale();
            double right = render.Get("x").AsReal + render.Get("surfaceOffsetX").AsReal + render.Get("surfaceWidth").AsReal * scale;
            double top = render.Get("y").AsReal + render.Get("surfaceOffsetY").AsReal;
            if (render.Get("drawBottom").AsBool) top += Math.Max(0, render.Get("drawAreaHeight").AsReal - render.Get("surfaceHeight").AsReal * scale);
            X = Math.Clamp(Mouse.X + (right - Game.Global["guiMouseX"].AsReal) / kx - Width - 4, 0, Math.Max(0, Draw.Width - Width));
            Y = Math.Clamp(Mouse.Y + (top - Game.Global["guiMouseY"].AsReal) / ky + 4, 0, Math.Max(0, Draw.Height - Height));
        }
    }
    internal void UpdateLanguageStrip(bool enabled)
    {
        if (_languageStrip == null && Screen != null) _languageStrip = Screen.Add(new LanguageStrip(this));
        var view = _editing.Current;
        if (enabled && view != null && view.Panel.Exists && _editing.Inspect(view.Panel)?.Root == view.Root)
        { _languageStrip?.Sync(this, view); return; }
        if (_languageStrip != null) { _languageStrip.Picker.CloseQuietly(); _languageStrip.Visible = false; }
        _editing.SelectingLanguage = false;
        if (_editing.PreviewLanguage != null)
        {
            if (view != null) _editing.SetPreviewLanguage(view, null);
        }
    }
}
