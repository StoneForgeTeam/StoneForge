namespace StoneForge;

/// <summary>Buffs and debuffs: mods' own (<see cref="Add"/>, <see cref="Apply"/>) and the game's
/// (<see cref="ApplyGame"/>: "o_db_daze", "o_db_poison", "o_db_bleed_tors"...).</summary>
public static class Buffs
{
    private sealed class Entry
    {
        public required ModContext Context;
        public required ModBuff Buff;
        public required int Number;
        public int Icon = -2;
        public int Aura = -2;
    }

    private static readonly Dictionary<string, Entry> ById = new();
    private static readonly Dictionary<int, Entry> ByNumber = new();
    // Effects to take off next frame (not inside the game's own event that's working on them).
    private static readonly HashSet<Instance> ToRemove = new();
    private static readonly HashSet<int> IdsToRemove = new();

    /// <summary>Adds a mod's buff or debuff (call it from <see cref="IStoneMod.Load"/>).</summary>
    public static void Add(ModContext context, ModBuff buff)
    {
        if (ById.TryGetValue(context.ContentId(buff.Key), out var existing))
            throw new ArgumentException($"There's already a buff \"{context.ContentId(buff.Key)}\" (from {existing.Context.Name})");
        buff.Attach(context);
        if (buff is ITickable tickable)
            context.AddTickable(tickable);
        var entry = new Entry { Context = context, Buff = buff, Number = NumberOf(buff.Id) };
        ById[buff.Id] = entry;
        ByNumber[entry.Number] = entry;
    }

    /// <summary>Puts a mod's buff on a unit (an enemy, the player...) for some turns; <paramref name="source"/>
    /// is who did it (the unit itself if not given). Null if the game wouldn't (immune, dead...) or it isn't a unit.</summary>
    public static Effect? Apply(ModBuff buff, GameInstance target, int turns, GameInstance? source = null)
    {
        Game.CheckRunning("Buffs.Apply");
        if (!ById.TryGetValue(buff.Id, out var entry) || entry.Buff != buff)
            throw new InvalidOperationException($"{buff.Id} hasn't been added (Buffs.Add)");
        if (!IsUnit(target))
            return null;
        int effectObject = Game.CallBuiltinTrusted("asset_get_index", default, default, buff.Kind == BuffKind.Debuff ? "o_stonemod_debuff" : "o_stonemod_buff").AsInt;
        if (effectObject < 0)
            throw new InvalidOperationException(@"The game data has no o_stonemod_buff - run dotnet\patcher\StoneForge.Patcher.exe");
        // (The effect's Create, during this call, reads which buff it's to be.)
        Game.Global["stonemod_buff_pending"] = entry.Number;
        GmValue made;
        try { made = Game.CallScript("scr_effect_create", default, effectObject, turns, IdOf(target.Instance), IdOf((source ?? target).Instance)); }
        finally { Game.Global["stonemod_buff_pending"] = -1; }
        Instance effect = made;
        if (effect.IsNone || made.Kind == GmKind.Real && made.AsReal < 0)
            return null;
        StartAura(effect, entry);
        var applied = new Effect(effect, buff);
        Run(entry, "OnApplied", b => b.OnApplied(applied));
        return applied;
    }

    /// <summary>Puts one of the game's own effects on a unit, by its object's name ("o_db_daze", "o_db_poison",
    /// "o_db_bleed_tors"...), for some turns. False if there's no such effect or the game wouldn't.</summary>
    public static bool ApplyGame(string effectObject, GameInstance target, int turns, GameInstance? source = null)
    {
        Game.CheckRunning("Buffs.ApplyGame");
        if (!IsUnit(target))
            return false;
        int index = Game.CallBuiltinTrusted("asset_get_index", default, default, effectObject).AsInt;
        if (index < 0 || !Game.CallBuiltinTrusted("object_exists", default, default, index).AsBool)
            return false;
        GmValue made = Game.CallScript("scr_effect_create", default, index, turns, IdOf(target.Instance), IdOf((source ?? target).Instance));
        return made.Kind != GmKind.Undefined && !(made.Kind == GmKind.Real && made.AsReal < 0);
    }

    /// <summary>Whether a unit has one of a mod's buffs on.</summary>
    public static bool Has(GameInstance target, ModBuff buff) => ActiveOf(buff).Any(e => SameUnit(e.Target.Instance, target.Instance));

    // ---- the loader ----

    // Something an effect can go on: a unit that's there (not nothing, a tile, a barrel...).
    private static bool IsUnit(GameInstance? target)
        => target != null && !target.Instance.IsNone && target.Instance.Exists
           && Game.CallBuiltinTrusted("object_is_ancestor", default, default, target.Instance.Get("object_index"), Game.CallBuiltinTrusted("asset_get_index", default, default, "o_unit")).AsBool;

