using RustyTales.Core.Authoring;
using RustyTales.Core.Modules;

namespace RustyTales.Cli;

internal sealed record WorkspaceExport(ModuleManifest Module, string Container);

/// <summary><c>tales workspace</c>: create and discover an authoring workspace.</summary>
internal static class WorkspaceCommand
{
    public static int Run(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--kit", "--id", "--title"], []);
        if (error is null && parsed.Positionals.Count == 0)
        {
            error = "Usage: tales workspace new <dir> [--kit <id>] [--id <campaign-id>] [--title <title>] | inspect [<path>] | build [<path>] | export [<path>] | install [<path>]";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        string command = parsed.Positionals[0];
        if (command != "new" && (parsed.Single("--kit") is not null || parsed.Single("--id") is not null || parsed.Single("--title") is not null))
        {
            return output.UsageError($"--kit, --id and --title only apply to `tales workspace new`.");
        }

        return command switch
        {
            "new" => New(parsed, output, workingDirectory),
            "inspect" => Inspect(parsed.Positionals.Skip(1), output, workingDirectory),
            "build" => Build(parsed.Positionals.Skip(1), output, workingDirectory),
            "export" => Export(parsed.Positionals.Skip(1), output, workingDirectory, install: false),
            "install" => Export(parsed.Positionals.Skip(1), output, workingDirectory, install: true),
            _ => output.UsageError($"Unknown workspace command '{command}'. Use new, inspect, build, export or install."),
        };
    }

    private static int New(Arguments parsed, Output output, string workingDirectory)
    {
        string[] values = parsed.Positionals.Skip(1).ToArray();
        if (values.Length != 1)
        {
            return output.UsageError("Usage: tales workspace new <dir> [--kit <id>] [--id <campaign-id>] [--title <title>]");
        }

        string? kitId = parsed.Single("--kit");
        string? campaignId = parsed.Single("--id");
        string? title = parsed.Single("--title");
        if (kitId is null && (campaignId is not null || title is not null))
        {
            return output.UsageError("--id and --title require --kit <id>. Run `tales schema kits` to list available genre kits.");
        }

        List<ModuleDiagnostic> diagnostics = [];
        Workspace? workspace = WorkspaceScaffold.Create(Path.GetFullPath(values[0], workingDirectory), diagnostics, kitId, campaignId, title);
        return output.WorkspaceCreated(workspace, diagnostics, kitId is null ? null : GenreKits.Find(kitId));
    }

    private static int Inspect(IEnumerable<string> positionals, Output output, string workingDirectory)
    {
        string[] values = positionals.ToArray();
        if (values.Length > 1)
        {
            return output.UsageError("Usage: tales workspace inspect [<path>]");
        }

        List<ModuleDiagnostic> diagnostics = [];
        string path = values.Length == 0 ? workingDirectory : Path.GetFullPath(values[0], workingDirectory);
        WorkspaceInspection? inspection = WorkspaceInspector.Read(path, diagnostics);
        return output.WorkspaceInspected(inspection, diagnostics);
    }

    private static int Build(IEnumerable<string> positionals, Output output, string workingDirectory)
    {
        string[] values = positionals.ToArray();
        if (values.Length > 1)
        {
            return output.UsageError("Usage: tales workspace build [<path>]");
        }

        List<ModuleDiagnostic> diagnostics = [];
        string path = values.Length == 0 ? workingDirectory : Path.GetFullPath(values[0], workingDirectory);
        Workspace? workspace = Workspace.Find(path, diagnostics);
        if (workspace is null || diagnostics.Count > 0)
        {
            return output.WorkspaceBuilt(null, diagnostics);
        }

        WorkspaceBuildResult result = ModuleSets.BuildWorkspace(workspace);
        return output.WorkspaceBuilt(result, diagnostics);
    }

    /// <summary>
    /// Builds the workspace and packs each staged module: into its exports
    /// directory, or with <paramref name="install"/> into the Game's module
    /// library, replacing the same versions installed before.
    /// </summary>
    private static int Export(IEnumerable<string> positionals, Output output, string workingDirectory, bool install)
    {
        string step = install ? "install" : "export";
        string[] values = positionals.ToArray();
        if (values.Length > 1)
        {
            return output.UsageError($"Usage: tales workspace {step} [<path>]");
        }

        List<ModuleDiagnostic> diagnostics = [];
        string path = values.Length == 0 ? workingDirectory : Path.GetFullPath(values[0], workingDirectory);
        Workspace? workspace = Workspace.Find(path, diagnostics);
        if (workspace is null || diagnostics.Count > 0)
        {
            return output.WorkspaceExported(null, [], diagnostics, step);
        }

        WorkspaceBuildResult result = ModuleSets.BuildWorkspace(workspace);
        if (!result.IsValid)
        {
            return output.WorkspaceExported(result, [], diagnostics, step);
        }

        AuthoringWorkspace authoring = workspace.Authoring!;
        string directory = install ? InstalledModules.DefaultDirectory() : authoring.ExportsDirectory;
        bool ready = install
            ? PrepareLibrary(directory, workspace.ManifestPath, diagnostics)
            : PrepareExportRoot(directory, workspace.ManifestPath, diagnostics);
        if (!ready)
        {
            return output.WorkspaceExported(result, [], diagnostics, step);
        }

        List<WorkspaceExport> exports = [];
        foreach (WorkspaceBuiltModule module in result.Modules)
        {
            string target = Path.Combine(
                directory,
                InstalledModules.FileName(module.Manifest.Id, module.Manifest.Version));
            string? failure = ContentPacker.Pack(module.StagedDirectory, target);
            if (failure is not null)
            {
                diagnostics.Add(new ModuleDiagnostic(
                    $"workspace.{step}.pack",
                    failure,
                    module.Manifest.Id,
                    target,
                    "$.authoring.exports"));
                continue;
            }

            exports.Add(new WorkspaceExport(module.Manifest, target));
        }

        return output.WorkspaceExported(result, exports, diagnostics, step);
    }

    private static bool PrepareLibrary(string library, string manifestPath, List<ModuleDiagnostic> diagnostics)
    {
        try
        {
            Directory.CreateDirectory(library);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "workspace.install.output",
                $"Can't create the module library {library}: {exception.Message} Fix its permissions, or set TALES_MODULE_LIBRARY to another directory.",
                File: manifestPath));
            return false;
        }
    }

    private static bool PrepareExportRoot(string exports, string manifestPath, List<ModuleDiagnostic> diagnostics)
    {
        try
        {
            if (File.Exists(exports))
            {
                diagnostics.Add(new ModuleDiagnostic(
                    "workspace.export.output",
                    $"The export output {exports} is a file. Move it or remove it so generated containers can be rebuilt.",
                    File: manifestPath,
                    JsonPath: "$.authoring.exports"));
                return false;
            }

            if (Directory.Exists(exports))
            {
                Directory.Delete(exports, recursive: true);
            }

            Directory.CreateDirectory(exports);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "workspace.export.output",
                $"Can't clean or create generated export directory {exports}: {exception.Message} Fix its permissions or choose another authoring.exports path.",
                File: manifestPath,
                JsonPath: "$.authoring.exports"));
            return false;
        }
    }
}
