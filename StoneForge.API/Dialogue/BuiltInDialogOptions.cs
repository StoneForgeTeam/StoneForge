namespace StoneForge;

// These controls replace the selected response; services click an existing
// native button instead of bypassing its costs, locks, or NPC-specific logic.
internal static class BuiltInDialogOptions
{
    private static readonly ModContext Context = new("stoneforge");
    internal static readonly IReadOnlyDictionary<string, DialogOptions.Entry> All = Create();
    private static Dictionary<string, DialogOptions.Entry> Create()
    {
        var entries = new Dictionary<string, DialogOptions.Entry>(StringComparer.Ordinal);
        void Add(string key, Action<DialogOptionContext> action, Func<DialogOptionContext, bool>? enabled = null)
        {
            string id = "stoneforge:" + key;
            entries[id] = new(id, Context, new(key), action, null, c => c.Panel.Exists && c.Speaker.Exists && !NativeDialogue.SceneBlocked && (enabled?.Invoke(c) ?? true), true);
        }
        void Button(string key, params string[] nativeKeys) => Add(key, c =>
        {
            var button = FindButton(c, nativeKeys);
            if (button.Exists) Game.CallBuiltinAs("event_perform", button, button, 6, 4);
        }, c => FindButton(c, nativeKeys).Exists);
        Add("exit_dialogue", c => { Active(c)?.Close(); if (c.Panel.Exists) c.Panel.Destroy(); });
        Add("return_topics", c =>
        {
            Active(c)?.Close(); if (!c.Panel.Exists) return;
            using var context = c.Panel.Get("dialog_id").AsStruct;
            string root = context?["RootFragment"].AsString ?? "";
            if (root.Length > 0) Hooks.CallOriginal("scr_dialogue_advance", c.Panel, c.Panel, new GmValue[] { root });
        });
        Add("finish_conversation", c => Active(c)!.Close(), c => Active(c) != null);
        Add("restart_conversation", c => { var active = Active(c)!; active.GoTo(active.Dialogue.Definition.StartNode); }, c => Active(c) != null);
        Add("refresh_dialogue", c =>
        {
            if (Active(c) is { } active) active.Refresh();
            else
            {
                var render = ContextMenus.InstanceOf(c.Panel.Get("render"));
                if (render.Exists) Game.CallBuiltinAs("event_user", render, render, 0);
            }
        });
        Button("back", "back", "return");
        Button("continue", "continue", "custom_continue");
        Button("next_page", "next");
        Button("open_trade", "trade", "tradeVogt");
        Button("ask_rumors", "chat");
        Button("learn_skills", "learn");
        Button("rent_room", "rent_room");
        return entries;
    }
    private static DialogueConversation? Active(DialogOptionContext c) => Dialogues.Active is { } active &&
        active.Native is { OwnsPanel: true } native && native.Panel.Equals(c.Panel) ? active : null;
    private static Instance FindButton(DialogOptionContext c, string[] keys)
    {
        if (!c.Panel.Exists) return default;
        int count = Gm.InstanceNumber(GameObjectId.o_contract_button);
        for (int i = 0; i < count; i++)
        {
            var button = ContextMenus.InstanceOf(Game.CallBuiltin("instance_find", (int)GameObjectId.o_contract_button, i));
            if (!button.Exists || !ContextMenus.InstanceOf(button.Get("parent")).Equals(c.Panel) || !button.Get("canPress").AsBool) continue;
            string key = button.Get("func").AsString; int hash = key.IndexOf("_HASH", StringComparison.Ordinal); if (hash > 0) key = key[..hash];
            if (keys.Contains(key, StringComparer.Ordinal)) return button;
        }
        return default;
    }
}
