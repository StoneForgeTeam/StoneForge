namespace StoneForge.Patcher;

/// <summary>One run of the MSL helper: the runtime it uses and the packages it applies, in order.</summary>
internal sealed record SmlPass(SmlRuntimeSelection Runtime, List<SmlPackage> Packages);
