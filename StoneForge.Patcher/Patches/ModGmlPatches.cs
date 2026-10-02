namespace StoneForge.Patcher;

internal static class ModGmlPatches
{
    internal static void Apply(GameDataEditor editor, GmlCatalog catalog)
    {
        foreach (var project in catalog.Projects.Values)
        {
            if (project.Functions.Count > 0)
                PatcherConsole.Log($"WARNING: {project.Name} uses GML bindings and can bypass StoneForge's security. Its GML can't be hot-reloaded: changes need a restart of the game. Use it at your own discretion.");
            // Each function compiled once, after the functions it calls: the compiler resolves a call only to a
            // function it already has (a placeholder replaced later keeps the placeholder's locals and adds a
            // second, misnamed entry).
            foreach (var function in project.CompileOrder)
                try { editor.AddFunction(project.Rewrite(function), project.InternalName(function.Name)); }
                catch (Exception e) { throw new InvalidOperationException($"{project.Name}/{function.Path}: {e.Message}", e); }
        }
    }
}
