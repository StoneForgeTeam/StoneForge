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
public abstract class ModSkill : ModSkillBase
{
    private readonly Dictionary<string, string> _columns = new();

    protected ModSkill(string key, string basedOn) : base(key, "skill")
    {
        BasedOn = basedOn;
    }

    // (Its objects are o_skill_ + its game key, and o_skill_ + its game key + _ico.)

    /// <summary>The game skill it's based on (its object's id: "jolt"). Its name, description and icon are the game
    /// skill's unless it sets its own.</summary>
    public string BasedOn { get; }

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
}
