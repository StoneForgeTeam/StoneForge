using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UndertaleModLib.Models;

namespace StoneForge.MslHost;

internal static class CodeOrdering
{
    // The legacy writer gives child entries the most recently written root's bytecode.
    // Mods can insert newly compiled children before their parent, so restore whole groups before saving.
    internal static void Normalize(IList<UndertaleCode> codes)
    {
        var present = new HashSet<UndertaleCode>(codes);
        if (present.Count != codes.Count) throw new InvalidDataException("MSL produced duplicate code entries.");
        var children = new Dictionary<UndertaleCode, List<UndertaleCode>>();
        foreach (var code in codes)
        {
            if (code.ParentEntry is not { } parent) continue;
            if (!present.Contains(parent))
                throw new InvalidDataException($"MSL code {code.Name?.Content} has a missing parent.");
            if (!children.TryGetValue(parent, out var group)) children[parent] = group = new();
            group.Add(code);
        }
        var ordered = new List<UndertaleCode>(codes.Count);
        void Append(UndertaleCode code)
        {
            ordered.Add(code);
            if (children.TryGetValue(code, out var group))
                foreach (var child in group) Append(child);
        }
        foreach (var root in codes.Where(c => c.ParentEntry is null)) Append(root);
        if (ordered.Count != codes.Count) throw new InvalidDataException("MSL produced a cycle in code parents.");
        for (int i = 0; i < ordered.Count; i++) codes[i] = ordered[i];
    }
}
