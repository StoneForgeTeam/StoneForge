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
var q = new[] { 1 }.AsQueryable().Where(x => x > 0);
    }
}
