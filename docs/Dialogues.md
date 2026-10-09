# Mod dialogues

### Whole-dialogue language preview

With Dev enabled, the native dialogue window has a language dropdown at its
top right. Choose a language to refresh the NPC message and all responses together. Right-click **Edit text** then edits that
translation directly in the dialogue window. The separate per-entry language
pickers are no longer needed.

The dropdown is disabled while typing an unsaved inline edit; save with Enter
or cancel with Escape before changing languages. **Game language** restores the
normal view. The game's language setting is unchanged, and leaving the dialogue
or disabling Dev clears the preview. Registered mod text providers use the
selected language even through their automatic refreshes. Previewing does not
re-enter nodes, select responses, or execute their callbacks.

Missing mod translations use their normal fallback. Vanilla NPC lines preview
the matching translation from the game's multilingual table where available;
unresolved native text keeps its current wording. Edits still save only to the
selected mod's dialogue document.

### Editing dialogue lines

Enable Dev on the mod's tab before entering the game. Talk to an NPC and
right-click a message or response. **Edit text** edits the displayed text directly
inside the dialogue window. Enter saves; Escape cancels typing. The language
dropdown chooses the translation to edit, and changes appear immediately.

**Delete option** removes a whole response. **Restore original** clears its text
override, and **Restore dialogue** clears all edits for this conversation.
Changes save to a file for each NPC in the selected mod's `Dialogue` folder,
such as `Dialogue/npc_verren.json`, and load with Dev disabled. Existing
`npcs.json` files are split automatically and kept as `npcs.json.migrated.bak`.
Editing never selects a response or runs its code.

There is no alternatives editor or custom random-line selection. Old saved
`Variants` lists are ignored. Stoneshard retains its normal native dialogue behavior.

### Code options in the NPC editor

The **Trigger Code** picker includes these built-in actions for every mod:

| Trigger ID (prefix `stoneforge:`) | Behavior |
| --- | --- |
| `exit_dialogue` | Closes the whole NPC dialogue window. |
| `return_topics` | Returns to the NPC's root topics, finishing a nested mod conversation first. |
| `finish_conversation` | Finishes the current StoneForge conversation; a nested conversation returns to the NPC. |
| `restart_conversation` | Restarts the current StoneForge conversation at its start node, including its normal entry callback. |
| `refresh_dialogue` | Refreshes mod dialogue text/conditions or redraws the native dialogue. |
| `back` | Selects the existing native Back/Return response. |
| `continue` | Selects the existing native Continue response. |
| `next_page` | Selects the existing native next text page. |
| `open_trade` | Selects the current NPC's existing Trade response. |
| `ask_rumors` | Selects the existing news/chat response. |
| `learn_skills` | Selects the existing training response. |
| `rent_room` | Selects the existing room rental response. |

Built-ins have readable labels marked **StoneForge**; search by label or full ID.
They replace the bound response's usual action. Service/navigation shortcuts
require a currently available, unlocked native response in this NPC window and
recheck it when clicked. They use its native click event, preserving costs and
NPC-specific checks. They do not create services on NPCs that lack them.
Finish/restart require an active StoneForge conversation. Bindings save in the
selected mod's dialogue JSON and remain available without attributed functions.

Declare a synchronous static method in your mod:

```csharp
[DialogOption("work")]
public static void Work(DialogOptionContext dialogue)
{
    dialogue.Mod.Log("Selected " + dialogue.Id);
    // Use dialogue.Speaker for the NPC, and dialogue.Player for the player.
}
```

StoneForge discovers these methods when the mod loads and registers them as
`modid:work`. A parameterless `static void` method also works. New responses use
the function ID as their initial label; edit the text in the dialogue editor.
`TextKey` can supply a key in your mod's localization catalog instead.
Instance methods, async methods, generic methods and duplicate
keys are rejected when loading the mod.

