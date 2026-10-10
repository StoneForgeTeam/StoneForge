namespace StoneForge;

/// <summary>One of a mod's UI screens (<see cref="ModContext.UI"/>: <see cref="ModUI.MainMenu"/>,
/// <see cref="ModUI.InGame"/>...): the whole GUI is its area; add elements to it. While it's in its context
/// (<see cref="IsActive"/>) the loader updates, draws and routes the mouse to them every frame, in the Draw GUI
/// pass; out of it, it's left alone - and as it leaves, nothing stays held: the mouse lets go, an open dropdown
/// closes, a text box lets go of the keyboard.</summary>
public sealed class UIScreen : UIElement
{
    private const double TooltipDelay = 0.35;

    private UIElement? _hovered, _pressed;
    private readonly List<UIElement> _overlays = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _last, _hoverTime;
    private readonly Func<bool> _active;
    private bool _wasActive;

    internal UIScreen(Func<bool> active, string owner, UILayer layer = UILayer.Gui)
    {
        HitTest = false;
        _active = active;
        Owner = owner;
        Layer = layer;
    }

    /// <summary>Where it's drawn: over everything, or with the game's HUD (under its windows).</summary>
    public UILayer Layer { get; }

    // The mod it's for.
    internal string Owner { get; }

    /// <summary>Whether it's in its context (shown) this frame.</summary>
    public bool IsActive => _wasActive;
    /// <summary>It came into its context (it's shown from this frame).</summary>
    public event Action? Shown;
    /// <summary>It left its context (it's no longer shown).</summary>
    public event Action? Hidden;

    /// <summary>Shows an element over everything else on the screen (a dropdown's list): drawn last, given
    /// the mouse first. <paramref name="owner"/> opened it. Place it with screen coordinates.</summary>
    public void ShowOverlay(UIElement overlay, UIElement owner)
    {
        overlay.OverlayOf = owner;
        if (!_overlays.Contains(overlay))
            _overlays.Add(overlay);
    }

    public void HideOverlay(UIElement overlay)
    {
        _overlays.Remove(overlay);
        overlay.OverlayOf = null;
    }

    // Once a frame: in its context, sized to the screen, the mouse routed, everything updated and drawn.
    internal void Frame()
    {
        bool active;
        try { active = _active(); }
        catch (Exception e)
        {
            Game.Log($"A UI screen's condition threw: {e.Message}");
            active = false;
        }
        double now = _clock.Elapsed.TotalSeconds, delta = now - _last;
        _last = now;
        if (active != _wasActive)
        {
            _wasActive = active;
            if (active)
                delta = 0;
            else
                Leave();
            Raise(active ? Shown : Hidden);
        }
        if (!active)
            return;
        Width = Draw.Width;
        Height = Draw.Height;

        if (!Mouse.HasFocus)
        {
            // Keep the last UI visible, without treating an alt-tab release as a click
            // or consuming typing/scrolling intended for the other application.
            ReleaseInput();
            DrawContents();
            return;
        }

        double mx = Mouse.X, my = Mouse.Y;
        UIElement? hovered = null;
        // (A window open - on any mod's screen - takes the mouse: only the one on top, and what's in it.)
        var top = UIWindow.Top;
        if (top == null || Contains(top))
        {
            for (int i = _overlays.Count - 1; i >= 0 && hovered == null; i--)
                if (top == null || IsIn(_overlays[i], top))
                    hovered = _overlays[i].HitAt(mx, my);
            hovered ??= top != null ? top.HitAt(mx, my) : HitAt(mx, my);
        }
        // (On the HUD: where the game's GUI drawn over it - a window, the bottom panel - is under the mouse, the mouse
        // is the game's.)
        if (hovered != null && Layer == UILayer.Hud && Mouse.GameUIUnder(nearerThan: Hooks.HudDepth))
            hovered = null;
        // What the mouse is on (even disabled) - and what's held, while dragged off it - is kept from the game.
        if (hovered != null)
            InputBlock.Report(RootOf(hovered));
        if (_pressed != null && Mouse.Down())
            InputBlock.Report(RootOf(_pressed));
        if (hovered != null && !hovered.Enabled)
            hovered = null;
        if (hovered != _hovered)
        {
            _hovered?.SetHovered(false);
            hovered?.SetHovered(true);
            _hovered = hovered;
            _hoverTime = 0;
        }
        _hoverTime += delta;
        if (Mouse.Pressed() && hovered != null)
        {
            _pressed = hovered;
            hovered.IsPressed = true;
            hovered.RaisePress();
        }
        if (_pressed != null && !Mouse.Down())
        {
            var pressed = _pressed;
            _pressed = null;
            pressed.IsPressed = false;
            if (pressed == hovered)
                pressed.RaiseClick();
        }
        int wheel = Mouse.Wheel;
        if (wheel != 0)
            for (var element = hovered; element != null; element = element.Parent ?? element.OverlayOf)
                if (element.Enabled && element.RaiseWheel(wheel))
                    break;

        RunUpdate(delta);
        foreach (var overlay in _overlays.ToArray())
            overlay.RunUpdate(delta);
        DrawContents();
        if (_pressed == null && _hoverTime >= TooltipDelay && (_hovered == null || !InOpenWindow(_hovered)))
            DrawTooltip(mx, my);
    }

