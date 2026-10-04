namespace StoneForge;

/// <summary>The game's contracts (the notice boards' jobs), as its own scripts handle them.</summary>
public static class Contracts
{
    /// <summary>Takes a contract away as the game does when it's over (scr_contract_delete, run by its time controller:
    /// unlisted, and failed if it wasn't done).</summary>
    public static void Delete(DsMap contract)
    {
        Instance clock = Instances.All(GameObjectId.o_time_controller).FirstOrDefault();
        Game.CallScript("scr_contract_delete", clock, contract, false);
    }

    /// <summary>Takes a contract's quest items out of the inventory, as the game does when it fails
    /// (scr_contract_quest_items_delete).</summary>
    public static void DeleteQuestItems(DsMap contract) => Game.CallScript("scr_contract_quest_items_delete", default, contract, false, false);
}
