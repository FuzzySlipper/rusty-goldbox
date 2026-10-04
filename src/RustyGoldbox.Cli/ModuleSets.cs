using Rusty.Engine.Testing;
using RustyGoldbox.Core.Authoring;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Cli;

/// <summary>Loads module sets for commands, with the Engine content service for installed containers.</summary>
internal static class ModuleSets
{
    /// <param name="path">A module directory or an installed <c>.rpak</c>.</param>
    /// <param name="extensions">Extension module IDs to add to the set (<c>--extension</c>).</param>
    public static ModuleSet Load(string path, IReadOnlyList<string> searchDirectories, IReadOnlyList<string>? extensions = null)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine => ModuleLoader.Load(path, searchDirectories, engine.Content, extensions));
    }

    /// <summary>Builds an authoring workspace with the same Engine content service the CLI uses for module loads.</summary>
    public static WorkspaceBuildResult BuildWorkspace(Workspace workspace)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine => WorkspaceBuilder.Build(workspace, engine.Content));
    }

    /// <summary>The extension IDs given with <c>--extension</c>, which may repeat or list several: <c>--extension a,b</c>.</summary>
    public static IReadOnlyList<string> Extensions(Arguments parsed)
    {
        return parsed.All("--extension")
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
    }
}
