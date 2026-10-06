namespace StoneForge;

/// <summary>Mods' skills - active (<see cref="ModSkill"/>) and passive (<see cref="ModPassive"/>): added here, each is the
/// game's own - its objects the patcher added (an active's o_skill_&lt;key&gt; and its icon, children of the game skill's;
/// a passive's o_pass_skill_&lt;key&gt;, one of the game's passives), an active's row of the skills table, its name, icon
/// and effect (<see cref="ModSkill.OnCast"/>, a passive's stats and reactions) from StoneForge - on its tab of the skills
/// menu (its Tab, in its Group), learnt with ability points. And any skill used, the game's or a mod's
/// (<see cref="OnUsed"/>).</summary>
public static class Skills
{
    // A page holds 9 (three rows of three, as the game's); a mod with more has more pages.
    private const int PerPage = 9;

    private sealed class Entry
    {
        public required ModContext Context;
        public required ModSkillBase Skill;
        public int Object = -1, Icon = -1, Sprite = -1;
        public bool Defined;
    }

    private static readonly Dictionary<string, Entry> ByKey = new();
    // By their objects - the skill's and its icon's (-1 when the game data has none yet).
    private static readonly Dictionary<int, Entry> ByObject = new();
    // The pages' backgrounds, by how many skills a page has (drawn once).
    private static readonly Dictionary<int, int> Backgrounds = new();

    /// <summary>Runs as a skill has been used - anyone's, the player's or a unit's, the game's or a mod's: its energy
    /// spent, its cooldown started, a spell's miracle or fumble rolled (the skill's user event 3). Not the plain actions
    /// that are skills in name only - moving, throwing an item, crafting, setting a trap, dousing, disarming, a bed or a
    /// dummy set up, a shot's attack mode. Its skill, caster and target are kept by id.</summary>
    public static void OnUsed(ModContext context, Action<SkillCast> handler)
        => context.OnCode("gml_Object_o_skill_Other_13", after: (skill, _) =>
        {
            if (skill.IsNone || !skill.Exists)
                return;
            Instance caster = Instance.Of(skill.Get("owner")), target = Instance.Of(skill.Get("target"));
            handler(new SkillCast(skill.Persist(), caster, target, skill.Get("is_crit").AsBool));
        });

    /// <summary>Adds a mod's skill, active or passive. Call it from <see cref="IStoneMod.Load"/>.</summary>
    public static void Add(ModContext context, ModSkillBase skill)
    {
        if (ByKey.TryGetValue(context.GameKey(skill.Key), out var existing))
            throw new ArgumentException($"There's already a skill \"{context.ContentId(skill.Key)}\" (from {existing.Context.Name})");
        skill.Attach(context);
        var entry = new Entry { Context = context, Skill = skill };
        ByKey[skill.GameKey] = entry;
        if (Game.Running && TableLoaded && Items.ModsLoaded())
            Define(entry);
    }

    internal static void RemoveMod(string mod)
    {
        foreach (var (key, entry) in ByKey.Where(e => e.Value.Context.Id == mod).ToList())
        {
            if (entry.Skill is ModPassive && Game.Running)
                ClearStats(entry);
            ByKey.Remove(key);
            ByObject.Remove(entry.Object);
            ByObject.Remove(entry.Icon);
        }
    }

    // (An array - Game.Global has it as undefined - made by the text loader, step 31.)
    private static bool TableLoaded => Game.CallBuiltinTrusted("variable_global_exists", default, default, "skills_stat_csv").AsBool;
    private static bool NamesLoaded => Game.Global["skill_name_text"].Kind == GmKind.Real;