Enable Dev for the mod, right-click an NPC message, and choose **Trigger Code**,
then select the registered action to add a response. Edit its
label and translations using the existing text editor. The binding is saved as
`ActionId` in that NPC's `Dialogue/npc_<id>.json`. StoneForge built-ins and that mod's actions are offered.
Keep the key stable when updating the mod; entries with missing actions are hidden.

**Trigger Code** is always available in the right-click menu. On a response it
assigns a callback to that response while preserving its existing dialogue
action. On an NPC message it inserts a new code response before the last option.
The picker searches the current mod's registered function IDs and labels; enter
either a short key or `modid:key`, or select a result. **Remove trigger** removes
a binding from an ordinary response. Code-only responses can be deleted instead.
The built-in actions remain available when the mod has no attributed functions.
Callbacks never execute from the picker or from an NPC message appearing.
Native response clicks queue their callbacks until the click event has returned.
If a callback destroys the panel or speaker, the original response is cancelled;
otherwise an ordinary response continues its existing action once.

Code runs when the player selects the response. Insertion, editing and language
refreshes do not invoke it. It can update quests or rewards, or open an existing
registered conversation in the same NPC window with `dialogue.Open(conversation)`.
Callbacks use StoneForge's normal mod error handling and are released on unload.

### Conditions in the NPC editor

Right-click a response and choose **Add condition**. Search the list of this mod's
registered conditions, or enter a key such as `kill_condition` or
`examplemod:kill_condition`. Once assigned, the context menu shows **Remove condition**.
The condition is independent of **Trigger Code**, so it also works on native
responses and authored topics with no action binding. It saves as `ConditionId`
in the NPC's JSON file and loads with Dev disabled.

```csharp
[DialogOption("kill")]
public static void Kill(DialogOptionContext dialogue) { /* perform the action */ }

[DialogCondition("kill_condition")]
public static DialogConditionResult GetKillCondition(DialogOptionContext dialogue)
{
    if (!dialogue.Speaker.Exists || dialogue.Speaker.Equals(dialogue.Player))
        return DialogConditionResult.Visible;
    if (dialogue.Speaker.Get("HP").AsReal <= 0)
        return DialogConditionResult.Visible;
    if (Game.CallScript("scr_gold_count", dialogue.Player, false, true).AsInt < 100)
        return DialogConditionResult.Visible;
    return DialogConditionResult.Enabled;
}
```

`Visible` shows a disabled response; `Enabled` permits selecting it, subject to
the game's own locks; `Hidden` hides it. Condition methods must be static,
synchronous and non-generic, returning `DialogConditionResult`, with no arguments
or one `DialogOptionContext`. They register automatically as `modid:key`; registration
does not execute them. `DialogOptionAttribute` no longer has `VisibleWhen` or
`EnabledWhen` properties.

Conditions refresh while the conversation is open and are checked again on
selection, including after an action click is queued. Keep conditions free of side
effects: they may run multiple times. Missing methods, exceptions, or invalid return
values keep the response visible but disabled. Delegates release on mod unload.

context.Dialogues registers branching conversations in Stoneshard's native
dialogue system. It uses the game's portraits, history, text paging and response
buttons. AddTopic adds a topic to matching NPCs' normal Talk conversation.
Finishing or closing that topic returns to the original NPC conversation.

```csharp
var saved = SaveData.ModData(context);
var dialogue = context.Dialogues.Add(new DialogueDefinition("smith_work", "offer")
{
    Nodes =
    {
        new DialogueNode("offer", "Could you help me?")
        {
            TextKey = "smith.offer",
            Choices =
            {
                new DialogueChoice("accept", "I'll help.", "thanks")
                {
                    TextKey = "smith.accept",
                    EnabledWhen = _ => !saved["accepted_help"].AsBool,
                    OnSelected = _ => saved["accepted_help"] = true
                },
                new DialogueChoice("decline", "Not now.")
                { TextKey = "smith.decline" }
            }
        },
        new DialogueNode("thanks", "Thank you. Come back when you're done.")
        {
            TextKey = "smith.thanks",
            Choices = { new DialogueChoice("leave", "Goodbye.") { TextKey = "smith.leave" } }
        }
    }
});
dialogue.AddTopic(npc => npc.Get("id_name").AsString == "osbrook_smith",
    _ => context.Localization.Get("smith.topic"));
```

