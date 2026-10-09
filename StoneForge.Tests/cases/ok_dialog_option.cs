using StoneForge;
public class CodeOptionMod : IStoneMod
{
    public void Load(ModContext context) { }
    public void Unload() { }
    [DialogOption("work")]
    public static void Work(DialogOptionContext dialogue) => dialogue.Mod.Log(dialogue.Id);
    [DialogCondition("available")]
    private static DialogConditionResult Available(DialogOptionContext dialogue) => dialogue.Speaker.Exists ? DialogConditionResult.Enabled : DialogConditionResult.Visible;
    [DialogOption("hello")]
    public static void Hello() { }
}
