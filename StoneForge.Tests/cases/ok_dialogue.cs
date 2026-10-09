using StoneForge;
public class ConversationMod : IStoneMod
{
    public void Load(ModContext context)
    {
        var talk = context.Dialogues.Add(new DialogueDefinition("work", "offer")
        {
            Nodes = { new DialogueNode("offer", "Can you help?")
            { Choices = { new DialogueChoice("accept", "Yes") { OnSelected = _ => context.Log("Accepted"), EnabledWhen = _ => Player.Exists } } } }
        });
        talk.AddTopic(npc => npc.Get("id_name").AsString == "osbrook_smith", _ => "Ask about work", position: 3);
    }
    public void Unload() { }
}
