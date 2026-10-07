namespace StoneForge;

/// <summary>The journal, as the game keeps it (its journalDataMap): its lists of tasks - the contracts taken, the tasks
/// failed - listing and unlisting one as the game does (scr_journalTaskAdd / Delete), and the diary page showing a task
/// (scr_journalDiaryUpdate). Game thread only, in a game.</summary>
public static class Journal
{
    private static int _diary = -2;

    /// <summary>The journal's data (null with no game).</summary>
    public static DsMap? Data => Game.Global["journalDataMap"].AsDsMap;

    /// <summary>The contracts listed as taken (contractsList: indexes into the contract list).</summary>
    public static DsList? Contracts => Data?.GetList("contractsList");

    /// <summary>The tasks listed as failed (tasksFailedList).</summary>
    public static DsList? Failed => Data?.GetList("tasksFailedList");

    /// <summary>Lists a task (its index) in one of the journal's lists, as the game does.</summary>
    public static void AddTask(DsList list, int index) => Game.CallScript("scr_journalTaskAdd", default, list, index);

    /// <summary>Takes a task (its index) out of one of the journal's lists, as the game does.</summary>
    public static void RemoveTask(DsList list, int index) => Game.CallScript("scr_journalTaskDelete", default, list, index);

    /// <summary>Whether a list has a task.</summary>
    public static bool Lists(DsList list, int index) => Game.CallBuiltin("ds_list_find_index", list.Id, index).AsInt >= 0;

    /// <summary>The diary page made the task's (scr_journalDiaryUpdate) - as a newly taken one with
    /// <paramref name="asNew"/> (the game's "new task" there), or brought up to date.</summary>
    public static void ShowInDiary(DsMap task, bool asNew = false)
    {
        if (asNew)
            Game.CallScript("scr_journalDiaryUpdate", default, task, false, true);
        else
            Game.CallScript("scr_journalDiaryUpdate", default, task);
    }

    /// <summary>Whether the diary page is showing a task now.</summary>
    public static bool DiaryShows(DsMap task)
    {
        if (_diary == -2)
            _diary = Gm.AssetGetIndex("o_diary");
        return _diary >= 0 && Instances.All(_diary).Exists(diary => diary.Get("map").AsReal == task.Id);
    }
}
