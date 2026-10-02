using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using static System.IO.File;

namespace TestMod;

public class M : IStoneMod
{
    public void Unload() { }

    public void Load(ModContext context)
    {
Delete("x");
    }
}
