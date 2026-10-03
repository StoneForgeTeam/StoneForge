using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;

// Functions defined inside another script's file: easeOutCubic (in EasingEquations - the probe calls it), the vineyard
// thief's wine check and a Gwynel house cutscene step (only hooked, to show they can be).
[assembly: HookScript(nameof(Scripts.easeOutCubic))]
[assembly: HookScript(nameof(Scripts.scr_npc_lines_vineyard_thief_check_wine))]
[assembly: HookScript(nameof(Scripts.scr_rewards_find_guinnel_1))]

// Install only in a disposable development game copy. Exercises API behavior in the main menu, without loading a save;
// once a save is loaded, the off-screen instances too. Every check logs LIVE PASS / LIVE FAIL (LIVE INFO: what the game
// itself does, for the record).
public sealed class ReliabilityProbe : IStoneMod, ITickable
{
    private ModContext _context = null!;
    private Instance _captured;
    private bool _done, _inGameDone;
    private int _sprite, _passed, _failed;
    // easeOutCubic's hooks: what they saw, while the probe calls it (the game's own calls only counted).
    private bool _probing;
    private int _gameCalls;
    private readonly List<double> _beforeArgs = new(), _afterResults = new();

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
        // (A script the game data doesn't hook: the hook would never be called, so it's refused at once.)
        try
        {
            context.OnScript("scr_is_cutscene", call => false);
            Fail("hook check: an unhooked script", "the hook was accepted");
        }
        catch (ArgumentException e) { Pass("hook check: an unhooked script", e.Message.Split('\n')[0]); }
        Check("hook check: a hookable script", () => { context.OnScript("scr_atr", call => false); return true; });
        Check("hook check: functions inside another script's file", () =>
        {
            Scripts.scr_npc_lines_vineyard_thief_check_wine.Before(context, call => false);
            Scripts.scr_rewards_find_guinnel_1.Before(context, call => false);
            Scripts.easeOutCubic.Before(context, call =>
            {
                if (_probing) _beforeArgs.Add(call.Args[0].AsReal);
                else _gameCalls++;
                return false;
            });
            Scripts.easeOutCubic.After(context, call =>
            {
                if (!_probing) return;
                _afterResults.Add(call.Result.AsReal);
                call.Result = call.Result + 1;
            });
            return true;
        });
    }

    public void Tick(double deltaTime)
    {
        if (Game.Global["stoneforge_probe_fault"].AsBool) throw new Exception("Intentional development fault");
        if (!_inGameDone && Gm.InGame)
        {
            _inGameDone = true;
            OffScreen();
        }
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
        Alarms(made);
        Game.CallBuiltin("instance_destroy", made);
        if (made.Exists || !made.Get("x").IsUndefined || made.Set("x", 1))
            throw new Exception("Destroyed instance remained usable.");
        _context.Log("LIVE PASS destroyed instance safely rejected");
        Check("destroyed instance is gone, not culled", () => made.IsGone && !made.IsCulled);

        ArraysAndStructs();
        DsMaps();
        DsLists();
        SeededRandom();
        ScriptHooks();
        Check("busy and cutscene in the main menu", expect =>
        {
            bool busy = Game.IsBusy;
            _context.Log($"LIVE INFO main menu: IsBusy {busy}, IsCutscene {Game.IsCutscene}");
            expect(!Game.IsCutscene, "no cutscene with no player");
        });
        _context.Log($"LIVE SUMMARY main menu: {_passed} passed, {_failed} failed");
    }

    public void Unload() => _context.Log("LIVE probe unload: sprite " + _sprite);

    private void Pass(string name, string detail = "")
    {
        _passed++;
        _context.Log("LIVE PASS " + name + (detail.Length > 0 ? ": " + detail : ""));
    }

    private void Fail(string name, string detail)
    {
        _failed++;
        _context.Log("LIVE FAIL " + name + ": " + detail);
    }

    private void Check(string name, Func<bool> check)
    {
        try
        {
            if (check()) Pass(name);
            else Fail(name, "false");
        }
        catch (Exception e) { Fail(name, e.Message); }
    }

    // A check of several conditions: a failure names the ones that didn't hold.
    private void Check(string name, Action<Action<bool, string>> check)
    {
        var failed = new List<string>();
        try
        {
            check((ok, condition) => { if (!ok) failed.Add(condition); });
            if (failed.Count == 0) Pass(name);
            else Fail(name, string.Join(", ", failed));
        }
        catch (Exception e) { Fail(name, string.Join(", ", failed.Append(e.Message))); }
    }

    private void Alarms(Instance made)
    {
        Check("alarm set and read", () =>
        {
            made.Alarm[3] = 7;
            return made.Alarm[3] == 7 && made.Alarm[0] == -1;
        });
        Check("alarm off", () => { made.Alarm[3] = -1; return made.Alarm[3] == -1; });
        Check("alarm index out of range throws", () =>
        {
            try { made.Alarm[12] = 1; return false; }
            catch (ArgumentOutOfRangeException) { return true; }
        });
    }

    private void ArraysAndStructs()
    {
        Check("array by reference", () =>
        {
            var array = GmArray.From(new GmValue[] { 1, "two" });
            array.Push(3);
            Game.Global["stoneforge_probe_array"] = array;
            var back = Game.Global["stoneforge_probe_array"].AsArray!;
            back[0] = 10;
            return back.Equals(array) && array.Length == 3 && array[0].AsInt == 10 && array[1].AsString == "two";
        });
        Check("array JSON", () => GmArray.FromJson("[1,[2,3]]")!.Length == 2);
        Check("struct by reference", () =>
        {
            var strukt = GmStruct.Create();
            strukt["hp"] = 5;
            strukt["name"] = "probe";
            Game.Global["stoneforge_probe_struct"] = strukt;
            var back = Game.Global["stoneforge_probe_struct"].AsStruct!;
            back.Remove("name");
            return back.Equals(strukt) && strukt.Count == 1 && strukt.Has("hp") && !strukt.Has("name") && strukt.Names.SequenceEqual(new[] { "hp" });
        });
        Check("struct JSON", () => GmStruct.FromJson("{\"a\":{\"b\":2}}")!["a"].AsStruct!["b"].AsInt == 2);
    }

    // Whether two maps / lists hold the same, nested ones compared by contents (and marked the same).
    private static bool Same(DsMap a, DsMap b)
        => a.Count == b.Count && a.Keys.All(k => b.Has(k) && SameSlot(a.IsMap(k), a.IsList(k), a[k], b.IsMap(k), b.IsList(k), b[k]));

    private static bool Same(DsList a, DsList b)
        => a.Count == b.Count && Enumerable.Range(0, a.Count).All(i => SameSlot(a.IsMap(i), a.IsList(i), a[i], b.IsMap(i), b.IsList(i), b[i]));

    private static bool SameSlot(bool aMap, bool aList, GmValue a, bool bMap, bool bList, GmValue b)
        => aMap ? bMap && Same(new DsMap(a.AsInt), new DsMap(b.AsInt))
            : aList ? bList && Same(new DsList(a.AsInt), new DsList(b.AsInt))
            : !bMap && !bList && a.AsString == b.AsString;

    private void DsMaps()
    {
        // (What the game itself does.)
        try
        {
            var map = DsMap.Create();
            var inner = DsMap.Create();
            Game.CallBuiltin("ds_map_add_map", map.Id, "inner", inner.Id);
            _context.Log($"LIVE INFO ds_map_is_map: {Game.CallBuiltin("ds_map_is_map", map.Id, "inner")}; is_list: {Game.CallBuiltin("ds_map_is_list", map.Id, "inner")}");
            Game.CallBuiltin("ds_map_delete", map.Id, "inner");
            _context.Log($"LIVE INFO ds_map_delete destroys a marked map: {!inner.Exists}");
            if (inner.Exists) inner.Destroy();
            map.Destroy();
            GmValue bad = Game.CallBuiltin("json_decode", "not json");
            _context.Log($"LIVE INFO json_decode of not-JSON: {bad.Kind} {bad}");
            GmValue read = Game.CallBuiltin("json_decode", "{\"a\":1,\"b\":{\"c\":2}}");
            _context.Log($"LIVE INFO json_encode: {Game.CallBuiltin("json_encode", read)}");
            if (read.AsDsMap is { } readMap) readMap.Destroy();
        }
        catch (Exception e) { Fail("ds_map raw behaviour", e.Message); }

        Check("ds_map keys and values", () =>
        {
            var map = DsMap.Create();
            map["hp"] = 10;
            map["name"] = "Verren";
            map[3] = 1;
            bool ok = map.Count == 3 && map["hp"].AsInt == 10 && map.Has(3) && !map.Has("3") && map.Get("none", 7).AsInt == 7
                && map.Keys.Length == 3 && ((GmValue)map.Id).AsDsMap == map;
            map.Remove("hp");
            ok &= !map.Has("hp") && map.Count == 2;
            map.Destroy();
            return ok && !map.Exists;
        });
        Check("ds_map nested ones owned", expect =>
        {
            var map = DsMap.Create();
            var inner = DsMap.Create();
            var list = DsList.Create();
            map.AddMap("inner", inner);
            map.AddList("list", list);
            expect(map.IsMap("inner"), "IsMap");
            expect(map.IsList("list"), "IsList");
            expect(map.GetMap("inner") == inner, "GetMap");
            expect(!map.IsMap("list"), "a list isn't a map");
            map["list"] = 5;
            expect(!list.Exists, "set over destroys the nested list");
            expect(!map.IsList("list"), "set over loses the mark");
            expect(map["list"].AsInt == 5, "set over sets");
            map.Destroy();
            expect(!inner.Exists, "destroyed with the map");
        });
        Check("ds_map JSON both ways", expect =>
        {
            var map = DsMap.FromJson("{\"a\":1,\"b\":{\"c\":[1,{\"d\":2}]}}")!.Value;
            string json = map.ToJson();
            var again = DsMap.FromJson(json)!.Value;
            expect(map.IsMap("b"), "nested map");
            expect(map.GetMap("b")!.Value.IsList("c"), "nested list");
            expect(Same(map, again), "the same read back: " + json);
            expect(DsMap.FromJson("not json") == null, "not JSON is null");
            map.Destroy();
            again.Destroy();
        });
        Check("ds_map AssignFrom in place", expect =>
        {
            var target = DsMap.FromJson("{\"gone\":1,\"hp\":5,\"stats\":{\"str\":1,\"old\":2},\"items\":[{\"id\":1},{\"id\":2}],\"deep\":{\"a\":{\"b\":1}},\"kind\":{\"x\":1}}")!.Value;
            var stats = target.GetMap("stats")!.Value;
            var items = target.GetList("items")!.Value;
            var deepest = target.GetMap("deep")!.Value.GetMap("a")!.Value;
            var kind = target.GetMap("kind")!.Value;
            var source = DsMap.FromJson("{\"hp\":7,\"stats\":{\"str\":3},\"items\":[{\"id\":4}],\"deep\":{\"a\":{\"b\":2,\"c\":[1]}},\"kind\":[1],\"new\":{\"x\":1}}")!.Value;
            target.AssignFrom(source);
            expect(Same(target, source), "the same as the source: " + target.ToJson());
            expect(!target.Has("gone"), "a key the source lacks removed");
            expect(target.GetMap("stats") == stats && stats["str"].AsInt == 3 && !stats.Has("old"), "nested map in place");
            expect(target.GetList("items") == items && items.Count == 1, "nested list in place");
            expect(deepest["b"].AsInt == 2, "two deep in place");
            expect(!kind.Exists, "a map replaced by a list destroyed");
            expect(target.GetMap("new") != source.GetMap("new"), "nothing shared");
            source.Destroy();
            expect(target.GetMap("new") is { } made && made.Exists, "still there with the source destroyed");
            target.Destroy();
        });
    }

    private void DsLists()
    {
        // (What the game itself does - which DsList doesn't count on either way.)
        try
        {
            var list = DsList.Create();
            var a = DsMap.Create();
            var b = DsMap.Create();
            list.AddMap(a);
            list.AddMap(b);
            Game.CallBuiltin("ds_list_delete", list.Id, 0);
            _context.Log($"LIVE INFO ds_list_delete destroys a marked map: {!a.Exists}; the next one's mark moves up with it: {list.IsMap(0)}");
            Game.CallBuiltin("ds_list_replace", list.Id, 0, 5);
            _context.Log($"LIVE INFO ds_list_replace keeps the mark: {list.IsMap(0)}");
            if (a.Exists) a.Destroy();
            if (b.Exists) b.Destroy();
            Game.CallBuiltin("ds_list_destroy", list.Id);
        }
        catch (Exception e) { Fail("ds_list raw behaviour", e.Message); }

        Check("ds_list values", () =>
        {
            var list = DsList.Create();
            list.Add(1);
            list.Add("two");
            list.Add(3);
            list[1] = 2;
            list.RemoveAt(0);
            bool ok = list.Count == 2 && list[0].AsInt == 2 && list[1].AsInt == 3 && ((GmValue)list.Id).AsDsList == list;
            list.Clear();
            ok &= list.Count == 0;
            list.Destroy();
            return ok && !list.Exists;
        });
        Check("ds_list nested slot set over loses its mark", () =>
        {
            var list = DsList.Create();
            var a = DsMap.Create();
            var b = DsMap.Create();
            list.AddMap(a);
            list.AddMap(b);
            list.Add(9);
            list[0] = "plain";
            bool ok = !a.Exists && !list.IsMap(0) && list[0].AsString == "plain" && list.IsMap(1) && list.GetMap(1) == b && list[2].AsInt == 9;
            list.RemoveAt(1);
            ok &= !b.Exists && list.Count == 2;
            list.Destroy();
            return ok;
        });
        Check("ds_list JSON both ways", () =>
        {
            var list = DsList.FromJson("[1,\"two\",{\"id\":3},[4,[5]]]")!.Value;
            var again = DsList.FromJson(list.ToJson())!.Value;
            bool ok = list.Count == 4 && list.IsMap(2) && list.GetList(3)!.Value.IsList(1) && Same(list, again) && DsList.FromJson("{\"a\":1}") == null;
            list.Destroy();
            again.Destroy();
            return ok;
        });
        Check("ds_list AssignFrom in place", () =>
        {
            var target = DsList.FromJson("[{\"id\":1},[1,2],3,{\"id\":4},5]")!.Value;
            var kept = target.GetMap(0)!.Value;
            var keptList = target.GetList(1)!.Value;
            var remade = target.GetMap(3)!.Value;
            var source = DsList.FromJson("[{\"id\":9},[7],{\"z\":1},\"four\",[6],7]")!.Value;
            target.AssignFrom(source);
            bool ok = Same(target, source) && target.GetMap(0) == kept && kept["id"].AsInt == 9 && target.GetList(1) == keptList
                && !remade.Exists && !target.IsMap(3);
            var shorter = DsList.FromJson("[1]")!.Value;
            target.AssignFrom(shorter);
            ok &= target.Count == 1 && !kept.Exists && !keptList.Exists;
            source.Destroy();
            shorter.Destroy();
            target.Destroy();
            return ok;
        });
    }

    private void ScriptHooks()
    {
        _context.Log($"LIVE INFO easeOutCubic called by the game so far: {_gameCalls}");
        Check("script After, on a function inside another script's file", expect =>
        {
            _probing = true;
            try
            {
                double result = Scripts.easeOutCubic.Call(null, 0.5).AsReal;
                expect(_beforeArgs.SequenceEqual(new[] { 0.5 }), "before saw the call: " + string.Join(",", _beforeArgs));
                expect(_afterResults.SequenceEqual(new[] { 0.875 }), "after saw its result: " + string.Join(",", _afterResults));
                expect(result == 1.875, "after changed the result: " + result);
                double original = Scripts.easeOutCubic.CallOriginal(null, 0.5).AsReal;
                expect(original == 0.875, "CallOriginal: " + original);
                expect(_beforeArgs.Count == 1 && _afterResults.Count == 1, "CallOriginal skipped the hooks");
                double again = Scripts.easeOutCubic.Call(null, 0).AsReal;
                expect(again == 1 && _afterResults.Count == 2, "hooked again after CallOriginal: " + again);
            }
            finally { _probing = false; }
        });
    }

    private void SeededRandom()
    {
        double Draw() => Game.CallBuiltin("irandom", 1000000).AsReal;
        string Draws() => string.Join(",", Enumerable.Range(0, 5).Select(_ => Draw()));
        // (What the game itself does: random_get_seed is the seed set, not where the generator is.)
        Game.CallBuiltin("random_set_seed", 7);
        Draws();
        _context.Log($"LIVE INFO random_get_seed after drawing: {Game.CallBuiltin("random_get_seed")} (set: 7)");

        Check("seeded random", expect =>
        {
            string first = Game.WithSeed(42, Draws);
            Draw();
            expect(Game.WithSeed(42, Draws) == first, "the same seed draws the same");
            expect(Game.WithSeed(43, Draws) != first, "another seed draws others");
            Game.CallBuiltin("random_set_seed", 7);
            string before = Draws();
            Game.WithSeed(42, Draws);
            string after = Draws();
            expect(after != before, "carries on without replaying");
            Game.CallBuiltin("random_set_seed", 7);
            Draws();
            Game.WithSeed(42, Draws);
            expect(Draws() == after, "a seeded game stays the same");
            try { Game.WithSeed(42, () => throw new InvalidOperationException()); }
            catch (InvalidOperationException) { expect(true, ""); }
        });
        Game.CallBuiltin("randomize");
    }

    // Once a save is loaded: busy / cutscene, as the player; the room's ground loot, active and culled.
    private void OffScreen()
    {
        Check("busy and cutscene in game", () =>
        {
            _context.Log($"LIVE INFO in game: IsBusy {Game.IsBusy}, IsCutscene {Game.IsCutscene}");
            return true;
        });
        Check("off-screen instances", () =>
        {
            var all = Instances.All(GameObjectId.o_loot, includeCulled: true);
            var active = Instances.All(GameObjectId.o_loot);
            int culled = all.Count(i => i.IsCulled);
            bool readable = all.Where(i => i.IsCulled).All(i => i.Get("object_index").AsInt > 0);
            _context.Log($"LIVE INFO ground loot: {all.Count} ({active.Count} active, {culled} culled)");
            return all.Count == active.Count + culled && readable && all.All(i => !i.IsGone);
        });
        _context.Log($"LIVE SUMMARY with a save: {_passed} passed, {_failed} failed");
    }
}
