using System.Globalization;

namespace StoneForge;

/// <summary>A mod's skill: one of the game's (<see cref="BasedOn"/>, its id) under the mod's own key - its targeting
/// (a target, a tile, an area, none), cast, cooldown, energy cost, sounds - with what the mod changes, and its own
/// effect, <see cref="OnCast"/>, in place of the game skill's. Inherit the game's from StoneForge.GameSkills
/// (<c>class ShockBolt : Jolt</c>), or this with its id:
/// <code>
/// public class ShockBolt : Jolt
/// {
///     public ShockBolt() : base("example_shock_bolt")
///     {
///         DisplayName = "Shock Bolt";
///         Icon = "shock_bolt.png";
///         Cooldown = 6;
///         EnergyCost = 12;
///     }
///     protected override void OnCast(SkillCast cast) => ...;
/// }
/// </code>
/// Add it with <see cref="Skills.Add"/>: it's on its <see cref="Tab"/> of the skills menu (under its
/// <see cref="Group"/>'s header), learnt with ability points. Its key must be written as a string literal in the base(...) call: StoneForge's patcher reads it
/// from the source to give the game objects for it (o_skill_&lt;key&gt; and its icon) at the game's next start.</summary>
public abstract class ModSkill
{
    private readonly Dictionary<string, string> _columns = new();

    protected ModSkill(string key, string basedOn)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException("A skill's key is letters, digits and _ only", nameof(key));
        ModIdentity.CheckKey(key, "skill");
        Key = key;
        BasedOn = basedOn;
    }

    /// <summary>Its key in its mod ("shock_bolt"; saves refer to it by this and the mod's ID - don't change it once
    /// players have it).</summary>
    public string Key { get; }
    /// <summary>Its full ID, "yourmod:shock_bolt" (set when it's added).</summary>
    public string Id { get; private set; } = "";
    // Its name in the game's data: its objects are o_skill_ + this (+ _ico), "yourmod__shock_bolt".
    internal string GameKey { get; private set; } = "";
    /// <summary>The game skill it's based on (its object's id: "jolt").</summary>
    public string BasedOn { get; }
    /// <summary>Its name in game (default: the game skill's).</summary>
    public string? DisplayName { get; protected set; }
    /// <summary>Its description (default: the game skill's).</summary>
    public string? Description { get; protected set; }
    /// <summary>Its icon, a PNG in the mod's Assets folder: the game skill's size (and frames, if it has more than
    /// one). Default: the game skill's.</summary>
    public string? Icon { get; protected set; }

    /// <summary>The tab of the skills menu it's on, with the mod's other skills of that tab (9 to a tab - more go on
    /// "Tab 2"...). Default: the mod's name.</summary>
    public string? Tab { get; protected set; }
    /// <summary>The header its tab is under in the skills menu's list, after the game's (as SORCERY is over
    /// Pyromancy...). Default: "Mods".</summary>
    public string Group { get; protected set; } = "Mods";

    /// <summary>The character level it can be learnt from (default: 0 - any).</summary>
    public int RequiredLevel { get; protected set; }

    internal int RequiredAttributePoints { get; private set; }
    internal IReadOnlyList<CharacterAttribute> RequiredAttributes { get; private set; } = Array.Empty<CharacterAttribute>();

    /// <summary>Learnt only with so many attribute points in these, together, over the 10 each starts at - as the
    /// game's skills ask: <c>RequireAttributes(14, CharacterAttribute.Perception, CharacterAttribute.Willpower)</c>
    /// is Perception and Willpower adding up to 34.</summary>
    protected void RequireAttributes(int points, params CharacterAttribute[] attributes)
    {
        if (attributes.Length == 0)
            throw new ArgumentException("Name at least one attribute", nameof(attributes));
        RequiredAttributePoints = points;
        RequiredAttributes = attributes.Distinct().ToArray();
    }

    /// <summary>Learnt only once a game skill is: its id, "chain_lightning" - its StoneForge.GameSkills class's
    /// <c>GameName</c> (<c>RequireSkill(ChainLightning.GameName)</c>).</summary>
    protected void RequireSkill(string gameSkillId)
    {
        if (string.IsNullOrWhiteSpace(gameSkillId))
            throw new ArgumentException("A skill's id", nameof(gameSkillId));
        // (Another mod's, by its full ID "othermod:key", as the game knows it.)
        _gameSkills.Add(gameSkillId.Contains(':') ? ModIdentity.ToGameKey(gameSkillId) : gameSkillId.ToLowerInvariant());
    }

    /// <summary>Learnt only once another mod skill is (this mod's or another's).</summary>
    protected void RequireSkill(ModSkill skill) => _modSkills.Add(skill);

    private readonly List<string> _gameSkills = new();
    private readonly List<ModSkill> _modSkills = new();
    // The skills it needs learnt, as the game knows them (a mod skill once it's added).
    internal IEnumerable<string> RequiredSkills => _gameSkills.Concat(_modSkills.Where(s => s.GameKey.Length > 0).Select(s => s.GameKey));

    /// <summary>Turns before it can be used again (default: the game skill's).</summary>
    public int Cooldown { set => SetColumn("KD", value.ToString(CultureInfo.InvariantCulture)); }
    /// <summary>The energy it costs (default: the game skill's).</summary>
    public int EnergyCost { set => SetColumn("MP", value.ToString(CultureInfo.InvariantCulture)); }
    /// <summary>How far it reaches, in tiles (default: the game skill's).</summary>
    public int Range { set => SetColumn("Range", value.ToString(CultureInfo.InvariantCulture)); }

    /// <summary>Whether the game skill's own effect happens too, after <see cref="OnCast"/> (default: no - only the
    /// mod's).</summary>
    public bool KeepGameEffect { get; protected set; }

    /// <summary>Whether the game skill's own conditions for using it stay (default: no - it can be used whenever
    /// a skill can: energy, cooldown, not silenced...). Some game skills only work in some cases - Static Field
    /// only with an enemy in Impulse or Resonance - which fit their effect, not a mod's.</summary>
    public bool KeepGameConditions { get; protected set; }

    /// <summary>The mod that added it; set by <see cref="Skills.Add"/>.</summary>
    protected ModContext Context { get; private set; } = null!;

    /// <summary>Cast - its energy paid and its cooldown started, by the game: its effect, here.</summary>
    protected internal virtual void OnCast(SkillCast cast) { }

    /// <summary>One of its columns in the game's skills table (table_skills_stats), from the game skill's.</summary>
    protected void Set(SkillColumn column, double value) => SetColumn(column.ToString(), value.ToString(CultureInfo.InvariantCulture));
    protected void Set(SkillColumn column, string value) => SetColumn(column.ToString(), value);

    private void SetColumn(string column, string value)
    {
        Items.CheckText(value, nameof(value));
        _columns[column] = value;
    }

    internal string ColumnsText => string.Join("|", _columns.Select(c => c.Key + "=" + c.Value));
    internal void Attach(ModContext context)
    {
        Context = context;
        Id = context.ContentId(Key);
        GameKey = context.GameKey(Key);
    }
}
