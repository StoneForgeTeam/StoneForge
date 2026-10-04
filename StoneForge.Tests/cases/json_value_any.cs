using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using StoneForge;

namespace TestMod;

public class M : IStoneMod
{
    public void Unload() { }

    public void Load(ModContext context)
    {
        // (Any object, written by reflection - every public property it has: refused.)
        context.Log(JsonValue.Create(context)!.ToJsonString());
    }
}
