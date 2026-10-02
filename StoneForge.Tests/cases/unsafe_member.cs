using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;

namespace TestMod;

public class M : IStoneMod
{
    unsafe void P(int* x) { }

    public void Unload() { }

    public void Load(ModContext context)
    {

    }
}
