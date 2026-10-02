namespace StoneForge;

// The sounds the game's own controls make (its Settings menu's sliders and comboboxes...), for ours.
internal static class UISounds
{
    // The game's hover sound, with its small random pitch.
    public static void Hover() => Scripts.scr_audio_play_hover_pitch.Call(null, GmValue.From(Sound.snd_button_enter));
    public static void Play(Sound sound, int priority = 3) => Gm.AudioPlaySound(sound, priority);
}
