using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;

namespace TestMod;

public class M : IStoneMod
{
    public record R(int A, string B);
    private static int Twice(int x) => x * 2;

    public void Unload() { }

    public void Load(ModContext context)
    {

        var list = new List<int> { 3, 1, 2 };
        var sorted = list.OrderBy(x => x).Select(x => x * 2).ToList();
        var map = new Dictionary<string, int> { ["a"] = 1 };
        foreach (var (k, v) in map) context.Log($"{k}={v}");
        (int a, string b) t = (1, "x");
        var (p, q) = t;
        var sb = new System.Text.StringBuilder();
        sb.Append(Math.Max(1, 2)).Append(DateTime.Now.Hour);
        var rx = System.Text.RegularExpressions.Regex.IsMatch("abc", "b");
        Func<int, int> f = x => x + 1;
        Action act = () => context.Log("hi");
        act();
        var path = System.IO.Path.Combine("a", "b.txt");
        string text = context.Files.Exists("x.txt") ? context.Files.ReadAllText("x.txt") : "";
        context.Files.WriteAllText("save.json", "{}");
        var rec = new R(1, "n");
        context.Log(rec.ToString() + string.Join(",", sorted) + f(1) + p + q + rx + path + text);
        try { throw new InvalidOperationException("x"); } catch (Exception e) { context.Log(e.Message); }
        var span = "hello".AsSpan(1, 2);
        context.Log(span.ToString());
        double hp = 5; GmValue g = hp; g = g + 2;
        Events.o_player.Step_0.After(context, pl => { double h = pl.HP; });

    }
}
