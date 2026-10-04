using RustyGoldbox.Core.Authoring;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Cli;

internal sealed record WorkspaceExport(ModuleManifest Module, string Container);

/// <summary><c>goldbox workspace</c>: create and discover an authoring workspace.</summary>
internal static class WorkspaceCommand
{
    public static int Run(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, [], []);
        if (error is null && parsed.Positionals.Count == 0)
        {
            error = "Usage: goldbox workspace new <dir> | inspect [<path>] | build [<path>] | export [<path>]";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        return parsed.Positionals[0] switch
        {
            "new" => New(parsed.Positionals.Skip(1), output, workingDirectory),
            "inspect" => Inspect(parsed.Positionals.Skip(1), output, workingDirectory),
            "build" => Build(parsed.Positionals.Skip(1), output, workingDirectory),
            "export" => Export(parsed.Positionals.Skip(1), output, workingDirectory),
            _ => output.UsageError($"Unknown workspace command '{parsed.Positionals[0]}'. Use new, inspect, build or export."),
        };
    }

    private static int New(IEnumerable<string> positionals, Output output, string workingDirectory)
    {
        string[] values = positionals.ToArray();
        if (values.Length != 1)
        {
            return output.UsageError("Usage: goldbox workspace new <dir>");
        }

        List<ModuleDiagnostic> diagnostics = [];
        Workspace? workspace = WorkspaceScaffold.Create(Path.GetFullPath(values[0], workingDirectory), diagnostics);
        return output.WorkspaceCreated(workspace, diagnostics);
    }

    private static int Inspect(IEnumerable<string> positionals, Output output, string workingDirectory)
    {
        string[] values = positionals.ToArray();
        if (values.Length > 1)
        {
            return output.UsageError("Usage: goldbox workspace inspect [<path>]");
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
            return output.UsageError("Usage: goldbox workspace build [<path>]");
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

    private static int Export(IEnumerable<string> positionals, Output output, string workingDirectory)
    {
        string[] values = positionals.ToArray();
        if (values.Length > 1)
        {
            return output.UsageError("Usage: goldbox workspace export [<path>]");
        }

        List<ModuleDiagnostic> diagnostics = [];
        string path = values.Length == 0 ? workingDirectory : Path.GetFullPath(values[0], workingDirectory);
        Workspace? workspace = Workspace.Find(path, diagnostics);
        if (workspace is null || diagnostics.Count > 0)
        {
            return output.WorkspaceExported(null, [], diagnostics);
        }

        WorkspaceBuildResult result = ModuleSets.BuildWorkspace(workspace);
        if (!result.IsValid)
        {
            return output.WorkspaceExported(result, [], diagnostics);
        }

        AuthoringWorkspace authoring = workspace.Authoring!;
        if (!PrepareExportRoot(authoring.ExportsDirectory, workspace.ManifestPath, diagnostics))
        {
            return output.WorkspaceExported(result, [], diagnostics);
        }

        List<WorkspaceExport> exports = [];
        foreach (WorkspaceBuiltModule module in result.Modules)
        {
            string target = Path.Combine(
                authoring.ExportsDirectory,
                InstalledModules.FileName(module.Manifest.Id, module.Manifest.Version));
            string? failure = ContentPacker.Pack(module.StagedDirectory, target);
            if (failure is not null)
            {
                diagnostics.Add(new ModuleDiagnostic(
                    "workspace.export.pack",
                    failure,
                    module.Manifest.Id,
                    target,
                    "$.authoring.exports"));
                continue;
            }

            exports.Add(new WorkspaceExport(module.Manifest, target));
        }

        return output.WorkspaceExported(result, exports, diagnostics);
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