    internal static void Install(ModContext loader)
    {
        _loader = loader;
        // (The native build: the mod page's GML - its Create, its layout (user event 14) - done in C#.)
        if (Game.IsNative)
        {
            ObjectEvents.Hook(loader, "o_skill_category_stonemod", "Create_0", after: SkillData.CategoryCreated);
            ObjectEvents.Hook(loader, "o_skill_category_stonemod", "Other_24", after: SkillData.CategoryLaidOut);
        }
        Events.o_textLoader.Other_25.After(loader, textLoader =>
        {
            int step = textLoader.Instance.Get("number").AsInt;
            if (step == 31)
                DefinePending();
            else if (step == 3)
                foreach (var entry in ByKey.Values.Where(e => e.Defined))
                    SetNames(entry);
        });
        loader.Frame += () =>
        {
            if (ByKey.Count == 0 || !Game.Running)
                return;
            if (ByKey.Values.Any(e => !e.Defined) && TableLoaded && Items.ModsLoaded())
                DefinePending();
            // (Once a second: the game locks skills of branches not studied yet - a new character's, a save's -
            // but a mod's are learnt with ability points alone.)
            if (++_frames % 60 == 0 && ByObject.Count > 0 && Gm.InGame)
                Unlock();
        };
        // Its tooltip, while it's locked: what it asks for, in place of the game's "read the treatise".
        loader.OnScript("scr_skill_reparse_locked", call =>
        {
            if (ByObject.Count == 0 || call.Self.IsNone || !ByObject.TryGetValue(call.Self.Get("object_index").AsInt, out var entry)
                || entry.Icon != call.Self.Get("object_index").AsInt)
                return false;
            var unmet = call.Self.Get("is_lock").AsBool ? Unmet(entry.Skill) : new List<string>();
            call.Result = unmet.Count == 0 ? "N/A" : "Requires " + string.Join(", ", unmet);
            return true;
        });
        // And as the skills menu fills a mod's tab (its category's alarm 0 makes the tab's icons).
        loader.OnCode("gml_Object_o_skill_category_Alarm_0", before: (self, _) =>
        {
            if (ByObject.Count > 0 && self.Get("object_index").AsInt == Gm.AssetGetIndex("o_skill_category_stonemod"))
                Unlock();
            return false;
        });
        // Their icons: the skill's and its icon's, as they're made.
        foreach (string code in new[] { "gml_Object_o_skill_Create_0", "gml_Object_o_skill_ico_Create_0" })
            loader.OnCode(code, after: (self, _) => SetSprite(self));
        // Cast: the mod's effect in place of the game skill's. A spell (scr_cast_spell / scr_cast_aoe_spell, run as
        // the skill) is cast as the game's - its casting animation, the "birth" object, still plays - but at its cast
        // frame (the birth's user event 0) OnCast runs in place of the projectile it would launch, and the turn ends
        // there (the projectile's end would have ended it). A skill without a target with a duration casts its buff
        // in alarm 3 (the skill ends that turn itself).
        loader.OnScript("scr_cast_spell", call => CastSpell(call, crit: 6));
        loader.OnScript("scr_cast_aoe_spell", call => CastSpell(call, crit: 8));
        foreach (string birth in new[] { "o_spellbirth", "o_spellbirth_ext" })
            HookBirth(birth);
        loader.OnCode("gml_Object_o_skill_Alarm_3", before: (self, _) =>
        {
            if (ByObject.Count == 0 || !ByObject.TryGetValue(self.Get("object_index").AsInt, out var entry) || entry.Skill is not ModSkill active)
                return false;
            GmValue owner = self.Get("owner");
            RunOnCast(entry, new SkillCast(self, Buffs.UnitOf(owner), Buffs.UnitOf(owner), self.Get("is_crit").AsBool));
            return !active.KeepGameEffect;
        });
        // Passives' stats: in their data map when the game asks the passives for theirs (o_skill_passive's user event 7,
        // when one's learnt or the stats are counted again) - from where it adds every learnt passive's to the player's.
        loader.OnCode("gml_Object_o_skill_passive_Other_17", after: (self, _) =>
        {
            if (ByObject.Count > 0 && ByObject.TryGetValue(self.Get("object_index").AsInt, out var entry) && entry.Skill is ModPassive passive
                && self.Get("is_open").AsBool && self.Get("data") is { Kind: GmKind.Real } data)
                foreach (var (stat, value) in passive.Stats)
                    Game.CallBuiltinTrusted("ds_map_replace", default, default, data, stat, value);
        });
        // Passives' reactions: the player's attacks and those on the player (Items' attack hooks).
        Items.PassivesListening = () => ByKey.Values.Any(e => e.Skill is ModPassive && e.Icon >= 0);
        Items.PassiveAttack = OnAttack;
        // The skills menu: the mods' groups and tabs.
        loader.OnCode("gml_Object_o_skillmenu_Create_0", after: (self, _) => AddPages(self));
    }

    private static void DefinePending()
    {
        foreach (var entry in ByKey.Values.Where(e => !e.Defined).ToList())
            Define(entry);
    }

