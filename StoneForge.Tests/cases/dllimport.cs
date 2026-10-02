using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using System.Runtime.InteropServices;

namespace TestMod;

public class M : IStoneMod
{
    [DllImport("kernel32")] static extern int Beep(int a, int b);

    public void Unload() { }

    public void Load(ModContext context)
    {

    }
}
