using System.Globalization;

namespace StoneForge.Loader;

// A small editing strip over the native conversation, not another modal dialogue/window.
internal sealed partial class NpcDialogueEditor : UIGroup
{
    private readonly NpcDialogueEditing _editing;
    private NpcDialogueEditing.View? _view;
    private NpcDialogueEditing.Option? _option;
    private UITextBox? _text, _position, _reply;
    private UILabel? _status;
    private bool _adding, _dirty;
    private int _languageRevision;
    internal UIGroup? ContextPopup { get; private set; }
    internal UITextBox? InlineInput { get; private set; }
    internal NpcDialogueEditor(NpcDialogueEditing editing)
    { _editing = editing; Visible = false; Anchor = UIAnchor.TopRight; X = 8; Y = 8; Width = 310; Height = 300; }
    private static string L(string key, params object?[] args) => Localization.Get("npc_editor." + key, args);
    internal static NpcDialogueEditor Install(ModContext context, Func<bool>? enabled = null)
    {
        var editing = new NpcDialogueEditing(context, context.Files.Resolve("Dialogue", write: true));
        editing.Load(); editing.Install();
        var tool = context.UI.InGame.Add(new NpcDialogueEditor(editing));
        tool._languageStrip = context.UI.InGame.Add(new LanguageStrip(tool));
        int languageRevision = Localization.Revision;
        editing.Selected = button => tool.Select(button.Get("func").AsString);
        // Poll only with a room-backed Draw GUI pass; never query instances on startup EVENT_FRAME.
        context.DrawGui += () =>
        {
            editing.TickConditions();
            tool.UpdateLanguageStrip(enabled == null || enabled());
            if (languageRevision != Localization.Revision)
            {
                languageRevision = Localization.Revision;
                if (editing.Current is { } translated) editing.RefreshLanguage(translated);
            }
            if (enabled != null && !enabled()) { if (tool.Visible || tool.ContextPopup != null || tool.InlineInput != null) tool.Close(); return; }
            if (Mouse.Pressed(Mouse.Right) && Mouse.HasFocus && !UIWindow.AnyOpen &&
                !(tool.Visible && tool.Contains(Mouse.X, Mouse.Y)) && !NativeDialogue.SceneBlocked)
            {
                if (tool.ContextPopup?.Contains(Mouse.X, Mouse.Y) == true) return;
                tool.ClosePopup();
                if (editing.Inspect(NativeDialogue.Find()) is { } clicked)
                    tool.EditAt(clicked, Game.Global["guiMouseX"].AsReal, Game.Global["guiMouseY"].AsReal);
            }
            if (!Keyboard.Down(Keyboard.Control) || !Keyboard.Pressed(Keyboard.F8) || UITextBox.AnyFocused) return;
            if (tool.Visible) tool.Close();
            else if (!UIWindow.AnyOpen && !NativeDialogue.SceneBlocked && editing.Inspect(NativeDialogue.Find()) is { } view) tool.Open(view);
        };
        return tool;
    }
    internal void Open(NpcDialogueEditing.View view)
    {
        ClosePopup();
        _view = view; _option = null; _adding = false; Visible = true;
        _editing.EditingPanel = view.Panel; _languageRevision = Localization.Revision; Build();
    }
    internal void Close()
    {
        ClosePopup();
        if (Screen != null) UITextBox.ReleaseFocusIn(Screen);
        CloseDropdowns(this); Visible = false; _editing.EditingPanel = default; _view = null;
        if (_editing.PreviewLanguage != null && _editing.Current is { } preview) _editing.SetPreviewLanguage(preview, null);
    }
    protected override void OnUpdate(double deltaTime)
    {
        if (_view == null || !_view.Panel.Exists) { Close(); return; }
        var current = _editing.Inspect(_view.Panel);
        if (current == null || current.Npc != _view.Npc || current.Root != _view.Root) { Close(); return; }
        if (Keyboard.Pressed(Keyboard.Escape)) { Close(); return; }
        if (_languageRevision != Localization.Revision) { _languageRevision = Localization.Revision; Build(); }
    }
    protected override void OnDraw(double x, double y) => Draw.Panel(x, y, Width, Height);
    private static void CloseDropdowns(UIElement parent)
    {
        foreach (var child in parent.Children)
        { if (child is UIDropdown dropdown) dropdown.CloseQuietly(); CloseDropdowns(child); }
    }
    internal void Select(string key)
    {
        if (_view == null) return;
        if (_dirty) { Status(L("draft")); return; }
        var option = _view.Buttons.FirstOrDefault(b => b.Key == key);
        if (option != null) { _option = option; _adding = false; Build(); }
    }
    internal bool EditAt(NpcDialogueEditing.View view, double x, double y)
    {
        var render = ContextMenus.InstanceOf(view.Panel.Get("render"));
        if (!render.Exists || !render.Get("surfaceDraw").AsBool || !InsideClip(render, x, y)) return false;
        var option = view.Buttons.FirstOrDefault(o => o.Button.Exists && InsideClip(o.Button, x, y) &&
            Inside(x, y, o.Button.Get("x").AsReal + o.Button.Get("textSpaceX").AsReal,
                o.Button.Get("y").AsReal + o.Button.Get("textSpaceY").AsReal,
                o.Button.Get("textWidth").AsReal, o.Button.Get("textHeight").AsReal));
        if (option == null)
        {
            double scale = render.Get("drawScale").AsReal;
            if (scale <= 0 || render.Get("speakerTextMap").IsUndefined) return false;
            double sx = render.Get("x").AsReal + render.Get("surfaceOffsetX").AsReal;
            double sy = render.Get("y").AsReal + render.Get("surfaceOffsetY").AsReal;
            if (render.Get("drawBottom").AsBool)
                sy += Math.Max(0, render.Get("drawAreaHeight").AsReal - render.Get("surfaceHeight").AsReal * scale);
            double height = Game.CallBuiltinTrusted("ds_map_find_value", default, default, render.Get("speakerTextMap"), "height").AsReal;
            if (!Inside(x, y,
                sx + (render.Get("contentX").AsReal + render.Get("portraitWidth").AsReal + render.Get("speakerSpaceX").AsReal) * scale,
                sy + (render.Get("contentY").AsReal + render.Get("historyHeight").AsReal + render.Get("fontDmgHeight").AsReal + render.Get("speakerSpaceY").AsReal) * scale,
                render.Get("speakerTextWidth").AsReal * scale, height * scale)) return false;
        }
        if (_dirty) { Status(L("draft")); return true; }
        ClosePopup();
        var popup = new TextMenu(this, view);
        void Action(string label, Action action, bool enabled = true)
        {
            popup.Add(new UIButton(label, 4, 4 + popup.Children.Count * 19, 132, 17) { Enabled = enabled })
                .Clicked += _ =>
                {
                    ClosePopup();
                    try { action(); }
                    catch (Exception error) { Open(view); Status(error.Message); Game.Log("Dialogue editor: " + error.Message); }
                };
        }
        Action(L("edit_text"), () =>
        {
            EditInline(view, option, popup.X, popup.Y);
        });
        Action(L("trigger_code"), () => CodePicker(view, option, popup.X, popup.Y),
            option != null || NpcDialogueEditing.CanAdd(view) && view.Buttons.Count > 0);
        if (option != null)
        {
            if (_editing.ConditionFor(view, option.Key) != null)
                Action(L("condition_remove"), () => { _editing.BindCondition(view, option, null); _editing.Refresh(view); _editing.Save(); });
            else Action(L("condition_add"), () => ConditionPicker(view, option, popup.X, popup.Y));
        }
        if (option == null || _editing.Authored(view, option.Key) == null)
            Action(L("restore_original"), () => RestoreOriginal(view, option));
        if (option != null)
        {
            int index = view.Buttons.FindIndex(o => o.Key == option.Key);
            Action(L("move_up"), () => Move(view, option.Key, -1), index > 0 && !view.IsPaging);
            Action(L("move_down"), () => Move(view, option.Key, 1), index < view.Buttons.Count - 1 && !view.IsPaging);
            Action(L("add_above"), () => AddRelative(view, option.Key, false), NpcDialogueEditing.CanAdd(view));
            Action(L("add_below"), () => AddRelative(view, option.Key, true), NpcDialogueEditing.CanAdd(view));
            Action(L("delete_option"), () => DeleteOption(view, option.Key), view.Buttons.Count > 1 && !view.IsPaging);
        }
        if (_editing.HasDeleted(view)) Action(L("restore_deleted"), () => { _editing.RestoreDeleted(view); _editing.Refresh(view); _editing.Save(); });
        Action(L("restore_dialogue"), () => { _editing.RestoreDialogue(view); _editing.Save(); });
        popup.Width = 140; popup.Height = 8 + popup.Children.Count * 19;
        PositionPopup(view, popup, Mouse.X, Mouse.Y);
        ContextPopup = popup; _editing.EditingPanel = view.Panel; Screen?.ShowOverlay(popup, this);
        return true;
    }
    private void ClosePopup()
    {
        if (InlineInput != null)
        {
            InlineInput.Blur(); Screen?.HideOverlay(InlineInput); InlineInput = null;
            if (!Visible) _editing.EditingPanel = default;
        }
        if (ContextPopup == null) return;
        CloseDropdowns(ContextPopup);
        if (ContextPopup.Children.OfType<UITextBox>().Any(t => t.IsFocused)) UITextBox.ReleaseFocus();
        Screen?.HideOverlay(ContextPopup); ContextPopup = null;
        if (!Visible) _editing.EditingPanel = default;
    }
    private void EditInline(NpcDialogueEditing.View view, NpcDialogueEditing.Option? option, double x, double y, string? locale = null)
    {
        locale ??= _editing.PreviewLanguage;
        ClosePopup();
        if (option != null) option = view.Buttons.FirstOrDefault(o => o.Key == option.Key);
        var input = new NativeTextInput(this, view, option, locale) { MaxLength = 8192,
            Tooltip = locale == null ? L("inline_hint") : L("inline_language_hint", locale) };
        input.Text = locale == null ? option?.Label ?? view.Panel.Get("full_text").AsString : _editing.Translation(view, option, locale);
        input.Place();
        InlineInput = input; _editing.EditingPanel = view.Panel; Screen?.ShowOverlay(input, this);
        input.Focus();
    }
    private sealed class NativeTextInput(NpcDialogueEditor owner, NpcDialogueEditing.View view, NpcDialogueEditing.Option? option, string? locale) : UITextBox
    {
        private string? _error;
        private readonly string _openedLanguage = Localization.Language;
        internal void Place()
        {
            var render = ContextMenus.InstanceOf(view.Panel.Get("render"));
            double nx, ny, width, height;
            if (option != null)
            {
                var button = option.Button;
                nx = button.Get("x").AsReal + button.Get("textSpaceX").AsReal;
                ny = button.Get("y").AsReal + button.Get("textSpaceY").AsReal;
                width = button.Get("textWidthMax").AsReal;
                if (width <= 0) width = button.Get("textWidth").AsReal;
                height = button.Get("textHeight").AsReal;
            }
            else
            {
                double scale = render.Get("drawScale").AsReal;
                nx = render.Get("x").AsReal + render.Get("surfaceOffsetX").AsReal +
                    (render.Get("contentX").AsReal + render.Get("portraitWidth").AsReal + render.Get("speakerSpaceX").AsReal) * scale;
                ny = render.Get("y").AsReal + render.Get("surfaceOffsetY").AsReal +
                    (render.Get("contentY").AsReal + render.Get("historyHeight").AsReal + render.Get("fontDmgHeight").AsReal + render.Get("speakerSpaceY").AsReal) * scale;
                if (render.Get("drawBottom").AsBool) ny += Math.Max(0, render.Get("drawAreaHeight").AsReal - render.Get("surfaceHeight").AsReal * scale);
                width = render.Get("speakerTextWidth").AsReal * scale;
                height = Game.CallBuiltinTrusted("ds_map_find_value", default, default, render.Get("speakerTextMap"), "height").AsReal * scale;
            }
            var (kx, ky) = NativeScale();
            X = Mouse.X + (nx - Game.Global["guiMouseX"].AsReal) / kx;
            Y = Mouse.Y + (ny - Game.Global["guiMouseY"].AsReal) / ky;
            Width = Math.Max(10, width / kx); Height = Math.Max(12, height / ky);
        }
        protected override void OnUpdate(double deltaTime)
        {
            var current = view.Panel.Exists ? owner._editing.Inspect(view.Panel) : null;
            if (current == null || current.Root != view.Root || current.Npc != view.Npc ||
                option != null && !option.Button.Exists || Keyboard.Pressed(Keyboard.Escape) || _openedLanguage != Localization.Language) { owner.ClosePopup(); return; }
            base.OnUpdate(deltaTime);
            Place();
            if (Keyboard.Pressed(Keyboard.Enter))
            {
                try
                {
                    if (locale == null) owner.ReplaceText(view, option, Text);
                    else { owner._editing.SetTranslation(view, option, locale, Text); owner._editing.RefreshLanguage(view); owner._editing.Save(); }
                    owner.ClosePopup();
                }
                catch (Exception error) { _error = error.Message; Focus(); }
            }
            else if (!IsFocused) owner.ClosePopup();
        }
        protected override void OnDraw(double x, double y)
        {
            // Cover only the native text area: the text itself becomes the editor.
            Draw.Rectangle(x, y, x + Width, y + Height, Draw.PanelColour);
            Draw.TextWrapped(x, y, IsFocused ? Text.Insert(CaretPosition, "|") : Text, Width, Draw.Muted);
            Draw.Rectangle(x, y + Height, x + Width, y + Height, Draw.Muted);
            if (_error != null) Draw.Text(x, y + Height + 2, _error);
            else if (locale != null) Draw.Text(x, y + Height + 2, L("inline_language_hint", locale), Draw.Muted);
        }
    }
    private static (double X, double Y) NativeScale()
    {
        double unit = Game.Global["window_ratio"].AsReal * Game.Global["cameraScale"].AsReal;
        double kx = unit > 0 && Draw.Width > 0 ? Game.CallBuiltinTrusted("window_get_width", default, default).AsReal / Draw.Width / unit : 1;
        double ky = unit > 0 && Draw.Height > 0 ? Game.CallBuiltinTrusted("window_get_height", default, default).AsReal / Draw.Height / unit : 1;
        return (kx > 0 ? kx : 1, ky > 0 ? ky : 1);
    }
    private static void PositionPopup(NpcDialogueEditing.View view, UIGroup popup, double x, double y)
    {
        var render = ContextMenus.InstanceOf(view.Panel.Get("render"));
        double unit = Game.Global["window_ratio"].AsReal * Game.Global["cameraScale"].AsReal;
        double kx = unit > 0 && Draw.Width > 0 ? Game.CallBuiltinTrusted("window_get_width", default, default).AsReal / Draw.Width / unit : 1;
        double ky = unit > 0 && Draw.Height > 0 ? Game.CallBuiltinTrusted("window_get_height", default, default).AsReal / Draw.Height / unit : 1;
        if (kx <= 0) kx = 1; if (ky <= 0) ky = 1;
        double mx = Mouse.X, my = Mouse.Y, gx = Game.Global["guiMouseX"].AsReal, gy = Game.Global["guiMouseY"].AsReal;
        double left = Math.Clamp(mx + (render.Get("guiVisibleAreaBorderLeft").AsReal - gx) / kx, 0, Draw.Width);
        double right = Math.Clamp(mx + (render.Get("guiVisibleAreaBorderRight").AsReal - gx) / kx, left, Draw.Width);
        double top = Math.Clamp(my + (render.Get("guiVisibleAreaBorderTop").AsReal - gy) / ky, 0, Draw.Height);
        double bottom = Math.Clamp(my + (render.Get("guiVisibleAreaBorderBottom").AsReal - gy) / ky, top, Draw.Height);
        if (right - left < popup.Width || bottom - top < popup.Height) { left = top = 0; right = Draw.Width; bottom = Draw.Height; }
        popup.X = Math.Clamp(x, left, Math.Max(left, right - popup.Width));
        popup.Y = Math.Clamp(y, top, Math.Max(top, bottom - popup.Height));
    }
    private void ReplaceText(NpcDialogueEditing.View view, NpcDialogueEditing.Option? option, string text)
    {
        NpcDialogueEditing.ValidateText(text); _view = view; _option = option;
        if (_editing.IsLocalized(view, option))
        {
            _editing.SetTranslation(view, option, _editing.Language, text); _editing.RefreshLanguage(view); Save(); return;
        }
        if (option != null)
        {
            if (_editing.Authored(view, option.Key) is { } topic) topic.Label[_editing.Language] = text;
            else _editing.Set(view, NpcDialogueEditing.VariantField("option", NpcDialogueEditing.StableKey(view, option.Key), option.OriginalLabel), text, null);
        }
        else
        {
            _editing.Set(view, Field(), text, null); NpcDialogueEditing.DisplayLine(view.Panel, text);
        }
        _editing.Refresh(view); Save();
        if (Visible) { _option = option == null ? null : view.Buttons.FirstOrDefault(o => o.Key == option.Key); Build(); }
    }
    private void RestoreOriginal(NpcDialogueEditing.View view, NpcDialogueEditing.Option? option)
    {
        _editing.RestoreOriginal(view, option); _editing.Save();
        if (Visible) { _view = view; _option = option == null ? null : view.Buttons.FirstOrDefault(o => o.Key == option.Key); Build(); }
    }
    private void Move(NpcDialogueEditing.View view, string key, int direction)
    {
        var option = view.Buttons.FirstOrDefault(o => o.Key == key);
        if (option == null || view.IsPaging) return;
        int position = view.Buttons.IndexOf(option) + 1 + direction;
        if (position < 1 || position > view.Buttons.Count) return;
        _view = view; _option = option;
        if (_editing.Authored(view, key) is { } topic) topic.Position = position;
        else _editing.SetPosition(view, key, position);
        _editing.Refresh(view); Save();
        if (Visible) { _option = view.Buttons.FirstOrDefault(o => o.Key == key); Build(); Status(L("saved")); }
    }
    private void AddRelative(NpcDialogueEditing.View view, string anchor, bool below)
    {
        var topic = _editing.AddRelative(view, anchor, below, L("new_option"), L("new_reply"));
        _editing.Refresh(view); _editing.Save();
        if (view.Buttons.FirstOrDefault(o => o.Key == topic.Fragment) is { } option) EditInline(view, option, 0, 0);
    }
    private void CodePicker(NpcDialogueEditing.View view, NpcDialogueEditing.Option? option, double x, double y)
    {
        ClosePopup();
        var popup = new TextMenu(this, view) { Width = 320, Height = 250 };
        var input = popup.Add(new UITextBox(6, 6, 308) { MaxLength = 128, Placeholder = L("code_search") });
        var status = popup.Add(new UILabel(L("code_hint"), 6, 26) { Width = 308, Height = 32, Wrap = true });
        var page = popup.Add(new UIScrollArea(6, 62, 308, 151) { Padding = 0, Spacing = 3 });
        void Apply(string name)
        {
            try
            {
                string id = _editing.ResolveAction(name);
                if (option != null) _editing.BindAction(view, option, id);
                else _editing.AddCodeRelative(view, view.Buttons.Last().Key, false, id);
                _editing.Refresh(view); _editing.Save();
                ClosePopup();
            }
            catch (Exception error) { status.Text = error.Message; input.Focus(); }
        }
        void Search(string query)
        {
            page.Clear();
            var entries = _editing.CodeOptions.Where(e => e.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var entry in entries)
                page.Add(new UIButton(entry.BuiltIn ? entry.Label + " (StoneForge)" : entry.Id, 0, 0, page.ContentWidth - 6, 18, () => Apply(entry.Id)) { Tooltip = entry.BuiltIn ? entry.Id : entry.Label });
            status.Text = _editing.CodeOptions.Count == 0 ? L("code_empty") : entries.Length == 0 ? L("code_no_matches") : L("code_hint");
        }
        input.TextChanged += Search; input.Submitted += Apply;
        popup.Add(new UIButton(L("code_bind"), 6, 225, 145, 18, () => Apply(input.Text)));
        if (option != null && _editing.ActionFor(view, option) != null && _editing.Authored(view, option.Key)?.ActionId == null)
            popup.Add(new UIButton(L("code_remove"), 157, 225, 157, 18, () =>
            { _editing.BindAction(view, option!, null); _editing.Save(); ClosePopup(); }));
        Search("");
        if (option != null && _editing.ActionFor(view, option) is { } current) input.Text = current;
        PositionPopup(view, popup, x, y); ContextPopup = popup;
        _editing.EditingPanel = view.Panel; Screen?.ShowOverlay(popup, this);
        input.Focus();
    }
    private void ConditionPicker(NpcDialogueEditing.View view, NpcDialogueEditing.Option option, double x, double y)
    {
        ClosePopup();
        var popup = new TextMenu(this, view) { Width = 320, Height = 250 };
        var input = popup.Add(new UITextBox(6, 6, 308) { MaxLength = 128, Placeholder = L("code_search") });
        var status = popup.Add(new UILabel(L("condition_hint"), 6, 26) { Width = 308, Height = 32, Wrap = true });
        var page = popup.Add(new UIScrollArea(6, 62, 308, 151) { Padding = 0, Spacing = 3 });
        void Apply(string name)
        {
            try
            {
                _editing.BindCondition(view, option, name); _editing.Save();
                ClosePopup(); _editing.Refresh(view);
            }
            catch (Exception error) { status.Text = error.Message; input.Focus(); }
        }
        void Search(string query)
        {
            page.Clear();
            var entries = _editing.ConditionOptions.Where(e => e.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var entry in entries)
                page.Add(new UIButton(entry.Id, 0, 0, page.ContentWidth - 6, 18, () => Apply(entry.Id)));
            status.Text = _editing.ConditionOptions.Count == 0 ? L("condition_empty") : entries.Length == 0 ? L("condition_no_matches") : L("condition_hint");
        }
        input.TextChanged += Search; input.Submitted += Apply;
        popup.Add(new UIButton(L("condition_assign"), 6, 225, 308, 18, () => Apply(input.Text)));
        Search(""); PositionPopup(view, popup, x, y); ContextPopup = popup;
        _editing.EditingPanel = view.Panel; Screen?.ShowOverlay(popup, this); input.Focus();
    }
    private void DeleteOption(NpcDialogueEditing.View view, string key)
    {
        _editing.DeleteOption(view, key); _editing.Refresh(view); _editing.Save();
        if (Visible) { _view = view; _option = null; Build(); }
    }
    private sealed class TextMenu(NpcDialogueEditor owner, NpcDialogueEditing.View view) : UIGroup
    {
        protected override void OnDraw(double x, double y) => Draw.Panel(x, y, Width, Height);
        protected override void OnUpdate(double deltaTime)
        {
            var current = view.Panel.Exists ? owner._editing.Inspect(view.Panel) : null;
            if (current == null || current.Npc != view.Npc || current.Root != view.Root || UIWindow.AnyOpen ||
                Keyboard.Pressed(Keyboard.Escape) || Mouse.Pressed() && !Contains(Mouse.X, Mouse.Y)) owner.ClosePopup();
        }
    }
    private static bool Inside(double x, double y, double left, double top, double width, double height) =>
        width > 0 && height > 0 && x >= left && x < left + width && y >= top && y < top + height;
    private static bool InsideClip(Instance instance, double x, double y) =>
        x >= instance.Get("guiVisibleAreaBorderLeft").AsReal && x < instance.Get("guiVisibleAreaBorderRight").AsReal &&
        y >= instance.Get("guiVisibleAreaBorderTop").AsReal && y < instance.Get("guiVisibleAreaBorderBottom").AsReal;
    private void Build()
    {
        if (_view == null) return;
        if (Screen != null) UITextBox.ReleaseFocusIn(Screen);
        CloseDropdowns(this); Clear(); _dirty = false;
        Width = Math.Min(310, Draw.Width - 16); Height = Math.Min(320, Draw.Height - 16);
        var page = Add(new UIScrollArea(8, 8, Width - 16, Height - 48) { Padding = 0, Spacing = 6 });
        page.AddHeader(L("title")); page.AddText(L("scope", _view.Npc, _editing.Language), Draw.Muted);
        var view = _view;
        if (!_adding)
        {
            var options = view.Buttons.ToArray();
            var labels = new[] { L("line") }.Concat(options.Select((o, i) => (i + 1) + ": " + o.Label));
            var picker = page.Add(new UIDropdown(labels, 0, 0, page.ContentWidth - 6, _option == null ? 0 : Array.FindIndex(options, o => o.Key == _option.Key) + 1));
            picker.Changed += i =>
            {
                if (_dirty) { picker.SelectedIndex = _option == null ? 0 : Array.FindIndex(options, o => o.Key == _option.Key) + 1; Status(L("draft")); return; }
                _option = i == 0 ? null : options[i - 1]; Build();
            };
        }
        page.AddText(L(_adding ? "new_label" : "text_hint"));
        _text = page.Add(new UITextBox(0, 0, page.ContentWidth - 6) { MaxLength = 16384 });
        _text.Text = _adding ? "" : DialogueEditor.Encode(_option?.Label ?? view.Panel.Get("full_text").AsString);
        _text.TextChanged += _ => { _dirty = true; Status(L("draft")); };
        if (_adding || _option != null)
        {
            page.AddText(L("position")); _position = page.Add(new UITextBox(0, 0, 60) { MaxLength = 3 });
            _position.Text = (_adding ? view.Buttons.Count + 1 : view.Buttons.FindIndex(o => o.Key == _option!.Key) + 1).ToString(CultureInfo.InvariantCulture);
            _position.TextChanged += _ => { _dirty = true; Status(L("draft")); };
        }
        else _position = null;
        if (_adding || _option != null && _editing.Authored(view, _option.Key) is { ActionId: null })
        {
            page.AddText(L("reply")); _reply = page.Add(new UITextBox(0, 0, page.ContentWidth - 6) { MaxLength = 16384 });
            var topic = _option == null ? null : _editing.Authored(view, _option.Key);
            _reply.Text = topic == null ? "" : DialogueEditor.Encode(topic.Reply.GetValueOrDefault(_editing.Language) ?? topic.Reply.GetValueOrDefault(Localization.DefaultLanguage) ?? "");
            _reply.TextChanged += _ => { _dirty = true; Status(L("draft")); };
        }
        else _reply = null;
        page.AddText(L("hint"), Draw.Muted); _status = page.AddText(L("ready"), Draw.Muted);
        var buttons = Add(new UIButtonRow(8, Height - 34, Width - 16));
        buttons.Add(L("apply"), () => Try(() => { Apply(); Status(L("applied")); }));
        buttons.Add(L("save"), () => Try(() => { if (_dirty) Apply(); Save(); Status(L("saved")); }));
        buttons.Add(L("add"), () => { if (_dirty) { Status(L("draft")); return; } _adding = true; _option = null; Build(); });
        buttons.Add(L(_option != null && _editing.Authored(view, _option.Key) != null ? "delete" : "reset"), () => Try(Reset));
        buttons.Add(L("done"), Close);
    }
    private void Apply()
    {
        var view = _view!; string text = DialogueEditor.Decode(_text!.Text);
        NpcDialogueEditing.ValidateText(text);
        int? position = null;
        if (_position != null)
        {
            if (!int.TryParse(_position.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)) throw new ArgumentException(L("invalid_position"));
            NpcDialogueEditing.ValidatePosition(value); position = value;
        }
        string? reply = _reply == null ? null : DialogueEditor.Decode(_reply.Text);
        if (reply != null) NpcDialogueEditing.ValidateText(reply);
        if (_adding)
        {
            var topic = _editing.Add(view, text, reply!, position!.Value); _adding = false;
            _editing.Refresh(view); _option = view.Buttons.FirstOrDefault(o => o.Key == topic.Fragment);
        }
        else
        {
            if (_option != null && _editing.Authored(view, _option.Key) is { } topic)
            { topic.Label[_editing.Language] = text; if (topic.ActionId == null) topic.Reply[_editing.Language] = reply!; topic.Position = position!.Value; }
            else if (_editing.IsLocalized(view, _option))
            { _editing.SetTranslation(view, _option, _editing.Language, text); if (_option != null) _editing.SetPosition(view, _option.Key, position!.Value); }
            else if (_option != null) _editing.SetOption(view, _option, text, position!.Value);
            else _editing.Set(view, Field(), text, position);
            if (_option == null) { NpcDialogueEditing.DisplayLine(view.Panel, text); _editing.Refresh(view); }
            else { string key = _option.Key; _editing.Refresh(view); _option = view.Buttons.FirstOrDefault(o => o.Key == key); }
        }
        Build();
    }
    private string Field() => _option == null ? NpcDialogueEditing.VariantField("line", NpcDialogueEditing.LineKey(_view!), _view!.OriginalLine)
        : NpcDialogueEditing.VariantField("option", NpcDialogueEditing.StableKey(_view!, _option.Key), _option.OriginalLabel);
    private void Save()
    {
        _editing.Save();
    }
    private void Reset()
    {
        if (_adding) { _adding = false; Build(); return; }
        if (_option != null && _editing.Authored(_view!, _option.Key) is { } authored)
        {
            _editing.Delete(authored);
            _view!.Options = _view.Options.Where(o => o != authored.Fragment).ToArray();
            _option = null; _editing.Refresh(_view); _editing.Save(); Build(); return;
        }
        {
            if (_option != null) _editing.ResetOption(_view!, _option);
            else
            {
                _editing.Reset(_view!, Field());
                _editing.Reset(_view!, "line/" + NpcDialogueEditing.LineKey(_view!));
            }
            if (_option == null) { NpcDialogueEditing.DisplayLine(_view!.Panel, _view.OriginalLine); _editing.Refresh(_view); }
            else { string key = _option.Key; _editing.Refresh(_view!); _option = _view!.Buttons.FirstOrDefault(o => o.Key == key); }
        }
        Save(); Build(); Status(L("reset_done"));
    }
    private void Status(string text) { if (_status != null) _status.Text = text; }
    private void Try(Action action)
    { try { action(); } catch (Exception error) { Status(error.Message); Game.Log("NPC dialogue editor: " + error.Message); } }
}