    private static void Define(Entry entry)
    {
        entry.Defined = true;
        var skill = entry.Skill;
        try
        {
            if (skill is ModSkill active)
            {
                entry.Object = Gm.AssetGetIndex("o_skill_" + skill.GameKey);
                entry.Icon = Gm.AssetGetIndex("o_skill_" + skill.GameKey + "_ico");
                if (entry.Object < 0 || entry.Icon < 0)
                {
                    entry.Context.Log($"skill \"{skill.Id}\": the game has no objects for it yet - they're added when the game starts (restart it)");
                    return;
                }
                if (!SkillData.DefineSkill(skill.GameKey, active.BasedOn, active.ColumnsText))
                {
                    entry.Context.Log($"skill \"{skill.Id}\": the game has no skill \"{active.BasedOn}\" to base it on");
                    return;
                }
                ByObject[entry.Object] = entry;
                if (!active.KeepGameConditions)
                {
                    GmValue validators = Game.Global["skill_extra_validate_map"];
                    if (validators.Kind == GmKind.Real)
                        Game.CallBuiltinTrusted("ds_map_delete", default, default, validators, skill.GameKey);
                }
                HookConditions(active.BasedOn);
                // (The native build: its objects' GML - their Create events - done in C#.)
                string key = skill.GameKey;
                int skillObject = entry.Object;
                HookNative("o_skill_" + key, self => SkillData.SkillCreated(self, key));
                HookNative("o_skill_" + key + "_ico", self => SkillData.IconCreated(self, skillObject));
            }
            else
            {
                // (A passive is its icon alone: o_pass_skill_<key>, as the game's are.)
                entry.Icon = Gm.AssetGetIndex("o_pass_skill_" + skill.GameKey);
                if (entry.Icon < 0)
                {
                    entry.Context.Log($"passive \"{skill.Id}\": the game has no object for it yet - it's added when the game starts (restart it)");
                    return;
                }
                string key = skill.GameKey;
                HookNative("o_pass_skill_" + key, self => SkillData.PassiveCreated(self, key));
            }
            ByObject[entry.Icon] = entry;
            if (skill.Icon != null)
                entry.Sprite = LoadLike(entry, skill.Icon, Game.CallBuiltinTrusted("object_get_sprite", default, default, entry.Icon).AsInt);
            if (NamesLoaded)
                SetNames(entry);
            entry.Context.Log(skill is ModSkill based ? $"skill \"{skill.Id}\" added (based on {based.BasedOn})" : $"passive \"{skill.Id}\" added");
        }
        catch (Exception e)
        {
            entry.Context.Log($"skill \"{skill.Id}\" couldn't be added: {e.Message}");
        }
    }

    // Its name and description in the game's text maps (by its key): its own, or the game skill's.
    private static void SetNames(Entry entry)
    {
        var skill = entry.Skill;
        string? baseName = BaseName(skill);
        foreach (var (map, value) in new (string Map, string? Value)[] { ("skill_name_text", skill.DisplayName), ("skill_desc", skill.Description) })
        {
            GmValue texts = Game.Global[map];
            if (texts.Kind != GmKind.Real)
                continue;
            GmValue text = value != null ? value : baseName != null ? Game.CallBuiltinTrusted("ds_map_find_value", default, default, texts, baseName) : GmValue.Undefined;
            if (text.Kind != GmKind.Undefined)
                Game.CallBuiltinTrusted("ds_map_set", default, default, texts, skill.GameKey, text);
        }
        if (baseName != null)
        {
            CopySpeech(baseName, skill.GameKey);
            CopySpeech("MC_" + baseName, "MC_" + skill.GameKey);
        }
    }

    // The character's lines for it - said on a miracle ("<skill>") and a miscast ("MC_<skill>") - the game skill's: global.speech_text is a
    // list of blocks - "<key>", its lines, "<key>_end".
    private static void CopySpeech(string from, string to)
    {
        GmValue list = Game.Global["speech_text"];
        if (list.Kind != GmKind.Real)
            return;
        int first = Game.CallBuiltinTrusted("ds_list_find_index", default, default, list, from).AsInt;
        int last = Game.CallBuiltinTrusted("ds_list_find_index", default, default, list, from + "_end").AsInt;
        if (first < 0 || last <= first || Game.CallBuiltinTrusted("ds_list_find_index", default, default, list, to).AsInt >= 0)
            return;
        var lines = new List<GmValue>();
        for (int i = first + 1; i < last; i++)
            lines.Add(Game.CallBuiltinTrusted("ds_list_find_value", default, default, list, i));
        Game.CallBuiltinTrusted("ds_list_add", default, default, list, to);
        foreach (var line in lines)
            Game.CallBuiltinTrusted("ds_list_add", default, default, list, line);
        Game.CallBuiltinTrusted("ds_list_add", default, default, list, to + "_end");
        GmValue chances = Game.Global["speech_chance"];
        if (chances.Kind == GmKind.Real)
        {
            GmValue chance = Game.CallBuiltinTrusted("ds_map_find_value", default, default, chances, from);
            if (chance.Kind != GmKind.Undefined)
                Game.CallBuiltinTrusted("ds_map_set", default, default, chances, to, chance);
        }
    }

