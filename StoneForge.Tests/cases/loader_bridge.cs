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
System.Func<int> x = () => 0; var b = (object)nameof(Bridge); Bridge.Equals(b, b);
    }
}
