using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using StoneForge;

namespace TestMod;

// System.Text.Json's nodes, with the game's values as JSON - and GmArray.From(values) beside the JSON overloads,
// which needs nothing of System.Text.Json named.
public class M : IStoneMod
{
    public void Unload() { }

    public void Load(ModContext context)
    {
        JsonNode? node = JsonNode.Parse("{\"a\": [1, 2]}");
        if (node is JsonObject obj && obj["a"] is JsonArray array)
        {
            array.Add(JsonValue.Create(3));
            context.Log(obj.ToJsonString() + (array[0]!.GetValueKind() == JsonValueKind.Number));
        }
        try { JsonNode.Parse("{"); }
        catch (JsonException) { }
        var made = GmArray.From(new GmValue[] { 1, 2 });
        JsonArray back = made.ToJsonNode();
        GmValue value = GmValue.FromJsonNode(back);
        GmStruct strukt = GmStruct.FromJsonNode(new JsonObject { ["hp"] = 10 });
        context.Log(back.ToJsonString() + value.Kind + strukt.ToJson());
    }
}