    // The game skill's name in its tables ("Chain_Lightning"): its object's id ("chain_lightning") as the names
    // table spells it, matched without case.
    private static string? BaseName(ModSkillBase skill) => skill is ModSkill active ? TableName(active.BasedOn) : null;

    private static readonly Dictionary<string, string> TableNames = new(StringComparer.OrdinalIgnoreCase);

    private static string? TableName(string id)
    {
        if (TableNames.TryGetValue(id, out string? known))
            return known;
        GmValue names = Game.Global["skill_name_text"];
        if (names.Kind != GmKind.Real)
            return null;
        for (GmValue key = Game.CallBuiltinTrusted("ds_map_find_first", default, default, names); key.Kind == GmKind.String;
             key = Game.CallBuiltinTrusted("ds_map_find_next", default, default, names, key))
            TableNames.TryAdd(key.AsString, key.AsString);
        return TableNames.TryGetValue(id, out known) ? known : null;
    }

    private static void SetSprite(Instance self)
    {
        if (ByObject.Count > 0 && ByObject.TryGetValue(self.Get("object_index").AsInt, out var entry) && entry.Sprite >= 0)
            self.Set("sprite_index", entry.Sprite);
    }

    private static int _frames;

    // Mod skills' icons locked till what they ask for is met (ModSkill.RequiredLevel...), unlocked after - the game
    // locks skills of branches not studied yet, but a mod's are learnt with ability points and their requirements.
    // (A learnt one stays learnt.)
    private static void Unlock()
    {
        foreach (var entry in ByKey.Values)
        {
            if (entry.Icon < 0)
                continue;
            int count = Game.CallBuiltinTrusted("instance_number", default, default, entry.Icon).AsInt;
            bool? met = null;
            for (int i = 0; i < count; i++)
            {
                Instance icon = Game.CallBuiltinTrusted("instance_find", default, default, entry.Icon, i).AsInstance;
                if (icon.IsNone || icon.Get("is_open").AsBool)
                    continue;
                met ??= Unmet(entry.Skill).Count == 0;
                if (icon.Get("is_lock").AsBool == met.Value)
                    icon.Set("is_lock", !met.Value);
            }
        }
    }

    // What a skill asks for that the player hasn't: as the tooltip says them ("level 8", "14 points in
    // Perception, Willpower", "Chain Lightning").
    private static List<string> Unmet(ModSkillBase skill)
    {
        var unmet = new List<string>();
        if (skill.RequiredLevel > 0)
        {
            // (The character's own level - not scr_atr's, which mods can hook.)
            GmValue data = Game.Global["characterDataMap"];
            double level = data.Kind == GmKind.Real ? Game.CallBuiltinTrusted("ds_map_find_value", default, default, data, "LVL").AsReal : 0;
            if (level < skill.RequiredLevel)
                unmet.Add($"level ~sy~{skill.RequiredLevel}~/~");
        }
        if (skill.RequiredAttributes.Count > 0)
        {
            Instance player = Game.CallBuiltinTrusted("instance_find", default, default, Gm.AssetGetIndex("o_player"), 0).AsInstance;
            double points = player.IsNone ? 0 : skill.RequiredAttributes.Sum(a => player.Get(a.GameName()).AsReal - 10);
            if (points < skill.RequiredAttributePoints)
                unmet.Add($"~sy~{skill.RequiredAttributePoints}~/~ points in " + string.Join(", ", skill.RequiredAttributes.Select(AttributeName)));
        }
        foreach (string id in skill.RequiredSkills)
        {
            int icon = IconOf(id);
            if (icon < 0)
                continue;
            bool learnt = false;
            int count = Game.CallBuiltinTrusted("instance_number", default, default, icon).AsInt;
            for (int i = 0; i < count && !learnt; i++)
                learnt = Game.CallBuiltinTrusted("instance_find", default, default, icon, i).AsInstance.Get("is_open").AsBool;
            if (!learnt)
                unmet.Add($"~sy~{SkillName(id)}~/~");
        }
        return unmet;
    }

    // A skill's icon object, by its id as the game knows it: a mod's (active or passive), a game skill's
    // (o_skill_<id>_ico) or a game passive's (o_pass_skill_<id>). -1: none.
    private static int IconOf(string id)
    {
        if (ByKey.TryGetValue(id, out var entry) && entry.Icon >= 0)
            return entry.Icon;
        int icon = Gm.AssetGetIndex("o_skill_" + id + "_ico");
        return icon >= 0 ? icon : Gm.AssetGetIndex("o_pass_skill_" + id);
    }

