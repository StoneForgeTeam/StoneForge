using System.Runtime.InteropServices;
using StoneForge;
using StoneForge.Loader;

// The loader against a fake bridge: the native boundary, instance lifetimes, owned content and fault handling.
public class ReliabilityTests : FakeGame
{
    [Fact]
    public unsafe void Bridge_ABI_layout()
    {
        Assert.Equal(32, sizeof(NValue));
        Assert.Equal(112, sizeof(BridgeApi));
        Assert.Equal(40, sizeof(ManagedCallbacks));
        Assert.Equal(8, Marshal.OffsetOf<BridgeApi>(nameof(BridgeApi.Log)).ToInt32());
        // (Version 3 added ReleaseRefs at the end, after InstanceId.)
        Assert.Equal(80, Marshal.OffsetOf<BridgeApi>(nameof(BridgeApi.ReleaseRefs)).ToInt32());
        // (Version 4: GetVarAt and SetVarAt after it.)
        Assert.Equal(88, Marshal.OffsetOf<BridgeApi>(nameof(BridgeApi.GetVarAt)).ToInt32());
        Assert.Equal(96, Marshal.OffsetOf<BridgeApi>(nameof(BridgeApi.SetVarAt)).ToInt32());
        // (Version 5: InactiveInstances after them.)
        Assert.Equal(104, Marshal.OffsetOf<BridgeApi>(nameof(BridgeApi.InactiveInstances)).ToInt32());
    }

    [Fact]
    public unsafe void Legacy_and_mismatched_bridges_refuse_before_registration()
    {
        delegate* unmanaged<BridgeApi*, ManagedCallbacks*, int> old = &Bridge.Initialize;
        delegate* unmanaged<BridgeApi*, ManagedCallbacks*, int> init = &Bridge.InitializeV2;
        Assert.Equal(2, old(null, null));
        Assert.Equal(2, init(null, null));
        var bad = new BridgeApi { Size = 8, Version = -1 };
        var cb = new ManagedCallbacks { Size = sizeof(ManagedCallbacks), Version = 2 };
        Assert.Equal(2, init(&bad, &cb));
        Assert.True(cb.OnFrame == null);
    }

    [Fact]
    public void Native_failure_becomes_named_managed_error()
    {
        Fail = true;
        Assert.Throws<GameCallException>(() => Game.CallBuiltin("abs", 1));
    }

    [Fact]
    public async Task Off_thread_access_refused()
        => await Task.Run(() => Assert.Throws<InvalidOperationException>(() => Game.CallBuiltin("abs", 1)));

    [Fact]
    public void Stored_room_instance_uses_ID_after_callback()
    {
        Instance instance;
        using (new CallbackLifetime())
        {
            instance = new Instance((IntPtr)42);
            Assert.Equal(55, instance.Get("HP").AsReal);
            Assert.Equal(123, instance.Persist().Id);
        }
        int reads = Reads;
        Assert.Equal(55, instance.Get("HP").AsReal);
        Assert.Equal(reads, Reads);
        Assert.Contains("variable_instance_get", Calls);
        Alive = false;
        Assert.False(instance.Exists);
        Assert.True(instance.Get("HP").IsUndefined);
        Assert.False(instance.Set("HP", 1));
        Assert.Throws<InvalidOperationException>(() => Game.CallScript("test", instance));
    }

    [Fact]
    public void Expired_struct_cannot_be_dereferenced_or_passed_back()
    {
        Instance temporary;
        using (new CallbackLifetime()) temporary = new Instance((IntPtr)43);
        Assert.False(temporary.Exists);
        Assert.Throws<InvalidOperationException>(() => temporary.Get("anything"));
        Assert.Throws<InvalidOperationException>(() => Game.CallBuiltin("abs", temporary));
        Assert.Throws<InvalidOperationException>(() => temporary.Persist());
    }