    private void DrawContents()
    {
        // (Open windows - and what of theirs is over them - are drawn after every screen: DrawWindows.)
        foreach (var child in Children.ToArray())
            if (child is not UIWindow { IsOpen: true })
                child.RunDraw();
        foreach (var overlay in _overlays.ToArray())
            if (!InOpenWindow(overlay))
                overlay.RunDraw();
    }

    // After every screen has drawn: the open windows, over everything (in the order they opened), each with
    // its dropdowns' lists and the tooltip of what's hovered in it.
    internal static void DrawWindows()
    {
        foreach (var window in UIWindow.InOrder)
        {
            if (window.ScreenOf is not UIScreen screen || !screen.IsActive || Hooks.IsSuspended(screen.Owner))
                continue;
            window.RunDraw();
            foreach (var overlay in screen._overlays.ToArray())
                if (IsIn(overlay, window))
                    overlay.RunDraw();
            if (screen._pressed == null && screen._hoverTime >= TooltipDelay && screen._hovered != null && IsIn(screen._hovered, window))
                screen.DrawTooltip(Mouse.X, Mouse.Y);
        }
    }

    // Whether an element is in (or is) another - through its parents, and the elements its overlays belong to.
    private static bool IsIn(UIElement element, UIElement container)
    {
        for (UIElement? e = element; e != null; e = e.Parent ?? e.OverlayOf)
            if (e == container)
                return true;
        return false;
    }

    private bool Contains(UIElement element) => IsIn(element, this);

    private static bool InOpenWindow(UIElement element)
    {
        for (UIElement? e = element; e != null; e = e.Parent ?? e.OverlayOf)
            if (e is UIWindow { IsOpen: true })
                return true;
        return false;
    }

    // Out of its context: nothing held - the mouse, an open dropdown's list, the keyboard.
    private void Leave()
    {
        foreach (var overlay in _overlays.ToArray())
        {
            if (overlay.OverlayOf is UIDropdown dropdown)
                dropdown.CloseQuietly();
            else
                HideOverlay(overlay);
        }
        ReleaseInput();
        UIWindow.ShutOn(this);
    }

    private void ReleaseInput()
    {
        _hovered?.SetHovered(false);
        _hovered = null;
        if (_pressed != null)
            _pressed.IsPressed = false;
        _pressed = null;
        _hoverTime = 0;
        UITextBox.ReleaseFocusIn(this);
    }

    private static void Raise(Action? handlers)
    {
        if (handlers == null)
            return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception e) { Game.Log($"A UI screen's Shown/Hidden handler threw: {e}"); }
        }
    }

    // The hovered element's tooltip (or its nearest parent's), in the game's hover frame by the mouse.
    private void DrawTooltip(double mx, double my)
    {
        string? text = null;
        for (var element = _hovered; element != null && text == null; element = element.Parent ?? element.OverlayOf)
            text = string.IsNullOrEmpty(element.Tooltip) ? null : element.Tooltip;
        if (text == null)
            return;
        const double pad = 6, maxWidth = 200;
        double textWidth = Math.Min(Draw.TextWidth(text), maxWidth);
        double width = textWidth + pad * 2, height = Draw.TextHeight(text, textWidth) + pad * 2;
        double x = mx + 14, y = my + 14;
        if (x + width > Width)
            x = mx - width - 4;
        if (y + height > Height)
            y = Height - height;
        Draw.Frame(x, y, width, height);
        Draw.TextWrapped(x + pad, y + pad, text, textWidth);
    }

    // The element on the screen (or the overlay) an element is part of: the area it covers.
    private UIElement RootOf(UIElement element)
    {
        while (element.Parent != null && element.Parent != this)
            element = element.Parent;
        return element;
    }

    /// <summary>Whether the mouse is over any of this mod's UI.</summary>
    public bool MouseOverUI => _hovered != null;
}