    // Whether the player has learnt a mod's skill (any of its icon's instances open).
    internal static bool IsLearnt(ModSkillBase skill)
    {
        if (!Game.Running || skill.GameKey.Length == 0 || !ByKey.TryGetValue(skill.GameKey, out var entry) || entry.Icon < 0)
            return false;
        int count = Game.CallBuiltinTrusted("instance_number", default, default, entry.Icon).AsInt;
        for (int i = 0; i < count; i++)
            if (Game.CallBuiltinTrusted("instance_find", default, default, entry.Icon, i).AsInstance.Get("is_open").AsBool)
                return true;
        return false;
    }

    // A passive's stats gone from the player's (its mod switched off): its data maps emptied, the stats counted again.
    private static void ClearStats(Entry entry)
    {
        if (entry.Icon < 0)
            return;
        int count = Game.CallBuiltinTrusted("instance_number", default, default, entry.Icon).AsInt;
        for (int i = 0; i < count; i++)
            if (Game.CallBuiltinTrusted("instance_find", default, default, entry.Icon, i).AsInstance is { IsNone: false } icon
                && icon.Get("data") is { Kind: GmKind.Real } data)
                Game.CallBuiltinTrusted("ds_map_clear", default, default, data);
        if (Game.CallBuiltinTrusted("instance_find", default, default, Gm.AssetGetIndex("o_player"), 0).AsInstance is { IsNone: false } player)
            player.Set("stats_is_change", true);
    }

    // An attack the player made or took: each learnt passive's reactions, in its mod's name.
    private static void OnAttack(Attack attack)
    {
        foreach (var entry in ByKey.Values.Where(e => e.Skill is ModPassive && e.Icon >= 0).ToList())
        {
            var passive = (ModPassive)entry.Skill;
            if (!IsLearnt(passive))
                continue;
            void Run(string what, Action action) => Hooks.Invoke(entry.Context.Id, passive.Id + "." + what, () => { action(); return false; });
            if (attack.ByPlayer)
            {
                Run("OnAttack", () => passive.OnAttack(attack));
                if (attack.IsHit)
                    Run("OnHit", () => passive.OnHit(attack));
                if (attack.IsHit && attack.Killed)
                    Run("OnKill", () => passive.OnKill(attack));
            }
            if (attack.OnPlayer)
            {
                Run("OnAttacked", () => passive.OnAttacked(attack));
                if (attack.IsHit)
                    Run("OnHitTaken", () => passive.OnHitTaken(attack));
            }
        }
    }

    // An attribute's name in the game's language (global.attribute), or in English.
    private static string AttributeName(CharacterAttribute attribute)
    {
        GmValue names = Game.Global["attribute"];
        GmValue name = names.Kind == GmKind.Real ? Game.CallBuiltinTrusted("ds_map_find_value", default, default, names, attribute.GameName()) : GmValue.Undefined;
        return name.Kind == GmKind.String ? name.AsString : attribute.ToString();
    }

    // A skill's name in game: a mod's (by its key), or a game skill's (its id as the names table spells it).
    private static string SkillName(string id)
    {
        if (ByKey.TryGetValue(id, out var entry) && entry.Skill.DisplayName != null)
            return entry.Skill.DisplayName;
        GmValue names = Game.Global["skill_name_text"];
        if (names.Kind != GmKind.Real)
            return id;
        GmValue name = Game.CallBuiltinTrusted("ds_map_find_value", default, default, names, TableName(id) ?? id);
        return name.Kind == GmKind.String ? name.AsString : id;
    }

    private static GmValue Arg(ScriptCall call, int index) => index < call.Args.Length ? call.Args[index] : GmValue.Undefined;

    private static ModContext _loader = null!;
    // The births (casting animations) of mod skills' casts, by instance id: the cast, till its cast frame (then
    // null - done, its user event 0 skipped from then on: some births' don't check they've run).
    private static readonly Dictionary<long, SkillCast?> Births = new();
    private static readonly HashSet<string> HookedBirths = new();