    // A unit's id, to hand to the game (an instance found by id, or the engine's pointer from an event).
    // An effect's unit, from its target variable: an instance (a reference), an instance id, or - as the game
    // stores the player's - its object (o_player), meaning that object's instance. None if it's gone.
    internal static Instance UnitOf(GmValue target)
    {
        if (target.Kind == GmKind.Instance)
            return target.AsInstance;
        if (target.Kind != GmKind.Real || target.AsReal < 0)
            return default;
        int value = target.AsInt;
        if (value < 100000 && Game.CallBuiltinTrusted("object_exists", default, default, value).AsBool)
            return Game.CallBuiltinTrusted("instance_find", default, default, value, 0).AsInstance;
        var instance = Instance.FromId(value);
        return instance.Exists ? instance : default;
    }

    private static GmValue IdOf(Instance instance) => instance.Pointer == IntPtr.Zero ? instance.Id : instance.Get("id");

    private static bool SameUnit(Instance a, Instance b) => IdOf(a).AsReal == IdOf(b).AsReal;

    internal static IEnumerable<Effect> ActiveOf(ModBuff buff)
    {
        if (!Game.Running || !ById.TryGetValue(buff.Id, out var entry) || entry.Buff != buff)
            yield break;
        foreach (string obj in new[] { "o_stonemod_buff", "o_stonemod_debuff" })
        {
            int index = Game.CallBuiltinTrusted("asset_get_index", default, default, obj).AsInt;
            if (index < 0)
                continue;
            int count = Game.CallBuiltinTrusted("instance_number", default, default, index).AsInt;
            for (int i = 0; i < count; i++)
            {
                Instance found = Game.CallBuiltinTrusted("instance_find", default, default, index, i);
                if (!found.IsNone && found.Get("stonemod_buff").AsInt == entry.Number)
                    yield return new Effect(found, buff);
            }
        }
    }

    internal static void Install(ModContext loader)
    {
        foreach (string obj in new[] { "o_stonemod_buff", "o_stonemod_debuff" })
        {
            // Made: by Buffs.Apply (global.stonemod_buff_pending says which) - or by a save loading, set up in
            // user event 5, after the game has put its save_counter back.
            loader.OnCode($"gml_Object_{obj}_Create_0", after: (self, _) =>
            {
                GmValue pending = Game.Global["stonemod_buff_pending"];
                if (pending.Kind == GmKind.Real && pending.AsInt > 0)
                    SetUp(self, pending.AsInt);
            });
            loader.OnCode($"gml_Object_{obj}_Other_15", after: (self, _) =>
            {
                if (self.Get("stonemod_buff").Kind == GmKind.Real)
                    return;
                SetUp(self, self.Get("save_counter").AsInt);
                if (EntryOf(self) is Entry entry)
                    StartAura(self, entry);
            });
            // Each of its unit's turns (user event 0), and gone.
            loader.OnCode($"gml_Object_{obj}_Other_10", after: (self, _) =>
            {
                if (EntryOf(self) is Entry entry)
                    Run(entry, "OnTurn", b => b.OnTurn(new Effect(self, entry.Buff)));
            });
            loader.OnCode($"gml_Object_{obj}_Destroy_0", before: (self, _) =>
            {
                ToRemove.Remove(self);
                GmValue aura = self.Get("stonemod_aura");
                if (aura.Kind == GmKind.Real && aura.AsReal >= 0)
                    Game.CallBuiltinTrusted("instance_destroy", default, default, aura);
                if (EntryOf(self) is Entry entry)
                    Run(entry, "OnRemoved", b => b.OnRemoved(new Effect(self, entry.Buff)));
                return false;
            });
        }
        loader.Frame += () =>
        {
            if (ToRemove.Count == 0 && IdsToRemove.Count == 0)
                return;
            foreach (var instance in ToRemove.ToList())
                if (instance.Exists) Destroy(instance);
            foreach (int id in IdsToRemove.ToList())
                Game.CallBuiltinTrusted("instance_destroy", default, default, id);
            ToRemove.Clear();
            IdsToRemove.Clear();
        };
    }

    private static Entry? EntryOf(Instance effect)
    {
        GmValue number = effect.Get("stonemod_buff");
        return number.Kind == GmKind.Real && ByNumber.TryGetValue(number.AsInt, out var entry) ? entry : null;
    }

