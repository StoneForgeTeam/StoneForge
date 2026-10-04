using System.Reflection;
using System.Runtime.InteropServices;

namespace StoneForge.Loader;

/// <summary>The entry point the native StoneForge.Bridge calls once the game has started.</summary>
public static unsafe class Bridge
{
    [UnmanagedCallersOnly]
    internal static int Initialize(BridgeApi* api, ManagedCallbacks* callbacks)
        => 2; // Legacy native bridges must upgrade, without dereferencing an incompatible layout.

    [UnmanagedCallersOnly]
    internal static int InitializeV2(BridgeApi* api, ManagedCallbacks* callbacks)
    {
        if (api == null || callbacks == null || api->Version != BridgeApi.ExpectedVersion
            || api->Size < sizeof(BridgeApi) || callbacks->Version != BridgeApi.ExpectedVersion
            || callbacks->Size < sizeof(ManagedCallbacks))
            return 2;
        if (api->Log == null || api->CallBuiltin == null || api->CallScript == null
            || api->GetVar == null || api->SetVar == null || api->HookCode == null
            || api->InstanceFromId == null || api->LastError == null || api->InstanceId == null || api->ReleaseRefs == null
            || api->GetVarAt == null || api->SetVarAt == null)
            return 2;
        Game.Api = api;
        Game.MarkGameThread();
        callbacks->OnFrame = &Hooks.OnFrame;
        callbacks->OnCodeBefore = &Hooks.OnCodeBefore;
        callbacks->OnCodeAfter = &Hooks.OnCodeAfter;
        callbacks->OnScript = &Hooks.OnScript;
        Hooks.BeforeFrame = () => { ModManager.Frame(); DevelopmentHost.Frame(); };
        Hooks.Faulted = (name, reason) => { ModRegistry.SetFault(name, reason); ModManager.QueueFault(name); };
        try
        {
            Game.Log($"StoneForge {LoaderVersion.Text} on .NET {Environment.Version}");
            LoaderOptions.Load();
            // (Which scripts mods may hook: those the patcher made hookable.)
            Hooks.LoadHookable(Path.Combine(Path.GetDirectoryName(typeof(Bridge).Assembly.Location)!, "stoneforge-hooks.txt"));
            // Main menu buttons (ours and mods'), the Mods window, mods' items, the Draw GUI pass.
            var loader = new ModContext(Hooks.LoaderId);
            MainMenu.Install(loader);
            EscMenu.Install(loader);
            GameDialogs.Install(loader);
            UIWindow.Install(loader);
            // (Mods' changed settings saved each frame.)
            loader.Frame += ModSettings.SaveChanged;
            // (The loader's own buttons - Mods - are the menu as it starts, as mods' made while they load are: a mod's
            // RestoreButtons keeps them.)
            MainMenu.BeginLoad();
            try { ModsMenu.Install(loader); }
            finally { MainMenu.EndLoad(); }
            Items.Install(loader);
            Consumables.Install(loader);
            Skills.Install(loader);
            GameObjects.Install(loader);
            ModNameTooltip.Install(loader);
            Items.ModsLoaded = () => ModManager.Startup.Finished;
            Buffs.Install(loader);
            loader.OnScript("scr_stonemod_draw_gui", _ => { Hooks.DrawGui(); return true; });
            // (And the HUD pass, under the game's windows: ModContext.DrawHud, ModUI.Hud.)
            loader.OnCode("gml_Object_o_stonemod_hud_Draw_0", after: (_, _) => Hooks.DrawHud());
            // The loading screen while the mods load (the game's own loading held till then), and StoneForge's
            // version on the main menu.
            LoadingScreen.Install(loader);
            loader.UI.Always.Add(new LoadingScreen());
            loader.UI.MainMenu.Add(new VersionLabel());
            // (Compiled in the background, loaded as each is ready - ModManager.Frame.)
            ModManager.BeginLoadAll();
            DevelopmentHost.Start();
            return 0;
        }
        catch (Exception e)
        {
            Game.Log("Loading mods failed: " + e);
            return 1;
        }
    }

    // <game>\mods: a folder per mod.
    internal static string ModsDir => ModManager.ModsDir;
}