    [Fact]
    public void Nested_callbacks_preserve_only_the_outer_lease()
    {
        using var outer = new CallbackLifetime();
        var a = new Instance((IntPtr)43);
        Instance b;
        using (new CallbackLifetime()) b = new Instance((IntPtr)43);
        Assert.True(a.Exists);
        Assert.False(b.Exists);
    }

    [Fact]
    public void Failed_ID_resolution_never_falls_back_to_global_scope()
    {
        int before = Calls.Count;
        Assert.Throws<GameCallException>(() => Game.CallBuiltinAs("abs", Instance.FromId(123), default, 1));
        Assert.DoesNotContain("abs", Calls.Skip(before));
    }

    [Fact]
    public void Sprites_retire_and_reuse_without_deleting_a_referenced_ID()
    {
        Adds = Replaces = 0;
        // (Its own calls: the log is every test's.)
        int start = Calls.Count;
        int first = ModContent.LoadSprite("test assets", "test.png", 1, 0, 0);
        Assert.Equal(first, ModContent.LoadSprite("test assets", "test.png", 1, 0, 0));
        Assert.Equal(1, Adds);
        ModContent.RemoveMod("test assets");
        Assert.Equal(0, ModContent.ActiveSprites);
        Assert.Equal(1, Replaces);
        Assert.Equal(first, ModContent.LoadSprite("test assets", "test.png", 1, 0, 0));
        Assert.Equal(1, Adds);
        Assert.Equal(2, Replaces);
        ReplaceFails = true;
        ModContent.RemoveMod("test assets");
        Assert.Equal(1, ModContent.ActiveSprites);
        ReplaceFails = false;
        ModContent.RemoveMod("test assets");
        Assert.Equal(0, ModContent.ActiveSprites);
        Assert.DoesNotContain("sprite_delete", Calls.Skip(start));
    }

    [Fact]
    public void Successful_callback_resets_consecutive_faults_and_other_mods_continue()
    {
        Hooks.FailureThreshold = 3;
        int faults = 0;
        Hooks.Faulted = (_, _) => faults++;
        bool Bad() => throw new InvalidOperationException("test failure");
        try
        {
            Hooks.Invoke("broken", "tick", Bad);
            Hooks.Invoke("broken", "tick", Bad);
            Hooks.Invoke("broken", "tick", () => true);
            Hooks.Invoke("broken", "tick", Bad);
            Assert.False(Hooks.IsSuspended("broken"));
            Hooks.Invoke("broken", "tick", Bad);
            Hooks.Invoke("broken", "tick", Bad);
            Assert.True(Hooks.IsSuspended("broken"));
            Assert.Equal(1, faults);
            Assert.False(Hooks.Invoke("broken", "tick", () => throw new Exception("must not run")));
            Assert.True(Hooks.Invoke("healthy", "tick", () => true));
            Hooks.ResetFault("broken");
            Assert.True(Hooks.Invoke("broken", "tick", () => true));
        }
        finally { Hooks.ResetFault("broken"); }
    }

    [Fact]
    public unsafe void Unmanaged_frame_boundary_contains_loader_exceptions()
    {
        Hooks.BeforeFrame = () => throw new InvalidOperationException("loader failure");
        try
        {
            delegate* unmanaged<void> frame = &Hooks.OnFrame;
            frame();
            Assert.Null(CallbackLifetime.Current);
        }
        finally { Hooks.BeforeFrame = null; }
    }

