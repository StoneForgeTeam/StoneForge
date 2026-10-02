using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using F = System.IO.File;

namespace TestMod;

public class M : IStoneMod
{
    public void Unload() { }

    public void Load(ModContext context)
    {
F.Delete("x");
    }
}