Definitions and node/choice keys are validated when registered; IDs are qualified
by the mod, for example `mymod:smith_work`. Node and choice lists are snapshotted:
changing their lists later does not change the registered graph. A graph supports
up to 128 nodes and 32 choices per node, including loops. Duplicate keys, missing
start nodes and unknown `NextNode` targets are rejected.

Call `dialogue.Start(npc)` directly to open a registered tree, or pass an entry
node: `dialogue.Start(npc, "report")`. It returns the new `DialogueConversation`,
or null if a modal/conversation is already open, no player/speaker exists, or the
speaker is too far away. `MaximumDistance` defaults to two room-grid steps;
set it to null for a conversation that intentionally ignores distance. A topic
can select its starting node dynamically with AddTopic's third argument.

`AddTopic` also accepts an optional one-based `position`, for example
`dialogue.AddTopic(matchesNpc, topicText, position: 3)` puts it in slot 3 among
the native options. A position beyond the available options appends it. The
default keeps the previous append behavior. Vanilla options keep their relative
order; multiple mods requesting the same slot are applied in hook/load order.

### NPC-attached dialogue editor

Before entering the game, open **Mods**, select the mod you are developing, and
click **Enable dev**. Only one mod can have dev enabled: the other mods' dev buttons
are disabled until you click **Disable dev**. Dev mode is a session toggle and starts
off when you launch the game.

Walk up to an NPC and open **Talk**, then **right-click the NPC's text or a response**
to open a context menu. **Edit text** makes the original text area editable in place.
Saved labels for newly added options refresh in the open conversation immediately.
**Restore original** removes the selected vanilla entry's text/translation and
position overrides. **Restore dialogue** removes this mod's edits and added
options for the current NPC dialogue, including translations, ordering and hidden
responses. Other NPC dialogues and other mods keep their edits. Restore applies
immediately and saves the result.
Type there and press **Enter** to save or **Escape** to cancel.

The inline field supports left/right arrows (including holding them), Home/End,
Delete/Backspace, Shift+arrows to select, and Ctrl+A/C/X/V to select all, copy, cut,
and paste at the caret. **Localization** lets you select one of the game's languages
and add or update that entry's translation in place, without changing the game language.
Enter saves the translation immediately to the developing mod's dialogue JSON. The
current language refreshes immediately; later game language changes refresh open
dialogues as well. Missing translations fall back to saved US English, then native text.
Explicit localization applies to the entry's fragment/option; ordinary text edits
retain their original displayed-variant scope.

Responses also have **Move up** and **Move down**, which immediately refresh and
save their positions. Movement keeps the response's
existing game action and dynamic label. First/last responses disable the unavailable
direction. Clicking outside the popup or pressing Escape dismisses it.
**Add above** and **Add below** insert a new topic beside the clicked response in
the NPC's Talk menu, then start editing its label in place. Open the new topic and
edit its placeholder NPC reply the same way. **Delete option** removes an authored
topic or hides an existing response for your mod. **Restore deleted options** appears
in the context menu when existing responses have been hidden. At least one response
remains available; editing operations are disabled on text paging controls.
**Ctrl+F8** opens the full editing strip for adding topics and resetting edits.
While a popup or the strip is open, clicking a native response selects it without
executing its trade, quest or other game action. **Done** closes the strip.

**Add** creates a new topic for this NPC's current Talk flow: enter its label,
NPC reply and option number, then Apply or Save. Selecting the new topic in
normal play opens its reply in Stoneshard's native dialogue window; Back returns
to the NPC's original options. Added topics can be edited or deleted from the
NPC menu. Return to the original NPC menu before adding another topic.