    [Fact]
    public void Healthy_sibling_handler_cannot_hide_repeated_faults()
    {
        object broken = new(), healthy = new();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                Hooks.Invoke("siblings", "Tick", () => throw new Exception("bad"), broken);
                Hooks.Invoke("siblings", "Tick", () => true, healthy);
            }
            Assert.True(Hooks.IsSuspended("siblings"));
        }
        finally { Hooks.ResetFault("siblings"); }
    }

    [Fact]
    public void Unloaded_consumables_remove_inventory_stacks_and_loot_and_reload_cancels_removal()
    {
        var context = new ModContext("cleanup_test");
        Game.Running = false;
        Consumables.Add(context, new CleanupTonic());
        // Model resolved game object IDs without loading actual game tables.
        var registry = (System.Collections.IDictionary)typeof(Consumables).GetField("ByKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        object entry = registry[context.GameKey("sf_cleanup_tonic")]!;
        entry.GetType().GetField("Object")!.SetValue(entry, 701);
        entry.GetType().GetField("LootObject")!.SetValue(entry, 702);
        ConsumableInstances = new() { [1001] = 701, [1002] = 701, [1003] = 702, [1004] = 703 };
        try
        {
            Game.Running = true;
            Consumables.RemoveMod(context.Name);
            Assert.Equal(4, ConsumableInstances.Count); // Deferred, outside unload/event execution.
            int start = Calls.Count;
            Consumables.RemoveOrphans();
            Assert.Single(ConsumableInstances);
            Assert.True(ConsumableInstances.ContainsKey(1004));
            Assert.Equal(2, Calls.Skip(start).Count(c => c == "script_execute"));
            Assert.Equal(1, Calls.Skip(start).Count(c => c == "instance_destroy"));
            ConsumableInstances[1005] = 701; // A stack restored later from a chest/save.
            Consumables.RemoveOrphans();
            Assert.False(ConsumableInstances.ContainsKey(1005));
            ConsumableInstances[1006] = 701;
            Game.Running = false;
            Consumables.Add(context, new CleanupTonic());
            Game.Running = true;
            Consumables.RemoveOrphans();
            Assert.True(ConsumableInstances.ContainsKey(1006));
        }
        finally { Consumables.RemoveMod(context.Name); Game.Running = true; }
    }

    [Fact]
    public void GML_gateway_refuses_disabled_stale_missing_and_paused_exports()
    {
        GmlScripts.Activate("GatewayMod", "gateway owner", "revision", new[] { "sf_gateway_fn" });
        try
        {
            int before = Calls.Count;
            GmlScripts.Call("GatewayMod", "revision", "sf_gateway_fn", 42);
            Assert.Contains("script_execute", Calls.Skip(before));
            Assert.Throws<InvalidOperationException>(() => GmlScripts.Call("GatewayMod", "old", "sf_gateway_fn"));
            Assert.Throws<InvalidOperationException>(() => GmlScripts.Call("GatewayMod", "revision", "not_exported"));
            // (Two mod classes from one folder share its GML: it stays callable till both are off.)
            GmlScripts.Activate("GatewayMod", "second owner", "revision", new[] { "sf_gateway_fn" });
            GmlScripts.RemoveMod("second owner");
            GmlScripts.Call("GatewayMod", "revision", "sf_gateway_fn");
            Assert.Throws<InvalidOperationException>(() => GmlScripts.Activate("GatewayMod", "third owner", "changed", new[] { "sf_gateway_fn" }));
            GmlScripts.RemoveMod("gateway owner");
            Assert.Throws<InvalidOperationException>(() => GmlScripts.Call("GatewayMod", "revision", "sf_gateway_fn"));
            GmlScripts.Activate("GatewayMod", "gateway owner", "revision", new[] { "sf_gateway_fn" });
            Hooks.FailureThreshold = 3;
            for (int i = 0; i < 3; i++) Hooks.Invoke("gateway owner", "failure", () => throw new Exception("test"));
            Assert.Throws<InvalidOperationException>(() => GmlScripts.Call("GatewayMod", "revision", "sf_gateway_fn"));
        }
        finally { GmlScripts.RemoveMod("gateway owner"); GmlScripts.RemoveMod("second owner"); Hooks.ResetFault("gateway owner"); }
    }

    private sealed class CleanupTonic : Consumable { public CleanupTonic() : base("sf_cleanup_tonic", "wine") {} }
}