    // Makes an effect instance the mod's buff: its name, description, icon and stats (its data map, which the
    // game adds to its unit's stats). Its number goes in save_counter, which saves keep. A buff whose mod isn't
    // loaded (a save from before) is taken off.
    private static void SetUp(Instance effect, int number)
    {
        if (!ByNumber.TryGetValue(number, out var entry))
        {
            ToRemove.Add(effect);
            return;
        }
        var buff = entry.Buff;
        effect.Set("stonemod_buff", number);
        effect.Set("save_counter", number);
        effect.Set("name", buff.DisplayName);
        effect.Set("mid_text", buff.Description);
        effect.Set("mid_text_source", buff.Description);
        if (entry.Icon == -2)
        {
            entry.Icon = buff.Icon == null ? -1 : entry.Context.LoadSprite(buff.Icon);
            if (entry.Icon >= 0)
                Game.CallBuiltinTrusted("sprite_set_offset", default, default, entry.Icon, Draw.SpriteWidth(entry.Icon) / 2, Draw.SpriteHeight(entry.Icon) / 2);
            else if (buff.Icon != null)
                entry.Context.Log($"buff {buff.Id}: no icon Assets\\{buff.Icon}");
        }
        if (entry.Icon >= 0)
            effect.Set("sprite_index", entry.Icon);
        GmValue data = effect.Get("data");
        if (data.Kind == GmKind.Real)
            foreach (var (stat, value) in buff.Stats)
                Game.CallBuiltinTrusted("ds_map_replace", default, default, data, stat, value);
    }

    // Its aura (if it has one) on its unit, until it's gone (the Destroy hook stops it).
    private static void StartAura(Instance effect, Entry entry)
    {
        var buff = entry.Buff;
        if (entry.Aura == -2)
        {
            entry.Aura = buff.AuraSprite ?? -1;
            if (buff.AuraFile != null)
            {
                entry.Aura = entry.Context.LoadSprite(buff.AuraFile, buff.AuraFrames);
                if (entry.Aura >= 0)
                    Game.CallBuiltinTrusted("sprite_set_offset", default, default, entry.Aura, Draw.SpriteWidth(entry.Aura) / 2, Draw.SpriteHeight(entry.Aura));
                else
                    entry.Context.Log($"buff {buff.Id}: no aura Assets\\{buff.AuraFile}");
            }
        }
        if (entry.Aura < 0)
            return;
        try
        {
            var o = buff.AuraOptions ?? new FxOptions();
            var options = new FxOptions { Speed = o.Speed, Loop = true, OffsetX = o.OffsetX, OffsetY = o.OffsetY, Colour = o.Colour, Alpha = o.Alpha, Light = o.Light, Under = o.Under };
            Instance unit = UnitOf(effect.Get("target"));
            if (unit.IsNone)
            {
                entry.Context.Log($"buff {buff.Id}: no unit for its aura (target {effect.Get("target")})");
                return;
            }
            var visual = Fx.Play(GameInstance.Wrap<GameInstance>(unit), entry.Aura, options);
            if (visual != null)
                effect.Set("stonemod_aura", visual.Id);
        }
        catch (Exception e) { entry.Context.Log($"buff {buff.Id}: its aura couldn't play: {e.Message}"); }
    }

    internal static void Remove(Instance effect)
    {
        if (effect.Pointer != IntPtr.Zero)
            ToRemove.Add(effect);
        else if (effect.Id >= 0)
            IdsToRemove.Add(effect.Id);
    }

    private static void Destroy(Instance effect)
    {
        try { Game.CallBuiltinTrusted("instance_destroy", effect, effect); }
        catch (Exception e) { Game.Log("couldn't remove an effect: " + e.Message); }
    }

    // A mod switched off: its buffs come off every unit, and go.
    internal static void RemoveMod(string mod)
    {
        foreach (var entry in ById.Values.Where(e => e.Context.Id == mod).ToList())
        {
            if (Game.Running)
                foreach (var effect in ActiveOf(entry.Buff).ToList())
                    Remove(effect.Instance);
            ById.Remove(entry.Buff.Id);
            ByNumber.Remove(entry.Number);
        }
    }

    private static void Run(Entry entry, string what, Action<ModBuff> action)
    {
        Hooks.Invoke(entry.Context.Id, entry.Buff.Id + "." + what, () => { action(entry.Buff); return false; });
    }

    // Each buff's number (saved with its effects): dotnet\mod_buffs.json, so it stays the same from run to run.
    private static Dictionary<string, int>? _numbers;
    private static string NumbersPath => Path.Combine(Path.GetDirectoryName(typeof(Buffs).Assembly.Location)!, "mod_buffs.json");

    private static int NumberOf(string id)
    {
        if (_numbers == null)
        {
            try
            {
                _numbers = File.Exists(NumbersPath)
                    ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(NumbersPath)) ?? new()
                    : new();
            }
            catch (Exception e)
            {
                Game.Log("mod_buffs.json unreadable, starting a new one: " + e.Message);
                _numbers = new();
            }
        }
        if (_numbers.TryGetValue(id, out int number))
            return number;
        number = _numbers.Count == 0 ? 1 : _numbers.Values.Max() + 1;
        _numbers[id] = number;
        try { File.WriteAllText(NumbersPath, System.Text.Json.JsonSerializer.Serialize(_numbers, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { Game.Log("couldn't write mod_buffs.json: " + e.Message); }
        return number;
    }
}
