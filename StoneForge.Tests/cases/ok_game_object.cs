using StoneForge;

namespace TestMod;

public class Marker : GameObject
{
    public Marker() : base("marker", "o_enemy") { Sprite = "s_dummy"; Persistent = true; }
    protected override bool ReplacesDraw => true;
    protected override void OnCreate(Instance self) => self["life"] = 60;
    protected override void OnStep(Instance self) => self["life"] = self["life"].AsInt - 1;
    protected override void OnAlarm(Instance self, int alarm) { }
}

public class M : IStoneMod
{
    public void Unload() { }
    public void Load(ModContext context) => context.Objects.Add(new Marker());
}
