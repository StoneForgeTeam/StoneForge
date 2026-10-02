namespace StoneForge;

/// <summary>Something run every game frame: <see cref="Tick"/>, with the seconds since the last frame. A mod
/// class implementing it is ticked from when it's loaded; anything else is ticked once given to
/// <see cref="ModContext.AddTickable"/> - or to <see cref="Buffs.Add"/> / <see cref="Items.Add(ModContext, ModItem)"/>, which add a
/// buff or item that implements it. All of a mod's stop when it's switched off.</summary>
public interface ITickable
{
    void Tick(double deltaTime);
}
