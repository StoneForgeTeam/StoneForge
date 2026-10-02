using System;
using StoneForge;

// Install only in a disposable development game copy. Exercises API behavior without loading a save.
public sealed class ReliabilityProbe : IStoneMod, ITickable
{
    private ModContext _context = null!;
    private Instance _captured;
    private bool _done;
    private int _sprite;

    public void Load(ModContext context)
    {
        _context = context;
        _sprite = context.LoadSprite("probe.png");
        if (_sprite < 0 || context.LoadSprite("probe.png") != _sprite)
            throw new Exception("Sprite import or deduplication failed.");
        context.Log("LIVE PASS sprite import/deduplication: " + _sprite);
        context.OnCode("gml_Object_o_stonemod_gui_Draw_75", before: (self, other) =>
        {
            if (_captured.IsNone) _captured = self;
            return false;
        });
    }

    public void Tick(double deltaTime)
    {
        if (Game.Global["stoneforge_probe_fault"].AsBool) throw new Exception("Intentional development fault");
        if (_done || _captured.IsNone || !Gm.InMainMenu) return;
        _done = true;
        if (!_captured.Exists || _captured.Get("object_index").IsUndefined)
            throw new Exception("Captured instance expired instead of resolving by ID.");
        _context.Log("LIVE PASS stored callback instance resolves next frame");
        try
        {
            Game.CallBuiltinAs("instance_exists", _captured, _captured, _captured.Persist());
            _context.Log("LIVE PASS stored instance resolves script self");
        }
        catch (GameCallException e) { _context.Log("LIVE LIMITATION self lookup: " + e.Message); }

        int obj = Game.CallBuiltin("asset_get_index", "o_stonemod_modal").AsInt;
        Instance made = Game.CallBuiltin("instance_create_depth", 0, 0, 0, obj);
        if (made.IsNone || !made.Exists) throw new Exception("Probe instance could not be created");
        Game.CallBuiltin("instance_destroy", made);
        if (made.Exists || !made.Get("x").IsUndefined || made.Set("x", 1))
            throw new Exception("Destroyed instance remained usable.");
        _context.Log("LIVE PASS destroyed instance safely rejected");
    }

    public void Unload() => _context.Log("LIVE probe unload: sprite " + _sprite);
}
