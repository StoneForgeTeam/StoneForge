using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;

namespace TestMod;

public class M : IStoneMod
{
    public void Unload() { }

    public void Load(ModContext context)
    {
        // (Makes objects of any type by reflection: refused, as the rest of reflection.)
        context.Log(System.Text.Json.JsonSerializer.Serialize(context));
    }
}
