namespace StoneForge.Loader;

/// <summary>A mod's settings (<see cref="ModSettings"/>) on its page in the Mods window: under a Settings
/// header, each with the game's control for it - a checkbox, a slider, a dropdown, a text box - and a button
/// putting them all back to their defaults. Changed, they're set (saved, and the mod told) at once.</summary>
internal static class SettingsPage
{
    // Where a setting's control starts, right of its name; how wide it is (the game's Settings comboboxes',
    // a slider's bar a little shorter for its value beside it).
    private const double ControlX = 110, ControlWidth = 125, SliderWidth = 120, RowWidth = 280;

    // Its settings on the page (nothing if it has none to show - or isn't loaded, so none are declared).
    // reopen shows the page again (after a reset, with the defaults).
    internal static void Add(UIScrollArea page, string mod, Action reopen)
    {
        if (!ModSettings.ByMod.TryGetValue(mod, out var settings))
            return;
        var shown = settings.All.Where(s => s.Visible).ToList();
        if (shown.Count == 0)
            return;
        page.AddHeader(Localization.Get("settings.title"));
        foreach (var setting in shown)
        {
            switch (setting)
            {
                case ToggleSetting toggle:
                    page.AddCheckbox(toggle.Label, toggle.Value, toggle.Tooltip).Changed += on => toggle.Value = on;
                    break;
                case SliderSetting slider:
                {
                    var row = Row(page, slider);
                    var control = row.Add(new UISlider(ControlX, 1, slider.Min, slider.Max, slider.Value, SliderWidth)
                    {
                        Step = slider.Step,
                        Format = slider.Format,
                        Tooltip = slider.Tooltip,
                    });
                    control.Changed += value => slider.Value = value;
                    break;
                }
                case ChoiceSetting choice:
                {
                    var row = Row(page, choice);
                    var control = row.Add(new UIDropdown(choice.Options, ControlX, 0, ControlWidth, choice.Value) { Tooltip = choice.Tooltip });
                    control.Changed += index => choice.Value = index;
                    break;
                }
                case TextSetting text:
                {
                    var row = Row(page, text);
                    var control = row.Add(new UITextBox(ControlX, 0, ControlWidth, text.Value) { MaxLength = text.MaxLength, Tooltip = text.Tooltip });
                    control.TextChanged += value => text.Value = value;
                    break;
                }
            }
        }
        var buttons = page.Add(new UIGroup(5, 0, RowWidth, 26));
        buttons.Add(new UIButton(Localization.Get("settings.reset"), 0, 0, onClick: () =>
        {
            settings.ResetAll();
            reopen();
        }) { Tooltip = Localization.Get("settings.reset_tooltip") });
    }

    // A row: the setting's name, its control right of it.
    private static UIGroup Row(UIScrollArea page, ModSetting setting)
    {
        var row = page.Add(new UIGroup(5, 0, RowWidth, 16) { Tooltip = setting.Tooltip });
        row.Add(new UILabel(setting.Label, 0, 2, Draw.Muted));
        return row;
    }
}