    // A mod skill's spell: the game's cast (its birth made), its own effect at the birth's cast frame.
    private static bool CastSpell(ScriptCall call, int crit)
    {
        if (ByObject.Count == 0 || !ByObject.TryGetValue(call.Self.Get("object_index").AsInt, out var entry))
            return false;
        if (entry.Skill is not ModSkill active)
            return false;
        var cast = new SkillCast(call.Self, Buffs.UnitOf(Arg(call, 2)), Buffs.UnitOf(Arg(call, 1)), Arg(call, crit).AsBool);
        if (active.KeepGameEffect)
        {
            RunOnCast(entry, cast);
            return false;
        }
        // The game's own cast, its birth found after: the newest of its object.
        GmValue spell = Arg(call, 0);
        int birthObject = spell.Kind == GmKind.String ? Gm.AssetGetIndex(spell.AsString) : spell.AsInt;
        call.Result = Hooks.CallOriginal(call.Name, call.Self, call.Other, call.Args);
        int count = birthObject >= 0 ? Game.CallBuiltinTrusted("instance_number", default, default, birthObject).AsInt : 0;
        Instance birth = count > 0 ? Game.CallBuiltinTrusted("instance_find", default, default, birthObject, count - 1).AsInstance : default;
        if (birth.IsNone || !birth.Exists)
        {
            // (No birth to wait for: the effect now, the turn ended now.)
            RunOnCast(entry, cast);
            EndTurn(cast);
            return true;
        }
        foreach (long gone in Births.Keys.Where(id => !Instance.FromId((int)id).Exists).ToList())
            Births.Remove(gone);
        Births[IdOf(birth)] = cast;
        HookBirth(Game.CallBuiltinTrusted("object_get_name", default, default, birthObject).AsString);
        return true;
    }

    // A birth object's user event 0 - the cast frame, where it launches the spell - hooked (once): for a mod
    // skill's, OnCast in its place.
    private static void HookBirth(string birthObject)
    {
        if (!HookedBirths.Add(birthObject))
            return;
        _loader.OnCode("gml_Object_" + birthObject + "_Other_10", before: (self, _) =>
        {
            if (Births.Count == 0 || !Births.TryGetValue(IdOf(self), out var cast))
                return false;
            if (cast != null)
            {
                Births[IdOf(self)] = null;
                self.Set("is_execute", true);
                if (ByObject.TryGetValue(cast.Skill.Instance.Exists ? cast.Skill.Instance.Get("object_index").AsInt : -1, out var entry))
                    RunOnCast(entry, cast);
                EndTurn(cast);
            }
            return true;
        });
    }

    private static readonly HashSet<string> HookedConditions = new();

    // A game skill's icon's check whether it can be used (its user event 15 - the patcher gave our icon the game
    // skill's icon's events): for a mod skill's icon not keeping it, o_skill_ico's own (energy, cooldown...).
    private static void HookConditions(string basedOn)
    {
        if (!HookedConditions.Add(basedOn))
            return;
        _loader.OnCode("gml_Object_o_skill_" + basedOn + "_ico_Other_25", before: (self, _) =>
        {
            if (!ByObject.TryGetValue(self.Get("object_index").AsInt, out var entry) || entry.Skill is not ModSkill { KeepGameConditions: false }
                || entry.Icon != self.Get("object_index").AsInt)
                return false;
            Game.CallBuiltinTrusted("event_perform_object", self, self, Gm.AssetGetIndex("o_skill_ico"), 7, 25);
            return true;
        });
    }

    // An instance's id (from an event's self - the engine's pointer - or found by id).
    private static long IdOf(Instance instance)
    {
        if (instance.Pointer == IntPtr.Zero)
            return instance.Id;
        GmValue id = instance.Get("id");
        return id.Kind == GmKind.Instance ? id.AsInstance.Id : (long)id.AsReal;
    }

    private static void RunOnCast(Entry entry, SkillCast cast)
    {
        if (entry.Skill is ModSkill active)
            Hooks.Invoke(entry.Context.Id, active.Id + ".OnCast", () => { active.OnCast(cast); return false; });
    }

    // The player's turn ended, as the game's spell would have once it hit.
    private static void EndTurn(SkillCast cast)
    {
        Instance caster = cast.Caster.Instance;
        if (!caster.IsNone && caster.Exists
            && Game.CallBuiltinTrusted("object_get_name", default, default, caster.Get("object_index")).AsString == "o_player")
            Game.CallScript("scr_allturn", default);
    }

    // ---- the skills menu ----

