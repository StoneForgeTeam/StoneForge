namespace StoneForge;

// The game's skills table and skills menu, as mods' skills are added to them (Skills) - what were the patcher's GML scripts
// scr_stonemod_skill_define and scr_stonemod_skill_category_setup, and on the native (YYC) build the mod objects' GML
// too (their Create events, the mod page's layout), done here: the native build can't run GML. The table is the game's
// own: global.skills_stat_csv (its header the row whose second column is "Object"), and its checks
// global.skill_extra_validate_map.
internal static class SkillData
{
    /// <summary>A mod's skill: a game skill's row (<paramref name="basedOn"/>: its object's id, "active_defence" - the row's
    /// name matched without case) copied under <paramref name="key"/> (or put in place of its own, defined again), with
    /// some columns changed ("column=value|..."), and its checks as the game skill's. False if there's no such row.</summary>
    internal static bool DefineSkill(string key, string basedOn, string columns)
    {
        using GmArray? csv = Game.Global["skills_stat_csv"].AsArray;
        if (csv == null)
            return false;
        int headerAt = -1, baseAt = -1, existing = -1;
        for (int i = 0; i < csv.Length; i++)
        {
            using GmArray? row = csv[i].AsArray;
            if (row == null || row.Length < 2)
                continue;
            string? id = row[0].AsString;
            if (row[1].AsString == "Object")
                headerAt = i;
            else if (id == key)
                existing = i;
            else if (baseAt < 0 && string.Equals(id, basedOn, StringComparison.OrdinalIgnoreCase))
                baseAt = i;
        }
        if (headerAt < 0 || baseAt < 0)
            return false;
        using GmArray header = csv[headerAt].AsArray!;
        string baseName;
        var values = new GmValue[header.Length];
        using (GmArray baseRow = csv[baseAt].AsArray!)
        {
            baseName = baseRow[0].AsString ?? basedOn;
            int baseWidth = baseRow.Length;
            for (int i = 0; i < values.Length; i++)
                values[i] = i < baseWidth ? baseRow[i] : "";
        }
        values[0] = key;
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 1; i < values.Length; i++)
            if (header[i].AsString is { } column)
                names.TryAdd(column, i);
        foreach (string pair in columns.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals > 0 && names.TryGetValue(pair[..equals], out int at))
                values[at] = pair[(equals + 1)..];
        }
        using (GmArray made = GmArray.From(values))
        {
            if (existing >= 0)
                csv[existing] = made;
            else
                csv.Push(made);
        }
        if (Game.Global["skill_extra_validate_map"].AsDsMap is { } checks && checks.Has(baseName))
            checks[key] = checks[baseName];
        return true;
    }

    /// <summary>A mod's page of the skills menu (an o_skill_category_stonemod) filled: its name (a key of global.tier_name),
    /// its skills' icon objects (none: an empty page) and its background.</summary>
    internal static void SetUpCategory(Instance category, string text, IReadOnlyList<int> icons, int background)
    {
        category.Set("text", text);
        using (GmArray skills = icons.Count == 0 ? GmArray.From(new GmValue[] { "1" }) : GmArray.From(icons.Select(icon => (GmValue)icon)))
            category.Set("skill", skills);
        if (background >= 0 && Game.CallBuiltinTrusted("sprite_exists", default, default, background).AsBool)
            category.Set("branch_sprite", background);
    }

    // ---- the native build: the mod objects' GML, done here ----

    /// <summary>A mod's skill object made (o_skill_key - its Create, the game skill's, run): made again under the mod's key,
    /// its row of the skills table, name and description.</summary>
    internal static void SkillCreated(Instance self, string key)
    {
        self.Set("skill", key);
        Game.CallBuiltinTrusted("ds_list_clear", default, default, self.Get("mid_text"));
        Game.CallScript("scr_skill_atr", self, 0);
    }

    /// <summary>A mod's skill's icon made (o_skill_key_ico - o_skill_ico's Create run): an icon for the mod's skill, learnt
    /// with ability points alone (nothing else to unlock it).</summary>
    internal static void IconCreated(Instance self, int skillObject)
    {
        self.Set("child_skill", skillObject);
        Unlocked(self);
        Game.CallBuiltinTrusted("ds_list_clear", default, default, self.Get("attribute"));
        // (0: ev_create.)
        Game.CallBuiltinAs("event_perform_object", self, self, skillObject, 0, 0);
    }

    /// <summary>A mod's passive made (o_pass_skill_key - o_skill_passive's Create run): its name and description the mod's,
    /// learnt with ability points alone.</summary>
    internal static void PassiveCreated(Instance self, string key)
    {
        Game.CallScript("scr_skill_atr", self, key);
        Unlocked(self);
    }

    /// <summary>A mod's page made (o_skill_category_stonemod - o_skill_category's Create run): empty until StoneForge fills
    /// it (SetUpCategory), before it makes its icons.</summary>
    internal static void CategoryCreated(Instance self)
    {
        self.Set("text", "stonemod");
        using (GmArray none = GmArray.From(new GmValue[] { "1" }))
            self.Set("skill", none);
        self.Set("branch_sprite", Gm.AssetGetIndex("s_basic_branch"));
    }

    /// <summary>A mod's page laying out its skills (user event 14 - o_skill_category's run): three to a row, as the game's own
    /// pages place theirs (columns 31, 81, 131; rows 55, 128, 201...), each a ctr_SkillPoint made as the game makes them
    /// (Gm.New).</summary>
    internal static void CategoryLaidOut(Instance self)
    {
        using GmArray? skills = self.Get("skill").AsArray;
        if (skills == null)
            return;
        GmValue render = self.Get("connectionsRender");
        for (int i = 0; i < skills.Length; i++)
        {
            GmValue icon = skills[i];
            if (icon.Kind == GmKind.String)
                continue;
            Gm.New("ctr_SkillPoint", render, icon, 31 + 50 * (i % 3), 55 + 73 * (i / 3));
        }
    }

    private static void Unlocked(Instance self)
    {
        self.Set("tier_to_open", -4);
        using (GmArray none = GmArray.Create())
            self.Set("attributes_names_to_open", none);
        self.Set("attributes_value_to_open", 0);
        self.Set("level_to_open", 0);
    }
}
