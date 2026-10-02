namespace StoneForge;

/// <summary>A checkbox with a label: the game's checkbox sprite; <see cref="Changed"/> when clicked.</summary>
public class UICheckbox : UIElement
{
    public string Text { get; set; } = "";
    public bool Checked { get; set; }
    public event Action<bool>? Changed;

    public UICheckbox() { Height = 16; Width = 120; }
    public UICheckbox(string text, double x, double y, bool isChecked = false) : this()
    {
        Text = text; X = x; Y = y; Checked = isChecked;
    }

    protected override void OnDraw(double x, double y)
    {
        int sprite = (int)global::StoneForge.Sprite.s_checkbox;
        Game.CallBuiltin("draw_sprite", sprite, Checked ? 1 : 0, x, y);
        if (IsHovered)
            Game.CallBuiltin("draw_sprite_ext", sprite, 2, x, y, 1, 1, 0, Draw.White, 0.15);
        double size = Draw.SpriteHeight(sprite);
        Draw.Text(x + Draw.SpriteWidth(sprite) + 5, y + size / 2, Text, Draw.Muted, Draw.AlignLeft, Draw.AlignMiddle);
    }

    protected override void OnMouseEnter() => UISounds.Hover();

    protected override void OnClick()
    {
        Checked = !Checked;
        Gm.AudioPlaySound(Checked ? Sound.snd_checkbox_on : Sound.snd_checkbox_off, 4);
        Changed?.Invoke(Checked);
    }
}
