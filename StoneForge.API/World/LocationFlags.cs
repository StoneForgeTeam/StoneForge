namespace StoneForge;

/// <summary>What of a location's saved state is spawned afresh the next time it loads (a preset's "flags", the game's
/// names): a flag that's set makes the loader ignore that kind's saved entities, spawn new ones, and clear the flag. The
/// game sets them when a location respawns - a settlement's mobs, NPCs, corpses, dropped loot, doors... - and a
/// location never saved spawns everything anyway.</summary>
[Flags]
public enum LocationFlags
{
    None = 0,
    Mobs = 1,
    Npc = 2,
    Corpses = 4,
    LootRoom = 8,
    LootDrop = 16,
    LootGrow = 32,
    StuffRoom = 64,
    StuffGrow = 128,
    ContainersRoom = 256,
    ContainersGrow = 512,
    ContainersCrime = 1024,
    Doors = 2048,
    Transitions = 4096,
    Marks = 8192,
    Triggers = 16384,
    TriggersAmbush = 32768,
    Insects = 65536,
}
