using StoneForge;

public sealed class LocalizedMod : IStoneMod
{
    public void Load(ModContext context)
    {
        context.Log(context.Localization.Get("reward", 100));
        context.Localization.LanguageChanged += () => context.Log(context.Localization.Language);
    }
    public void Unload() { }
}
