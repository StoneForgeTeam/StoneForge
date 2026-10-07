namespace StoneForge;

/// <summary>The game's quests (its questsDataMap, by quest id: "BlackTablet"...): whether one is started, done or failed,
/// and as they start, move on a step, or end (<see cref="OnStarted"/>, <see cref="OnProgress"/>, <see cref="OnCompleted"/>,
/// <see cref="OnFailed"/>) - told only when something changed, not for every call that would. (StoneForge makes the
/// quest scripts hookable itself.) Game thread only, in a game.</summary>
/// <example><code>
/// Quests.OnCompleted(context, quest => context.Log($"Done: {quest}"));
/// </code></example>
public static class Quests
{
    /// <summary>A quest's data (live, the game's); null if there's no quest by that id.</summary>
    public static DsMap? Get(string quest) => Game.Global["questsDataMap"].AsDsMap?.GetMap(quest);

    /// <summary>Whether a quest has been started.</summary>
    public static bool IsStarted(string quest) => Get(quest)?["IsStarted"].AsBool ?? false;

    /// <summary>Whether a quest is done.</summary>
    public static bool IsCompleted(string quest) => Get(quest)?["isComplete"].AsReal == 1;

    /// <summary>Whether a quest has failed.</summary>
    public static bool IsFailed(string quest) => Get(quest)?["isComplete"].AsReal == -1;

    /// <summary>Runs as a quest starts (scr_quest_start - also on its first step): its id.</summary>
    public static void OnStarted(ModContext context, Action<string> handler)
        => WhenTurns(context, "scr_quest_start", IsStarted, handler);

    /// <summary>Runs as a quest has been completed (scr_quest_set_complete): its id.</summary>
    public static void OnCompleted(ModContext context, Action<string> handler)
        => WhenTurns(context, "scr_quest_set_complete", IsCompleted, handler);

    /// <summary>Runs as a quest has failed (scr_quest_set_failed): its id.</summary>
    public static void OnFailed(ModContext context, Action<string> handler)
        => WhenTurns(context, "scr_quest_set_failed", IsFailed, handler);

    /// <summary>Runs as one of a quest's tasks moves on (scr_quest_set_progress: its count, or -1 done, -2 failed): the
    /// quest, the task, and its value now.</summary>
    public static void OnProgress(ModContext context, Action<string, string, GmValue> handler)
    {
        var before = new Stack<GmValue>();
        context.OnScript("scr_quest_set_progress",
            before: call =>
            {
                before.Push(call.Args.Length > 1 && call.Args[0].Kind == GmKind.String ? TaskValue(call.Args[0].AsString, call.Args[1]) : GmValue.Undefined);
                return false;
            },
            after: call =>
            {
                if (before.Count == 0)
                    return;
                GmValue was = before.Pop();
                if (call.Args.Length < 2 || call.Args[0].Kind != GmKind.String)
                    return;
                string quest = call.Args[0].AsString;
                GmValue now = TaskValue(quest, call.Args[1]);
                if (!now.IsUndefined && !now.Equals(was))
                    handler(quest, call.Args[1].AsString ?? "", now);
            });
    }

    // A quest's state as a script that may change it runs: told if it turned on (it may have been so already).
    private static void WhenTurns(ModContext context, string script, Func<string, bool> state, Action<string> handler)
    {
        var before = new Stack<bool>();
        context.OnScript(script,
            before: call =>
            {
                before.Push(call.Args.Length > 0 && call.Args[0].Kind == GmKind.String && state(call.Args[0].AsString));
                return false;
            },
            after: call =>
            {
                if (before.Count == 0)
                    return;
                bool was = before.Pop();
                if (!was && call.Args.Length > 0 && call.Args[0].Kind == GmKind.String && state(call.Args[0].AsString))
                    handler(call.Args[0].AsString);
            });
    }

    // A task's value: its quest's Tasks list holds five values a task - its id, its target, its count, its description...
    private static GmValue TaskValue(string quest, GmValue task)
    {
        if (Get(quest)?.GetList("Tasks") is not { } tasks)
            return GmValue.Undefined;
        for (int i = 0; i + 2 < tasks.Count; i += 5)
            if (tasks[i].Equals(task))
                return tasks[i + 2];
        return GmValue.Undefined;
    }
}