**Apply** refreshes the current presentation without advancing the dialogue or
replaying its actions. **Save** applies a draft and keeps changes. **Reset**
restores existing entries; for an authored topic the button becomes **Delete**.
Vanilla presentation edits and authored topics live in
`mods/<selected mod>/Dialogue/npc_<id>.json`, with the previous version kept as `.bak`.
They load with that mod, including when dev mode is off, and stop applying when the
mod is disabled. The former global `dotnet/DialogueEditor/npcs.json` is no longer
loaded. They apply to the NPC's stable `id_name` and
dialogue flow, rather than its temporary room instance ID. Edited vanilla text
is language-specific and tied to the original displayed text variant; the game
still selects lines according to its normal context, such as first meeting versus
already-discussed responses. Editing one variant does not overwrite another.
If the original rendered text changes (including dynamic names or numbers), that
new variant keeps its native text until separately edited. Existing saved broad
overrides remain compatible; reset and edit them again to use variant scoping.
Positions apply across languages. New topics use their
authored text as fallback until translations are added.

StoneForge nodes/responses keep using their owning mod's editor override file;
NPC-authored topics are stored in the central NPC document. Existing actions,
conditions and destinations remain unchanged. This first authoring version adds
a topic, one NPC reply and a Back response, rather than a whole quest/branch graph.

### Registered dialogue override files

While a StoneForge conversation is open, the NPC editing strip edits its current
node/response text and response order. Type `\n` for a line break and `\\` for a
literal backslash. It no longer opens a separate tree browser.

**Apply** updates this session; an open mod conversation refreshes immediately.
**Save** applies a changed draft and saves all edits for this tree to
`mods/<mod>/Dialogue/<key>.editor.json`; the previous file is kept as `.bak`.
Files load automatically when the mod registers the dialogue on the next start
or reload. Ship this file with a mod to distribute the presentation changes.
**Reset** restores the selected text for the current language and its position;
resetting a response restores the whole node's response order. Reset saves the
restored defaults. Closing without saving keeps applied edits for this session.
An unapplied draft must be applied, saved or reset before changing selections.

Editor text overrides apply only to the language and resolved text variant being
edited. TextProvider and localization still run when context/language changes;
other resolved values retain their original dynamic text. Older broad overrides
in existing files remain compatible; reset and re-edit them to scope them to a
variant. Positions apply to all languages. The editor works with the current
NPC line and visible responses, without entering another node or invoking rewards.

`DialogueConversation` exposes `Speaker`, `NodeKey`, current `Text`, `Responses`,
`IsOpen`, `CloseReason`, and a transient `State` dictionary. `Choose(key)` selects
an eligible response; `GoTo(node)` changes nodes; `Close()` cancels. A choice with
no next node ends normally after its callback. If its callback explicitly calls
GoTo or Close, that takes precedence over the choice's next node.

`VisibleWhen` hides responses; `EnabledWhen` greys them out. Conditions are checked
again on selection, so an old button cannot bypass changed eligibility. Actions
run once per successful selection. Each explicit node visit runs its OnEnter
callback once. Refreshes do not run entry callbacks or choice actions. Keep text
and condition providers free of side effects; they cannot navigate or select
responses. Use choice callbacks for quest progress, item hand-ins and rewards.

`TextKey` uses the registering mod's localization catalog. `TextProvider` overrides
both the key and literal text, and can format current values:

```csharp
new DialogueNode("report", "How is the job going?")
{
    TextProvider = conversation => context.Localization.Get("smith.progress",
        saved["help_progress"].AsInt)
};
```

Text and response conditions refresh periodically while open, immediately after
language/catalog changes, or on `conversation.Refresh()`. Long text and responses
use the game's text paging and response layout. Close with the native controls or finish
a branch. It also closes when its speaker disappears/leaves range, the room
changes, or its mod unloads. Callback failures are attributed to the owning mod
and close the conversation without retrying its action. OnClosed receives the
close reason once; it is not called during mod unload.

Conversations are not saved or resumed. Persist outcomes through quests or ModData;
reopen at the appropriate node from those saved values. ExampleMod's
`ExampleNpcJobs.cs` demonstrates saved employers, acceptance, partial deliveries,
filtered kill tracking and returning for one-time quest rewards using this API.
