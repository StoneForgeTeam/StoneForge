namespace StoneForge;

// Which mod an item is from, on its tooltip: "Mod: Example Mod" at the bottom left, on the price's row (which the
// game leaves empty on the left) - for mods' weapons and armour (o_hoverWeapon) and consumables (o_hoverConsum,
// o_hoverPotion). Drawn after the tooltip draws itself (its user event 11, into its surface), at its content's
// last row, in its text, a space under its description - unless the item's ShowModName is off, or it has no price
// (and so no row for it).
internal static class ModNameTooltip
{
    private static readonly int Label = Draw.Rgb(149, 121, 106), Name = Draw.Rgb(39, 177, 234);

    internal static void Install(ModContext loader)
    {
        foreach (string hover in new[] { "o_hoverWeapon", "o_hoverConsum", "o_hoverPotion" })
        {
            loader.OnCode($"gml_Object_{hover}_Other_20", after: (self, _) => MakeRoom(self));
            loader.OnCode($"gml_Object_{hover}_Other_21", after: (self, _) => DrawFor(self));
        }
    }

    // As the tooltip works its size out (its user event 10): a space between the description and the price's row,
    // where the mod's name goes - the description's height grown by the game's space between a tooltip's parts.
    private static void MakeRoom(Instance hover)
    {
        if (ModOf(hover) == null || hover.Get("priceHeight").AsReal <= 0)
            return;
        double space = hover.Get("spaceHeight").AsReal;
        hover.Set("descriptionHeight", hover.Get("descriptionHeight").AsReal + space);
        hover.Set("contentHeight", hover.Get("contentHeight").AsReal + space);
    }

    private static string? ModOf(Instance hover)
    {
        Instance item = hover.Get("owner").AsInstance;
        return item.IsNone ? null : Items.ModNameOf(item) ?? Consumables.ModNameOf(item);
    }

    private static void DrawFor(Instance hover)
    {
        if (ModOf(hover) is not string mod)
            return;
        double priceHeight = hover.Get("priceHeight").AsReal;
        if (priceHeight <= 0)
            return;
        double x = hover.Get("contentX").AsReal;
        double y = hover.Get("contentY").AsReal + hover.Get("contentHeight").AsReal - priceHeight;
        GmValue scale = hover.Get("textScale"), font = Game.Global["f_dmg"];
        const string label = "Mod: ";
        Scripts.scr_drawText.Call(null, x, y, label, Label, Draw.AlignLeft, Draw.AlignTop, font, scale, 1);
        double labelWidth = Game.CallScript("scr_stringGetWidth", default, label, font, scale).AsReal;
        Scripts.scr_drawText.Call(null, x + labelWidth, y, mod, Name, Draw.AlignLeft, Draw.AlignTop, font, scale, 1);
    }
}
