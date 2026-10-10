namespace StoneForge.Loader;

internal sealed class ReportWindow(ModContext context) : UIWindow(Localization.Get("report.title"))
{
    private ReportTarget[] _targets = [];
    private UIDropdown? _mods;
    private UITextBox? _title, _description;
    private UICheckbox? _logs;
    private UILabel? _status, _repository, _notice;
    private UIButton? _send, _close;
    private string _draftTitle = "", _draftDescription = "", _selectedId = Hooks.LoaderId;
    private string _message = "report.help";
    private int? _httpStatus;
    private bool _includeLogs = true;
    private Task<ReportResult>? _pending;
    private int _languageRevision = -1;
    private double _labelsWidth = -1;
    private string? _shownRepository;

    internal new static void Install(ModContext context) => InstallWhenReady(context, () => ModManager.Startup.Finished);

    internal static void InstallWhenReady(ModContext context, Func<bool> ready)
    {
        var window = context.UI.InGame.Add(new ReportWindow(context));
        bool registered = false;
        // Completion is processed on the game thread even if the report window was closed.
        context.Frame += () =>
        {
            // Adding a button schedules EscMenu's instance_find/rebuild. The YYC runner cannot
            // safely service that lookup during its first startup frames, before game data is ready.
            if (!registered && ready())
            {
                EscMenu.BeginLoad();
                try { EscMenu.AddButton(context, () => Localization.Get("report.button"), window.Open); registered = true; }
                finally { EscMenu.EndLoad(); }
            }
            window.Poll();
        };
    }
    protected override void OnFit()
    {
        double width = Math.Min(440, Draw.Width - 24), height = Math.Min(340, Draw.Height - 20);
        Frame.X += (Frame.Width - width) / 2; Frame.Y += (Frame.Height - height) / 2;
        Frame.Width = width; Frame.Height = height;
        ContentInsets = new(16, 28, 16, 10);
    }
    protected override void OnOpen()
    {
        _languageRevision = -1;
        _labelsWidth = -1; _shownRepository = null;
        _targets = BugReporter.Targets(context);
        int selected = Array.FindIndex(_targets, m => m.Id == _selectedId);
        _selectedId = _targets[Math.Max(0, selected)].Id;
        _mods = Content.Add(new UIDropdown(_targets.Select(m => m.Name + " (" + m.Id + ")"), 0, 14,
            Content.Width, Math.Max(0, selected)) { MaxVisibleRows = 8 });
        _mods.Changed += index => { _selectedId = _targets[index].Id; _message = "report.help"; };
        _repository = Content.Add(new UILabel("", 0, 32, Draw.Muted));
        _title = Content.Add(new UITextBox { X = 0, Y = 62, Width = Content.Width, Height = 17, MaxLength = 120, Text = _draftTitle });
        _description = Content.Add(new UITextBox { X = 0, Y = 100, Width = Content.Width,
            Height = 90, MaxLength = 4000, Multiline = true, Text = _draftDescription });
        _logs = Content.Add(new UICheckbox("", 0, 0, _includeLogs));
        _notice = Content.Add(new UILabel { Wrap = true, Colour = Draw.Muted });
        _status = Content.Add(new UILabel { Wrap = true });
        _send = Content.Add(new UIButton("", 0, 0, 110, onClick: Send));
        _close = Content.Add(new UIButton("", 0, 0, 100, onClick: Close));
        Content.Add(new FieldLabel("report.mod", 0));
        Content.Add(new FieldLabel("report.subject", 48));
        Content.Add(new FieldLabel("report.description", 86));
        Refresh();
    }
    protected override void OnClosed()
    {
        _draftTitle = _title?.Text ?? _draftTitle; _draftDescription = _description?.Text ?? _draftDescription;
        _includeLogs = _logs?.Checked ?? _includeLogs;
        _title?.Blur(); _description?.Blur(); _mods?.Close();
    }
    protected override void OnUpdate(double deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (IsOpen) Refresh();
    }
    private ReportTarget? Selected => _mods != null && _mods.SelectedIndex >= 0 && _mods.SelectedIndex < _targets.Length
        ? _targets[_mods.SelectedIndex] : null;
    private bool Eligible => Selected is { } selected && BugReporter.Targets(context).Any(m => m.Id == selected.Id && m.Repo == selected.Repo);
    private void Refresh()
    {
        if (_mods == null || _title == null || _description == null) return;
        bool sending = _pending != null;
        _mods.Width = _title.Width = _description.Width = Content.Width;
        if (_labelsWidth != Content.Width)
        {
            _labelsWidth = Content.Width; _shownRepository = null;
            for (int i = 0; i < _targets.Length; i++)
                _mods.Options[i] = FitText(_targets[i].Name + " (" + _targets[i].Id + ")", Content.Width - 20);
        }
        _description.Height = Math.Max(24, Content.Height - 204);
        _mods.Enabled = _title.Enabled = _description.Enabled = _logs!.Enabled = !sending;
        _logs.Y = Content.Height - 98; _logs.Width = Content.Width;
        _notice!.Y = Content.Height - 78; _notice.Width = Content.Width;
        _status!.Y = Content.Height - 50; _status.Width = Content.Width;
        _send!.Y = _close!.Y = Content.Height - 26; _close.X = Content.Width - _close.Width;
        string repository = Selected?.Repo ?? Localization.Get("report.empty");
        if (_shownRepository != repository)
        {
            _shownRepository = repository;
            _repository!.Text = FitText(repository, Content.Width); _repository.Tooltip = repository;
        }
        _status.Text = Localization.Get(_message) + (_httpStatus is { } status && _message != "report.sent" ? $" (HTTP {status})" : "");
        _send.Enabled = !sending && Eligible && _title.Text.Trim().Length >= 5 && _description.Text.Trim().Length >= 20;
        if (_languageRevision == Localization.Revision) return;
        _languageRevision = Localization.Revision;
        Title = Localization.Get("report.title"); _send.Text = Localization.Get("report.send");
        _close.Text = Localization.Get("common.close"); _logs.Text = Localization.Get("report.logs");
        _notice.Text = Localization.Get("report.notice");
    }
    private void Send()
    {
        if (_pending != null || Selected is not { } target) return;
        if (!Eligible) { _message = "report.unavailable"; return; }
        _title!.Blur(); _description!.Blur();
        _draftTitle = _title.Text; _draftDescription = _description.Text;
        _selectedId = target.Id;
        try
        {
            object payload = BugReporter.Payload(target, _draftTitle, _draftDescription, _logs!.Checked);
            _pending = BugReporter.Default.SendAsync(target, _draftTitle, _draftDescription, payload);
            _message = "report.sending";
            _httpStatus = null;
        }
        catch (Exception e) { _message = "report.failed"; Game.Log("Bug reporter metadata failed: " + e.Message); }
    }
    private static string FitText(string text, double width)
    {
        if (text.Length <= 200 && Draw.TextWidth(text) <= width) return text;
        int low = 0, high = Math.Min(text.Length, 200);
        while (low < high)
        {
            int middle = low + (high - low + 1) / 2;
            if (Draw.TextWidth(text[..middle] + "…") <= width) low = middle; else high = middle - 1;
        }
        if (low > 0 && char.IsHighSurrogate(text[low - 1])) low--;
        return text[..low] + "…";
    }
    internal void Poll()
    {
        if (_pending?.IsCompleted != true) return;
        var finished = _pending; _pending = null;
        try
        {
            var result = finished.GetAwaiter().GetResult(); _message = result.MessageKey; _httpStatus = result.HttpStatus;
            Game.Log($"BugDrop submission: {result.MessageKey}; HTTP {result.HttpStatus?.ToString() ?? "none"}; " +
                $"code {result.ServiceCode ?? "none"}; request {result.PayloadBytes} bytes; {result.ServiceError ?? ""}");
            if (result.Success)
            {
                _draftTitle = _draftDescription = "";
                if (_title != null) _title.Text = "";
                if (_description != null) _description.Text = "";
            }
        }
        catch (Exception e) { _message = "report.failed"; Game.Log("Bug reporter failed: " + e.Message); }
    }
    private sealed class FieldLabel(string key, double top) : UILabel("", 0, top)
    {
        protected override void OnUpdate(double deltaTime) { Text = Localization.Get(key); base.OnUpdate(deltaTime); }
    }
}
