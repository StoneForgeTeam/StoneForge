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

        var q = from x in new[] { 1, 2, 3 } where x > 1 select x * 2;
        var g = new[] { "a", "bb" }.GroupBy(s => s.Length).ToDictionary(k => k.Key, v => v.Count());
        context.Log(string.Join(",", q) + g.Count);

    }
}