    // As the skills menu makes its categories (its Create): after the game's, each group's header and its tabs
    // (9 skills a page) - made as the game makes its own.
    private static void AddPages(Instance menu)
    {
        // Groups (headers) in the order their first skills were added, each with its tabs, each tab 9 skills a page.
        var groups = ByKey.Values.Where(e => e.Defined && ByObject.ContainsKey(e.Icon))
            .GroupBy(e => e.Skill.Group, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Group: g.Key, Tabs: g.GroupBy(e => e.Skill.Tab ?? e.Context.Name)
                .SelectMany(t => t.Select((e, i) => (e, i)).GroupBy(x => x.i / PerPage, x => x.e)
                    .Select(p => (Name: p.Key == 0 ? t.Key : $"{t.Key} {p.Key + 1}", Mods: t.Select(e => e.Context.Name).Distinct().ToList(), Skills: p.ToList())))
                .ToList()))
            .ToList();
        if (groups.Count == 0)
            return;
        try
        {
            Instance left = menu.Get("leftContainer").AsInstance;
            double depth = menu.Get("depth").AsReal - 1;
            const double width = 82;
            var sections = GameSections(left);
            foreach (var (group, tabs) in groups)
            {
                // One of the game's sections (Weaponry, Utility, Sorcery): its tabs go in it, after the game's.
                int section = SkillGroup.GameIndex(group, sections.Select(h => h.Get("name").AsString).ToList());
                if (section < 0)
                    Header(left, depth, width, group);
                var made = tabs.Select(tab => Tab(left, depth, width, group, tab.Name, tab.Mods, tab.Skills)).ToList();
                if (section >= 0)
                    MoveIntoSection(left, sections[section], made);
            }
        }
        catch (Exception e) { Game.Log($"Skills menu: couldn't add the mods' tabs: {e}"); }
    }

    // The game's section headers in the skills menu's list, in order (Weaponry, Utility, Sorcery).
    private static List<Instance> GameSections(Instance left)
    {
        var headers = new List<Instance>();
        GmValue children = left.Get("guiChildrenList");
        int header = Gm.AssetGetIndex("o_skill_metacategory");
        int count = Game.CallBuiltinTrusted("ds_list_size", default, default, children).AsInt;
        for (int i = 0; i < count; i++)
        {
            Instance child = InstanceOf(Game.CallBuiltinTrusted("ds_list_find_value", default, default, children, i));
            if (!child.IsNone && child.Get("object_index").AsInt == header)
                headers.Add(child);
        }
        return headers;
    }

    // A GUI list's entry: an instance, or its id.
    private static Instance InstanceOf(GmValue value) => value.Kind == GmKind.Instance ? value.AsInstance
        : value.Kind == GmKind.Real && value.AsReal >= 0 ? Instance.FromId((int)value.AsReal) : default;

    // A group of the mods' own: a gap, and its header (upper case, as the game's: SORCERY...), at the list's end.
    private static void Header(Instance left, double depth, double width, string group)
    {
        Instance spacer = Simple(left, "o_guiSimpleEmpty", depth);
        Game.CallScript("scr_guiSizeUpdate", spacer, spacer, width, 5);
        Instance header = Simple(left, "o_skill_metacategory", depth);
        string title = group.ToUpperInvariant();
        header.Set("name", title);
        double headerNameWidth = width - header.Get("nameOffsetX").AsReal * 2;
        header.Set("nameWidth", headerNameWidth);
        header.Set("image_xscale", width);
        header.Set("image_yscale", Game.CallScript("scr_stringGetHeightExt", header, title, headerNameWidth, Game.Global["f_digits"]).AsReal + header.Get("lineHeight").AsReal);
        Game.CallScript("scr_guiSizeUpdate", header, header, width, header.Get("image_yscale").AsReal);
    }

    // A tab of mods' skills, at the list's end: its name and description, and its page.
    private static Instance Tab(Instance left, double depth, double width, string group, string name, List<string> mods, List<Entry> skills)
    {
        GmValue tierNames = Game.Global["tier_name"], tierDescriptions = Game.Global["tier_description"];
        string text = "stonemod_" + group.ToLowerInvariant() + "_" + name;
        if (tierNames.Kind == GmKind.Real)
            Game.CallBuiltinTrusted("ds_map_set", default, default, tierNames, text, name);
        if (tierDescriptions.Kind == GmKind.Real)
            Game.CallBuiltinTrusted("ds_map_set", default, default, tierDescriptions, text,
                mods.Count == 1 ? $"Skills from the {mods[0]} mod." : $"Skills from the {string.Join(", ", mods)} mods.");
        Instance category = Game.CallScript("scr_guiCreateInteractive", left, left, Gm.AssetGetIndex("o_skill_category_stonemod"), depth, 0, 0).AsInstance;
        double nameWidth = width - category.Get("nameOffsetX").AsReal * 2;
        category.Set("name", name);
        category.Set("nameWidth", nameWidth);
        category.Set("image_xscale", width);
        category.Set("image_yscale", Game.CallScript("scr_stringGetHeightExt", category, name, nameWidth).AsReal + category.Get("nameOffsetY").AsReal * 2);
        Game.CallScript("scr_guiSizeUpdate", category, category, width, category.Get("image_yscale").AsReal);
        SkillData.SetUpCategory(category, text, skills.Select(entry => entry.Icon).ToList(), Background(skills.Count));
        return category;
    }

    // Tabs moved from the list's end into one of the game's sections: after its last tab (before the gap and the next
    // header, if there's one), and the list laid out again.
    private static void MoveIntoSection(Instance left, Instance sectionHeader, List<Instance> tabs)
    {
        GmValue children = left.Get("guiChildrenList");
        int header = Gm.AssetGetIndex("o_skill_metacategory"), gap = Gm.AssetGetIndex("o_guiSimpleEmpty");
        foreach (var tab in tabs)
            Game.CallBuiltinTrusted("ds_list_delete", default, default, children,
                Game.CallBuiltinTrusted("ds_list_find_index", default, default, children, tab));
        int count = Game.CallBuiltinTrusted("ds_list_size", default, default, children).AsInt;
        int at = Game.CallBuiltinTrusted("ds_list_find_index", default, default, children, sectionHeader).AsInt + 1;
        while (at < count && InstanceOf(Game.CallBuiltinTrusted("ds_list_find_value", default, default, children, at)).Get("object_index").AsInt is int obj
               && obj != header && obj != gap)
            at++;
        foreach (var tab in tabs)
            Game.CallBuiltinTrusted("ds_list_insert", default, default, children, at++, tab);
        Game.CallScript("scr_guiContainerRebuild", left, left.Get("guiBaseParent"));
    }

    private static Instance Simple(Instance parent, string obj, double depth)
        => Game.CallScript("scr_guiCreateSimple", parent, parent, Gm.AssetGetIndex(obj), depth, 0, 0).AsInstance;

    // A page's background for so many skills, as the game's are drawn: its panel colour, the header line, and a
    // slot (locked, until learnt) where each skill goes - drawn once from the game's sword page.
    // The native build: a mod skill object's Create done in C# (its GML on the VM build) - hooked once each.
    private static readonly HashSet<string> NativeHooked = new();
    private static void HookNative(string obj, Action<Instance> created)
    {
        if (Game.IsNative && _loader != null && NativeHooked.Add(obj))
            ObjectEvents.Hook(_loader, obj, "Create_0", after: created);
    }

    private static int Background(int count)
    {
        if (Backgrounds.TryGetValue(count, out int sprite) && Game.CallBuiltinTrusted("sprite_exists", default, default, sprite).AsBool)
            return sprite;
        const int width = 163, height = 257;
        int source = (int)Sprite.s_sword_branch;
        GmValue surface = Game.CallBuiltinTrusted("surface_create", default, default, width, height);
        Game.CallBuiltinTrusted("surface_set_target", default, default, surface);
        Game.CallBuiltinTrusted("draw_clear_alpha", default, default, Draw.PanelColour, 1);
        Game.CallBuiltinTrusted("draw_sprite_part", default, default, source, 0, 0, 16, width, 6, 0, 16);
        for (int i = 0; i < count; i++)
        {
            double x = 31 + 50 * (i % 3), y = 55 + 73 * (i / 3);
            Game.CallBuiltinTrusted("draw_sprite_part", default, default, source, 0, 16, 40, 31, 31, x - 15, y - 15);
        }
        Game.CallBuiltinTrusted("surface_reset_target", default, default);
        sprite = Game.CallBuiltinTrusted("sprite_create_from_surface", default, default, surface, 0, 0, width, height, false, false, 0, 0).AsInt;
        Game.CallBuiltinTrusted("surface_free", default, default, surface);
        Backgrounds[count] = sprite;
        return sprite;
    }

    // A picture from the mod's Assets folder made like the game's (like): its frames, said if the size is off.
    private static int LoadLike(Entry entry, string file, int like)
    {
        int frames = like >= 0 ? Math.Max(1, Game.CallBuiltinTrusted("sprite_get_number", default, default, like).AsInt) : 1;
        int sprite = entry.Context.LoadSprite(file, frames);
        if (sprite < 0)
        {
            entry.Context.Log($"skill \"{entry.Skill.Id}\": no picture Assets\\{file}");
            return -1;
        }
        if (like >= 0)
        {
            double width = Draw.SpriteWidth(sprite), height = Draw.SpriteHeight(sprite);
            double likeWidth = Draw.SpriteWidth(like), likeHeight = Draw.SpriteHeight(like);
            if (width != likeWidth || height != likeHeight)
                entry.Context.Log($"skill \"{entry.Skill.Id}\": {file} has {frames} frame(s) of {width}x{height}; the game skill's icon has {frames} of {likeWidth}x{likeHeight} - it should be {likeWidth * frames}x{likeHeight}");
            Game.CallBuiltinTrusted("sprite_set_offset", default, default, sprite,
                Game.CallBuiltinTrusted("sprite_get_xoffset", default, default, like), Game.CallBuiltinTrusted("sprite_get_yoffset", default, default, like));
        }
        return sprite;
    }
}
