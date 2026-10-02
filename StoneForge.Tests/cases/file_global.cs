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
global::System.IO.File.WriteAllText("x", "y");
    }
}
