namespace StoneForge;

/// <summary>What a mod's active skill (<see cref="ModSkill"/>) and passive skill (<see cref="ModPassive"/>) share: its
/// key and IDs, its name, description and icon, its tab of the skills menu, and what it takes to learn it - always an
/// ability point, and whatever it requires. Add either with <see cref="Skills.Add"/>.</summary>
public abstract class ModSkillBase
{
    private protected ModSkillBase(string key, string what)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException($"A {what}'s key is letters, digits and _ only", nameof(key));
        ModIdentity.CheckKey(key, what);
        Key = key;
    }

    /// <summary>Its key in its mod ("shock_bolt"; saves refer to it by this and the mod's ID - don't change it once
    /// players have it).</summary>
    public string Key { get; }
    /// <summary>Its full ID, "yourmod:shock_bolt" (set when it's added).</summary>
    public string Id { get; private set; } = "";
    // Its name in the game's data, "yourmod__shock_bolt": its objects are named after it.
    internal string GameKey { get; private set; } = "";
    /// <summary>Its name in game.</summary>
    public string? DisplayName { get; protected set; }
    /// <summary>Its description, in its tooltip.</summary>
    public string? Description { get; protected set; }
    /// <summary>Its icon, a PNG in the mod's Assets folder, the size (and frames) of the game's skill icons.</summary>
    public string? Icon { get; protected set; }

    /// <summary>The tab of the skills menu it's on, with the mod's other skills of that tab (9 to a tab - more go on
    /// "Tab 2"...). Default: the mod's name.</summary>
    public string? Tab { get; protected set; }
    /// <summary>The section its tab is in, in the skills menu's list (<see cref="SkillGroup"/>): one of the game's -
    /// <see cref="SkillGroup.Sorcery"/>, beside Pyromancy... - or a section of the mods' own, after the game's, under its
    /// own header (as SORCERY is over Pyromancy...). Default: <see cref="SkillGroup.Mods"/>.</summary>
    public string Group { get; protected set; } = SkillGroup.Mods;

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
    /// <c>GameName</c> (<c>RequireSkill(ChainLightning.GameName)</c>) - or a game passive's ("conduit").</summary>
    protected void RequireSkill(string gameSkillId)
    {
        if (string.IsNullOrWhiteSpace(gameSkillId))
            throw new ArgumentException("A skill's id", nameof(gameSkillId));
        // (Another mod's, by its full ID "othermod:key", as the game knows it.)
        _gameSkills.Add(gameSkillId.Contains(':') ? ModIdentity.ToGameKey(gameSkillId) : gameSkillId.ToLowerInvariant());
    }

    /// <summary>Learnt only once another mod skill - active or passive - is (this mod's or another's).</summary>
    protected void RequireSkill(ModSkillBase skill) => _modSkills.Add(skill);

    private readonly List<string> _gameSkills = new();
    private readonly List<ModSkillBase> _modSkills = new();
    // The skills it needs learnt, as the game knows them (a mod skill once it's added).
    internal IEnumerable<string> RequiredSkills => _gameSkills.Concat(_modSkills.Where(s => s.GameKey.Length > 0).Select(s => s.GameKey));

    /// <summary>The mod that added it; set by <see cref="Skills.Add"/>.</summary>
    protected ModContext Context { get; private set; } = null!;

    internal void Attach(ModContext context)
    {
        Context = context;
        Id = context.ContentId(Key);
        GameKey = context.GameKey(Key);
    }
}
