using Rusty.Engine.Testing;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Cli;

/// <summary>Loads module sets for commands, with the Engine content service for installed containers.</summary>
internal static class ModuleSets
{
    /// <param name="path">A module directory or an installed <c>.rpak</c>.</param>
    public static ModuleSet Load(string path, IReadOnlyList<string> searchDirectories)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine => ModuleLoader.Load(path, searchDirectories, engine.Content));
    }
}
